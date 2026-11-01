using HistoryStrenua.SolidWorks;

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
    private const int DocumentPart = 1;
    private const int DocumentDrawing = 3;

    // swCustomInfoType_e.swCustomInfoText、swCustomPropertyAddOption_e.swCustomPropertyDeleteAndAdd：
    // 模板已把槽建好且留空，OnlyIfNew 会认为已存在而一个字都不写。
    private const int CustomInfoText = 30;
    private const int DeleteAndAdd = 2;
    private const int SaveSilent = 1;

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
            var request = new WriteRequest(
                PropertyOptions.ResolveMaterial(arguments.GetString("material"), PropertyOptions.Materials()),
                Selected(arguments.GetString("surface")),
                Selected(arguments.GetString("heat")),
                PropertySlots.ResolveDate(arguments.GetString("date"), DateTime.Now),
                Selected(arguments.GetString("designer")),
                QuickArgs.Flag(arguments, "save"));
            return context => Write(context, request);
        },
        SelfReport: true);

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
    private static (object? Part, string Configuration, object? Drawing, string? Error) Target(QuickCommandContext context)
    {
        var api = context.Api;
        if (context.Session.ActiveDocument() is not { } active)
            return (null, string.Empty, null, "SolidWorks 里没有打开的文档。");
        var type = api.CallInt(active, "IModelDoc2", "GetType");
        if (type == DocumentPart)
            return (active, ActiveConfiguration(api, active), null, null);
        if (type != DocumentDrawing)
            return (null, string.Empty, null, "属性只作用在零件上：请打开零件或它的工程图。");

        foreach (var view in HoleScan.SheetViews(api, active))
        {
            if (api.Call(view, "IView", "get_ReferencedDocument") is not { } model)
                continue;
            return api.CallInt(model, "IModelDoc2", "GetType") == DocumentPart
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

    private static QuickOutcome Write(QuickCommandContext context, WriteRequest request)
    {
        var (part, configuration, drawing, error) = Target(context);
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

        object?[] save = [SaveSilent, 0, 0];
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
