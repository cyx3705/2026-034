namespace HistoryStrenua;

/// <summary>视图里一条候选孔边投影到图纸上的样子。坐标与半径都是图纸空间，单位米。</summary>
/// <param name="Index">回指调用方手里那条边。</param>
/// <param name="X">圆心 X。</param>
/// <param name="Y">圆心 Y。</param>
/// <param name="Radius">图纸上的半径（已乘视图比例）。</param>
internal readonly record struct HoleEdge(int Index, double X, double Y, double Radius);

/// <summary>图纸上的一个点（米）。</summary>
internal readonly record struct SheetPoint(double X, double Y);

/// <summary>要加的一个孔标注：用哪条边、标注文字放在哪。</summary>
internal readonly record struct CalloutTarget(int EdgeIndex, SheetPoint Center, SheetPoint Placement);

/// <summary>规划结果。</summary>
/// <param name="Targets">要加的标注，按图纸上从上到下、从左到右排。</param>
/// <param name="HoleCount">视图里认出的孔数（同心的算一个）。</param>
/// <param name="AlreadyAnnotated">其中已经有孔标注、本次跳过的孔数。</param>
internal sealed record HoleCalloutPlan(IReadOnlyList<CalloutTarget> Targets, int HoleCount, int AlreadyAnnotated);

/// <summary>
/// 孔标注的纯几何部分：哪些圆边算孔、同心的怎么合并、已经标过的怎么跳过、标注放在哪。
/// 不碰 SolidWorks，所以能离线测。
/// </summary>
internal static class HoleCalloutPlanner
{
    /// <summary>
    /// 两个圆心在图纸上相距不到 0.02 mm 就算同一个孔。
    /// 同一个孔在视图里常有好几条同心圆边：通孔的上下两个口、沉头孔的沉头与底孔、倒角的内外沿。
    /// </summary>
    public const double CenterTolerance = 2e-5;

    /// <summary>标注文字离孔边的距离（图纸上 5 mm）。</summary>
    public const double PlacementGap = 0.005;

    /// <summary>
    /// 孔的轴线在视图空间里与图纸法向的夹角余弦至少这么大，才算「正对着看」。
    /// 侧着看的孔投影成两条直线，那不是孔标注该标的地方。
    /// </summary>
    public const double FacingCosine = 0.999;

    /// <summary>圆柱面半径与圆边半径的相对容差。</summary>
    private const double RadiusTolerance = 1e-4;

    /// <summary>
    /// 孔的轴线（已变换到视图空间）是否正对图纸。长度不必归一。
    /// </summary>
    public static bool FacesViewer(double x, double y, double z)
    {
        var length = Math.Sqrt(x * x + y * y + z * z);
        return length > 0 && Math.Abs(z) / length >= FacingCosine;
    }

    /// <summary>
    /// 圆边旁边这张圆柱面是不是**孔**的内壁。
    /// </summary>
    /// <remarks>
    /// 解析圆柱面的曲面法向恒朝离开轴线的方向，而面的法向朝材料外。
    /// 孔壁的材料在外圈，面法向朝轴线，与曲面法向相反（<c>FaceInSurfaceSense</c> 为 false）；
    /// 凸台、轴的外圆面两者同向。半径也要对上——倒角、圆角旁边那张圆柱面不是这个圆的。
    /// </remarks>
    public static bool IsHoleWall(double circleRadius, double cylinderRadius, bool faceInSurfaceSense)
        => !faceInSurfaceSense
           && Math.Abs(circleRadius - cylinderRadius) <= Math.Max(1e-9, circleRadius * RadiusTolerance);

    /// <summary>
    /// 把候选边合并成孔，去掉已经有标注的，定下每个标注放在哪。
    /// </summary>
    /// <param name="edges">视图里正对图纸的孔边。</param>
    /// <param name="annotatedCenters">这个视图里已有孔标注所指的孔心。</param>
    public static HoleCalloutPlan Plan(IReadOnlyList<HoleEdge> edges, IReadOnlyList<SheetPoint> annotatedCenters)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(annotatedCenters);

        // 同心的归成一组，组里取最小的那条：通孔取孔径本身，沉头/锥孔取底孔。
        // 异形孔向导的孔标注从哪条边进去都读得到完整规格；手工切出来的孔只有取底孔才标的是孔径。
        var holes = new List<HoleEdge>();
        foreach (var edge in edges.OrderBy(edge => edge.Radius))
        {
            if (!holes.Any(hole => SameCenter(hole.X, hole.Y, edge.X, edge.Y)))
                holes.Add(edge);
        }

        var targets = new List<CalloutTarget>();
        var annotated = 0;
        foreach (var hole in holes
                     .OrderByDescending(hole => Math.Round(hole.Y / CenterTolerance))
                     .ThenBy(hole => hole.X))
        {
            if (annotatedCenters.Any(center => SameCenter(center.X, center.Y, hole.X, hole.Y)))
            {
                annotated++;
                continue;
            }

            targets.Add(new CalloutTarget(hole.Index, new SheetPoint(hole.X, hole.Y), Placement(hole)));
        }

        return new HoleCalloutPlan(targets, holes.Count, annotated);
    }

    /// <summary>标注放在孔的右上方 45°，离孔边 <see cref="PlacementGap"/>。</summary>
    public static SheetPoint Placement(HoleEdge hole)
    {
        var offset = (hole.Radius + PlacementGap) * Math.Sqrt(0.5);
        return new SheetPoint(hole.X + offset, hole.Y + offset);
    }

    private static bool SameCenter(double ax, double ay, double bx, double by)
    {
        var dx = ax - bx;
        var dy = ay - by;
        return dx * dx + dy * dy <= CenterTolerance * CenterTolerance;
    }
}
