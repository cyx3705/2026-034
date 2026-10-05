using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 「技术要求」类的模板表格（1.13.0，用户定）：点一行就把那份技术要求插进当前工程图；图框里已有技术要求就先删掉，在原来那一处放新的。
/// 放哪只许标题栏上方或左侧，「避障」开时压到视图就挪视图让地方（<see cref="TechNotePlacement"/>）。
/// 表格「设置」列把一份设为默认（记到本机），「一键出图」的「技术要求」一步插的就是默认那份。
/// </summary>
/// <remarks>
/// 带参数（模板名），所以不进 <see cref="QuickCommands.All"/>（那里是无参按钮）；指令 <see cref="CommandName"/> 单独注册，
/// 每次按名字现做一条 <see cref="QuickCommand"/> 交给同一个执行器——仍是一次只跑一条、可取消。
/// 1.13.0 起原「技术要求」按钮、<c>strenua.tech.note</c> 与「技术要求.txt」都删了（用户定），技术要求只从这里来。
/// </remarks>
internal static class TechApply
{
    public const string Key = "tech-apply";

    public const string CommandName = StrenuaIdentity.Domain + ".tech.apply";

    /// <summary>表格「技术要求」列的单元格动作。</summary>
    public const string ActionId = CommandName;

    /// <summary>设默认模板的指令，也是表格「设置」列的单元格动作。</summary>
    public const string DefaultCommandName = StrenuaIdentity.Domain + ".tech.default";

    public const string DefaultActionId = DefaultCommandName;

    /// <summary>没设过默认时用这份（与 1.9.0 起一键出图用的用户手工图那 8 条最接近）。</summary>
    public const string InitialDefault = "机加件-钢材";

    /// <summary>表格「设置」列：默认那一行与其余行按钮上的字。</summary>
    public const string IsDefaultText = "默认";

    public const string SetDefaultText = "设为默认";

    public const string Summary = "把「通用技术要求」里的一份插进当前工程图（标题栏上方或左侧）；图框里已有技术要求就删掉、在原处换成这份，避障开时挪视图让地方。";

    public const string DefaultSummary = "把「通用技术要求」里的一份设为默认（记到本机），「一键出图」插的就是它；省略 name 只报当前默认。";

    public static QuickCommand Command(string name, object? drawing = null) => new(
        Key: Key,
        CommandName: CommandName,
        Title: $"技术要求「{name}」",
        Summary: Summary,
        Usage: Summary,
        Run: context => Run(context, name, drawing));

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="name">模板名。</param>
    /// <param name="drawing">工程图；null 时取活动工程图（「一键出图」传刚建的那张）。</param>
    internal static QuickOutcome Run(QuickCommandContext context, string name, object? drawing = null)
    {
        var template = TechTemplates.Find(name);
        if (template is null)
            return QuickOutcome.Fail(Missing(name));

        var api = context.Api;
        drawing ??= HoleScan.ActiveDrawing(context);
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
        var action = existing.Count > 0 ? $"删掉原来的技术要求，换成「{template.Name}」（{template.Items} 条）" : $"加上「{template.Name}」（{template.Items} 条）";
        return QuickOutcome.Ok($"技术要求：{action}，{where}。");
    }

    /// <summary><c>strenua.tech.default</c>：带 name 就设默认、记到本机并刷新表格；不带就报当前默认。</summary>
    internal static async Task<CommandResult> SetDefaultAsync(StrenuaOptions options, string? name, ICommandBus? bus)
    {
        if (string.IsNullOrWhiteSpace(name))
            return CommandResult.Ok($"技术要求：当前默认「{options.TechDefault}」" + (TechTemplates.Find(options.TechDefault) is null ? "（模板目录里已经没有这份了）。" : "。"));
        var template = TechTemplates.Find(name);
        if (template is null)
            return CommandResult.Fail(Missing(name));

        var failure = options.SetTechDefault(template.Name);
        if (bus is not null)
        {
            try
            {
                // 表格「设置」列跟着变；页面没开着或没装 Aurora 时刷不到，不影响结果。来源由宿主盖章。
                await bus.ExecuteAsync($"aurora.ui.refreshdata node={StrenuaPage.TechTableId}", "").ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        var message = $"技术要求：默认改为「{template.Name}」（{template.Items} 条），「一键出图」插这份。";
        return CommandResult.Ok(failure is null ? message : message + $"（未能记到本机，下次启动回到上次的值：{failure}）");
    }

    /// <summary>表格的行：一份模板一行（name 是「技术要求」列按钮的字，也是两个动作的参数；setting 是「设置」列按钮的字）。</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Rows(string defaultName, string folder = TechTemplates.Folder)
        => TechTemplates.Load(folder)
            .Select(template => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["name"] = template.Name,
                ["setting"] = string.Equals(template.Name, defaultName, StringComparison.OrdinalIgnoreCase) ? IsDefaultText : SetDefaultText,
                ["items"] = template.Items.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["content"] = template.Summary,
            })
            .ToList();

    private static string Missing(string name)
    {
        var all = TechTemplates.Load();
        return all.Count == 0
            ? $"技术要求：找不到模板目录或目录里没有 {TechTemplates.Extension}：{TechTemplates.Folder}"
            : $"技术要求：没有叫「{name}」的模板，可选：{string.Join("、", all.Select(t => t.Name))}。";
    }
}
