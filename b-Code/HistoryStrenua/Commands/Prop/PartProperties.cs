using HistoryStrenua.SolidWorks;
using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 属性类（1.33.0，用户定：Minerva 不再写属性）：读、写当前零件的属性。当前是工程图时作用在它第一个模型视图引用的零件上。
/// </summary>
/// <remarks>
/// 写完逐槽读回核对：<c>Add3</c> 返回 0 只说明调用被接受，链接或只读的槽会静默不动。
/// 默认不存盘（零件里可能有用户别的未保存改动），要存填「保存」= 是。
/// </remarks>
internal static class PartProperties
{

    // swCustomInfoType_e.swCustomInfoText、swCustomPropertyAddOption_e.swCustomPropertyDeleteAndAdd：
    // 模板已把槽建好且留空，OnlyIfNew 会认为已存在而一个字都不写。
    private const int CustomInfoText = 30;
    private const int DeleteAndAdd = 2;

    public static QuickCommand ReadCommand { get; } = new(
        Key: "prop-read",
        CommandName: StrenuaIdentity.Domain + ".prop.read",
        Title: "读取属性",
        Summary: "读当前零件（当前是工程图时取它引用的零件）的图号、名称、材料、表面处理、热处理、日期、设计。",
        Usage: "读当前零件活动配置的属性；当前是工程图时读它引用的零件。",
        Run: Read,
        Readonly: true,
        SelfReport: true);

    public static QuickCommand WriteCommand { get; } = new(
        Key: "prop-write",
        CommandName: StrenuaIdentity.Domain + ".prop.write",
        Title: "写入属性",
        Summary: "写当前零件属性：图号、名称取自文件名，材料、表面处理、热处理、日期、设计只写填了的；写完读回核对。",
        Usage: "图号、名称总是按文件名（第一个空格前是图号）写。其余选「不改」或留空就不动。默认不存盘，「保存」选是才存零件。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("material", PropertySlots.Material, "SolidWorks 收藏材料的名字；省略不动", false, 0, null, MaterialsView),
            new QuickParameter("surface", PropertySlots.SurfaceTreatment, "属性模板里的表面处理；省略不动", false, 0, null, SurfacesView),
            new QuickParameter("heat", PropertySlots.HeatTreatment, "属性模板里的热处理；省略不动", false, 0, null, HeatsView),
            new QuickParameter("date", PropertySlots.Date, "今天，或 yyyy/MM/dd；省略不动", false, 1, [PropertySlots.KeepOption, PropertySlots.TodayOption]),
            new QuickParameter("designer", PropertySlots.Designer, "设计者；省略不动", false, 1),
            new QuickParameter("save", "保存", "是 = 写完把零件存盘；省略为否", false, 1, ["否", "是"]),
        ],
        Example: StrenuaIdentity.Domain + ".prop.write date=今天 designer=张三",
        Bind: arguments =>
        {
            var request = Parse(FromArguments(arguments), QuickArgs.Flag(arguments, "save"));
            return context => Write(context, request);
        },
        SelfReport: true);

    /// <summary>「属性」类的命令类名。</summary>
    public const string CommandClass = "prop";

    /// <summary><c>strenua.prop.default</c>：只改本机设置，不碰 SolidWorks。</summary>
    public static CommandDescriptor DefaultDescriptor(StrenuaOptions options) => new()
    {
        Name = DefaultCommandName,
        Domain = StrenuaIdentity.Domain,
        CommandClass = CommandClass,
        Summary = DefaultSummary,
        Example = DefaultCommandName + " date=今天 designer=张三",
        Parameters = WriteCommand.Arguments
            .Where(parameter => parameter.Name != "save")
            .Select((parameter, index) => parameter.ToSpec(index))
            .ToArray(),
        Handler = CommandDescriptor.Sync(context => SetDefaults(options, context)),
    };

    /// <summary>设默认值的指令，也是页面「设为默认」按钮的动作。不碰 SolidWorks，所以不走快捷指令执行器。</summary>
    public const string DefaultCommandName = StrenuaIdentity.Domain + ".prop.default";

    public const string DefaultSummary = "把材料、表面处理、热处理、日期、设计记成默认（存本机）：页面初值显示它们，一键类出图时自动写入零件；没填的项不写。";

    private static PropertyDefaults FromArguments(CommandContext arguments) => new(
        Selected(arguments.GetString("material")),
        Selected(arguments.GetString("surface")),
        Selected(arguments.GetString("heat")),
        Selected(arguments.GetString("date")),
        Selected(arguments.GetString("designer")));

    /// <summary><c>strenua.prop.default</c>：先校验再整份替换并存档。</summary>
    internal static CommandResult SetDefaults(StrenuaOptions options, CommandContext arguments)
    {
        var values = FromArguments(arguments);
        try
        {
            Parse(values, save: false);
        }
        catch (QuickArgumentException ex)
        {
            return CommandResult.Fail(ex.Message);
        }

        var failure = options.SetPropDefaults(values);
        var filled = new[] { ("材料", values.Material), ("表面处理", values.Surface), ("热处理", values.Heat), ("日期", values.Date), ("设计", values.Designer) }
            .Where(item => item.Item2 is not null).Select(item => $"{item.Item1}={item.Item2}").ToList();
        var message = "属性默认：" + (filled.Count == 0 ? "全部不改" : string.Join("，", filled)) + "；一键类出图时写入。";
        return CommandResult.Ok(failure is null ? message : message + $"（未能记到本机：{failure}）");
    }

    /// <summary>选择框候选的取数 view（<c>strenua.ui.data</c>）。</summary>
    public const string MaterialsView = "materials";
    public const string SurfacesView = "surfaces";
    public const string HeatsView = "heats";

    /// <summary>候选表：第一项是「不改」。</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>>? OptionRows(string? view)
    {
        IEnumerable<string>? values = view switch
        {
            MaterialsView => PropertyOptions.Materials().Select(item => item.Label),
            SurfacesView => PropertyOptions.SurfaceTreatments(),
            HeatsView => PropertyOptions.HeatTreatments(),
            _ => null,
        };
        return values?.Prepend(PropertySlots.KeepOption)
            .Select(value => (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["value"] = value })
            .ToList();
    }

    private static string? Selected(string? text) => PropertySlots.IsUnset(text) ? null : text!.Trim();

    /// <summary>要操作的零件与配置：当前零件的活动配置，或当前工程图第一个模型视图引用的零件与配置。</summary>
    private static (object? Part, string Configuration, object? Drawing, string? Error) Target(QuickCommandContext context, object? document = null)
    {
        var api = context.Api;
        if ((document ?? context.Session.ActiveDocument()) is not { } active)
            return (null, string.Empty, null, "SolidWorks 里没有打开的文档。");
        var type = api.CallInt(active, "IModelDoc2", "GetType");
        if (type == SwConst.DocumentPart)
            return (active, ActiveConfiguration(api, active), null, null);
        if (type != SwConst.DocumentDrawing)
            return (null, string.Empty, null, "属性只作用在零件上：请打开零件或它的工程图。");

        foreach (var view in DrawingAccess.SheetViews(api, active))
        {
            if (api.Call(view, "IView", "get_ReferencedDocument") is not { } model)
                continue;
            return api.CallInt(model, "IModelDoc2", "GetType") == SwConst.DocumentPart
                ? (model, api.CallString(view, "IView", "get_ReferencedConfiguration"), active, null)
                : (null, string.Empty, active, "这张工程图引用的不是零件，属性只作用在零件上。");
        }

        return (null, string.Empty, active, "这张工程图当前页没有引用模型的视图。");
    }

    private static string ActiveConfiguration(SolidWorksApi api, object part)
    {
        var manager = api.Call(part, "IModelDoc2", "get_ConfigurationManager")
            ?? throw new QuickCommandException("SolidWorks 没有返回配置管理器。");
        var configuration = api.Call(manager, "IConfigurationManager", "get_ActiveConfiguration")
            ?? throw new QuickCommandException("SolidWorks 没有返回活动配置。");
        return api.CallString(configuration, "IConfiguration", "get_Name");
    }

    private static object Manager(SolidWorksApi api, object part, string configuration)
    {
        var extension = api.Call(part, "IModelDoc2", "get_Extension")
            ?? throw new QuickCommandException("SolidWorks 没有返回 ModelDocExtension。");
        return api.Call(extension, "IModelDocExtension", "get_CustomPropertyManager", configuration)
            ?? throw new QuickCommandException("SolidWorks 没有返回属性管理器。");
    }

    /// <summary>读解析后的值。<c>Get6</c> 最后一个 out 是 bool，传错类型反射绑不上。</summary>
    private static string ReadSlot(SolidWorksApi api, object manager, string name)
    {
        object?[] arguments = [name, false, string.Empty, string.Empty, false, false];
        api.Call(manager, "ICustomPropertyManager", "Get6", arguments);
        return arguments[3] as string ?? string.Empty;
    }

    private static string CurrentMaterial(SolidWorksApi api, object part, string configuration)
    {
        object?[] arguments = [configuration, string.Empty];
        return api.CallString(part, "IPartDoc", "GetMaterialPropertyName2", arguments);
    }

    private static QuickOutcome Read(QuickCommandContext context)
    {
        var (part, configuration, _, error) = Target(context);
        if (part is null)
            return QuickOutcome.Fail("读取属性：" + error);
        var api = context.Api;
        var manager = Manager(api, part, configuration);
        var values = PropertySlots.All.ToDictionary(
            slot => slot,
            slot => slot == PropertySlots.Material ? CurrentMaterial(api, part, configuration) : ReadSlot(api, manager, slot));
        var lines = values.Select(pair => $"{pair.Key}：{(pair.Value.Length == 0 ? "（空）" : pair.Value)}");
        return QuickOutcome.Ok($"「{api.CallString(part, "IModelDoc2", "GetTitle")}」配置「{configuration}」{Environment.NewLine}{string.Join(Environment.NewLine, lines)}", values);
    }

    /// <summary>一键类最后一步：把默认值写进这张图引用的零件。不存盘；默认材料已不在收藏里时这一步算没成，不拦出图。</summary>
    internal static QuickOutcome WriteDefaults(QuickCommandContext context, object drawing)
    {
        try
        {
            return Write(context, Parse(context.Options.PropDefaults, save: false), drawing);
        }
        catch (QuickArgumentException ex)
        {
            return QuickOutcome.Fail("写入属性：默认值不能用：" + ex.Message);
        }
    }

    /// <summary>默认值 → 写入请求（校验材料与日期，不对抛 <see cref="QuickArgumentException"/>）。</summary>
    internal static WriteRequest Parse(PropertyDefaults values, bool save) => new(
        PropertyOptions.ResolveMaterial(values.Material, PropertyOptions.Materials()),
        Selected(values.Surface),
        Selected(values.Heat),
        PropertySlots.ResolveDate(values.Date, DateTime.Now),
        Selected(values.Designer),
        save);

    private static QuickOutcome Write(QuickCommandContext context, WriteRequest request, object? document = null)
    {
        var (part, configuration, drawing, error) = Target(context, document);
        if (part is null)
            return QuickOutcome.Fail("写入属性：" + error);
        var api = context.Api;
        var path = api.CallString(part, "IModelDoc2", "GetPathName");
        if (path.Length == 0)
            return QuickOutcome.Fail("写入属性：零件还没存盘，没有文件名可取图号和名称。");

        var fileName = Path.GetFileName(path);
        var manager = Manager(api, part, configuration);
        var existing = api.CallArray(manager, "ICustomPropertyManager", "GetNames").OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var written = new List<string>();
        var created = new List<string>();

        // 材质先应用再写槽：换材质会重建。SetMaterialPropertyName2 传错也不报错，必须读回。
        if (request.Material is { } material)
        {
            api.Call(part, "IPartDoc", "SetMaterialPropertyName2", configuration, material.Database, material.Name);
            var applied = CurrentMaterial(api, part, configuration);
            if (applied != material.Name)
                return QuickOutcome.Fail($"写入属性：材质应用后读回是「{applied}」，不是「{material.Name}」（材料库 {material.Database}）。");
            var failure = WriteSlot(api, manager, PropertySlots.Material, PropertySlots.MaterialLink(configuration, fileName), material.Name, existing, created);
            if (failure is not null)
                return QuickOutcome.Fail("写入属性：" + failure);
            written.Add(PropertySlots.Material);
        }

        foreach (var (slot, value) in PropertySlots.Plan(fileName, request))
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var failure = WriteSlot(api, manager, slot, value, value, existing, created);
            if (failure is not null)
                return QuickOutcome.Fail($"写入属性：{failure}已写入：{string.Join("、", written)}。");
            written.Add(slot);
        }

        // 标题栏是属性链接，当前是工程图时重建才看得到新值。
        if (drawing is not null)
            api.Call(drawing, "IModelDoc2", "EditRebuild3");

        var message = $"写入属性：「{fileName}」配置「{configuration}」已写 {string.Join("、", written)}";
        if (created.Count > 0)
            message += $"；模板里原本没有 {string.Join("、", created)}，是新建的槽，请核对属性模板";
        if (!request.Save)
            return QuickOutcome.Ok(message + "。零件未存盘。");

        object?[] save = [SwConst.SaveSilent, 0, 0];
        return api.CallBool(part, "IModelDoc2", "Save3", save)
            ? QuickOutcome.Ok(message + "，零件已存盘。")
            : QuickOutcome.Fail(message + $"，但存盘失败（错误码 {save[1]}）。");
    }

    /// <summary>写一槽并读回；返回失败原因，成功为 null。</summary>
    private static string? WriteSlot(SolidWorksApi api, object manager, string slot, string value, string expected, HashSet<string> existing, List<string> created)
    {
        if (!existing.Contains(slot))
            created.Add(slot);
        var status = api.CallInt(manager, "ICustomPropertyManager", "Add3", slot, CustomInfoText, value, DeleteAndAdd);
        if (status != 0)
            return $"SolidWorks 拒绝写入「{slot}」（返回 {status}）。";
        var readBack = ReadSlot(api, manager, slot);
        return readBack == expected ? null : $"「{slot}」写入后读回是「{readBack}」，不是「{expected}」。";
    }
}
