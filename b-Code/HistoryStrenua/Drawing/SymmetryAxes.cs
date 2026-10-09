using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「对称轴」（1.14.1，出图类，用户定为单独的全图指令）：当前图纸页全部视图（轴测图跳过）里，边和孔整个关于一根轴对称的，
/// 插上对称轴（中心线）；已经有落在轴上的中心线就不再加。
/// </summary>
/// <remarks>
/// 判法见 <see cref="SymmetryPlanner"/>（要有孔不在轴上，或有要定位的圆弧圆心（1.14.2）；孔全在轴上、又没有这样的圆弧的视图不加——每个矩形都对称，全加上就满图中心线）。
/// 「孔位尺寸」以对称轴为基准时也调这里的 <see cref="Insert"/>，补上它要的那根，所以单按孔类指令不会缺轴。
/// </remarks>
internal static class SymmetryAxes
{
    public static QuickCommand Command { get; } = new(
        Key: "symmetry-axes",
        CommandName: StrenuaIdentity.Domain + ".drawing.symmetry",
        Title: "对称轴",
        Summary: "当前图纸页全部视图（轴测图跳过）：边和孔整个左右或上下对称、且有孔不在轴上或有要定位的圆弧圆心的，加对称轴（中心线），已有的不再加。",
        Usage: "不用点视图：当前图纸页上的全部视图（轴测图跳过）逐个看，视图里的边和孔整个左右（或上下）对称、且有孔不在对称轴上（或有要定位的圆弧圆心：两端不都与直线相切、不与孔同心的圆弧）的，选一对对称的边插一根对称轴（中心线，两头伸出零件一点）；已经有落在轴上的中心线不再加。孔全在轴上又没有这样的圆弧、或同位置是别种孔的视图不加。「圆心位置」也会自己补它要的那根。「孔位尺寸」遇到对称的视图也会自己补上它要的那根并以它为基准标。某个视图没成不中断，最后汇总。",
        Run: Run);

    // swAnnotationType_e.swCenterLine
    private const int CenterLineAnnotation = 15;

    internal static QuickOutcome Run(QuickCommandContext context) => FilletAll.EachView(context, "对称轴", RunView);

    private static QuickOutcome RunView(QuickCommandContext context, object? view)
    {
        var api = context.Api;
        var scan = HoleScan.Scan(context, "对称轴", withLines: true, view: view, withArcs: true);
        var holes = HoleCalloutPlanner.Recognize(scan.Candidates);
        var axes = SymmetryPlanner.Axes(scan.Lines, scan.CurveSegments, holes, ArcCenterPlanner.Locatable(scan.Arcs, scan.Lines, holes));
        if (axes.Count == 0)
            return QuickOutcome.Ok($"视图「{scan.ViewName}」不对称（或孔都在轴上、没有要定位的圆弧），没有加对称轴。");
        var names = string.Join("、", axes.Select(axis => axis.Name));
        var (added, failed) = Insert(api, scan, axes);
        api.Call(scan.Document, "IModelDoc2", "GraphicsRedraw2");
        var message = $"视图「{scan.ViewName}」：{names}"
            + (added > 0 ? $"，加了 {added} 根" : string.Empty)
            + (axes.Count - added - failed > 0 ? $"，{axes.Count - added - failed} 根已有" : string.Empty)
            + (failed > 0 ? $"，{failed} 根 SolidWorks 没有插上" : string.Empty) + "。";
        return failed > 0 && added == 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }

    /// <summary>
    /// 视图里还没有落在这根轴上的中心线，就选左右（上下）一对对称的直边，<c>IDrawingDoc.InsertCenterLine2</c>。
    /// 真机（SW 2025 SP5，XJ05A-01 安装板）：生成在两边正中、两头各伸出 5 mm，与用户手工插的那根完全一致；中心线注解类型 15，
    /// 位置读回为空，线段在显示数据里（线型 6）。
    /// </summary>
    /// <returns>加上的根数与没插上的根数（已有的两样都不算）。</returns>
    internal static (int Added, int Failed) Insert(SolidWorksApi api, ScannedView scan, IReadOnlyList<SymmetryAxis> axes)
    {
        var drawn = CenterLines(api, scan.View);
        var (added, failed) = (0, 0);
        foreach (var axis in axes)
        {
            if (SymmetryPlanner.Drawn(axis, drawn))
                continue;
            api.Call(scan.Document, "IModelDoc2", "ClearSelection2", true);
            var line = HolePosition.SelectEdge(api, scan, scan.LineEdges[axis.FirstLine], false)
                       && HolePosition.SelectEdge(api, scan, scan.LineEdges[axis.SecondLine], true)
                ? api.Call(scan.Document, "IDrawingDoc", "InsertCenterLine2")
                : null;
            api.Call(scan.Document, "IModelDoc2", "ClearSelection2", true);
            if (line is null)
                failed++;
            else
                added++;
        }

        return (added, failed);
    }

    /// <summary>视图里已有中心线注解的线段（图纸坐标）。「圆心位置」（1.14.2）判对齐的圆心连没连上也用它。</summary>
    internal static List<SheetSegment> CenterLines(SolidWorksApi api, object view)
    {
        var lines = new List<SheetSegment>();
        foreach (var annotation in api.CallArray(view, "IView", "GetAnnotations"))
        {
            if (annotation is not null && api.CallInt(annotation, "IAnnotation", "GetType") == CenterLineAnnotation)
                lines.AddRange(DisplayLines(api, annotation));
        }

        return lines;
    }

    /// <summary>一个注解显示出来的线段（图纸坐标）；中心线注解的位置读回为空，线在显示数据里。</summary>
    internal static List<SheetSegment> DisplayLines(SolidWorksApi api, object annotation)
    {
        var lines = new List<SheetSegment>();
        if (api.Call(annotation, "IAnnotation", "GetDisplayData") is not { } data)
            return lines;
        var count = api.CallInt(data, "IDisplayData", "GetLineCount");
        for (var i = 0; i < count; i++)
        {
            // [颜色, 线型, 线样式, 线宽, 起点 xyz, 终点 xyz]
            var line = api.CallDoubles(data, "IDisplayData", "GetLineAtIndex3", i);
            if (line.Length >= 10)
                lines.Add(new SheetSegment(line[4], line[5], line[7], line[8]));
        }

        return lines;
    }
}
