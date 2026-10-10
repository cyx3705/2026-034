using System.Text.Json;
using System.Text.RegularExpressions;

namespace HistoryStrenua;

/// <summary>一键类按零件类别出图（1.17.0，用户定）：钣金、框架（铝型材骨架）、加工件（其他情况）。</summary>
internal enum PartKind
{
    /// <summary>钣金：标孔（含方形槽）与外轮廓，折弯处的线不标、不当基准。</summary>
    SheetMetal,

    /// <summary>框架：只标外轮廓，轴测图上写「采用 xx 铝型材」。</summary>
    Frame,

    /// <summary>加工件：其他情况，即 1.16.0 的「一键出图」全套。</summary>
    Machined,
}

/// <summary>
/// 零件类别的纯逻辑部分（1.17.0）：读 AI 的答复、AI 没成时按特征树猜、型材规格怎么写。
/// </summary>
internal static partial class PartKindPlanner
{
    /// <summary>类别在页面、提示词、回执里的叫法。</summary>
    public static string Title(PartKind kind) => kind switch
    {
        PartKind.SheetMetal => "钣金",
        PartKind.Frame => "框架",
        _ => "加工件",
    };

    /// <summary>提示词里的候选（顺序即说明顺序）。</summary>
    public static IReadOnlyList<string> Titles { get; } = [Title(PartKind.SheetMetal), Title(PartKind.Frame), Title(PartKind.Machined)];

    /// <summary>
    /// AI 的答复 <c>{"kind": "钣金|框架|加工件", "reason": "…"}</c> → 类别与理由；读不出、不是三者之一返回 null。
    /// 宽松认几个常见说法（「铝型材框架」「机加件」「钣金件」）：模型偶尔不照抄候选原文。
    /// </summary>
    public static (PartKind Kind, string Reason)? Read(string reply)
    {
        if (TechAiEdit.ReadJson(reply) is not { } json || !json.TryGetProperty("kind", out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        var reason = json.TryGetProperty("reason", out var why) && why.ValueKind == JsonValueKind.String ? why.GetString()!.Trim() : string.Empty;
        var text = value.GetString()!.Trim();
        PartKind? kind = text switch
        {
            _ when text.Contains("钣金", StringComparison.Ordinal) => PartKind.SheetMetal,
            _ when text.Contains("框架", StringComparison.Ordinal) || text.Contains("型材", StringComparison.Ordinal) => PartKind.Frame,
            _ when text.Contains("加工", StringComparison.Ordinal) || text.Contains("机加", StringComparison.Ordinal) => PartKind.Machined,
            _ => null,
        };
        return kind is { } k ? (k, reason) : null;
    }

    /// <summary>
    /// AI 没成时的退路（也写进提示词给 AI 参考）：按图纸信息 JSON（<see cref="TechAiInfo.Read"/>）里模型的特征类型与材料、文件名猜。
    /// 特征树里有钣金特征（<c>SheetMetal</c>）→ 钣金；有焊件结构构件（<c>WeldMemberFeat</c>）、或文件名带「型材 / 骨架」且材料是铝 → 框架；其余加工件。
    /// </summary>
    public static PartKind Guess(string infoJson)
    {
        try
        {
            using var document = JsonDocument.Parse(infoJson);
            if (!document.RootElement.TryGetProperty("模型", out var model))
                return PartKind.Machined;
            var features = model.TryGetProperty("特征类型计数", out var counts) && counts.ValueKind == JsonValueKind.Object
                ? counts.EnumerateObject().Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : [];
            if (features.Contains("SheetMetal"))
                return PartKind.SheetMetal;
            if (features.Contains("WeldMemberFeat"))
                return PartKind.Frame;
            var name = model.TryGetProperty("文件", out var file) ? file.GetString() ?? string.Empty : string.Empty;
            var material = model.TryGetProperty("材料", out var stuff) ? stuff.GetString() ?? string.Empty : string.Empty;
            var aluminum = material.Contains('铝') || material.StartsWith("6061", StringComparison.Ordinal) || material.StartsWith("6063", StringComparison.Ordinal);
            return aluminum && (name.Contains("型材", StringComparison.Ordinal) || name.Contains("骨架", StringComparison.Ordinal))
                ? PartKind.Frame
                : PartKind.Machined;
        }
        catch (JsonException)
        {
            return PartKind.Machined;
        }
    }

    /// <summary>
    /// 焊件型材文件名 → 规格（「采用 xx 铝型材」的 xx）：「20x20」「20×20」写成「2020」，别的（「2020欧标」「4040」）原样，
    /// 去掉扩展名与末尾的「铝型材」「型材」。空名返回空串。
    /// </summary>
    public static string ProfileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path ?? string.Empty).Trim();
        if (name.Length == 0)
            return string.Empty;
        if (SizePair().Match(name) is { Success: true } pair)
            name = pair.Groups[1].Value + pair.Groups[2].Value + name[(pair.Index + pair.Length)..].Trim();
        foreach (var tail in new[] { "铝型材", "型材" })
        {
            if (name.EndsWith(tail, StringComparison.Ordinal))
            {
                name = name[..^tail.Length].Trim();
                break;
            }
        }

        return name;
    }

    [GeneratedRegex(@"^(\d+)\s*[xX×*]\s*(\d+)")]
    private static partial Regex SizePair();

    /// <summary>
    /// 没有焊件型材文件时按实体包围盒猜规格：每根型材一个实体，包围盒最小的两个边长就是截面（毫米取整）；按出现次数排，
    /// 写成「2020」「4040」，几种就用「、」连起来。截面两边差三倍以上（不是方型材）、边长超过 200 mm（不像一根型材）的不算。
    /// </summary>
    /// <param name="boxes">每个实体的包围盒三个边长（米）。</param>
    public static string ProfileFromBodies(IEnumerable<(double A, double B, double C)> boxes)
    {
        var sections = new List<(int A, int B)>();
        foreach (var (a, b, c) in boxes)
        {
            var sides = new[] { a, b, c }.Order().Select(v => (int)Math.Round(v * 1000)).ToArray();
            var (small, middle) = (sides[0], sides[1]);
            if (small <= 0 || middle > 200 || middle > small * 3)
                continue;
            sections.Add((small, middle));
        }

        return string.Join("、", sections.GroupBy(section => section)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key.A)
            .Select(group => $"{group.Key.A}{group.Key.B}"));
    }

    /// <summary>规格名开头的截面宽（「2020」「2020欧标」→ 20 mm、「4080」→ 40 mm，米）；读不出返回 0。外轮廓筛站用（<see cref="OutlinePlanner.Sparse"/>）。</summary>
    public static double ProfileWidth(string name)
    {
        var digits = new string((name ?? string.Empty).TakeWhile(char.IsDigit).ToArray());
        if (digits.Length is not (4 or 6))
            return 0;
        var half = digits.Length / 2;
        return Math.Min(int.Parse(digits[..half]), int.Parse(digits[half..])) / 1000.0;
    }

    /// <summary>轴测图上的说明文字：「采用2020铝型材」（用户样图「采用2020欧标铝型材」：规格名里带「欧标」就原样带上）。</summary>
    public static string ProfileNote(string name) => name.Length == 0 ? "采用铝型材" : $"采用{name}铝型材";
}
