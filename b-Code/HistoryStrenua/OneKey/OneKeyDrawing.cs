namespace HistoryStrenua;

/// <summary>
/// 一键类（1.16.0 起，用户定）：「一键出图」与 1.17.0 加的「一键出钣金」「一键出框架」「一键出加工件」。不保存。
/// </summary>
/// <remarks>
/// <para>四条都先做基础类「基础出图」（排版之前插技术要求），再按零件类别做下面的标注（1.17.0，用户定）：</para>
/// <list type="bullet">
/// <item>钣金：孔标注全流程（含方形槽）→ 全图外轮廓。折弯处的线（折弯切线、折弯区里的短边）在识别层就去掉了（<see cref="SheetMetal"/>），基准是外轮廓。</item>
/// <item>框架：全图外轮廓（离别的站不到一个型材宽的不标，<see cref="OutlinePlanner.Sparse"/>）→ 轴测图上写「采用 xx 铝型材」（<see cref="ProfileNote"/>）。</item>
/// <item>加工件（其他情况）：孔标注全流程 → 全图外轮廓 → 全图倒圆倒角，即 1.16.0 的一键出图。</item>
/// </list>
/// <para>「一键出图」不问人：图建好、排好后让 AI 看截图与图纸信息选这是三类里的哪一类（<see cref="PartKindAi"/>），AI 没成按特征树猜。
/// 钣金、框架只在一键类里有（用户定：不单独成类），它们用到的方形槽、外轮廓是孔类、基础类的普通指令。</para>
/// <para>技术要求就是同一条（用户定：技术要求已经统一交给 AI 选）：「AI 填写技术要求」开着走 AI，关着插默认模板，三类不另写默认。
/// 外轮廓放在孔标注全流程之后：尺寸链模式下它并进孔的那组坐标尺寸，孔位尺寸重标会连组删掉。检查类只读，不在里面。某一步没成不中断，回执里列出来。</para>
/// </remarks>
internal static class OneKeyDrawing
{
    public static QuickCommand Command { get; } = new(
        Key: "onekey-drawing",
        CommandName: StrenuaIdentity.Domain + ".onekey.drawing",
        Title: "一键出图",
        Summary: "基础出图（中途插技术要求）后由 AI 判断这是钣金、框架还是加工件，再按那一类标注，一次出一张基本标好的图，不保存。",
        Usage: "在 SolidWorks 里打开要出图的零件（或在装配体里选中一个零件）再按：先做基础类「基础出图」（新建工程图 → 投影视图 → 轴测图 → 技术要求 → 排版 → 对称轴；技术要求同「要求」类：「AI 填写技术要求」开着由 AI 选，关着插默认那份），"
            + "再把图纸截图和零件信息交给 AI（经 HistoryApollo）判断类别——钣金、框架（铝型材骨架）还是加工件（其他）——照下面那一行同名按钮的做法标注；AI 没成就按特征树猜（有钣金特征是钣金、有焊件结构构件是框架、其余加工件）。"
            + "都照避障、尺寸链开关。某一步没成不中断，最后汇总。新图不保存，请检查、微调后自己保存。",
        Run: context => Run(context, null));

    public static QuickCommand SheetMetalCommand { get; } = new(
        Key: "onekey-sheetmetal",
        CommandName: StrenuaIdentity.Domain + ".onekey.sheetmetal",
        Title: "一键出钣金",
        Summary: "钣金件出图：基础出图（中途插技术要求）→ 孔标注全流程（含方形槽）→ 全图外轮廓，折弯处的线不标、不当基准，不保存。",
        Usage: "在 SolidWorks 里打开钣金零件再按：基础出图（同「一键出图」，含技术要求）→ 孔类「孔标注全流程」（销钉符号、中心符号线、孔位尺寸、方形槽、孔标注、销孔标注）→ 基础类「全图外轮廓」。"
            + "折弯处的线不当边、不标、不当基准，以外轮廓（最外那圈）为基准：正对着看的平板与折弯圆柱面相切的那条线，以及离外轮廓不到一个折弯外半径（内 R + 板厚）的短边（折边内表面、让位缺口，如 1.5、2.21、447.79）都不算。不标圆弧倒角（折弯半径不标）。照避障、尺寸链开关；新图不保存。",
        Run: context => Run(context, PartKind.SheetMetal),
        Row: 1);

    public static QuickCommand FrameCommand { get; } = new(
        Key: "onekey-frame",
        CommandName: StrenuaIdentity.Domain + ".onekey.frame",
        Title: "一键出框架",
        Summary: "铝型材框架出图：基础出图（中途插技术要求）→ 全图外轮廓（只标主要的站）→ 轴测图上写「采用 xx 铝型材」，不保存。",
        Usage: "在 SolidWorks 里打开铝型材骨架零件再按：基础出图（同「一键出图」，含技术要求）→ 基础类「全图外轮廓」，但离别的站不到一个型材宽的不标（每根型材另一条边、端面 T 型槽口都由型材规格定了）→ "
            + "轴测图最右上角引一条注释「采用 xx 铝型材」（xx 取焊件结构构件的型材文件名，没有焊件就按每个实体的截面猜，如 2020）。不标孔、不标圆弧倒角。照避障、尺寸链开关；新图不保存。",
        Run: context => Run(context, PartKind.Frame),
        Row: 1);

    public static QuickCommand MachinedCommand { get; } = new(
        Key: "onekey-machined",
        CommandName: StrenuaIdentity.Domain + ".onekey.machined",
        Title: "一键出加工件",
        Summary: "加工件（钣金、框架以外的其他情况）出图：基础出图（中途插技术要求）→ 孔标注全流程 → 全图外轮廓 → 全图倒圆倒角，不保存。",
        Usage: "在 SolidWorks 里打开零件再按：基础出图（同「一键出图」，含技术要求）→ 孔类「孔标注全流程」（销钉符号、中心符号线、孔位尺寸、方形槽、孔标注、销孔标注）→ 基础类「全图外轮廓」→ 倒圆类「全图倒圆倒角」（全图圆心、全图圆弧、全图倒角）。"
            + "照避障、尺寸链开关；某一步没成不中断，最后汇总。新图不保存。",
        Run: context => Run(context, PartKind.Machined),
        Row: 1);

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="fixedKind">按这一类出；null 时图建好后由 AI 判断（「一键出图」）。</param>
    internal static QuickOutcome Run(QuickCommandContext context, PartKind? fixedKind)
    {
        // 技术要求：同一条（AI 开着走 AI 并退回默认那份，关着插默认那份；1.14.0）。
        QuickOutcome Tech(object drawing) => context.Options.TechAi
            ? TechAi.Run(context, drawing, fallback: context.Options.TechDefault)
            : TechApply.Run(context, context.Options.TechDefault, drawing);

        var name = fixedKind is { } named ? "一键出" + PartKindPlanner.Title(named) : "一键出图";
        var basic = DrawingBasic.Run(context, ("技术要求", Tech));
        if (basic.Drawing is not { } drawing)
            return basic.Outcome;

        var steps = new StepLog(context);
        steps.Lines.Add(basic.Outcome.Message);
        if (!basic.Outcome.Success)
            steps.Failures.Add("基础出图");

        var kind = fixedKind ?? PartKind.Machined;
        if (fixedKind is null)
        {
            context.Report("一键出图：视图已建好、排好，让 AI 判断零件类别。");
            var judged = PartKindAi.Classify(context, drawing);
            kind = judged.Kind;
            steps.Lines.Add(judged.Message);
        }

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

        context.Api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        context.Api.Call(drawing, "IModelDoc2", "ViewZoomtofit2");
        return steps.Finish(name, $"新图建好并按{PartKindPlanner.Title(kind)}标完（未保存）");
    }

    /// <summary>当前图纸页第一个引用了模型的视图的模型（<c>IModelDoc2</c>）。</summary>
    private static object? FirstModel(QuickCommandContext context, object drawing)
        => HoleScan.SheetViews(context.Api, drawing)
            .Select(view => context.Api.Call(view, "IView", "get_ReferencedDocument"))
            .FirstOrDefault(model => model is not null);
}
