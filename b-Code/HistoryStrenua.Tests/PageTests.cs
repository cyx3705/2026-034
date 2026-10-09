using System.Text.Json;
using HistoryStrenua;
using HistoryVulcan.Core.Commands;

/// <summary>离线测试：页面、登记表、开关（Page/）。</summary>
internal static partial class Tests
{
    static void TestCommandRegistration()
    {
        var registry = Registry();
        string[] expected =
        [
            "strenua.hole.callout",
            "strenua.hole.centermark",
            "strenua.hole.position",
            "strenua.hole.dowel",
            "strenua.hole.flow",
            "strenua.hole.dowelfit",
            "strenua.hole.outline",
            "strenua.onekey.drawing",
            "strenua.drawing.basic",
            "strenua.drawing.create",
            "strenua.drawing.project",
            "strenua.drawing.iso",
            "strenua.drawing.arrange",
            "strenua.drawing.symmetry",
            "strenua.fillet.all",
            "strenua.fillet.arccenterall",
            "strenua.fillet.arcall",
            "strenua.fillet.chamferall",
            "strenua.fillet.arccenter",
            "strenua.fillet.arc",
            "strenua.fillet.chamfer",
            "strenua.tech.apply",
            "strenua.tech.default",
            "strenua.tech.ai",
            "strenua.check.dimension",
            "strenua.check.decimal",
            "strenua.check.dangling",
            "strenua.check.overlap",
            "strenua.check.snapshot",
            "strenua.quick.list",
            "strenua.quick.run",
            "strenua.quick.cancel",
            "strenua.ui.describe",
            "strenua.ui.actions",
            "strenua.ui.data",
            "strenua.option.clearance",
            "strenua.option.chain",
            "strenua.option.techai",
        ];
        foreach (var name in expected)
        {
            True(registry.TryGet(name, out var descriptor), $"未注册 {name}");
            Equal("strenua", descriptor!.Domain!);
        }

        Equal(expected.Length, registry.All.Count(d => d.Name.StartsWith("strenua.", StringComparison.Ordinal)));

        True(registry.TryGet("strenua.hole.callout", out var hole), "缺少孔标注");
        True(!hole!.Readonly, "孔标注会改工程图，不是只读");
        True(hole.HiddenReason is null, "快捷指令要能在控制台直接敲");
        True(registry.TryGet("strenua.hole.centermark", out var centerMark) && !centerMark!.Readonly, "中心符号线会改工程图，不是只读");
        True(registry.TryGet("strenua.quick.list", out var list) && list!.Readonly, "列表是只读的");
        True(!registry.TryGet("strenua.drawing.auto", out _), "1.16.0：出图类的一键出图拆成基础出图与一键类的一键出图");
        foreach (var name in new[] { "strenua.drawing.create", "strenua.drawing.basic", "strenua.onekey.drawing", "strenua.fillet.all", "strenua.drawing.project", "strenua.drawing.iso", "strenua.tech.apply",
                     "strenua.drawing.arrange", "strenua.fillet.arcall", "strenua.fillet.chamferall", "strenua.fillet.arc", "strenua.fillet.chamfer",
                     "strenua.fillet.arccenter", "strenua.fillet.arccenterall",
                     "strenua.drawing.symmetry", "strenua.check.snapshot" })
            True(registry.TryGet(name, out var drawing) && !drawing!.Readonly && drawing.HiddenReason is null, $"{name} 要能在控制台直接敲（建图、加尺寸、写图片都不是只读）");
        True(registry.TryGet("strenua.quick.run", out var run) && !run!.Readonly, "按 key 执行会改工程图，不是只读");
        var keys = run!.Parameters!.Single(p => p.Name == "key").AllowedValues!;
        True(QuickCommands.All.All(command => keys.Contains(command.Key)), "quick.run 的 key 候选应覆盖全部快捷指令");
        // 1.13.0：旧「技术要求」按钮连同 strenua.tech.note 删掉（用户定），单独放技术要求只走模板表格。
        True(!registry.TryGet("strenua.tech.note", out _), "旧的技术要求指令应已删除");
        // 1.13.0：技术要求模板表格取数（界面内部）；点表格插模板的指令要能在控制台敲。
        True(registry.TryGet("strenua.tech.apply", out var apply) && !apply!.Readonly && apply.HiddenReason is null, "tech.apply 改工程图、要能在控制台敲");
        True(apply!.Parameters!.Single(p => p.Name == "name").Required, "tech.apply 必须给模板名");
        True(registry.TryGet("strenua.tech.default", out var techDefault) && !techDefault!.Readonly && techDefault.HiddenReason is null
            && !techDefault.Parameters!.Single(p => p.Name == "name").Required, "tech.default 改设置、要能在控制台敲，省略 name 报当前值");
        // 1.14.0：AI 填写技术要求是一个普通按钮（改工程图、要能在控制台敲）。
        True(registry.TryGet("strenua.tech.ai", out var techAi) && !techAi!.Readonly && techAi.HiddenReason is null, "tech.ai 改工程图、要能在控制台敲");
        foreach (var method in new[] { "clearance", "chain", "techai" })
            True(registry.TryGet("strenua.option." + method, out var option) && option!.HiddenReason is null && !option.Readonly,
                $"strenua.option.{method} 改设置，要能在控制台敲");
        foreach (var method in new[] { "describe", "actions", "data" })
        {
            True(registry.TryGet("strenua.ui." + method, out var ui), $"缺少 strenua.ui.{method}");
            True(ui!.HiddenReason is not null, $"strenua.ui.{method} 是界面内部协议，必须 HiddenReason");
        }
    }

    static void TestPageOwner()
    {
        using var description = JsonDocument.Parse(StrenuaPage.Describe(new StrenuaOptions()));
        var root = description.RootElement;
        // Aurora 的判据：owner = "History" + 首字母大写的指令域；对不上整页被静默拒收。
        var expected = "History" + char.ToUpperInvariant(StrenuaIdentity.Domain[0]) + StrenuaIdentity.Domain[1..];
        Equal(expected, root.GetProperty("owner").GetString()!);
        Equal(1, root.GetProperty("schemaVersion").GetInt32());
        var pages = root.GetProperty("pages");
        Equal(1, pages.GetArrayLength());
        Equal(StrenuaPage.PageId, pages[0].GetProperty("id").GetString()!);
        Equal(expected, pages[0].GetProperty("scene").GetString()!);
        Equal("PowerSW", pages[0].GetProperty("title").GetString()!);

        using var actions = JsonDocument.Parse(StrenuaPage.Actions());
        Equal(expected, actions.RootElement.GetProperty("owner").GetString()!);
    }

    static void TestPageWiring()
    {
        var registry = Registry();
        using var actions = JsonDocument.Parse(StrenuaPage.Actions());
        var declared = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var action in actions.RootElement.GetProperty("actions").EnumerateArray())
        {
            var id = action.GetProperty("id").GetString()!;
            var command = action.GetProperty("command").GetString()!;
            True(declared.TryAdd(id, command), $"动作 id 重复：{id}");
            // aurora.* 由 Aurora 注册，不在本模块的注册表里。
            if (!command.StartsWith("aurora.", StringComparison.Ordinal))
                True(registry.TryGet(command, out _), $"动作 {id} 指向未注册的指令 {command}");
        }

        Equal("aurora.ui.float", declared[StrenuaPage.FloatActionId]);
        Equal("strenua.option.clearance", declared[StrenuaPage.ClearanceActionId]);
        Equal("strenua.option.chain", declared[StrenuaPage.ChainActionId]);
        Equal("strenua.option.techai", declared[StrenuaPage.TechAiActionId]);

        using var description = JsonDocument.Parse(StrenuaPage.Describe(new StrenuaOptions()));
        var bound = Descendants(description.RootElement)
            .Where(node => node.TryGetProperty("kind", out var kind) && kind.GetString() is "button" or "switch")
            .Select(node => node.GetProperty("action").GetString()!)
            .ToList();
        foreach (var command in QuickCommands.All)
            True(bound.Contains(command.ActionId), $"指令「{command.Title}」应是页面上的一个按钮");
        foreach (var action in bound)
            True(declared.ContainsKey(action), $"页面绑定了未声明的动作 {action}");

        // 1.7.0 起没有指令表；1.13.0 唯一的表格是「技术要求」类里的模板表格：取数指令已注册，「技术要求」列点了按那一行的名字插模板。
        var tables = Descendants(description.RootElement).Where(node => node.TryGetProperty("type", out var type) && type.GetString() == "table").ToList();
        Equal(1, tables.Count);
        Equal(StrenuaPage.TechTableId, tables[0].GetProperty("id").GetString()!);
        var source = tables[0].GetProperty("dataSource");
        True(registry.TryGet(source.GetProperty("command").GetString()!, out _), "表格取数指令要已注册");
        Equal("tech", source.GetProperty("args").GetProperty("view").GetString()!);
        var columns = tables[0].GetProperty("columns").EnumerateArray().ToList();
        // 1.13.0（用户定）：「条数」列换成「设置」按钮列（设为默认）。
        Equal("name,setting,content", string.Join(",", columns.Select(c => c.GetProperty("key").GetString())));
        Equal("技术要求,设置,内容", string.Join(",", columns.Select(c => c.GetProperty("title").GetString())));
        Equal(TechApply.ActionId, columns[0].GetProperty("cellAction").GetString()!);
        Equal(TechApply.DefaultActionId, columns[1].GetProperty("cellAction").GetString()!);
        True(columns.Take(2).All(c => c.GetProperty("cellStyle").GetString() == "button"), "前两列是按钮");
        Equal(TechApply.DefaultCommandName, declared[TechApply.DefaultActionId]);
        var defaultAction = actions.RootElement.GetProperty("actions").EnumerateArray().Single(a => a.GetProperty("id").GetString() == TechApply.DefaultActionId);
        Equal("{name}", defaultAction.GetProperty("args").GetProperty("name").GetString()!);
        Equal(TechApply.CommandName, declared[TechApply.ActionId]);
        var applyAction = actions.RootElement.GetProperty("actions").EnumerateArray().Single(a => a.GetProperty("id").GetString() == TechApply.ActionId);
        Equal("{name}", applyAction.GetProperty("args").GetProperty("name").GetString()!);
    }

    static void TestListRows()
    {
        var rows = HistoryStrenuaModule.ListRows(new QuickCommandRunner(new StrenuaOptions()));
        Equal(QuickCommands.All.Count, rows.Count);
        var hole = rows.Single(row => row["id"] == "hole-callout");
        Equal("孔标注", hole["title"]);
        Equal("就绪", hole["state"]);
        Equal(string.Empty, hole["result"]);
        foreach (var row in rows)
            True(new[] { "id", "title", "class", "usage", "state", "result", "time" }.All(row.ContainsKey), "列表行缺列");
    }

    static void TestSwitchPanel()
    {
        var options = new StrenuaOptions();
        options.Set(StrenuaOption.Clearance, false);
        options.Set(StrenuaOption.Chain, true);
        using var description = JsonDocument.Parse(StrenuaPage.Describe(options));
        // 1.11.0（用户定）：开关不在任何类面板里，单独一块面板排在页面最后；中间的切换容器 fill，把它推到窗口最下面。
        var children = description.RootElement.GetProperty("pages")[0].GetProperty("content").GetProperty("children").EnumerateArray().ToList();
        Equal(3, children.Count);
        Equal(StrenuaPage.PanelId, children[0].GetProperty("id").GetString()!);
        Equal(StrenuaPage.ClassSwitchId, children[1].GetProperty("id").GetString()!);
        True(children[1].GetProperty("fill").GetBoolean(), "切换容器应占住剩余高度");
        True(!children[0].TryGetProperty("fill", out _) && !children[2].TryGetProperty("fill", out _), "只有中间一格 fill");
        // 1.14.0（用户定）：最下面一格跟着「类」切换——「技术要求」类下面是「AI 填写技术要求」，别的类（第一支，不写 case）照旧避障 + 尺寸链。
        Equal(StrenuaPage.OptionSwitchId, children[2].GetProperty("id").GetString()!);
        Equal("switch", children[2].GetProperty("type").GetString()!);
        Equal("{selection." + StrenuaPage.ClassChannel + ".value}", children[2].GetProperty("source").GetString()!);
        var areas = children[2].GetProperty("children").EnumerateArray().ToList();
        Equal(2, areas.Count);
        Equal(StrenuaPage.SwitchPanelId, areas[0].GetProperty("id").GetString()!);
        True(!areas[0].TryGetProperty("case", out _), "第一支不写 case：技术要求以外的类都落到它");
        Equal(StrenuaPage.TechSwitchPanelId, areas[1].GetProperty("id").GetString()!);
        Equal("要求", areas[1].GetProperty("case").GetString()!);
        // 用户定（第三轮）：「写入」按钮与 AI 开关并排一行，空间更大。
        var tech = areas[1].GetProperty("rows")[0].GetProperty("widgets").EnumerateArray().ToList();
        Equal("button,switch", string.Join(",", tech.Select(w => w.GetProperty("kind").GetString())));
        Equal("写入", tech[0].GetProperty("text").GetString()!);
        Equal(TechAi.Command.ActionId, tech[0].GetProperty("action").GetString()!);
        Equal("AI 填写技术要求", tech[1].GetProperty("label").GetString()!);
        Equal(StrenuaPage.TechAiActionId, tech[1].GetProperty("action").GetString()!);
        Equal("false", tech[1].GetProperty("value").GetString()!);
        options.Set(StrenuaOption.TechAi, true);
        using (var on = JsonDocument.Parse(StrenuaPage.Describe(options)))
        {
            var techSwitch = Descendants(on.RootElement).Single(node => node.TryGetProperty("action", out var a) && a.GetString() == StrenuaPage.TechAiActionId);
            Equal("true", techSwitch.GetProperty("value").GetString()!);
        }

        var rows = areas[0].GetProperty("rows").EnumerateArray().ToList();
        Equal(1, rows.Count);
        var switches = rows[0].GetProperty("widgets").EnumerateArray().ToList();
        Equal("避障,尺寸链", string.Join(",", switches.Select(w => w.GetProperty("label").GetString())));
        True(switches.All(w => w.GetProperty("kind").GetString() == "switch"), "开关面板应全是开关");
        Equal(StrenuaPage.ClearanceActionId, switches[0].GetProperty("action").GetString()!);
        Equal(StrenuaPage.ChainActionId, switches[1].GetProperty("action").GetString()!);
        // 初值取当前设置。
        Equal("false", switches[0].GetProperty("value").GetString()!);
        Equal("true", switches[1].GetProperty("value").GetString()!);
    }

    static void TestToolbar()
    {
        using var description = JsonDocument.Parse(StrenuaPage.Describe(new StrenuaOptions()));
        var panel = Descendants(description.RootElement)
            .First(node => node.TryGetProperty("id", out var id) && id.GetString() == StrenuaPage.PanelId);
        var rows = panel.GetProperty("rows").EnumerateArray().ToList();
        Equal(1, rows.Count);
        var widgets = rows[0].GetProperty("widgets").EnumerateArray().ToList();
        Equal(4, widgets.Count);
        Equal(StrenuaPage.FloatActionId, widgets[0].GetProperty("action").GetString()!);
        // 中间是占余宽的文字：浮出后按住它拖动整窗。
        Equal("text", widgets[1].GetProperty("kind").GetString()!);
        True(widgets[1].GetProperty("flex").GetBoolean(), "占位文字应占余宽");
        Equal("select", widgets[2].GetProperty("mode").GetString()!);
        Equal(StrenuaPage.ClassChannel, widgets[2].GetProperty("channel").GetString()!);
        Equal("一键", widgets[2].GetProperty("options")[0].GetString()!);
        Equal("一键", widgets[2].GetProperty("value").GetString()!);
        Equal(StrenuaPage.CancelActionId, widgets[3].GetProperty("action").GetString()!);
    }

    static void TestClassPanels()
    {
        using var description = JsonDocument.Parse(StrenuaPage.Describe(new StrenuaOptions()));
        var container = Descendants(description.RootElement)
            .First(node => node.TryGetProperty("id", out var id) && id.GetString() == StrenuaPage.ClassSwitchId);
        Equal("switch", container.GetProperty("type").GetString()!);
        Equal("{selection." + StrenuaPage.ClassChannel + ".value}", container.GetProperty("source").GetString()!);
        var branches = container.GetProperty("children").EnumerateArray().ToList();
        // 每个类选项正好一块面板，case 就是选项里的字。
        Equal(string.Join(",", StrenuaPage.ClassOptions(QuickCommands.All)),
            string.Join(",", branches.Select(branch => branch.GetProperty("case").GetString())));

        // 1.12.0（用户定）：每类面板只有一行，这一类的按钮全排进去；放不下由 Aurora 折行，浮窗拖宽拖窄时均匀伸缩。
        var panels = branches.SelectMany(Descendants).Where(node => node.TryGetProperty("type", out var type) && type.GetString() == "panel").ToList();
        string Row(string commandClass)
        {
            var panel = panels.Single(branch => branch.GetProperty("id").GetString() == StrenuaPage.ClassPanelId(commandClass));
            Equal(1, panel.GetProperty("rows").GetArrayLength());
            var row = panel.GetProperty("rows")[0];
            Equal("even", row.GetProperty("mode").GetString()!);
            return string.Join(",", row.GetProperty("widgets").EnumerateArray().Select(w => w.GetProperty("text").GetString()));
        }

        foreach (var group in QuickCommands.All.GroupBy(c => c.CommandClass).Where(g => g.Key != "tech"))
            Equal(string.Join(",", group.Select(c => c.Title)), Row(group.Key));
        Equal("孔标注全流程,销钉符号,中心符号线,孔位尺寸,孔标注,销孔标注,外轮廓", Row("hole"));
        // 1.16.0（用户定）：出图类改名「基础」、一键出图改成「基础出图」只做这一类；倒圆倒角改名「倒圆」并加「全图倒圆倒角」；新类「一键」只有一键出图。
        Equal("基础出图,新建工程图,投影视图,轴测图,排版,对称轴", Row("drawing"));
        Equal("全图倒圆倒角,全图圆心,全图圆弧,全图倒角,圆心位置,圆弧标注,倒角标注", Row("fillet"));
        Equal("一键出图", Row("onekey"));
        // 1.13.0（用户定）：旧「技术要求」按钮删掉，「技术要求」类整支就是模板表格；1.14.0 的「写入」按钮不在这里，在最下面一行和 AI 开关并排（见 switches 那组）。
        var tech = branches.Single(branch => branch.GetProperty("case").GetString() == "要求");
        Equal("table", tech.GetProperty("type").GetString()!);
        Equal(StrenuaPage.TechTableId, tech.GetProperty("id").GetString()!);
        True(!panels.Any(panel => panel.GetProperty("id").GetString() == StrenuaPage.ClassPanelId("tech")), "技术要求类中间没有按钮面板");
        True(QuickCommands.All.All(command => command.Title != "技术要求"), "技术要求按钮已删");
        // 检查类 1.12.0 加「悬空标注」「注解重叠」。
        // 1.16.0：加「两位小数」。
        Equal("未标尺寸,两位小数,悬空标注,注解重叠,图纸截图", Row("check"));
        Equal("一键,孔,基础,倒圆,要求,检查", string.Join(",", StrenuaPage.ClassOptions(QuickCommands.All)));
        // 类面板里只有按钮，没有开关（1.11.0 开关挪到窗口最下面）。
        True(panels.SelectMany(branch => branch.GetProperty("rows").EnumerateArray())
            .SelectMany(row => row.GetProperty("widgets").EnumerateArray())
            .All(w => w.GetProperty("kind").GetString() == "button"), "类面板里应只有按钮");
    }

    static void TestOptions()
    {
        var defaults = new StrenuaOptions();
        True(defaults.Clearance, "避障默认开");
        True(!defaults.Chain, "尺寸链默认关");
        Equal("机加件-钢材", defaults.TechDefault);
        True(!defaults.TechAi, "AI 填写技术要求默认关");

        var dir = Path.Combine(Path.GetTempPath(), "strenua-options-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, StrenuaOptions.FileName);
        try
        {
            // 目录还不存在也能存；重开读回上次。
            var options = new StrenuaOptions(path);
            Equal<string?>(null, options.Set(StrenuaOption.Clearance, false));
            Equal<string?>(null, options.Set(StrenuaOption.Chain, true));
            Equal<string?>(null, options.SetTechDefault("钣金-焊接"));
            Equal<string?>(null, options.Set(StrenuaOption.TechAi, true));
            var reopened = new StrenuaOptions(path);
            True(!reopened.Clearance && reopened.Chain && reopened.TechAi, "重启后应保持上次的开关");
            Equal("钣金-焊接", reopened.TechDefault);
            // 改开关不丢默认技术要求。
            reopened.Set(StrenuaOption.Clearance, true);
            Equal("钣金-焊接", new StrenuaOptions(path).TechDefault);

            // 存档坏了、缺项：回默认值，不拦指令。
            File.WriteAllText(path, "{ 坏的");
            var broken = new StrenuaOptions(path);
            True(broken.Clearance && !broken.Chain, "存档坏了应回默认值");
            File.WriteAllText(path, "{\"Chain\":true}");
            var partial = new StrenuaOptions(path);
            True(partial.Clearance && partial.Chain && partial.TechDefault == "机加件-钢材" && !partial.TechAi, "缺的项按默认值（含旧存档没有默认技术要求、没有 AI 开关）");
        }
        finally
        {
            Directory.Delete(dir, true);
        }

        // 指令：带 value 改、不带报当前值、乱写拒绝。
        var runner = new QuickCommandRunner(new StrenuaOptions());
        var registry = new Registrar();
        HistoryStrenuaModule.Register(registry, runner);
        True(registry.TryGet("strenua.option.chain", out var chain), "缺少尺寸链开关指令");
        CommandResult Run(Dictionary<string, string> values)
            => chain!.Handler(new CommandContext(chain, values, "test", null, default)).GetAwaiter().GetResult();
        var set = Run(new() { ["value"] = "true" });
        True(set.Success && runner.Options.Chain, "value=true 应打开尺寸链");
        var query = Run(new());
        True(query.Success && query.Message.Contains("开"), "不带 value 应报当前值");
        var bad = Run(new() { ["value"] = "maybe" });
        True(!bad.Success && runner.Options.Chain, "乱写的 value 应拒绝且不改设置");

        // 默认技术要求：不带 name 报当前；不存在的模板拒绝且不改；存在的（用户 z 级目录在本机时）改掉。
        True(registry.TryGet("strenua.tech.default", out var techDefault), "缺少默认技术要求指令");
        CommandResult Default(Dictionary<string, string> values)
            => techDefault!.Handler(new CommandContext(techDefault, values, "test", null, default)).GetAwaiter().GetResult();
        var current = Default(new());
        True(current.Success && current.Message.Contains("机加件-钢材", StringComparison.Ordinal), current.Message);
        var missing = Default(new() { ["name"] = "没有这份" });
        True(!missing.Success && runner.Options.TechDefault == "机加件-钢材", "不存在的模板应拒绝且不改");
        if (Directory.Exists(TechTemplates.Folder))
        {
            var changed = Default(new() { ["name"] = "钣金-焊接" });
            True(changed.Success && runner.Options.TechDefault == "钣金-焊接", changed.Message);
            Equal("默认", TechApply.Rows(runner.Options.TechDefault).Single(r => r["name"] == "钣金-焊接")["setting"]);
        }
    }

    static void TestCancelWhenIdle()
    {
        var result = new QuickCommandRunner(new StrenuaOptions()).Cancel();
        True(result.Success, "没有在跑时按取消也应成功，不该让宿主抢控制台");
    }
}
