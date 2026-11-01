using HistoryStrenua.SolidWorks;
using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// PowerSW 的一条快捷指令：总线上的一条指令、页面上它那一类控制面板里的一个按钮。
/// </summary>
/// <remarks>
/// <para>
/// 加一条快捷指令 = 写一个 <see cref="Run"/>，再在 <see cref="QuickCommands.All"/> 里登记一行。
/// 动作声明、指令注册、按钮、类选项都从这份登记表生成，不需要再改页面描述。
/// </para>
/// <para>
/// 带参数的（1.20.0 起「视图」「标注」两类，用户定：和以前的指令待遇一样，上页面、进列表）登记 <see cref="Parameters"/> 与 <see cref="Bind"/>：
/// 页面生成器把这一类的参数渲染成面板上方的输入框（同名参数共用一个框），动作经 <c>{参数名}</c> 占位符取框里的值传进指令；
/// <see cref="Bind"/> 在附着 SolidWorks 之前解析参数，填错直接失败、不碰图。无参指令不受影响。
/// </para>
/// </remarks>
/// <param name="Key">稳定标识，也是动作 id 的后缀与 <c>strenua.quick.run key=</c> 的值。</param>
/// <param name="CommandName">总线指令名，形如 <c>strenua.&lt;类&gt;.&lt;方法&gt;</c>。</param>
/// <param name="Title">页面按钮上的字。</param>
/// <param name="Summary">一句话说明（120 字内）：写进指令自描述的 Summary。</param>
/// <param name="Usage">怎么用的完整说明：写进按钮动作的说明（悬停可见）。</param>
/// <param name="Run">在已附着 SolidWorks 的 STA 线程上执行。</param>
/// <param name="Row">
/// 在类面板里排第几行（1.17.0，用户定：同类按钮分行）。默认 0；
/// 同一行的按钮仍交给 Aurora 自己折行。
/// </param>
/// <param name="Parameters">参数表；无参指令为 null。</param>
/// <param name="Example">照抄能跑的例子（带全部必填参数）；null 时就是指令名。</param>
/// <param name="Readonly">不改图（只读回，如视图列表、注解列表）。</param>
/// <param name="Bind">
/// 带参数的指令：解析参数，返回在 STA 线程上执行的那一段；参数不对抛 <see cref="QuickArgumentException"/>。为 null 时执行 <see cref="Run"/>。
/// </param>
/// <param name="Page">
/// 页面上归哪一类（1.23.0，用户定：基础类的视图指令挪进视图类、其余并进一键类，页数少）；null 时就是 <see cref="CommandClass"/>。
/// 只动页面，指令名不变，MCP 调用照旧。
/// </param>
/// <param name="SelfReport">
/// 回执自己已读回改了什么（挪标注「现在占…」、删标注、改尺寸读回显示文字），经 MCP 调用也不再附本步 SVG（1.24.0：附上的和文字说的完全重复）。
/// </param>
internal sealed record QuickCommand(
    string Key,
    string CommandName,
    string Title,
    string Summary,
    string Usage,
    Func<QuickCommandContext, QuickOutcome> Run,
    int Row = 0,
    IReadOnlyList<QuickParameter>? Parameters = null,
    string? Example = null,
    bool Readonly = false,
    Func<CommandContext, Func<QuickCommandContext, QuickOutcome>>? Bind = null,
    string? Page = null,
    bool SelfReport = false)
{
    /// <summary>带参数的指令的 <see cref="Run"/>：只经 <see cref="Bind"/> 执行，走到这里说明调用方没给参数。</summary>
    public static QuickOutcome Unbound(QuickCommandContext context) => QuickOutcome.Fail("这条指令要带参数。");

    /// <summary>参数表（无参为空）。</summary>
    public IReadOnlyList<QuickParameter> Arguments => Parameters ?? [];

    /// <summary>页面动作 id。</summary>
    public string ActionId => StrenuaIdentity.Domain + ".quick." + Key;

    /// <summary>指令名的第二段，作为命令类。</summary>
    public string CommandClass => CommandName.Split('.')[1];

    /// <summary>页面上的类（<see cref="Page"/>，没登记就是 <see cref="CommandClass"/>）。</summary>
    public string PageClass => Page ?? CommandClass;

    /// <summary>页面「类」选项框里显示的类名，也是那一类控制面板的 case。</summary>
    public string ClassTitle => QuickCommands.ClassTitle(PageClass);
}

/// <summary>一次执行的结论。<see cref="Success"/> 为 false 时总线回执失败。</summary>
/// <param name="Success">成没成。</param>
/// <param name="Message">回执文字（进控制台）。</param>
/// <param name="Data">成功回执带的数据（1.20.0，细粒度读回指令的行）；快捷指令都是 null。</param>
internal sealed record QuickOutcome(bool Success, string Message, object? Data = null)
{
    public static QuickOutcome Ok(string message) => new(true, message);

    public static QuickOutcome Ok(string message, object data) => new(true, message, data);

    public static QuickOutcome Fail(string message) => new(false, message);

    /// <summary>
    /// 成功但图上什么都没动（没有可标的东西、都已标过）。1.24.0：多步、多视图的汇总把这类合成一行，回执不再一行一行「没有…」。
    /// </summary>
    public static QuickOutcome Nothing(string message) => new(true, message) { Idle = true };

    /// <summary>见 <see cref="Nothing"/>。</summary>
    public bool Idle { get; init; }
}

/// <summary>快捷指令执行时手里的东西。</summary>
internal sealed class QuickCommandContext(
    SolidWorksSession session,
    StrenuaOptions options,
    Action<string> report,
    Action<string> setState,
    CancellationToken cancellation,
    ICommandBus? bus = null,
    bool popups = true)
{
    public SolidWorksSession Session { get; } = session;

    public SolidWorksApi Api => Session.Api;

    /// <summary>本条命令开始处理的工程图。多步流程锁定这张图，避免 SW 激活引用零件后余下步骤跑错文档。</summary>
    public object? TargetDrawing { get; set; }

    /// <summary>这条指令里已识别的视图（多步流程不重复读同一视图的边，见 <see cref="HoleScan.Scan"/>）。</summary>
    internal Dictionary<string, (double[] Transform, int Generation, ScannedView Scan)> Scans { get; } = new(StringComparer.Ordinal);

    /// <summary>页面开关（避障、尺寸链模式）的当前值。</summary>
    public StrenuaOptions Options { get; } = options;

    public CancellationToken Cancellation { get; } = cancellation;

    /// <summary>一行进度，进宿主控制台。</summary>
    public void Report(string message) => report(message);

    /// <summary>改这条指令的状态（<c>strenua.quick.list</c> 里看得到），如「等待点选视图」。</summary>
    public void SetState(string state) => setState(state);

    /// <summary>
    /// 经命令总线安静执行一条别的模块的指令（1.14.0，调 HistoryApollo）并等它回来。在 SolidWorks 的 STA 线程上阻塞等待：
    /// 这条线程没有同步上下文，总线的续体落在线程池，不会互等；等待期间不碰 SolidWorks。没有总线（离线测试）时回失败回执。
    /// </summary>
    public CommandResult Invoke(string commandText)
        => bus is null
            ? CommandResult.Fail("没有命令总线（离线运行），调不到别的模块。")
            : Session.Yield(() => bus.InvokeAsync(commandText, TechAi.CommandSource, Cancellation).GetAwaiter().GetResult());

    /// <summary>
    /// 经命令总线发一条别的模块的指令、不等它回来（1.16.0，检查结果弹窗 <c>aurora.ui.dialog</c>：它要等人关窗才回）。
    /// 失败只进控制台不影响本指令；没有总线（离线测试）或 AI 调用时（1.29.0，用户定：只有手动点才弹窗）什么都不做。
    /// </summary>
    public void Post(string commandText)
    {
        if (bus is null || !popups)
            return;
        _ = bus.InvokeAsync(commandText, TechAi.CommandSource, CancellationToken.None).ContinueWith(task =>
        {
            if (!task.IsCompletedSuccessfully)
                report("弹窗没弹出来：" + (task.Exception?.GetBaseException().Message ?? "已取消"));
            else if (!task.Result.Success)
                report("弹窗没弹出来：" + task.Result.Message);
        }, TaskScheduler.Default);
    }
}

/// <summary>用户能自己纠正的前置条件不满足（SolidWorks 没开、当前不是工程图……）。消息原样给人看。</summary>
internal sealed class QuickCommandException(string message) : Exception(message);

/// <summary>快捷指令登记表。顺序就是页面上按钮的顺序。</summary>
internal static class QuickCommands
{
    public static IReadOnlyList<QuickCommand> All { get; } =
    [
        OneKeyDrawing.SheetMetalCommand,
        OneKeyDrawing.FrameCommand,
        OneKeyDrawing.MachinedCommand,
        HoleFlow.Command,
        DowelSymbol.Command,
        CenterMark.Command,
        HolePosition.Command,
        HoleCallout.Command,
        DowelFit.Command,
        SquareSlot.Command,
        CalloutPlacer.Command,
        DrawingBasic.Command,
        DrawingCreate.Command,
        DrawingProject.Command,
        DrawingIso.Command,
        DrawingArrange.Command,
        DrawingFinish.Command,
        SymmetryAxes.Command,
        OutlineAll.Command,
        Outline.Command,
        FilletFlow.Command,
        ArcCenterAll.Command,
        FilletAll.Command,
        ChamferAll.Command,
        ArcCenter.Command,
        FilletDimension.Command,
        ChamferDimension.Command,
        DimensionCheck.Command,
        RedundantCheck.Command,
        ChainCheck.Command,
        DecimalCheck.Command,
        DanglingCheck.Command,
        OverlapCheck.Command,
        DrawingSnapshot.Command,
        DrawingSvg.Command,
        TechAi.Command,
        ViewList.Command,
        MoveView.Command,
        DimList.Command,
        DimDelete.Command,
        DimMove.Command,
        DimEdit.Command,
        DimAdd.Command,
        DimHole.Command,
        DimCenter.Command,
        DocOpen.ListCommand,
        DocOpen.OpenCommand,
        DocOpen.ActivateCommand,
        DocSave.SaveCommand,
        DocSave.CloseCommand,
        PartProperties.ReadCommand,
        PartProperties.WriteCommand,
    ];

    /// <summary>
    /// 页面上类的顺序（1.13.0）。「技术要求」类 1.13.0 一度没有按钮、只有模板表格，从登记表里分组推不出它的位置，所以顺序单独写；
    /// 1.14.0 起它有「AI 填写技术要求」一个按钮。登记表里出现了这里没有的类就排在最后。
    /// 1.15.0（用户定）：圆心位置、圆弧、倒角及其全图版从「出图」拆出，单成「倒圆倒角」类，排在出图后面。
    /// 1.16.0（用户定）：「出图」改名「基础」、「倒圆倒角」改名「倒圆」、「技术要求」改名「要求」（命令类名不变，MCP 调用照旧）；
    /// 新类「一键」排最前。
    /// 1.20.0（用户定：新指令待遇和以前一样）：带参数的「视图」「标注」两类排在倒圆后面。
    /// 1.23.0（用户定，页数少）：「基础」类拆掉——新建工程图、投影视图、轴测图、排版进「视图」，其余（基础出图、整理标注、对称轴、外轮廓）并进「一键」；
    /// 按 <see cref="QuickCommand.PageClass"/> 分，指令名（<c>strenua.drawing.*</c>）不变。
    /// </summary>
    public static IReadOnlyList<string> ClassOrder { get; } = ["onekey", "hole", "fillet", "view", "dim", "doc", "tech", "prop", "check"];

    /// <summary>命令类 → 页面上的类名。新类不登记也能用，只是显示成英文类名。</summary>
    private static readonly IReadOnlyDictionary<string, string> ClassTitles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["onekey"] = "一键",
            ["hole"] = "孔",
            ["drawing"] = "基础",
            ["fillet"] = "倒圆",
            ["view"] = "视图",
            ["dim"] = "标注",
            ["doc"] = "文档",
            ["tech"] = "要求",
            ["prop"] = "属性",
            ["check"] = "检查",
        };

    public static string ClassTitle(string commandClass)
        => ClassTitles.TryGetValue(commandClass, out var title) ? title : commandClass;
}
