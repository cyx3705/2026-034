using System.Text;
using System.Text.Json.Nodes;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 蚂蚁工场第 3 层（1.37.0）：选参数，选全了就下载 3D 插进当前总装。没选全只回还缺哪些参数和可选值，不碰图。
/// </summary>
/// <remarks>
/// 文件放总装目录下的「外购件」子文件夹（用户定，同 HistoryMinerva 外购件的去处），以料号命名；同料号已有就直接复用、不重下。
/// STP 用 <c>LoadFile4</c> 导入后另存为 SolidWorks 文件（导入出零件存 .SLDPRT、出装配体存 .SLDASM），STP 本身下在临时目录、用完删。
/// 1.38.0：按零件原点插（AddComponent5 给的是包围盒中心），可给 at / axes 直接摆好并固定；不加配合；不存总装（同模块「不替用户保存」）。
/// </remarks>
internal static class MymoooInsert
{
    public static QuickCommand Command { get; } = new(
        Key: "mymooo-insert",
        CommandName: StrenuaIdentity.Domain + ".mymooo.insert",
        Title: "插入标准件",
        CatalogSummary: "选型下载标准件并插入当前总装",
        Summary: "蚂蚁工场第 3 层：按参数选型，选全了下载 3D 存进「外购件」，按零件原点插进当前总装（at / axes 摆位）；没选全回还要选的参数",
        Usage: "「商品」填 strenua.mymooo.product 列出的商品号。先只给商品看要选哪些参数，再用「参数」填 码=值（逗号分开，如 M=8,L=20,XN=S11），"
               + "有多个型式的先填「型式」；知道完整料号可直接填「料号」。选全了才下载插入，文件以料号命名放在总装目录的「外购件」里（装配体的零件放「外购件/料号/」），同料号复用。当前文档要是存过的总装。"
               + "按零件原点摆：「位置」x,y,z（总装 mm），「朝向」零件 x,y,z 轴各朝总装哪根轴（如 X,-Z,Y），默认固定；insert=否 只下载不插。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("product", "商品", "商品号（strenua.mymooo.product / category keyword= 列出的号）", true),
            new QuickParameter("params", "参数", "参数码或参数名=值，逗号分开，如 M=8,L=20；区间参数直接填数；省略只看要选哪些参数", false, 2),
            new QuickParameter("type", "型式", "型式代号（商品有多个型式时必填，回执会列出）", false, 2),
            new QuickParameter("number", "料号", "完整料号（如 SBH1-M8-L20-S11），给了就按料号直选、不看 params", false, 2),
            new QuickParameter("at", "位置", "零件原点放在总装哪（x,y,z mm）；省略为总装原点", false, 3),
            new QuickParameter("axes", "朝向", "零件 x,y,z 轴朝总装哪根轴，如 X,-Z,Y；省略不转", false, 3),
            new QuickParameter("fix", "固定", "是（默认）/ 否", false, 3),
            new QuickParameter("insert", "插入", "否 = 只下载存进外购件、不插进总装；省略为是", false, 3),
            new QuickParameter("refresh", "重下", "是 = 外购件里已有也重新下载覆盖（旧版导入引用了临时目录时用）", false, 3),
        ],
        Example: StrenuaIdentity.Domain + ".mymooo.insert product=302121 params=M=8,L=20,XN=S11",
        Bind: arguments =>
        {
            var product = QuickArgs.Name(arguments, "product");
            if (!long.TryParse(product, out var productId))
                throw new QuickArgumentException($"product={product} 要写商品号（数字）。");
            var pairs = MymoooRules.Pairs(QuickArgs.Optional(arguments, "params"), "params");
            var type = QuickArgs.Optional(arguments, "type");
            var number = QuickArgs.Optional(arguments, "number");
            var placing = new Placing(
                QuickArgs.Optional(arguments, "at") is { } at ? ModelSpec.Point(at, "at") : (0, 0, 0),
                ModelSpec.Axes(QuickArgs.Optional(arguments, "axes") ?? "X,Y,Z"),
                QuickArgs.Optional(arguments, "fix") is null || QuickArgs.Flag(arguments, "fix"),
                QuickArgs.Optional(arguments, "insert") is null || QuickArgs.Flag(arguments, "insert"),
                QuickArgs.Flag(arguments, "refresh"));
            return context => Insert(context, productId, pairs, type, number, placing);
        },
        SelfReport: true);

    private const int OpenSilent = 1;

    /// <summary>摆法：零件原点落点、三根轴朝向、固定、要不要插、要不要重下。</summary>
    private sealed record Placing((double X, double Y, double Z) At, double[,] Axes, bool Fix, bool Insert, bool Refresh);

    private static QuickOutcome Insert(QuickCommandContext context, long productId, IReadOnlyList<KeyValuePair<string, string>> pairs, string? type, string? number, Placing placing)
    {
        var cancellation = context.Cancellation;
        context.Report("选型");
        var request = new JsonObject { ["ProductId"] = productId, ["ProductQueryParamList"] = new JsonArray(), ["Qty"] = 1 };
        var selection = MymoooClient.Select(request, number, cancellation);
        var notes = new List<string>();

        if (number is null)
        {
            var types = selection["productTypes"] as JsonArray ?? [];
            if (type is not null)
            {
                var chosen = types.FirstOrDefault(t => string.Equals(MymoooRules.Text(t, "typeCode"), type, StringComparison.OrdinalIgnoreCase) || MymoooRules.Text(t, "typeDesc") == type)
                             ?? throw new QuickArgumentException($"型式没有「{type}」，有：{TypeList(types)}。");
                request["TypeId"] = chosen["typeId"]?.DeepClone();
                request["TypeCode"] = MymoooRules.Text(chosen, "typeCode");
                selection = MymoooClient.Select(request, null, cancellation);
            }
            else if (types.Count > 1 && pairs.Count > 0)
            {
                return QuickOutcome.Nothing($"商品 {productId} 有 {types.Count} 个型式，先给 type=：{TypeList(types)}。");
            }

            selection = MymoooParts.Narrow(request, selection, pairs, notes, cancellation);
        }

        var shown = MymoooRules.Plain(MymoooRules.Text(selection, "number"));
        var numbers = selection["productNumbers"] as JsonArray ?? [];
        if (!MymoooRules.Complete(shown) || numbers.Count != 1)
            return QuickOutcome.Nothing(Remaining(productId, selection, shown, notes));

        var final = MymoooRules.Text(numbers[0], "number") is { Length: > 0 } listed ? listed : shown;
        var orderProduct = long.TryParse(MymoooRules.Text(numbers[0]?["querys"], "productId"), out var queried) ? queried : productId;
        return Place(context, orderProduct, final, notes, placing);
    }

    /// <summary>没选全：当前料号 + 每个还没定的编码参数和它剩下的可选值。</summary>
    private static string Remaining(long productId, JsonNode selection, string shown, List<string> notes)
    {
        var text = new StringBuilder($"商品 {productId} 还没选全，没有插入。当前料号：{(shown.Length > 0 ? shown : "（先定型式）")}").AppendLine();
        var types = selection["productTypes"] as JsonArray ?? [];
        if (types.Count > 1 && !types.Any(t => t?["checked"]?.GetValue<bool>() == true))
            text.AppendLine($"型式（type=）：{TypeList(types)}");
        foreach (var parameter in MymoooParts.Coded(selection))
        {
            var unit = MymoooRules.Text(parameter, "paramUnit");
            text.AppendLine($"  {MymoooRules.Label(parameter)}{(unit.Length > 0 ? $"（{unit}）" : string.Empty)}：{MymoooRules.Choices(parameter)}");
        }

        foreach (var note in notes)
            text.AppendLine("注意：" + note);
        text.Append("把还要选的用 params=码=值 补上再调（已选的一起带上）。");
        return text.ToString();
    }

    private static string TypeList(JsonArray types)
        => string.Join("、", types.Select(t => MymoooRules.Text(t, "typeCode") is var code && MymoooRules.Text(t, "typeDesc") is var desc && desc.Length > 0 && desc != code ? $"{code}（{desc}）" : code));

    /// <summary>料号定了：找或下载文件，按零件原点插进当前总装。</summary>
    private static QuickOutcome Place(QuickCommandContext context, long productId, string number, List<string> notes, Placing placing)
    {
        var api = context.Api;
        var application = context.Session.Application;
        var assembly = context.Session.ActiveDocument();
        if (assembly is null || api.CallInt(assembly, "IModelDoc2", "GetType") != SwConst.DocumentAssembly)
            throw new QuickCommandException($"料号 {number} 已选定，但当前文档不是总装：先打开要插入的总装（存过的）再调。");
        var assemblyPath = api.CallString(assembly, "IModelDoc2", "GetPathName");
        if (assemblyPath.Length == 0)
            throw new QuickCommandException($"料号 {number} 已选定，但总装还没存过，定不了「{MymoooRules.Folder}」放哪：先存总装。");
        var assemblyTitle = api.CallString(assembly, "IModelDoc2", "GetTitle");
        var folder = Path.Combine(Path.GetDirectoryName(assemblyPath)!, MymoooRules.Folder);
        var name = MymoooRules.FileName(number);

        var existing = MymoooParts.Existing(folder, name);
        var broken = existing is not null && MymoooParts.Broken(api, application, existing);
        string path;
        string source;
        if (existing is not null && (placing.Refresh || broken))
        {
            if (api.Call(application, "ISldWorks", "GetOpenDocumentByName", existing) is not null)
                throw new QuickCommandException(
                    $"外购件 {existing} {(broken ? "是旧版导入的，引用的是 SolidWorks 临时目录（重启或清临时文件就丢）" : "要重新下载")}，但它正被 SolidWorks 打开（多半在总装里），覆盖不了："
                    + "先关掉引用它的总装，在别的已存总装里用 insert=否 refresh=是 重下，再回原总装。");
            path = MymoooParts.Fetch(context, productId, number, folder, name);
            source = broken ? "（旧版导入引用临时目录，已重新下载覆盖）" : "（重新下载覆盖）";
        }
        else
        {
            path = existing ?? MymoooParts.Fetch(context, productId, number, folder, name);
            source = existing is null ? "（新下载）" : "（外购件里已有，复用）";
        }

        if (!placing.Insert)
            return QuickOutcome.Ok($"下载标准件：{number} 已存进外购件，没插入。{Environment.NewLine}文件：{path}{source}",
                new Dictionary<string, string> { ["number"] = number, ["path"] = path });

        context.Report("插入总装");
        // AddComponent5 要求零件已在内存里：先开（已开着就是切过去），再切回总装插。
        object?[] open = [path, Path.GetExtension(path).Equals(".SLDASM", StringComparison.OrdinalIgnoreCase) ? SwConst.DocumentAssembly : SwConst.DocumentPart, OpenSilent, string.Empty, 0, 0];
        var model = api.Call(application, "ISldWorks", "OpenDoc6", open)
                    ?? throw new QuickCommandException($"SolidWorks 没打开 {path}（错误码 {open[4]}）。");
        var modelTitle = api.CallString(model, "IModelDoc2", "GetTitle");
        api.Call(application, "ISldWorks", "ActivateDoc3", assemblyTitle, false, 1, 0);
        var component = api.Call(assembly, "IAssemblyDoc", "AddComponent5", path, 0, string.Empty, false, string.Empty, 0.0, 0.0, 0.0);
        // 只关窗口：零件已被总装引用，留在内存里。
        api.Call(application, "ISldWorks", "CloseDoc", modelTitle);
        api.Call(application, "ISldWorks", "ActivateDoc3", assemblyTitle, false, 1, 0);
        if (component is null)
            return QuickOutcome.Fail($"插入标准件：SolidWorks 没把 {path} 插进「{assemblyTitle}」。");
        // AddComponent5 给的是包围盒中心（1.37.0 真机：模组插在原点却偏了半个长度），按零件原点重摆。
        ModelAccess.Place(context, assembly, component, placing.Axes, placing.At, placing.Fix);
        api.Call(assembly, "IModelDoc2", "EditRebuild3");

        var componentName = api.CallString(component, "IComponent2", "get_Name2");
        var origin = ModelAccess.Frame(api, component).Origin;
        var text = new StringBuilder($"插入标准件：{number} 已插进「{assemblyTitle}」，零部件名 {componentName}，零件原点在 {ModelAccess.Format(origin)}、轴 {ModelSpec.AxesText(placing.Axes)}")
            .Append(placing.Fix ? "，已固定" : "，浮动").Append("（没加配合，总装没存）。")
            .AppendLine().Append($"文件：{path}{source}");
        foreach (var note in notes)
            text.AppendLine().Append("注意：" + note);
        return QuickOutcome.Ok(text.ToString(), new Dictionary<string, string> { ["component"] = componentName, ["number"] = number, ["path"] = path });
    }

}
