using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「注解重叠」（1.12.0，检查类）：当前图纸页全部视图里的尺寸、孔标注、注释等，文字有没有互相压、压到别的注解的线、
/// 压到视图轮廓线与孔、压到图纸注释与图框线、出了图框；列进控制台并在 SolidWorks 里选中。
/// </summary>
/// <remarks>
/// <para>怎么算压到见 <see cref="AnnotationCheckPlanner"/>（与避障同一判法）。视图的线取「孔类」同一套识别（<see cref="HoleScan.Scan"/>：直边、
/// 曲线近似段、孔圆近似成八边形）；轴测图不读线（斜着的边认不全），只查它的注解之间。</para>
/// <para>只读：不挪不删，只改选择。查出来也回执成功，消息里写明几处。</para>
/// </remarks>
internal static class OverlapCheck
{
    public static QuickCommand Command { get; } = new(
        Key: "check-overlap",
        CommandName: StrenuaIdentity.Domain + ".check.overlap",
        Title: "注解重叠",
        Summary: "当前图纸页全部视图查尺寸、孔标注、注释的文字有没有互相压、压线、出图框，列出并在 SolidWorks 里选中，不改图。",
        Usage: "不用点视图：当前图纸页上全部视图里的尺寸、孔标注、注释等逐个查——文字和别的注解的文字重叠、压在别的注解的线（尺寸线、尺寸界线、引线、中心符号线）上、压在视图轮廓线或孔上（相邻视图的也算）、压在图纸上的注释（技术要求、标题栏的字）或图框 / 标题栏线上、出了图框。判法与「避障」相同，自己的线不算。查出来的列进控制台，并在 SolidWorks 里选中压到别处的那些注解；不挪、不删任何东西。轴测图只查注解之间。",
        Run: Run);

    // swAnnotationType_e.swDisplayDimension
    private const int AnnotationDimension = 4;

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var api = context.Api;
        var document = HoleScan.ActiveDrawing(context);
        var views = HoleScan.SheetViews(api, document);
        if (views.Count == 0)
            return QuickOutcome.Ok("当前图纸页上没有视图，没有可检查的。");

        context.SetState("检查");
        var annotations = new List<CheckedAnnotation>();
        var objects = new List<object>();
        var viewLines = new List<SheetSegment>();
        var axonometric = new List<string>();
        for (var i = 0; i < views.Count; i++)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var view = views[i];
            var viewName = DrawingSheet.Name(api, view);
            context.Report($"注解重叠：读视图 {i + 1}/{views.Count}「{viewName}」。");
            if (api.Call(view, "IView", "get_ReferencedDocument") is not null)
            {
                if (HoleScan.Frame(context, view).Axonometric)
                {
                    axonometric.Add(viewName);
                }
                else
                {
                    var scan = HoleScan.Scan(context, "注解重叠", withLines: true, view: view);
                    viewLines.AddRange(scan.Lines);
                    viewLines.AddRange(scan.CurveSegments);
                    foreach (var hole in scan.Candidates)
                        viewLines.AddRange(ClearancePlanner.Octagon(new SheetPoint(hole.X, hole.Y), hole.Radius));
                }
            }

            foreach (var annotation in api.CallArray(view, "IView", "GetAnnotations"))
            {
                if (annotation is null)
                    continue;
                try
                {
                    var (lines, texts) = Clearance.DisplayGeometry(api, annotation);
                    if (texts.Count == 0 && lines.Count == 0)
                        continue;
                    var type = api.CallInt(annotation, "IAnnotation", "GetType");
                    annotations.Add(new CheckedAnnotation(objects.Count, viewName, api.CallString(annotation, "IAnnotation", "GetName"), Kind(api, annotation, type), lines, texts));
                    objects.Add(annotation);
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or System.Reflection.TargetException)
                {
                    // 读不动显示数据的注解（对象已失效）跳过。
                }
            }
        }

        var sheetBoxes = DrawingSheet.Annotations(api, document).Select(item => (item.Name, item.Rect)).ToList();
        var frame = DrawingSheet.FrameRect(api, document)?.Inflate(DrawingSheet.TextFrameMargin);
        var issues = AnnotationCheckPlanner.Overlaps(annotations, viewLines, DrawingSheet.FrameLines(api, document), sheetBoxes, frame);

        api.Call(document, "IModelDoc2", "ClearSelection2", true);
        foreach (var index in issues.Select(issue => issue.Index).Distinct())
            api.CallBool(objects[index], "IAnnotation", "Select3", true, null);
        api.Call(document, "IModelDoc2", "GraphicsRedraw2");

        var scope = $"当前图纸页 {views.Count} 个视图里 {annotations.Count} 个注解"
            + (axonometric.Count > 0 ? $"（轴测图 {string.Join("、", axonometric.Select(name => $"「{name}」"))} 只查注解之间）" : string.Empty);
        if (issues.Count == 0)
            return QuickOutcome.Ok($"注解重叠：{scope}，没有压到别处的。");

        var report = issues
            .GroupBy(issue => annotations[issue.Index].View)
            .SelectMany(group => new[] { $"视图「{group.Key}」：{group.Count()} 处——" }
                .Concat(group.Select(issue => $"  · {AnnotationCheckPlanner.Label(annotations[issue.Index])}：{issue.Reason}")));
        return QuickOutcome.Ok($"注解重叠：{scope}，{issues.Count} 处压到别处（{issues.Select(issue => issue.Index).Distinct().Count()} 个注解），已在 SolidWorks 里选中。"
            + Environment.NewLine + string.Join(Environment.NewLine, report));
    }

    /// <summary>注解类名（回执用）：尺寸里再分出孔标注。</summary>
    internal static string Kind(SolidWorksApi api, object annotation, int type)
        => type == AnnotationDimension
            && api.Call(annotation, "IAnnotation", "GetSpecificAnnotation") is { } display
            && api.CallBool(display, "IDisplayDimension", "IsHoleCallout")
                ? "孔标注"
                : AnnotationCheckPlanner.KindName(type);
}
