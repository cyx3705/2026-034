namespace HistoryStrenua;

/// <summary>
/// 视图里的一根对称轴（1.14.1）。
/// </summary>
/// <param name="Axis">
/// 它管哪个方向的孔位尺寸：<see cref="PositionAxis.Horizontal"/> 时对称轴是竖线 x = <paramref name="At"/>（左右对称），
/// <see cref="PositionAxis.Vertical"/> 时是横线 y = <paramref name="At"/>（上下对称）。
/// </param>
/// <param name="At">对称轴在图纸上的位置（米）。</param>
/// <param name="FirstLine">插中心线要选的一对对边之一（<see cref="ScannedView.Lines"/> 下标）：左（上）边。</param>
/// <param name="SecondLine">与它对称的那条边。</param>
internal sealed record SymmetryAxis(PositionAxis Axis, double At, int FirstLine, int SecondLine)
{
    /// <summary>回执里的叫法。</summary>
    public string Name => Axis == PositionAxis.Horizontal ? "竖直对称轴" : "水平对称轴";
}

/// <summary>
/// 「对称轴」（1.14.1，用户定）：视图里全部的边和孔都关于一根轴对称时，加上对称轴（SolidWorks 中心线），
/// 这个方向的孔位尺寸以对称轴为基准——不从对称轴标起，而是直接标对称轴两侧孔之间的距离（用户手工示范：板条两孔标 75，不再标 12.5）。
/// </summary>
/// <remarks>
/// <para>「同种孔可以互相标、不同种不互相标」照旧：每种孔在这个方向上仍是链式（<see cref="HolePositionPlanner.Plan"/>），
/// 只是不再有从基准边到第一个孔的那个尺寸——整种孔关于对称轴对称，链本身就跨过对称轴把位置定死了；正好在对称轴上的单个孔不用标。</para>
/// <para>判法：对称轴取边的外包范围的中线；每条直边、曲线近似段关于它镜像后都要被同一直线上的边盖住（边在投影里常被切成几段，按并集判）；
/// 每个孔（含腰型孔端头）镜像后都要落在一个同种、同孔径的孔上（腰型孔端头鼓出方向也要镜像）。
/// 还要至少有一个孔不在对称轴上——孔全在轴上（示范里侧视图的横向）时照旧从基准边标（用户示范保留了那个「5」）。
/// 1.14.2（用户定：理论上对称的视图就要加对称轴）：视图里有要定位的圆弧圆心（<see cref="ArcCenterPlanner.Locatable"/>）也算，
/// 在不在轴上都行——没有孔、只有圆弧的视图（右视图的键槽形型腔）同样加轴，轴上的圆心那个方向就不用标。</para>
/// <para>插中心线选左右（上下）一对对称的直边，<c>IDrawingDoc.InsertCenterLine2</c> 就生成在两边正中、两头各伸出一点，与手工插的一致（真机核对过）。
/// 找不到这样一对边就不算对称（没有轴，尺寸也就没有基准）。尺寸链模式（坐标尺寸）不管对称，照旧从直边量。</para>
/// </remarks>
internal static class SymmetryPlanner
{
    /// <summary>对称判定的容差（图纸 0.05 mm）。</summary>
    public const double Tolerance = 5e-5;

    /// <summary>直线方向算平行的容差（单位向量叉积）。</summary>
    private const double ParallelTolerance = 1e-3;

    /// <param name="lines">视图里的直边（图纸）。</param>
    /// <param name="curves">其余曲线边近似成的线段。</param>
    /// <param name="holes">认出来的孔（<see cref="HoleCalloutPlanner.Recognize"/> 的结果，含腰型孔两端）。</param>
    /// <param name="centers">要定位的圆弧圆心（1.14.2）：有它们时孔都在轴上、或没有孔也加轴。圆弧本身的对称由 <paramref name="curves"/> 判。</param>
    /// <returns>竖直对称轴（管水平尺寸）在前、水平对称轴在后；都不对称为空。</returns>
    public static List<SymmetryAxis> Axes(
        IReadOnlyList<SheetSegment> lines, IReadOnlyList<SheetSegment> curves, IReadOnlyList<HoleEdge> holes, IReadOnlyList<SheetPoint>? centers = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(curves);
        ArgumentNullException.ThrowIfNull(holes);
        var result = new List<SymmetryAxis>();
        var segments = lines.Concat(curves).Where(segment => Length(segment) > Tolerance).ToList();
        var arcCenters = centers ?? [];
        if (segments.Count == 0 || (holes.Count == 0 && arcCenters.Count == 0))
            return result;

        foreach (var axis in new[] { PositionAxis.Horizontal, PositionAxis.Vertical })
        {
            var vertical = axis == PositionAxis.Horizontal;
            var values = segments.SelectMany(s => vertical ? new[] { s.X1, s.X2 } : [s.Y1, s.Y2]).ToList();
            var at = (values.Min() + values.Max()) / 2;
            if (arcCenters.Count == 0 && !holes.Any(hole => Math.Abs((vertical ? hole.X : hole.Y) - at) > Tolerance))
                continue;
            if (!holes.All(hole => holes.Any(other => MirrorHole(hole, other, vertical, at))))
                continue;
            if (!segments.All(segment => Covered(Mirror(segment, vertical, at), segments)))
                continue;
            if (Pair(lines, vertical, at) is { } pair)
                result.Add(new SymmetryAxis(axis, at, pair.First, pair.Second));
        }

        return result;
    }

    /// <summary>
    /// 已有的中心线里有没有就是这根轴的（图纸上的线段整条落在轴上）：重复点不加第二根。
    /// </summary>
    public static bool Drawn(SymmetryAxis axis, IEnumerable<SheetSegment> centerLines)
        => centerLines.Any(line => axis.Axis == PositionAxis.Horizontal
            ? Math.Abs(line.X1 - axis.At) <= Tolerance && Math.Abs(line.X2 - axis.At) <= Tolerance
            : Math.Abs(line.Y1 - axis.At) <= Tolerance && Math.Abs(line.Y2 - axis.At) <= Tolerance);

    /// <summary>坐标 <paramref name="coordinate"/> 正好在某根管 <paramref name="axis"/> 方向的对称轴上（「未标尺寸」不再要它的这个方向）。</summary>
    public static bool OnAxis(IEnumerable<SymmetryAxis> axes, PositionAxis axis, double coordinate)
        => axes.Any(a => a.Axis == axis && Math.Abs(a.At - coordinate) <= Tolerance);

    /// <summary>
    /// 插中心线用的一对对边：轴一侧离轴最远的那条与轴平行的直边，以及另一侧与它恰好镜像的直边（同长同位）。
    /// 最远的那条没有恰好镜像的（被切成了几段）就往里找下一条。
    /// </summary>
    private static (int First, int Second)? Pair(IReadOnlyList<SheetSegment> lines, bool vertical, double at)
    {
        var parallel = Enumerable.Range(0, lines.Count)
            .Where(i => Length(lines[i]) > Tolerance && (vertical
                ? Math.Abs(lines[i].X1 - lines[i].X2) <= Tolerance
                : Math.Abs(lines[i].Y1 - lines[i].Y2) <= Tolerance))
            .ToList();
        // 竖直轴取左侧（X 小）的边，水平轴取上侧（Y 大）的边，由远到近。
        var near = parallel
            .Where(i => vertical ? lines[i].X1 < at - Tolerance : lines[i].Y1 > at + Tolerance)
            .OrderByDescending(i => Math.Abs((vertical ? lines[i].X1 : lines[i].Y1) - at))
            .ThenByDescending(i => Length(lines[i]));
        foreach (var first in near)
        {
            var mirror = Mirror(lines[first], vertical, at);
            var second = parallel.FirstOrDefault(j => j != first && SameSegment(lines[j], mirror), -1);
            if (second >= 0)
                return (first, second);
        }

        return null;
    }

    private static bool MirrorHole(HoleEdge hole, HoleEdge other, bool vertical, double at)
    {
        var (x, y) = vertical ? (2 * at - hole.X, hole.Y) : (hole.X, 2 * at - hole.Y);
        if (other.Kind != hole.Kind
            || Math.Abs(other.Radius - hole.Radius) > Math.Max(1e-7, hole.Radius * HoleCalloutPlanner.RadiusTolerance)
            || Math.Abs(other.X - x) > Tolerance || Math.Abs(other.Y - y) > Tolerance
            || other.IsSlotEnd != hole.IsSlotEnd)
            return false;
        if (!hole.IsSlotEnd)
            return true;
        var (bx, by) = vertical ? (-hole.BulgeX, hole.BulgeY) : (hole.BulgeX, -hole.BulgeY);
        return Math.Abs(other.BulgeX - bx) < 1e-3 && Math.Abs(other.BulgeY - by) < 1e-3;
    }

    private static SheetSegment Mirror(SheetSegment s, bool vertical, double at)
        => vertical ? new SheetSegment(2 * at - s.X1, s.Y1, 2 * at - s.X2, s.Y2) : new SheetSegment(s.X1, 2 * at - s.Y1, s.X2, 2 * at - s.Y2);

    private static bool SameSegment(SheetSegment a, SheetSegment b)
        => (Near(a.X1, a.Y1, b.X1, b.Y1) && Near(a.X2, a.Y2, b.X2, b.Y2)) || (Near(a.X1, a.Y1, b.X2, b.Y2) && Near(a.X2, a.Y2, b.X1, b.Y1));

    /// <summary>线段 <paramref name="target"/> 是否被 <paramref name="segments"/> 里与它共线的那些（并集）整条盖住。</summary>
    internal static bool Covered(SheetSegment target, IReadOnlyList<SheetSegment> segments)
    {
        var length = Length(target);
        if (length <= Tolerance)
            return true;
        var (ux, uy) = ((target.X2 - target.X1) / length, (target.Y2 - target.Y1) / length);
        double Along(double x, double y) => (x - target.X1) * ux + (y - target.Y1) * uy;
        double Across(double x, double y) => Math.Abs((x - target.X1) * uy - (y - target.Y1) * ux);

        var spans = new List<(double From, double To)>();
        foreach (var s in segments)
        {
            var l = Length(s);
            if (l <= Tolerance)
                continue;
            var cross = Math.Abs((s.X2 - s.X1) / l * uy - (s.Y2 - s.Y1) / l * ux);
            if (cross > ParallelTolerance || Across(s.X1, s.Y1) > Tolerance || Across(s.X2, s.Y2) > Tolerance)
                continue;
            var (a, b) = (Along(s.X1, s.Y1), Along(s.X2, s.Y2));
            spans.Add((Math.Min(a, b), Math.Max(a, b)));
        }

        var reached = 0.0;
        foreach (var (from, to) in spans.OrderBy(span => span.From))
        {
            if (from > reached + Tolerance)
                break;
            reached = Math.Max(reached, to);
        }

        return reached >= length - Tolerance;
    }

    private static bool Near(double ax, double ay, double bx, double by)
        => Math.Abs(ax - bx) <= Tolerance && Math.Abs(ay - by) <= Tolerance;

    private static double Length(SheetSegment s) => Math.Sqrt((s.X2 - s.X1) * (s.X2 - s.X1) + (s.Y2 - s.Y1) * (s.Y2 - s.Y1));
}
