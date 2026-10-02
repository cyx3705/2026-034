namespace HistoryStrenua;

/// <summary>
/// 快捷指令「孔标注全流程」（1.6.0）：当前图纸页上的全部视图逐个做一遍
/// 销钉符号 → 中心符号线 → 孔位尺寸 → 孔标注（后两步各自带标注避障），全部视图做完指令才结束。
/// </summary>
/// <remarks>
/// <para>顺序是用户定的：先销钉符号，再中心符号线（孔位尺寸的水平尺寸要连它），再孔位尺寸，孔标注最后加，
/// 这样孔标注避障时尺寸都已在位。避障不单独成步，并在孔位尺寸与孔标注里（用户定）。</para>
/// <para>不用点选视图；范围只是当前图纸页（用户定，不切换图纸页）。某个视图的某一步失败（如找不到基准边）
/// 不中断，接着做后面的步骤与视图，最后回执失败并列出哪几步没成；取消则立即停。</para>
/// </remarks>
internal static class HoleFlow
{
    public static QuickCommand Command { get; } = new(
        Key: "hole-flow",
        CommandName: StrenuaIdentity.Domain + ".hole.flow",
        Title: "孔标注全流程",
        Summary: "当前图纸页全部视图依次加销钉符号、中心符号线、孔位尺寸、孔标注并避障，全部做完才结束。",
        Usage: "不用点视图：当前图纸页上的全部视图逐个做一遍「销钉符号 → 中心符号线 → 孔位尺寸 → 孔标注」，孔位尺寸与孔标注各自带避障（文字压在别的孔的标注线条上就挪开）；某一步失败不中断，最后汇总。",
        Run: Run);

    private static readonly (string Title, Func<QuickCommandContext, object, QuickOutcome> Run)[] Steps =
    [
        ("销钉符号", DowelSymbol.Run),
        ("中心符号线", CenterMark.Run),
        ("孔位尺寸", HolePosition.Run),
        ("孔标注", HoleCallout.Run),
    ];

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var api = context.Api;
        var document = HoleScan.ActiveDrawing(context);
        var views = HoleScan.SheetViews(api, document);
        if (views.Count == 0)
            return QuickOutcome.Ok("当前图纸页上没有视图，孔标注全流程没有改动。");

        var lines = new List<string>();
        var failures = new List<string>();
        for (var i = 0; i < views.Count; i++)
        {
            var view = views[i];
            var viewName = api.CallString(view, "IView", "get_Name");
            if (api.Call(view, "IView", "get_ReferencedDocument") is null)
            {
                lines.Add($"视图「{viewName}」没有引用模型，跳过。");
                continue;
            }

            foreach (var (title, step) in Steps)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                context.Report($"孔标注全流程：视图 {i + 1}/{views.Count}「{viewName}」— {title}。");
                QuickOutcome outcome;
                try
                {
                    outcome = step(context, view);
                }
                catch (QuickCommandException ex)
                {
                    outcome = QuickOutcome.Fail(ex.Message);
                }

                lines.Add($"[{title}] {outcome.Message}");
                if (!outcome.Success)
                    failures.Add($"视图「{viewName}」{title}");
            }
        }

        var head = $"孔标注全流程：当前图纸页 {views.Count} 个视图都已做完"
            + (failures.Count > 0 ? $"，{failures.Count} 步没成（{string.Join("、", failures)}）" : string.Empty)
            + "。";
        var message = head + Environment.NewLine + string.Join(Environment.NewLine, lines);
        return failures.Count > 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }
}
