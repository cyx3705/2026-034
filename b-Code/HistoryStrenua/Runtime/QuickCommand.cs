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
    public string ClassTitle => QuickClasses.Title(PageClass);
}

