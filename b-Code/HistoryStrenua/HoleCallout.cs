using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「孔标注」：点一个工程图视图，视图里看得见的孔（含腰型孔）每种加一个孔标注。
/// </summary>
/// <remarks>
/// 选视图与认孔见 <see cref="HoleScan"/>。每种孔（同一特征、同一孔径）只标一个：SolidWorks 的孔标注
/// 自己会写上「N×」，每个都标就是同一行字重复 N 遍。某种孔里已经有一个带标注，整种跳过，连点两次不会出双份。
/// 腰型孔选它一端的圆弧加孔标注，与圆孔同一个 <c>AddHoleCallout2</c>，规格由 SolidWorks 一次写全。
/// </remarks>
internal static class HoleCallout
{
    public static QuickCommand Command { get; } = new(
        Key: "hole-callout",
        CommandName: StrenuaIdentity.Domain + ".hole.callout",
        Title: "孔标注",
        Summary: "点一个工程图视图，视图里每种孔（含腰型孔）各标一次孔标注，已有标注的种跳过。",
        Usage: "在工程图里点一个视图（先点后按、先按后点都行，60 秒内），该视图里每种孔（含腰型孔）标一次（数量由 SolidWorks 的 N× 带出）；已有标注的种跳过。",
        Run: Run);

    // swSelectType_e
    private const int SelectEdge = 1;

    /// <summary>首次落位时文字中心放在折点左边多远（图纸上 20 mm），只是让文字先落在左边。</summary>
    private const double InitialTextOffset = 0.02;

    /// <summary>对准折点最多挪几次：挪动后 SolidWorks 可能换引线接的那一侧，要回读再挪。</summary>
    private const int AlignPasses = 3;

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var api = context.Api;
        var scan = HoleScan.Scan(context, "孔标注");
        var (document, view, viewName) = (scan.Document, scan.View, scan.ViewName);

        var plan = HoleCalloutPlanner.Plan(scan.Candidates, ExistingCalloutCenters(api, scan.Geometry, view));
        if (plan.HoleCount == 0)
            return QuickOutcome.Ok($"视图「{viewName}」里没有正对图纸的孔，没有加标注。");
        if (plan.Targets.Count == 0)
            return QuickOutcome.Ok($"视图「{viewName}」里 {plan.Summary}，都已有孔标注，没有新加。");

        context.SetState("加标注");
        context.Report($"孔标注：视图「{viewName}」认出 {plan.Summary}，开始为 {plan.Targets.Count} 种各加一个标注。");
        var added = 0;
        var failed = 0;
        _ = api.Call(document, "IDrawingDoc", "ActivateView", viewName);
        try
        {
            foreach (var target in plan.Targets)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                api.Call(document, "IModelDoc2", "ClearSelection2", true);
                // 先按「文字中心在折点左边一段」落下，再按实际下划线把折点对准——文字多宽要生成后才知道。
                var callout = api.CallBool(view, "IView", "SelectEntity", scan.Edges[target.EdgeIndex], false)
                    ? api.Call(document, "IDrawingDoc", "AddHoleCallout2", target.Placement.X - InitialTextOffset, target.Placement.Y, 0.0)
                    : null;
                if (callout is null)
                {
                    failed++;
                    continue;
                }

                added++;
                if (api.Call(callout, "IDisplayDimension", "GetAnnotation") is { } annotation)
                    AlignShoulder(api, annotation, target.Placement);
            }
        }
        finally
        {
            api.Call(document, "IModelDoc2", "ClearSelection2", true);
            api.Call(document, "IModelDoc2", "GraphicsRedraw2");
        }

        var message = $"视图「{viewName}」：{plan.Summary}，新加 {added} 个孔标注"
            + (plan.AlreadyAnnotated > 0 ? $"，{plan.AlreadyAnnotated} 种已有标注跳过" : string.Empty)
            + (failed > 0 ? $"，{failed} 个 SolidWorks 没有接受" : string.Empty)
            + "。";
        return added == 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }

    /// <summary>
    /// 平移整个孔标注，让文字下划线的右端（引线折点）落在 <paramref name="target"/>。
    /// 读不到下划线就保持原位——标注已经加上，只是位置不理想。
    /// </summary>
    private static void AlignShoulder(SolidWorksApi api, object annotation, SheetPoint target)
    {
        for (var pass = 0; pass < AlignPasses; pass++)
        {
            if (Shoulder(api, annotation) is not { } shoulder)
                return;
            var dx = target.X - shoulder.X;
            var dy = target.Y - shoulder.Y;
            if (Math.Abs(dx) < HoleCalloutPlanner.AlignTolerance && Math.Abs(dy) < HoleCalloutPlanner.AlignTolerance)
                return;
            var position = api.CallDoubles(annotation, "IAnnotation", "GetPosition");
            if (position.Length < 3
                || !api.CallBool(annotation, "IAnnotation", "SetPosition2", position[0] + dx, position[1] + dy, position[2]))
                return;
        }
    }

    /// <summary>从显示数据里读出孔标注的下划线右端。</summary>
    private static SheetPoint? Shoulder(SolidWorksApi api, object annotation)
    {
        if (api.Call(annotation, "IAnnotation", "GetDisplayData") is not { } data)
            return null;
        var count = api.CallInt(data, "IDisplayData", "GetLineCount");
        var lines = new List<SheetSegment>(count);
        for (var i = 0; i < count; i++)
        {
            // [颜色, 线型, 线样式, 线宽, 起点 xyz, 终点 xyz]
            var line = api.CallDoubles(data, "IDisplayData", "GetLineAtIndex3", i);
            if (line.Length >= 10)
                lines.Add(new SheetSegment(line[4], line[5], line[7], line[8]));
        }

        return HoleCalloutPlanner.ShoulderEnd(lines);
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
