namespace HistoryStrenua;

/// <summary>
/// 「技术要求」类的模板表格（1.13.0，用户定）：点一行就把那份技术要求插进当前工程图；图框里已有技术要求就先删掉，在原来那一处放新的。
/// 放哪只许标题栏上方或左侧，「避障」开时压到视图就挪视图让地方（<see cref="TechNotePlacement"/>）。
/// </summary>
/// <remarks>
/// 带参数（模板名），所以不进 <see cref="QuickCommands.All"/>（那里是无参按钮）；指令 <see cref="CommandName"/> 单独注册，
/// 每次按名字现做一条 <see cref="QuickCommand"/> 交给同一个执行器——仍是一次只跑一条、可取消。
/// </remarks>
internal static class TechApply
{
    public const string Key = "tech-apply";

    public const string CommandName = StrenuaIdentity.Domain + ".tech.apply";

    /// <summary>表格「技术要求」列的单元格动作。</summary>
    public const string ActionId = CommandName;

    public const string Summary = "把「通用技术要求」里的一份插进当前工程图（标题栏上方或左侧）；图框里已有技术要求就删掉、在原处换成这份，避障开时挪视图让地方。";

    public static QuickCommand Command(string name) => new(
        Key: Key,
        CommandName: CommandName,
        Title: $"技术要求「{name}」",
        Summary: Summary,
        Usage: Summary,
        Run: context => Run(context, name));

    internal static QuickOutcome Run(QuickCommandContext context, string name)
    {
        var template = TechTemplates.Find(name);
        if (template is null)
        {
            var all = TechTemplates.Load();
            return QuickOutcome.Fail(all.Count == 0
                ? $"技术要求：找不到模板目录或目录里没有 {TechTemplates.Extension}：{TechTemplates.Folder}"
                : $"技术要求：没有叫「{name}」的模板，可选：{string.Join("、", all.Select(t => t.Name))}。");
        }

        var api = context.Api;
        var drawing = HoleScan.ActiveDrawing(context);
        context.SetState("换技术要求");
        var existing = TechNotePlacement.Notes(api, drawing);
        NoteSlot? preferred = null;
        if (existing.Count > 0 && TechNotePlacement.Space(api, drawing) is { } space)
            preferred = TechNotePlanner.SlotOf(space, existing[0].Rect);

        var (_, leftover) = existing.Count == 0
            ? (0, 0)
            : AnnotationEraser.Erase(context, drawing, () => TechNotePlacement.Notes(api, drawing).Select(item => item.Annotation).ToList());
        if (leftover > 0)
            return QuickOutcome.Fail($"技术要求：原来的技术要求有 {leftover} 条删不掉，没有换成「{template.Name}」。");

        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        var note = api.Call(drawing, "IModelDoc2", "InsertNote", template.Text);
        if (note is null || api.Call(note, "INote", "GetAnnotation") is not { } annotation)
            return QuickOutcome.Fail($"技术要求：SolidWorks 没有接受注释，「{template.Name}」没有加上" + (existing.Count > 0 ? "（原来的已删）。" : "。"));

        // 让不开也算加上了（注释已在图上），回执里说压到了谁。
        var (where, _) = TechNotePlacement.Place(context, drawing, note, annotation, preferred);
        var action = existing.Count > 0 ?$"删掉原来的技术要求，换成「{template.Name}」（{template.Items} 条）" : $"加上「{template.Name}」（{template.Items} 条）";
        return QuickOutcome.Ok($"技术要求：{action}，{where}。");
    }

    /// <summary>表格的行：一份模板一行（name 是单元格按钮的字，也是动作参数）。</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Rows(string folder = TechTemplates.Folder)
        => TechTemplates.Load(folder)
            .Select(template => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["name"] = template.Name,
                ["items"] = template.Items.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["content"] = template.Summary,
            })
            .ToList();
}
