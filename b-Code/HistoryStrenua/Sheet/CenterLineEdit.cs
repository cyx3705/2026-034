using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>中心线（对称轴）的共用动作。</summary>
internal static class CenterLineEdit
{
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

    /// <summary>
    /// 视图里还没有落在这根轴上的中心线，就选左右（上下）一对对称的直边，<c>IDrawingDoc.InsertCenterLine2</c>。
    /// 真机（SW 2025 SP5，XJ05A-01 安装板）：生成在两边正中、两头各伸出 5 mm，与用户手工插的那根完全一致；中心线注解类型 15，
    /// 位置读回为空，线段在显示数据里（线型 6）。
    /// </summary>
    /// <returns>加上的根数与没插上或读回不贯穿视图的根数（已有的两样都不算）。</returns>
    internal static (int Added, int Failed) Insert(SolidWorksApi api, ScannedView scan, IReadOnlyList<SymmetryAxis> axes)
    {
        var drawn = CenterLines(api, scan.View);
        var (added, failed) = (0, 0);
        foreach (var axis in axes)
        {
            if (SymmetryPlanner.Drawn(axis, drawn))
                continue;
            api.ClearSelection(scan.Document);
            var line = DimensionEdit.SelectEdge(api, scan, scan.LineEdges[axis.FirstLine], false)
                       && DimensionEdit.SelectEdge(api, scan, scan.LineEdges[axis.SecondLine], true)
                ? api.Call(scan.Document, "IDrawingDoc", "InsertCenterLine2")
                : null;
            api.ClearSelection(scan.Document);
            // InsertCenterLine2 回的是 ICenterLine，不是注解；当 IAnnotation 调会 TargetException（1.23.1）。
            var annotation = line is null ? null : api.Call(line, "ICenterLine", "GetAnnotation");
            if (annotation is null)
                failed++;
            else if (CoversView(axis, DisplayLines(api, annotation), scan.Lines.Concat(scan.CurveSegments).ToList()))
                added++;
            else
            {
                // SolidWorks 偶尔按选中的短边生成短中心线。插入后按显示数据核对完整视图跨度，错误结果立即删掉。
                api.ClearSelection(scan.Document);
                if (api.CallBool(annotation, "IAnnotation", "Select3", false, null))
                    api.Call(scan.Document, "IModelDoc2", "EditDelete");
                failed++;
            }
        }

        return (added, failed);
    }

    internal static bool CoversView(SymmetryAxis axis, IReadOnlyList<SheetSegment> centerLines, IReadOnlyList<SheetSegment> geometry)
    {
        if (centerLines.Count == 0 || geometry.Count == 0)
            return false;
        var vertical = axis.Axis == PositionAxis.Horizontal;
        var coordinates = geometry.SelectMany(line => vertical ? new[] { line.Y1, line.Y2 } : new[] { line.X1, line.X2 }).ToList();
        var min = coordinates.Min();
        var max = coordinates.Max();
        var axisLines = centerLines.Where(line => vertical
            ? Math.Abs(line.X1 - axis.At) <= SymmetryPlanner.Tolerance && Math.Abs(line.X2 - axis.At) <= SymmetryPlanner.Tolerance
            : Math.Abs(line.Y1 - axis.At) <= SymmetryPlanner.Tolerance && Math.Abs(line.Y2 - axis.At) <= SymmetryPlanner.Tolerance).ToList();
        if (axisLines.Count == 0)
            return false;
        var starts = axisLines.Select(line => Math.Min(vertical ? line.Y1 : line.X1, vertical ? line.Y2 : line.X2));
        var ends = axisLines.Select(line => Math.Max(vertical ? line.Y1 : line.X1, vertical ? line.Y2 : line.X2));
        return starts.Min() <= min + SymmetryPlanner.Tolerance && ends.Max() >= max - SymmetryPlanner.Tolerance;
    }

    internal const int CenterLineAnnotation = 15;
}
