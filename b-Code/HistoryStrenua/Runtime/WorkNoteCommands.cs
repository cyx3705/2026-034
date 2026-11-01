using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>笔记区指令 <c>strenua.note.write / read</c>：不碰 SolidWorks，不进执行器。</summary>
internal static class WorkNoteCommands
{
    public static IEnumerable<CommandDescriptor> All(WorkNotes notes)
    {
        var areas = string.Join(" / ", WorkNotes.Areas.Select(area => $"{area}（{WorkNotes.Title(area)}）"));
        yield return new CommandDescriptor
        {
            Name = StrenuaIdentity.Domain + ".note.write",
            Domain = StrenuaIdentity.Domain,
            CommandClass = "note",
            Summary = "往笔记区记一条出图的细节调整或反思（不用开 SolidWorks）；经 MCP 的每条回执最上面都带通用区和本类区。replace= 改或删一条",
            Example = StrenuaIdentity.Domain + ".note.write area=onekey text=钣金先核对板厚有没有标",
            Parameters =
            [
                new ParameterSpec { Name = "area", Description = "分区：" + areas, Required = true, Position = 0 },
                new ParameterSpec { Name = "text", Description = "笔记内容：写具体规律，不写流水账；配合 replace 写 none 是删除", Required = true, Position = 1 },
                new ParameterSpec { Name = "replace", Description = "改第几条（从 1 起，回执里的序号）；省略是新记一条", Required = false, Position = 2 },
            ],
            Handler = CommandDescriptor.Sync(context => Write(notes, context)),
        };

        yield return new CommandDescriptor
        {
            Name = StrenuaIdentity.Domain + ".note.read",
            Domain = StrenuaIdentity.Domain,
            CommandClass = "note",
            Summary = "读笔记区：给 area 读那一区，省略读全部",
            Example = StrenuaIdentity.Domain + ".note.read area=onekey",
            Readonly = true,
            Parameters =
            [
                new ParameterSpec { Name = "area", Description = "分区：" + areas + "；省略读全部", Required = false, Position = 0 },
            ],
            Handler = CommandDescriptor.Sync(context => Read(notes, context.GetString("area"))),
        };
    }

    /// <summary><c>strenua.note.write</c>：回执带写完后的本区，好核对序号。</summary>
    private static CommandResult Write(WorkNotes notes, CommandContext context)
    {
        if (WorkNotes.Area(context.RequireString("area")) is not { } area)
            return CommandResult.Fail("没有这个分区，可写：" + string.Join("、", WorkNotes.Areas));
        var text = context.RequireString("text").Trim();
        if (text.Length == 0)
            return CommandResult.Fail("笔记内容是空的。");
        int? replace = null;
        if (context.GetString("replace")?.Trim() is { Length: > 0 } number)
        {
            if (!int.TryParse(number, out var parsed))
                return CommandResult.Fail($"replace 要写序号：{number}");
            replace = parsed;
        }
        else if (text.Equals(QuickArgs.None, StringComparison.OrdinalIgnoreCase))
        {
            return CommandResult.Fail("删除要带 replace=序号。");
        }

        if (notes.Write(area, text, replace) is { } failure)
            return CommandResult.Fail(failure);
        var section = notes.Section(area);
        var tail = section.Length > WorkNotes.SectionBudget ? $"笔记区已超过 {WorkNotes.SectionBudget} 字，每条回执都带，请合并旧笔记。" : string.Empty;
        return CommandResult.Ok($"已记入「{WorkNotes.Title(area)}」区。{tail}{Environment.NewLine}{section}");
    }

    /// <summary><c>strenua.note.read</c>。</summary>
    private static CommandResult Read(WorkNotes notes, string? area)
    {
        if (string.IsNullOrWhiteSpace(area))
        {
            var all = string.Concat(WorkNotes.Areas.Select(item => notes.Read(item).Count > 0
                ? $"【{WorkNotes.Title(item)}】{Environment.NewLine}" + string.Concat(notes.Read(item).Select((note, index) => $"{index + 1}. {note}{Environment.NewLine}"))
                : string.Empty));
            return CommandResult.Ok(all.Length == 0 ? "笔记区是空的。" : all);
        }

        if (WorkNotes.Area(area) is not { } known)
            return CommandResult.Fail("没有这个分区，可读：" + string.Join("、", WorkNotes.Areas));
        var list = notes.Read(known);
        return CommandResult.Ok(list.Count == 0
            ? $"「{WorkNotes.Title(known)}」区是空的。"
            : string.Concat(list.Select((note, index) => $"{index + 1}. {note}{Environment.NewLine}")));
    }
}
