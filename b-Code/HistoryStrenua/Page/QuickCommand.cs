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
/// 当前不带参数。将来要带参数时，给这里加参数表，由页面生成器渲染成输入控件，
/// 并经动作占位符传进指令——已有的无参指令不受影响。
/// </para>
/// </remarks>
/// <param name="Key">稳定标识，也是动作 id 的后缀与 <c>strenua.quick.run key=</c> 的值。</param>
/// <param name="CommandName">总线指令名，形如 <c>strenua.&lt;类&gt;.&lt;方法&gt;</c>。</param>
/// <param name="Title">页面按钮上的字。</param>
/// <param name="Summary">一句话说明（120 字内）：写进指令自描述的 Summary。</param>
/// <param name="Usage">怎么用的完整说明：写进按钮动作的说明（悬停可见）。</param>
/// <param name="Run">在已附着 SolidWorks 的 STA 线程上执行。</param>
/// <param name="Row">
/// 在类面板里排第几行（1.17.0，用户定：一键类的「一键出钣金 / 框架 / 加工件」在「一键出图」下面另起一行）。默认 0；
/// 同一行的按钮仍交给 Aurora 自己折行。
/// </param>
internal sealed record QuickCommand(
    string Key,
    string CommandName,
    string Title,
    string Summary,
    string Usage,
    Func<QuickCommandContext, QuickOutcome> Run,
    int Row = 0)
{
    /// <summary>页面动作 id。</summary>
    public string ActionId => StrenuaIdentity.Domain + ".quick." + Key;

    /// <summary>指令名的第二段，作为命令类。</summary>
    public string CommandClass => CommandName.Split('.')[1];

    /// <summary>页面「类」选项框里显示的类名，也是那一类控制面板的 case。</summary>
    public string ClassTitle => QuickCommands.ClassTitle(CommandClass);
}

/// <summary>一次执行的结论。<see cref="Success"/> 为 false 时总线回执失败。</summary>
internal sealed record QuickOutcome(bool Success, string Message)
{
    public static QuickOutcome Ok(string message) => new(true, message);

    public static QuickOutcome Fail(string message) => new(false, message);
}

/// <summary>快捷指令执行时手里的东西。</summary>
internal sealed class QuickCommandContext(
    SolidWorksSession session,
    StrenuaOptions options,
    Action<string> report,
    Action<string> setState,
    CancellationToken cancellation,
    ICommandBus? bus = null)
{
    public SolidWorksSession Session { get; } = session;

    public SolidWorksApi Api => Session.Api;

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
            : bus.InvokeAsync(commandText, TechAi.CommandSource, Cancellation).GetAwaiter().GetResult();

    /// <summary>
    /// 经命令总线发一条别的模块的指令、不等它回来（1.16.0，检查结果弹窗 <c>aurora.ui.dialog</c>：它要等人关窗才回）。
    /// 失败只进控制台不影响本指令；没有总线（离线测试）时什么都不做。
    /// </summary>
    public void Post(string commandText)
    {
        if (bus is null)
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
        OneKeyDrawing.Command,
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
        DrawingBasic.Command,
        DrawingCreate.Command,
        DrawingProject.Command,
        DrawingIso.Command,
        DrawingArrange.Command,
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
        DecimalCheck.Command,
        DanglingCheck.Command,
        OverlapCheck.Command,
        DrawingSnapshot.Command,
        TechAi.Command,
    ];

    /// <summary>
    /// 页面上类的顺序（1.13.0）。「技术要求」类 1.13.0 一度没有按钮、只有模板表格，从登记表里分组推不出它的位置，所以顺序单独写；
    /// 1.14.0 起它有「AI 填写技术要求」一个按钮。登记表里出现了这里没有的类就排在最后。
    /// 1.15.0（用户定）：圆心位置、圆弧、倒角及其全图版从「出图」拆出，单成「倒圆倒角」类，排在出图后面。
    /// 1.16.0（用户定）：「出图」改名「基础」、「倒圆倒角」改名「倒圆」、「技术要求」改名「要求」（命令类名不变，MCP 调用照旧）；
    /// 新类「一键」只有「一键出图」（把各类的一键都做一遍），排最前。
    /// </summary>
    public static IReadOnlyList<string> ClassOrder { get; } = ["onekey", "hole", "drawing", "fillet", "tech", "check"];

    /// <summary>命令类 → 页面上的类名。新类不登记也能用，只是显示成英文类名。</summary>
    private static readonly IReadOnlyDictionary<string, string> ClassTitles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["onekey"] = "一键",
            ["hole"] = "孔",
            ["drawing"] = "基础",
            ["fillet"] = "倒圆",
            ["tech"] = "要求",
            ["check"] = "检查",
        };

    public static string ClassTitle(string commandClass)
        => ClassTitles.TryGetValue(commandClass, out var title) ? title : commandClass;
}
