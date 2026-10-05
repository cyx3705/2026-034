using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「悬空标注」（1.12.0，检查类）：当前图纸页上附着丢了的尺寸与注解（模型改过、中心符号线删过以后常见），列进控制台并在 SolidWorks 里选中。
/// </summary>
/// <remarks>
/// <para>判法同「孔位尺寸」重标时删悬空尺寸（1.3.0 DEC-012）：<c>IAnnotation.IsDangling</c>；尺寸再加两条保险——附着对象读出来是空的、
/// 附着类型码是 0（真机：删掉中心符号线后挂在上面的尺寸就是这样，<c>IsDangling</c> 不一定报）。</para>
/// <para>范围：当前图纸页的全部视图（轴测图也查）和图纸本身。只读：不删不挪，只改选择。查出来也回执成功，消息里写明几处。</para>
/// </remarks>
internal static class DanglingCheck
{
    public static QuickCommand Command { get; } = new(
        Key: "check-dangling",
        CommandName: StrenuaIdentity.Domain + ".check.dangling",
        Title: "悬空标注",
        Summary: "当前图纸页全部视图与图纸上查附着丢了的尺寸和注解（悬空），列出并在 SolidWorks 里选中，不改图。",
        Usage: "不用点视图：当前图纸页上全部视图（含轴测图）和图纸本身逐个查尺寸、孔标注、注释、中心符号线等注解，附着的边 / 点丢了（SolidWorks 显示成悬空色的那种，模型改过或中心符号线删过以后常见）就列进控制台，并在 SolidWorks 里选中它们，按 Delete 即可删掉或手工重新附着；不删、不挪任何东西。",
        Run: Run);

    // swAnnotationType_e.swDisplayDimension
    private const int AnnotationDimension = 4;

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var api = context.Api;
        var document = HoleScan.ActiveDrawing(context);
        var views = HoleScan.SheetViews(api, document);
        var viewCount = views.Count;
        if (api.Call(document, "IDrawingDoc", "GetFirstView") is { } sheet)
            views.Insert(0, sheet);

        context.SetState("检查");
        var found = new List<(string View, string Label, object Annotation)>();
        var total = 0;
        foreach (var view in views)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var viewName = DrawingSheet.Name(api, view);
            foreach (var annotation in api.CallArray(view, "IView", "GetAnnotations"))
            {
                if (annotation is null)
                    continue;
                total++;
                try
                {
                    var type = api.CallInt(annotation, "IAnnotation", "GetType");
                    if (!Dangling(api, annotation, type))
                        continue;
                    var name = api.CallString(annotation, "IAnnotation", "GetName");
                    var kind = OverlapCheck.Kind(api, annotation, type);
                    found.Add((viewName, name.Length > 0 ? $"{kind} {name}" : kind, annotation));
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or System.Reflection.TargetException)
                {
                    // 读不动的注解（对象已失效）不算悬空，跳过。
                }
            }
        }

        api.Call(document, "IModelDoc2", "ClearSelection2", true);
        foreach (var (_, _, annotation) in found)
            api.CallBool(annotation, "IAnnotation", "Select3", true, null);
        api.Call(document, "IModelDoc2", "GraphicsRedraw2");

        var scope = $"当前图纸页 {viewCount} 个视图与图纸上 {total} 个注解";
        if (found.Count == 0)
            return QuickOutcome.Ok($"悬空标注：{scope}，没有悬空的。");
        var lines = found
            .GroupBy(item => item.View)
            .SelectMany(group => new[] { $"视图「{group.Key}」：{group.Count()} 个——" }.Concat(group.Select(item => "  · " + item.Label)));
        return QuickOutcome.Ok($"悬空标注：{scope}，{found.Count} 个悬空，已在 SolidWorks 里选中（按 Delete 删掉，或手工重新附着）。"
            + Environment.NewLine + string.Join(Environment.NewLine, lines));
    }

    /// <summary>悬空了没有：<c>IsDangling</c>；尺寸再看附着对象有没有空的、类型码有没有 0。</summary>
    private static bool Dangling(SolidWorksApi api, object annotation, int type)
        => api.CallBool(annotation, "IAnnotation", "IsDangling")
            || (type == AnnotationDimension
                && (api.CallArray(annotation, "IAnnotation", "GetAttachedEntities3").Any(entity => entity is null)
                    || api.CallArray(annotation, "IAnnotation", "GetAttachedEntityTypes").Any(code => Convert.ToInt32(code) == 0)));
}
