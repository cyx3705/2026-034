using System.Text.Encodings.Web;
using System.Text.Json;

namespace HistoryStrenua;

/// <summary>
/// Aurora 页面协议 V1：一页 PowerSW。上面一块控制面板放快捷指令按钮，下面一张状态表。
/// 按钮、动作声明、表格行全部从 <see cref="QuickCommands.All"/> 生成——加指令不改这里。
/// </summary>
internal static class StrenuaPage
{
    public const string PageId = "powersw";
    public const string PanelId = "quick-actions";
    public const string StatusTableId = "quick-commands";
    public const string CancelActionId = StrenuaIdentity.Domain + ".quick.cancel";

    /// <summary>一行最多几个按钮。再多就折到下一声明行，免得按钮被挤成一条条窄缝。</summary>
    internal const int ButtonsPerRow = 4;

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
                            text = "SolidWorks 快捷指令",
                            rows = ButtonRows(commands),
                        },
                        new
                        {
                            type = "text",
                            style = "caption",
                            text = "作用于你正在用的那个 SolidWorks；不会替你启动它。执行过程与结果同时写进控制台。",
                        },
                        new
                        {
                            type = "table",
                            id = StatusTableId,
                            dataSource = new
                            {
                                command = StrenuaIdentity.Domain + ".ui.data",
                                args = new { view = "commands" },
                            },
                            columns = new object[]
                            {
                                new { key = "title", title = "指令", width = "90" },
                                new { key = "usage", title = "用法", width = "2*" },
                                new { key = "state", title = "状态", width = "100" },
                                new { key = "result", title = "上次结果", width = "2*" },
                                new { key = "time", title = "时间", width = "70" },
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
                id = CancelActionId,
                title = "取消",
                command = StrenuaIdentity.Domain + ".quick.cancel",
                summary = "取消正在执行的 PowerSW 快捷指令（包括正在等你点视图的那一条）",
            })
            .ToArray(),
    }, Options);

    /// <summary>状态表的行：一条快捷指令一行。</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Rows(QuickCommandRunner runner)
        => QuickCommands.All
            .Select(command =>
            {
                var status = runner.StatusOf(command.Key);
                return (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                {
                    ["id"] = command.Key,
                    ["title"] = command.Title,
                    ["usage"] = command.Usage,
                    ["state"] = status.State,
                    ["result"] = status.Result,
                    ["time"] = QuickCommandRunner.FormatTime(status.FinishedAt),
                };
            })
            .ToList();

    /// <summary>按钮按登记顺序每 <see cref="ButtonsPerRow"/> 个一行，「取消」跟在最后一行末尾。</summary>
    private static object[] ButtonRows(IReadOnlyList<QuickCommand> commands)
    {
        var buttons = commands
            .Select(command => new { kind = "button", action = command.ActionId, text = command.Title })
            .Append(new { kind = "button", action = CancelActionId, text = "取消" })
            .ToArray();
        return buttons
            .Chunk(ButtonsPerRow)
            .Select(chunk => (object)new { mode = "even", widgets = chunk })
            .ToArray();
    }
}
