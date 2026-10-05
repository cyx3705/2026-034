using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「圆角标注」（1.9.0，出图类）：点一个工程图视图，视图里正对图纸的圆角弧每个加一个 R 尺寸（R1 按技术要求不标，
/// 同半径 3 段以上合标「N x R」），已有 R / 直径尺寸的跳过。
/// </summary>
/// <remarks>
/// 认弧见 <see cref="HoleScan.ViewGeometry.TryReadArc"/>（孔与腰型孔端头先被认成孔，不算圆角），标哪些、文字放哪见
/// <see cref="FilletPlanner"/>。选弧边后 <c>IModelDoc2.AddRadialDimension2</c>（真机：引线指到弧、带下划线，与用户手标的 R5 / R10 一样）。
/// </remarks>
internal static class FilletDimension
{
    public static QuickCommand Command { get; } = new(
        Key: "fillet",
        CommandName: StrenuaIdentity.Domain + ".drawing.fillet",
        Title: "圆角标注",
        Summary: "点一个工程图视图，给视图里的圆角弧各加一个 R 尺寸（R1 按技术要求不标，同半径 3 个以上合标 N x R），已标的跳过。",
        Usage: "在工程图里点一个视图（先点后按、先按后点都行，60 秒内），该视图里正对图纸的圆角（外圆角、内圆角、凹弧）每个加一个 R 尺寸，文字放在零件外的空处、尽量不压线；R1 不标（技术要求「未注圆角R1」），同一半径有 3 个以上时只标一个并写「N x R」；已有 R 或直径尺寸的跳过。孔和腰型孔不算圆角。",
        Run: context => Run(context, null));

    // swDimensionType_e
    private const int RadialDimension = 5;
    private const int DiameterDimension = 6;

    // swDimensionTextParts_e.swDimensionTextPrefix
    private const int TextPrefix = 1;

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="view">直接处理这个视图（「一键出图」用）；null 时取选中的或等用户点选。</param>
    internal static QuickOutcome Run(QuickCommandContext context, object? view)
    {
        var api = context.Api;
        var scan = HoleScan.Scan(context, "圆角标注", withLines: true, view: view, withArcs: true);
        var (document, viewName) = (scan.Document, scan.ViewName);
        if (scan.Arcs.Count == 0)
            return QuickOutcome.Ok($"视图「{viewName}」里没有正对图纸的圆角，没有加 R 尺寸。");

        var plan = FilletPlanner.Plan(scan.Arcs, Dimensioned(api, scan), scan.Lines.Concat(scan.CurveSegments).Concat(DrawingSheet.FrameLines(api, document)).ToList(),
            DrawingSheet.FrameRect(api, document));
        var skipped = (plan.DefaultCount > 0 ? $"，{plan.DefaultCount} 个是 R1（技术要求未注圆角 R1）不标" : string.Empty)
            + (plan.Dimensioned > 0 ? $"，{plan.Dimensioned} 个已有尺寸跳过" : string.Empty);
        if (plan.Targets.Count == 0)
            return QuickOutcome.Ok($"视图「{viewName}」：{plan.ArcCount} 个圆角{skipped}，没有新加。");

        context.SetState("加圆角尺寸");
        context.Report($"圆角标注：视图「{viewName}」认出 {plan.ArcCount} 个圆角，加 {plan.Targets.Count} 个 R 尺寸。");
        var added = 0;
        var failed = 0;
        _ = api.Call(document, "IDrawingDoc", "ActivateView", viewName);
        try
        {
            foreach (var target in plan.Targets)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                api.Call(document, "IModelDoc2", "ClearSelection2", true);
                var dimension = api.CallBool(scan.View, "IView", "SelectEntity", scan.ArcEdges![target.Index], false)
                    ? api.Call(document, "IModelDoc2", "AddRadialDimension2", target.TextAt.X, target.TextAt.Y, 0.0)
                    : null;
                if (dimension is null)
                {
                    failed++;
                    continue;
                }

                added++;
                if (target.Count > 1)
                {
                    // 半径尺寸的前缀本来就是「R」（真机读回），合标时写成「N x R」。
                    var prefix = api.CallString(dimension, "IDisplayDimension", "GetText", TextPrefix);
                    api.Call(dimension, "IDisplayDimension", "SetText", TextPrefix, target.Prefix + (prefix.Length > 0 ? prefix : "R"));
                }
            }
        }
        finally
        {
            api.Call(document, "IModelDoc2", "ClearSelection2", true);
            api.Call(document, "IModelDoc2", "GraphicsRedraw2");
        }

        var message = $"视图「{viewName}」：{plan.ArcCount} 个圆角，新加 {added} 个 R 尺寸{skipped}"
            + (failed > 0 ? $"，{failed} 个 SolidWorks 没有接受" : string.Empty) + "。";
        return added == 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }

    /// <summary>视图里已有的半径 / 直径尺寸连着的弧（图纸上的圆心与半径）。</summary>
    private static List<(SheetPoint Center, double Radius)> Dimensioned(SolidWorksApi api, ScannedView scan)
    {
        var result = new List<(SheetPoint, double)>();
        for (var display = api.Call(scan.View, "IView", "GetFirstDisplayDimension5"); display is not null; display = api.Call(display, "IDisplayDimension", "GetNext5"))
        {
            var type = api.CallInt(display, "IDisplayDimension", "get_Type2");
            if (type is not (RadialDimension or DiameterDimension) || api.CallBool(display, "IDisplayDimension", "IsHoleCallout"))
                continue;
            if (api.Call(display, "IDisplayDimension", "GetAnnotation") is not { } annotation)
                continue;
            foreach (var entity in api.CallArray(annotation, "IAnnotation", "GetAttachedEntities3"))
                if (entity is not null && scan.Geometry.TryReadCircle(entity) is { } circle)
                    result.Add(circle);
        }

        return result;
    }
}
