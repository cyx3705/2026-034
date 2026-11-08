using System.Text.Json.Nodes;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 蚂蚁工场标准件的选型与取文件（1.44.0 从 <c>mymooo.insert</c> 下沉，一键螺丝共用）：逐个参数收窄选型、料号定了找「外购件」里的文件或下载 STP 导入另存。
/// </summary>
/// <remarks>
/// 文件放总装目录下的「外购件」子文件夹（用户定，同 HistoryMinerva 外购件的去处），以料号命名；同料号已有就直接复用、不重下。
/// STP 用 <c>LoadFile4</c> 导入后另存为 SolidWorks 文件（导入出零件存 .SLDPRT、出装配体存 .SLDASM），STP 本身下在临时目录、用完删。
/// </remarks>
internal static class MymoooParts
{
    /// <summary>按 码=值 逐个选，每选一个回传一次（服务器逐步收窄）；选不上的记进 <paramref name="notes"/>。回最后一次选型结果。</summary>
    public static JsonNode Narrow(JsonObject request, JsonNode selection, IReadOnlyList<KeyValuePair<string, string>> pairs, List<string> notes, CancellationToken cancellation)
    {
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

        return selection;
    }

    /// <summary>
    /// 一键螺丝（1.44.0）用：只有一个型式的商品按 码=值 选型。选全回料号与下单商品号；没选全回 null（<paramref name="selection"/> 是最后的选型结果，可读剩下的可选值）。
    /// </summary>
    public static (string Number, long Product)? Resolve(long productId, IReadOnlyList<KeyValuePair<string, string>> pairs, CancellationToken cancellation, out JsonNode selection, out List<string> notes)
    {
        notes = [];
        var request = new JsonObject { ["ProductId"] = productId, ["ProductQueryParamList"] = new JsonArray(), ["Qty"] = 1 };
        selection = Narrow(request, MymoooClient.Select(request, null, cancellation), pairs, notes, cancellation);
        var shown = MymoooRules.Plain(MymoooRules.Text(selection, "number"));
        var numbers = selection["productNumbers"] as JsonArray ?? [];
        if (!MymoooRules.Complete(shown) || numbers.Count != 1)
            return null;
        var final = MymoooRules.Text(numbers[0], "number") is { Length: > 0 } listed ? listed : shown;
        return (final, long.TryParse(MymoooRules.Text(numbers[0]?["querys"], "productId"), out var queried) ? queried : productId);
    }

    /// <summary>料号的文件：总装旁「外购件」里有就用，没有就下载另存（同 <c>mymooo.insert</c>）。回路径与是不是新下的。</summary>
    public static (string Path, bool Downloaded) FileFor(QuickCommandContext context, long productId, string number, string assemblyPath)
    {
        var folder = Path.Combine(Path.GetDirectoryName(assemblyPath)!, MymoooRules.Folder);
        var name = MymoooRules.FileName(number);
        return Existing(folder, name) is { } existing ? (existing, false) : (Fetch(context, productId, number, folder, name), true);
    }

    /// <summary>编码参数（组成料号的那几个）。</summary>
    public static IEnumerable<JsonNode> Coded(JsonNode selection)
        => (selection["productParameters"] as JsonArray ?? [])
            .Where(p => p is not null && MymoooRules.Text(p, "paramType") == MymoooRules.CodeParameter)
            .Select(p => p!);

    /// <summary>外购件里已有的同料号文件（零件或装配体）。</summary>
    public static string? Existing(string folder, string name)
        => new[] { ".SLDPRT", ".SLDASM" }.Select(extension => Path.Combine(folder, name + extension)).FirstOrDefault(File.Exists);

    /// <summary>
    /// 1.37.0 导入的装配体：3D Interconnect 开着时导入件是「链接到 STP」的，另存出来的总装只引用 SolidWorks 临时目录 <c>swx&lt;进程号&gt;\IC~~</c> 里的缓存，
    /// STP 又已删掉——临时目录一清就打不开。读文件的引用（不打开），引用里有 <c>\IC~~\</c> 就算坏的
    /// （不按「在临时目录下」判：工程本身可能就放在 %TEMP% 里）。
    /// </summary>
    public static bool Broken(SolidWorksApi api, object application, string path)
        => path.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase)
           && api.CallArray(application, "ISldWorks", "GetDocumentDependencies2", path, false, true, false)
               .OfType<string>()
               .Any(reference => reference.Contains(@"\IC~~\", StringComparison.OrdinalIgnoreCase));

    // swUserPreferenceToggle_e
    private const int Enable3DInterconnect = 691;
    private const int AlwaysUseDefaultTemplates = 111;

    /// <summary>
    /// 下载 STP 到临时目录，导入后另存进外购件。装配体的每个零件、子装配也另存成真文件，放「外购件\料号\」。
    /// </summary>
    /// <remarks>
    /// 导入时临时关掉 3D Interconnect（开着时导入件只是链接，见 <see cref="Broken"/>）、打开「总是使用默认模板」
    /// （用户反馈：选型完要人手点确认导入用的装配体模板，不点就卡住），用完恢复原值。
    /// </remarks>
    public static string Fetch(QuickCommandContext context, long productId, string number, string folder, string name)
    {
        var api = context.Api;
        var application = context.Session.Application;
        var temp = Path.Combine(Path.GetTempPath(), "HistoryStrenua-mymooo", Guid.NewGuid().ToString("N"));
        var interconnect = api.CallBool(application, "ISldWorks", "GetUserPreferenceToggle", Enable3DInterconnect);
        var defaults = api.CallBool(application, "ISldWorks", "GetUserPreferenceToggle", AlwaysUseDefaultTemplates);
        try
        {
            context.Report("下载 3D");
            var step = MymoooClient.Download(productId, number, temp, context.Cancellation);
            if (!Path.GetExtension(step).StartsWith(".ST", StringComparison.OrdinalIgnoreCase))
                throw new QuickCommandException($"蚂蚁工场给的 3D 是 {Path.GetFileName(step)}，这版只接 STP。");

            context.Report("导入 STP");
            api.Call(application, "ISldWorks", "SetUserPreferenceToggle", Enable3DInterconnect, false);
            api.Call(application, "ISldWorks", "SetUserPreferenceToggle", AlwaysUseDefaultTemplates, true);
            var importData = api.Call(application, "ISldWorks", "GetImportFileData", step);
            object?[] load = [step, "r", importData, 0];
            var document = api.Call(application, "ISldWorks", "LoadFile4", load)
                           ?? throw new QuickCommandException($"SolidWorks 导入 STP 没成（错误码 {load[3]}）。");
            try
            {
                Directory.CreateDirectory(folder);
                var isAssembly = api.CallInt(document, "IModelDoc2", "GetType") == SwConst.DocumentAssembly;
                if (isAssembly)
                {
                    context.Report("另存零件");
                    SaveChildren(context, document, Path.Combine(folder, name));
                }

                var path = Path.Combine(folder, name + (isAssembly ? ".SLDASM" : ".SLDPRT"));
                SaveAs(api, document, path);
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
            api.CallForCleanup(application, "ISldWorks", "SetUserPreferenceToggle", Enable3DInterconnect, interconnect);
            api.CallForCleanup(application, "ISldWorks", "SetUserPreferenceToggle", AlwaysUseDefaultTemplates, defaults);
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

    /// <summary>导入出来的零件、子装配逐个另存到 <paramref name="folder"/>：先最深的一层（零件），再往上，总装引用随之指到新文件。</summary>
    public static void SaveChildren(QuickCommandContext context, object assembly, string folder)
    {
        var api = context.Api;
        Directory.CreateDirectory(folder);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var children = api.CallArray(assembly, "IAssemblyDoc", "GetComponents", false)
            .Select(component => (Depth: api.CallString(component, "IComponent2", "get_Name2").Count(c => c == '/'), Model: api.Call(component, "IComponent2", "GetModelDoc2")))
            .Where(child => child.Model is not null)
            .OrderByDescending(child => child.Depth)
            .ToList();
        foreach (var (_, model) in children)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var title = api.CallString(model!, "IModelDoc2", "GetTitle");
            if (!seen.Add(title))
                continue;
            var extension = api.CallInt(model!, "IModelDoc2", "GetType") == SwConst.DocumentAssembly ? ".SLDASM" : ".SLDPRT";
            var stem = MymoooRules.FileName(StripExtensions(title));
            var file = stem;
            for (var i = 2; !used.Add(file); i++)
                file = $"{stem}-{i}";
            SaveAs(api, model!, Path.Combine(folder, file + extension));
        }
    }

    /// <summary>导入件标题常是「XX-PT.STP.SLDPRT」「XX.STEP」，去掉这些扩展名。</summary>
    public static string StripExtensions(string title)
    {
        var stem = title;
        while (Path.GetExtension(stem).ToUpperInvariant() is ".SLDPRT" or ".SLDASM" or ".STP" or ".STEP")
            stem = Path.GetFileNameWithoutExtension(stem);
        return stem.Length > 0 ? stem : title;
    }

    public static void SaveAs(SolidWorksApi api, object document, string path)
    {
        object?[] save = [path, 0, SwConst.SaveSilent, null, 0, 0];
        var extension = api.Call(document, "IModelDoc2", "get_Extension")!;
        if (!api.CallBool(extension, "IModelDocExtension", "SaveAs", save) || !File.Exists(path))
            throw new QuickCommandException($"另存 {path} 没成（错误码 {save[4]}）。");
    }
}
