using HistoryStrenua.SolidWorks;

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
internal sealed record QuickCommand(
    string Key,
    string CommandName,
    string Title,
    string Summary,
    string Usage,
    Func<QuickCommandContext, QuickOutcome> Run)
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
    CancellationToken cancellation)
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
}

/// <summary>用户能自己纠正的前置条件不满足（SolidWorks 没开、当前不是工程图……）。消息原样给人看。</summary>
internal sealed class QuickCommandException(string message) : Exception(message);

/// <summary>快捷指令登记表。顺序就是页面上按钮的顺序。</summary>
internal static class QuickCommands
{
    public static IReadOnlyList<QuickCommand> All { get; } =
    [
        HoleFlow.Command,
        DowelSymbol.Command,
        CenterMark.Command,
        HolePosition.Command,
        HoleCallout.Command,
        DowelFit.Command,
        Outline.Command,
        DimensionCheck.Command,
    ];

    /// <summary>命令类 → 页面上的类名。新类不登记也能用，只是显示成英文类名。</summary>
    private static readonly IReadOnlyDictionary<string, string> ClassTitles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hole"] = "孔",
            ["check"] = "检查",
        };

    public static string ClassTitle(string commandClass)
        => ClassTitles.TryGetValue(commandClass, out var title) ? title : commandClass;
}
