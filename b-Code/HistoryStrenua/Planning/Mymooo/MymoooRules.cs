using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace HistoryStrenua;

/// <summary>
/// 蚂蚁工场在线选型（1.37.0）的纯规则：料号、参数配对、选项匹配、文件名。不联网、不碰 SolidWorks。
/// </summary>
/// <remarks>
/// 接口取自蚂蚁工场 SolidWorks 在线版插件（反编译），2026-11-02 实测全链路不要登录。
/// 选型是逐步收窄的：每选一个参数回传一次，料号里还没定的段写成 <c>&lt;span&gt;[3,4,5…]&lt;/span&gt;</c>，
/// 区间参数写成 <c>[15-800/1]</c>（下限-上限/步长，只有一个 optionId 为 0 的选项，值原样填进 SelectData，超出区间服务器静默不收）。
/// </remarks>
internal static partial class MymoooRules
{
    /// <summary>下载的标准件放在总装目录下的这个子文件夹（同 HistoryMinerva 改名时外购件的去处）。</summary>
    public const string Folder = "外购件";

    /// <summary>参数类型「编码参数」：组成料号的那几个，选型就是把它们选定。</summary>
    public const string CodeParameter = "M";

    /// <summary>料号已经定成一个（不再有方括号待选段）。</summary>
    public static bool Complete(string? number)
        => !string.IsNullOrWhiteSpace(number) && number.IndexOfAny(['<', '[']) < 0;

    /// <summary>去掉料号里的 span 标签，留方括号待选段给人读。</summary>
    public static string Plain(string? number)
        => SpanTag().Replace(number ?? string.Empty, string.Empty);

    /// <summary>
    /// <c>M=8,L=20</c> 这样的一串配对（逗号、分号、中文逗号分号分开），保持顺序；写了但不是 名=值 抛错。
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Pairs(string? text, string parameter)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var item in (text ?? string.Empty).Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cut = item.IndexOfAny(['=', '＝']);
            if (cut <= 0 || cut == item.Length - 1)
                throw new QuickArgumentException($"{parameter} 要写成 名=值，逗号分开（如 M=8,L=20）：{item}");
            pairs.Add(new(item[..cut].Trim(), item[(cut + 1)..].Trim()));
        }

        return pairs;
    }

    /// <summary>料号当文件名：Windows 不许的字符换成下划线。</summary>
    public static string FileName(string number)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(number.Trim().Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
    }

    /// <summary>按参数码（不分大小写）或参数名找参数。</summary>
    public static JsonNode? Parameter(JsonArray parameters, string key)
        => parameters.FirstOrDefault(p => string.Equals(Text(p, "paramCode"), key, StringComparison.OrdinalIgnoreCase))
           ?? parameters.FirstOrDefault(p => Text(p, "paramName") == key);

    /// <summary>区间参数（只有一个选项且带上限）的 下限、上限。</summary>
    public static (double Min, double Max)? Range(JsonNode parameter)
    {
        if (parameter["paramValues"] is not JsonArray { Count: 1 } values
            || Number(Text(values[0], "optionValue")) is not { } min
            || Number(Text(values[0], "toValue")) is not { } max)
            return null;
        return (min, max);
    }

    /// <summary>
    /// 填进 SelectData 的值：枚举按显示值或文字匹配（数值按大小比，「8」认「8.0」），给 optionId；区间参数原样给数。
    /// 都对不上返回 null。
    /// </summary>
    public static string? SelectData(JsonNode parameter, string value)
    {
        if (Range(parameter) is { } range)
            return Number(value) is { } number && number >= range.Min && number <= range.Max ? value.Trim() : null;
        var values = parameter["paramValues"] as JsonArray ?? [];
        var option = values.FirstOrDefault(v => Text(v, "optionValue") == value || Text(v, "optionText") == value)
                     ?? values.FirstOrDefault(v => Number(value) is { } wanted && Number(Text(v, "optionValue")) == wanted);
        return option?["optionId"]?.ToString();
    }

    /// <summary>参数的可选值，给人读：区间写「15~800」，枚举最多列 <paramref name="limit"/> 个。</summary>
    public static string Choices(JsonNode parameter, int limit = 40)
    {
        if (Range(parameter) is { } range)
            return $"{range.Min.ToString(CultureInfo.InvariantCulture)}~{range.Max.ToString(CultureInfo.InvariantCulture)}（区间，直接填数）";
        var values = (parameter["paramValues"] as JsonArray ?? []).Select(v => Text(v, "optionValue")).Where(v => v.Length > 0).ToList();
        return values.Count <= limit ? string.Join(" / ", values) : string.Join(" / ", values.Take(limit)) + $" …共 {values.Count} 个";
    }

    /// <summary>参数「码 名称」，名称与码相同只写一个。</summary>
    public static string Label(JsonNode parameter)
    {
        var code = Text(parameter, "paramCode");
        var name = Text(parameter, "paramName");
        return name.Length == 0 || name == code ? code : $"{code} {name}";
    }

    /// <summary>JSON 里的字符串字段，没有为空串。</summary>
    public static string Text(JsonNode? node, string field)
        => node?[field] is JsonValue value ? value.ToString() : string.Empty;

    /// <summary>商品说明里的 &lt;br /&gt; 换成「；」。</summary>
    public static string Description(string text)
        => BreakTag().Replace(text, "；").Trim('；', ' ');

    private static double? Number(string text)
        => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    [GeneratedRegex("</?span>", RegexOptions.IgnoreCase)]
    private static partial Regex SpanTag();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakTag();
}
