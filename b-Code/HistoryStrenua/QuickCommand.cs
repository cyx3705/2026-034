using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// PowerSW 的一条快捷指令：页面上的一个按钮、总线上的一条指令、表格里的一行。
/// </summary>
/// <remarks>
/// <para>
/// 加一条快捷指令 = 写一个 <see cref="Run"/>，再在 <see cref="QuickCommands.All"/> 里登记一行。
/// 按钮、动作声明、指令注册、状态表都从这份登记表生成，不需要再改页面描述。
/// </para>
/// <para>
/// 当前只有「按钮」一种形态，不带参数。将来要带参数时，给这里加参数表，
/// 由页面生成器把它们渲染成按钮前面的输入框，并经动作占位符传进指令——
/// 已有的无参指令不受影响。
/// </para>
/// </remarks>
/// <param name="Key">稳定标识，也是表格行 id 与动作 id 的后缀。</param>
/// <param name="CommandName">总线指令名，形如 <c>strenua.&lt;类&gt;.&lt;方法&gt;</c>。</param>
/// <param name="Title">按钮文字。</param>
/// <param name="Usage">怎么用：写进状态表与指令摘要。</param>
/// <param name="Run">在已附着 SolidWorks 的 STA 线程上执行。</param>
internal sealed record QuickCommand(
    string Key,
    string CommandName,
    string Title,
    string Usage,
    Func<QuickCommandContext, QuickOutcome> Run)
{
    /// <summary>页面动作 id。</summary>
    public string ActionId => StrenuaIdentity.Domain + ".quick." + Key;

    /// <summary>指令名的第二段，作为命令类。</summary>
    public string CommandClass => CommandName.Split('.')[1];
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
    Action<string> report,
    Action<string> setState,
    CancellationToken cancellation)
{
    public SolidWorksSession Session { get; } = session;

    public SolidWorksApi Api => Session.Api;

    public CancellationToken Cancellation { get; } = cancellation;

    /// <summary>一行进度，进宿主控制台。</summary>
    public void Report(string message) => report(message);

    /// <summary>改状态表里这一行的「状态」格，如「等待点选视图」。</summary>
    public void SetState(string state) => setState(state);
}

/// <summary>用户能自己纠正的前置条件不满足（SolidWorks 没开、当前不是工程图……）。消息原样给人看。</summary>
internal sealed class QuickCommandException(string message) : Exception(message);

/// <summary>快捷指令登记表。顺序就是页面上按钮的顺序。</summary>
internal static class QuickCommands
{
    public static IReadOnlyList<QuickCommand> All { get; } =
    [
        HoleCallout.Command,
    ];
}
