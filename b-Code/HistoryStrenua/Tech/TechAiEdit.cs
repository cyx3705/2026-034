using System.Text.Json;
using System.Text.RegularExpressions;

namespace HistoryStrenua;

/// <summary>
/// 一份技术要求拆成条目（1.14.0「AI 填写技术要求」）：标题行、第一条前面的 SolidWorks 段落格式标记、各条正文（不带编号）。
/// </summary>
/// <param name="Heading">标题行原文（模板里是缩进的「技术要求」）。</param>
/// <param name="Prefix">第一条前面的格式标记（如 <c>&lt;PARA indent=0 …&gt;</c>），没有为空。</param>
/// <param name="Items">各条正文，不带「1、」编号。</param>
internal sealed record TechItems(string Heading, string Prefix, IReadOnlyList<string> Items)
{
    /// <summary>没有模板做基础时用的标题与格式（与「机加件」等模板一致）。</summary>
    public static TechItems Empty { get; } = new(
        "        技术要求",
        "<PARA  indent=0 findent=0 indentStep=10 number=off bullet=off paraSpace=0.001 lineSpace=0.001>",
        []);

    /// <summary>拼回交给 <c>InsertNote</c> 的全文：标题、格式标记、按顺序重新编号的各条。</summary>
    public string Text => Heading + "\n" + Prefix + string.Join("\n", Items.Select((item, index) => $"{index + 1}、{item}"));
}

/// <summary>AI 给的删除建议及结果（<see cref="TechAiEdit.Apply"/>）。</summary>
/// <param name="Result">改完的技术要求。</param>
/// <param name="Deleted">删掉的条目：原编号与原文。</param>
/// <param name="Dropped">AI 给出的新增内容、超出上限的删除编号或不合法编号，没有采用的数量。</param>
internal sealed record TechEditOutcome(TechItems Result, IReadOnlyList<(int Number, string Text)> Deleted, int Dropped);

/// <summary>
/// 「AI 填写技术要求」里不碰 SolidWorks、不碰模型的部分：模板拆条、AI 答复读成 JSON、只应用删除建议。
/// </summary>
/// <remarks>
/// <para>1.18.0：AI 只能交回「删第几条」；已有条目原样保留（连标点都不动），编号由这里重排。最多删 <see cref="MaxDelete"/> 条。</para>
/// <para>1.18.0：AI 只能从现有模板选择基础并删除条目；新增字段一律不采用。</para>
/// </remarks>
internal static class TechAiEdit
{
    public const int MaxDelete = 3;

    private static readonly Regex ItemLine = new(@"^\s*(?<tags>(?:<[^>]*>)*)\s*(?<number>\d+)\s*[、.．]\s*(?<text>.*)$", RegexOptions.Compiled);
    private static readonly Regex LeadingTags = new(@"^\s*((?:<[^>]*>)+)", RegexOptions.Compiled);

    /// <summary>
    /// 把模板全文拆成条目：第一行（含「技术要求」的那行）是标题；之后以「数字、」开头的行各起一条，其余行接到上一条后面（换行保留）。
    /// 第一条前面的格式标记单独留着，拼回时放回原处。
    /// </summary>
    public static TechItems Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var start = Array.FindIndex(lines, line => line.Contains("技术要求", StringComparison.Ordinal));
        var heading = start >= 0 ? lines[start] : TechItems.Empty.Heading;
        var prefix = string.Empty;
        var items = new List<string>();
        foreach (var line in lines.Skip(start + 1))
        {
            var match = ItemLine.Match(line);
            if (match.Success)
            {
                if (items.Count == 0)
                    prefix = match.Groups["tags"].Value;
                items.Add(match.Groups["text"].Value.TrimEnd());
            }
            else if (line.Trim().Length > 0)
            {
                if (items.Count == 0)
                {
                    // 第一条之前单独一行的格式标记。
                    prefix += LeadingTags.Match(line) is { Success: true } tags ? tags.Groups[1].Value : string.Empty;
                    continue;
                }

                items[^1] += "\n" + line.TrimEnd();
            }
        }

        return new TechItems(heading, prefix, items);
    }

    /// <summary>
    /// 从模型答复里取 JSON 对象：答复可能带 <c>```json</c> 围栏、前后说明或用量脚注，取第一个「{」到最后一个「}」。取不到返回 null。
    /// </summary>
    public static JsonElement? ReadJson(string reply)
    {
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;
        try
        {
            using var document = JsonDocument.Parse(reply[start..(end + 1)]);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>选基础的答复：<c>{"base": "模板名" | null, "reason": "…"}</c>。模板名不在候选里视为答错（返回 Valid=false）。</summary>
    public static (bool Valid, string? Base, string Reason) ReadChoice(string reply, IReadOnlyCollection<string> candidates)
    {
        if (ReadJson(reply) is not { } json || !json.TryGetProperty("base", out var choice))
            return (false, null, string.Empty);
        var reason = json.TryGetProperty("reason", out var why) && why.ValueKind == JsonValueKind.String ? why.GetString()!.Trim() : string.Empty;
        if (choice.ValueKind == JsonValueKind.Null)
            return (true, null, reason);
        if (choice.ValueKind != JsonValueKind.String)
            return (false, null, reason);
        var name = choice.GetString()!.Trim().Trim('「', '」', '"');
        if (name.Length == 0 || name.Equals("null", StringComparison.OrdinalIgnoreCase) || name == "无")
            return (true, null, reason);
        var matched = candidates.FirstOrDefault(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
        return matched is null ? (false, name, reason) : (true, matched, reason);
    }

    /// <summary>
    /// 应用修改答复 <c>{"delete":[原编号…], "reason":"…"}</c>。模型给出的新增内容不采用。
    /// 读不出 JSON 返回 null。
    /// </summary>
    public static (TechEditOutcome Outcome, string Reason)? ReadEdit(string reply, TechItems based)
    {
        if (ReadJson(reply) is not { } json)
            return null;
        var reason = json.TryGetProperty("reason", out var why) && why.ValueKind == JsonValueKind.String ? why.GetString()!.Trim() : string.Empty;
        var deletes = new List<int>();
        var malformed = 0;
        if (json.TryGetProperty("delete", out var delete) && delete.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in delete.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var number))
                    deletes.Add(number);
                else if (item.ValueKind == JsonValueKind.String && int.TryParse(item.GetString(), out var parsed))
                    deletes.Add(parsed);
                else
                    malformed++;
            }
        }

        if (json.TryGetProperty("add", out var add) && add.ValueKind == JsonValueKind.Array)
            malformed += add.GetArrayLength();
        if (json.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            malformed += items.GetArrayLength();
        var outcome = Apply(based, deletes, []);
        return (outcome with { Dropped = outcome.Dropped + malformed }, reason);
    }

    /// <summary>
    /// 只按原编号删除；新增条目参数保留兼容测试接口，但永不采用。
    /// </summary>
    public static TechEditOutcome Apply(TechItems based, IReadOnlyList<int> deletes, IReadOnlyList<(int After, string Text)> adds)
    {
        var dropped = adds.Count;
        var deleted = new SortedSet<int>();
        foreach (var number in deletes)
        {
            if (number < 1 || number > based.Items.Count || deleted.Contains(number) || deleted.Count >= MaxDelete)
                dropped++;
            else
                deleted.Add(number);
        }

        var items = based.Items.Where((_, index) => !deleted.Contains(index + 1)).ToList();

        return new TechEditOutcome(
            based with { Items = items },
            deleted.Select(number => (number, based.Items[number - 1])).ToList(),
            dropped);
    }

    /// <summary>基础条目编成「编号：正文」一行一条，交给模型。</summary>
    public static IReadOnlyList<string> Numbered(TechItems items)
        => items.Items.Select((item, index) => $"{index + 1}：{Plain(item)}").ToList();

    /// <summary>去掉格式标记、换行压成空格，给人和模型看。</summary>
    public static string Plain(string item)
        => string.Join(" ", Regex.Replace(item, "<[^>]*>", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

}
