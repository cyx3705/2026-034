using System.Text.Encodings.Web;
using System.Text.Json;

namespace HistoryStrenua;

/// <summary>
/// Aurora 页面协议 V1：一页 PowerSW。上面一条工具条，下面是按「类」切换的控制面板。
/// </summary>
/// <remarks>
/// <para>
/// 1.7.0 起没有指令表（用户定）：表格和宿主的命令集页面完全重叠、信息又杂，删掉；每条快捷指令直接是控制面板上的一个按钮，
/// 执行过程与结果照旧进控制台。
/// </para>
/// <para>
/// 工具条从左到右：浮动、占位、类、取消。「浮动」是普通按钮，动作指向 Aurora 的 <c>aurora.ui.float</c>（1.30.1 起）。
/// 占位是一段占满余宽的说明文字，浮成小窗后按住它就能拖动整窗（按钮、选择框会吃掉按下，拖不动）。
/// 「类」选择框把值发上 <see cref="ClassChannel"/>，下面的切换容器跟着它换成那一类的控制面板——将来加别的类，
/// 加一块面板即可，工具条不变。
/// </para>
/// <para>
/// 「孔」面板第一行是孔类指令的按钮，第二行是两个开关：避障（默认开）、尺寸链（默认关）。开关拨动即生效并记到本机
/// （<see cref="StrenuaOptions"/>），页面描述里的初值取当前值。
/// </para>
/// <para>按钮、类选项、动作声明全部从 <see cref="QuickCommands.All"/> 生成——加指令不改这里。</para>
/// </remarks>
internal static class StrenuaPage
{
    public const string PageId = "powersw";
    public const string PanelId = "quick-toolbar";
    public const string ClassSwitchId = "class-panels";
    public const string CancelActionId = StrenuaIdentity.Domain + ".quick.cancel";

    /// <summary>工具条「浮动」按钮的动作：调 Aurora 把本页浮出 / 还原。</summary>
    public const string FloatActionId = StrenuaIdentity.Domain + ".page.float";

    /// <summary>Aurora 的页面浮动指令（切换）。</summary>
    public const string FloatCommand = "aurora.ui.float";

    public const string ClassChannel = StrenuaIdentity.Domain + ".class";

    /// <summary>工具条中间的占位文字：占满余宽，浮出后按住它拖动整窗。</summary>
    public const string DragHint = "PowerSW · 按住此处拖动";

    /// <summary>开关的动作 id 与指令名相同。</summary>
    public const string ClearanceActionId = StrenuaIdentity.Domain + ".option.clearance";

    public const string ChainActionId = StrenuaIdentity.Domain + ".option.chain";

    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>一类控制面板的 id。</summary>
    public static string ClassPanelId(string commandClass) => "class-" + commandClass;

    public static string Describe(StrenuaOptions options) => Describe(QuickCommands.All, options);

    internal static string Describe(IReadOnlyList<QuickCommand> commands, StrenuaOptions options) => JsonSerializer.Serialize(new
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
                            type = "switch",
                            id = ClassSwitchId,
                            source = "{selection." + ClassChannel + ".value}",
                            children = Classes(commands)
                                .Select(group => ClassPanel(group.Key, group.ToList(), options))
                                .ToArray(),
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
                id = ClearanceActionId,
                title = "避障",
                command = ClearanceActionId,
                args = new { value = "{value}" },
                summary = "开着时孔标注、孔位尺寸加完后把压在别的孔相关注解线条上的文字挪开（默认开）",
            })
            .Append(new
            {
                id = ChainActionId,
                title = "尺寸链",
                command = ChainActionId,
                args = new { value = "{value}" },
                summary = "开着时孔位尺寸每个方向全部孔排成一条链（基准→第一个→下一个…）逐段标，不分种、不用阵列写法（默认关）",
            })
            .Append(new
            {
                id = FloatActionId,
                title = "浮动",
                command = FloatCommand,
                args = new { name = PageId },
                summary = "把 PowerSW 浮成置顶小窗（操作 SolidWorks 时也点得到），按住工具条中间的文字拖动；已浮出时再点就还原",
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

    /// <summary>类选项框的候选：按登记顺序去重。</summary>
    internal static IReadOnlyList<string> ClassOptions(IReadOnlyList<QuickCommand> commands)
        => Classes(commands).Select(group => group.First().ClassTitle).ToList();

    /// <summary>按命令类分组，保持登记顺序。</summary>
    private static IEnumerable<IGrouping<string, QuickCommand>> Classes(IReadOnlyList<QuickCommand> commands)
        => commands.GroupBy(command => command.CommandClass, StringComparer.Ordinal);

    /// <summary>工具条：浮动 | 占位（占余宽，浮出后拖这里） | 类 | 取消。</summary>
    private static object Toolbar(IReadOnlyList<QuickCommand> commands)
    {
        var classes = ClassOptions(commands);
        return new
        {
            widgets = new object[]
            {
                new { kind = "button", action = FloatActionId, text = "浮动" },
                new { kind = "text", text = DragHint, flex = true },
                new { kind = "textbox", id = "class", label = "类", mode = "select", channel = ClassChannel, options = classes, value = classes.FirstOrDefault() ?? string.Empty, minWidth = 60 },
                new { kind = "button", action = CancelActionId, text = "取消" },
            },
        };
    }

    /// <summary>一类的控制面板：一行按钮；孔类再加一行开关。</summary>
    private static object ClassPanel(string commandClass, IReadOnlyList<QuickCommand> commands, StrenuaOptions options)
    {
        var rows = new List<object>
        {
            new
            {
                mode = "even",
                widgets = commands.Select(command => (object)new { kind = "button", action = command.ActionId, text = command.Title }).ToArray(),
            },
        };
        if (commandClass == "hole")
        {
            rows.Add(new
            {
                mode = "even",
                widgets = new object[]
                {
                    Switch("clearance", "避障", options.Clearance, ClearanceActionId),
                    Switch("chain", "尺寸链", options.Chain, ChainActionId),
                },
            });
        }

        return new
        {
            type = "panel",
            id = ClassPanelId(commandClass),
            @case = commands[0].ClassTitle,
            text = commands[0].ClassTitle,
            rows,
        };
    }

    private static object Switch(string id, string label, bool value, string action)
        => new { kind = "switch", id, label, value = value ? "true" : "false", action };
}
