using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「销孔标注」（1.8.0）：点一个工程图视图，销孔的孔标注带上 H7（代号与偏差值），相邻销孔之间的尺寸带 ±0.02。
/// </summary>
/// <remarks>
/// <para>销孔、站、「(仅销孔)」怎么判见 <see cref="DowelFitPlanner"/>。</para>
/// <para>H7：每种销孔一个孔标注（与「孔标注」相同，SolidWorks 自带 N×）。已有的孔标注直接改，没有就先加一个；
/// 改的是孔标注里孔径那个长度变量（<c>ICalloutVariable</c>）：公差类型「配合（带公差）」、孔配合 H7、线性显示，
/// SolidWorks 按标准表算出偏差值，显示成「⌀6H7(+0.012/0)」一类。文字变长后把引线折点挪回原处。</para>
/// <para>±0.02：已有的那段尺寸改成对称公差，没有就在两个销孔之间新加一个（水平尺寸优先连中心符号线，同孔位尺寸）。
/// 「避障」开着时最后做一遍孔位尺寸的避障，新加的数字压线就滑开。</para>
/// <para>孔位尺寸重标会删掉连着孔的线性尺寸（含这里的 ±0.02），所以全流程把本步放在孔位尺寸之后。</para>
/// </remarks>
internal static class DowelFit
{
    public static QuickCommand Command { get; } = new(
        Key: "dowel-fit",
        CommandName: StrenuaIdentity.Domain + ".hole.dowelfit",
        Title: "销孔标注",
        Summary: "点一个工程图视图，销孔的孔标注带上 H7 与偏差值，相邻销孔之间的尺寸带 ±0.02（已有的改、没有的补）。",
        Usage: "在工程图里点一个视图（先点后按、先按后点都行，60 秒内）。只认异形孔向导的销钉孔：每种销孔的孔标注（没有就先加）孔径带 H7 和偏差值；相邻销孔之间的水平 / 竖直尺寸带 ±0.02，已有的直接改、没有的新加（销孔与别的孔、与基准之间不加）；这段尺寸和别的孔的同段尺寸重叠被并成一个时，后面写「(仅销孔)」。「避障」开着时最后把压线的数字滑开。",
        Run: context => Run(context, null));

    // swCalloutVariableType_e / swFitTolDisplay_e
    private const int CalloutLength = 1;
    private const int FitLinear = 3;

    /// <summary>H7 偏差值显示几位小数。</summary>
    private const int TolerancePrecision = 3;

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="view">直接处理这个视图（全流程用）；null 时取选中的或等用户点选。</param>
    internal static QuickOutcome Run(QuickCommandContext context, object? view)
    {
        var api = context.Api;
        var scan = HoleScan.Scan(context, "销孔标注", withLines: true, view: view);
        var (document, viewName) = (scan.Document, scan.ViewName);
        var dowels = DowelFitPlanner.Dowels(scan.Candidates);
        if (dowels.Count == 0)
            return QuickOutcome.Ok($"视图「{viewName}」里没有异形孔向导的销钉孔，销孔标注没有改动。");

        var scale = scan.Geometry.Scale;
        var (leftIndex, topIndex) = HolePositionPlanner.Datums(scan.Lines);
        var left = leftIndex is { } l ? scan.Lines[l].X1 : dowels.Min(hole => hole.X);
        var top = topIndex is { } t ? scan.Lines[t].Y1 : dowels.Max(hole => hole.Y);
        var dimensions = DimensionScan.Read(api, scan);
        var plan = DowelFitPlanner.Plan(scan.Candidates, dimensions.Select(item => item.Geometry).ToList(), scale, context.Options.Chain, left, top);
        context.Report($"销孔标注：视图「{viewName}」认出 {plan.DowelCount} 个销孔共 {plan.Kinds.Count} 种，"
            + $"孔标注加 H7，相邻销孔之间 {plan.Spans.Count} 段尺寸加 ±0.02。");

        var fits = 0;
        var calloutsAdded = 0;
        var fitFailed = 0;
        var tolerated = 0;
        var spansAdded = 0;
        var spansShared = 0;
        var spanFailed = 0;
        var clearance = default(ClearanceResult);
        _ = api.Call(document, "IDrawingDoc", "ActivateView", viewName);
        try
        {
            context.SetState("孔标注加 H7");
            foreach (var kind in plan.Kinds)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                switch (FitCallout(api, scan, dimensions, kind, scale))
                {
                    case FitResult.Fitted:
                        fits++;
                        break;
                    case FitResult.AddedAndFitted:
                        fits++;
                        calloutsAdded++;
                        break;
                    default:
                        fitFailed++;
                        break;
                }
            }

            context.SetState("销孔间 ±0.02");
            foreach (var span in plan.Spans)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                object? display;
                if (span.Existing is { } index)
                {
                    display = dimensions[index].Display;
                }
                else
                {
                    var dimension = new PositionDimension(span.Axis, span.FromEdgeIndex, span.ToEdgeIndex, span.TextAt, string.Empty);
                    (display, _) = HolePosition.Insert(api, scan, null, dimension);
                    if (display is not null)
                        spansAdded++;
                }

                if (display is null || !DimensionScan.SetSymmetric(api, display, DowelFitPlanner.PlusMinus))
                {
                    spanFailed++;
                    continue;
                }

                tolerated++;
                SetSharedSuffix(api, display, span.Shared);
                if (span.Shared)
                    spansShared++;
            }

            if (context.Options.Clearance && spansAdded > 0)
            {
                context.SetState("避障");
                clearance = Clearance.ClearDimensions(context, scan, HoleCalloutPlanner.Recognize(scan.Candidates));
            }
        }
        finally
        {
            api.Call(document, "IModelDoc2", "ClearSelection2", true);
            api.Call(document, "IModelDoc2", "GraphicsRedraw2");
        }

        var message = $"视图「{viewName}」：{plan.DowelCount} 个销孔共 {plan.Kinds.Count} 种，{fits} 种孔标注已带 H7"
            + (calloutsAdded > 0 ? $"（其中 {calloutsAdded} 个孔标注是新加的）" : string.Empty)
            + (fitFailed > 0 ? $"，{fitFailed} 种没改成" : string.Empty)
            + $"；销孔间尺寸 {tolerated} 个带 ±0.02（新加 {spansAdded} 个"
            + (spansShared > 0 ? $"，{spansShared} 个兼管别的孔写了「(仅销孔)」" : string.Empty) + "）"
            + (spanFailed > 0 ? $"，{spanFailed} 个没成" : string.Empty)
            + clearance.Describe("尺寸数字")
            + "。";
        return fitFailed + spanFailed > 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }

    private enum FitResult
    {
        Failed,
        Fitted,
        AddedAndFitted,
    }

    /// <summary>一种销孔的孔标注带上 H7：已有的改（改完引线折点挪回原处），没有的先加（折点放左上，同「孔标注」）。</summary>
    private static FitResult FitCallout(
        SolidWorksApi api, ScannedView scan, IReadOnlyList<ScannedDimension> dimensions, IReadOnlyList<HoleEdge> kind, double scale)
    {
        var existing = dimensions.FirstOrDefault(item => item.Geometry.HoleCallout && item.Geometry.Anchors.Any(anchor =>
            anchor is { Hole: true, X: { } x, Y: { } y }
            && kind.Any(hole => HoleCalloutPlanner.SameCenter(new SheetPoint(x, y), new SheetPoint(hole.X, hole.Y)))));
        var first = kind[0];
        var diameter = 2 * first.Radius / scale;

        if (existing is not null)
        {
            var hole = AttachedHole(existing.Geometry, kind) ?? first;
            var (shoulder, textLeft) = NearShoulder(api, existing.Annotation, hole);
            if (!SetFit(api, existing.Display, diameter))
                return FitResult.Failed;
            if (shoulder is { } target)
                HoleCallout.AlignShoulder(api, existing.Annotation, target, textLeft);
            return FitResult.Fitted;
        }

        api.Call(scan.Document, "IModelDoc2", "ClearSelection2", true);
        var placement = HoleCalloutPlanner.Placement(first);
        var callout = api.CallBool(scan.View, "IView", "SelectEntity", scan.Edges[first.Index], false)
            ? api.Call(scan.Document, "IDrawingDoc", "AddHoleCallout2", placement.X - 0.02, placement.Y, 0.0)
            : null;
        api.Call(scan.Document, "IModelDoc2", "ClearSelection2", true);
        if (callout is null)
            return FitResult.Failed;
        var fitted = SetFit(api, callout, diameter);
        if (api.Call(callout, "IDisplayDimension", "GetAnnotation") is { } annotation)
            HoleCallout.AlignShoulder(api, annotation, placement, textLeft: true);
        return fitted ? FitResult.AddedAndFitted : FitResult.Failed;
    }

    /// <summary>孔标注连着这种销孔里的哪一个。</summary>
    private static HoleEdge? AttachedHole(ViewDimension callout, IReadOnlyList<HoleEdge> kind)
        => kind.Where(hole => callout.Anchors.Any(anchor => anchor is { Hole: true, X: { } x, Y: { } y }
                && HoleCalloutPlanner.SameCenter(new SheetPoint(x, y), new SheetPoint(hole.X, hole.Y))))
            .Cast<HoleEdge?>()
            .FirstOrDefault();

    /// <summary>孔标注下划线靠孔那一端（引线折点）与文字在哪一侧；读不到为 null。</summary>
    private static (SheetPoint? Shoulder, bool TextLeft) NearShoulder(SolidWorksApi api, object annotation, HoleEdge hole)
    {
        if (HoleCallout.Shoulder(api, annotation, textLeft: true) is not { } right
            || HoleCallout.Shoulder(api, annotation, textLeft: false) is not { } left)
            return (null, true);
        double Distance(SheetPoint p) => Math.Pow(p.X - hole.X, 2) + Math.Pow(p.Y - hole.Y, 2);
        return Distance(right) <= Distance(left) ? (right, true) : (left, false);
    }

    /// <summary>
    /// 孔标注里孔径那个长度变量设为「配合（带公差）」H7、线性显示，读回核对。
    /// </summary>
    private static bool SetFit(SolidWorksApi api, object callout, double diameter)
    {
        var variables = api.CallArray(callout, "IDisplayDimension", "GetHoleCalloutVariables");
        var described = variables
            .Select(variable => variable is not null && api.CallInt(variable, "ICalloutVariable", "get_Type") == CalloutLength
                ? (api.CallString(variable, "ICalloutVariable", "get_VariableName"),
                    Convert.ToDouble(api.Call(variable, "ICalloutLengthVariable", "get_Length")))
                : (string.Empty, double.NaN))
            .ToList();
        var index = DowelFitPlanner.DiameterVariable(described, diameter);
        if (index < 0)
            return false;

        var target = variables[index];
        api.Call(target, "ICalloutVariable", "set_ToleranceType", DimensionGeometry.ToleranceFitWithTolerance);
        api.Call(target, "ICalloutVariable", "set_HoleFit", DowelFitPlanner.HoleFit);
        api.Call(target, "ICalloutVariable", "set_FitDisplayStyle", FitLinear);
        api.Call(target, "ICalloutVariable", "set_ShowParenthesis", true);
        // 偏差值三位小数：默认两位时 ⌀5H7 的 +0.012 显示成 +0.01（真机底板）。
        api.Call(target, "ICalloutLengthVariable", "set_TolerancePrecision", TolerancePrecision);
        return api.CallInt(target, "ICalloutVariable", "get_ToleranceType") == DimensionGeometry.ToleranceFitWithTolerance
               && api.CallString(target, "ICalloutVariable", "get_HoleFit") == DowelFitPlanner.HoleFit;
    }

    /// <summary>兼管别的孔就写「(仅销孔)」后缀；不兼管了而后缀还是它就去掉。别的后缀不动。</summary>
    private static void SetSharedSuffix(SolidWorksApi api, object display, bool shared)
    {
        var suffix = api.CallString(display, "IDisplayDimension", "GetText", DimensionScan.TextSuffix);
        if (shared && suffix != DowelFitPlanner.SharedSuffix)
            api.Call(display, "IDisplayDimension", "SetText", DimensionScan.TextSuffix, DowelFitPlanner.SharedSuffix);
        else if (!shared && suffix.Trim() == DowelFitPlanner.SharedSuffix.Trim())
            api.Call(display, "IDisplayDimension", "SetText", DimensionScan.TextSuffix, string.Empty);
    }
}
