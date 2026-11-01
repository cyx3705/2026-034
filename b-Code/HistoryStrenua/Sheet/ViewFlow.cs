using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>当前图纸页逐视图做一步的共用流程。</summary>
internal static class ViewFlow
{
    /// <summary>
    /// 当前图纸页全部视图（轴测图、没引用模型的跳过）逐个做 <paramref name="step"/>，某个视图没成不中断，最后汇总（全图圆弧、全图圆心、全图倒角共用）。
    /// </summary>
    internal static QuickOutcome EachView(QuickCommandContext context, string title, Func<QuickCommandContext, object?, QuickOutcome> step)
    {
        var api = context.Api;
        var document = DrawingAccess.ActiveDrawing(context);
        var views = DrawingAccess.SheetViews(api, document);
        var lines = new List<string>();
        var failures = new List<string>();
        var idle = new List<string>();
        var done = 0;
        foreach (var view in views)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            if (api.Call(view, "IView", "get_ReferencedDocument") is null || DrawingAccess.Frame(context, view).Axonometric)
                continue;
            var name = DrawingSheet.Name(api, view);
            context.Report($"{title}：视图「{name}」。");
            QuickOutcome outcome;
            try
            {
                outcome = step(context, view);
            }
            catch (QuickCommandException ex)
            {
                outcome = QuickOutcome.Fail(ex.Message);
            }
            catch (Exception ex) when (QuickError.Unexpected(ex))
            {
                outcome = QuickOutcome.Fail($"视图「{name}」{title}出错：{QuickError.Describe(ex)}");
            }

            done++;
            if (outcome.Idle)
                idle.Add($"「{name}」");
            else
                lines.Add("· " + outcome.Message);
            if (!outcome.Success)
                failures.Add($"视图「{name}」");
        }

        api.ClearSelection(document);
        if (done == 0)
            return QuickOutcome.Nothing($"{title}：当前图纸页上没有要标的视图（轴测图跳过），没有改动。");
        // 1.24.0：没东西可标的视图合成一行；全都没有就整条一行。
        if (idle.Count == done)
            return QuickOutcome.Nothing($"{title}：{done} 个视图都没有可标的，没有改动。");
        if (idle.Count > 0)
            lines.Add($"· 没有可标的：视图{string.Join("、", idle)}。");
        var head = $"{title}：当前图纸页 {done} 个视图都已做完"
            + (failures.Count > 0 ? $"，{failures.Count} 个没成（{string.Join("、", failures)}）" : string.Empty) + "。";
        var message = head + Environment.NewLine + string.Join(Environment.NewLine, lines);
        return failures.Count > 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }
}
