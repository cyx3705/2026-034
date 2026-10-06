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

/// <summary>AI 给的修改，已按克制的上限裁过（<see cref="TechAiEdit.Apply"/>）。</summary>
/// <param name="Result">改完的技术要求。</param>
/// <param name="Deleted">删掉的条目：原编号与原文。</param>
/// <param name="Added">加上的条目：放在原第几条后面（0 = 最前）与正文。</param>
/// <param name="Dropped">AI 多给、超出上限或不合规（编号不存在、空文、与已有条目重复）没采用的条数。</param>
internal sealed record TechEditOutcome(TechItems Result, IReadOnlyList<(int Number, string Text)> Deleted, IReadOnlyList<(int After, string Text)> Added, int Dropped);

/// <summary>
/// 「AI 填写技术要求」里不碰 SolidWorks、不碰模型的部分：模板拆条、AI 答复读成 JSON、按「克制增删」的上限应用修改。
/// </summary>
/// <remarks>
/// <para>用户定（1.14.0）：选完基础只在它上面<b>克制地增删</b>，别整段重写。所以 AI 只能交回「删第几条」「在第几条后加一条」，
/// 不能交回改写过的全文；已有条目原样保留（连标点都不动），编号由这里重排。每份最多删 <see cref="MaxDelete"/> 条、
/// 加 <see cref="MaxAdd"/> 条，多给的不采用并在回执里说出来——提示词里也写了上限，这里是兜底，不靠模型自觉。</para>
/// <para>没有合适的基础（AI 选了 null）时才由 AI 写全文，也只给条目、最多 <see cref="MaxFresh"/> 条，标题与格式照模板。</para>
/// </remarks>
internal static class TechAiEdit
{
    public const int MaxDelete = 3;

    public const int MaxAdd = 3;

    public const int MaxFresh = 10;

    /// <summary>新加一条最长多少字：技术要求是一行一条，长段落多半是模型在解释而不是在写要求。</summary>
    public const int MaxItemLength = 120;

    private static readonly Regex ItemLine = new(@"^\s*(?<tags>(?:<[^>]*>)*)\s*(?<number>\d+)\s*[、.．]\s*(?<text>.*)$", RegexOptions.Compiled);
    private static readonly Regex LeadingTags = new(@"^\s*((?:<[^>]*>)+)", RegexOptions.Compiled);
    private static readonly Regex LeadingNumber = new(@"^\s*\d+\s*[、.．)）]\s*", RegexOptions.Compiled);

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
    /// 应用修改答复 <c>{"delete":[原编号…], "add":[{"after":原编号, "text":"…"}…], "reason":"…"}</c>；没有基础时读 <c>{"items":["…"]}</c>。
    /// 读不出 JSON 返回 null。
    /// </summary>
    public static (TechEditOutcome Outcome, string Reason)? ReadEdit(string reply, TechItems based, bool fresh)
    {
        if (ReadJson(reply) is not { } json)
            return null;
        var reason = json.TryGetProperty("reason", out var why) && why.ValueKind == JsonValueKind.String ? why.GetString()!.Trim() : string.Empty;
        var deletes = new List<int>();
        var adds = new List<(int After, string Text)>();
        var malformed = 0;
        if (fresh)
        {
            if (json.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        adds.Add((based.Items.Count, item.GetString()!));
                    else
                        malformed++;
                }
            }
        }
        else
        {
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
            {
                foreach (var item in add.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    {
                        var after = item.TryGetProperty("after", out var position) && position.ValueKind == JsonValueKind.Number && position.TryGetInt32(out var value)
                            ? value
                            : based.Items.Count;
                        adds.Add((after, text.GetString()!));
                    }
                    else
                    {
                        malformed++;
                    }
                }
            }
        }

        var outcome = Apply(based, deletes, adds, fresh ? MaxFresh : MaxAdd);
        return (outcome with { Dropped = outcome.Dropped + malformed }, reason);
    }

    /// <summary>
    /// 按原编号删、按原编号插：编号都指<b>基础原来的</b>第几条，所以先删后加互不干扰。删不存在的编号、超上限的、
    /// 空的或与已有条目重复的新条目都不采用（计入 <see cref="TechEditOutcome.Dropped"/>）。
    /// </summary>
    public static TechEditOutcome Apply(TechItems based, IReadOnlyList<int> deletes, IReadOnlyList<(int After, string Text)> adds, int maxAdd = MaxAdd)
    {
        var dropped = 0;
        var deleted = new SortedSet<int>();
        foreach (var number in deletes)
        {
            if (number < 1 || number > based.Items.Count || deleted.Contains(number) || deleted.Count >= MaxDelete)
                dropped++;
            else
                deleted.Add(number);
        }

        var kept = based.Items.Where((_, index) => !deleted.Contains(index + 1)).Select(Normalize).ToHashSet(StringComparer.Ordinal);
        var added = new List<(int After, string Text)>();
        foreach (var (after, raw) in adds)
        {
            var text = Clean(raw);
            if (text.Length == 0 || added.Count >= maxAdd || kept.Contains(Normalize(text)))
            {
                dropped++;
                continue;
            }

            kept.Add(Normalize(text));
            added.Add((Math.Clamp(after, 0, based.Items.Count), text));
        }

        // 原第 n 条之后插 after=n 的新条目（同一处按 AI 给的先后）；after=0 的放最前。
        var items = new List<string>();
        void Insert(int after) => items.AddRange(added.Where(item => item.After == after).Select(item => item.Text));
        Insert(0);
        for (var number = 1; number <= based.Items.Count; number++)
        {
            if (!deleted.Contains(number))
                items.Add(based.Items[number - 1]);
            Insert(number);
        }

        return new TechEditOutcome(
            based with { Items = items },
            deleted.Select(number => (number, based.Items[number - 1])).ToList(),
            added,
            dropped);
    }

    /// <summary>基础条目编成「编号：正文」一行一条，交给模型。</summary>
    public static IReadOnlyList<string> Numbered(TechItems items)
        => items.Items.Select((item, index) => $"{index + 1}：{Plain(item)}").ToList();

    /// <summary>去掉格式标记、换行压成空格，给人和模型看。</summary>
    public static string Plain(string item)
        => string.Join(" ", Regex.Replace(item, "<[^>]*>", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>新条目：去掉模型自带的编号、换行与格式标记，截到 <see cref="MaxItemLength"/> 字。</summary>
    private static string Clean(string text)
    {
        var plain = LeadingNumber.Replace(Plain(text), string.Empty).Trim();
        return plain.Length <= MaxItemLength ? plain : plain[..MaxItemLength];
    }

    /// <summary>比重复用：去掉空白与句末标点。</summary>
    private static string Normalize(string text)
        => new string(Plain(text).Where(c => !char.IsWhiteSpace(c)).ToArray()).TrimEnd('；', ';', '。', '.', '，', ',');
}
