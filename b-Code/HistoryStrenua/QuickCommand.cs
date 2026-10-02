using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// PowerSW 的一条快捷指令：总线上的一条指令、页面状态表里的一行（点「指令」列的名称就执行）。
/// </summary>
/// <remarks>
/// <para>
/// 加一条快捷指令 = 写一个 <see cref="Run"/>，再在 <see cref="QuickCommands.All"/> 里登记一行。
/// 动作声明、指令注册、状态表行、类选项都从这份登记表生成，不需要再改页面描述。
/// </para>
/// <para>
/// 当前不带参数。将来要带参数时，给这里加参数表，由页面生成器渲染成输入控件，
/// 并经动作占位符传进指令——已有的无参指令不受影响。
/// </para>
/// </remarks>
/// <param name="Key">稳定标识，也是表格行 id 与动作 id 的后缀。</param>
/// <param name="CommandName">总线指令名，形如 <c>strenua.&lt;类&gt;.&lt;方法&gt;</c>。</param>
/// <param name="Title">状态表「指令」列里那个可点的名称。</param>
/// <param name="Summary">一句话说明（120 字内）：写进指令自描述的 Summary。</param>
/// <param name="Usage">怎么用的完整说明：写进页面状态表与 <c>strenua.quick.list</c>。</param>
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

    /// <summary>页面「类」选项框与状态表「类」列里显示的类名。</summary>
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

/// <summary>快捷指令登记表。顺序就是页面状态表里行的顺序。</summary>
internal static class QuickCommands
{
    public static IReadOnlyList<QuickCommand> All { get; } =
    [
        HoleCallout.Command,
        CenterMark.Command,
        HolePosition.Command,
    ];

    /// <summary>命令类 → 页面上的类名。新类不登记也能用，只是显示成英文类名。</summary>
    private static readonly IReadOnlyDictionary<string, string> ClassTitles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hole"] = "孔",
        };

    public static string ClassTitle(string commandClass)
        => ClassTitles.TryGetValue(commandClass, out var title) ? title : commandClass;

    /// <summary>
    /// 页面里的筛选：<paramref name="query"/> 按空白拆成几段，每段都要在标题、类名、用法或指令名里出现；
    /// <paramref name="commandClass"/> 为空或 <see cref="StrenuaPage.AllClasses"/> 时不按类筛，否则认类名或命令类。
    /// </summary>
    public static IEnumerable<QuickCommand> Filter(
        IEnumerable<QuickCommand> commands,
        string? query,
        string? commandClass)
    {
        var terms = (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var cls = commandClass?.Trim() ?? string.Empty;
        var anyClass = cls.Length == 0 || cls == StrenuaPage.AllClasses;
        return commands.Where(command =>
            (anyClass
             || string.Equals(command.ClassTitle, cls, StringComparison.OrdinalIgnoreCase)
             || string.Equals(command.CommandClass, cls, StringComparison.OrdinalIgnoreCase))
            && terms.All(term =>
                command.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || command.ClassTitle.Contains(term, StringComparison.OrdinalIgnoreCase)
                || command.Usage.Contains(term, StringComparison.OrdinalIgnoreCase)
                || command.CommandName.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }
}
