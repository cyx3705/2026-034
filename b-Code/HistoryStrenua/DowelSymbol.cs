using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「销钉符号」（1.6.0）：点一个工程图视图，视图里异形孔向导做的销钉孔，没有销钉符号的各加一个。
/// </summary>
/// <remarks>
/// 选视图与认孔见 <see cref="HoleScan"/>，哪些孔要加见 <see cref="DowelPlanner"/>。
/// 选孔边后 <c>IDrawingDoc.InsertDowelSymbol</c>，一孔一个。已有的销钉符号附着在孔边上，按它附着的圆心认；
/// 附着丢了再用它自己的圆弧点（图纸坐标）。
/// </remarks>
internal static class DowelSymbol
{
    public static QuickCommand Command { get; } = new(
        Key: "dowel-symbol",
        CommandName: StrenuaIdentity.Domain + ".hole.dowel",
        Title: "销钉符号",
        Summary: "点一个工程图视图，给视图里异形孔向导做的销钉孔补上销钉符号，已有的不重复加。",
        Usage: "在工程图里点一个视图（先点后按、先按后点都行，60 秒内），该视图里异形孔向导做的销钉孔（孔类型「销钉孔」）没有销钉符号的各加一个；已有的跳过，手工切出来的孔不认。",
        Run: context => Run(context, null));

    // swAnnotationType_e
    private const int AnnotationDowel = 10;

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="view">直接处理这个视图（全流程用）；null 时取选中的或等用户点选。</param>
    internal static QuickOutcome Run(QuickCommandContext context, object? view)
    {
        var api = context.Api;
        var scan = HoleScan.Scan(context, "销钉符号", view: view);
        var (document, viewName) = (scan.Document, scan.ViewName);

        var plan = DowelPlanner.Plan(scan.Candidates, MarkedCenters(api, scan));
        if (plan.DowelCount == 0)
            return QuickOutcome.Ok($"视图「{viewName}」里没有异形孔向导的销钉孔，没有加销钉符号。");
        if (plan.EdgeIndices.Count == 0)
            return QuickOutcome.Ok($"视图「{viewName}」里 {plan.DowelCount} 个销钉孔都已有销钉符号，没有新加。");

        context.SetState("加销钉符号");
        context.Report($"销钉符号：视图「{viewName}」认出 {plan.DowelCount} 个销钉孔，开始为其中 {plan.EdgeIndices.Count} 个加销钉符号。");
        var added = 0;
        var failed = 0;
        _ = api.Call(document, "IDrawingDoc", "ActivateView", viewName);
        try
        {
            foreach (var index in plan.EdgeIndices)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                api.Call(document, "IModelDoc2", "ClearSelection2", true);
                var symbol = api.CallBool(scan.View, "IView", "SelectEntity", scan.Edges[index], false)
                    ? api.Call(document, "IDrawingDoc", "InsertDowelSymbol")
                    : null;
                if (symbol is null)
                    failed++;
                else
                    added++;
            }
        }
        finally
        {
            api.Call(document, "IModelDoc2", "ClearSelection2", true);
            api.Call(document, "IModelDoc2", "GraphicsRedraw2");
        }

        var message = $"视图「{viewName}」：{plan.DowelCount} 个销钉孔，新加 {added} 个销钉符号"
            + (plan.AlreadyMarked > 0 ? $"，{plan.AlreadyMarked} 个已有跳过" : string.Empty)
            + (failed > 0 ? $"，{failed} 个 SolidWorks 没有接受" : string.Empty)
            + "。";
        return added == 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }

    /// <summary>视图里已有销钉符号所在的孔心（图纸坐标）。</summary>
    internal static List<SheetPoint> MarkedCenters(SolidWorksApi api, ScannedView scan)
    {
        var centers = new List<SheetPoint>();
        foreach (var annotation in api.CallArray(scan.View, "IView", "GetAnnotations"))
        {
            if (annotation is null || api.CallInt(annotation, "IAnnotation", "GetType") != AnnotationDowel)
                continue;
            var attached = HoleCallout.AttachedCircleCenters(api, scan.Geometry, annotation).ToList();
            if (attached.Count == 0
                && api.Call(annotation, "IAnnotation", "GetSpecificAnnotation") is { } symbol
                && DowelPlanner.ArcCenter(api.CallDoubles(symbol, "IDowelSymbol", "GetArcPoints")) is { } center)
                attached.Add(center);
            centers.AddRange(attached);
        }

        return centers;
    }
}
