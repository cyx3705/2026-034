using System.Runtime.InteropServices;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「孔位尺寸」：点一个工程图视图，视图里全部的孔以左侧、上侧为基准标位置尺寸。
/// </summary>
/// <remarks>
/// <para>认孔、分种与「孔标注」相同（<see cref="HoleScan"/>）；怎么标见 <see cref="HolePositionPlanner"/>：
/// 同种孔接着前一个孔标，不同种孔从基准标；同种孔一个方向超过 4 个且等距用阵列标法「(N-1) x 间距 =总长」。
/// 腰型孔只标上方那一端圆弧的圆心（1.3.0，用户定）。</para>
/// <para>「重新标」：先删掉连着这些孔的旧线性尺寸与视图里悬空的线性尺寸（孔标注、直径尺寸、外形尺寸不动），再全部重标。</para>
/// <para>最后做标注避障（1.6.0，<see cref="Clearance.ClearDimensions"/>）：尺寸数字压在别的孔相关注解上就沿尺寸线滑开。</para>
/// </remarks>
internal static class HolePosition
{
    public static QuickCommand Command { get; } = new(
        Key: "hole-position",
        CommandName: StrenuaIdentity.Domain + ".hole.position",
        Title: "孔位尺寸",
        Summary: "点一个工程图视图，删掉孔的旧位置尺寸后以零件左侧、上侧直边为基准重标全部孔位尺寸。",
        Usage: "在工程图里点一个视图（先点后按、先按后点都行，60 秒内），该视图里全部的孔删掉旧位置尺寸（含悬空的线性尺寸）后，以零件左侧、上侧直边为基准重标：同种孔接着前一个标，不同种从基准标；同种一个方向超过 4 个且等距时标「(N-1) x 间距 =总长」；腰型孔标在上方那个圆上；数字压在别的孔的尺寸、中心符号线等线条上就沿尺寸线滑开。",
        Run: context => Run(context, null));

    // swDimensionTextParts_e / swSelectType_e
    private const int TextPrefix = 1;
    private const int SelectCenterMark = 100;

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="view">直接处理这个视图（全流程用）；null 时取选中的或等用户点选。</param>
    internal static QuickOutcome Run(QuickCommandContext context, object? view)
    {
        var api = context.Api;
        var scan = HoleScan.Scan(context, "孔位尺寸", withLines: true, view: view);
        var (document, viewName) = (scan.Document, scan.ViewName);

        var holes = HoleCalloutPlanner.Recognize(scan.Candidates);
        if (holes.Count == 0)
            return QuickOutcome.Ok($"视图「{viewName}」里没有正对图纸的孔，没有加尺寸。");

        var (leftIndex, topIndex) = HolePositionPlanner.Datums(scan.Lines);
        if (leftIndex is not { } l || topIndex is not { } t)
        {
            return QuickOutcome.Fail($"视图「{viewName}」里找不到"
                + (leftIndex is null ? "竖直的直边做左侧基准" : "水平的直边做上侧基准")
                + "，孔位尺寸没有标。");
        }

        var plan = HolePositionPlanner.Plan(scan.Candidates, scan.Lines[l].X1, scan.Lines[t].Y1, scan.Geometry.Scale);
        context.Report($"孔位尺寸：视图「{viewName}」认出 {plan.Summary}，"
            + $"删掉孔上的旧位置尺寸后标 {plan.Dimensions.Count} 个（阵列 {plan.PatternCount} 个）。");
        _ = api.Call(document, "IDrawingDoc", "ActivateView", viewName);

        int removed;
        var added = 0;
        var onCenterLines = 0;
        var failed = 0;
        ClearanceResult clearance;
        try
        {
            context.SetState("删旧尺寸");
            (removed, var leftover) = AnnotationEraser.Erase(context, document, () => ReadObsolete(api, scan, holes));
            if (leftover > 0)
            {
                return QuickOutcome.Fail($"视图「{viewName}」：有 {leftover} 个旧位置尺寸删不掉，没有重标（已删 {removed} 个）。"
                    + "请在 SolidWorks 里手工删掉后再按。");
            }

            context.SetState("加尺寸");
            foreach (var dimension in plan.Dimensions)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                var datumEdge = scan.LineEdges[dimension.Axis == PositionAxis.Horizontal ? l : t];
                var (ok, centerLines) = Insert(api, scan, datumEdge, dimension);
                if (!ok)
                {
                    failed++;
                    continue;
                }

                added++;
                if (centerLines)
                    onCenterLines++;
            }

            context.SetState("避障");
            clearance = Clearance.ClearDimensions(context, scan, holes);
        }
        finally
        {
            api.Call(document, "IModelDoc2", "ClearSelection2", true);
            api.Call(document, "IModelDoc2", "GraphicsRedraw2");
        }

        var message = $"视图「{viewName}」：{plan.Summary}，删掉旧位置尺寸 {removed} 个，"
            + $"新加 {added} 个（阵列标法 {plan.PatternCount} 个；连在中心线上 {onCenterLines} 个，其余孔上没有中心符号线、连在孔边上）"
            + (failed > 0 ? $"，{failed} 个 SolidWorks 没有接受" : string.Empty)
            + clearance.Describe("尺寸数字")
            + "。";
        return added == 0 && plan.Dimensions.Count > 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }

    /// <summary>
    /// 选起点（基准边或前一个孔）与终点孔，按方向加水平 / 竖直尺寸；阵列标法再写前缀。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 水平尺寸的孔那一头优先选孔上的中心符号线：SolidWorks 把尺寸连到符号线的竖线上，和手工「按中心线标」一样；
    /// 有一头选不到中心符号线（孔上没有），整条尺寸退回选孔边（量到圆心，值相同）。
    /// </para>
    /// <para>
    /// 竖直尺寸只选孔边。真机（SW 2025 SP5）实测：选中中心符号线加竖直尺寸，SolidWorks 不论点选偏移、选择先后，
    /// 一律拿符号的竖线，与水平基准边一起建成角度尺寸。API 也拿不到中心符号线的横线单独去选。
    /// </para>
    /// <para>建出来的不是线性尺寸就删掉，退回选孔边再建。</para>
    /// </remarks>
    /// <returns>加上了没有，以及是不是连在中心线上。</returns>
    private static (bool Added, bool OnCenterLines) Insert(SolidWorksApi api, ScannedView scan, object datumEdge, PositionDimension dimension)
    {
        var horizontal = dimension.Axis == PositionAxis.Horizontal;
        var method = horizontal ? "AddHorizontalDimension2" : "AddVerticalDimension2";
        foreach (var onCenterLines in horizontal ? new[] { true, false } : [false])
        {
            api.Call(scan.Document, "IModelDoc2", "ClearSelection2", true);
            var selected = (dimension.FromEdgeIndex is { } from ? SelectHole(api, scan, from, false, onCenterLines) : SelectEdge(api, scan, datumEdge, false))
                && SelectHole(api, scan, dimension.ToEdgeIndex, true, onCenterLines);
            if (!selected)
                continue;
            if (api.Call(scan.Document, "IModelDoc2", method, dimension.TextAt.X, dimension.TextAt.Y, 0.0) is not { } created)
                continue;
            if (!HolePositionPlanner.IsLinear(api.CallInt(created, "IDisplayDimension", "get_Type2")))
            {
                Discard(api, scan, created);
                continue;
            }

            if (dimension.Prefix.Length > 0)
                api.Call(created, "IDisplayDimension", "SetText", TextPrefix, dimension.Prefix);
            return (true, onCenterLines);
        }

        return (false, false);
    }

    /// <summary>删掉刚建错的尺寸（例如 SolidWorks 建成了角度尺寸）。</summary>
    private static void Discard(SolidWorksApi api, ScannedView scan, object displayDimension)
    {
        api.Call(scan.Document, "IModelDoc2", "ClearSelection2", true);
        if (api.Call(displayDimension, "IDisplayDimension", "GetAnnotation") is { } annotation
            && api.CallBool(annotation, "IAnnotation", "Select3", false, null))
            api.Call(scan.Document, "IModelDoc2", "EditDelete");
    }

    private static bool SelectEdge(SolidWorksApi api, ScannedView scan, object edge, bool append)
        => api.CallBool(scan.View, "IView", "SelectEntity", edge, append);

    /// <summary>
    /// 选孔：<paramref name="centerMark"/> 时在孔心点选中心符号线，并核对选中的确实是中心符号线
    /// （点空了 SolidWorks 会改选整个视图）；否则选孔边。
    /// </summary>
    private static bool SelectHole(SolidWorksApi api, ScannedView scan, int edgeIndex, bool append, bool centerMark)
    {
        if (!centerMark)
            return SelectEdge(api, scan, scan.Edges[edgeIndex], append);

        var hole = scan.Candidates[edgeIndex];
        var extension = api.Call(scan.Document, "IModelDoc2", "get_Extension");
        var selection = api.Call(scan.Document, "IModelDoc2", "get_SelectionManager");
        if (extension is null || selection is null)
            return false;
        var before = api.CallInt(selection, "ISelectionMgr", "GetSelectedObjectCount2", -1);
        if (!api.CallBool(extension, "IModelDocExtension", "SelectByID2", string.Empty, "CENTERMARKSYMS", hole.X, hole.Y, 0.0, append, 0, null, 0))
            return false;
        var count = api.CallInt(selection, "ISelectionMgr", "GetSelectedObjectCount2", -1);
        return count == before + 1
            && api.CallInt(selection, "ISelectionMgr", "GetSelectedObjectType3", count, -1) == SelectCenterMark;
    }

    /// <summary>
    /// 视图里连着这些孔的旧线性尺寸（<c>IAnnotation</c>）：连着孔边的，或连着穿过孔心的中心线的。孔标注不算。
    /// </summary>
    private static IReadOnlyList<object> ReadObsolete(SolidWorksApi api, ScannedView scan, IReadOnlyList<HoleEdge> holes)
    {
        var annotations = new List<object>();
        var existing = new List<ExistingDimension>();
        var dimension = api.Call(scan.View, "IView", "GetFirstDisplayDimension5");
        while (dimension is not null)
        {
            if (!api.CallBool(dimension, "IDisplayDimension", "IsHoleCallout")
                && api.Call(dimension, "IDisplayDimension", "GetAnnotation") is { } annotation)
            {
                var type = api.CallInt(dimension, "IDisplayDimension", "get_Type2");
                var centers = HoleCallout.AttachedCircleCenters(api, scan.Geometry, annotation).ToList();
                existing.Add(new ExistingDimension(annotations.Count, type, centers, AttachedLines(api, scan, annotation), IsDangling(api, annotation)));
                annotations.Add(annotation);
            }

            dimension = api.Call(dimension, "IDisplayDimension", "GetNext5");
        }

        return HolePositionPlanner.Obsolete(holes, existing).Select(index => annotations[index]).ToList();
    }

    /// <summary>
    /// 尺寸悬空了没有：<c>IAnnotation.IsDangling</c>；保险起见附着对象里有空的也算
    /// （真机上「中心符号线」删掉旧符号线后，挂在上面的尺寸附着类型读回 0、对象为空）。
    /// </summary>
    private static bool IsDangling(SolidWorksApi api, object annotation)
        => api.CallBool(annotation, "IAnnotation", "IsDangling")
            || api.CallArray(annotation, "IAnnotation", "GetAttachedEntities3").Any(entity => entity is null)
            || api.CallArray(annotation, "IAnnotation", "GetAttachedEntityTypes").Any(type => Convert.ToInt32(type) == 0);

    /// <summary>
    /// 注解连着的视图草图线（中心符号线、中心线）在图纸上的样子。草图坐标是模型尺寸、原点在视图位置，
    /// 图纸点 = 视图位置 + 草图点 × 比例；<c>GetXform</c> 不含旋转，所以旋转过的视图不认（返回空）。
    /// </summary>
    internal static List<SheetSegment> AttachedLines(SolidWorksApi api, ScannedView scan, object annotation)
    {
        var lines = new List<SheetSegment>();
        var angle = api.Call(scan.View, "IView", "get_Angle") is { } value ? Convert.ToDouble(value) : 0.0;
        var xform = api.CallDoubles(scan.View, "IView", "GetXform");
        if (Math.Abs(angle) > 1e-9 || xform.Length < 3)
            return lines;

        // 不按附着类型码筛：真机（1.6.0 过渡板）上有尺寸的附着类型报 1（边），对象却是视图草图线段
        // （穿过孔心的中心线），按 24 筛就认不出它连着孔，重标时删不掉、新旧叠成两个。所以每个附着对象都试着当草图直线读。
        foreach (var entity in api.CallArray(annotation, "IAnnotation", "GetAttachedEntities3"))
        {
            // 悬空（连着的中心符号线被删了）时对象读回 null。
            if (entity is null)
                continue;
            try
            {
                if (api.Call(entity, "ISketchLine", "GetStartPoint2") is not { } start
                    || api.Call(entity, "ISketchLine", "GetEndPoint2") is not { } end)
                    continue;
                double X(object point) => xform[0] + Convert.ToDouble(api.Call(point, "ISketchPoint", "get_X")) * xform[2];
                double Y(object point) => xform[1] + Convert.ToDouble(api.Call(point, "ISketchPoint", "get_Y")) * xform[2];
                lines.Add(new SheetSegment(X(start), Y(start), X(end), Y(end)));
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or System.Reflection.TargetException)
            {
                // 是模型边、不是直线（圆弧形的中心线之类），或对象已失效：不认。
            }
        }

        return lines;
    }
}
