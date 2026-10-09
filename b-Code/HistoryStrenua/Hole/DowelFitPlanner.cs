namespace HistoryStrenua;

/// <summary>相邻两个销孔之间要带 ±0.02 的一个尺寸。</summary>
/// <param name="Axis">方向。</param>
/// <param name="From">较小的那条中心线坐标（水平是 X、竖直是 Y，图纸坐标）。</param>
/// <param name="To">较大的那条。</param>
/// <param name="FromEdgeIndex">一头的销孔边下标（新加时选它）。</param>
/// <param name="ToEdgeIndex">另一头。</param>
/// <param name="Existing">已经有这段尺寸：它在已有尺寸表里的下标；没有为 null（要新加）。</param>
/// <param name="Shared">这段尺寸兼管别的孔（被去重并掉过别的孔的同段尺寸，或连着的不是销孔）：后缀写「(公差仅对销孔)」。</param>
/// <param name="TextAt">新加时文字放哪（图纸坐标）。</param>
internal sealed record DowelSpan(
    PositionAxis Axis, double From, double To, int FromEdgeIndex, int ToEdgeIndex, int? Existing, bool Shared, SheetPoint TextAt);

/// <summary>规划结果。</summary>
/// <param name="Kinds">销孔按种分好（每种一个孔标注带 H7）。</param>
/// <param name="Spans">同种相邻销孔之间的尺寸，先水平后竖直，同方向按种。</param>
/// <param name="DowelCount">销孔个数。</param>
internal sealed record DowelFitPlan(IReadOnlyList<IReadOnlyList<HoleEdge>> Kinds, IReadOnlyList<DowelSpan> Spans, int DowelCount);

/// <summary>
/// 「销孔标注」（1.8.0）的纯几何部分：哪些孔是销孔、相邻销孔之间要哪几段尺寸、已有尺寸里哪个就是这段、要不要写「(公差仅对销孔)」。
/// </summary>
/// <remarks>
/// <para>销孔沿用销钉符号的认法：异形孔向导的销钉孔（<see cref="HoleEdge.Dowel"/>），腰型孔不算。</para>
/// <para>销孔之间（用户定，只在销孔与销孔之间，销孔与别的孔、与基准之间不加）：同一种销孔（同一个异形孔向导特征、同孔径，
/// 与孔标注的分种相同）里，每个方向按中心线归成站，相邻两站一段。两个销孔在同一行就只有水平一段，同一列只有竖直一段，斜对角两段都有。
/// 不跨种：真机底板 16 个销孔分 3 种、分在几处，不分种串起来出了 545 mm 这种跨整块板的「销孔间」尺寸；分种后与孔位尺寸
/// 「同种接着前一个标」的链一致，普通模式下多半是已有的改。</para>
/// <para>已有的改、没有的补（用户定）：已有线性尺寸沿同一方向、两头落在这两条中心线上，就是这段——连的是孔边还是中心符号线、
/// 是不是连着这两个销孔都不论（孔位尺寸按中心线去重，同一段可能连在同列的别的孔上）。</para>
/// <para>「(公差仅对销孔)」（用户定）：这段尺寸和别的孔的同段尺寸重叠、被去重并成了一个，后缀写「(公差仅对销孔)」，说明 ±0.02 只管销孔。
/// 判法：普通模式下有别的种的孔也想标这一段（<see cref="HolePositionPlanner.RequestedSpans"/>），或者已有的这段尺寸
/// 连着的不是销孔。尺寸链模式孔之间没有线性尺寸，不会被去重，不写。</para>
/// </remarks>
internal static class DowelFitPlanner
{
    /// <summary>销孔之间的公差 ±0.02 mm（模型长度，米）。</summary>
    public const double PlusMinus = 2e-5;

    /// <summary>销孔大小的配合公差代号。</summary>
    public const string HoleFit = "H7";

    /// <summary>兼管别的孔时写在销孔间尺寸后面的字。</summary>
    public const string SharedSuffix = " (公差仅对销孔)";

    /// <summary>1.15.0 之前写的后缀（用户改成「(公差仅对销孔)」）：重跑时认得出、换成新写法。</summary>
    public const string LegacySharedSuffix = " (仅销孔)";

    /// <summary>新加的销孔间尺寸离孔边多远（图纸上 6 mm）。</summary>
    public const double SpanReach = 0.006;

    /// <param name="edges">视图里正对图纸的孔边。</param>
    /// <param name="existing">视图里已有的尺寸（<see cref="DimensionScan"/> 读出）。</param>
    /// <param name="scale">视图比例。</param>
    /// <param name="chainMode">页面「尺寸链」开关。</param>
    /// <param name="left">左侧基准 X（找不到时给孔的最小 X 也行，只影响「兼管」判定里的排序）。</param>
    /// <param name="top">上侧基准 Y。</param>
    public static DowelFitPlan Plan(
        IReadOnlyList<HoleEdge> edges, IReadOnlyList<ViewDimension> existing, double scale, bool chainMode, double left, double top)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(existing);
        var dowels = Dowels(edges);
        var kinds = HoleCalloutPlanner.GroupKinds(dowels).Select(kind => (IReadOnlyList<HoleEdge>)kind).ToList();
        var requested = chainMode
            ? new List<(PositionAxis Axis, double From, double To, bool Dowel)>()
            : HolePositionPlanner.RequestedSpans(edges, left, top, scale);

        var spans = new List<DowelSpan>();
        foreach (var (axis, kind) in new[] { PositionAxis.Horizontal, PositionAxis.Vertical }.SelectMany(axis => kinds.Select(kind => (axis, kind))))
        {
            var stops = Stops(kind, axis);
            for (var i = 1; i < stops.Count; i++)
            {
                var (a, b) = (stops[i - 1], stops[i]);
                var (from, to) = (Coordinate(a, axis), Coordinate(b, axis));
                var match = existing.FirstOrDefault(dimension => dimension.Linear
                    && DimensionGeometry.Measures(dimension, axis, scale)
                    && DimensionGeometry.Span(dimension, axis) is { } span
                    && DimensionGeometry.Same(span.From, from) && DimensionGeometry.Same(span.To, to));
                var shared = requested.Any(r => r.Axis == axis && !r.Dowel && DimensionGeometry.Same(r.From, from) && DimensionGeometry.Same(r.To, to))
                             || (match is not null && match.Anchors.Take(2).Any(anchor => !OnDowel(anchor, dowels)));
                spans.Add(new DowelSpan(axis, from, to, a.Index, b.Index, match?.Index, shared, TextAt(axis, a, b)));
            }
        }

        return new DowelFitPlan(kinds, spans, dowels.Count);
    }

    /// <summary>视图里的销孔（同心合并后，圆孔）。</summary>
    public static List<HoleEdge> Dowels(IReadOnlyList<HoleEdge> edges)
        => HoleCalloutPlanner.Recognize(edges).Where(hole => hole.Dowel && hole.Slot < 0).ToList();

    /// <summary>
    /// 一个方向上的站：同一条中心线上的销孔算一站，由小到大。每站挑离尺寸文字近的那个孔
    /// （水平尺寸放在上方，挑最上面的；竖直尺寸放在左侧，挑最左的）。
    /// </summary>
    private static List<HoleEdge> Stops(IReadOnlyList<HoleEdge> dowels, PositionAxis axis)
    {
        var stops = new List<HoleEdge>();
        foreach (var hole in dowels)
        {
            var index = stops.FindIndex(stop => DimensionGeometry.Same(Coordinate(stop, axis), Coordinate(hole, axis)));
            if (index < 0)
                stops.Add(hole);
            else if (axis == PositionAxis.Horizontal ? hole.Y > stops[index].Y : hole.X < stops[index].X)
                stops[index] = hole;
        }

        return stops.OrderBy(stop => Coordinate(stop, axis)).ToList();
    }

    private static double Coordinate(HoleEdge hole, PositionAxis axis) => axis == PositionAxis.Horizontal ? hole.X : hole.Y;

    /// <summary>尺寸这一头是不是落在销孔上：是销孔的孔边，或是穿过销孔心的线。</summary>
    private static bool OnDowel(DimensionAnchor anchor, IReadOnlyList<HoleEdge> dowels)
        => anchor.Segment is { } line
            ? dowels.Any(hole => HolePositionPlanner.PassesThrough(line, hole))
            : anchor is { Hole: true, X: { } x, Y: { } y } && dowels.Any(hole => HoleCalloutPlanner.SameCenter(new SheetPoint(x, y), new SheetPoint(hole.X, hole.Y)));

    /// <summary>新加的销孔间尺寸放在两孔旁边：水平的在较高那个孔上方，竖直的在较左那个孔左边，离孔边 <see cref="SpanReach"/>。</summary>
    private static SheetPoint TextAt(PositionAxis axis, HoleEdge a, HoleEdge b)
        => axis == PositionAxis.Horizontal
            ? new SheetPoint((a.X + b.X) / 2, Math.Max(a.Y + a.Radius, b.Y + b.Radius) + SpanReach)
            : new SheetPoint(Math.Min(a.X - a.Radius, b.X - b.Radius) - SpanReach, (a.Y + b.Y) / 2);

    /// <summary>
    /// 孔标注里哪个长度变量是孔径：长度等于孔径（模型长度，米；也认毫米）的那个，名字里带 diam 的优先。没有返回 -1。
    /// </summary>
    /// <param name="variables">每个长度变量的（名字, 长度）；不是长度变量的长度给 NaN。</param>
    /// <param name="diameter">孔径（模型长度，米）。</param>
    public static int DiameterVariable(IReadOnlyList<(string Name, double Length)> variables, double diameter)
    {
        bool Matches(double length) => Math.Abs(length - diameter) <= 1e-6 || Math.Abs(length - diameter * 1000) <= 1e-3;
        var candidates = variables.Select((variable, index) => (variable, index)).Where(item => Matches(item.variable.Length)).ToList();
        if (candidates.Count == 0)
            return -1;
        return candidates.FirstOrDefault(item => item.variable.Name.Contains("diam", StringComparison.OrdinalIgnoreCase), candidates[0]).index;
    }
}
