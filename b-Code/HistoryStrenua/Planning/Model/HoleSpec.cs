using System.Globalization;

namespace HistoryStrenua;

/// <summary>
/// 异形孔向导的一种孔（1.43.0）：中文类型名 → SolidWorks 孔向导的大类 + GB 紧固件类型。
/// </summary>
/// <remarks>
/// 编号全是真机读 <c>ISldWorks.GetHoleStandardsData</c> 拿到的（SolidWorks 2025，GB = 13）：
/// 柱形沉头 361 内六角圆柱头 GB/T 70.1；锥形沉头 ISO 140（ISO 10642）退 GB 362；通孔 356 螺钉间隙；钻孔 355 钻孔大小；销孔 708 暗销孔；螺纹孔 359。
/// 规格写法：螺钉类「M6」，钻孔 / 销孔「Ø6.0」（至少一位小数，「6」「Ø6」SolidWorks 都不认）。
/// </remarks>
internal sealed record HoleKind(string Name, int Generic, int Fastener, bool Metric, string Description, int IsoFastener = 0, bool IsoFirst = false)
{
    public const int StandardGb = 13;
    public const int StandardIso = 8;

    /// <summary>
    /// 依次试的（孔库标准, 紧固件类型）：GB 有的档先用 GB，没有的退到 ISO 同类。
    /// 真机：GB 362 / 363 沉头只有 M6 起，M3–M5 建不出；ISO 140（ISO 10642 内六角沉头，等同 GB/T 70.3）M3–M12 全有，锥形沉头先用它。
    /// </summary>
    public IReadOnlyList<(int Standard, int Fastener)> Candidates
        => IsoFastener == 0 ? [(StandardGb, Fastener)]
            : IsoFirst ? [(StandardIso, IsoFastener), (StandardGb, Fastener)]
            : [(StandardGb, Fastener), (StandardIso, IsoFastener)];

    // swWzdGeneralHoleTypes_e
    public const int CounterBore = 0;
    public const int CounterSink = 1;
    public const int Plain = 2;
    public const int Tap = 4;

    public static IReadOnlyList<HoleKind> All { get; } =
    [
        new("沉头", CounterBore, 361, true, "柱形沉头孔（内六角圆柱头螺钉 GB/T 70.1，沉孔按标准）", 139),
        new("锥形沉头", CounterSink, 362, true, "锥形沉头孔（内六角沉头螺钉 ISO 10642 / GB/T 70.3，90°）", 140, true),
        new("通孔", Plain, 356, true, "螺钉过孔（按螺钉配合取孔径）", 144),
        new("螺纹", Tap, 359, true, "螺纹孔（底孔 + 装饰螺纹线）", 147),
        new("钻孔", Plain, 355, false, "光孔（直接给直径）", 143),
        new("销孔", Plain, 708, false, "销钉孔（暗销孔）", 710),
    ];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["沉头"] = "沉头", ["沉头孔"] = "沉头", ["柱形沉头"] = "沉头", ["沉孔"] = "沉头", ["cbore"] = "沉头", ["counterbore"] = "沉头",
        ["锥形沉头"] = "锥形沉头", ["锥孔"] = "锥形沉头", ["埋头"] = "锥形沉头", ["埋头孔"] = "锥形沉头", ["csk"] = "锥形沉头", ["countersink"] = "锥形沉头",
        ["通孔"] = "通孔", ["过孔"] = "通孔", ["螺钉过孔"] = "通孔", ["clearance"] = "通孔",
        ["螺纹"] = "螺纹", ["螺纹孔"] = "螺纹", ["攻丝"] = "螺纹", ["tap"] = "螺纹",
        ["钻孔"] = "钻孔", ["光孔"] = "钻孔", ["盲孔"] = "钻孔", ["drill"] = "钻孔",
        ["销孔"] = "销孔", ["销钉孔"] = "销孔", ["dowel"] = "销孔",
    };

    public static HoleKind Parse(string text)
        => Aliases.TryGetValue(text.Trim(), out var name)
            ? All.First(kind => kind.Name == name)
            : throw new QuickArgumentException($"type={text}：不认识。写 {string.Join(" / ", All.Select(kind => kind.Name))}。");
}

/// <summary>螺钉配合（swWzdHoleScrewClearanceTypes_e）：只对沉头 / 锥形沉头 / 通孔有用。</summary>
internal enum HoleFit
{
    Close = 0,
    Normal = 1,
    Loose = 2,
}

/// <summary>一次「异形孔」请求：类型、规格、深度、位置与可选覆盖值，全部 mm。</summary>
internal sealed record HoleSpec(
    HoleKind Kind,
    string Size,
    double? Depth,
    IReadOnlyList<(double X, double Y, double Z)> Points,
    HoleFit Fit = HoleFit.Normal,
    double? Diameter = null,
    double? CounterDiameter = null,
    double? CounterDepth = null,
    double? ThreadDepth = null,
    bool Reverse = false)
{
    public bool Through => Depth is null;

    /// <summary>规格写成 SolidWorks 孔库认的样子：螺钉类「M6」，钻孔 / 销孔「Ø6.0」。</summary>
    public static string NormalizeSize(HoleKind kind, string text)
    {
        var raw = text.Trim().TrimStart('Ø', 'ø', 'Φ', 'φ', '⌀').Trim();
        if (kind.Metric)
        {
            var body = raw.StartsWith('M') || raw.StartsWith('m') ? raw[1..] : raw;
            if (!double.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out var nominal) || nominal <= 0)
                throw new QuickArgumentException($"size={text}：{kind.Name}要写螺纹规格，如 M6、M12。");
            return "M" + nominal.ToString("0.##", CultureInfo.InvariantCulture);
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var diameter) || diameter <= 0)
            throw new QuickArgumentException($"size={text}：{kind.Name}要写直径 mm，如 6 或 Ø6。");
        return "Ø" + diameter.ToString("0.0##", CultureInfo.InvariantCulture);
    }

    /// <summary>螺钉类规格的公称直径（mm）；钻孔 / 销孔就是直径。</summary>
    public double Nominal => double.Parse(Size.TrimStart('M', 'Ø'), CultureInfo.InvariantCulture);

    /// <summary>「x,y,z;x,y,z;…」零件坐标 mm。</summary>
    public static IReadOnlyList<(double X, double Y, double Z)> ParsePoints(string text)
    {
        var points = new List<(double X, double Y, double Z)>();
        foreach (var item in text.Split([';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            points.Add(ModelSpec.Point(item, "at"));
        if (points.Count == 0)
            throw new QuickArgumentException("at= 是空的：写孔心 x,y,z（零件坐标 mm，落在开孔那张面上），多个用分号隔开。");
        for (var i = 0; i < points.Count; i++)
            for (var j = i + 1; j < points.Count; j++)
                if (Distance(points[i], points[j]) < 0.01)
                    throw new QuickArgumentException($"at= 第 {i + 1}、{j + 1} 个孔心重合。");
        return points;
    }

    public static double Distance((double X, double Y, double Z) a, (double X, double Y, double Z) b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

    /// <summary>
    /// <c>IFeatureManager.HoleWizard5</c> 的数值段：Diameter、Depth、Length、Value1–12（米 / 弧度；-1 = 按孔库标准）。
    /// </summary>
    /// <remarks>
    /// 真机：普通孔（钻孔 / 通孔 / 销孔）的近端、远端锥孔值给 -1 就建不出特征，必须给 0；沉头、锥形沉头、螺纹孔给 -1 都行。
    /// 螺纹孔 Value7 装饰螺纹 1 = 带螺纹标注，Value8 螺纹终止 0 = 盲、1 = 贯穿。
    /// </remarks>
    public double[] WizardValues()
    {
        const double k = 0.001;
        var depth = Depth is { } d ? d * k : -1.0;
        var fit = (double)(int)Fit;
        var values = Enumerable.Repeat(-1.0, 15).ToArray();
        values[0] = Diameter is { } dia ? dia * k : Kind.Metric ? -1 : Nominal * k;
        values[1] = depth;
        var v = values.AsSpan(3);
        switch (Kind.Generic)
        {
            case HoleKind.CounterBore:
                v[0] = CounterDiameter is { } cd ? cd * k : -1;
                v[1] = CounterDepth is { } cdep ? cdep * k : -1;
                v[3] = fit;
                break;
            case HoleKind.CounterSink:
                v[0] = CounterDiameter is { } sd ? sd * k : -1;
                v[3] = fit;
                break;
            case HoleKind.Plain:
                v[0] = fit;
                v[2] = v[3] = v[4] = v[5] = v[6] = 0;
                break;
            case HoleKind.Tap:
                v[0] = ThreadDepth is { } td ? td * k : Through ? -1 : Math.Min(2 * Nominal, Depth!.Value) * k;
                v[6] = 1;
                v[7] = Through ? 1 : 0;
                break;
        }

        return values;
    }

    /// <summary>swEndConditions_e：盲孔 0、完全贯穿 1。</summary>
    public int EndCondition => Through ? 1 : 0;

    public string Title => $"{Size} {Kind.Name}{(Through ? "（贯穿）" : $"（深 {ModelSpec.Format(Depth!.Value)}）")}";
}
