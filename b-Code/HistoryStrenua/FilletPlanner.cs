using System.Globalization;

namespace HistoryStrenua;

/// <summary>视图里一段圆角弧（图纸坐标，米）。</summary>
/// <param name="Index">回指调用方手里那条边。</param>
/// <param name="Center">圆心。</param>
/// <param name="Radius">图纸上的半径（已乘比例）。</param>
/// <param name="ModelRadius">模型半径（尺寸值）。</param>
/// <param name="Middle">弧中点。</param>
/// <param name="Concave">内凹（内圆角、凹弧：圆心在零件外）；外圆角为 false。</param>
/// <param name="Sweep">圆心角（弧度，1.12.0）：带一个端点的整圈为 2π；0 = 没量（不参与整圆判定）。</param>
internal readonly record struct FilletArc(int Index, SheetPoint Center, double Radius, double ModelRadius, SheetPoint Middle, bool Concave, double Sweep = 0);

/// <summary>要加的一个 R 尺寸（整圆是 Ø 尺寸，1.12.0）：标哪段弧、文字放哪、前缀（几段同半径的合标时「N x 」）。</summary>
internal readonly record struct FilletTarget(int Index, SheetPoint TextAt, int Count, bool Diameter = false)
{
    public string Prefix => Count > 1 ? $"{Count} x " : string.Empty;
}

/// <summary>规划结果。</summary>
/// <param name="Targets">要加的 R 尺寸，按图纸上从上到下、从左到右。</param>
/// <param name="ArcCount">认出的圆角（同一圆角被切成几段的算一个）。</param>
/// <param name="DefaultCount">R1 按技术要求「未注圆角R1」不标的个数。</param>
/// <param name="Dimensioned">已经有 R / 直径尺寸、跳过的个数（圆角与整圆合计）。</param>
/// <param name="CircleCount">认出的要标 Ø 的整圆（1.12.0：孔已减掉，剩凸台、轴端这类）；不算在 <paramref name="ArcCount"/> 里。</param>
internal sealed record FilletPlan(IReadOnlyList<FilletTarget> Targets, int ArcCount, int DefaultCount, int Dimensioned, int CircleCount = 0);

/// <summary>
/// 圆角标注（1.9.0）的纯几何部分：哪些弧要标 R、几段同半径的怎么合标、文字放哪。不碰 SolidWorks，能离线测。
/// </summary>
/// <remarks>
/// <para>依据用户手工图（连接件 R5 / R10 / R5、夹爪 R101 / R30 / R5 / R5）：每个圆角一个 R 尺寸、引线指到弧上、文字在零件外的空处；
/// 技术要求写了「未注圆角R1」，R1 不标。同半径的有 3 段以上时只标一个、前面写「N x 」（与用户「2 x C5」倒角、「8 x M5」孔标注同一写法）。</para>
/// <para>文字往弧外的空处放：外圆角从圆心往弧中点再往外，内圆角（圆心在零件外）从弧中点往圆心方向。正方向压到视图里的线就左右各转 30°、60°，
/// 再远一档（8 / 12 / 16 mm），都压就取压得最少的。</para>
/// <para>1.11.0 归「避障」开关管（用户定：加东西的指令都挂上避障）：开着时除了视图的线，还躲视图里已有注解的线条与文字
/// （孔标注、孔位尺寸……，一键出图里圆角在孔标注全流程之后）；关着时放在正方向近的一档，只保证在图框里。</para>
/// </remarks>
internal static class FilletPlanner
{
    /// <summary>技术要求「未注圆角R1」：模型半径 1 mm（差 0.005 mm 内）不标。</summary>
    public const double DefaultRadius = 0.001;

    /// <summary>两段弧算同一个圆角：图纸上圆心、半径都差不到 0.05 mm（一个圆角面被别的特征切成几段时）。</summary>
    public const double SameTolerance = 5e-5;

    /// <summary>同半径（模型差 0.005 mm 内）的圆角到这么多段才合标「N x R」。</summary>
    public const int GroupThreshold = 3;

    /// <summary>文字离弧中点的几档距离（图纸 8 / 12 / 16 mm）。</summary>
    public static readonly double[] Reaches = [0.008, 0.012, 0.016];

    /// <summary>文字方向在正方向两侧依次转的角度。</summary>
    private static readonly double[] Turns = [0, Math.PI / 6, -Math.PI / 6, Math.PI / 3, -Math.PI / 3];

    /// <summary>文字估计：字高 3.5 mm，每个字宽 2.6 mm（与模板「汉仪长仿宋 3.5」实测相符）。</summary>
    private const double CharHeight = 0.0035;
    private const double CharWidth = 0.0026;

    /// <param name="arcs">视图里的圆角弧。</param>
    /// <param name="dimensioned">已有 R / 直径尺寸连着的弧：图纸上的圆心与半径。</param>
    /// <param name="obstacles">文字不要压的线（视图里的边、标题栏等的边）。</param>
    /// <param name="inside">文字要落在这里面（图框，1.10.0）；null 不限。</param>
    /// <param name="texts">已有注解的文字框，也要躲（1.11.0）。</param>
    /// <param name="avoid">「避障」开关（1.11.0）：false 时不躲线和文字，只保证在 <paramref name="inside"/> 里。</param>
    public static FilletPlan Plan(IReadOnlyList<FilletArc> arcs, IReadOnlyList<(SheetPoint Center, double Radius)> dimensioned, IReadOnlyList<SheetSegment> obstacles,
        SheetRect? inside = null, IReadOnlyList<TextBox>? texts = null, bool avoid = true)
    {
        var distinct = new List<FilletArc>();
        foreach (var arc in arcs)
            if (!distinct.Any(known => Same(known.Center, known.Radius, arc.Center, arc.Radius)))
                distinct.Add(arc);

        // 1.12.0：整圆（几段合起来满一圈；孔已由 WithoutCircles 减掉）标 Ø，不吃「未注圆角 R1」，文字往右上。
        var full = distinct.Where(arc => IsFull(arcs, arc)).Select(arc => arc.Index).ToHashSet();
        bool Default(FilletArc arc) => !full.Contains(arc.Index) && Math.Abs(arc.ModelRadius - DefaultRadius) <= 5e-6;
        var defaults = distinct.Count(Default);
        var already = distinct.Count(arc => !Default(arc) && dimensioned.Any(d => Same(d.Center, d.Radius, arc.Center, arc.Radius)));
        var pending = distinct
            .Where(arc => !Default(arc))
            .Where(arc => !dimensioned.Any(d => Same(d.Center, d.Radius, arc.Center, arc.Radius)))
            .Select(arc => full.Contains(arc.Index) ? arc with { Middle = UpRight(arc), Concave = false } : arc)
            .ToList();

        var chosen = new List<(FilletArc Arc, int Count)>();
        foreach (var group in pending.GroupBy(arc => (Math.Round(arc.ModelRadius / 5e-6), arc.Concave, full.Contains(arc.Index))))
        {
            var members = group.OrderByDescending(arc => arc.Middle.Y).ThenBy(arc => arc.Middle.X).ToList();
            if (members.Count >= GroupThreshold)
                chosen.Add((members[0], members.Count));
            else
                chosen.AddRange(members.Select(arc => (arc, 1)));
        }

        var lines = avoid ? obstacles : [];
        var placed = avoid ? new List<TextBox>(texts ?? []) : [];
        var targets = new List<FilletTarget>();
        foreach (var (arc, count) in chosen.OrderByDescending(item => item.Arc.Middle.Y).ThenBy(item => item.Arc.Middle.X))
        {
            var diameter = full.Contains(arc.Index);
            var text = (count > 1 ? $"{count} x " : string.Empty) + (diameter ? "Ø" + Value(2 * arc.ModelRadius) : "R" + Value(arc.ModelRadius));
            var (at, box) = PlaceText(arc.Middle, Outward(arc), text.Length, lines, placed, inside);
            if (avoid)
                placed.Add(box);
            targets.Add(new FilletTarget(arc.Index, at, count, diameter));
        }

        return new FilletPlan(targets, distinct.Count - full.Count, defaults, already, full.Count);
    }

    /// <summary>尺寸值的写法：「5」「5.5」「101」。</summary>
    public static string Value(double modelRadius) => (modelRadius * 1000).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>文字的正方向：外圆角从圆心指向弧中点，内圆角从弧中点指向圆心（空处都在那边）。</summary>
    public static (double X, double Y) Outward(FilletArc arc)
    {
        var (dx, dy) = arc.Concave
            ? (arc.Center.X - arc.Middle.X, arc.Center.Y - arc.Middle.Y)
            : (arc.Middle.X - arc.Center.X, arc.Middle.Y - arc.Center.Y);
        var length = Math.Sqrt(dx * dx + dy * dy);
        return length > 0 ? (dx / length, dy / length) : (Math.Sqrt(0.5), Math.Sqrt(0.5));
    }

    /// <summary>
    /// 文字放哪（圆角、倒角共用）：从 <paramref name="from"/> 沿 <paramref name="outward"/> 走近的一档，先正方向、再两侧转；
    /// 压到线或别的文字、出了 <paramref name="inside"/>（图框）就试下一个，都不行取压得最少的。
    /// </summary>
    internal static (SheetPoint At, TextBox Box) PlaceText(SheetPoint from, (double X, double Y) outward, int characters,
        IReadOnlyList<SheetSegment> obstacles, IReadOnlyList<TextBox> placed, SheetRect? inside = null)
    {
        var (ox, oy) = outward;
        var width = characters * CharWidth;
        (SheetPoint At, TextBox Box, int Hits) best = default;
        var first = true;
        foreach (var reach in Reaches)
        {
            foreach (var turn in Turns)
            {
                var (dx, dy) = (ox * Math.Cos(turn) - oy * Math.Sin(turn), ox * Math.Sin(turn) + oy * Math.Cos(turn));
                var at = new SheetPoint(from.X + dx * reach, from.Y + dy * reach);
                // 文字以 at 为中心估一个框（水平文字）。
                var box = new TextBox(at.X - width / 2, at.Y - CharHeight / 2, width, CharHeight);
                var hits = obstacles.Count(line => ClearancePlanner.Hits(box, line)) + placed.Count(other => ClearancePlanner.Hits(box, other))
                    + (inside is { } frame && !new SheetRect(box.X, box.Y, box.X + box.Width, box.Y + box.Height).Within(frame) ? 10 : 0);
                if (hits == 0)
                    return (at, box);
                if (first || hits < best.Hits)
                    (best, first) = ((at, box, hits), false);
            }
        }

        return (best.At, best.Box);
    }

    /// <summary>判整圆的余量：同一圆上的弧合起来差这么多（弧度，约 3°）就算整圈。</summary>
    private const double SweepTolerance = 0.05;

    /// <summary>
    /// 减掉孔（1.12.0，用户定「识别所有圆，减去孔标注的圆」：认得出是孔的一律减，孔标注没标、标错是孔类的事，圆角标注不替它补）。减掉的：
    /// <list type="bullet">
    /// <item>内凹、合起来满一圈的圆：孔（含带一个端点的整圆边、被别的特征的边切成几段的孔——<see cref="HoleScan.ViewGeometry.TryReadHole"/>
    /// 只认没有端点的整圈和恰好半圈，这些漏到了这里）；</item>
    /// <item>内凹且超过半圈：被零件边切掉一截的孔口（圆角最多半圈）；</item>
    /// <item>与认出的孔（含腰型孔端头）同圆心同半径：孔的另一部分。</item>
    /// </list>
    /// 留下的：圆角弧（外凸弧即使与孔同心，如耳板端头 R10，照旧标 R），以及外凸的整圆（凸台、轴端），由 <see cref="Plan"/> 标 Ø。
    /// </summary>
    /// <returns>留下的弧与减掉的段数。</returns>
    public static (List<FilletArc> Arcs, int Excluded) WithoutCircles(IReadOnlyList<FilletArc> arcs, IReadOnlyList<HoleEdge> holes)
    {
        var kept = new List<FilletArc>();
        foreach (var arc in arcs)
        {
            var circle = arcs.Where(other => Same(other.Center, other.Radius, arc.Center, arc.Radius)).ToList();
            var sweep = circle.Sum(other => other.Sweep);
            var isHole = (arc.Concave && sweep > Math.PI + SweepTolerance)
                || holes.Any(hole => Same(new SheetPoint(hole.X, hole.Y), hole.Radius, arc.Center, arc.Radius));
            if (!isHole)
                kept.Add(arc);
        }

        return (kept, arcs.Count - kept.Count);
    }

    /// <summary>这段弧所在的圆（同圆心同半径的几段合起来）满一圈。</summary>
    private static bool IsFull(IReadOnlyList<FilletArc> arcs, FilletArc arc)
        => arcs.Where(other => Same(other.Center, other.Radius, arc.Center, arc.Radius)).Sum(other => other.Sweep) >= 2 * Math.PI - SweepTolerance;

    /// <summary>整圆的 Ø 文字从圆心往右上 45° 引出（与孔标注的左上错开）。</summary>
    private static SheetPoint UpRight(FilletArc arc) => new(arc.Center.X + arc.Radius * Math.Sqrt(0.5), arc.Center.Y + arc.Radius * Math.Sqrt(0.5));

    /// <summary>圆弧的圆心角（弧度）：起点、终点重合（带一个端点的整圈）为 2π；否则是起点到弧中点夹角的两倍。</summary>
    public static double SweepOf(SheetPoint center, SheetPoint start, SheetPoint end, SheetPoint middle)
    {
        var radius = Math.Max(Distance(center, start), 1e-12);
        if (Distance(start, end) <= radius * 1e-6)
            return 2 * Math.PI;
        var (sx, sy) = (start.X - center.X, start.Y - center.Y);
        var (mx, my) = (middle.X - center.X, middle.Y - center.Y);
        return 2 * Math.Abs(Math.Atan2(sx * my - sy * mx, sx * mx + sy * my));
    }

    private static double Distance(SheetPoint a, SheetPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static bool Same(SheetPoint a, double ra, SheetPoint b, double rb)
        => Math.Abs(a.X - b.X) <= SameTolerance && Math.Abs(a.Y - b.Y) <= SameTolerance && Math.Abs(ra - rb) <= SameTolerance;
}
