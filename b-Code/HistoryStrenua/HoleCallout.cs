using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「孔标注」：点一个工程图视图，视图里看得见的孔每种加一个孔标注。
/// </summary>
/// <remarks>
/// 选视图与认孔见 <see cref="HoleScan"/>。每种孔（同一特征、同一孔径）只标一个：SolidWorks 的孔标注
/// 自己会写上「N×」，每个都标就是同一行字重复 N 遍。某种孔里已经有一个带标注，整种跳过，连点两次不会出双份。
/// </remarks>
internal static class HoleCallout
{
    public static QuickCommand Command { get; } = new(
        Key: "hole-callout",
        CommandName: StrenuaIdentity.Domain + ".hole.callout",
        Title: "孔标注",
        Usage: "在工程图里点一个视图（先点后按、先按后点都行，60 秒内），该视图里每种孔标一次（数量由 SolidWorks 的 N× 带出）；已有标注的种跳过。",
        Run: Run);

    // swSelectType_e
    private const int SelectEdge = 1;

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var api = context.Api;
        var scan = HoleScan.Scan(context, "孔标注");
        var (document, view, viewName) = (scan.Document, scan.View, scan.ViewName);

        var plan = HoleCalloutPlanner.Plan(scan.Candidates, ExistingCalloutCenters(api, scan.Geometry, view));
        if (plan.HoleCount == 0)
            return QuickOutcome.Ok($"视图「{viewName}」里没有正对图纸的孔，没有加标注。");
        if (plan.Targets.Count == 0)
            return QuickOutcome.Ok($"视图「{viewName}」里 {plan.HoleCount} 个孔共 {plan.KindCount} 种，都已有孔标注，没有新加。");

        context.SetState("加标注");
        context.Report($"孔标注：视图「{viewName}」认出 {plan.HoleCount} 个孔共 {plan.KindCount} 种，开始为 {plan.Targets.Count} 种各加一个标注。");
        var added = 0;
        var failed = 0;
        _ = api.Call(document, "IDrawingDoc", "ActivateView", viewName);
        try
        {
            foreach (var target in plan.Targets)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                api.Call(document, "IModelDoc2", "ClearSelection2", true);
                var created = api.CallBool(view, "IView", "SelectEntity", scan.Edges[target.EdgeIndex], false)
                    && api.Call(document, "IDrawingDoc", "AddHoleCallout2", target.Placement.X, target.Placement.Y, 0.0) is not null;
                if (created)
                    added++;
                else
                    failed++;
            }
        }
        finally
        {
            api.Call(document, "IModelDoc2", "ClearSelection2", true);
            api.Call(document, "IModelDoc2", "GraphicsRedraw2");
        }

        var message = $"视图「{viewName}」：{plan.HoleCount} 个孔共 {plan.KindCount} 种，新加 {added} 个孔标注"
            + (plan.AlreadyAnnotated > 0 ? $"，{plan.AlreadyAnnotated} 种已有标注跳过" : string.Empty)
            + (failed > 0 ? $"，{failed} 个 SolidWorks 没有接受" : string.Empty)
            + "。";
        return added == 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }

    /// <summary>视图里已有孔标注各自指着的孔心。</summary>
    private static List<SheetPoint> ExistingCalloutCenters(SolidWorksApi api, HoleScan.ViewGeometry geometry, object view)
    {
        var centers = new List<SheetPoint>();
        var dimension = api.Call(view, "IView", "GetFirstDisplayDimension5");
        while (dimension is not null)
        {
            if (api.CallBool(dimension, "IDisplayDimension", "IsHoleCallout")
                && api.Call(dimension, "IDisplayDimension", "GetAnnotation") is { } annotation)
                centers.AddRange(AttachedCircleCenters(api, geometry, annotation));

            dimension = api.Call(dimension, "IDisplayDimension", "GetNext5");
        }

        return centers;
    }

    /// <summary>一个注解所附着的圆边的圆心（图纸坐标）。</summary>
    internal static IEnumerable<SheetPoint> AttachedCircleCenters(SolidWorksApi api, HoleScan.ViewGeometry geometry, object annotation)
    {
        var types = api.CallArray(annotation, "IAnnotation", "GetAttachedEntityTypes")
            .Select(Convert.ToInt32)
            .ToArray();
        var entities = api.CallArray(annotation, "IAnnotation", "GetAttachedEntities3");
        for (var i = 0; i < entities.Length && i < types.Length; i++)
        {
            if (types[i] == SelectEdge && geometry.TryReadCircleCenter(entities[i]) is { } center)
                yield return center;
        }
    }
}
