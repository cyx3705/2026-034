namespace HistoryStrenua;

/// <summary>
/// 快捷指令「一键出图」（1.9.0，出图类）：新建工程图（<see cref="DrawingCreate"/>）后接着对新图做孔标注全流程（<see cref="HoleFlow"/>，
/// 照页面开关）和每个视图的圆角标注（<see cref="FilletDimension"/>，轴测图跳过）。不保存。
/// </summary>
/// <remarks>
/// 顺序：视图建好、排好 → 孔标注全流程（销钉符号、中心符号线、孔位尺寸、外轮廓、孔标注、销孔标注）→ 圆角标注。
/// 圆角放最后：文字找空处时孔的尺寸已在位（它躲视图里的线；孔类注解各自的避障不管 R 尺寸）。某一步没成不中断，回执里列出来。
/// </remarks>
internal static class DrawingAuto
{
    public static QuickCommand Command { get; } = new(
        Key: "drawing-auto",
        CommandName: StrenuaIdentity.Domain + ".drawing.auto",
        Title: "一键出图",
        Summary: "新建工程图后接着做孔标注全流程和圆角标注（轴测图跳过，照页面开关），一次出一张基本标好的图，不保存。",
        Usage: "在 SolidWorks 里打开要出图的零件（或在装配体里选中一个零件）再按：先按「新建工程图」建图摆好视图与技术要求，再对新图全部视图（轴测图跳过）做「孔标注全流程」（销钉符号 → 中心符号线 → 孔位尺寸 → 外轮廓 → 孔标注 → 销孔标注，照「孔」面板的避障、尺寸链开关），最后每个视图做「圆角标注」。某一步没成不中断，最后汇总。新图不保存，请检查、微调后自己保存。",
        Run: Run);

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var created = DrawingCreate.Create(context);
        if (!created.Outcome.Success || created.Drawing is null)
            return created.Outcome;

        var api = context.Api;
        var lines = new List<string> { created.Outcome.Message };
        var failures = new List<string>();

        context.Report("一键出图：视图已建好，开始孔标注全流程。");
        var flow = HoleFlow.Run(context);
        lines.Add(flow.Message);
        if (!flow.Success)
            failures.Add("孔标注全流程");

        foreach (var view in HoleScan.SheetViews(api, created.Drawing))
        {
            context.Cancellation.ThrowIfCancellationRequested();
            if (api.Call(view, "IView", "get_ReferencedDocument") is null || HoleScan.Frame(context, view).Axonometric)
                continue;
            var name = api.CallString(view, "IView", "get_Name");
            context.Report($"一键出图：视图「{name}」圆角标注。");
            QuickOutcome fillet;
            try
            {
                fillet = FilletDimension.Run(context, view);
            }
            catch (QuickCommandException ex)
            {
                fillet = QuickOutcome.Fail(ex.Message);
            }

            lines.Add("[圆角标注] " + fillet.Message);
            if (!fillet.Success)
                failures.Add($"视图「{name}」圆角标注");
        }

        api.Call(created.Drawing, "IModelDoc2", "ClearSelection2", true);
        api.Call(created.Drawing, "IModelDoc2", "ViewZoomtofit2");
        var head = "一键出图：" + (failures.Count == 0 ? "新图建好并标完（未保存）。" : $"新图建好（未保存），{failures.Count} 步没成（{string.Join("、", failures)}）。");
        var message = head + Environment.NewLine + string.Join(Environment.NewLine, lines);
        return failures.Count == 0 ? QuickOutcome.Ok(message) : QuickOutcome.Fail(message);
    }
}
