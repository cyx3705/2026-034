namespace HistoryStrenua;

/// <summary>
/// 快捷指令「全图圆角」（1.9.0，出图类，从「一键出图」拆出）：当前图纸页全部视图（轴测图跳过）逐个做「圆角标注」（<see cref="FilletDimension"/>）。
/// </summary>
/// <remarks>
/// 不用点视图（AI 经 MCP 调用时点不了视图）。某个视图没成不中断，最后汇总；范围只是当前图纸页，与「孔标注全流程」一样。
/// </remarks>
internal static class FilletAll
{
    public static QuickCommand Command { get; } = new(
        Key: "fillet-all",
        CommandName: StrenuaIdentity.Domain + ".drawing.filletall",
        Title: "全图圆角",
        Summary: "当前图纸页全部视图（轴测图跳过）逐个做圆角标注：R1 不标，同半径 3 个以上合标 N x R，已标的跳过。",
        Usage: "不用点视图：当前图纸页上的全部视图（轴测图跳过）逐个做「圆角标注」——正对图纸的圆角弧各加一个 R 尺寸，文字放在零件外的空处；R1 不标（技术要求「未注圆角R1」），同一半径 3 个以上只标一个并写「N x R」，已有 R 或直径尺寸的跳过。某个视图没成不中断，最后汇总。",
        Run: Run);

    internal static QuickOutcome Run(QuickCommandContext context)
    {
        var api = context.Api;
        var document = HoleScan.ActiveDrawing(context);
        var views = HoleScan.SheetViews(api, document);
        var lines = new List<string>();
        var failures = new List<string>();
        var done = 0;
        foreach (var view in views)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            if (api.Call(view, "IView", "get_ReferencedDocument") is null || HoleScan.Frame(context, view).Axonometric)
                continue;
            var name = DrawingSheet.Name(api, view);
            context.Report($"全图圆角：视图「{name}」。");
            QuickOutcome outcome;
            try
            {
                outcome = FilletDimension.Run(context, view);
            }
            catch (QuickCommandException ex)
            {
                outcome = QuickOutcome.Fail(ex.Message);
            }

            done++;
            lines.Add("· " + outcome.Message);
            if (!outcome.Success)
                failures.Add($"视图「{name}」");
        }

        api.Call(document, "IModelDoc2", "ClearSelection2", true);
        if (done == 0)
            return QuickOutcome.Ok("全图圆角：当前图纸页上没有要标圆角的视图（轴测图跳过），没有改动。");
        var head = $"全图圆角：当前图纸页 {done} 个视图都已做完"
            + (failures.Count > 0 ? $"，{failures.Count} 个没成（{string.Join("、", failures)}）" : string.Empty) + "。";
        var message = head + Environment.NewLine + string.Join(Environment.NewLine, lines);
        return failures.Count > 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }
}
