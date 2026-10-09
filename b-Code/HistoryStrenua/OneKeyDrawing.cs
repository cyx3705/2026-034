namespace HistoryStrenua;

/// <summary>
/// 快捷指令「一键出图」（1.16.0，一键类，用户定）：把别的类的「一键」都做一遍——基础出图（中途插要求）→ 孔标注全流程 → 全图倒圆倒角。不保存。
/// </summary>
/// <remarks>
/// <para>与 1.9.0–1.15.0 出图类的「一键出图」做的事一样，只是改成调各类自己的一键：基础类「基础出图」（<see cref="DrawingBasic"/>）、
/// 要求类的技术要求（排版之前插：排版要按它实际占的地方排视图）、孔类「孔标注全流程」（<see cref="HoleFlow"/>）、倒圆类「全图倒圆倒角」（<see cref="FilletFlow"/>）。</para>
/// <para>技术要求插默认模板（表格「设置」列）；「AI 填写技术要求」开着时由 AI 选基础并微调，AI 没成退回默认那份。
/// 检查类只读、不改图，不在里面。某一步没成不中断，回执里列出来。</para>
/// </remarks>
internal static class OneKeyDrawing
{
    public static QuickCommand Command { get; } = new(
        Key: "onekey-drawing",
        CommandName: StrenuaIdentity.Domain + ".onekey.drawing",
        Title: "一键出图",
        Summary: "把各类的一键都做一遍：基础出图（中途插技术要求）、孔标注全流程、全图倒圆倒角，一次出一张基本标好的图，不保存。",
        Usage: "在 SolidWorks 里打开要出图的零件（或在装配体里选中一个零件）再按：依次做基础类「基础出图」（新建工程图 → 投影视图 → 轴测图 → 技术要求 → 排版 → 对称轴；技术要求插「要求」类表格里设为默认的那份，「AI 填写技术要求」开着时由 AI 选基础并微调），再对新图做孔类「孔标注全流程」（销钉符号 → 中心符号线 → 孔位尺寸 → 外轮廓 → 孔标注 → 销孔标注）和倒圆类「全图倒圆倒角」（全图圆心 → 全图圆弧 → 全图倒角），都照避障、尺寸链开关。每一步也都能在各自的类里单独按。某一步没成不中断，最后汇总。新图不保存，请检查、微调后自己保存。",
        Run: Run);

    private static QuickOutcome Run(QuickCommandContext context)
    {
        // 技术要求：默认模板；「AI 填写技术要求」开着时由 AI 选基础并微调，AI 没成退回默认那份（1.14.0）。
        QuickOutcome Tech(object drawing) => context.Options.TechAi
            ? TechAi.Run(context, drawing, fallback: context.Options.TechDefault)
            : TechApply.Run(context, context.Options.TechDefault, drawing);

        var basic = DrawingBasic.Run(context, ("技术要求", Tech));
        if (basic.Drawing is not { } drawing)
            return basic.Outcome;

        var steps = new StepLog(context);
        steps.Lines.Add(basic.Outcome.Message);
        if (!basic.Outcome.Success)
            steps.Failures.Add("基础出图");
        context.Report("一键出图：视图已建好、排好，开始孔标注全流程。");
        steps.Run("孔标注全流程", () => HoleFlow.Run(context));
        steps.Run("全图倒圆倒角", () => FilletFlow.Run(context));

        context.Api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        context.Api.Call(drawing, "IModelDoc2", "ViewZoomtofit2");
        return steps.Finish("一键出图", "新图建好并标完（未保存）");
    }
}
