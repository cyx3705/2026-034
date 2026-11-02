using System.Text;
using System.Text.Json.Nodes;

namespace HistoryStrenua;

/// <summary>
/// 蚂蚁工场第 3 层（1.37.0）：选参数，选全了就下载 3D 插进当前总装。没选全只回还缺哪些参数和可选值，不碰图。
/// </summary>
/// <remarks>
/// 文件放总装目录下的「外购件」子文件夹（用户定，同 HistoryMinerva 外购件的去处），以料号命名；同料号已有就直接复用、不重下。
/// STP 用 <c>LoadFile4</c> 导入后另存为 SolidWorks 文件（导入出零件存 .SLDPRT、出装配体存 .SLDASM），STP 本身下在临时目录、用完删。
/// 插在总装原点，不加配合；不存总装（同模块「不替用户保存」）。
/// </remarks>
internal static class MymoooInsert
{
    public static QuickCommand Command { get; } = new(
        Key: "mymooo-insert",
        CommandName: StrenuaIdentity.Domain + ".mymooo.insert",
        Title: "插入标准件",
        Summary: "蚂蚁工场第 3 层·参数：按参数选型，选全了下载 3D 存进总装旁「外购件」并插入当前总装；没选全回还要选的参数",
        Usage: "「商品」填 strenua.mymooo.product 列出的商品号。先只给商品看要选哪些参数，再用「参数」填 码=值（逗号分开，如 M=8,L=20,XN=S11），"
               + "有多个型式的先填「型式」；知道完整料号可直接填「料号」。选全了才下载插入，文件以料号命名放在总装目录的「外购件」里，同料号复用。当前文档要是存过的总装。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("product", "商品", "商品号（strenua.mymooo.product / category keyword= 列出的号）", true),
            new QuickParameter("params", "参数", "参数码或参数名=值，逗号分开，如 M=8,L=20；区间参数直接填数；省略只看要选哪些参数", false, 2),
            new QuickParameter("type", "型式", "型式代号（商品有多个型式时必填，回执会列出）", false, 2),
            new QuickParameter("number", "料号", "完整料号（如 SBH1-M8-L20-S11），给了就按料号直选、不看 params", false, 2),
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
            return context => Insert(context, productId, pairs, type, number);
        },
        SelfReport: true);

    private const int OpenSilent = 1;

    private static QuickOutcome Insert(QuickCommandContext context, long productId, IReadOnlyList<KeyValuePair<string, string>> pairs, string? type, string? number)
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

            foreach (var (key, value) in pairs)
            {
                var parameters = selection["productParameters"] as JsonArray ?? [];
                var parameter = MymoooRules.Parameter(parameters, key)
                                ?? throw new QuickArgumentException($"没有参数「{key}」，要选的有：{string.Join("、", Coded(selection).Select(MymoooRules.Label))}。");
                var data = MymoooRules.SelectData(parameter, value);
                if (data is null)
                {
                    notes.Add($"{MymoooRules.Label(parameter)}={value} 不在可选范围（{MymoooRules.Choices(parameter)}），没选上");
                    continue;
                }

                var code = MymoooRules.Text(parameter, "paramCode");
                var list = (JsonArray)request["ProductQueryParamList"]!;
                foreach (var old in list.Where(item => MymoooRules.Text(item, "ParamCode") == code).ToList())
                    list.Remove(old);
                list.Add(new JsonObject
                {
                    ["ParamId"] = parameter["paramId"]?.DeepClone(),
                    ["ParamCode"] = code,
                    ["SelectData"] = data,
                    ["IsInclude"] = false,
                    ["IsSpecOption"] = false,
                });
                selection = MymoooClient.Select(request, null, cancellation);
            }
        }

        var shown = MymoooRules.Plain(MymoooRules.Text(selection, "number"));
        var numbers = selection["productNumbers"] as JsonArray ?? [];
        if (!MymoooRules.Complete(shown) || numbers.Count != 1)
            return QuickOutcome.Nothing(Remaining(productId, selection, shown, notes));

        var final = MymoooRules.Text(numbers[0], "number") is { Length: > 0 } listed ? listed : shown;
        var orderProduct = long.TryParse(MymoooRules.Text(numbers[0]?["querys"], "productId"), out var queried) ? queried : productId;
        return Place(context, orderProduct, final, notes);
    }

    /// <summary>没选全：当前料号 + 每个还没定的编码参数和它剩下的可选值。</summary>
    private static string Remaining(long productId, JsonNode selection, string shown, List<string> notes)
    {
        var text = new StringBuilder($"商品 {productId} 还没选全，没有插入。当前料号：{(shown.Length > 0 ? shown : "（先定型式）")}").AppendLine();
        var types = selection["productTypes"] as JsonArray ?? [];
        if (types.Count > 1 && !types.Any(t => t?["checked"]?.GetValue<bool>() == true))
            text.AppendLine($"型式（type=）：{TypeList(types)}");
        foreach (var parameter in Coded(selection))
        {
            var unit = MymoooRules.Text(parameter, "paramUnit");
            text.AppendLine($"  {MymoooRules.Label(parameter)}{(unit.Length > 0 ? $"（{unit}）" : string.Empty)}：{MymoooRules.Choices(parameter)}");
        }

        foreach (var note in notes)
            text.AppendLine("注意：" + note);
        text.Append("把还要选的用 params=码=值 补上再调（已选的一起带上）。");
        return text.ToString();
    }

    /// <summary>编码参数（组成料号的那几个）。</summary>
    private static IEnumerable<JsonNode> Coded(JsonNode selection)
        => (selection["productParameters"] as JsonArray ?? [])
            .Where(p => p is not null && MymoooRules.Text(p, "paramType") == MymoooRules.CodeParameter)
            .Select(p => p!);

    private static string TypeList(JsonArray types)
        => string.Join("、", types.Select(t => MymoooRules.Text(t, "typeCode") is var code && MymoooRules.Text(t, "typeDesc") is var desc && desc.Length > 0 && desc != code ? $"{code}（{desc}）" : code));

    /// <summary>料号定了：找或下载文件，插进当前总装原点。</summary>
    private static QuickOutcome Place(QuickCommandContext context, long productId, string number, List<string> notes)
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

        var reused = Existing(folder, name);
        var path = reused ?? Fetch(context, productId, number, folder, name);

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

        var componentName = api.CallString(component, "IComponent2", "get_Name2");
        var text = new StringBuilder($"插入标准件：{number} 已插进「{assemblyTitle}」原点，零部件名 {componentName}（没加配合，总装没存）。")
            .AppendLine().Append($"文件：{path}{(reused is null ? "（新下载）" : "（外购件里已有，复用）")}");
        foreach (var note in notes)
            text.AppendLine().Append("注意：" + note);
        return QuickOutcome.Ok(text.ToString(), new Dictionary<string, string> { ["component"] = componentName, ["number"] = number, ["path"] = path });
    }

    /// <summary>外购件里已有的同料号文件（零件或装配体）。</summary>
    private static string? Existing(string folder, string name)
        => new[] { ".SLDPRT", ".SLDASM" }.Select(extension => Path.Combine(folder, name + extension)).FirstOrDefault(File.Exists);

    /// <summary>下载 STP 到临时目录，导入后另存进外购件。</summary>
    private static string Fetch(QuickCommandContext context, long productId, string number, string folder, string name)
    {
        var api = context.Api;
        var application = context.Session.Application;
        var temp = Path.Combine(Path.GetTempPath(), "HistoryStrenua-mymooo", Guid.NewGuid().ToString("N"));
        try
        {
            context.Report("下载 3D");
            var step = MymoooClient.Download(productId, number, temp, context.Cancellation);
            if (!Path.GetExtension(step).StartsWith(".ST", StringComparison.OrdinalIgnoreCase))
                throw new QuickCommandException($"蚂蚁工场给的 3D 是 {Path.GetFileName(step)}，这版只接 STP。");

            context.Report("导入 STP");
            var importData = api.Call(application, "ISldWorks", "GetImportFileData", step);
            object?[] load = [step, "r", importData, 0];
            var document = api.Call(application, "ISldWorks", "LoadFile4", load)
                           ?? throw new QuickCommandException($"SolidWorks 导入 STP 没成（错误码 {load[3]}）。");
            try
            {
                var extension = api.CallInt(document, "IModelDoc2", "GetType") == SwConst.DocumentAssembly ? ".SLDASM" : ".SLDPRT";
                var path = Path.Combine(folder, name + extension);
                Directory.CreateDirectory(folder);
                object?[] save = [path, 0, SwConst.SaveSilent, null, 0, 0];
                var documentExtension = api.Call(document, "IModelDoc2", "get_Extension")!;
                if (!api.CallBool(documentExtension, "IModelDocExtension", "SaveAs", save) || !File.Exists(path))
                    throw new QuickCommandException($"另存 {path} 没成（错误码 {save[4]}）。");
                return path;
            }
            finally
            {
                // 另存后标题变成新文件名，关的时候现读。
                api.CallForCleanup(application, "ISldWorks", "CloseDoc", api.CallString(document, "IModelDoc2", "GetTitle"));
            }
        }
        finally
        {
            try
            {
                Directory.Delete(temp, true);
            }
            catch (IOException)
            {
                // 临时目录删不掉不影响结果。
            }
        }
    }
}
