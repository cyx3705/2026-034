namespace HistoryStrenua;

/// <summary>
/// 快捷指令「方形槽」（1.17.0，孔类，用户定，样图 WTJYQ-04-02 右防护板）：点一个工程图视图，标出视图里封闭的方形槽（矩形窗口）的位置与大小。
/// </summary>
/// <remarks>
/// <para>认槽与标法见 <see cref="SquareSlotPlanner"/>；基准同孔位尺寸（<see cref="HolePositionPlanner.Datums"/>），钣金折弯处的线不当基准（<see cref="SheetMetal"/>）。
/// 图上分不出槽与凸台，四条边各问一次 SolidWorks（<see cref="HoleScan.ViewGeometry.MaterialToward"/>）：有一条说正对图纸的面往矩形里面延伸就是凸台，不标；
/// 至少一条说不延伸才算槽。</para>
/// <para>普通模式：先删旧的方形槽尺寸再标，排在现有尺寸最外层之外，「避障」开着时数字压线就沿尺寸线滑开（与外轮廓同一套，<see cref="Clearance.ClearOutline"/>）。
/// 尺寸链模式：槽的四条边加进孔的那组坐标尺寸（<see cref="Outline.ExtendGroup"/>），已有同值的不加；所以全流程把它放在孔位尺寸之后。</para>
/// <para>不跨视图去重：方形槽一般只在正对它的那个视图里看得见四条边。</para>
/// </remarks>
internal static class SquareSlot
{
    public static QuickCommand Command { get; } = new(
        Key: "square-slot",
        CommandName: StrenuaIdentity.Domain + ".hole.squareslot",
        Title: "方形槽",
        Summary: "点一个工程图视图，标出封闭方形槽（矩形窗口）的位置与长宽：以零件左、上直边为基准，照「尺寸链」开关出线性或坐标尺寸。",
        Usage: "在工程图里点一个视图（先点后按、先按后点都行，60 秒内）。视图里四条直边围成的封闭矩形窗口（直角或四角同样大的圆角；开口槽归外轮廓，凸台不标）各标一组：「尺寸链」关时每个方向一层，基准到近边、近边到远边两个尺寸并排，排在已有尺寸外面（重按先删旧的）；「尺寸链」开时四条边加进孔的那组坐标尺寸。基准同孔位尺寸（零件最左、最上直边，钣金折弯处的线不算）。「避障」开着时新加尺寸的数字压线就沿尺寸线滑开。",
        Run: context => Run(context, null));

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="view">直接处理这个视图（全流程用）；null 时取选中的或等用户点选。</param>
    internal static QuickOutcome Run(QuickCommandContext context, object? view)
    {
        var api = context.Api;
        var scan = HoleScan.Scan(context, "方形槽", withLines: true, view: view);
        var (document, viewName) = (scan.Document, scan.ViewName);
        var outer = OutlinePlanner.OuterLines(scan.Lines, scan.CurveSegments);
        var found = SquareSlotPlanner.Find(scan.Lines, outer);
        var slots = found.Where(slot => IsOpening(scan, slot)).ToList();
        var bosses = found.Count - slots.Count;
        if (slots.Count == 0)
            return QuickOutcome.Ok($"视图「{viewName}」里没有封闭的方形槽" + (bosses > 0 ? $"（{bosses} 个矩形是凸台，不标）" : string.Empty) + "，没有加尺寸。");

        var (leftIndex, topIndex) = HolePositionPlanner.Datums(scan.Lines);
        if (leftIndex is not { } l || topIndex is not { } t)
            return QuickOutcome.Fail($"视图「{viewName}」里找不到" + (leftIndex is null ? "竖直的直边做左侧基准" : "水平的直边做上侧基准") + "，方形槽没有标。");

        var (left, top, scale) = (scan.Lines[l].X1, scan.Lines[t].Y1, scan.Geometry.Scale);
        var chain = context.Options.Chain;
        context.Report($"方形槽：视图「{viewName}」{slots.Count} 个方形槽" + (chain ? "（尺寸链模式）。" : "。"));
        _ = api.Call(document, "IDrawingDoc", "ActivateView", viewName);

        var (added, present, removed, failed) = (0, 0, 0, 0);
        var clearance = default(ClearanceResult);
        try
        {
            if (chain)
            {
                context.SetState("加尺寸");
                var ordinates = DimensionScan.Read(api, scan).Where(item => item.Geometry.Ordinate).ToList();
                foreach (var axis in new[] { PositionAxis.Horizontal, PositionAxis.Vertical })
                {
                    context.Cancellation.ThrowIfCancellationRequested();
                    var horizontal = axis == PositionAxis.Horizontal;
                    var stops = slots
                        .SelectMany(slot => horizontal ? new[] { slot.Left, slot.Right } : new[] { slot.Top, slot.Bottom })
                        .Select(index => new GroupStop(scan.LineEdges[index], (horizontal ? scan.Lines[index].X1 - left : top - scan.Lines[index].Y1) / scale))
                        .ToList();
                    var datum = horizontal ? l : t;
                    var (count, already, lost) = Outline.ExtendGroup(api, scan, ordinates, axis, scan.LineEdges[datum], scan.Lines[datum], stops, left, top);
                    added += count;
                    present += already;
                    failed += lost;
                }
            }
            else
            {
                context.SetState("删旧尺寸");
                var slotLines = slots.SelectMany(slot => new[] { slot.Left, slot.Right, slot.Top, slot.Bottom }).Select(index => scan.Lines[index]).ToList();
                var datums = new[] { scan.Lines[l], scan.Lines[t] };
                (removed, var leftover) = AnnotationEraser.Erase(context, document, () =>
                {
                    var dimensions = DimensionScan.Read(api, scan);
                    return SquareSlotPlanner.Obsolete(dimensions.Select(item => item.Geometry).ToList(), slotLines, datums)
                        .Select(index => dimensions[index].Annotation).ToList();
                });
                if (leftover > 0)
                    return QuickOutcome.Fail($"视图「{viewName}」：有 {leftover} 个旧方形槽尺寸删不掉，没有重标（已删 {removed} 个）。请在 SolidWorks 里手工删掉后再按。");

                context.SetState("加尺寸");
                var (firstHorizontal, firstVertical) = Outline.FirstTiers(api, scan, left, top);
                var before = Outline.DimensionNames(api, scan);
                foreach (var dimension in SquareSlotPlanner.Dimensions(slots, left, top, firstHorizontal, firstVertical))
                {
                    context.Cancellation.ThrowIfCancellationRequested();
                    var from = dimension.From < 0 ? (dimension.Axis == PositionAxis.Horizontal ? l : t) : dimension.From;
                    if (Outline.Insert(api, scan, scan.LineEdges[from], scan.LineEdges[dimension.To], dimension.Axis, dimension.TextAt))
                        added++;
                    else
                        failed++;
                }

                if (context.Options.Clearance && added > 0)
                {
                    context.SetState("避障");
                    var fresh = Outline.DimensionNames(api, scan);
                    fresh.ExceptWith(before);
                    clearance = Clearance.ClearOutline(context, scan, HoleCalloutPlanner.Recognize(scan.Candidates), fresh);
                }
            }
        }
        finally
        {
            api.Call(document, "IModelDoc2", "ClearSelection2", true);
            api.Call(document, "IModelDoc2", "GraphicsRedraw2");
        }

        var sizes = string.Join("、", slots.Select(slot => $"{slot.Rect.Width / scale * 1000:0.##}×{slot.Rect.Height / scale * 1000:0.##}"));
        var message = $"视图「{viewName}」：方形槽 {slots.Count} 个（{sizes}），"
            + (bosses > 0 ? $"{bosses} 个矩形是凸台不标，" : string.Empty)
            + (chain
                ? $"尺寸链模式新加 {added} 个坐标尺寸" + (present > 0 ? $"，{present} 站组里已有同值跳过" : string.Empty)
                : $"新加 {added} 个尺寸")
            + (removed > 0 ? $"（先删掉旧方形槽尺寸 {removed} 个）" : string.Empty)
            + (failed > 0 ? $"，{failed} 个 SolidWorks 没有接受" : string.Empty)
            + clearance.Describe("尺寸数字")
            + "。";
        return added == 0 && failed > 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }

    /// <summary>是槽不是凸台：四条边里有一条说正对图纸的面往矩形里面延伸就不是；至少一条说不延伸才是（都读不出也不算）。</summary>
    private static bool IsOpening(ScannedView scan, SlotRect slot)
    {
        var answers = new[]
        {
            scan.Geometry.MaterialToward(scan.LineEdges[slot.Left], 1, 0),
            scan.Geometry.MaterialToward(scan.LineEdges[slot.Right], -1, 0),
            scan.Geometry.MaterialToward(scan.LineEdges[slot.Top], 0, -1),
            scan.Geometry.MaterialToward(scan.LineEdges[slot.Bottom], 0, 1),
        };
        return !answers.Contains(true) && answers.Contains(false);
    }
}
