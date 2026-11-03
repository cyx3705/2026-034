namespace HistoryStrenua;

/// <summary>
/// 写进尺寸、孔标注、注释的文字里的特殊符号（1.38.0）：一律换成 SolidWorks 的符号码。
/// </summary>
/// <remarks>
/// 真机（2026-043 货叉托板）：前缀里直接写 Unicode「↧」「Ø」，SolidWorks 用正文字体画，深度符号画成乱码、直径符号和数字挤在一起；
/// 换成 <c>&lt;HOLE-DEPTH&gt;</c>、<c>&lt;MOD-DIAM&gt;</c> 后由符号字体画，和 SolidWorks 自己生成的孔标注一模一样。
/// 所以凡是 AI 或人给的文字，写进图之前都过一遍 <see cref="Tokens"/>。
/// </remarks>
internal static class SwText
{
    public const string Diameter = "<MOD-DIAM>";
    public const string Depth = "<HOLE-DEPTH>";
    public const string Counterbore = "<HOLE-SPOT>";
    public const string Countersink = "<HOLE-SINK>";

    private static readonly (string From, string To)[] Symbols =
    [
        ("⌀", Diameter), ("Ø", Diameter), ("ø", Diameter), ("Φ", Diameter), ("φ", Diameter),
        ("↧", Depth), ("⌴", Counterbore), ("⌵", Countersink),
        ("±", "<MOD-PM>"), ("°", "<MOD-DEG>"),
    ];

    /// <summary>把 Unicode 符号换成符号码；没有符号原样返回。</summary>
    public static string Tokens(string text)
    {
        foreach (var (from, to) in Symbols)
            text = text.Replace(from, to, StringComparison.Ordinal);
        return text;
    }

    /// <summary>符号码换回可读的 Unicode（回执、SVG 用）。</summary>
    public static string Readable(string text)
        => text.Replace(Diameter, "Ø", StringComparison.Ordinal).Replace(Depth, "↧", StringComparison.Ordinal)
            .Replace(Counterbore, "⌴", StringComparison.Ordinal).Replace(Countersink, "⌵", StringComparison.Ordinal)
            .Replace("<MOD-PM>", "±", StringComparison.Ordinal).Replace("<MOD-DEG>", "°", StringComparison.Ordinal);
}
