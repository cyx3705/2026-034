using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「未标尺寸」（1.8.0，检查类；1.16.0 重写）：当前图纸页全部视图（轴测图除外）查有没有边、孔、圆弧没标尺寸，
/// 在 SolidWorks 里选中（高亮）漏标的边，并弹窗列出来。
/// </summary>
/// <remarks>
/// <para>判法与标注指令无关（1.16.0 用户定：别依赖标注那一侧），见 <see cref="DimensionCheckPlanner"/>：同一模型的全部视图连成一张尺寸网，
/// 没连进主网的边、孔心、圆弧圆心就是漏了位置尺寸；孔、圆弧另查大小。只读：不加、不删、不挪任何注解，只改选择。</para>
/// <para>查出缺漏也回执成功（检查本身做完了）。弹窗经总线调 Aurora 的 <c>aurora.ui.dialog</c>，不等人关窗（<see cref="QuickCommandContext.Post"/>）；
/// 没装 Aurora 时只进控制台。</para>
/// </remarks>
internal static class DimensionCheck
{
    public static QuickCommand Command { get; } = new(
        Key: "check-dimension",
        CommandName: StrenuaIdentity.Domain + ".check.dimension",
        Title: "未标尺寸",
        Summary: "当前图纸页全部视图（轴测图除外）查边、孔、圆弧有没有没标的尺寸（位置按尺寸网连通判，孔径、R 另查），选中漏标的边并弹窗列出，不改图。",
        Usage: "不用点视图：当前图纸页上全部视图（轴测图不查）一起检查，与标注按钮怎么标无关——同一零件的全部尺寸（各视图合起来）连成一张网，竖直边的左右位置、水平边的上下位置、孔心与 R1 以上圆弧圆心的两向位置，没连进这张网的就是漏标（与直边相切的圆弧、阵列「N x」跨度里的、对称轴上的、腰型孔另一端算已定）；孔要有孔标注或 Ø、圆弧要有 R（同尺寸有一个标了就算）；斜边要连着尺寸或两端都定了（C1 小倒角不查）。查完在 SolidWorks 里选中（高亮）漏标的边，并弹窗列出；不加、不删任何尺寸。",
        Run: Run);

    // swAnnotationType_e
    private const int AnnotationCenterLine = 15;

    /// <summary>弹窗正文最多列几条（多的写「另有 N 处」，全文在控制台）。</summary>
    private const int DialogLimit = 60;

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var api = context.Api;
        var document = HoleScan.ActiveDrawing(context);
        var views = HoleScan.SheetViews(api, document);
        if (views.Count == 0)
            return QuickOutcome.Ok("当前图纸页上没有视图，没有可检查的。");

        context.SetState("检查");
        var scans = new List<ScannedView>();
        var checks = new List<CheckView>();
        var axonometric = new List<string>();
        for (var i = 0; i < views.Count; i++)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var view = views[i];
            if (api.Call(view, "IView", "get_ReferencedDocument") is null)
                continue;
            var viewName = api.CallString(view, "IView", "get_Name");
            if (HoleScan.Frame(context, view).Axonometric)
            {
                axonometric.Add(viewName);
                continue;
            }

            context.Report($"未标尺寸：读视图 {i + 1}/{views.Count}「{viewName}」。");
            var scan = HoleScan.Scan(context, "未标尺寸", withLines: true, view: view, withArcs: true, quiet: true);
            var dimensions = DimensionScan.Read(api, scan).Select(item => item.Geometry).ToList();
            scans.Add(scan);
            checks.Add(new CheckView(viewName, HoleScan.ModelKey(api, view), scan.Geometry.Frame,
                scan.Lines, scan.Candidates, scan.Arcs, dimensions, CenterLines(api, view)));
        }

        var missing = DimensionCheckPlanner.Check(checks);
        api.Call(document, "IModelDoc2", "ClearSelection2", true);
        foreach (var item in missing)
        {
            var scan = scans[item.View];
            foreach (var index in item.Indexes)
            {
                var entity = item.Target switch
                {
                    CheckTarget.Line => scan.LineEdges[index],
                    CheckTarget.Hole => scan.Edges[index],
                    _ => scan.ArcEdges is { } arcs && index < arcs.Count ? arcs[index] : null,
                };
                if (entity is not null)
                    api.CallBool(scan.View, "IView", "SelectEntity", entity, true);
            }
        }

        api.Call(document, "IModelDoc2", "GraphicsRedraw2");
        var lines = new List<string>();
        if (axonometric.Count > 0)
            lines.Add($"轴测图不查：{string.Join("、", axonometric.Select(name => $"「{name}」"))}。");
        for (var v = 0; v < checks.Count; v++)
        {
            var here = missing.Where(item => item.View == v).ToList();
            lines.Add(here.Count == 0 ? $"视图「{checks[v].Name}」：没有漏标。" : $"视图「{checks[v].Name}」：{here.Count} 处漏标——");
            lines.AddRange(here.Select(item => "  · " + item.Text));
        }

        var head = missing.Count == 0
            ? $"未标尺寸：当前图纸页 {checks.Count} 个视图都没有查出漏标。"
            : $"未标尺寸：当前图纸页 {checks.Count} 个视图共 {missing.Count} 处漏标，已在 SolidWorks 里选中（高亮）漏标的边。";
        if (missing.Count > 0)
            Popup(context, "未标尺寸", head, lines);
        return QuickOutcome.Ok(head + Environment.NewLine + string.Join(Environment.NewLine, lines));
    }

    /// <summary>视图里中心线注解（对称轴）的线条。读不了的跳过。</summary>
    private static List<SheetSegment> CenterLines(SolidWorksApi api, object view)
    {
        var result = new List<SheetSegment>();
        foreach (var annotation in api.CallArray(view, "IView", "GetAnnotations"))
        {
            if (annotation is null)
                continue;
            try
            {
                if (api.CallInt(annotation, "IAnnotation", "GetType") == AnnotationCenterLine)
                    result.AddRange(Clearance.DisplayGeometry(api, annotation).Lines);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or System.Reflection.TargetException)
            {
            }
        }

        return result;
    }

    /// <summary>
    /// 检查结果弹窗（1.16.0，用户要：查完弹出来）：Aurora 的 content 弹窗，标题 + 一句摘要 + 列表正文，不等人关窗。
    /// 列表过长只列前 <see cref="DialogLimit"/> 条，全文在控制台。
    /// </summary>
    internal static void Popup(QuickCommandContext context, string title, string summary, IReadOnlyList<string> lines)
    {
        var shown = lines.Take(DialogLimit).ToList();
        if (lines.Count > DialogLimit)
            shown.Add($"……另有 {lines.Count - DialogLimit} 行，全文见控制台。");
        context.Post("aurora.ui.dialog kind=content"
                     + " title=" + HistoryVulcan.Core.Commands.CommandParser.QuoteArg(title)
                     + " body=" + HistoryVulcan.Core.Commands.CommandParser.QuoteArg(summary)
                     + " content=" + HistoryVulcan.Core.Commands.CommandParser.QuoteArg(string.Join("\n", shown)));
    }
}
