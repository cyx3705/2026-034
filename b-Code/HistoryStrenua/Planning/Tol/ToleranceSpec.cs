using System.Globalization;
using System.Text.RegularExpressions;

namespace HistoryStrenua;

/// <summary>一个尺寸公差：<paramref name="Type"/> 是 <c>swTolType_e</c>；偏差是带符号的 mm，上偏差 <paramref name="Upper"/>、下偏差 <paramref name="Lower"/>。</summary>
internal readonly record struct DimensionToleranceSpec(int Type, string Title, double? Upper = null, double? Lower = null, string? HoleFit = null, string? ShaftFit = null);

/// <summary>形位公差框的一行：几何特征符号、公差值、基准。文字都是 SolidWorks 的符号代码，直接交给 <c>IGtol</c>。</summary>
internal sealed record GtolFrame(string Title, string Symbol, bool Diameter, string Value, string Modifiers, string? ProjectedZone, IReadOnlyList<string> Datums);

/// <summary>公差标注的参数解析（1.36.0）：纯文本，填错在碰 SolidWorks 之前就报。</summary>
internal static class ToleranceSpec
{
    private const int None = 0, Basic = 1, Bilateral = 2, Limit = 3, Symmetric = 4, Min = 5, Max = 6, Fit = 7, FitWithTolerance = 8, FitToleranceOnly = 9;

    /// <summary>type= 的写法。</summary>
    public const string TypeHelp = "symmetric 对称、bilateral 双向、limit 极限、basic 基本（理论正确尺寸）、min 最小、max 最大、fit 配合、fittol 配合带偏差、fitonly 只显示配合偏差、none 去掉";

    public static DimensionToleranceSpec Dimension(string type, double? plus, double? minus, string? hole, string? shaft)
    {
        var (code, title) = type.Trim().ToLowerInvariant() switch
        {
            "symmetric" or "对称" => (Symmetric, "对称"),
            "bilateral" or "双向" or "双边" => (Bilateral, "双向"),
            "limit" or "极限" => (Limit, "极限"),
            "basic" or "基本" or "理论正确" => (Basic, "基本"),
            "min" or "最小" => (Min, "最小"),
            "max" or "最大" => (Max, "最大"),
            "fit" or "配合" => (Fit, "配合"),
            "fittol" or "配合带偏差" => (FitWithTolerance, "配合带偏差"),
            "fitonly" or "只显示偏差" => (FitToleranceOnly, "只显示配合偏差"),
            "none" or "无" => (None, "无公差"),
            _ => throw new QuickArgumentException($"type={type} 写不对：{TypeHelp}。"),
        };
        switch (code)
        {
            case Symmetric:
                if (plus is not > 0)
                    throw new QuickArgumentException("对称公差要 plus= 正数（mm），如 plus=0.02。");
                return new DimensionToleranceSpec(code, title, plus, -plus);
            case Bilateral or Limit:
                if (plus is not { } upper || minus is not { } lower)
                    throw new QuickArgumentException($"{title}公差要 plus= 上偏差和 minus= 下偏差（带符号 mm），如 plus=0.02 minus=-0.01。");
                if (upper <= lower)
                    throw new QuickArgumentException($"上偏差 {upper.ToString(CultureInfo.InvariantCulture)} 要大于下偏差 {lower.ToString(CultureInfo.InvariantCulture)}。");
                return new DimensionToleranceSpec(code, title, upper, lower);
            case Fit or FitWithTolerance or FitToleranceOnly:
                if (hole is null && shaft is null)
                    throw new QuickArgumentException($"{title}要 hole= 孔的配合（如 H7）或 shaft= 轴的配合（如 g6），至少一个。");
                return new DimensionToleranceSpec(code, title, HoleFit: FitCode(hole, "hole"), ShaftFit: FitCode(shaft, "shaft"));
            default:
                return new DimensionToleranceSpec(code, title);
        }
    }

    private static string? FitCode(string? text, string parameter)
    {
        if (text is null)
            return null;
        return FitPattern.IsMatch(text) ? text : throw new QuickArgumentException($"{parameter}={text} 写不对：公差带代号，如 H7、g6、JS7。");
    }

    /// <summary>解析 <c>frame=</c>：一行「符号 公差 基准…」，多行用分号分开，如 <c>位置度 Ø0.1(M) A B(M) C</c>。</summary>
    public static IReadOnlyList<GtolFrame> Frames(string text)
    {
        var frames = text.Split([';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(ParseFrame).ToList();
        return frames.Count > 0 ? frames : throw new QuickArgumentException("缺 frame=，如 frame=平行度,0.02,A。");
    }

    /// <summary>基准符号的字母：一两个大写字母。</summary>
    public static string DatumLabel(string text)
    {
        var label = text.Trim().ToUpperInvariant();
        return Regex.IsMatch(label, "^[A-Z]{1,2}$") ? label : throw new QuickArgumentException($"label={text} 写不对：基准字母，如 A。");
    }

    /// <summary>尺寸公差写成一句（SVG 的 data-tol）：种类 + 上下偏差（mm）或配合；没有公差为空串。</summary>
    public static string Describe(int type, double upper, double lower, string hole, string shaft)
    {
        static string Plain(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
        static string Signed(double value) => value > 0 ? "+" + Plain(value) : Plain(value);
        var fit = string.Join("/", new[] { hole, shaft }.Where(item => item.Length > 0));
        return type switch
        {
            Basic => "基本",
            Bilateral => $"双向 {Signed(upper)}/{Signed(lower)}",
            Limit => $"极限 {Signed(upper)}/{Signed(lower)}",
            Symmetric => $"对称 ±{Plain(upper)}",
            Min => "最小",
            Max => "最大",
            Fit => $"配合 {fit}",
            FitWithTolerance => $"配合带偏差 {fit}",
            FitToleranceOnly => $"配合偏差 {fit}",
            _ => string.Empty,
        };
    }

    /// <summary>引线落点在哪个几何上：先孔（落在孔内或孔边 0.5 mm 内），后直边（0.5 mm 内）；都不是为空串。单位米。</summary>
    public static string Locate(SheetPoint point, IReadOnlyList<SheetSegment> lines, IReadOnlyList<HoleEdge> holes)
    {
        const double reach = 0.0005;
        foreach (var hole in holes)
            if (Math.Sqrt(Math.Pow(point.X - hole.X, 2) + Math.Pow(point.Y - hole.Y, 2)) <= hole.Radius + reach)
                return "H" + hole.Index.ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < lines.Count; i++)
            if (Distance(point, lines[i]) <= reach)
                return "E" + i.ToString(CultureInfo.InvariantCulture);
        return string.Empty;
    }

    private static double Distance(SheetPoint p, SheetSegment line)
    {
        var (dx, dy) = (line.X2 - line.X1, line.Y2 - line.Y1);
        var length = dx * dx + dy * dy;
        var t = length <= 0 ? 0 : Math.Clamp(((p.X - line.X1) * dx + (p.Y - line.Y1) * dy) / length, 0, 1);
        return Math.Sqrt(Math.Pow(p.X - line.X1 - t * dx, 2) + Math.Pow(p.Y - line.Y1 - t * dy, 2));
    }

    /// <summary>显示数据里形位公差的符号代码 → 文字（读的是 AI，写中文名，和 frame= 的输入同一种写法）。</summary>
    public static IEnumerable<(string Code, string Symbol)> DisplaySymbols()
    {
        foreach (var (code, symbol) in Symbols.Select(item => (item.Code, item.Names[0])))
            yield return (code, symbol);
        yield return ("<MOD-MMC>", "(M)");
        yield return ("<MOD-LMC>", "(L)");
        yield return ("<MOD-FREES>", "(F)");
        yield return ("<MOD-TANP>", "(T)");
        yield return ("<MOD-PTZ>", "(P)");
    }

    private static GtolFrame ParseFrame(string text)
    {
        var tokens = text.Split([' ', '	', ',', '，'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
            throw new QuickArgumentException($"frame「{text}」写不对：至少要符号和公差值，如 平面度 0.02。");
        var symbol = Symbols.FirstOrDefault(item => item.Names.Contains(tokens[0], StringComparer.OrdinalIgnoreCase));
        if (symbol.Code is null)
            throw new QuickArgumentException($"frame「{text}」的符号 {tokens[0]} 不认得。认得的：{string.Join("、", Symbols.Select(item => item.Names[0]))}。");
        var match = ValuePattern.Match(tokens[1]);
        if (!match.Success)
            throw new QuickArgumentException($"frame「{text}」的公差值 {tokens[1]} 写不对：如 0.02、Ø0.1(M)、0.05/100、0.2(P50)。");

        string? zone = null;
        var modifiers = string.Empty;
        foreach (Capture capture in match.Groups["mod"].Captures)
        {
            var mod = capture.Value.ToUpperInvariant();
            if (mod[0] == 'P' && mod.Length > 1)
                zone = mod[1..];
            else
                modifiers += ModifierCode(mod, text);
        }

        var datums = tokens.Skip(2).Select(token => Datum(token, text)).ToList();
        if (datums.Count > 3)
            throw new QuickArgumentException($"frame「{text}」基准最多三个。");
        if (symbol.Datum == DatumRule.Forbidden && datums.Count > 0)
            throw new QuickArgumentException($"{symbol.Names[0]}是形状公差，不带基准：{text}。");
        if (symbol.Datum == DatumRule.Required && datums.Count == 0)
            throw new QuickArgumentException($"{symbol.Names[0]}要有基准：{text}。");
        return new GtolFrame(symbol.Names[0], symbol.Code, match.Groups["dia"].Success, match.Groups["value"].Value, modifiers, zone, datums);
    }

    private static string Datum(string token, string frame)
    {
        var match = DatumPattern.Match(token.ToUpperInvariant());
        if (!match.Success)
            throw new QuickArgumentException($"frame「{frame}」的基准 {token} 写不对：如 A、A-B、B(M)。");
        return match.Groups["name"].Value + (match.Groups["mod"].Success ? ModifierCode(match.Groups["mod"].Value, frame) : string.Empty);
    }

    private static string ModifierCode(string modifier, string frame) => modifier switch
    {
        "M" => "<MOD-MMC>",
        "L" => "<MOD-LMC>",
        "F" => "<MOD-FREES>",
        "T" => "<MOD-TANP>",
        _ => throw new QuickArgumentException($"frame「{frame}」的修饰符 ({modifier}) 不认得：M 最大实体、L 最小实体、F 自由状态、T 相切平面、P50 延伸公差带（高 50）。"),
    };

    private enum DatumRule
    {
        Forbidden,
        Optional,
        Required,
    }

    // 符号代码取自 SolidWorks 的 gtol97.sym。线、面轮廓度与位置度有基准没基准都合法。
    private static readonly (string[] Names, string Code, DatumRule Datum)[] Symbols =
    [
        (["直线度", "straight"], "<IGTOL-STRAIGHT>", DatumRule.Forbidden),
        (["平面度", "flat"], "<IGTOL-FLAT>", DatumRule.Forbidden),
        (["圆度", "circular"], "<IGTOL-CIRC>", DatumRule.Forbidden),
        (["圆柱度", "cylindricity"], "<IGTOL-CYL>", DatumRule.Forbidden),
        (["线轮廓度", "lineprofile"], "<IGTOL-LPROF>", DatumRule.Optional),
        (["面轮廓度", "surfaceprofile"], "<IGTOL-SPROF>", DatumRule.Optional),
        (["平行度", "parallel"], "<IGTOL-PARA>", DatumRule.Required),
        (["垂直度", "perpendicular"], "<IGTOL-PERP>", DatumRule.Required),
        (["倾斜度", "angular"], "<IGTOL-ANGULAR>", DatumRule.Required),
        (["位置度", "position"], "<IGTOL-POSI>", DatumRule.Optional),
        (["同轴度", "同心度", "concentric"], "<IGTOL-CONC>", DatumRule.Required),
        (["对称度", "symmetry"], "<IGTOL-SYMMETRY>", DatumRule.Required),
        (["圆跳动", "runout"], "<IGTOL-SRUN>", DatumRule.Required),
        (["全跳动", "totalrunout"], "<IGTOL-TRUN>", DatumRule.Required),
    ];

    private static readonly Regex FitPattern = new("^[A-Za-z]{1,3}[0-9]{1,2}$", RegexOptions.CultureInvariant);

    private static readonly Regex ValuePattern = new(@"^(?<dia>[Øø⌀φΦ])?(?<value>\d+(\.\d+)?(/\d+)?)(\((?<mod>[A-Za-z][0-9.]*)\))*$", RegexOptions.CultureInvariant);

    private static readonly Regex DatumPattern = new(@"^(?<name>[A-Z]\d?(-[A-Z]\d?)*)(\((?<mod>[A-Z])\))?$", RegexOptions.CultureInvariant);
}
