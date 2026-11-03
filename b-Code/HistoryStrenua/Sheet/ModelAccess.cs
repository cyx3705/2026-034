using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 建模、装配、蚂蚁工场共用的零件 / 总装动作（1.38.0）：认活动文档、摆零部件、固定。
/// </summary>
internal static class ModelAccess
{
    /// <summary>当前活动文档是零件，否则抛可纠正的错。</summary>
    public static object ActivePart(QuickCommandContext context)
        => Active(context, SwConst.DocumentPart, "零件", "strenua.model.new 新建或 strenua.doc.open 打开零件");

    /// <summary>当前活动文档是总装，否则抛可纠正的错。</summary>
    public static object ActiveAssembly(QuickCommandContext context)
        => Active(context, SwConst.DocumentAssembly, "装配体", "strenua.doc.open 打开总装");

    private static object Active(QuickCommandContext context, int type, string kind, string hint)
    {
        var document = context.Session.ActiveDocument() ?? throw new QuickCommandException($"SolidWorks 里没有打开的文档：先 {hint}。");
        if (context.Api.CallInt(document, "IModelDoc2", "GetType") != type)
            throw new QuickCommandException($"活动文档「{context.Api.CallString(document, "IModelDoc2", "GetTitle")}」不是{kind}：先 {hint}。");
        return document;
    }

    /// <summary>
    /// 把零部件摆到 <paramref name="origin"/>（总装 mm）、零件三根轴朝 <paramref name="axes"/>，再按 <paramref name="fix"/> 固定或浮动。
    /// </summary>
    /// <remarks>
    /// 固定着的零部件改 <c>Transform2</c> 不生效，要先解除固定再摆。有配合的零部件摆完下次重建会被配合拉回去，回执里提醒（这里不删配合）。
    /// <c>AddComponent5</c> 给的坐标是零部件<b>包围盒中心</b>（真机：蚂蚁工场模组插在原点却偏了半个长度），所以插入后一律用这里按零件原点重摆。
    /// </remarks>
    public static void Place(QuickCommandContext context, object assembly, object component, double[,] axes, (double X, double Y, double Z) origin, bool fix)
    {
        var api = context.Api;
        Select(api, assembly, component);
        api.Call(assembly, "IAssemblyDoc", "UnfixComponent");
        var math = api.Call(context.Session.Application, "ISldWorks", "GetMathUtility")
                   ?? throw new QuickCommandException("SolidWorks 没有返回 MathUtility。");
        var transform = api.Call(math, "IMathUtility", "CreateTransform", ModelSpec.TransformData(axes, origin))
                        ?? throw new QuickCommandException("建不出变换矩阵。");
        api.Call(component, "IComponent2", "set_Transform2", transform);
        if (fix)
        {
            Select(api, assembly, component);
            api.Call(assembly, "IAssemblyDoc", "FixComponent");
        }

        api.ClearSelection(assembly);
    }

    /// <summary>零部件现在的原点（总装 mm）与三根轴。</summary>
    public static ((double X, double Y, double Z) Origin, double[,] Axes) Frame(SolidWorksApi api, object component)
    {
        var data = api.Call(component, "IComponent2", "get_Transform2") is { } transform
            ? api.CallDoubles(transform, "IMathTransform", "get_ArrayData")
            : [];
        var axes = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        if (data.Length < 12)
            return ((0, 0, 0), axes);
        for (var i = 0; i < 9; i++)
            axes[i / 3, i % 3] = Math.Round(data[i], 9);
        return ((data[9] * 1000, data[10] * 1000, data[11] * 1000), axes);
    }

    /// <summary>零部件在总装里的包围盒（mm：minX,minY,minZ,maxX,maxY,maxZ）；读不到为空。</summary>
    public static double[] Box(SolidWorksApi api, object component)
        => api.CallDoubles(component, "IComponent2", "GetBox", false, false) is { Length: 6 } box ? box.Select(value => value * 1000).ToArray() : [];

    /// <summary>零部件是否固定。</summary>
    public static bool Fixed(SolidWorksApi api, object component) => api.CallBool(component, "IComponent2", "IsFixed");

    /// <summary>总装顶层零部件按名字（<c>Name2</c>，如「底板-1」）找；找不到抛参数错并列出有哪些。</summary>
    public static object Component(SolidWorksApi api, object assembly, string name)
    {
        var all = api.CallArray(assembly, "IAssemblyDoc", "GetComponents", true);
        foreach (var component in all)
        {
            if (string.Equals(api.CallString(component, "IComponent2", "get_Name2"), name, StringComparison.OrdinalIgnoreCase))
                return component;
        }

        var names = all.Select(component => api.CallString(component, "IComponent2", "get_Name2")).Take(30);
        throw new QuickArgumentException($"总装里没有零部件「{name}」，顶层有：{string.Join("、", names)}{(all.Length > 30 ? "…" : string.Empty)}。");
    }

    private static void Select(SolidWorksApi api, object assembly, object component)
    {
        api.ClearSelection(assembly);
        api.Call(component, "IComponent2", "Select4", false, null, false);
    }

    public static string Format((double X, double Y, double Z) point)
        => $"({ModelSpec.Format(point.X)}, {ModelSpec.Format(point.Y)}, {ModelSpec.Format(point.Z)})";
}
