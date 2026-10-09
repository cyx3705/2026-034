using System.Text.Encodings.Web;
using System.Text.Json;

namespace HistoryStrenua;

/// <summary>
/// Aurora 页面协议 V1：一页 PowerSW。上面一条工具条，下面是按「类」切换的控制面板。
/// </summary>
/// <remarks>
/// <para>
/// 1.7.0 起没有指令表（用户定）：表格和宿主的命令集页面完全重叠、信息又杂，删掉；每条快捷指令直接是控制面板上的一个按钮，
/// 执行过程与结果照旧进控制台。1.13.0 起页面上唯一的表格是「技术要求」类里的模板表格（用户要的「点一下就选出技术要求」），
/// 取数走 <see cref="DataCommand"/>。
/// </para>
/// <para>
/// 工具条从左到右：浮动、占位、类、取消。「浮动」是普通按钮，动作指向 Aurora 的 <c>aurora.ui.float</c>（1.30.1 起）。
/// 占位是一段占满余宽的说明文字，浮成小窗后按住它就能拖动整窗（按钮、选择框会吃掉按下，拖不动）。
/// 「类」选择框把值发上 <see cref="ClassChannel"/>，下面的切换容器跟着它换成那一类的控制面板——将来加别的类，
/// 加一块面板即可，工具条不变。
/// </para>
/// <para>
/// 每类面板只有一行（1.12.0，用户定）：这一类的按钮全排进去，放不下由 Aurora 自己折行（折出来仍是同一行、按母行 even 分宽），
/// 浮窗拖宽拖窄时按钮跟着均匀伸缩，不再按固定 4 个一行切。两个开关——避障（默认开）、尺寸链（默认关）——1.11.0 起从「孔」面板挪出来，
/// 单独一块面板 <see cref="SwitchPanelId"/> 固定在整个窗口最下面（用户定：不连着上方）：切换容器标 Aurora 1.30.2 的 <c>fill</c>
/// 占住中间的剩余高度，开关面板就被推到底。开关拨动即生效并记到本机（<see cref="StrenuaOptions"/>），页面描述里的初值取当前值。
/// </para>
/// <para>
/// 1.14.0（用户定）：「技术要求」类下面的开关不是避障、尺寸链，换成「AI 填写技术要求」。最下面一格因此也是一个跟着「类」走的切换容器
/// <see cref="OptionSwitchId"/>：第一支（不写 case，别的类都落到它）是避障 + 尺寸链，「技术要求」那一支是 <see cref="TechSwitchPanelId"/>。
/// 避障、尺寸链仍对所有类生效（倒圆倒角类标圆心、圆弧、倒角时也要能选），只是在「技术要求」类下面不显示；「技术要求」类自己插技术要求也照旧吃避障（存的值）。
/// 「技术要求」类的按钮（「写入」= AI 填写技术要求）不单占一行，和这个开关并排在最下面一行（用户定：并排空间更大），中间那一支只有模板表格。
/// </para>
/// <para>按钮、类选项、动作声明全部从 <see cref="QuickCommands.All"/> 生成——加指令不改这里。</para>
/// </remarks>
internal static class StrenuaPage
{
    public const string PageId = "powersw";
    public const string PanelId = "quick-toolbar";
    public const string ClassSwitchId = "class-panels";

    /// <summary>窗口最下面的开关面板（1.11.0）：避障 + 尺寸链；1.14.0 起是 <see cref="OptionSwitchId"/> 的第一支（「技术要求」以外的类）。</summary>
    public const string SwitchPanelId = "option-switches";

    /// <summary>窗口最下面一格（1.14.0）：跟着「类」切换的开关区。</summary>
    public const string OptionSwitchId = "option-area";

    /// <summary>「技术要求」类最下面一行（1.14.0）：这一类的按钮（「写入」）与「AI 填写技术要求」开关并排。</summary>
    public const string TechSwitchPanelId = "tech-switches";
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

    public const string TechAiActionId = StrenuaIdentity.Domain + ".option.techai";

    /// <summary>「避障」开关管哪些指令（动作说明与指令自描述共用）。</summary>
    public const string ClearanceSummary = "开着时往图纸上加东西的指令都躲开已有的：孔标注、孔位尺寸、销孔标注、外轮廓、圆心位置挪开压线的文字，"
        + "圆弧、倒角、技术要求、轴测图、投影视图找不压的地方；关着放默认位置（默认开）";

    /// <summary>「尺寸链」开关管哪些指令。</summary>
    public const string ChainSummary = "开着时孔位尺寸、外轮廓与圆心位置改用 SW 尺寸链（坐标尺寸），每方向一组、0 点在零件左 / 上侧直边；"
        + "建图、投影视图留尺寸空间也照它估（默认关）";

    /// <summary>「AI 填写技术要求」开关管哪些地方。</summary>
    public const string TechAiSummary = "开着时模板表格点哪一份、「一键出图」插技术要求，都改由 AI 看图从全部模板里选基础再克制地增删（AI 没成退回原来那份）；"
        + "关着照旧原样插模板（默认关）";

    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>「技术要求」类的命令类名。</summary>
    public const string TechClass = "tech";

    /// <summary>「技术要求」那一支就是这张模板表格（1.13.0）。</summary>
    public const string TechTableId = "tech-templates";

    /// <summary>表格取数指令（1.13.0 起只有技术要求模板这一张表）。</summary>
    public const string DataCommand = StrenuaIdentity.Domain + ".ui.data";

    public const string TechView = "tech";

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
                            // 占住中间剩余高度，下面的开关面板被推到窗口最下面（Aurora 1.30.2；旧 Aurora 忽略，开关紧跟在面板下面）。
                            fill = true,
                            source = "{selection." + ClassChannel + ".value}",
                            children = Classes(commands)
                                .Select(group => ClassBranch(group.Class, group.Commands))
                                .ToArray(),
                        },
                        OptionArea(commands, options),
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
                id = TechApply.ActionId,
                title = "插入技术要求",
                command = TechApply.CommandName,
                args = new { name = "{name}" },
                summary = TechApply.Summary,
            })
            .Append(new
            {
                id = TechApply.DefaultActionId,
                title = "设为默认技术要求",
                command = TechApply.DefaultCommandName,
                args = new { name = "{name}" },
                summary = "把这一份设为默认（记到本机），「一键出图」插的就是它",
            })
            .Append(new
            {
                id = ClearanceActionId,
                title = "避障",
                command = ClearanceActionId,
                args = new { value = "{value}" },
                summary = ClearanceSummary,
            })
            .Append(new
            {
                id = ChainActionId,
                title = "尺寸链",
                command = ChainActionId,
                args = new { value = "{value}" },
                summary = ChainSummary,
            })
            .Append(new
            {
                id = TechAiActionId,
                title = "AI 填写技术要求",
                command = TechAiActionId,
                args = new { value = "{value}" },
                summary = TechAiSummary,
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

    /// <summary>类选项框的候选：按 <see cref="QuickCommands.ClassOrder"/>。</summary>
    internal static IReadOnlyList<string> ClassOptions(IReadOnlyList<QuickCommand> commands)
        => Classes(commands).Select(group => QuickCommands.ClassTitle(group.Class)).ToList();

    /// <summary>
    /// 各类与它的按钮：先按 <see cref="QuickCommands.ClassOrder"/>（没有按钮的只留「技术要求」——它有模板表格），再接登记表里多出来的类，类内保持登记顺序。
    /// </summary>
    private static IEnumerable<(string Class, IReadOnlyList<QuickCommand> Commands)> Classes(IReadOnlyList<QuickCommand> commands)
    {
        var grouped = commands.GroupBy(command => command.CommandClass, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<QuickCommand>)group.ToList(), StringComparer.Ordinal);
        foreach (var commandClass in QuickCommands.ClassOrder)
        {
            if (grouped.TryGetValue(commandClass, out var members))
                yield return (commandClass, members);
            else if (commandClass == TechClass)
                yield return (commandClass, []);
        }

        foreach (var (commandClass, members) in grouped)
            if (!QuickCommands.ClassOrder.Contains(commandClass))
                yield return (commandClass, members);
    }

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

    /// <summary>
    /// 窗口最下面一格（1.14.0）：跟着「类」切换。第一支不写 case——「技术要求」以外的类都落到它（Aurora：没有一支匹配时显示第一支）——
    /// 是避障 + 尺寸链；「技术要求」类是这一类的按钮（「写入」）与「AI 填写技术要求」开关并排一行。
    /// </summary>
    private static object OptionArea(IReadOnlyList<QuickCommand> commands, StrenuaOptions options) => new
    {
        type = "switch",
        id = OptionSwitchId,
        source = "{selection." + ClassChannel + ".value}",
        children = new object[]
        {
            new
            {
                type = "panel",
                id = SwitchPanelId,
                text = "开关",
                rows = new object[]
                {
                    new
                    {
                        mode = "even",
                        widgets = new object[]
                        {
                            Switch("clearance", "避障", options.Clearance, ClearanceActionId),
                            Switch("chain", "尺寸链", options.Chain, ChainActionId),
                        },
                    },
                },
            },
            new
            {
                type = "panel",
                id = TechSwitchPanelId,
                @case = QuickCommands.ClassTitle(TechClass),
                text = "开关",
                rows = new object[]
                {
                    new
                    {
                        mode = "even",
                        widgets = commands.Where(command => command.CommandClass == TechClass)
                            .Select(command => (object)new { kind = "button", action = command.ActionId, text = command.Title })
                            .Append(Switch("techai", "AI 填写技术要求", options.TechAi, TechAiActionId))
                            .ToArray(),
                    },
                },
            },
        },
    };

    /// <summary>
    /// 切换容器的一支：一类的控制面板。「技术要求」类整支是模板表格，点「技术要求」列的名字就插那一份；
    /// 它的按钮（1.14.0「写入」）不在这里，在最下面一行和「AI 填写技术要求」开关并排（<see cref="OptionArea"/>）。
    /// </summary>
    private static object ClassBranch(string commandClass, IReadOnlyList<QuickCommand> commands)
        => commandClass == TechClass ? TechTable(QuickCommands.ClassTitle(commandClass)) : ClassPanel(commandClass, commands);

    /// <summary>一类的控制面板：只有一行（1.12.0）——按钮按登记顺序排，放不下由 Aurora 折行。</summary>
    private static object ClassPanel(string commandClass, IReadOnlyList<QuickCommand> commands)
    {
        var widgets = commands.Select(command => (object)new { kind = "button", action = command.ActionId, text = command.Title }).ToArray();
        var title = QuickCommands.ClassTitle(commandClass);
        return new { type = "panel", id = ClassPanelId(commandClass), @case = title, text = title, rows = new object[] { new { mode = "even", widgets } } };
    }

    /// <summary>技术要求模板表格（1.13.0）：一份「通用技术要求」一行；「技术要求」列是按钮，点了插那一份（换掉图上原有的）；「设置」列设默认。</summary>
    private static object TechTable(string @case) => new
    {
        type = "table",
        id = TechTableId,
        @case,
        dataSource = new { command = DataCommand, args = new { view = TechView } },
        columns = new object[]
        {
            new { key = "name", title = "技术要求", width = "110", cellAction = TechApply.ActionId, cellStyle = "button" },
            // 1.13.0（用户定）：「条数」列换成「设置」——默认那份显示「默认」，其余「设为默认」，点了就改默认（一键出图插它）。
            new { key = "setting", title = "设置", width = "50", cellAction = TechApply.DefaultActionId, cellStyle = "button" },
            new { key = "content", title = "内容", width = "*" },
        },
    };

    private static object Switch(string id, string label, bool value, string action)
        => new { kind = "switch", id, label, value = value ? "true" : "false", action };
}
