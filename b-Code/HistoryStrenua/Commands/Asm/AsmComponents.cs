using System.Text;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 装配类（1.38.0，用户定：SolidWorks MCP 插入零部件只能平移、不能旋转，弃用，并进 Strenua）：列零部件、插入、摆位、删除、干涉。
/// </summary>
/// <remarks>
/// 摆位用「原点 + 三根轴朝向」（<see cref="ModelSpec.Axes"/>），直接写变换并固定，不加配合——AI 按设计坐标摆，比配合好核对；
/// 有配合的零部件摆完会被配合拉回，回执提醒。只作用于活动总装，不存盘（存用 <c>strenua.doc.save</c>）。
/// </remarks>
internal static class AsmComponents
{
    public static QuickCommand ListCommand { get; } = new(
        Key: "asm-list",
        CommandName: StrenuaIdentity.Domain + ".asm.list",
        Title: "零部件列表",
        Summary: "列活动总装的顶层零部件：名字、文件、原点、轴朝向、包围盒、是否固定；名字供摆位、删除用。",
        Usage: "不带参数。原点、包围盒是总装 mm；轴朝向写法同插入的 axes（零件 x,y,z 各朝总装哪根轴）。",
        Run: List,
        Readonly: true,
        SelfReport: true);

    public static QuickCommand InsertCommand { get; } = new(
        Key: "asm-insert",
        CommandName: StrenuaIdentity.Domain + ".asm.insert",
        Title: "插入零部件",
        Summary: "把零件或子装配插进活动总装：零件原点放在 at，三根轴按 axes 朝向，默认固定；可一次插几个同一文件的。",
        Usage: "「路径」填 .SLDPRT / .SLDASM。「位置」x,y,z（总装 mm，零件原点落点），几个用分号隔开就插几个；"
               + "「朝向」零件 x,y,z 轴各朝总装哪根轴，如 X,-Z,Y（默认 X,Y,Z 不转），必须右手系；fix=否 不固定。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("path", "路径", "零件 / 子装配完整路径", true),
            new QuickParameter("at", "位置", "x,y,z（总装 mm），分号隔开插几个", true, 1),
            new QuickParameter("axes", "朝向", "零件 x,y,z 轴朝向，如 X,-Z,Y；省略不转", false, 1),
            new QuickParameter("fix", "固定", "是（默认）/ 否", false, 1),
        ],
        Example: StrenuaIdentity.Domain + @".asm.insert path=D:\项目\b-Module-SW\XX-01-01底板.SLDPRT at=0,262,0 axes=X,-Z,Y",
        Bind: arguments =>
        {
            var path = QuickArgs.Name(arguments, "path");
            var type = Path.GetExtension(path).ToUpperInvariant() switch
            {
                ".SLDPRT" => SwConst.DocumentPart,
                ".SLDASM" => SwConst.DocumentAssembly,
                _ => throw new QuickArgumentException($"path={path} 要以 .SLDPRT 或 .SLDASM 结尾。"),
            };
            var points = QuickArgs.Name(arguments, "at").Split([';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(text => ModelSpec.Point(text, "at")).ToList();
            var axes = ModelSpec.Axes(QuickArgs.Optional(arguments, "axes") ?? "X,Y,Z");
            var fix = QuickArgs.Optional(arguments, "fix") is null || QuickArgs.Flag(arguments, "fix");
            return context => Insert(context, path, type, points, axes, fix);
        },
        SelfReport: true);

    public static QuickCommand MoveCommand { get; } = new(
        Key: "asm-move",
        CommandName: StrenuaIdentity.Domain + ".asm.move",
        Title: "摆零部件",
        Summary: "把总装里一个零部件的原点挪到 at、轴按 axes 转（省略的保持原样），默认固定；读回位置。",
        Usage: "「零部件」填零部件列表的 name（如 底板-1）。「位置」x,y,z（总装 mm）省略不挪；「朝向」同插入，省略不转；fix=否 摆完不固定。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("name", "零部件", "零部件名（零部件列表的 name）", true),
            new QuickParameter("at", "位置", "x,y,z（总装 mm）；省略不挪", false, 1),
            new QuickParameter("axes", "朝向", "零件 x,y,z 轴朝向；省略不转", false, 1),
            new QuickParameter("fix", "固定", "是（默认）/ 否", false, 1),
        ],
        Example: StrenuaIdentity.Domain + ".asm.move name=底板-1 at=-600,262,0",
        Bind: arguments =>
        {
            var name = QuickArgs.Name(arguments, "name");
            var at = QuickArgs.Optional(arguments, "at") is { } text ? ModelSpec.Point(text, "at") : ((double, double, double)?)null;
            var axes = QuickArgs.Optional(arguments, "axes") is { } axesText ? ModelSpec.Axes(axesText) : null;
            if (at is null && axes is null && QuickArgs.Optional(arguments, "fix") is null)
                throw new QuickArgumentException("没有要改的：给 at=、axes= 或 fix= 至少一个。");
            var fix = QuickArgs.Optional(arguments, "fix") is null || QuickArgs.Flag(arguments, "fix");
            return context => Move(context, name, at, axes, fix);
        },
        SelfReport: true);

    public static QuickCommand DeleteCommand { get; } = new(
        Key: "asm-delete",
        CommandName: StrenuaIdentity.Domain + ".asm.delete",
        Title: "删零部件",
        Summary: "按名字从活动总装删零部件（逗号分开删几个），不删文件。",
        Usage: "「零部件」填零部件列表的 name，逗号分开。只从总装里拿掉，零件文件不动。",
        Run: QuickCommand.Unbound,
        Parameters: [new QuickParameter("name", "零部件", "零部件名，逗号分开", true)],
        Example: StrenuaIdentity.Domain + ".asm.delete name=底板-2",
        Bind: arguments =>
        {
            var names = QuickArgs.Names(QuickArgs.Name(arguments, "name"), "name");
            return context => Delete(context, names);
        },
        SelfReport: true);

    private const int OpenSilent = 1;

    private static QuickOutcome List(QuickCommandContext context)
    {
        var api = context.Api;
        var assembly = ModelAccess.ActiveAssembly(context);
        var rows = new List<Dictionary<string, string>>();
        var text = new StringBuilder();
        foreach (var component in api.CallArray(assembly, "IAssemblyDoc", "GetComponents", true))
        {
            var (origin, axes) = ModelAccess.Frame(api, component);
            var box = ModelAccess.Box(api, component);
            var row = new Dictionary<string, string>
            {
                ["name"] = api.CallString(component, "IComponent2", "get_Name2"),
                ["file"] = Path.GetFileName(api.CallString(component, "IComponent2", "GetPathName")),
                ["at"] = ModelAccess.Format(origin),
                ["axes"] = ModelSpec.AxesText(axes),
                ["box"] = box.Length == 6 ? string.Join(",", box.Select(value => ModelSpec.Format(value, "0.#"))) : string.Empty,
                ["fixed"] = ModelAccess.Fixed(api, component) ? "是" : "否",
                ["suppressed"] = api.CallBool(component, "IComponent2", "IsSuppressed") ? "是" : "否",
            };
            rows.Add(row);
            text.AppendLine($"· {row["name"]}  {row["file"]}  原点{row["at"]}  轴 {row["axes"]}  盒[{row["box"]}]{(row["fixed"] == "是" ? "  固定" : string.Empty)}{(row["suppressed"] == "是" ? "  压缩" : string.Empty)}");
        }

        return QuickOutcome.Ok($"零部件列表：「{api.CallString(assembly, "IModelDoc2", "GetTitle")}」顶层 {rows.Count} 个。{Environment.NewLine}{text.ToString().TrimEnd()}", rows);
    }

    private static QuickOutcome Insert(QuickCommandContext context, string path, int type, IReadOnlyList<(double X, double Y, double Z)> points, double[,] axes, bool fix)
    {
        var api = context.Api;
        var application = context.Session.Application;
        var assembly = ModelAccess.ActiveAssembly(context);
        if (!File.Exists(path))
            return QuickOutcome.Fail($"插入零部件：文件不存在 {path}。");
        var assemblyTitle = api.CallString(assembly, "IModelDoc2", "GetTitle");
        // AddComponent5 要求零件已在内存里：先开（已开着就是切过去），再切回总装插；插完关掉零件窗口（被总装引用，仍在内存）。
        object?[] open = [path, type, OpenSilent, string.Empty, 0, 0];
        var model = api.Call(application, "ISldWorks", "OpenDoc6", open) ?? throw new QuickCommandException($"SolidWorks 没打开 {path}（错误码 {open[4]}）。");
        var modelTitle = api.CallString(model, "IModelDoc2", "GetTitle");
        var visible = api.CallBool(model, "IModelDoc2", "get_Visible");
        api.Call(application, "ISldWorks", "ActivateDoc3", assemblyTitle, false, 1, 0);
        var names = new List<string>();
        try
        {
            foreach (var point in points)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                var component = api.Call(assembly, "IAssemblyDoc", "AddComponent5", path, 0, string.Empty, false, string.Empty, 0.0, 0.0, 0.0);
                if (component is null)
                    break;
                ModelAccess.Place(context, assembly, component, axes, point, fix);
                names.Add($"{api.CallString(component, "IComponent2", "get_Name2")} 原点{ModelAccess.Format(ModelAccess.Frame(api, component).Origin)}");
            }
        }
        finally
        {
            if (visible && !ReferenceEquals(model, assembly))
                api.CallForCleanup(application, "ISldWorks", "CloseDoc", modelTitle);
            api.CallForCleanup(application, "ISldWorks", "ActivateDoc3", assemblyTitle, false, 1, 0);
        }

        api.Call(assembly, "IModelDoc2", "EditRebuild3");
        var text = $"插入零部件：{Path.GetFileName(path)} 插了 {names.Count}/{points.Count} 个，轴 {ModelSpec.AxesText(axes)}{(fix ? "，已固定" : string.Empty)}（总装没存）：{string.Join("；", names)}。";
        return names.Count == points.Count ? QuickOutcome.Ok(text) : QuickOutcome.Fail(text + " 余下的 SolidWorks 没插进去。");
    }

    private static QuickOutcome Move(QuickCommandContext context, string name, (double X, double Y, double Z)? at, double[,]? axes, bool fix)
    {
        var api = context.Api;
        var assembly = ModelAccess.ActiveAssembly(context);
        var component = ModelAccess.Component(api, assembly, name);
        var (origin, current) = ModelAccess.Frame(api, component);
        var target = at ?? origin;
        ModelAccess.Place(context, assembly, component, axes ?? current, target, fix);
        api.Call(assembly, "IModelDoc2", "EditRebuild3");
        var (after, turned) = ModelAccess.Frame(api, component);
        var off = Math.Max(Math.Abs(after.X - target.X), Math.Max(Math.Abs(after.Y - target.Y), Math.Abs(after.Z - target.Z)));
        var mates = MateCount(api, component);
        var text = $"摆零部件：「{name}」原点 {ModelAccess.Format(origin)} → {ModelAccess.Format(after)}，轴 {ModelSpec.AxesText(turned)}{(fix ? "，已固定" : "，浮动")}"
                   + $"，包围盒 [{string.Join(",", ModelAccess.Box(api, component).Select(value => ModelSpec.Format(value, "0.#")))}]。"
                   + (mates > 0 ? $" 它有 {mates} 个配合，位置以配合为准。" : string.Empty);
        return off < 0.01 ? QuickOutcome.Ok(text) : QuickOutcome.Fail(text + $" 读回差 {ModelSpec.Format(off)} mm（被配合拉回或固定没解开）。");
    }

    private static QuickOutcome Delete(QuickCommandContext context, IReadOnlyList<string> names)
    {
        var api = context.Api;
        var assembly = ModelAccess.ActiveAssembly(context);
        var deleted = new List<string>();
        foreach (var name in names)
        {
            var component = ModelAccess.Component(api, assembly, name);
            api.ClearSelection(assembly);
            api.Call(component, "IComponent2", "Select4", false, null, false);
            var extension = api.Call(assembly, "IModelDoc2", "get_Extension")!;
            // swDelete_Absorbed = 2；零部件不吃别的东西，等同普通删除。
            if (api.CallBool(extension, "IModelDocExtension", "DeleteSelection2", 2))
                deleted.Add(name);
        }

        api.ClearSelection(assembly);
        api.Call(assembly, "IModelDoc2", "EditRebuild3");
        var text = $"删零部件：从总装删了 {string.Join("、", deleted)}（文件没动，总装没存）。";
        return deleted.Count == names.Count ? QuickOutcome.Ok(text) : QuickOutcome.Fail(text + $" 没删掉：{string.Join("、", names.Except(deleted))}。");
    }

    private static int MateCount(SolidWorksApi api, object component)
        => api.CallArray(component, "IComponent2", "GetMates").Length;
}
