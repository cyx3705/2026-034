namespace HistoryStrenua;

/// <summary>要加的一个圆心位置尺寸：从基准边量到圆弧圆心。</summary>
/// <param name="Axis">方向：水平量圆心离左侧基准的 X，竖直量离上侧基准的 Y。</param>
/// <param name="ArcIndex">选哪段弧（<see cref="FilletArc.Index"/>，即 <see cref="ScannedView.ArcEdges"/> 的下标）；SolidWorks 选圆弧量到圆心。</param>
/// <param name="Coordinate">圆心沿这个方向的图纸坐标（米）。</param>
/// <param name="Value">离基准的距离（模型长度，米，取绝对值），用来核对建出来的值。</param>
/// <param name="TextAt">尺寸文字的位置（图纸坐标，米）。</param>
internal sealed record ArcCenterTarget(PositionAxis Axis, int ArcIndex, double Coordinate, double Value, SheetPoint TextAt);

/// <summary>规划结果。</summary>
/// <param name="Targets">要加的尺寸，先水平后竖直，同方向近的在里。</param>
/// <param name="CenterCount">认出的圆心（同圆心的几段弧、同心不同半径的弧都算一个）。</param>
/// <param name="Tangent">靠两端相切已经定位、不用标的圆心个数。</param>
/// <param name="OnHole">与孔同心、由孔位尺寸定位的圆心个数。</param>
/// <param name="Present">已经有尺寸、跳过的方向个数（另含落在基准、对称轴上不用标的方向）。</param>
internal sealed record ArcCenterPlan(IReadOnlyList<ArcCenterTarget> Targets, int CenterCount, int Tangent, int OnHole, int Present);

/// <summary>
/// 「圆心位置」（1.14.2，用户定）的纯几何部分：圆弧（圆角、凹弧、孔以外的整圆）的圆心要不要标、标哪个方向、放哪。不碰 SolidWorks。
/// </summary>
/// <remarks>
/// <para>圆弧标注（原「圆角标注」）事实上承担了孔以外所有圆的标注，R / Ø 之外圆心也要定位。用户定的规则看圆弧两端与直线相不相切：</para>
/// <list type="bullet">
/// <item>两端都与直线相切：圆心由两条切线和半径定死，不标；</item>
/// <item>两端都不相切（含没有端点的整圆）：标两个尺寸，水平、竖直各一，基准与孔位尺寸相同（左 / 上直边，<see cref="HolePositionPlanner.Datums"/>）；</item>
/// <item>只有一端相切：只标一个。切线与两个基准都不平行（斜的）时标哪个都行，取水平；切线是水平的（平行于上侧基准），
/// 以上侧基准量的竖直尺寸不能标（与切线重复约束），只能从左侧基准标水平尺寸；切线是竖直的反过来，只标竖直尺寸。</item>
/// </list>
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
    /// <param name="holes">认出来的孔（同心的弧不标）。</param>
    /// <param name="left">左侧基准边的 X。</param>
    /// <param name="top">上侧基准边的 Y。</param>
    /// <param name="scale">视图比例。</param>
    /// <param name="existing">视图里已有的尺寸（量到某个圆心的方向不重复标）。</param>
    /// <param name="firstHorizontal">水平尺寸第一层离上侧基准多远（<see cref="OutlinePlanner.FirstTier"/>）。</param>
    /// <param name="firstVertical">竖直尺寸第一层离左侧基准多远。</param>
    /// <param name="symmetry">视图的对称轴（圆心落在轴上的那个方向不标）；尺寸链模式不给。</param>
    public static ArcCenterPlan Plan(
        IReadOnlyList<FilletArc> arcs, IReadOnlyList<SheetSegment> lines, IReadOnlyList<HoleEdge> holes,
        double left, double top, double scale, IReadOnlyList<ViewDimension> existing,
        double firstHorizontal, double firstVertical, IReadOnlyList<SymmetryAxis>? symmetry = null)
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
        var pending = new List<(PositionAxis Axis, FilletArc Arc, double Offset)>();
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
            var arc = group.OrderByDescending(item => item.Sweep).First();
            foreach (var axis in needs)
            {
                var horizontal = axis == PositionAxis.Horizontal;
                var offset = horizontal ? center.X - left : top - center.Y;
                var coordinate = horizontal ? center.X : center.Y;
                if (Math.Abs(offset) <= HoleCalloutPlanner.CenterTolerance
                    || (symmetry ?? []).Any(item => item.Axis == axis && Math.Abs(item.At - coordinate) <= SymmetryPlanner.Tolerance)
                    || existing.Any(dimension => Locates(dimension, center, axis, scale)))
                {
                    present++;
                    continue;
                }

                pending.Add((axis, arc, offset));
            }
        }

        var targets = new List<ArcCenterTarget>();
        foreach (var axis in new[] { PositionAxis.Horizontal, PositionAxis.Vertical })
        {
            var tier = 0;
            foreach (var (_, arc, offset) in pending.Where(item => item.Axis == axis).OrderBy(item => Math.Abs(item.Offset)))
            {
                var textAt = axis == PositionAxis.Horizontal
                    ? new SheetPoint((left + arc.Center.X) / 2, top + firstHorizontal + tier * HolePositionPlanner.TierStep)
                    : new SheetPoint(left - firstVertical - tier * HolePositionPlanner.TierStep, (top + arc.Center.Y) / 2);
                var coordinate = axis == PositionAxis.Horizontal ? arc.Center.X : arc.Center.Y;
                targets.Add(new ArcCenterTarget(axis, arc.Index, coordinate, Math.Abs(offset) / scale, textAt));
                tier++;
            }
        }

        return new ArcCenterPlan(targets, groups.Count, tangent, onHole, present);
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

    /// <summary>已有的线性 / 坐标尺寸有一头连着这个圆心、量的是这个方向。</summary>
    private static bool Locates(ViewDimension dimension, SheetPoint center, PositionAxis axis, double scale)
        => (dimension.Linear || dimension.Ordinate)
           && dimension.Anchors.Any(anchor => anchor.Hole && anchor.X is { } x && anchor.Y is { } y && HoleCalloutPlanner.SameCenter(new SheetPoint(x, y), center))
           && DimensionGeometry.Measures(dimension, axis, scale);

    private static bool Near(SheetPoint point, double x, double y) => Math.Abs(point.X - x) <= EndTolerance && Math.Abs(point.Y - y) <= EndTolerance;
}
