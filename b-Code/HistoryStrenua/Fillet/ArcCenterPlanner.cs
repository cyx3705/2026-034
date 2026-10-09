namespace HistoryStrenua;

/// <summary>要加的一个圆心位置尺寸：从基准边量到圆弧圆心。</summary>
/// <param name="Axis">方向：水平量圆心离左侧基准的 X，竖直量离上侧基准的 Y。</param>
/// <param name="ArcIndex">选哪段弧（<see cref="FilletArc.Index"/>，即 <see cref="ScannedView.ArcEdges"/> 的下标）；SolidWorks 选圆弧量到圆心。</param>
/// <param name="Coordinate">圆心沿这个方向的图纸坐标（米）。</param>
/// <param name="Value">离基准的距离（模型长度，米，取绝对值），用来核对建出来的值。</param>
/// <param name="TextAt">尺寸文字的位置（图纸坐标，米）。</param>
internal sealed record ArcCenterTarget(PositionAxis Axis, int ArcIndex, double Coordinate, double Value, SheetPoint TextAt);

/// <summary>中心线一头连着的圆心：一段圆弧，或一个孔。</summary>
/// <param name="Center">圆心（图纸坐标）。</param>
/// <param name="ArcIndex">圆弧（<see cref="ScannedView.ArcEdges"/> 下标）；是孔时为 null。</param>
/// <param name="HoleIndex">孔边（<see cref="ScannedView.Edges"/> 下标）；是圆弧时为 null。</param>
internal readonly record struct CenterRef(SheetPoint Center, int? ArcIndex, int? HoleIndex);

/// <summary>
/// 要加的一根中心线（1.14.2 第二轮，用户定）：几个圆心在某个方向上坐标相同，只标一个尺寸，其余用中心线连上表示对齐。
/// </summary>
/// <param name="Axis">对齐的方向：<see cref="PositionAxis.Horizontal"/> 是 X 相同（竖线），<see cref="PositionAxis.Vertical"/> 是 Y 相同（横线）。</param>
/// <param name="First">线一头的圆心（沿线最靠前的）。</param>
/// <param name="Second">线另一头的圆心（沿线最靠后的）。</param>
/// <param name="Centers">线上要穿过的全部圆心（含两头）。</param>
internal sealed record ArcCenterLink(PositionAxis Axis, CenterRef First, CenterRef Second, IReadOnlyList<SheetPoint> Centers);

/// <summary>规划结果。</summary>
/// <param name="Targets">要加的尺寸，先水平后竖直，同方向近的在里。</param>
/// <param name="CenterCount">认出的圆心（同圆心的几段弧、同心不同半径的弧都算一个）。</param>
/// <param name="Tangent">靠两端相切已经定位、不用标的圆心个数。</param>
/// <param name="OnHole">与孔同心、由孔位尺寸定位的圆心个数。</param>
/// <param name="Present">已经有尺寸、跳过的方向个数（另含落在基准、对称轴上不用标的方向）。</param>
/// <param name="Links">要加的中心线（对齐的圆心，1.14.2 第二轮）。</param>
/// <param name="Aligned">因为与别的圆心（孔、已标的或这一轮标的圆弧圆心）对齐而不标的方向个数。</param>
/// <param name="LinkPresent">已经有线串着、不再加的中心线根数。</param>
internal sealed record ArcCenterPlan(
    IReadOnlyList<ArcCenterTarget> Targets, int CenterCount, int Tangent, int OnHole, int Present,
    IReadOnlyList<ArcCenterLink>? Links = null, int Aligned = 0, int LinkPresent = 0)
{
    /// <summary>要加的中心线（没有时为空表）。</summary>
    public IReadOnlyList<ArcCenterLink> CenterLinks => Links ?? [];
}

/// <summary>
/// 「圆心位置」（1.14.2，用户定）的纯几何部分：圆弧（圆角、凹弧、孔以外的整圆）的圆心要不要标、标哪个方向、放哪，
/// 对齐的圆心用哪根中心线连。不碰 SolidWorks。
/// </summary>
/// <remarks>
/// <para>圆弧标注（原「圆角标注」）事实上承担了孔以外所有圆的标注，R / Ø 之外圆心也要定位。用户定的规则看圆弧两端与直线相不相切：</para>
/// <list type="bullet">
/// <item>两端都与直线相切：圆心由两条切线和半径定死，不标；</item>
/// <item>两端都不相切（含没有端点的整圆）：标两个尺寸，水平、竖直各一，基准与孔位尺寸相同（左 / 上直边，<see cref="HolePositionPlanner.Datums"/>）；</item>
/// <item>只有一端相切：只标一个。切线与两个基准都不平行（斜的）时标哪个都行，取水平；切线是水平的（平行于上侧基准），
/// 以上侧基准量的竖直尺寸不能标（与切线重复约束），只能从左侧基准标水平尺寸；切线是竖直的反过来，只标竖直尺寸。</item>
/// </list>
/// <para><b>去重</b>（第二轮，用户定：圆心在某个方向上重叠的只标一个，用中心线连上）：某个方向要标的圆心，与孔、已有尺寸量过的圆心
/// 在这个方向上坐标相同，就不标，用一根中心线连到最近的那个；这一轮要标的几个圆弧圆心坐标相同，只标离尺寸近的那个
/// （水平尺寸在上方取最高的，竖直尺寸在左侧取最左的），其余与它连成一根中心线。已经有线（中心线、视图草图线）串着的不再加。</para>
/// <para>我补的口径：</para>
/// <list type="bullet">
/// <item>两端都相切、但两条切线平行（腰形外轮廓的端头，切上下两条水平边）：沿切线方向圆心仍不定，按「一端相切」处理，只标那一个。</item>
/// <item>同一圆心的几段弧（被别的特征切开的、同心不同半径的）合起来算一个圆心：它们的切线一起算约束，够两个不平行的方向就不标。</item>
/// <item>与孔同心（如耳板端头 R10 包着孔）：孔位尺寸已经定了圆心，不标。</item>
/// <item>圆心正落在基准边上、或落在视图对称轴（<see cref="SymmetryPlanner"/>）上的那个方向不标；已有尺寸量到这个圆心的方向不重复标。</item>
/// </list>
/// <para>只认与直线相切；与另一段圆弧相切的那一端当不相切（圆心靠另一段弧定位要看那段弧定没定，不追）。
/// 尺寸排在已有尺寸最外层之外，一个一层、近的在里（同外轮廓）。</para>
/// </remarks>
internal static class ArcCenterPlanner
{
    /// <summary>圆弧端点与直线端点算接上的距离（图纸 0.05 mm）。</summary>
    public const double EndTolerance = 5e-5;

    /// <summary>相切：直线方向与端点半径方向夹角的余弦不超过这个（约 1°）。</summary>
    public const double TangentTolerance = 0.02;

    /// <summary>两条切线算平行（单位向量叉积）、算水平 / 竖直（斜率）的容差。</summary>
    private const double ParallelTolerance = 1e-3;

    /// <param name="arcs">视图里的圆弧（孔已减掉，<see cref="FilletPlanner.WithoutCircles"/>）。</param>
    /// <param name="lines">视图里的直边。</param>
    /// <param name="holes">认出来的孔（同心的弧不标；对齐的圆心可以连到孔上）。</param>
    /// <param name="left">左侧基准边的 X。</param>
    /// <param name="top">上侧基准边的 Y。</param>
    /// <param name="scale">视图比例。</param>
    /// <param name="existing">视图里已有的尺寸（量到某个圆心的方向不重复标）。</param>
    /// <param name="firstHorizontal">水平尺寸第一层离上侧基准多远（<see cref="OutlinePlanner.FirstTier"/>）。</param>
    /// <param name="firstVertical">竖直尺寸第一层离左侧基准多远。</param>
    /// <param name="symmetry">视图的对称轴（圆心落在轴上的那个方向不标）；尺寸链模式不给。</param>
    /// <param name="drawn">视图里已有的中心线、草图线（图纸坐标）：已经把对齐的圆心串起来的不再加中心线。</param>
    public static ArcCenterPlan Plan(
        IReadOnlyList<FilletArc> arcs, IReadOnlyList<SheetSegment> lines, IReadOnlyList<HoleEdge> holes,
        double left, double top, double scale, IReadOnlyList<ViewDimension> existing,
        double firstHorizontal, double firstVertical, IReadOnlyList<SymmetryAxis>? symmetry = null,
        IReadOnlyList<SheetSegment>? drawn = null)
    {
        ArgumentNullException.ThrowIfNull(arcs);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(holes);
        ArgumentNullException.ThrowIfNull(existing);
        if (scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale));

        var groups = new List<List<FilletArc>>();
        foreach (var arc in arcs)
        {
            var group = groups.FirstOrDefault(known => HoleCalloutPlanner.SameCenter(known[0].Center, arc.Center));
            if (group is null)
                groups.Add([arc]);
            else
                group.Add(arc);
        }

        var tangent = 0;
        var onHole = 0;
        var present = 0;
        // 每个方向：要标的圆心，与已经定了位置、可以拿来对齐的圆心（孔、已有尺寸量过的圆弧圆心）。
        var needers = new Dictionary<PositionAxis, List<CenterRef>>();
        var located = new Dictionary<PositionAxis, List<CenterRef>>();
        foreach (var axis in new[] { PositionAxis.Horizontal, PositionAxis.Vertical })
        {
            needers[axis] = [];
            located[axis] = holes.Select(hole => new CenterRef(new SheetPoint(hole.X, hole.Y), null, hole.Index)).ToList();
        }

        foreach (var group in groups)
        {
            var center = group[0].Center;
            if (holes.Any(hole => HoleCalloutPlanner.SameCenter(new SheetPoint(hole.X, hole.Y), center)))
            {
                onHole++;
                continue;
            }

            var needs = Needs(group.SelectMany(arc => Tangents(arc, lines)).ToList());
            if (needs.Count == 0)
            {
                tangent++;
                continue;
            }

            // 选弧挑最长的那段（选得准、引出线短）。
            var reference = new CenterRef(center, group.OrderByDescending(item => item.Sweep).First().Index, null);
            foreach (var axis in needs)
            {
                var coordinate = Along(axis, center);
                if (Math.Abs(Offset(axis, center, left, top)) <= HoleCalloutPlanner.CenterTolerance
                    || (symmetry ?? []).Any(item => item.Axis == axis && Math.Abs(item.At - coordinate) <= SymmetryPlanner.Tolerance))
                {
                    present++;
                    continue;
                }

                if (existing.Any(dimension => Locates(dimension, center, axis, scale)))
                {
                    present++;
                    located[axis].Add(reference);
                    continue;
                }

                needers[axis].Add(reference);
            }
        }

        var pending = new List<(PositionAxis Axis, CenterRef Center)>();
        var links = new List<ArcCenterLink>();
        var aligned = 0;
        var linkPresent = 0;
        foreach (var axis in new[] { PositionAxis.Horizontal, PositionAxis.Vertical })
        {
            var remaining = needers[axis].ToList();
            while (remaining.Count > 0)
            {
                var at = Along(axis, remaining[0].Center);
                var same = remaining.Where(item => DimensionGeometry.Same(Along(axis, item.Center), at)).ToList();
                remaining.RemoveAll(same.Contains);
                var anchors = located[axis].Where(item => DimensionGeometry.Same(Along(axis, item.Center), at)).ToList();
                List<CenterRef> members;
                if (anchors.Count > 0)
                {
                    // 与孔 / 已标的圆心对齐：一个都不标，连到离它们最近的那个。
                    var nearest = anchors.OrderBy(anchor => same.Min(item => Math.Abs(Across(axis, item.Center) - Across(axis, anchor.Center)))).First();
                    aligned += same.Count;
                    members = [.. same, nearest];
                }
                else
                {
                    // 这一轮几个圆弧圆心对齐：标离尺寸近的那个，其余连上它。
                    var representative = axis == PositionAxis.Horizontal
                        ? same.OrderByDescending(item => item.Center.Y).First()
                        : same.OrderBy(item => item.Center.X).First();
                    pending.Add((axis, representative));
                    aligned += same.Count - 1;
                    members = same;
                }

                if (members.Count < 2)
                    continue;
                var ordered = members.OrderBy(item => Across(axis, item.Center)).ToList();
                var link = new ArcCenterLink(axis, ordered[0], ordered[^1], ordered.Select(item => item.Center).ToList());
                if ((drawn ?? []).Any(line => Covers(line, link)))
                    linkPresent++;
                else
                    links.Add(link);
            }
        }

        var targets = new List<ArcCenterTarget>();
        foreach (var axis in new[] { PositionAxis.Horizontal, PositionAxis.Vertical })
        {
            var tier = 0;
            foreach (var (_, reference) in pending.Where(item => item.Axis == axis).OrderBy(item => Math.Abs(Offset(axis, item.Center.Center, left, top))))
            {
                var center = reference.Center;
                var textAt = axis == PositionAxis.Horizontal
                    ? new SheetPoint((left + center.X) / 2, top + firstHorizontal + tier * HolePositionPlanner.TierStep)
                    : new SheetPoint(left - firstVertical - tier * HolePositionPlanner.TierStep, (top + center.Y) / 2);
                targets.Add(new ArcCenterTarget(axis, reference.ArcIndex!.Value, Along(axis, center),
                    Math.Abs(Offset(axis, center, left, top)) / scale, textAt));
                tier++;
            }
        }

        return new ArcCenterPlan(targets, groups.Count, tangent, onHole, present, links, aligned, linkPresent);
    }

    /// <summary>
    /// 要定位的圆弧圆心（1.14.2，判对称轴要不要加用，<see cref="SymmetryPlanner.Axes"/>）：同圆心的弧合成一个，与孔同心的、两端切线定死的不算。
    /// </summary>
    public static List<SheetPoint> Locatable(IReadOnlyList<FilletArc> arcs, IReadOnlyList<SheetSegment> lines, IReadOnlyList<HoleEdge> holes)
    {
        var centers = new List<SheetPoint>();
        foreach (var arc in arcs)
        {
            if (centers.Any(center => HoleCalloutPlanner.SameCenter(center, arc.Center))
                || holes.Any(hole => HoleCalloutPlanner.SameCenter(new SheetPoint(hole.X, hole.Y), arc.Center)))
                continue;
            var group = arcs.Where(other => HoleCalloutPlanner.SameCenter(other.Center, arc.Center));
            if (Needs(group.SelectMany(other => Tangents(other, lines)).ToList()).Count > 0)
                centers.Add(arc.Center);
        }

        return centers;
    }

    /// <summary>
    /// 已有的线把这根中心线要串的圆心都串上了：线沿对齐方向（X 相同是竖线、Y 相同是横线），坐标对得上，两头盖过最远的两个圆心
    /// （留 <see cref="HolePositionPlanner.CenterGap"/> 的余量，中心线、中心符号线在圆心处常断开一点）。
    /// </summary>
    public static bool Covers(SheetSegment line, ArcCenterLink link)
    {
        var axis = link.Axis;
        var at = Along(axis, link.First.Center);
        if (!DimensionGeometry.Same(Along(axis, new SheetPoint(line.X1, line.Y1)), at) || !DimensionGeometry.Same(Along(axis, new SheetPoint(line.X2, line.Y2)), at))
            return false;
        var (a, b) = (Across(axis, new SheetPoint(line.X1, line.Y1)), Across(axis, new SheetPoint(line.X2, line.Y2)));
        var (first, last) = (Across(axis, link.First.Center), Across(axis, link.Second.Center));
        return Math.Min(a, b) <= Math.Min(first, last) + HolePositionPlanner.CenterGap
               && Math.Max(a, b) >= Math.Max(first, last) - HolePositionPlanner.CenterGap;
    }

    /// <summary>
    /// 一段弧两端相切的直线方向（单位向量，图纸）：端点与直线的一个端点接上、直线方向与端点的半径方向垂直。没有端点的整圆没有。
    /// </summary>
    public static List<(double X, double Y)> Tangents(FilletArc arc, IReadOnlyList<SheetSegment> lines)
    {
        var result = new List<(double, double)>();
        foreach (var end in new[] { arc.Start, arc.End })
        {
            if (end is not { } point)
                continue;
            var (rx, ry) = (point.X - arc.Center.X, point.Y - arc.Center.Y);
            var radius = Math.Sqrt(rx * rx + ry * ry);
            if (radius <= 0)
                continue;
            foreach (var line in lines)
            {
                var (dx, dy) = (line.X2 - line.X1, line.Y2 - line.Y1);
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length <= EndTolerance || !(Near(point, line.X1, line.Y1) || Near(point, line.X2, line.Y2)))
                    continue;
                if (Math.Abs((dx * rx + dy * ry) / (length * radius)) <= TangentTolerance)
                {
                    result.Add((dx / length, dy / length));
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 切线方向 → 还要标哪几个方向：没有切线两个都标；有两个不平行的切线方向一个都不标；
    /// 只有一个方向（一端相切，或两端切的线平行）时，水平切线只标水平尺寸、竖直切线只标竖直尺寸、斜切线取水平尺寸。
    /// </summary>
    public static List<PositionAxis> Needs(IReadOnlyList<(double X, double Y)> tangents)
    {
        if (tangents.Count == 0)
            return [PositionAxis.Horizontal, PositionAxis.Vertical];
        var first = tangents[0];
        if (tangents.Any(other => Math.Abs(first.X * other.Y - first.Y * other.X) > ParallelTolerance))
            return [];
        return Math.Abs(first.X) < ParallelTolerance ? [PositionAxis.Vertical] : [PositionAxis.Horizontal];
    }

    /// <summary>沿尺寸方向的坐标：水平尺寸量 X，竖直尺寸量 Y。</summary>
    private static double Along(PositionAxis axis, SheetPoint point) => axis == PositionAxis.Horizontal ? point.X : point.Y;

    /// <summary>沿中心线的坐标：X 相同的圆心连竖线、按 Y 排；Y 相同的连横线、按 X 排。</summary>
    private static double Across(PositionAxis axis, SheetPoint point) => axis == PositionAxis.Horizontal ? point.Y : point.X;

    /// <summary>离基准的图纸距离：水平从左侧基准往右，竖直从上侧基准往下。</summary>
    private static double Offset(PositionAxis axis, SheetPoint point, double left, double top)
        => axis == PositionAxis.Horizontal ? point.X - left : top - point.Y;

    /// <summary>已有的线性 / 坐标尺寸有一头连着这个圆心、量的是这个方向。</summary>
    private static bool Locates(ViewDimension dimension, SheetPoint center, PositionAxis axis, double scale)
        => (dimension.Linear || dimension.Ordinate)
           && dimension.Anchors.Any(anchor => anchor.Hole && anchor.X is { } x && anchor.Y is { } y && HoleCalloutPlanner.SameCenter(new SheetPoint(x, y), center))
           && DimensionGeometry.Measures(dimension, axis, scale);

    private static bool Near(SheetPoint point, double x, double y) => Math.Abs(point.X - x) <= EndTolerance && Math.Abs(point.Y - y) <= EndTolerance;
}
