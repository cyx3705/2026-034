using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「孔标注」：点一个工程图视图，视图里看得见的孔（含腰型孔）每种加一个孔标注。
/// </summary>
/// <remarks>
/// 选视图与认孔见 <see cref="HoleScan"/>。每种孔（同一特征、同一孔径）只标一个：SolidWorks 的孔标注
/// 自己会写上「N×」，每个都标就是同一行字重复 N 遍。某种孔里已经有一个带标注，整种跳过，连点两次不会出双份。
/// 腰型孔选它一端的圆弧加孔标注，与圆孔同一个 <c>AddHoleCallout2</c>，规格由 SolidWorks 一次写全。
/// 最后做标注避障（1.6.0，<see cref="Clearance.ClearCallouts"/>）：视图里孔标注的文字压在别的孔相关注解上就换角位，
/// 已有的孔标注也算。
/// </remarks>
internal static class HoleCallout
{
    public static QuickCommand Command { get; } = new(
        Key: "hole-callout",
        CommandName: StrenuaIdentity.Domain + ".hole.callout",
        Title: "孔标注",
        Summary: "点一个工程图视图，视图里每种孔（含腰型孔）各标一次孔标注，已有标注的种跳过。",
        Usage: "在工程图里点一个视图（先点后按、先按后点都行，60 秒内），该视图里每种孔（含腰型孔）标一次（数量由 SolidWorks 的 N× 带出）；已有标注的种跳过。最后避障：孔标注文字压在别的孔的尺寸、中心符号线等线条上就换到孔的另一个角。",
        Run: context => Run(context, null));

    // swSelectType_e
    private const int SelectEdge = 1;

    /// <summary>首次落位时文字中心放在折点左边多远（图纸上 20 mm），只是让文字先落在左边。</summary>
    private const double InitialTextOffset = 0.02;

    /// <summary>对准折点最多挪几次：挪动后 SolidWorks 可能换引线接的那一侧，要回读再挪。</summary>
    private const int AlignPasses = 3;

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="view">直接处理这个视图（全流程用）；null 时取选中的或等用户点选。</param>
    internal static QuickOutcome Run(QuickCommandContext context, object? view)
    {
        var api = context.Api;
        var scan = HoleScan.Scan(context, "孔标注", view: view);
        var (document, viewName) = (scan.Document, scan.ViewName);

        var plan = HoleCalloutPlanner.Plan(scan.Candidates, ExistingCalloutCenters(api, scan.Geometry, scan.View));
        if (plan.HoleCount == 0)
            return QuickOutcome.Ok($"视图「{viewName}」里没有正对图纸的孔，没有加标注。");

        context.SetState("加标注");
        context.Report(plan.Targets.Count == 0
            ? $"孔标注：视图「{viewName}」认出 {plan.Summary}，都已有孔标注，只做避障。"
            : $"孔标注：视图「{viewName}」认出 {plan.Summary}，开始为 {plan.Targets.Count} 种各加一个标注。");
        var added = 0;
        var failed = 0;
        ClearanceResult clearance;
        _ = api.Call(document, "IDrawingDoc", "ActivateView", viewName);
        try
        {
            foreach (var target in plan.Targets)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                api.Call(document, "IModelDoc2", "ClearSelection2", true);
                // 先按「文字中心在折点左边一段」落下，再按实际下划线把折点对准——文字多宽要生成后才知道。
                var callout = api.CallBool(scan.View, "IView", "SelectEntity", scan.Edges[target.EdgeIndex], false)
                    ? api.Call(document, "IDrawingDoc", "AddHoleCallout2", target.Placement.X - InitialTextOffset, target.Placement.Y, 0.0)
                    : null;
                if (callout is null)
                {
                    failed++;
                    continue;
                }

                added++;
                if (api.Call(callout, "IDisplayDimension", "GetAnnotation") is { } annotation)
                    AlignShoulder(api, annotation, target.Placement, textLeft: true);
            }

            context.SetState("避障");
            clearance = Clearance.ClearCallouts(context, scan, HoleCalloutPlanner.Recognize(scan.Candidates));
        }
        finally
        {
            api.Call(document, "IModelDoc2", "ClearSelection2", true);
            api.Call(document, "IModelDoc2", "GraphicsRedraw2");
        }

        var message = $"视图「{viewName}」：{plan.Summary}，新加 {added} 个孔标注"
            + (plan.AlreadyAnnotated > 0 ? $"，{plan.AlreadyAnnotated} 种已有标注跳过" : string.Empty)
            + (failed > 0 ? $"，{failed} 个 SolidWorks 没有接受" : string.Empty)
            + clearance.Describe("孔标注")
            + "。";
        return added == 0 && plan.Targets.Count > 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }

    /// <summary>
    /// 平移整个孔标注，让引线折点（文字在左时是下划线右端，在右时是左端）落在 <paramref name="target"/>。
    /// 读不到下划线就保持原位——标注已经加上，只是位置不理想。
    /// </summary>
    internal static void AlignShoulder(SolidWorksApi api, object annotation, SheetPoint target, bool textLeft)
    {
        for (var pass = 0; pass < AlignPasses; pass++)
        {
            if (Shoulder(api, annotation, textLeft) is not { } shoulder)
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

    /// <summary>从显示数据里读出孔标注的下划线端点（<paramref name="textLeft"/> 取右端，否则左端）。</summary>
    internal static SheetPoint? Shoulder(SolidWorksApi api, object annotation, bool textLeft)
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

        return HoleCalloutPlanner.ShoulderEnd(lines, textLeft);
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

    /// <summary>一个注解所附着的圆边的圆心（图纸坐标）。悬空注解的附着对象读回 null，跳过。</summary>
    internal static IEnumerable<SheetPoint> AttachedCircleCenters(SolidWorksApi api, HoleScan.ViewGeometry geometry, object annotation)
    {
        var types = api.CallArray(annotation, "IAnnotation", "GetAttachedEntityTypes")
            .Select(Convert.ToInt32)
            .ToArray();
        var entities = api.CallArray(annotation, "IAnnotation", "GetAttachedEntities3");
        for (var i = 0; i < entities.Length && i < types.Length; i++)
        {
            if (types[i] == SelectEdge && entities[i] is { } entity && geometry.TryReadCircleCenter(entity) is { } center)
                yield return center;
        }
    }
}
