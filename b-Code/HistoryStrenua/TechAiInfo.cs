using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 「AI 填写技术要求」交给模型的图纸基本信息（1.14.0）：图纸页、引用的模型（材料、包围盒、自定义属性、特征类型计数）、图上现有的技术要求。
/// </summary>
/// <remarks>
/// <para>截图给模型看形状与标注，这份 JSON 给它看截图里读不准或看不到的：材料、属性（Minerva 写的名称 / 表面处理……）、
/// 是钣金还是焊件（特征类型名一眼就看得出）、有没有螺纹孔与倒角。只读，不改图。</para>
/// <para>每一项读失败都跳过、不让整条指令失败：信息少一点模型照样能选，拦下来反而一条都插不上。</para>
/// </remarks>
internal static class TechAiInfo
{
    // swDocumentTypes_e
    private const int DocumentPart = 1;
    private const int DocumentAssembly = 2;

    private const int MaxProperties = 30;
    private const int MaxValueLength = 100;

    /// <summary>特征树里与「这是什么零件」无关的类型：文件夹、基准、草图、注解收纳……</summary>
    private static readonly HashSet<string> Housekeeping = new(StringComparer.OrdinalIgnoreCase)
    {
        "CommentsFolder", "FavoriteFolder", "HistoryFolder", "SelectionSetFolder", "SensorFolder", "DocsFolder", "DetailCabinet",
        "SurfaceBodyFolder", "SolidBodyFolder", "EqnFolder", "InkMarkupFolder", "EnvFolder", "MaterialFolder", "MateGroup",
        "OriginProfileFeature", "RefPlane", "RefAxis", "RefPoint", "CoordSys", "ProfileFeature", "3DProfileFeature",
        "MarkupCommentFolder", "BlockFolder", "LiveSectionFolder", "FtrFolder", "AnnotationFolder", "Attribute", "TableFolder",
    };

    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>读当前图纸页与它第一个模型视图引用的模型，返回一行 JSON（不缩进，直接拼进提示词）。</summary>
    public static string Read(QuickCommandContext context, object drawing)
    {
        var api = context.Api;
        var info = new Dictionary<string, object?>
        {
            ["图纸"] = Sheet(context, drawing),
        };
        if (FirstModelView(api, drawing) is { } view && Try(() => api.Call(view, "IView", "get_ReferencedDocument")) is { } model)
            info["模型"] = Model(context, model, Try(() => api.CallString(view, "IView", "get_ReferencedConfiguration")) ?? string.Empty);
        var notes = Try(() => TechNotePlacement.Notes(api, drawing));
        if (notes is { Count: > 0 } && Try(() => api.CallString(notes[0].Note, "INote", "GetText")) is { Length: > 0 } existing)
            info["图上现有技术要求"] = string.Join(" ", TechAiEdit.Parse(existing).Items.Select(TechAiEdit.Plain));
        return JsonSerializer.Serialize(info, Options);
    }

    private static Dictionary<string, object?> Sheet(QuickCommandContext context, object drawing)
    {
        var api = context.Api;
        var result = new Dictionary<string, object?> { ["文件"] = Try(() => api.CallString(drawing, "IModelDoc2", "GetTitle")) };
        if (Try(() => api.Call(drawing, "IDrawingDoc", "GetCurrentSheet")) is { } sheet)
        {
            result["图纸页"] = Try(() => api.CallString(sheet, "ISheet", "GetName"));
            var properties = Try(() => api.CallDoubles(sheet, "ISheet", "GetProperties2")) ?? [];
            if (properties.Length > 6)
                result["图幅mm"] = $"{Mm(properties[5])}×{Mm(properties[6])}";
            if (properties.Length > 3 && properties[2] > 0 && properties[3] > 0)
                result["比例"] = $"{Number(properties[2])}:{Number(properties[3])}";
        }

        var views = Try(() => HoleScan.SheetViews(api, drawing)) ?? [];
        result["视图"] = views.Select(view => Try(() => api.CallString(view, "IView", "get_Name"))).Where(name => !string.IsNullOrEmpty(name)).ToList();
        return result;
    }

    private static Dictionary<string, object?> Model(QuickCommandContext context, object model, string configuration)
    {
        var api = context.Api;
        var type = Try(() => api.CallInt(model, "IModelDoc2", "GetType"));
        var result = new Dictionary<string, object?>
        {
            ["文件"] = Try(() => api.CallString(model, "IModelDoc2", "GetTitle")),
            ["类型"] = type switch { DocumentPart => "零件", DocumentAssembly => "装配体", _ => "其他" },
            ["配置"] = configuration,
        };

        if (type == DocumentPart)
        {
            // GetMaterialPropertyName2(配置, out 材料库) 返回材料名；没指定材料是空串。
            object?[] arguments = [configuration, null];
            if (Try(() => api.CallString(model, "IPartDoc", "GetMaterialPropertyName2", arguments)) is { Length: > 0 } material)
                result["材料"] = material;
            var box = Try(() => api.CallDoubles(model, "IPartDoc", "GetPartBox", true)) ?? [];
            if (box.Length >= 6)
                result["包围盒mm"] = $"{Mm(box[3] - box[0])}×{Mm(box[4] - box[1])}×{Mm(box[5] - box[2])}";
        }
        else if (type == DocumentAssembly && Try(() => api.CallInt(model, "IAssemblyDoc", "GetComponentCount", true)) is { } count)
        {
            result["零部件数"] = count;
        }

        var properties = Properties(api, model, configuration);
        if (properties.Count > 0)
            result["属性"] = properties;
        var features = Features(context, model);
        if (features.Count > 0)
            result["特征类型计数"] = features;
        return result;
    }

    /// <summary>文件级与配置级自定义属性（配置级覆盖同名的文件级），取解析后的值。</summary>
    private static Dictionary<string, string> Properties(SolidWorksApi api, object model, string configuration)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Try(() => api.Call(model, "IModelDoc2", "get_Extension")) is not { } extension)
            return result;
        foreach (var scope in new[] { string.Empty, configuration }.Distinct())
        {
            if (Try(() => api.Call(extension, "IModelDocExtension", "get_CustomPropertyManager", scope)) is not { } manager)
                continue;
            foreach (var name in Try(() => api.CallArray(manager, "ICustomPropertyManager", "GetNames")) ?? [])
            {
                if (name is not string key || key.Length == 0 || result.Count >= MaxProperties && !result.ContainsKey(key))
                    continue;
                // Get5(名, 用缓存, out 原值, out 解析值, out 是否已解析)；解析值空时退回原值。
                object?[] arguments = [key, false, null, null, null];
                if (Try(() => api.Call(manager, "ICustomPropertyManager", "Get5", arguments)) is null && arguments[2] is null)
                    continue;
                var value = (arguments[3] as string) is { Length: > 0 } resolved ? resolved : arguments[2] as string ?? string.Empty;
                value = value.Trim();
                if (value.Length > 0)
                    result[key] = value.Length <= MaxValueLength ? value : value[..MaxValueLength] + "…";
            }
        }

        return result;
    }

    /// <summary>特征树里各类型出现几次（不进子特征），按次数排。</summary>
    private static Dictionary<string, int> Features(QuickCommandContext context, object model)
    {
        var api = context.Api;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var feature = Try(() => api.Call(model, "IModelDoc2", "FirstFeature"));
        for (var guard = 0; feature is not null && guard < 5000; guard++)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var current = feature;
            if (Try(() => api.CallString(current, "IFeature", "GetTypeName2")) is { Length: > 0 } name && !Housekeeping.Contains(name))
                counts[name] = counts.GetValueOrDefault(name) + 1;
            feature = Try(() => api.Call(current, "IFeature", "GetNextFeature"));
        }

        return counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>当前图纸页上第一个引用了模型的视图（非轴测优先没有意义：只为找到模型）。</summary>
    private static object? FirstModelView(SolidWorksApi api, object drawing)
        => (Try(() => HoleScan.SheetViews(api, drawing)) ?? [])
            .FirstOrDefault(view => Try(() => api.Call(view, "IView", "get_ReferencedDocument")) is not null);

    private static string Mm(double meters) => Number(Math.Round(meters * 1000, 1));

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>读不到就是没有：COM 拒绝、方法不存在（旧版 SW 没有 Get5）都按缺省处理。取消照旧抛出。</summary>
    private static T? Try<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return default;
        }
    }
}
