using System.Globalization;

namespace HistoryStrenua;

/// <summary>视图里一段圆角弧（图纸坐标，米）。</summary>
/// <param name="Index">回指调用方手里那条边。</param>
/// <param name="Center">圆心。</param>
/// <param name="Radius">图纸上的半径（已乘比例）。</param>
/// <param name="ModelRadius">模型半径（尺寸值）。</param>
/// <param name="Middle">弧中点。</param>
/// <param name="Concave">内凹（内圆角、凹弧：圆心在零件外）；外圆角为 false。</param>
internal readonly record struct FilletArc(int Index, SheetPoint Center, double Radius, double ModelRadius, SheetPoint Middle, bool Concave);

/// <summary>要加的一个 R 尺寸：标哪段弧、文字放哪、前缀（几段同半径的合标时「N x 」）。</summary>
internal readonly record struct FilletTarget(int Index, SheetPoint TextAt, int Count)
{
    public string Prefix => Count > 1 ? $"{Count} x " : string.Empty;
}

/// <summary>规划结果。</summary>
/// <param name="Targets">要加的 R 尺寸，按图纸上从上到下、从左到右。</param>
/// <param name="ArcCount">认出的圆角（同一圆角被切成几段的算一个）。</param>
/// <param name="DefaultCount">R1 按技术要求「未注圆角R1」不标的个数。</param>
/// <param name="Dimensioned">已经有 R / 直径尺寸、跳过的个数。</param>
internal sealed record FilletPlan(IReadOnlyList<FilletTarget> Targets, int ArcCount, int DefaultCount, int Dimensioned);

/// <summary>
/// 圆角标注（1.9.0）的纯几何部分：哪些弧要标 R、几段同半径的怎么合标、文字放哪。不碰 SolidWorks，能离线测。
/// </summary>
/// <remarks>
/// <para>依据用户手工图（连接件 R5 / R10 / R5、夹爪 R101 / R30 / R5 / R5）：每个圆角一个 R 尺寸、引线指到弧上、文字在零件外的空处；
/// 技术要求写了「未注圆角R1」，R1 不标。同半径的有 3 段以上时只标一个、前面写「N x 」（与用户「2 x C5」倒角、「8 x M5」孔标注同一写法）。</para>
/// <para>文字往弧外的空处放：外圆角从圆心往弧中点再往外，内圆角（圆心在零件外）从弧中点往圆心方向。正方向压到视图里的线就左右各转 30°、60°，
/// 再远一档（8 / 12 / 16 mm），都压就取压得最少的。</para>
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
    /// <param name="obstacles">文字不要压的线（视图里的边）。</param>
    public static FilletPlan Plan(IReadOnlyList<FilletArc> arcs, IReadOnlyList<(SheetPoint Center, double Radius)> dimensioned, IReadOnlyList<SheetSegment> obstacles)
    {
        var distinct = new List<FilletArc>();
        foreach (var arc in arcs)
            if (!distinct.Any(known => Same(known.Center, known.Radius, arc.Center, arc.Radius)))
                distinct.Add(arc);

        var defaults = distinct.Count(arc => Math.Abs(arc.ModelRadius - DefaultRadius) <= 5e-6);
        var already = distinct.Count(arc => Math.Abs(arc.ModelRadius - DefaultRadius) > 5e-6 && dimensioned.Any(d => Same(d.Center, d.Radius, arc.Center, arc.Radius)));
        var pending = distinct
            .Where(arc => Math.Abs(arc.ModelRadius - DefaultRadius) > 5e-6)
            .Where(arc => !dimensioned.Any(d => Same(d.Center, d.Radius, arc.Center, arc.Radius)))
            .ToList();

        var chosen = new List<(FilletArc Arc, int Count)>();
        foreach (var group in pending.GroupBy(arc => (Math.Round(arc.ModelRadius / 5e-6), arc.Concave)))
        {
            var members = group.OrderByDescending(arc => arc.Middle.Y).ThenBy(arc => arc.Middle.X).ToList();
            if (members.Count >= GroupThreshold)
                chosen.Add((members[0], members.Count));
            else
                chosen.AddRange(members.Select(arc => (arc, 1)));
        }

        var placed = new List<TextBox>();
        var targets = new List<FilletTarget>();
        foreach (var (arc, count) in chosen.OrderByDescending(item => item.Arc.Middle.Y).ThenBy(item => item.Arc.Middle.X))
        {
            var text = (count > 1 ? $"{count} x " : string.Empty) + "R" + Value(arc.ModelRadius);
            var (at, box) = Place(arc, text.Length, obstacles, placed);
            placed.Add(box);
            targets.Add(new FilletTarget(arc.Index, at, count));
        }

        return new FilletPlan(targets, distinct.Count, defaults, already);
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

    /// <summary>文字放哪：近的一档里先正方向、再两侧转；压到线或别的文字就试下一个，都压取压得最少的。</summary>
    private static (SheetPoint At, TextBox Box) Place(FilletArc arc, int characters, IReadOnlyList<SheetSegment> obstacles, IReadOnlyList<TextBox> placed)
    {
        var (ox, oy) = Outward(arc);
        var width = characters * CharWidth;
        (SheetPoint At, TextBox Box, int Hits) best = default;
        var first = true;
        foreach (var reach in Reaches)
        {
            foreach (var turn in Turns)
            {
                var (dx, dy) = (ox * Math.Cos(turn) - oy * Math.Sin(turn), ox * Math.Sin(turn) + oy * Math.Cos(turn));
                var at = new SheetPoint(arc.Middle.X + dx * reach, arc.Middle.Y + dy * reach);
                // 文字以 at 为中心估一个框（水平文字）。
                var box = new TextBox(at.X - width / 2, at.Y - CharHeight / 2, width, CharHeight);
                var hits = obstacles.Count(line => ClearancePlanner.Hits(box, line)) + placed.Count(other => ClearancePlanner.Hits(box, other));
                if (hits == 0)
                    return (at, box);
                if (first || hits < best.Hits)
                    (best, first) = ((at, box, hits), false);
            }
        }

        return (best.At, best.Box);
    }

    private static bool Same(SheetPoint a, double ra, SheetPoint b, double rb)
        => Math.Abs(a.X - b.X) <= SameTolerance && Math.Abs(a.Y - b.Y) <= SameTolerance && Math.Abs(ra - rb) <= SameTolerance;
}
