using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 技术要求逐条增删（1.34.0，用户定：模板里不适用的条目要能单独去掉）：<c>strenua.tech.list / add / remove</c>。
/// 读图上那份注释、拆条、改完重新编号，再走 <see cref="TechApply.Insert"/> 原处替换。挂在文档类下不上页面，是给 AI 用的。
/// </summary>
internal static class TechLine
{
    public static QuickCommand ListCommand { get; } = new(
        Key: "tech-list",
        CommandName: StrenuaIdentity.Domain + ".tech.list",
        Title: "技术要求条目",
        Summary: "列出当前工程图技术要求的各条：编号与正文；编号供增删条目用。",
        Usage: "不用点视图，读图框里那份技术要求。不改图。",
        Run: List,
        Readonly: true,
        Page: "doc",
        SelfReport: true);

    public static QuickCommand RemoveCommand { get; } = new(
        Key: "tech-remove",
        CommandName: StrenuaIdentity.Domain + ".tech.remove",
        Title: "删技术要求条目",
        Summary: "按编号删当前工程图技术要求的一条（可多条），其余重新编号，原处替换。",
        Usage: "编号看 strenua.tech.list。几条用逗号分开。含「未注」的条目是未标尺寸检查的依据，删了要自己补标。不保存。",
        Run: QuickCommand.Unbound,
        Parameters: [new QuickParameter("number", "编号", "条目编号，几条用逗号分开，如 9 或 7,9", true)],
        Example: StrenuaIdentity.Domain + ".tech.remove number=9",
        Bind: arguments =>
        {
            var numbers = QuickArgs.Names(QuickArgs.Name(arguments, "number"), "number").Select(text => Number(text, "number")).ToList();
            return context => Remove(context, numbers);
        },
        Page: "doc",
        SelfReport: true);

    public static QuickCommand AddCommand { get; } = new(
        Key: "tech-add",
        CommandName: StrenuaIdentity.Domain + ".tech.add",
        Title: "加技术要求条目",
        Summary: "给当前工程图技术要求加一条，默认加在最后，其余重新编号，原处替换。",
        Usage: "正文不用写编号。after 填加在第几条后面，0 是最前面，省略加在最后。不保存。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("text", "正文", "条目正文，不带编号", true),
            new QuickParameter("after", "位置", "加在第几条后面，0 为最前；省略加在最后", false),
        ],
        Example: StrenuaIdentity.Domain + ".tech.add text=表面喷砂",
        Bind: arguments =>
        {
            var text = QuickArgs.Name(arguments, "text");
            var after = QuickArgs.Optional(arguments, "after") is { } value ? Number(value, "after") : (int?)null;
            return context => Add(context, text, after);
        },
        Page: "doc",
        SelfReport: true);

    /// <summary>删掉这些编号的条目；编号不对返回 null。</summary>
    internal static TechItems? Without(TechItems items, IReadOnlyCollection<int> numbers)
        => numbers.Count == 0 || numbers.Any(number => number < 1 || number > items.Items.Count)
            ? null
            : items with { Items = items.Items.Where((_, index) => !numbers.Contains(index + 1)).ToList() };

    /// <summary>在第 <paramref name="after"/> 条后面加一条（null 加在最后）；位置不对返回 null。</summary>
    internal static TechItems? With(TechItems items, string text, int? after)
    {
        var at = after ?? items.Items.Count;
        if (at < 0 || at > items.Items.Count)
            return null;
        var list = items.Items.ToList();
        list.Insert(at, text.Trim());
        return items with { Items = list };
    }

    private static int Number(string text, string parameter)
        => int.TryParse(text.Trim(), out var number) ? number : throw new QuickArgumentException($"{parameter} 要填数字：{text}");

    private static (object Drawing, TechItems Items)? Read(QuickCommandContext context)
    {
        var drawing = DrawingAccess.ActiveDrawing(context);
        var note = TechNotePlacement.Notes(context.Api, drawing).FirstOrDefault();
        return note is null ? null : (drawing, TechAiEdit.Parse(context.Api.CallString(note.Note, "INote", "GetText")));
    }

    private const string NoNote = "技术要求：这张图上没有技术要求，先用 strenua.tech.apply 插一份。";

    private static QuickOutcome List(QuickCommandContext context)
    {
        if (Read(context) is not { } found)
            return QuickOutcome.Fail(NoNote);
        return QuickOutcome.Ok($"技术要求：共 {found.Items.Items.Count} 条。{Environment.NewLine}" + string.Join(Environment.NewLine, TechAiEdit.Numbered(found.Items)));
    }

    private static QuickOutcome Remove(QuickCommandContext context, IReadOnlyList<int> numbers)
    {
        if (Read(context) is not { } found)
            return QuickOutcome.Fail(NoNote);
        if (Without(found.Items, numbers) is not { } result)
            return QuickOutcome.Fail($"技术要求：编号要在 1–{found.Items.Items.Count} 之间。");
        var removed = numbers.Order().Select(number => $"第 {number} 条「{TechAiEdit.Plain(found.Items.Items[number - 1])}」");
        return TechApply.Insert(context, found.Drawing, "改过的技术要求", result.Text, result.Items.Count, "删了 " + string.Join("、", removed));
    }

    private static QuickOutcome Add(QuickCommandContext context, string text, int? after)
    {
        if (Read(context) is not { } found)
            return QuickOutcome.Fail(NoNote);
        if (With(found.Items, text, after) is not { } result)
            return QuickOutcome.Fail($"技术要求：位置要在 0–{found.Items.Items.Count} 之间。");
        return TechApply.Insert(context, found.Drawing, "改过的技术要求", result.Text, result.Items.Count, $"加了「{text.Trim()}」");
    }
}
