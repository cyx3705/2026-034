namespace HistoryStrenua;

/// <summary>
/// 一键类（1.17.0 起，用户定）：「一键出钣金」「一键出框架」「一键出加工件」。不保存。1.28.0（用户定）：删掉总的「一键出图」——AI 总爱用它，粒度只到这一级，类别由调用方自己判断。
/// 三条的回执末尾都带修正要点（<see cref="OneKeyFix"/>）：一键只出初稿，必须由 AI 接着修。
/// </summary>
/// <remarks>
/// <para>三条都先做「基础出图」（排版之前插技术要求），再按零件类别做下面的标注（1.17.0，用户定）：</para>
/// <list type="bullet">
/// <item>钣金：孔标注全流程（含方形槽）→ 全图外轮廓。折弯处的线（折弯切线、折弯区里的短边）在识别层就去掉了（<see cref="SheetMetal"/>），基准是外轮廓。</item>
/// <item>框架：全图外轮廓（离别的站不到一个型材宽的不标，<see cref="OutlinePlanner.Sparse"/>）→ 轴测图上写「采用 xx 铝型材」（<see cref="ProfileNote"/>）。</item>
/// <item>加工件（其他情况）：孔标注全流程 → 全图外轮廓 → 全图倒圆倒角，原来的全套。</item>
/// </list>
/// <para>钣金、框架只在一键类里有（用户定：不单独成类），它们用到的方形槽、外轮廓是孔类、基础类的普通指令。</para>
/// <para>技术要求就是同一条（用户定：技术要求已经统一交给 AI 选）：「AI 填写技术要求」开着走 AI，关着插默认模板，三类不另写默认。
/// 外轮廓放在孔标注全流程之后：尺寸链模式下它并进孔的那组坐标尺寸，孔位尺寸重标会连组删掉。检查类只读，不在里面。某一步没成不中断，回执里列出来。</para>
/// </remarks>
internal static class OneKeyDrawing
{
    public static QuickCommand SheetMetalCommand { get; } = new(
        Key: "onekey-sheetmetal",
        CommandName: StrenuaIdentity.Domain + ".onekey.sheetmetal",
        Title: "一键出钣金",
        Summary: "钣金件出初稿：基础出图（中途插技术要求）→ 孔标注全流程（含方形槽）→ 全图外轮廓，折弯处的线不标、不当基准，回执带修正要点，不保存。",
        Usage: "在 SolidWorks 里打开钣金零件再按：基础出图（中途插技术要求）→ 孔类「孔标注全流程」（销钉符号、中心符号线、孔位尺寸、方形槽、孔标注、销孔标注）→ 「全图外轮廓」。"
            + "折弯处的线不当边、不标、不当基准，以外轮廓（最外那圈）为基准：正对着看的平板与折弯圆柱面相切的那条线，以及离外轮廓不到一个折弯外半径（内 R + 板厚）的短边（折边内表面、让位缺口，如 1.5、2.21、447.79）都不算。不标圆弧倒角（折弯半径不标）。最后整理标注并按真实占位排版。照避障、尺寸链开关；新图不保存。",
        Run: context => Run(context, PartKind.SheetMetal),
        Row: 0);

    public static QuickCommand FrameCommand { get; } = new(
        Key: "onekey-frame",
        CommandName: StrenuaIdentity.Domain + ".onekey.frame",
        Title: "一键出框架",
        Summary: "铝型材框架出初稿：基础出图（中途插技术要求）→ 全图外轮廓（只标主要的站）→ 轴测图上写「采用 xx 铝型材」，回执带修正要点，不保存。",
        Usage: "在 SolidWorks 里打开铝型材骨架零件再按：基础出图（中途插技术要求）→ 「全图外轮廓」，但离别的站不到一个型材宽的不标（每根型材另一条边、端面 T 型槽口都由型材规格定了）→ "
            + "轴测图最右上角引一条注释「采用 xx 铝型材」（xx 取焊件结构构件的型材文件名，没有焊件就按每个实体的截面猜，如 2020）。不标孔、不标圆弧倒角。最后整理标注并按真实占位排版。照避障、尺寸链开关；新图不保存。",
        Run: context => Run(context, PartKind.Frame),
        Row: 0);

    public static QuickCommand MachinedCommand { get; } = new(
        Key: "onekey-machined",
        CommandName: StrenuaIdentity.Domain + ".onekey.machined",
        Title: "一键出加工件",
        Summary: "加工件（钣金、框架以外的其他情况）出初稿：基础出图（中途插技术要求）→ 孔标注全流程 → 全图外轮廓 → 全图倒圆倒角，回执带修正要点，不保存。",
        Usage: "在 SolidWorks 里打开零件再按：基础出图（中途插技术要求）→ 孔类「孔标注全流程」（销钉符号、中心符号线、孔位尺寸、方形槽、孔标注、销孔标注）→ 「全图外轮廓」→ 倒圆类「全图倒圆倒角」（全图圆心、全图圆弧、全图倒角）。"
            + "最后整理标注并按真实占位排版。照避障、尺寸链开关；某一步没成不中断，最后汇总。新图不保存。",
        Run: context => Run(context, PartKind.Machined),
        Row: 0);

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="kind">按这一类出。</param>
    internal static QuickOutcome Run(QuickCommandContext context, PartKind kind)
    {
        // 技术要求：同一条（AI 开着走 AI 并退回默认那份，关着插默认那份；1.14.0）。
        QuickOutcome Tech(object drawing) => context.Options.TechAi
            ? TechAi.Run(context, drawing, fallback: context.Options.TechDefault)
            : TechApply.Run(context, context.Options.TechDefault, drawing);

        var name = "一键出" + PartKindPlanner.Title(kind);
        var basic = DrawingBasic.Run(context, ("技术要求", Tech));
        if (basic.Drawing is not { } drawing)
            return basic.Outcome;

        var steps = new StepLog(context);
        steps.Lines.Add(basic.Outcome.Message);
        if (!basic.Outcome.Success)
            steps.Failures.Add("基础出图");

        context.Report($"{name}：按{PartKindPlanner.Title(kind)}标注。");
        switch (kind)
        {
            case PartKind.SheetMetal:
                steps.Run("孔标注全流程", () => HoleFlow.Run(context));
                steps.Run("全图外轮廓", () => OutlineAll.Run(context));
                break;
            case PartKind.Frame:
                var profile = FirstModel(context, drawing) is { } model ? ProfileNote.Read(context, model) : string.Empty;
                steps.Run("全图外轮廓", () => OutlineAll.Run(context, PartKindPlanner.ProfileWidth(profile)));
                steps.Run("型材说明", () => ProfileNote.Insert(context, drawing, profile));
                break;
            default:
                steps.Run("孔标注全流程", () => HoleFlow.Run(context));
                steps.Run("全图外轮廓", () => OutlineAll.Run(context));
                steps.Run("全图倒圆倒角", () => FilletFlow.Run(context));
                break;
        }

        steps.Run("整理标注", () => DrawingFinish.Run(context, drawing));
        // 孔标注最早标、看不到后面的尺寸，等视图和尺寸都定了再挑位置；框架不标孔。
        if (kind != PartKind.Frame)
            steps.Run("孔标注选位", () => CalloutPlacer.Run(context, drawing));
        context.Api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        context.Api.Call(drawing, "IModelDoc2", "ViewZoomtofit2");
        var outcome = steps.Finish(name, $"新图建好并按{PartKindPlanner.Title(kind)}标完（未保存）");
        return outcome with { Message = outcome.Message + Environment.NewLine + Environment.NewLine + OneKeyFix.Text(kind) };
    }

    /// <summary>当前图纸页第一个引用了模型的视图的模型（<c>IModelDoc2</c>）。</summary>
    private static object? FirstModel(QuickCommandContext context, object drawing)
        => HoleScan.SheetViews(context.Api, drawing)
            .Select(view => context.Api.Call(view, "IView", "get_ReferencedDocument"))
            .FirstOrDefault(model => model is not null);
}
