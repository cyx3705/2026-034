namespace HistoryStrenua;

/// <summary>
/// 快捷指令「基础出图」（1.16.0，基础类；1.9.0 起的「一键出图」收窄而来）：只做基础类里有的几步——新建工程图 → 投影视图 → 轴测图 → 排版 → 对称轴。
/// 不插技术要求、不标注，不保存。全套（再加要求、孔标注全流程、全图倒圆倒角）是一键类的三条（<see cref="OneKeyDrawing"/>）。
/// </summary>
/// <remarks>
/// <para>每一步都是一条单独的指令（用户定：人手工、AI 经 MCP 都能一步步做、中间改），这里只是按顺序调它们，零件只读一次、主视图沿用刚建的那个。
/// 某一步没成不中断，回执里列出来。</para>
/// <para>对称轴放最后：视图排好了再加，轴跟着视图走；「孔位尺寸」「圆心位置」要轴时也会自己补，这里加上是因为基础类有这一步（用户定：理论上对称的视图就要加）。</para>
/// </remarks>
internal static class DrawingBasic
{
    public static QuickCommand Command { get; } = new(
        Key: "drawing-basic",
        CommandName: StrenuaIdentity.Domain + ".drawing.basic",
        Title: "基础出图",
        Summary: "依次做新建工程图、投影视图、轴测图、排版、对称轴，出一张视图齐全、排好的新图（不插技术要求、不标注），不保存。",
        Usage: "在 SolidWorks 里打开要出图的零件（或在装配体里选中一个零件）再按：依次做视图类的「新建工程图 → 投影视图 → 轴测图 → 排版」与「对称轴」（轴测图、投影视图照避障开关找空地）。不插技术要求、不标注——技术要求在「要求」类里点模板，标注用孔类、倒圆类，或直接用一键类「一键出钣金 / 框架 / 加工件」全做。某一步没成不中断，最后汇总。新图不保存。",
        Run: context => Run(context).Outcome,
        Row: 2, Page: "onekey");

    /// <summary>做完的结论与新图（<c>IModelDoc2</c>）；新建工程图没成时新图为 null。</summary>
    internal sealed record Result(QuickOutcome Outcome, object? Drawing);

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="beforeArrange">
    /// 排版之前插一步（「一键出图」在这里插技术要求：排版要按技术要求实际占的地方排视图，与 1.9.0 起的顺序一致）；null 不插。
    /// </param>
    internal static Result Run(QuickCommandContext context, (string Title, Func<object, QuickOutcome> Run)? beforeArrange = null)
    {
        var created = DrawingCreate.Create(context, next: false);
        if (!created.Outcome.Success || created.Drawing is not { } drawing || created.Main is not { } main || created.Part is not { } part)
            return new Result(created.Outcome, null);

        var steps = new StepLog(context);
        context.TargetDrawing = drawing;
        steps.Lines.Add(created.Outcome.Message);
        DrawingSheet Sheet() => DrawingSheet.Read(context, drawing, "基础出图", main, part);
        steps.Run("投影视图", () => DrawingProject.Run(context, Sheet()));
        steps.Run("轴测图", () => DrawingIso.Run(context, Sheet()));
        if (beforeArrange is { } extra)
            steps.Run(extra.Title, () => extra.Run(drawing));
        steps.Run("排版", () => DrawingArrange.Run(context, Sheet()));
        steps.Run("对称轴", () => SymmetryAxes.Run(context));

        context.Api.ClearSelection(drawing);
        context.Api.Call(drawing, "IModelDoc2", "ViewZoomtofit2");
        return new Result(steps.Finish("基础出图", "新图建好、视图排好（未保存）"), drawing);
    }
}

