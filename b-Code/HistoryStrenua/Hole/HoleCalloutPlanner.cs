namespace HistoryStrenua;

/// <summary>视图里一条候选孔边投影到图纸上的样子。坐标与半径都是图纸空间，单位米。</summary>
/// <param name="Index">回指调用方手里那条边。</param>
/// <param name="X">圆心 X。</param>
/// <param name="Y">圆心 Y。</param>
/// <param name="Radius">图纸上的半径（已乘视图比例）。</param>
/// <param name="Kind">
/// 孔的「种」：孔壁所属的组件与特征。同一种里同孔径的孔只标一次——SolidWorks 的孔标注
/// 会自己在前面写上「N×」，每个都标就是把同一行字重复 N 遍。
/// </param>
/// <param name="BulgeX">腰型孔端头半圆朝哪边鼓（图纸上的单位向量 X）；整圈的圆孔为 0。</param>
/// <param name="BulgeY">同上，Y。</param>
/// <param name="Slot">
/// 腰型孔编号（1.3.0 起）：配成一对的两个端头编号相同；圆孔为 -1。由 <see cref="SlotPlanner.Pair"/> 填。
/// </param>
/// <param name="Dowel">孔壁是异形孔向导的销钉孔（1.6.0 起，判法见 <c>HoleScan.ViewGeometry</c>）。</param>
/// <param name="Axis">孔在零件里的轴线（1.15.0，跨视图认同一个孔用，<see cref="HoleCoveragePlanner"/>）；读不到为 null。</param>
internal readonly record struct HoleEdge(
    int Index, double X, double Y, double Radius, string Kind = "", double BulgeX = 0, double BulgeY = 0, int Slot = -1, bool Dowel = false,
    HoleAxis? Axis = null)
{
    /// <summary>是腰型孔端头的半圆（不是整圈的圆孔）。</summary>
    public bool IsSlotEnd => BulgeX != 0 || BulgeY != 0;
}

/// <summary>图纸上的一个点（米）。</summary>
internal readonly record struct SheetPoint(double X, double Y);

/// <summary>要加的一个孔标注：用哪条边、引线折点（文字下划线的右端）落在哪。</summary>
internal readonly record struct CalloutTarget(int EdgeIndex, SheetPoint Center, SheetPoint Placement);

/// <summary>图纸上的一条线段（米）。</summary>
internal readonly record struct SheetSegment(double X1, double Y1, double X2, double Y2);

/// <summary>规划结果。</summary>
/// <param name="Targets">要加的标注，每种孔一个，按图纸上从上到下、从左到右排。</param>
/// <param name="HoleCount">视图里认出的孔数（同心的算一个，一个腰型孔算一个）。</param>
/// <param name="KindCount">孔的种数。</param>
/// <param name="AlreadyAnnotated">已经有孔标注、本次跳过的种数。</param>
/// <param name="SlotCount">其中腰型孔的个数。</param>
/// <param name="Elsewhere">别的视图已标过（或留给看得见沉孔的视图标）、本次跳过的种（1.15.0），每种记一个视图名。</param>
internal sealed record HoleCalloutPlan(
    IReadOnlyList<CalloutTarget> Targets, int HoleCount, int KindCount, int AlreadyAnnotated, int SlotCount = 0,
    IReadOnlyList<(CalloutPlace Place, string View)>? Elsewhere = null)
{
    /// <summary>回执里的「N 个孔（含 M 个腰型孔）共 K 种」。</summary>
    public string Summary => HoleCalloutPlanner.Summary(HoleCount, SlotCount, KindCount);
}

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

    /// <summary>引线折点离孔心的距离比孔半径多出的部分（图纸上 12 mm）。</summary>
    public const double LeaderReach = 0.012;

    /// <summary>引线折点对准时允许的偏差（图纸上 0.1 mm）。</summary>
    public const double AlignTolerance = 1e-4;

    /// <summary>
    /// 孔的轴线在视图空间里与图纸法向的夹角余弦至少这么大，才算「正对着看」。
    /// 侧着看的孔投影成两条直线，那不是孔标注该标的地方。
    /// </summary>
    public const double FacingCosine = 0.999;

    /// <summary>圆柱面半径与圆边半径的相对容差。</summary>
    internal const double RadiusTolerance = 1e-4;

    /// <summary>
    /// 孔的轴线（已变换到视图空间）是否正对图纸。长度不必归一。
    /// </summary>
    public static bool FacesViewer(double x, double y, double z)
    {
        var length = Math.Sqrt(x * x + y * y + z * z);
        return length > 0 && Math.Abs(z) / length >= FacingCosine;
    }

    /// <summary>
    /// 圆边旁边这张圆柱面是不是**孔**的内壁：面法向（朝材料外）指向轴线。
    /// </summary>
    /// <param name="circleRadius">圆边半径。</param>
    /// <param name="cylinderRadius">圆柱面半径。半径要对上——倒角、圆角旁边那张圆柱面不是这个圆的。</param>
    /// <param name="faceInSurfaceSense"><c>IFace2.FaceInSurfaceSense</c>。</param>
    /// <param name="surfaceNormalDotRadial">
    /// 圆柱面上一点的**曲面**法向与「轴线→该点」径向的点积（<c>ISurface.EvaluateAtPoint</c> 前三位）。
    /// </param>
    /// <remarks>
    /// <para>
    /// <b><c>FaceInSurfaceSense</c> 为 true 表示面法向与曲面法向相反</b>，与 API 文档的字面相反。
    /// 2026-09-29 在 SolidWorks 2025 SP5 上标定：零件最高平面（外法向必为 +Z）曲面法向 +Z、该值 false；
    /// 最低平面曲面法向同为 +Z、该值 true。1.0.0 按字面理解写反，真实工程图上一个孔都认不出。
    /// </para>
    /// <para>
    /// 也不假定圆柱曲面法向一定背离轴线（实测是背离的），而是现场取点求值后再换算，
    /// 两个约定只依赖一个，且那一个有标定证据。
    /// </para>
    /// </remarks>
    public static bool IsHoleWall(
        double circleRadius,
        double cylinderRadius,
        bool faceInSurfaceSense,
        double surfaceNormalDotRadial)
    {
        if (Math.Abs(circleRadius - cylinderRadius) > Math.Max(1e-9, circleRadius * RadiusTolerance))
            return false;
        var faceNormalDotRadial = faceInSurfaceSense ? -surfaceNormalDotRadial : surfaceNormalDotRadial;
        return faceNormalDotRadial < 0;
    }

    /// <summary>与 <paramref name="x"/>,<paramref name="y"/>,<paramref name="z"/> 垂直的单位向量，用来在圆上取点。</summary>
    public static (double X, double Y, double Z) Perpendicular(double x, double y, double z)
    {
        // 取一个与轴线不接近平行的辅助方向做叉积。
        var (ex, ey, ez) = Math.Abs(x) < 0.9 ? (1.0, 0.0, 0.0) : (0.0, 1.0, 0.0);
        var cx = y * ez - z * ey;
        var cy = z * ex - x * ez;
        var cz = x * ey - y * ex;
        var length = Math.Sqrt(cx * cx + cy * cy + cz * cz);
        return (cx / length, cy / length, cz / length);
    }

    /// <summary>
    /// 把候选边合并成孔、把孔归成种，每种挑一个孔标注，已经标过的种跳过，定下标注放在哪。
    /// </summary>
    /// <param name="edges">视图里正对图纸的孔边（含腰型孔端头的半圆）。</param>
    /// <param name="annotatedCenters">这个视图里已有孔标注所指的孔心。</param>
    /// <param name="place">
    /// 一种孔该不该标在这个视图（1.15.0，<see cref="HoleCoveragePlanner.Callout"/>）：给出 <see cref="CalloutPlace.Elsewhere"/> /
    /// <see cref="CalloutPlace.Deferred"/> 的种跳过。null 时都标在这里。
    /// </param>
    /// <remarks>
    /// 腰型孔两端都留在种里：已有标注挂在哪一端都算标过；新标注标在这一种最靠左上的那一端上
    /// （SolidWorks 的孔标注从腰型孔任一端的圆弧进去都是整个腰型孔的规格）。
    /// </remarks>
    public static HoleCalloutPlan Plan(
        IReadOnlyList<HoleEdge> edges, IReadOnlyList<SheetPoint> annotatedCenters, Func<IReadOnlyList<HoleEdge>, (CalloutPlace Place, string? View)>? place = null)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(annotatedCenters);

        var holes = Recognize(edges);
        var kinds = GroupKinds(holes);

        var targets = new List<(HoleEdge Hole, CalloutTarget Target)>();
        var annotated = 0;
        var elsewhere = new List<(CalloutPlace, string)>();
        foreach (var kind in kinds)
        {
            // 这一种里任何一个孔已经有标注，整种都算标过：那个标注上的「N×」已经把其余的数进去了。
            if (kind.Any(hole => annotatedCenters.Any(center => SameCenter(center.X, center.Y, hole.X, hole.Y))))
            {
                annotated++;
                continue;
            }

            if (place?.Invoke(kind) is { Place: CalloutPlace.Elsewhere or CalloutPlace.Deferred } skip)
            {
                elsewhere.Add((skip.Place, skip.View ?? string.Empty));
                continue;
            }

            // 标在这一种里最靠左上的那个孔上，读图时从左上开始找得到。
            var first = kind[0];
            targets.Add((first, new CalloutTarget(first.Index, new SheetPoint(first.X, first.Y), Placement(first))));
        }

        var (holeCount, slotCount) = Count(holes);
        return new HoleCalloutPlan(
            targets
                .OrderByDescending(item => Math.Round(item.Hole.Y / CenterTolerance))
                .ThenBy(item => item.Hole.X)
                .Select(item => item.Target)
                .ToList(),
            holeCount,
            kinds.Count,
            annotated,
            slotCount,
            elsewhere);
    }

    /// <summary>
    /// 候选边 → 孔：同心合并（<see cref="MergeConcentric"/>），再把腰型孔两端配成对（<see cref="SlotPlanner.Pair"/>）。
    /// 腰型孔的两个端头各是一个元素，编号相同；配不上的半圆丢掉。三条孔类指令都从这里取孔。
    /// </summary>
    public static List<HoleEdge> Recognize(IReadOnlyList<HoleEdge> edges)
        => SlotPlanner.Pair(MergeConcentric(edges));

    /// <summary>
    /// 每个腰型孔只留一端：最上面的那一端（一样高取左边的）。孔位尺寸只标这一端的圆心。圆孔原样保留。
    /// </summary>
    public static List<HoleEdge> Representatives(IReadOnlyList<HoleEdge> holes)
        => holes
            .Where(hole => hole.Slot < 0)
            .Concat(holes.Where(hole => hole.Slot >= 0).GroupBy(hole => hole.Slot).Select(slot => ReadingOrder(slot).First()))
            .ToList();

    /// <summary>孔数（一个腰型孔算一个）与其中的腰型孔数。</summary>
    public static (int Holes, int Slots) Count(IReadOnlyList<HoleEdge> holes)
    {
        var slots = holes.Where(hole => hole.Slot >= 0).Select(hole => hole.Slot).Distinct().Count();
        return (holes.Count(hole => hole.Slot < 0) + slots, slots);
    }

    /// <summary>回执里的「12 个孔共 3 种」「14 个孔（含 2 个腰型孔）共 4 种」。</summary>
    public static string Summary(int holes, int slots, int kinds)
        => $"{holes} 个孔" + (slots > 0 ? $"（含 {slots} 个腰型孔）" : string.Empty) + $"共 {kinds} 种";

    /// <summary>
    /// 同心的归成一个孔，取最小的那条：通孔取孔径本身，沉头/锥孔取底孔。
    /// 异形孔向导的孔标注从哪条边进去都读得到完整规格；手工切出来的孔只有取底孔才标的是孔径。
    /// </summary>
    public static List<HoleEdge> MergeConcentric(IReadOnlyList<HoleEdge> edges)
    {
        var holes = new List<HoleEdge>();
        foreach (var edge in edges.OrderBy(edge => edge.Radius))
        {
            if (!holes.Any(hole => SameCenter(hole.X, hole.Y, edge.X, edge.Y)))
                holes.Add(edge);
        }

        return holes;
    }

    /// <summary>
    /// 一种 = 同一特征、同一孔径（腰型孔另加同一长度，<see cref="SlotPlanner.Pair"/> 已写进种名）。
    /// 孔径按图纸上 0.001 mm 取整，免得浮点尾数把一种拆成几种。
    /// 每种里的孔按图纸上从上到下、从左到右排。
    /// </summary>
    public static List<List<HoleEdge>> GroupKinds(IEnumerable<HoleEdge> holes)
        => holes
            .GroupBy(hole => (hole.Kind, Math.Round(hole.Radius / 1e-6)))
            .Select(kind => ReadingOrder(kind).ToList())
            .ToList();

    /// <summary>图纸上从上到下、从左到右。同一行（Y 差在容差内）按 X 排。</summary>
    public static IEnumerable<HoleEdge> ReadingOrder(IEnumerable<HoleEdge> holes)
        => holes.OrderByDescending(hole => Math.Round(hole.Y / CenterTolerance)).ThenBy(hole => hole.X);

    /// <summary>两个图纸点是否算同一个孔心（<see cref="CenterTolerance"/>）。</summary>
    public static bool SameCenter(SheetPoint a, SheetPoint b) => SameCenter(a.X, a.Y, b.X, b.Y);

    /// <summary>
    /// 引线折点——文字下划线的右端——放在孔的左上方 45°，离孔心「半径 + <see cref="LeaderReach"/>」。
    /// 文字从折点往左排，引线从折点斜向右下指到孔：用户在移动底板上手工摆出来的样子（1.1.0 起）。
    /// </summary>
    public static SheetPoint Placement(HoleEdge hole)
    {
        var offset = (hole.Radius + LeaderReach) * Math.Sqrt(0.5);
        return new SheetPoint(hole.X - offset, hole.Y + offset);
    }

    /// <summary>
    /// 孔标注的引线折点：显示数据里最长的那条水平线（文字下划线）的右端（文字在折点右边时取左端，1.6.0）。
    /// 没有水平线返回 null。
    /// </summary>
    public static SheetPoint? ShoulderEnd(IEnumerable<SheetSegment> lines, bool textLeft = true)
    {
        var shoulder = lines
            .Where(line => Math.Abs(line.Y1 - line.Y2) < 1e-7 && Math.Abs(line.X1 - line.X2) > 1e-6)
            .OrderByDescending(line => Math.Abs(line.X1 - line.X2))
            .Cast<SheetSegment?>()
            .FirstOrDefault();
        return shoulder is { } s ? new SheetPoint(textLeft ? Math.Max(s.X1, s.X2) : Math.Min(s.X1, s.X2), s.Y1) : null;
    }

    private static bool SameCenter(double ax, double ay, double bx, double by)
    {
        var dx = ax - bx;
        var dy = ay - by;
        return dx * dx + dy * dy <= CenterTolerance * CenterTolerance;
    }
}
