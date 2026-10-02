using System.Text.Encodings.Web;
using System.Text.Json;

namespace HistoryStrenua;

/// <summary>
/// Aurora 页面协议 V1：一页 PowerSW。上面一条工具条，下面一张指令表。
/// </summary>
/// <remarks>
/// <para>
/// 1.5.0 起没有按钮面板：按钮与表格是同一份清单，信息重复。指令表第一列「指令」就是入口——
/// 点名称就执行（Aurora 可点击单元格，行内小按钮）。
/// </para>
/// <para>
/// 工具条从左到右：浮动、搜索、类、取消。「浮动」是普通按钮，动作指向 Aurora 的
/// <c>aurora.ui.float</c>（1.30.1 起）：把整页浮成置顶小窗，操作 SolidWorks 时也点得到；
/// 已浮出时再点就还原。搜索框与类选项框把值发上选择通道，指令表的取数参数引用这两个通道，值一变就重取。
/// </para>
/// <para>表格行、类选项、动作声明全部从 <see cref="QuickCommands.All"/> 生成——加指令不改这里。</para>
/// </remarks>
internal static class StrenuaPage
{
    public const string PageId = "powersw";
    public const string PanelId = "quick-toolbar";
    public const string StatusTableId = "quick-commands";
    public const string CancelActionId = StrenuaIdentity.Domain + ".quick.cancel";

    /// <summary>点指令表「指令」列时执行的动作：按被点那一行的 id 跑那一条快捷指令。</summary>
    public const string RunActionId = StrenuaIdentity.Domain + ".quick.run";

    /// <summary>工具条「浮动」按钮的动作：调 Aurora 把本页浮出 / 还原。</summary>
    public const string FloatActionId = StrenuaIdentity.Domain + ".page.float";

    /// <summary>Aurora 的页面浮动指令（切换）。</summary>
    public const string FloatCommand = "aurora.ui.float";

    public const string SearchChannel = StrenuaIdentity.Domain + ".search";
    public const string ClassChannel = StrenuaIdentity.Domain + ".class";

    /// <summary>类选项框的第一项：不按类筛。</summary>
    public const string AllClasses = "全部";

    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Describe() => Describe(QuickCommands.All);

    internal static string Describe(IReadOnlyList<QuickCommand> commands) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        owner = StrenuaIdentity.PageOwner,
        pages = new object[]
        {
            new
            {
                id = PageId,
                title = StrenuaIdentity.PageTitle,
                scene = StrenuaIdentity.PageOwner,
                placement = new { side = "center", visible = true, singleton = true },
                content = new
                {
                    type = "stack",
                    gap = "tight",
                    children = new object[]
                    {
                        new
                        {
                            type = "panel",
                            id = PanelId,
                            text = "PowerSW 工具条",
                            rows = new object[] { Toolbar(commands) },
                        },
                        new
                        {
                            type = "table",
                            id = StatusTableId,
                            dataSource = new
                            {
                                command = StrenuaIdentity.Domain + ".ui.data",
                                args = new
                                {
                                    view = "commands",
                                    query = "{selection." + SearchChannel + ".value}",
                                    @class = "{selection." + ClassChannel + ".value}",
                                },
                            },
                            columns = new object[]
                            {
                                new { key = "title", title = "指令", width = "90", cellAction = RunActionId, cellStyle = "button" },
                                new { key = "class", title = "类", width = "40" },
                                new { key = "usage", title = "用法", width = "2*" },
                                new { key = "state", title = "状态", width = "90" },
                                new { key = "result", title = "上次结果", width = "2*" },
                                new { key = "time", title = "时间", width = "60" },
                            },
                        },
                    },
                },
            },
        },
    }, Options);

    public static string Actions() => Actions(QuickCommands.All);

    internal static string Actions(IReadOnlyList<QuickCommand> commands) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        owner = StrenuaIdentity.PageOwner,
        actions = commands
            .Select(command => (object)new
            {
                id = command.ActionId,
                title = command.Title,
                command = command.CommandName,
                summary = command.Usage,
            })
            .Append(new
            {
                id = RunActionId,
                title = "执行",
                command = StrenuaIdentity.Domain + ".quick.run",
                args = new { key = "{id}" },
                summary = "执行这一行的快捷指令（执行过程与结果同时写进控制台）",
            })
            .Append(new
            {
                id = FloatActionId,
                title = "浮动",
                command = FloatCommand,
                args = new { name = PageId },
                summary = "把 PowerSW 浮成置顶小窗（操作 SolidWorks 时也点得到），拖空白处移动；已浮出时再点就还原",
            })
            .Append(new
            {
                id = CancelActionId,
                title = "取消",
                command = StrenuaIdentity.Domain + ".quick.cancel",
                summary = "取消正在执行的 PowerSW 快捷指令（包括正在等你点视图的那一条）",
            })
            .ToArray(),
    }, Options);

    /// <summary>类选项框的候选：「全部」在前，其余按登记顺序去重。</summary>
    internal static IReadOnlyList<string> ClassOptions(IReadOnlyList<QuickCommand> commands)
        => commands
            .Select(command => command.ClassTitle)
            .Distinct(StringComparer.Ordinal)
            .Prepend(AllClasses)
            .ToList();

    /// <summary>指令表的行：一条快捷指令一行，按搜索词与类筛过。</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Rows(
        QuickCommandRunner runner,
        string? query = null,
        string? commandClass = null)
        => QuickCommands.Filter(QuickCommands.All, query, commandClass)
            .Select(command =>
            {
                var status = runner.StatusOf(command.Key);
                return (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                {
                    ["id"] = command.Key,
                    ["title"] = command.Title,
                    ["class"] = command.ClassTitle,
                    ["usage"] = command.Usage,
                    ["state"] = status.State,
                    ["result"] = status.Result,
                    ["time"] = QuickCommandRunner.FormatTime(status.FinishedAt),
                };
            })
            .ToList();

    /// <summary>工具条：浮动 | 搜索（占余宽） | 类 | 取消。</summary>
    private static object Toolbar(IReadOnlyList<QuickCommand> commands) => new
    {
        widgets = new object[]
        {
            new { kind = "button", action = FloatActionId, text = "浮动" },
            new { kind = "textbox", id = "search", label = "搜索", channel = SearchChannel, flex = true },
            new { kind = "textbox", id = "class", label = "类", mode = "select", channel = ClassChannel, options = ClassOptions(commands), minWidth = 60 },
            new { kind = "button", action = CancelActionId, text = "取消" },
        },
    };
}
