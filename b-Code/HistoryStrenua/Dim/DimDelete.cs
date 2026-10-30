namespace HistoryStrenua;

/// <summary>
/// 细粒度指令「删注解」<c>strenua.dim.delete</c>（1.20.0）：按名字删一个视图里的几个尺寸 / 注解。
/// </summary>
/// <remarks>
/// 走「重新标」共用的 <see cref="AnnotationEraser"/>：<c>EditDelete</c> 没有返回值，删完回读、还在就再删（最多三遍）；
/// 删掉里层尺寸 SolidWorks 会把外层往里收一层，删完把视图里其余尺寸放回原处。删坐标尺寸组里的一个，SolidWorks 会连带重建同组其余的，
/// 它们的名字可能变，回执提醒重读。
/// </remarks>
internal static class DimDelete
{
    public static FineCommand Command { get; } = new(
        Key: "dim-delete",
        CommandName: StrenuaIdentity.Domain + ".dim.delete",
        Title: "删注解",
        Summary: "按名字删一个视图里的尺寸或注解（可多个），删完回读核对，其余尺寸放回原处不被 SolidWorks 往里收。",
        Example: StrenuaIdentity.Domain + ".dim.delete view=工程图视图1 names=D1,D3",
        Parameters:
        [
            FineArgs.Text("view", "视图名（strenua.view.list 的 name 列；图纸页上的注释用图纸页名）", true, 0),
            FineArgs.Text("names", "注解名，逗号分开（strenua.dim.list 的 name 列），如 D1,D3", true, 1),
        ],
        Readonly: false,
        Bind: arguments =>
        {
            var view = FineArgs.Name(arguments, "view");
            var names = FineArgs.Names(arguments.GetString("names"), "names");
            return context => Run(context, view, names);
        });

    private static QuickOutcome Run(QuickCommandContext context, string viewName, IReadOnlyList<string> names)
    {
        var api = context.Api;
        var drawing = HoleScan.ActiveDrawing(context);
        var view = FineLookup.View(api, drawing, viewName);
        var wanted = names.ToHashSet(StringComparer.Ordinal);
        var present = FineLookup.Annotations(api, view).Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var missing = names.Where(name => !present.Contains(name)).ToList();
        if (missing.Count == names.Count)
            return QuickOutcome.Fail($"删注解：视图「{viewName}」里没有 {string.Join("、", missing)}（先用 strenua.dim.list 查名字）。");

        context.SetState("删注解");
        var (removed, leftover) = AnnotationEraser.Erase(
            context,
            drawing,
            () => FineLookup.Annotations(api, view).Where(item => wanted.Contains(item.Name)).Select(item => item.Annotation).ToList(),
            FineLookup.IsSheet(api, view) ? null : view);
        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        api.Call(drawing, "IModelDoc2", "GraphicsRedraw2");

        var after = FineLookup.Annotations(api, view).Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var stuck = names.Where(after.Contains).ToList();
        var renamed = present.Except(wanted).Count(name => !after.Contains(name));
        var lines = new List<string> { $"删注解：视图「{viewName}」删掉 {removed} 个。" };
        if (missing.Count > 0)
            lines.Add($"· 没有这些名字：{string.Join("、", missing)}。");
        if (leftover > 0 || stuck.Count > 0)
            lines.Add($"· 删了三遍还在：{string.Join("、", stuck)}。");
        if (renamed > 0)
            lines.Add($"· 另有 {renamed} 个没点名的也跟着没了或改了名（坐标尺寸组会整组重建），请重读 strenua.dim.list。");
        var message = string.Join(Environment.NewLine, lines);
        return missing.Count == 0 && stuck.Count == 0 ? QuickOutcome.Ok(message) : QuickOutcome.Fail(message);
    }
}
