using System.Text.RegularExpressions;

namespace HistoryStrenua;

/// <summary>参与重标判断的一个尺寸。</summary>
/// <param name="View">第几个 <see cref="CheckView"/>。</param>
/// <param name="Index"><see cref="ViewDimension.Index"/>。</param>
/// <param name="Value">尺寸值（模型，米）。</param>
/// <param name="Tolerance">带公差。</param>
internal sealed record RedundantMember(int View, int Index, double Value, bool Tolerance);

/// <summary>一个可算出的尺寸：<c>Target = Σ Sign × Term + Given</c>。</summary>
/// <param name="Target">建议删的尺寸。</param>
/// <param name="Terms">用哪些尺寸算出来，正负号。</param>
/// <param name="Given">相切、对称、阵列这些不用尺寸也定了的关系补上的值（模型，米）。</param>
internal sealed record RedundantFormula(RedundantMember Target, IReadOnlyList<(RedundantMember Term, int Sign)> Terms, double Given);

/// <summary>一组互相算得出的尺寸：<see cref="Members"/> 里删掉 <see cref="Surplus"/> 个仍然定得住。</summary>
/// <param name="Horizontal">在图纸上是水平方向量的（取第一个尺寸所在视图）。</param>
/// <param name="Members">这一组的全部尺寸。</param>
/// <param name="Surplus">多了几个。</param>
/// <param name="Suggested">算法建议删的，每个带算式；AI 或人可以换成组里别的。</param>
internal sealed record RedundantLoop(bool Horizontal, IReadOnlyList<RedundantMember> Members, int Surplus, IReadOnlyList<RedundantFormula> Suggested);

/// <summary>
/// 「重标尺寸」（1.29.0，用户定：尺寸能由别的尺寸算出就要删，由 AI 权衡删哪个）的算法。同一模型、同一方向的面是图的点，线性 / 坐标尺寸是边：
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>不用尺寸也定了的关系（腰型孔两端、对称轴、未注倒角、阵列跨度里等距的孔、没标 R 的相切）先把点并起来；与「未标尺寸」用同一张尺寸网，跨视图也算。</item>
/// <item>R、C 也是边（用户定：圆角倒角要纳入判断）：R 连圆心与切线（如 13 = 18 − R5），C 连斜边两头。</item>
/// <item>一条边的两头已经并成一个点：这个尺寸自己就是多余的。</item>
/// <item>其余找桥（Tarjan）：桥不在环上，删了就定不住，不是重标。去掉桥后每个连通块是一组，多余个数 = 边数 − 点数 + 1。</item>
/// <item>每组按保留优先级求最大生成树（公差 &gt; 阵列 &gt; 总尺寸 &gt; R、C &gt; 整数，同分留前面视图的），不在树上的就是建议删的，树上的路径就是它的算式。</item>
/// </list>
/// <para>孔径、角度、斜向距离不进图；R、C 只经相切、斜边进图。</para>
/// </remarks>
internal static partial class DimensionCheckPlanner
{
    private static readonly Regex ArrayPrefix = new(@"^\s*(\d+)\s*[x×]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ChamferPrefix = new(@"^\s*(?:\d+\s*[x×]\s*)?C\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private sealed record DimensionEdge(RedundantMember Member, ModelPlane From, ModelPlane To, bool Horizontal, bool Array, bool Size = false)
    {
        /// <summary>从 <see cref="From"/> 到 <see cref="To"/> 的带符号距离。</summary>
        public double Delta => To.Offset - From.Offset;
    }

    /// <param name="views">同页各视图（轴测图除外）。</param>
    /// <param name="chamfer">「未注倒角」的值；null 用默认。</param>
    public static List<RedundantLoop> Redundant(IReadOnlyList<CheckView> views, double? chamfer = null)
    {
        ArgumentNullException.ThrowIfNull(views);
        var result = new List<RedundantLoop>();
        foreach (var model in views.Select((view, index) => (view, index)).GroupBy(item => item.view.Model))
            result.AddRange(RedundantInModel(model.ToList(), chamfer ?? DefaultChamfer));
        return result;
    }

    private static List<RedundantLoop> RedundantInModel(List<(CheckView View, int Index)> views, double chamfer)
    {
        var edges = new List<DimensionEdge>();
        var given = new OutlineCoverage();
        ConnectGiven(given, views, tangents: false);
        var radii = new List<(RedundantMember Member, SheetPoint? Center)>();
        foreach (var (view, index) in views)
        {
            ConnectChamfers(given, view, [], chamfer);
            foreach (var dimension in view.Dimensions)
            {
                if (dimension.Type == RadialDimension && !double.IsNaN(dimension.Value))
                    radii.Add((new RedundantMember(index, dimension.Index, dimension.Value, dimension.ToleranceType != 0),
                        dimension.Anchors.FirstOrDefault(anchor => anchor.Hole) is { X: { } x, Y: { } y } ? new SheetPoint(x, y) : null));
                if (!(dimension.Linear || dimension.Ordinate))
                    continue;
                if (ChamferPrefix.IsMatch(dimension.Prefix))
                {
                    edges.AddRange(ChamferEdges(view, index, dimension));
                    continue;
                }

                if (Edge(view, index, dimension) is not { } edge)
                    continue;
                edges.Add(edge);
                ConnectArray(given, view, dimension, edge);
            }
        }

        // 相切：标了这个 R 就是一条边（同半径共用一个 R，同「未标尺寸」的认法），没标的算已定。
        foreach (var (view, viewIndex) in views)
        {
            foreach (var arc in view.Arcs)
            {
                // 优先标在这段弧上的那个 R，其次同视图的，算式才指得对。
                var radius = radii
                    .Where(item => Math.Abs(item.Member.Value - arc.ModelRadius) <= DimensionGeometry.ValueTolerance)
                    .OrderByDescending(item => item.Member.View == viewIndex && item.Center is { } center && HoleCalloutPlanner.SameCenter(center, arc.Center))
                    .ThenByDescending(item => item.Member.View == viewIndex)
                    .Select(item => item.Member)
                    .FirstOrDefault();
                foreach (var (center, line, axis) in TangentPlanes(view, arc))
                {
                    if (radius is not null)
                        edges.Add(new DimensionEdge(radius, center, line, axis == PositionAxis.Horizontal, false, Size: true));
                    else
                        given.Connect(center, line);
                }
            }
        }

        // 对称轴要两侧那一对已经定了才定得住轴：用含全部尺寸的网判，只把轴并进来，那一对本身不并（否则量那一对的尺寸成了自环）。
        var full = new OutlineCoverage();
        ConnectGiven(full, views);
        foreach (var edge in edges)
            full.Connect(edge.From, edge.To);
        foreach (var (view, _) in views)
            ConnectSymmetry(full, view, given);

        return Loops(edges, given);
    }

    /// <summary>「C」尺寸：每条斜边两头在两个方向上各一条边。</summary>
    private static IEnumerable<DimensionEdge> ChamferEdges(CheckView view, int index, ViewDimension dimension)
    {
        var member = new RedundantMember(index, dimension.Index, dimension.Value, dimension.ToleranceType != 0);
        foreach (var line in ChamferLines(view, dimension))
        {
            foreach (var axis in Axes)
            {
                var (a, b) = axis == PositionAxis.Horizontal ? (line.X1, line.X2) : (line.Y1, line.Y2);
                yield return new DimensionEdge(member, view.Frame.Plane(axis, Math.Min(a, b)), view.Frame.Plane(axis, Math.Max(a, b)), axis == PositionAxis.Horizontal, false, Size: true);
            }
        }
    }

    /// <summary>尺寸量的那一段：只连一条边的量它自身跨度；量不出的返回 null。45° 两向都对得上时只算水平。</summary>
    private static DimensionEdge? Edge(CheckView view, int index, ViewDimension dimension)
    {
        var member = new RedundantMember(index, dimension.Index, dimension.Value, dimension.ToleranceType != 0);
        var array = ArrayPrefix.IsMatch(dimension.Prefix);
        foreach (var axis in Axes)
        {
            (double From, double To)? span = null;
            if (dimension.Linear && dimension.Anchors is [{ Segment: { } line }])
            {
                var (a, b) = axis == PositionAxis.Horizontal ? (line.X1, line.X2) : (line.Y1, line.Y2);
                if (DimensionGeometry.Same(Math.Abs(a - b) / view.Frame.Scale, dimension.Value))
                    span = (Math.Min(a, b), Math.Max(a, b));
            }
            else if (dimension.Anchors.Count >= 2 && Measured(dimension, axis, view.Frame.Scale))
            {
                span = DimensionGeometry.Span(dimension, axis);
            }

            // 坐标尺寸的 0 点两头是同一条边，长度 0，不是一条边。
            if (span is { } found && !DimensionGeometry.Same(found.From, found.To))
                return new DimensionEdge(member, view.Frame.Plane(axis, found.From), view.Frame.Plane(axis, found.To), axis == PositionAxis.Horizontal, array);
        }

        return null;
    }

    /// <summary>阵列「N x 间距 =」：跨度里按等距落着的孔跟起点并在一起（不看别的东西：碰巧在跨度里的边不算定了）。</summary>
    private static void ConnectArray(OutlineCoverage given, CheckView view, ViewDimension dimension, DimensionEdge edge)
    {
        if (!edge.Array || !int.TryParse(ArrayPrefix.Match(dimension.Prefix).Groups[1].Value, out var count) || count < 2)
            return;
        var axis = edge.Horizontal ? PositionAxis.Horizontal : PositionAxis.Vertical;
        foreach (var hole in view.Holes)
        {
            var plane = view.Frame.Plane(axis, edge.Horizontal ? hole.X : hole.Y);
            var steps = (plane.Offset - edge.From.Offset) / (edge.Delta / count);
            if (steps > 0.5 && steps < count - 0.5 && Math.Abs(steps - Math.Round(steps)) * Math.Abs(edge.Delta / count) <= DimensionGeometry.ValueTolerance)
                given.Connect(plane, edge.From);
        }
    }
}
