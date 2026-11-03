using System.Globalization;
using System.Text.RegularExpressions;

namespace HistoryStrenua;

/// <summary>
/// 螺纹孔标注（1.38.0「螺纹孔」<c>strenua.dim.thread</c>）：模型里螺纹孔画成底孔时，把孔标注改写成螺纹的写法。
/// </summary>
/// <remarks>
/// 写法（我定，按 GB/T 4458.4 常见标法）：盲螺纹两行「4 x M6↧12」「孔↧15」；螺纹贯穿一行「4 x M6 贯穿」；底孔贯穿而螺纹不到底只写第一行。
/// 底孔直径由螺纹规格定，不写。规格和图上底孔直径对不上（粗牙按攻丝底孔表，细牙按 大径 − 螺距）只提醒，不拦：模型可能故意画成别的。
/// </remarks>
internal sealed record ThreadSpec(string Text, double Major, double Pitch)
{
    /// <summary>粗牙螺距与攻丝底孔（mm）。</summary>
    private static readonly IReadOnlyDictionary<double, (double Pitch, double Drill)> Coarse = new Dictionary<double, (double, double)>
    {
        [1.6] = (0.35, 1.25), [2] = (0.4, 1.6), [2.5] = (0.45, 2.05), [3] = (0.5, 2.5), [4] = (0.7, 3.3), [5] = (0.8, 4.2),
        [6] = (1, 5), [8] = (1.25, 6.8), [10] = (1.5, 8.5), [12] = (1.75, 10.2), [14] = (2, 12), [16] = (2, 14),
        [18] = (2.5, 15.5), [20] = (2.5, 17.5), [22] = (2.5, 19.5), [24] = (3, 21), [27] = (3, 24), [30] = (3.5, 26.5),
    };

    private static readonly Regex Pattern = new(@"^M(\d+(?:\.\d+)?)(?:\s*[xX×]\s*(\d+(?:\.\d+)?))?(-\w+)?$", RegexOptions.Compiled);

    /// <summary>攻丝底孔直径（mm）。</summary>
    public double Drill => Coarse.TryGetValue(Major, out var coarse) && Math.Abs(coarse.Pitch - Pitch) < 1e-6 ? coarse.Drill : Math.Round(Major - Pitch, 2);

    /// <summary>解析「M6」「M6x0.75」「M6-6H」；不认识的写法抛参数错。</summary>
    public static ThreadSpec Parse(string text)
    {
        var trimmed = text.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        var match = Pattern.Match(trimmed.ToUpperInvariant());
        if (!match.Success)
            throw new QuickArgumentException($"spec={text} 不是螺纹规格：写 M6、M6x0.75 或 M6-6H。");
        var major = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        double pitch;
        if (match.Groups[2].Success)
            pitch = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        else if (Coarse.TryGetValue(major, out var coarse))
            pitch = coarse.Pitch;
        else
            throw new QuickArgumentException($"spec={text}：M{Format(major)} 不在粗牙表里，写上螺距（如 M{Format(major)}x1）。");
        // 显示保留用户的写法（含公差带），只把乘号统一成 x。
        var shown = "M" + match.Groups[1].Value + (match.Groups[2].Success ? "x" + match.Groups[2].Value : string.Empty) + match.Groups[3].Value;
        return new ThreadSpec(shown, major, pitch);
    }

    /// <summary>
    /// 孔标注两行：上一行「N x M6↧12」或「N x M6 贯穿」，下一行「孔↧15」或空。
    /// </summary>
    /// <param name="count">孔数；1 时不写「1 x」。</param>
    /// <param name="threadDepth">螺纹深（mm）；null 表示螺纹贯穿。</param>
    /// <param name="drillDepth">底孔深（mm）；null 表示底孔贯穿。</param>
    public (string Above, string Below) Lines(int count, double? threadDepth, double? drillDepth)
    {
        var head = (count > 1 ? $"{count} x " : string.Empty) + Text;
        if (threadDepth is not { } thread)
            return (head + " 贯穿", string.Empty);
        var below = drillDepth is { } drill && drill > thread + 1e-6 ? "孔" + SwText.Depth + Format(drill) : string.Empty;
        return (head + SwText.Depth + Format(thread), below);
    }

    /// <summary>图上底孔直径和规格对得上（差不到 0.15 mm）。</summary>
    public bool Fits(double drillDiameter) => Math.Abs(drillDiameter - Drill) <= 0.15;

    private static readonly Regex DepthAfterSymbol = new(@"<HOLE-DEPTH>\s*(\d+(?:\.\d+)?)", RegexOptions.Compiled);
    private static readonly Regex Count = new(@"^\s*(\d+)\s*[xX×]", RegexOptions.Compiled);

    private static readonly Regex AlreadyThread = new(@"\bM\d", RegexOptions.Compiled);

    /// <summary>
    /// 从孔标注原来的文字读底孔深度；没有「↧数」（贯穿）为 null。普通孔标注取第一个「↧数」；
    /// 已经写成螺纹的（再改一次）第一个是螺纹深，底孔深是最后一个，只有一个就是底孔贯穿。
    /// </summary>
    public static double? ReadDrillDepth(string text)
    {
        var matches = DepthAfterSymbol.Matches(SwText.Tokens(text));
        if (matches.Count == 0)
            return null;
        var thread = AlreadyThread.IsMatch(text);
        if (thread && matches.Count == 1)
            return null;
        return double.Parse(matches[thread ? matches.Count - 1 : 0].Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>从孔标注原来的文字读「N x」；没有为 1。</summary>
    public static int ReadCount(string text)
        => Count.Match(text) is { Success: true } match ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 1;

    public static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
