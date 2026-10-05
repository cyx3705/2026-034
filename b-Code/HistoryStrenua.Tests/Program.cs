using System.Text.Json;
using HistoryStrenua;
using HistoryVulcan.Core.Commands;

// 全部离线：不需要 SolidWorks，也不会去碰本机正在运行的那个。
// 真机验证（附着 SolidWorks、在工程图上加孔标注）见 b-Office/current/验证合同.md 的 VERIFY-LIVE。
var tests = new (string Name, Action Run)[]
{
    ("command registration", TestCommandRegistration),
    ("page owner follows domain", TestPageOwner),
    ("buttons, actions and commands line up", TestPageWiring),
    ("list rows", TestListRows),
    ("toolbar: float, drag area, class, cancel", TestToolbar),
    ("class panels: buttons and switches", TestClassPanels),
    ("options: defaults, persistence, commands", TestOptions),
    ("cancel when idle", TestCancelWhenIdle),
    ("concentric edges are one hole", TestConcentricMerge),
    ("annotated holes are skipped", TestAnnotatedSkipped),
    ("one callout per kind", TestOnePerKind),
    ("center marks: one group per kind", TestCenterMarkGroups),
    ("center marks: only marks on holes are redone", TestCenterMarkObsolete),
    ("hole position: evenly spaced row uses the pattern form", TestPositionPattern),
    ("hole position: uneven row falls back to a chain", TestPositionUneven),
    ("hole position: kinds start from the datum, one tier each", TestPositionKinds),
    ("hole position: kinds sharing center lines are not repeated", TestPositionSharedCenterLines),
    ("hole position: datums are the leftmost and topmost straight edges", TestPositionDatums),
    ("hole position: only linear dimensions on holes are redone", TestPositionObsolete),
    ("hole position: pattern prefix text", TestPatternPrefix),
    ("hole position: chain mode is one ordinate group per direction", TestPositionChain),
    ("slots: two facing half circles pair up", TestSlotPairing),
    ("slots: semicircle and bulge direction", TestSlotGeometry),
    ("slots: one callout per kind, on the upper end", TestSlotCallout),
    ("slots: center marks on both ends", TestSlotCenterMarks),
    ("slots: position on the upper end only", TestSlotPosition),
    ("dowels: only hole wizard dowel holes without a symbol", TestDowelPlan),
    ("dowels: fastener types and arc center", TestDowelGeometry),
    ("clearance: text boxes and lines (real drawing data)", TestClearanceHits),
    ("clearance: dimension text slides along its line", TestClearanceSlide),
    ("clearance: callout moves to a free corner", TestClearanceCallout),
    ("full flow: steps and scope", TestFlowCommand),
    ("dimension geometry: axis from anchors and value", TestDimensionAxes),
    ("dowel fit: adjacent dowel spans, existing reused, others added", TestDowelFitSpans),
    ("dowel fit: shared spans get the dowel-only suffix", TestDowelFitShared),
    ("dowel fit: diameter variable of the callout", TestDowelFitDiameterVariable),
    ("outline: outer edges by ray casting", TestOutlineOuterLines),
    ("outline: stations, tiers and ordinate gaps", TestOutlineStations),
    ("outline: obsolete outline dimensions", TestOutlineObsolete),
    ("outline: view frames, planes and axonometric views", TestViewFrames),
    ("outline: stations already fixed by other views", TestOutlineCoverage),
    ("outline: chain-mode members fixed by other views", TestCoveredOrdinates),
    ("check: callouts, positions, dowels and outline", TestDimensionCheck),
    ("distinct holes stay distinct", TestDistinctHoles),
    ("targets read top-down, left-right", TestTargetOrder),
    ("placement sits up-left of the hole", TestPlacement),
    ("shoulder end is the right end of the underline", TestShoulderEnd),
    ("hole faces the viewer", TestFacesViewer),
    ("hole wall is concave and same radius", TestHoleWall),
    ("perpendicular is unit and orthogonal", TestPerpendicular),
    ("drawing: templates by file name", TestDrawingTemplates),
    ("drawing: main view (15 real parts)", TestDrawingMainView),
    ("drawing: sheet and scale (15 real parts)", TestDrawingSheet),
    ("drawing: side views by hole openings", TestDrawingSideViews),
    ("drawing: layout avoids frame contents", TestDrawingLayout),
    ("drawing: scales, ratios and iso factors", TestDrawingScales),
    ("drawing: technical note text", TestTechnicalNote),
    ("drawing steps: existing views by direction", TestDrawingSlotOf),
    ("drawing steps: side view beside the main view", TestDrawingBeside),
    ("drawing steps: flexible side on the current sheet", TestDrawingChooseSlots),
    ("drawing steps: iso and note on their own", TestDrawingSpots),
    ("drawing steps: one-click runs the steps in order", TestDrawingSteps),
    ("chamfer: which chamfers, grouping, which leg and where", TestChamferPlan),
    ("chamfer: side views and margins", TestChamferViews),
    ("fillet: which arcs, grouping and text side", TestFilletPlan),
    ("snapshot: file name", TestSnapshotName),
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static Registrar Registry()
{
    var registry = new Registrar();
    HistoryStrenuaModule.Register(registry, new QuickCommandRunner(new StrenuaOptions()));
    return registry;
}

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
        "strenua.drawing.auto",
        "strenua.drawing.create",
        "strenua.drawing.project",
        "strenua.drawing.iso",
        "strenua.drawing.note",
        "strenua.drawing.arrange",
        "strenua.drawing.filletall",
        "strenua.drawing.chamferall",
        "strenua.drawing.fillet",
        "strenua.drawing.chamfer",
        "strenua.check.dimension",
        "strenua.check.snapshot",
        "strenua.quick.list",
        "strenua.quick.run",
        "strenua.quick.cancel",
        "strenua.ui.describe",
        "strenua.ui.actions",
        "strenua.option.clearance",
        "strenua.option.chain",
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
    foreach (var name in new[] { "strenua.drawing.create", "strenua.drawing.auto", "strenua.drawing.project", "strenua.drawing.iso", "strenua.drawing.note",
                 "strenua.drawing.arrange", "strenua.drawing.filletall", "strenua.drawing.chamferall", "strenua.drawing.fillet", "strenua.drawing.chamfer",
                 "strenua.check.snapshot" })
        True(registry.TryGet(name, out var drawing) && !drawing!.Readonly && drawing.HiddenReason is null, $"{name} 要能在控制台直接敲（建图、加尺寸、写图片都不是只读）");
    True(registry.TryGet("strenua.quick.run", out var run) && !run!.Readonly, "按 key 执行会改工程图，不是只读");
    var keys = run!.Parameters!.Single(p => p.Name == "key").AllowedValues!;
    True(QuickCommands.All.All(command => keys.Contains(command.Key)), "quick.run 的 key 候选应覆盖全部快捷指令");
    True(!registry.TryGet("strenua.ui.data", out _), "1.7.0 起没有指令表，不该再有取数指令");
    foreach (var method in new[] { "clearance", "chain" })
        True(registry.TryGet("strenua.option." + method, out var option) && option!.HiddenReason is null && !option.Readonly,
            $"strenua.option.{method} 改设置，要能在控制台敲");
    foreach (var method in new[] { "describe", "actions" })
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

    using var description = JsonDocument.Parse(StrenuaPage.Describe(new StrenuaOptions()));
    var bound = Descendants(description.RootElement)
        .Where(node => node.TryGetProperty("kind", out var kind) && kind.GetString() is "button" or "switch")
        .Select(node => node.GetProperty("action").GetString()!)
        .ToList();
    foreach (var command in QuickCommands.All)
        True(bound.Contains(command.ActionId), $"指令「{command.Title}」应是页面上的一个按钮");
    foreach (var action in bound)
        True(declared.ContainsKey(action), $"页面绑定了未声明的动作 {action}");

    // 1.7.0：没有表格、也没有取数。
    True(!Descendants(description.RootElement).Any(node => node.TryGetProperty("type", out var type) && type.GetString() == "table"),
        "页面不该再有表格");
    True(!Descendants(description.RootElement).Any(node => node.TryGetProperty("dataSource", out _)), "页面不该再取数");
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
    Equal("孔", widgets[2].GetProperty("options")[0].GetString()!);
    Equal("孔", widgets[2].GetProperty("value").GetString()!);
    Equal(StrenuaPage.CancelActionId, widgets[3].GetProperty("action").GetString()!);
}

static void TestClassPanels()
{
    var options = new StrenuaOptions();
    options.Set(StrenuaOption.Clearance, false);
    options.Set(StrenuaOption.Chain, true);
    using var description = JsonDocument.Parse(StrenuaPage.Describe(options));
    var container = Descendants(description.RootElement)
        .First(node => node.TryGetProperty("id", out var id) && id.GetString() == StrenuaPage.ClassSwitchId);
    Equal("switch", container.GetProperty("type").GetString()!);
    Equal("{selection." + StrenuaPage.ClassChannel + ".value}", container.GetProperty("source").GetString()!);
    var branches = container.GetProperty("children").EnumerateArray().ToList();
    // 每个类选项正好一块面板，case 就是选项里的字。
    Equal(string.Join(",", StrenuaPage.ClassOptions(QuickCommands.All)),
        string.Join(",", branches.Select(branch => branch.GetProperty("case").GetString())));

    var hole = branches.Single(branch => branch.GetProperty("id").GetString() == StrenuaPage.ClassPanelId("hole"));
    var rows = hole.GetProperty("rows").EnumerateArray().ToList();
    var holeTitles = QuickCommands.All.Where(c => c.CommandClass == "hole").Select(c => c.Title).ToList();
    var buttonRows = (holeTitles.Count + StrenuaPage.ButtonsPerRow - 1) / StrenuaPage.ButtonsPerRow;
    Equal(buttonRows + 1, rows.Count);
    var buttons = rows.Take(buttonRows).SelectMany(row => row.GetProperty("widgets").EnumerateArray()).ToList();
    True(rows.Take(buttonRows).All(row => row.GetProperty("widgets").GetArrayLength() <= StrenuaPage.ButtonsPerRow), "一行最多 4 个按钮");
    Equal(string.Join(",", holeTitles), string.Join(",", buttons.Select(button => button.GetProperty("text").GetString())));
    // 检查类一块面板、只有按钮没有开关（1.9.0 多了「图纸截图」）。
    var check = branches.Single(branch => branch.GetProperty("id").GetString() == StrenuaPage.ClassPanelId("check"));
    Equal("检查", check.GetProperty("case").GetString()!);
    Equal(1, check.GetProperty("rows").GetArrayLength());
    Equal("未标尺寸,图纸截图", string.Join(",", check.GetProperty("rows")[0].GetProperty("widgets").EnumerateArray().Select(w => w.GetProperty("text").GetString())));
    // 1.9.0 出图类：一键出图与它拆出的各步（用户定），1.10.0 加全图倒角、倒角标注，三行按钮，没有开关（照「孔」面板的开关走）。
    var drawing = branches.Single(branch => branch.GetProperty("id").GetString() == StrenuaPage.ClassPanelId("drawing"));
    Equal("出图", drawing.GetProperty("case").GetString()!);
    Equal(3, drawing.GetProperty("rows").GetArrayLength());
    Equal("一键出图,新建工程图,投影视图,轴测图|技术要求,排版,全图圆角,全图倒角|圆角标注,倒角标注", string.Join("|", drawing.GetProperty("rows").EnumerateArray()
        .Select(row => string.Join(",", row.GetProperty("widgets").EnumerateArray().Select(w => w.GetProperty("text").GetString())))));
    Equal("孔,出图,检查", string.Join(",", StrenuaPage.ClassOptions(QuickCommands.All)));
    // 开关在按钮下面，初值取当前设置。
    var switches = rows[buttonRows].GetProperty("widgets").EnumerateArray().ToList();
    Equal("避障,尺寸链", string.Join(",", switches.Select(w => w.GetProperty("label").GetString())));
    True(switches.All(w => w.GetProperty("kind").GetString() == "switch"), "第二行应全是开关");
    Equal("false", switches[0].GetProperty("value").GetString()!);
    Equal("true", switches[1].GetProperty("value").GetString()!);
}

static void TestOptions()
{
    var defaults = new StrenuaOptions();
    True(defaults.Clearance, "避障默认开");
    True(!defaults.Chain, "尺寸链默认关");

    var dir = Path.Combine(Path.GetTempPath(), "strenua-options-" + Guid.NewGuid().ToString("N"));
    var path = Path.Combine(dir, StrenuaOptions.FileName);
    try
    {
        // 目录还不存在也能存；重开读回上次。
        var options = new StrenuaOptions(path);
        Equal<string?>(null, options.Set(StrenuaOption.Clearance, false));
        Equal<string?>(null, options.Set(StrenuaOption.Chain, true));
        var reopened = new StrenuaOptions(path);
        True(!reopened.Clearance && reopened.Chain, "重启后应保持上次的开关");

        // 存档坏了、缺项：回默认值，不拦指令。
        File.WriteAllText(path, "{ 坏的");
        var broken = new StrenuaOptions(path);
        True(broken.Clearance && !broken.Chain, "存档坏了应回默认值");
        File.WriteAllText(path, "{\"Chain\":true}");
        var partial = new StrenuaOptions(path);
        True(partial.Clearance && partial.Chain, "缺的项按默认值");
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
}

static void TestCancelWhenIdle()
{
    var result = new QuickCommandRunner(new StrenuaOptions()).Cancel();
    True(result.Success, "没有在跑时按取消也应成功，不该让宿主抢控制台");
}

static void TestConcentricMerge()
{
    // 沉头孔：沉头 Ø10、底孔 Ø5.5，同一圆心；再加通孔另一端口的同径圆。
    var plan = HoleCalloutPlanner.Plan(
        [
            new HoleEdge(0, 0.1, 0.1, 0.005),
            new HoleEdge(1, 0.1, 0.1, 0.00275),
            new HoleEdge(2, 0.1 + 5e-6, 0.1, 0.00275),
        ],
        []);
    Equal(1, plan.HoleCount);
    Equal(1, plan.Targets.Count);
    True(plan.Targets[0].EdgeIndex is 1 or 2, "同心合并时应取最小的那条（底孔）");
}

static void TestAnnotatedSkipped()
{
    // 两种孔，其中一种已有标注：只给另一种加。
    var plan = HoleCalloutPlanner.Plan(
        [new HoleEdge(0, 0.10, 0.10, 0.003, "A"), new HoleEdge(1, 0.15, 0.10, 0.003, "B")],
        [new SheetPoint(0.10 + 1e-6, 0.10)]);
    Equal(2, plan.HoleCount);
    Equal(2, plan.KindCount);
    Equal(1, plan.AlreadyAnnotated);
    Equal(1, plan.Targets.Count);
    Equal(1, plan.Targets[0].EdgeIndex);
}

static void TestOnePerKind()
{
    // 实测那张移动底板：6 个 M6 同一特征、2 个 Ø4 同一特征、4 个沉头孔（沉头 + 底孔同心）同一特征。
    var edges = new List<HoleEdge>();
    foreach (var (x, y) in new[] { (0.10, 0.20), (0.13, 0.20), (0.10, 0.13), (0.13, 0.13), (0.10, 0.06), (0.13, 0.06) })
        edges.Add(new HoleEdge(edges.Count, x, y, 0.0025, "/M6"));
    foreach (var (x, y) in new[] { (0.10, 0.115), (0.13, 0.115) })
        edges.Add(new HoleEdge(edges.Count, x, y, 0.002, "/Cut4"));
    foreach (var (x, y) in new[] { (0.08, 0.18), (0.15, 0.18), (0.08, 0.115), (0.15, 0.115) })
    {
        edges.Add(new HoleEdge(edges.Count, x, y, 0.0055, "/CBore"));
        edges.Add(new HoleEdge(edges.Count, x, y, 0.0033, "/CBore"));
    }

    var plan = HoleCalloutPlanner.Plan(edges, []);
    Equal(12, plan.HoleCount);
    Equal(3, plan.KindCount);
    Equal(3, plan.Targets.Count);
    // 每种标在最靠左上的那个孔上：M6 在 (0.10,0.20)，沉头在 (0.08,0.18)（取底孔那条边），Ø4 在 (0.10,0.115)。
    Equal("0,9,6", string.Join(",", plan.Targets.Select(target => target.EdgeIndex)));

    // 同一特征里不同孔径是两种。
    var mixed = HoleCalloutPlanner.Plan(
        [new HoleEdge(0, 0.1, 0.1, 0.002, "/Cut"), new HoleEdge(1, 0.2, 0.1, 0.003, "/Cut")], []);
    Equal(2, mixed.KindCount);

    // 一种里只要有一个孔已有标注，整种跳过。
    var skipped = HoleCalloutPlanner.Plan(edges, [new SheetPoint(0.13, 0.06)]);
    Equal(1, skipped.AlreadyAnnotated);
    Equal(2, skipped.Targets.Count);
}

static List<HoleEdge> MovingPlate()
{
    // 实测那张移动底板：6 个 M6 同一特征、2 个 Ø4 同一特征、4 个沉头孔（沉头 + 底孔同心）同一特征。
    var edges = new List<HoleEdge>();
    foreach (var (x, y) in new[] { (0.10, 0.20), (0.13, 0.20), (0.10, 0.13), (0.13, 0.13), (0.10, 0.06), (0.13, 0.06) })
        edges.Add(new HoleEdge(edges.Count, x, y, 0.0025, "/M6"));
    foreach (var (x, y) in new[] { (0.10, 0.115), (0.13, 0.115) })
        edges.Add(new HoleEdge(edges.Count, x, y, 0.002, "/Cut4"));
    foreach (var (x, y) in new[] { (0.08, 0.18), (0.15, 0.18), (0.08, 0.115), (0.15, 0.115) })
    {
        edges.Add(new HoleEdge(edges.Count, x, y, 0.0055, "/CBore"));
        edges.Add(new HoleEdge(edges.Count, x, y, 0.0033, "/CBore"));
    }

    return edges;
}

static void TestCenterMarkGroups()
{
    var plan = CenterMarkPlanner.Plan(MovingPlate(), []);
    Equal(12, plan.HoleCount);
    Equal(3, plan.KindCount);
    Equal(3, plan.Groups.Count);
    True(plan.Groups.All(group => group.Linear), "三种孔都不止一个，都该是线性组");
    // 组按第一个孔的阅读顺序：M6 (0.10,0.20) → 沉头 (0.08,0.18) → Ø4 (0.10,0.115)；
    // 组内从上到下、从左到右，沉头孔取底孔那条边（奇数下标）。
    Equal("0,1,2,3,4,5", string.Join(",", plan.Groups[0].EdgeIndices));
    Equal("9,11,13,15", string.Join(",", plan.Groups[1].EdgeIndices));
    Equal("6,7", string.Join(",", plan.Groups[2].EdgeIndices));

    var lone = CenterMarkPlanner.Plan([new HoleEdge(0, 0.1, 0.1, 0.002, "/Cut")], []);
    True(!lone.Groups.Single().Linear, "只有一个孔的种用单个中心符号线");
}

static void TestCenterMarkObsolete()
{
    var edges = MovingPlate();
    ExistingCenterMark[] existing =
    [
        new(0, [new SheetPoint(0.10, 0.20)]),                                 // 单个，标在 M6 上
        new(1, [new SheetPoint(0.30, 0.30)]),                                 // 圆角之类，不在孔上
        new(2, [new SheetPoint(0.31, 0.30), new SheetPoint(0.15, 0.1150001)]), // 一组，其中一个在沉头孔上
        new(3, []),                                                           // 读不出位置
    ];
    var plan = CenterMarkPlanner.Plan(edges, existing);
    Equal("0,2", string.Join(",", plan.Obsolete));
}

/// <summary>
/// 用户样图那根板条：左边为基准 x=0、上边为基准 y=0（模型 mm），11 个 M5 孔在 y=-30、x=10+60i。
/// 视图比例 1:2，图纸原点在 (0.05, 0.20)。
/// </summary>
static (List<HoleEdge> Holes, double Left, double Top, double Scale) Strip(Func<int, double> x)
{
    const double scale = 0.5;
    const double left = 0.05;
    const double top = 0.20;
    var holes = Enumerable.Range(0, 11)
        .Select(i => new HoleEdge(i, left + x(i) / 1000 * scale, top - 30.0 / 1000 * scale, 0.0021 * scale, "/M5"))
        .ToList();
    return (holes, left, top, scale);
}

static void TestPositionPattern()
{
    var (holes, left, top, scale) = Strip(i => 10 + 60 * i);
    var plan = HolePositionPlanner.Plan(holes, left, top, scale);
    Equal(1, plan.KindCount);
    Equal(1, plan.PatternCount);
    // 水平：第一个孔到最后一个孔「10 x 60 =」（里层），基准到第一个孔（外层）；竖直：基准到第一个孔。
    Equal(3, plan.Dimensions.Count);
    var pattern = plan.Dimensions[0];
    Equal(PositionAxis.Horizontal, pattern.Axis);
    Equal<int?>(0, pattern.FromEdgeIndex);
    Equal(10, pattern.ToEdgeIndex);
    Equal("10 x 60 =", pattern.Prefix);
    Near(top + HolePositionPlanner.FirstTier, pattern.TextAt.Y);

    var fromDatum = plan.Dimensions[1];
    Equal(PositionAxis.Horizontal, fromDatum.Axis);
    Equal<int?>(null, fromDatum.FromEdgeIndex);
    Equal(0, fromDatum.ToEdgeIndex);
    Equal(string.Empty, fromDatum.Prefix);
    Near(top + HolePositionPlanner.FirstTier + HolePositionPlanner.TierStep, fromDatum.TextAt.Y);

    var vertical = plan.Dimensions[2];
    Equal(PositionAxis.Vertical, vertical.Axis);
    Equal<int?>(null, vertical.FromEdgeIndex);
    Equal(0, vertical.ToEdgeIndex);
    Near(left - HolePositionPlanner.FirstTier, vertical.TextAt.X);

    // 正好 4 个等距：不到阵列标法的门槛，逐个接着标。
    var four = HolePositionPlanner.Plan(holes.Take(4).ToList(), left, top, scale);
    Equal(0, four.PatternCount);
    Equal(1 + 3 + 1, four.Dimensions.Count);
}

static void TestPositionChain()
{
    // 等距 11 孔：尺寸链模式出坐标尺寸组、不出线性尺寸，不用阵列写法。0 点是基准边（用户定，不是孔），
    // 水平组 11 站、值 10/70/…/610 mm；竖直方向只有一行（离上边 30 mm）。
    var (holes, left, top, scale) = Strip(i => 10 + 60 * i);
    var plan = HolePositionPlanner.Plan(holes, left, top, scale, chainMode: true);
    Equal(0, plan.PatternCount);
    Equal(0, plan.Dimensions.Count);
    Equal(2, plan.OrdinateGroups.Count);
    var horizontal = plan.OrdinateGroups[0];
    Equal(PositionAxis.Horizontal, horizontal.Axis);
    Equal(string.Join(",", Enumerable.Range(0, 11)), string.Join(",", horizontal.EdgeIndexes));
    for (var i = 0; i < 11; i++)
        Near((10 + 60 * i) / 1000.0, horizontal.Values[i]);
    // 0 点文字在左侧直边正上方 14 mm。
    Near(top + HolePositionPlanner.OrdinateOffset, horizontal.At.Y);
    Near(left, horizontal.At.X);
    var vertical = plan.OrdinateGroups[1];
    Equal(1, vertical.EdgeIndexes.Count);
    Near(0.030, vertical.Values[0]);
    Near(left - HolePositionPlanner.OrdinateOffset, vertical.At.X);
    Near(top, vertical.At.Y);
    // 每组加一个 0 点。
    Equal(12 + 2, plan.Count);

    // 移动底板：三种孔混在一组里。水平列 x=.08/.10/.13/.15 → 4 站；竖直行 y=.20/.18/.13/.115/.06 → 5 站。
    var plate = MovingPlate();
    var mixed = HolePositionPlanner.Plan(plate, 0.05, 0.25, 1, chainMode: true);
    Equal(3, mixed.KindCount);
    var h = mixed.OrdinateGroups.Single(g => g.Axis == PositionAxis.Horizontal);
    var v = mixed.OrdinateGroups.Single(g => g.Axis == PositionAxis.Vertical);
    Equal(4, h.EdgeIndexes.Count);
    Equal(5, v.EdgeIndexes.Count);
    // 竖直组：0 点是上侧直边 y=.25，第一站是最上面那行 .20（值 50 mm），由上往下；文字在左侧直边左边 14 mm、与上边齐。
    Near(0.20, plate[v.EdgeIndexes[0]].Y);
    Near(0.05, v.Values[0]);
    for (var i = 1; i < v.EdgeIndexes.Count; i++)
        True(plate[v.EdgeIndexes[i]].Y < plate[v.EdgeIndexes[i - 1]].Y, "竖直组应由上往下");
    Near(0.05 - HolePositionPlanner.OrdinateOffset, v.At.X);
    Near(0.25, v.At.Y);
    // 水平组：由左往右（第一站 x=.08，离左边 30 mm），每列取最上面的孔（离文字近）。
    Near(0.08, plate[h.EdgeIndexes[0]].X);
    Near(0.03, h.Values[0]);
    foreach (var index in h.EdgeIndexes)
        True(!plate.Any(p => Math.Abs(p.X - plate[index].X) < 1e-9 && p.Y > plate[index].Y + 1e-9), "水平组应连这一列最上面的孔");

    // 核对 SolidWorks 建出来的值。
    True(HolePositionPlanner.SameValues([0, 0.02, 0.05], [0, 0.020004, 0.05]), "0.004 mm 以内算相同");
    True(!HolePositionPlanner.SameValues([0, 0.02, 0.05], [0, 0.021, 0.05]), "差 1 mm 不算相同");
    True(!HolePositionPlanner.SameValues([0, 0.02], [0, 0.02, 0.05]), "个数不同不算相同");
}

static void TestPositionUneven()
{
    // 第 6 个孔错开 5 mm：不等距，退回第一个从基准、其余接着前一个。
    var (holes, left, top, scale) = Strip(i => 10 + 60 * i + (i == 5 ? 5 : 0));
    var plan = HolePositionPlanner.Plan(holes, left, top, scale);
    Equal(0, plan.PatternCount);
    var horizontal = plan.Dimensions.Where(d => d.Axis == PositionAxis.Horizontal).ToList();
    Equal(11, horizontal.Count);
    Equal<int?>(null, horizontal[0].FromEdgeIndex);
    for (var i = 1; i < horizontal.Count; i++)
    {
        Equal<int?>(i - 1, horizontal[i].FromEdgeIndex);
        Equal(i, horizontal[i].ToEdgeIndex);
    }

    True(horizontal.All(d => Math.Abs(d.TextAt.Y - horizontal[0].TextAt.Y) < 1e-12), "同一种的链式尺寸排在同一层");
}

static void TestPositionKinds()
{
    // 移动底板：M6 (x .10/.13, y .20/.13/.06)、Ø4 (x .10/.13, y .115)、沉头 (x .08/.15, y .18/.115)。
    var plan = HolePositionPlanner.Plan(MovingPlate(), 0.05, 0.25, 1);
    Equal(12, plan.HoleCount);
    var horizontal = plan.Dimensions.Where(d => d.Axis == PositionAxis.Horizontal).ToList();
    var vertical = plan.Dimensions.Where(d => d.Axis == PositionAxis.Vertical).ToList();
    // 水平：M6 与 Ø4 在同两条竖直中心线（x=.10/.13）上，「基准→.10」「.10→.13」各只标一次；沉头基准 + 一段。
    Equal(4, horizontal.Count);
    Equal(2, horizontal.Count(d => d.FromEdgeIndex is null));
    // 竖直：Ø4 基准 1 个；沉头基准 + 1；M6 基准 + 2。
    Equal(6, vertical.Count);
    Equal(3, vertical.Count(d => d.FromEdgeIndex is null));
    // 每种一层；一个尺寸都没分到的种不占层。
    Equal(2, horizontal.Select(d => Math.Round(d.TextAt.Y, 9)).Distinct().Count());
    Equal(3, vertical.Select(d => Math.Round(d.TextAt.X, 9)).Distinct().Count());
    // 水平尺寸挑每列最上面的孔（尺寸在上方，界线短）。
    var hole = MovingPlate();
    foreach (var d in horizontal)
        True(!hole.Any(h => Math.Abs(h.X - hole[d.ToEdgeIndex].X) < 1e-9 && h.Y > hole[d.ToEdgeIndex].Y + 1e-9 && h.Kind == hole[d.ToEdgeIndex].Kind),
            "水平尺寸应连在这一列最上面的孔上");
}

static void TestPositionSharedCenterLines()
{
    // 真机移动底板 View1（图纸 mm）：左基准 x=59.3、上基准 y=251。沉头孔与中间那对 Ø5 在同两条水平中心线
    // y=176/111 上，首版各标了一遍「65」（用户截图指出重复）。按中心线标后只剩一个。
    static HoleEdge H(int i, double x, double y, double d, string kind) => new(i, x / 1000, y / 1000, d / 2000, kind);
    List<HoleEdge> holes =
    [
        H(0, 90.3, 201, 5, "/M6"), H(1, 128.3, 201, 5, "/M6"), H(2, 90.3, 126, 5, "/M6"),
        H(3, 128.3, 126, 5, "/M6"), H(4, 90.3, 51, 5, "/M6"), H(5, 128.3, 51, 5, "/M6"),
        H(6, 71.8, 176, 6.6, "/CBore"), H(7, 146.8, 176, 6.6, "/CBore"), H(8, 71.8, 111, 6.6, "/CBore"), H(9, 146.8, 111, 6.6, "/CBore"),
        H(10, 109.3, 176, 5, "/Back5"), H(11, 109.3, 111, 5, "/Back5"),
        H(12, 90.3, 111, 4, "/Cut4"), H(13, 128.3, 111, 4, "/Cut4"),
    ];
    var plan = HolePositionPlanner.Plan(holes, 0.0593, 0.251, 1);
    Equal(14, plan.HoleCount);
    Equal(4, plan.KindCount);

    // 竖直：M6 50/75/75，沉头 75/65，Ø4 140；中间那对 Ø5 的 75、65 都已标过。
    var vertical = plan.Dimensions.Where(d => d.Axis == PositionAxis.Vertical).ToList();
    Equal(6, vertical.Count);
    // 水平：沉头 12.5/75，M6 31/38，Ø5 50；Ø4 的 31、38 都已标过。
    var horizontal = plan.Dimensions.Where(d => d.Axis == PositionAxis.Horizontal).ToList();
    Equal(5, horizontal.Count);

    // 任何一段「中心线 → 中心线」只出现一次。
    foreach (var axis in new[] { PositionAxis.Horizontal, PositionAxis.Vertical })
    {
        double At(int? index) => index is { } i
            ? (axis == PositionAxis.Horizontal ? holes[i].X : holes[i].Y)
            : (axis == PositionAxis.Horizontal ? 0.0593 : 0.251);
        var spans = plan.Dimensions.Where(d => d.Axis == axis)
            .Select(d => (Math.Round(At(d.FromEdgeIndex) * 1e5), Math.Round(At(d.ToEdgeIndex) * 1e5)))
            .ToList();
        Equal(spans.Count, spans.Distinct().Count());
    }
}

static void TestPositionDatums()
{
    // 图纸坐标 Y 向上：上外轮廓 Y 最大。
    SheetSegment[] lines =
    [
        new(0.052, 0.110, 0.052, 0.240), // 左外轮廓
        new(0.058, 0.170, 0.058, 0.240), // 左边往里一点的台阶线
        new(0.052, 0.240, 0.300, 0.240), // 上外轮廓
        new(0.052, 0.170, 0.300, 0.170), // 中间一条水平线
        new(0.052, 0.220, 0.052, 0.240), // 与左外轮廓同一位置的短边：取长的
        new(0.100, 0.100, 0.120, 0.120), // 斜边不算
    ];
    var (left, top) = HolePositionPlanner.Datums(lines);
    Equal<int?>(0, left);
    Equal<int?>(2, top);
    var (none, _) = HolePositionPlanner.Datums([new(0, 0, 0.1, 0)]);
    Equal<int?>(null, none);
}

static void TestPositionObsolete()
{
    var holes = HoleCalloutPlanner.MergeConcentric(MovingPlate());
    ExistingDimension[] existing =
    [
        new(0, 11, [new SheetPoint(0.10, 0.20)]),  // 水平尺寸，连着 M6：删
        new(1, 12, [new SheetPoint(0.08, 0.18)]),  // 竖直尺寸，连着沉头孔：删
        new(2, 6, [new SheetPoint(0.10, 0.20)]),   // 直径尺寸：不动
        new(3, 11, []),                            // 外形尺寸，两头都是直边：不动
        new(4, 2, [new SheetPoint(0.40, 0.40)]),   // 连着的圆不是孔：不动
        new(5, 2, [new SheetPoint(0.13, 0.115)]),  // 斜的线性尺寸，连着 Ø4：删
        // 按中心线标的：连着穿过 M6 孔心 (0.13,0.06) 的竖直中心符号线（真机导轨立板上用户手工标的就是这种）：删
        new(6, 2, [], [new SheetSegment(0.13, 0.065, 0.13, 0.055)]),
        // 连着的中心线不过任何孔心：不动
        new(7, 2, [], [new SheetSegment(0.30, 0.10, 0.30, 0.20)]),
        // 悬空的线性尺寸（挂着的旧中心符号线被删了，连着什么已读不出）：删
        new(8, 2, [], [], Dangling: true),
        // 悬空的直径尺寸：不是线性尺寸，不动
        new(9, 6, [], [], Dangling: true),
        // 尺寸链（坐标尺寸）连着穿过 M6 孔心 (0.10,0.20) 的水平中心符号线：删（两种模式来回切也不留旧的）
        new(10, 1, [], [new SheetSegment(0.095, 0.20, 0.105, 0.20)]),
        // 尺寸链连着孔边：删
        new(11, 1, [new SheetPoint(0.13, 0.115)]),
        // 尺寸链两头都不是孔：不动
        new(12, 1, [], [new SheetSegment(0.30, 0.10, 0.30, 0.20)]),
        // 尺寸链的 0 点（只连基准边、不连孔）；同组成员 14 连着孔与这条边（真机附着顺序是 [孔, 0 点]）：跟着删
        new(13, 1, [], Attached: ["L-left"]),
        new(14, 1, [new SheetPoint(0.13, 0.06)], Attached: ["C-hole", "L-left"]),
        // 另一组的 0 点，组里没有要删的：不动
        new(15, 1, [], Attached: ["L-other"]),
    ];
    Equal("0,1,5,6,8,10,11,13,14", string.Join(",", HolePositionPlanner.Obsolete(holes, existing)));
    True(!HolePositionPlanner.PassesThrough(new SheetSegment(0.13, 0.07, 0.13, 0.08), holes[0]), "线段延长线过孔心不算");
    // 真机底板（图纸 mm）：线性中心符号线的连接线 x=89.91、y 184.17–206.95，孔心 (89.91, 208.06) 在线头外 1.11 mm。
    var gapHole = new HoleEdge(99, 0.08991, 0.20806, 0.000275, "/M3");
    True(HolePositionPlanner.PassesThrough(new SheetSegment(0.08991, 0.18417, 0.08991, 0.20695), gapHole), "中心符号线在孔心前断开的空隙要算穿过");
    True(!HolePositionPlanner.PassesThrough(new SheetSegment(0.08991, 0.18417, 0.08991, 0.20495), gapHole), "断开 3 mm 就不算了");
    True(!HolePositionPlanner.PassesThrough(new SheetSegment(0.08891, 0.18417, 0.08891, 0.20895), gapHole), "偏开 1 mm 的平行线不算");
}

static void TestPatternPrefix()
{
    Equal("10 x 60 =", HolePositionPlanner.PatternPrefix(10, 0.06));
    Equal("4 x 12.5 =", HolePositionPlanner.PatternPrefix(4, 0.0125));
    Equal("5 x 20 =", HolePositionPlanner.PatternPrefix(5, 0.0200000001));
}

/// <summary>
/// 一个腰型孔的两条端头半圆（图纸坐标，米）：两端圆心 (x1,y1)、(x2,y2)，各朝背离对方的方向鼓。
/// </summary>
static HoleEdge[] Slot(int index, double x1, double y1, double x2, double y2, double radius, string kind)
{
    var length = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
    var (ux, uy) = ((x2 - x1) / length, (y2 - y1) / length);
    return
    [
        new HoleEdge(index, x1, y1, radius, kind, -ux, -uy),
        new HoleEdge(index + 1, x2, y2, radius, kind, ux, uy),
    ];
}

static void TestSlotPairing()
{
    List<HoleEdge> edges =
    [
        // 两个竖腰型孔首尾相接排成一列：中间相对的两端（y=.13 与 .15）是互相朝着鼓的，不能配成一对。
        .. Slot(0, 0.10, 0.18, 0.10, 0.15, 0.003, "/Slot"),
        .. Slot(2, 0.10, 0.13, 0.10, 0.10, 0.003, "/Slot"),
        // 开口槽口只有一个端头半圆：配不上，丢掉。
        new HoleEdge(4, 0.30, 0.10, 0.003, "/Notch", 0, 1),
        // 同位置同朝向、但不是同一个特征的半圆：不配。
        new HoleEdge(5, 0.20, 0.10, 0.003, "/A", -1, 0),
        new HoleEdge(6, 0.25, 0.10, 0.003, "/B", 1, 0),
        // 圆孔原样保留。
        new HoleEdge(7, 0.40, 0.10, 0.002, "/Round"),
    ];
    // 同心合并按半径排过，这里按下标看。
    var holes = HoleCalloutPlanner.Recognize(edges).OrderBy(hole => hole.Index).ToList();
    Equal("0,1,2,3,7", string.Join(",", holes.Select(hole => hole.Index)));
    Equal(holes[0].Slot, holes[1].Slot);
    Equal(holes[2].Slot, holes[3].Slot);
    True(holes[0].Slot != holes[2].Slot, "两个腰型孔编号不同");
    Equal(-1, holes[4].Slot);
    True(holes[0].Kind.StartsWith("/Slot/腰", StringComparison.Ordinal), "腰型孔的种名带上长度");
    Equal("/Round", holes[4].Kind);
    Equal((3, 2), HoleCalloutPlanner.Count(holes));
    Equal("3 个孔（含 2 个腰型孔）共 2 种", HoleCalloutPlanner.Summary(3, 2, 2));
    Equal("12 个孔共 3 种", HoleCalloutPlanner.Summary(12, 0, 3));

    // 沉头腰型孔：沉头端头与底端头同心，合并后仍是一对（取小的）。
    var counterbored = HoleCalloutPlanner.Recognize(
        [.. Slot(0, 0.1, 0.2, 0.1, 0.1, 0.005, "/CSlot"), .. Slot(2, 0.1, 0.2, 0.1, 0.1, 0.003, "/CSlot")]);
    Equal("2,3", string.Join(",", counterbored.Select(hole => hole.Index)));
    Equal(counterbored[0].Slot, counterbored[1].Slot);

    // 同一特征里长短不同的腰型孔是两种。
    var lengths = HoleCalloutPlanner.GroupKinds(HoleCalloutPlanner.Recognize(
        [.. Slot(0, 0.1, 0.2, 0.1, 0.18, 0.003, "/S"), .. Slot(2, 0.2, 0.2, 0.2, 0.17, 0.003, "/S")]));
    Equal(2, lengths.Count);
}

static void TestSlotGeometry()
{
    // 圆心原点、轴 +Z、半径 5：(5,0,0)→(-5,0,0) 是半圈；(5,0,0)→(0,5,0) 是四分之一圈。
    double[] circle = [0, 0, 0, 0, 0, 1, 0.005];
    True(SlotPlanner.IsSemicircle(circle, [0.005, 0, 0], [-0.005, 0, 0]), "半圈");
    True(!SlotPlanner.IsSemicircle(circle, [0.005, 0, 0], [0, 0.005, 0]), "四分之一圈（内角圆角）不是");

    // 轴 × 弦 = (0,0,1) × (-1,0,0) = (0,-1,0)。
    var normal = SlotPlanner.ChordNormal(circle, [0.005, 0, 0], [-0.005, 0, 0]);
    Near(0, normal.X);
    Near(-1, normal.Y);
    double[] probe = [0, -0.005, 0];
    // 弧在上半边：离 probe 最近的是端点（约 √2 倍半径远），鼓向反方向 +Y。
    Near(1, SlotPlanner.Bulge(normal, probe, [0.005, 0, 0], 0.005).Y);
    // 弧在下半边：probe 自己就在弧上，鼓向 -Y。
    Near(-1, SlotPlanner.Bulge(normal, probe, probe, 0.005).Y);
}

static void TestSlotCallout()
{
    // 两个同特征的竖腰型孔 + 两个圆孔：两种各标一次；腰型孔标在最左那个腰型孔的上端。
    List<HoleEdge> edges =
    [
        .. Slot(0, 0.10, 0.20, 0.10, 0.17, 0.003, "/Slot"),
        .. Slot(2, 0.14, 0.20, 0.14, 0.17, 0.003, "/Slot"),
        new HoleEdge(4, 0.10, 0.10, 0.002, "/Round"),
        new HoleEdge(5, 0.14, 0.10, 0.002, "/Round"),
    ];
    var plan = HoleCalloutPlanner.Plan(edges, []);
    Equal(4, plan.HoleCount);
    Equal(2, plan.SlotCount);
    Equal(2, plan.KindCount);
    Equal("0,4", string.Join(",", plan.Targets.Select(target => target.EdgeIndex)));
    Equal("4 个孔（含 2 个腰型孔）共 2 种", plan.Summary);

    // 已有孔标注挂在某个腰型孔的下端：整种算标过。
    var skipped = HoleCalloutPlanner.Plan(edges, [new SheetPoint(0.14, 0.17)]);
    Equal(1, skipped.AlreadyAnnotated);
    Equal("4", string.Join(",", skipped.Targets.Select(target => target.EdgeIndex)));
}

static void TestSlotCenterMarks()
{
    // 腰型孔两端各当一个孔标：两个竖腰型孔排成 2×2，一组线性；圆孔自成一组。
    List<HoleEdge> edges =
    [
        .. Slot(0, 0.10, 0.20, 0.10, 0.17, 0.003, "/Slot"),
        .. Slot(2, 0.14, 0.20, 0.14, 0.17, 0.003, "/Slot"),
        new HoleEdge(4, 0.10, 0.10, 0.002, "/Round"),
    ];
    var plan = CenterMarkPlanner.Plan(edges, [new ExistingCenterMark(0, [new SheetPoint(0.14, 0.17)]), new ExistingCenterMark(1, [new SheetPoint(0.3, 0.3)])]);
    Equal(3, plan.HoleCount);
    Equal(2, plan.Groups.Count);
    Equal("0,2,1,3", string.Join(",", plan.Groups[0].EdgeIndices));
    True(plan.Groups[0].Linear, "腰型孔端头成线性组");
    True(plan.Groups[0].Slot && !plan.Groups[1].Slot, "腰型孔那组要按槽口样式插");
    True(!plan.Groups[1].Linear, "单个圆孔用单个");
    // 标在腰型孔端头上的旧符号线要删。
    Equal("0", string.Join(",", plan.Obsolete));

    // 只有一个腰型孔：两端也是一组线性，连接线就是它的中心线。
    var lone = CenterMarkPlanner.Plan(Slot(0, 0.1, 0.2, 0.1, 0.17, 0.003, "/Slot"), []);
    Equal("0,1", string.Join(",", lone.Groups.Single().EdgeIndices));
    True(lone.Groups.Single().Linear, "一个腰型孔两端连成线性组");
}

static void TestSlotPosition()
{
    // 左基准 x=.05、上基准 y=.25。竖腰型孔上端 (.10,.20)、下端 (.10,.17)；横腰型孔左端 (.15,.12)、右端 (.19,.12)。
    List<HoleEdge> edges =
    [
        .. Slot(0, 0.10, 0.17, 0.10, 0.20, 0.003, "/V"),
        .. Slot(2, 0.19, 0.12, 0.15, 0.12, 0.003, "/H"),
    ];
    var plan = HolePositionPlanner.Plan(edges, 0.05, 0.25, 1);
    Equal(2, plan.HoleCount);
    Equal(2, plan.SlotCount);
    // 每个腰型孔只标一端：竖的标上端（下标 1），横的一样高取左端（下标 3）；每个方向各从基准一个。
    Equal(4, plan.Dimensions.Count);
    True(plan.Dimensions.All(d => d.FromEdgeIndex is null), "两种都从基准标");
    Equal("1,3", string.Join(",", plan.Dimensions.Select(d => d.ToEdgeIndex).Distinct().OrderBy(i => i)));

    // 旧尺寸挂在腰型孔下端上也要删。
    var holes = HoleCalloutPlanner.Recognize(edges);
    Equal("0", string.Join(",", HolePositionPlanner.Obsolete(holes, [new ExistingDimension(0, 12, [new SheetPoint(0.10, 0.17)])])));
}

static void TestDowelPlan()
{
    // 两个销钉孔（其中一个已有符号）、一个同径普通孔、一个沉头销钉孔（两条同心边）、一对腰型孔端头（即便标成销钉也不算）。
    var plan = DowelPlanner.Plan(
        [
            new HoleEdge(0, 0.10, 0.20, 0.003, "/销钉孔1", Dowel: true),
            new HoleEdge(1, 0.15, 0.20, 0.003, "/销钉孔1", Dowel: true),
            new HoleEdge(2, 0.20, 0.20, 0.003, "/Cut"),
            new HoleEdge(3, 0.10, 0.10, 0.005, "/销钉孔2", Dowel: true),
            new HoleEdge(4, 0.10, 0.10, 0.002, "/销钉孔2", Dowel: true),
            .. Slot(5, 0.3, 0.2, 0.3, 0.1, 0.003, "/S").Select(edge => edge with { Dowel = true }),
        ],
        [new SheetPoint(0.15 + 1e-6, 0.20)]);
    Equal(3, plan.DowelCount);
    Equal(1, plan.AlreadyMarked);
    Equal("0,4", string.Join(",", plan.EdgeIndices));
    Equal(0, DowelPlanner.Plan([new HoleEdge(0, 0.1, 0.1, 0.003, "/Cut")], []).DowelCount);
}

static void TestDowelGeometry()
{
    True(DowelPlanner.IsDowelFastener(708), "GB 销钉孔");
    True(DowelPlanner.IsDowelFastener(703) && DowelPlanner.IsDowelFastener(712), "703–712 都是销钉孔");
    True(!DowelPlanner.IsDowelFastener(361), "GB 内六角圆柱头螺钉（沉头孔）不是");
    // 真机读回（过渡板 View2 Ø5.5 孔上的销钉符号）：起点 (79.56,94.68)、中点 (79.56,91.93)、终点同起点，图纸 mm。
    var center = DowelPlanner.ArcCenter([0.07956, 0.09468, 0.07956, 0.09193, 0.07956, 0.09468]);
    Near(0.07956, center!.Value.X);
    True(Math.Abs(center.Value.Y - 0.093305) < 1e-9, "圆心在起点与中点正中");
    True(DowelPlanner.ArcCenter([0.1, 0.2]) is null, "点不够返回 null");
}

static void TestClearanceHits()
{
    // 过渡板 View2 真机显示数据（图纸 mm → m）。竖直尺寸「24」：基线点 (44.31,91.74)、角 π/2、宽 6.13、高 3.5。
    static double M(double mm) => mm / 1000;
    var text24 = new TextBox(M(44.31), M(91.74), M(6.13), M(3.5), Math.PI / 2);
    var corners = text24.Corners().ToList();
    True(corners.All(p => p.X <= M(44.31) + 1e-12 && p.X >= M(40.81) - 1e-12), "竖直尺寸的字往 -X 长 3.5 mm");
    // 同一列上的「10」的尺寸线伸出箭头的那一截 (44.31,100.8)-(44.31,97.3) 伸到「24」字底下：算（分不清是谁的数）；
    // 共线但停在字外的不算。
    True(ClearancePlanner.Hits(text24, new SheetSegment(M(44.31), M(100.8), M(44.31), M(97.3))), "伸到字底下的共线尺寸线要算");
    True(!ClearancePlanner.Hits(text24, new SheetSegment(M(44.31), M(105.8), M(44.31), M(98.5))), "停在字外的共线尺寸线不算");
    // 一条水平尺寸界线横穿字：算。
    True(ClearancePlanner.Hits(text24, new SheetSegment(M(30), M(94), M(60), M(94))), "横穿字的界线要算");
    // 端点在字里、另一端在外：算；整条在外：不算。
    True(ClearancePlanner.Hits(text24, new SheetSegment(M(42), M(93), M(42), M(80))), "伸进字里的线要算");
    True(!ClearancePlanner.Hits(text24, new SheetSegment(M(30), M(80), M(60), M(80))), "外面的线不算");
    // 水平尺寸「42.50」与「30」同层相邻：两段字不相交；字叠在一起才算。
    var text4250 = new TextBox(M(63.57), M(113.8), M(10.74), M(3.5));
    var text30 = new TextBox(M(83.99), M(113.8), M(6.13), M(3.5));
    True(!ClearancePlanner.Hits(text4250, text30), "相邻的字不相交");
    True(ClearancePlanner.Hits(text4250, text30.Shift(M(-15), 0)), "叠上的字要算");
    True(ClearancePlanner.Hits(text4250, new TextBox(M(65), M(114.5), M(1), M(1))), "整个包在里面也算");
    var obstacles = new Obstacles([new SheetSegment(M(30), M(94), M(60), M(94))], [text30]);
    Equal(1, ClearancePlanner.Hits([text24], obstacles));
    Equal(0, ClearancePlanner.Hits([text4250], obstacles));
}

static void TestClearanceSlide()
{
    // 水平尺寸数字宽 6 mm，正中有一条竖直界线穿过：沿尺寸线（+X 优先）滑到字完全离开（含留白）。
    var text = new TextBox(0.100, 0.200, 0.006, 0.0035);
    var obstacles = new Obstacles([new SheetSegment(0.103, 0.190, 0.103, 0.210)], []);
    var offset = ClearancePlanner.Slide([text], 1, 0, obstacles);
    True(offset is not null, "应能滑开");
    Equal(0, ClearancePlanner.Hits(ClearancePlanner.Shift([text], offset!.Value, 0), obstacles));
    True(Math.Abs(offset.Value - 0.0035) < 1e-12, $"应滑 3.5 mm（0.5 mm 一步、躲开 0.3 mm 留白），实际 {offset.Value * 1000:0.###}");
    Equal(0.0, ClearancePlanner.Slide([text], 1, 0, Obstacles.Empty));
    // 两边都被挡死：null，不挪。
    var walls = Enumerable.Range(-40, 81).Select(i => new SheetSegment(0.1 + i * 0.001, 0.19, 0.1 + i * 0.001, 0.21)).ToList();
    True(ClearancePlanner.Slide([text], 1, 0, new Obstacles(walls, [])) is null, "滑到头都压：不挪");

    // 真机过渡板「9.50」：尺寸界线 x=58.31 与 63.06（跨度 4.75 mm），字宽 9.2 mm 装不下；自己的界线也算障碍，
    // 尺寸线（与字平行）不算。字整个滑到界线外。
    static double M(double mm) => mm / 1000;
    SheetSegment[] own =
    [
        new(M(58.31), M(105.3), M(58.31), M(121.8)),
        new(M(63.06), M(105.55), M(63.06), M(121.8)),
        new(M(58.31), M(119.8), M(63.06), M(119.8)),
    ];
    var crossing = ClearancePlanner.CrossingLines(own, 0).ToList();
    Equal(2, crossing.Count);
    Equal(1, ClearancePlanner.CrossingLines(own, Math.PI / 2).Count());
    var text950 = new TextBox(M(56.09), M(119.8), M(9.2), M(3.5));
    var slide = ClearancePlanner.Slide([text950], 1, 0, new Obstacles(crossing, []));
    var moved = text950.Shift(slide!.Value, 0);
    True(moved.X >= M(63.06) || moved.X + moved.Width <= M(58.31), $"字应整个在界线外，实际 {moved.X * 1000:0.##}–{(moved.X + moved.Width) * 1000:0.##}");
}

static void TestClearanceCallout()
{
    // 真机：孔标注「2 x Ø5.50 完全贯穿」在左时下划线 32.5–65.44，右端（折点）= 文字右缘 64.19 + 1.25；
    // 整块右移 50 mm 后 SolidWorks 把引线换到左侧，下划线左端 81.25 = 文字左缘 82.5 - 1.25。
    static double M(double mm) => mm / 1000;
    TextBox[] texts = [new(M(32.5), M(68.82), M(6.13), M(3.5)), new(M(42.74), M(68.82), M(21.45), M(3.5)), new(M(36.06), M(64.49), M(7.36), M(3.5))];
    var (dx, dy) = ClearancePlanner.CalloutShift(texts, M(68.84), new CalloutSpot(new SheetPoint(M(65.44), M(68.84)), TextLeft: true));
    True(Math.Abs(dx) < 1e-5 && Math.Abs(dy) < 1e-9, "原位折点不该挪");
    (dx, _) = ClearancePlanner.CalloutShift(texts, M(68.84), new CalloutSpot(new SheetPoint(M(81.25), M(68.84)), TextLeft: false));
    True(Math.Abs(dx - M(50)) < 1e-5, $"换到右侧应平移 50 mm，实际 {dx * 1000:0.###}");

    // 孔在 (100,100) mm、半径 2 mm；左上角位压着一条水平线：换到右上。
    var hole = new HoleEdge(0, M(100), M(100), M(2));
    var spots = ClearancePlanner.CalloutSpots(hole).ToList();
    Equal(12, spots.Count);
    True(spots[0].TextLeft && spots[0].Shoulder.X < hole.X && spots[0].Shoulder.Y > hole.Y, "第一个是左上（默认）");
    True(!spots[1].TextLeft && spots[1].Shoulder.X > hole.X && spots[1].Shoulder.Y > hole.Y, "第二个是右上，文字在右");
    var first = ClearancePlanner.CalloutShift(texts, M(68.84), spots[0]);
    var block = ClearancePlanner.Shift(texts, first.Dx, first.Dy);
    var top = block.SelectMany(text => text.Corners()).Max(p => p.Y);
    var obstacles = new Obstacles([new SheetSegment(M(40), top - M(1), M(99), top - M(1))], []);
    var hits = ClearancePlanner.Hits(block, obstacles);
    Equal(1, hits);
    var chosen = ClearancePlanner.ChooseCallout(texts, M(68.84), hole, obstacles, hits);
    Equal(spots[1], chosen!.Value);
    // 四面八方都压且不比现在少：不挪。
    var everywhere = new Obstacles(Enumerable.Range(0, 40).Select(i => new SheetSegment(0, M(50 + i * 2), M(300), M(50 + i * 2))).ToList(), []);
    True(ClearancePlanner.ChooseCallout(texts, M(68.84), hole, everywhere, 1) is null, "没有更好的角位就不挪");
}

static void TestFlowCommand()
{
    var flow = QuickCommands.All.Single(command => command.Key == "hole-flow");
    Equal("strenua.hole.flow", flow.CommandName);
    Equal("孔标注全流程", flow.Title);
    // 用户定的顺序写在用法里，页面上看得到。
    var usage = flow.Usage;
    True(usage.IndexOf("销钉符号", StringComparison.Ordinal) < usage.IndexOf("中心符号线", StringComparison.Ordinal)
        && usage.IndexOf("中心符号线", StringComparison.Ordinal) < usage.IndexOf("孔位尺寸", StringComparison.Ordinal)
        && usage.IndexOf("孔位尺寸", StringComparison.Ordinal) < usage.IndexOf("→ 外轮廓", StringComparison.Ordinal)
        && usage.IndexOf("→ 外轮廓", StringComparison.Ordinal) < usage.IndexOf("→ 孔标注", StringComparison.Ordinal)
        && usage.IndexOf("→ 孔标注", StringComparison.Ordinal) < usage.IndexOf("→ 销孔标注", StringComparison.Ordinal), "步骤顺序");
    True(usage.Contains("当前图纸页", StringComparison.Ordinal), "范围是当前图纸页");
    Equal("hole-flow,dowel-symbol,center-mark,hole-position,hole-callout,dowel-fit,outline,"
        + "drawing-auto,drawing-create,drawing-project,drawing-iso,drawing-note,drawing-arrange,fillet-all,chamfer-all,fillet,chamfer,check-dimension,snapshot",
        string.Join(",", QuickCommands.All.Select(command => command.Key)));
}

static DimensionAnchor C(double x, double y) => DimensionAnchor.Center(new SheetPoint(x, y));

static DimensionAnchor L(double x1, double y1, double x2, double y2, bool model = true)
    => DimensionAnchor.Line(new SheetSegment(x1, y1, x2, y2), model);

static void TestDimensionAxes()
{
    // 比例 1:2：两孔心横向差 0.02（图纸）→ 模型 0.04。
    var horizontal = new ViewDimension(0, 2, false, 0.04, [C(0.10, 0.20), C(0.12, 0.15)]);
    Equal((true, false), DimensionGeometry.Axes(horizontal, 0.5));
    var vertical = new ViewDimension(1, 2, false, 0.10, [C(0.10, 0.20), C(0.12, 0.15)]);
    Equal((false, true), DimensionGeometry.Axes(vertical, 0.5));
    // 连中心符号线竖线（只给 X）与左侧基准边（竖直模型边）。
    var onLines = new ViewDimension(2, 2, false, 0.02, [L(0.12, 0.18, 0.12, 0.22, model: false), L(0.10, 0.0, 0.10, 0.3)]);
    Equal((true, false), DimensionGeometry.Axes(onLines, 1.0));
    Equal((0.10, 0.12), DimensionGeometry.Span(onLines, PositionAxis.Horizontal)!.Value);
    True(DimensionGeometry.Span(onLines, PositionAxis.Vertical) is null, "竖线给不出 Y");
    // 斜线不给坐标；值读不出的不认方向。
    var slanted = DimensionAnchor.Line(new SheetSegment(0, 0, 0.1, 0.1), true);
    True(slanted.X is null && slanted.Y is null, "斜线两个坐标都不给");
    Equal((false, false), DimensionGeometry.Axes(new ViewDimension(3, 2, false, double.NaN, [C(0, 0), C(1, 0)]), 1));
}

static void TestDowelFitSpans()
{
    // 三个销孔：A(0.10,0.20)、B(0.15,0.20) 同一行，C(0.15,0.12) 在 B 正下方；一个普通孔。比例 1。
    HoleEdge[] edges =
    [
        new(0, 0.10, 0.20, 0.003, "/销孔", Dowel: true),
        new(1, 0.15, 0.20, 0.003, "/销孔", Dowel: true),
        new(2, 0.15, 0.12, 0.003, "/销孔", Dowel: true),
        new(3, 0.30, 0.20, 0.004, "/Cut"),
    ];
    // 已有一个 A→B 的水平尺寸（连孔边）；没有竖直的。
    ViewDimension[] existing = [new(0, 2, false, 0.05, [C(0.10, 0.20), C(0.15, 0.20)])];
    var plan = DowelFitPlanner.Plan(edges, existing, 1.0, chainMode: true, left: 0.05, top: 0.25);
    Equal(3, plan.DowelCount);
    Equal(1, plan.Kinds.Count);
    Equal(2, plan.Spans.Count);
    var h = plan.Spans.Single(span => span.Axis == PositionAxis.Horizontal);
    Equal(0, h.Existing!.Value);
    True(!h.Shared, "连着两个销孔，不兼管");
    var v = plan.Spans.Single(span => span.Axis == PositionAxis.Vertical);
    True(v.Existing is null, "竖直段要新加");
    Near(0.12, v.From);
    Near(0.20, v.To);
    // 竖直站各取最左的孔：0.12 那站只有 C，0.20 那站 A 比 B 靠左。
    Equal("2,0", $"{v.FromEdgeIndex},{v.ToEdgeIndex}");
    True(v.TextAt.X < 0.10 - 0.003, "竖直尺寸放在较左那个孔的左边");
    // 不跨种：另一种销孔 F 和 A 在同一行，也不与 A、B 连。
    var mixed = DowelFitPlanner.Plan([.. edges, new(4, 0.40, 0.20, 0.002, "/销孔2", Dowel: true)], existing, 1.0, true, 0.05, 0.25);
    Equal(2, mixed.Kinds.Count);
    Equal(2, mixed.Spans.Count);
    True(mixed.Spans.All(span => span.FromEdgeIndex != 4 && span.ToEdgeIndex != 4), "单个的另一种销孔没有销孔间尺寸");
    // 普通孔与销孔之间不加；没有销孔就什么都没有。
    Equal(0, DowelFitPlanner.Plan([edges[3]], [], 1.0, false, 0, 0.3).Spans.Count);
    // 腰型孔端头即便标成销钉也不算。
    Equal(0, DowelFitPlanner.Dowels(Slot(0, 0.3, 0.2, 0.3, 0.1, 0.003, "/S").Select(e => e with { Dowel = true }).ToList()).Count);
}

static void TestDowelFitShared()
{
    // 两个销孔 A(0.10,0.20)、B(0.15,0.20)；普通孔 P(0.10,0.10)、Q(0.15,0.10) 同一种、各在销孔的正下方——
    // 普通模式下 P→Q 这段与 A→B 重叠，去重只标一次：销孔间尺寸要写「(仅销孔)」。
    HoleEdge[] edges =
    [
        new(0, 0.10, 0.20, 0.003, "/销孔", Dowel: true),
        new(1, 0.15, 0.20, 0.003, "/销孔", Dowel: true),
        new(2, 0.10, 0.10, 0.004, "/Cut"),
        new(3, 0.15, 0.10, 0.004, "/Cut"),
    ];
    var spans = HolePositionPlanner.RequestedSpans(edges, 0.05, 0.25, 1.0);
    True(spans.Any(span => span.Axis == PositionAxis.Horizontal && !span.Dowel && Math.Abs(span.From - 0.10) < 1e-9 && Math.Abs(span.To - 0.15) < 1e-9),
        "普通孔也想标 0.10→0.15");
    var plan = DowelFitPlanner.Plan(edges, [], 1.0, chainMode: false, left: 0.05, top: 0.25);
    True(plan.Spans.Single().Shared, "普通模式下重叠 → 兼管");
    // 尺寸链模式没有线性尺寸去重，不写。
    True(!DowelFitPlanner.Plan(edges, [], 1.0, chainMode: true, left: 0.05, top: 0.25).Spans.Single().Shared, "尺寸链模式不写");
    // 已有的这段连在普通孔上（去重时留下的是普通孔那个）：也算兼管，且认得出它就是这段。
    ViewDimension[] existing = [new(0, 2, false, 0.05, [C(0.10, 0.10), C(0.15, 0.10)])];
    var reuse = DowelFitPlanner.Plan(edges.Take(2).ToList(), existing, 1.0, chainMode: true, left: 0.05, top: 0.25).Spans.Single();
    Equal(0, reuse.Existing!.Value);
    True(reuse.Shared, "连着别的孔 → 兼管");
    // 连穿过销孔心的中心符号线：不算兼管。
    ViewDimension[] onMarks = [new(0, 2, false, 0.05, [L(0.10, 0.19, 0.10, 0.21, false), L(0.15, 0.19, 0.15, 0.21, false)])];
    var marks = DowelFitPlanner.Plan(edges.Take(2).ToList(), onMarks, 1.0, chainMode: true, left: 0.05, top: 0.25).Spans.Single();
    True(marks.Existing == 0 && !marks.Shared, "连销孔中心符号线 → 就是销孔间尺寸");
}

static void TestDowelFitDiameterVariable()
{
    // 孔径 6 mm：深度 12 mm 不是；名字带 diam 的优先；毫米单位也认。
    Equal(1, DowelFitPlanner.DiameterVariable([("<hw-depth>", 0.012), ("<hw-diam>", 0.006)], 0.006));
    Equal(2, DowelFitPlanner.DiameterVariable([("<a>", double.NaN), ("<x>", 0.006), ("<hw-diam>", 6.0)], 0.006));
    Equal(-1, DowelFitPlanner.DiameterVariable([("<hw-depth>", 0.012)], 0.006));
}

static SheetSegment S(double x1, double y1, double x2, double y2) => new(x1, y1, x2, y2);

/// <summary>
/// 测试用零件（图纸坐标，米）：外框 0..0.2 × 0..0.1，右上角切一个台阶（x 0.15..0.2、y 0.07..0.1 去掉），
/// 上边开一个通到外面的槽（x 0.05..0.07、深到 y 0.08），中间一个封闭方型腔（0.09..0.11 × 0.03..0.05），左下一条斜边。
/// </summary>
static List<SheetSegment> Part()
    =>
    [
        S(0, 0, 0, 0.1),           // 0 左边（基准）
        S(0, 0.1, 0.05, 0.1),      // 1 上边左段
        S(0.05, 0.1, 0.05, 0.08),  // 2 槽左壁
        S(0.05, 0.08, 0.07, 0.08), // 3 槽底
        S(0.07, 0.08, 0.07, 0.1),  // 4 槽右壁
        S(0.07, 0.1, 0.15, 0.1),   // 5 上边中段（最长，上侧基准）
        S(0.15, 0.1, 0.15, 0.07),  // 6 台阶竖边
        S(0.15, 0.07, 0.2, 0.07),  // 7 台阶横边
        S(0.2, 0.07, 0.2, 0),      // 8 右边
        S(0.2, 0, 0, 0),           // 9 下边
        S(0.09, 0.03, 0.11, 0.03), // 10 型腔
        S(0.11, 0.03, 0.11, 0.05), // 11
        S(0.11, 0.05, 0.09, 0.05), // 12
        S(0.09, 0.05, 0.09, 0.03), // 13
        S(0.02, 0.02, 0.04, 0.04), // 14 斜边（不标）
    ];

static void TestOutlineOuterLines()
{
    var outer = OutlinePlanner.OuterLines(Part(), []);
    Equal("0,1,2,3,4,5,6,7,8,9", string.Join(",", outer));
    True(!OutlinePlanner.Outside(new SheetPoint(0.1, 0.04), Part()), "型腔里出不去");
    True(OutlinePlanner.Outside(new SheetPoint(0.06, 0.09), Part()), "开口槽里朝上出得去");
    True(OutlinePlanner.RayHits(new SheetPoint(0, 0), 1, 0, S(0.1, -0.1, 0.1, 0.1)), "正前方的竖线挡住");
    True(!OutlinePlanner.RayHits(new SheetPoint(0, 0), -1, 0, S(0.1, -0.1, 0.1, 0.1)), "背后的不挡");
    True(!OutlinePlanner.RayHits(new SheetPoint(0, 0), 1, 0, S(0.1, 0, 0.2, 0)), "平行的不算");
    // 曲线边也挡：右边换成圆角近似的折线，台阶照样认。
    var rounded = Part();
    rounded[8] = S(0.2, 0.07, 0.2, 0.01);
    var corner = new List<SheetSegment> { S(0.2, 0.01, 0.197, 0.003), S(0.197, 0.003, 0.19, 0) };
    rounded[9] = S(0.19, 0, 0, 0);
    Equal("0,1,2,3,4,5,6,7,8,9", string.Join(",", OutlinePlanner.OuterLines(rounded, corner)));
}

static void TestOutlineStations()
{
    var lines = Part();
    var outer = OutlinePlanner.OuterLines(lines, []);
    var invariant = System.Globalization.CultureInfo.InvariantCulture;
    // 比例 1:2：图纸 0.05 → 模型 0.1。
    var stations = OutlinePlanner.Stations(lines, outer, 0, 0.1, 0.5);
    var h = stations.Where(s => s.Axis == PositionAxis.Horizontal).ToList();
    var v = stations.Where(s => s.Axis == PositionAxis.Vertical).ToList();
    // 水平：槽两壁 0.05/0.07、台阶 0.15、右边 0.2（总长）；左边是基准不算。
    Equal("0.1,0.14,0.3,0.4", string.Join(",", h.Select(s => Math.Round(s.Value, 6).ToString(invariant))));
    // 竖直：槽底 0.08、台阶 0.07、下边 0（总宽）；上边是基准不算。
    Equal("0.04,0.06,0.2", string.Join(",", v.Select(s => Math.Round(s.Value, 6).ToString(invariant))));
    Equal(9, v.Last().LineIndex);

    var dims = OutlinePlanner.Dimensions(stations, 0, 0.1, 0.02, 0.014);
    Equal(7, dims.Count);
    // 水平一站一层，近的在里：第一层 y = 上 + 0.02，往外每层 6 mm。
    Near(0.1 + 0.02, dims[0].TextAt.Y);
    Near(0.1 + 0.02 + 3 * HolePositionPlanner.TierStep, dims[3].TextAt.Y);
    Near(0.2 / 2, dims[3].TextAt.X);
    Near(-0.014, dims[4].TextAt.X);

    Near(HolePositionPlanner.FirstTier, OutlinePlanner.FirstTier([]));
    Near(HolePositionPlanner.FirstTier, OutlinePlanner.FirstTier([-0.05]));
    Near(0.02 + HolePositionPlanner.TierStep, OutlinePlanner.FirstTier([0.008, 0.02, -0.1]));

    // 组里已有 0.3（孔恰好在台阶那条线上）就不再加。
    Equal("0.1,0.14,0.4", string.Join(",", OutlinePlanner.MissingFromGroup(h, [0.0, 0.3 + 1e-7]).Select(s => s.Value.ToString("0.##", invariant))));
}

static void TestOutlineObsolete()
{
    var lines = Part();
    var outer = OutlinePlanner.OuterLines(lines, []).Select(i => lines[i]).ToList();
    DimensionAnchor E(int index) => DimensionAnchor.Line(lines[index], true);
    ViewDimension[] existing =
    [
        new(0, 2, false, 0.4, [E(0), E(8)]),            // 左边 → 右边：外轮廓线性尺寸，删
        new(1, 2, false, 0.1, [E(0), C(0.05, 0.05)]),   // 左边 → 孔：孔位尺寸，不动
        new(2, 2, false, 0.18, [E(0), E(13)]),          // 左边 → 型腔壁：不是外轮廓，不动
        new(3, 0, true, double.NaN, [C(0.05, 0.05)]),   // 孔标注
        new(4, 1, false, 0, [E(9)]),                    // 只有外轮廓成员的一组坐标尺寸：0 点
        new(5, 1, false, 0.06, [E(7), E(9)]),           //   + 台阶
        new(6, 1, false, 0, [E(1)]),                    // 孔的那组：0 点
        new(7, 1, false, 0.1, [C(0.05, 0.05), E(1)]),   //   + 孔
        new(8, 1, false, 0.04, [E(3), E(1)]),           //   + 槽底
    ];
    Equal("0", string.Join(",", OutlinePlanner.Obsolete(existing, outer)));
    // 普通模式也删外轮廓坐标尺寸；孔那组的 0 点留着。
    Equal("0,5,8,4", string.Join(",", OutlinePlanner.Obsolete(existing, outer, includeOrdinates: true)));
}

// 1.8.1 跨视图去重的样件：长方体 X 0..0.2、Y（高）0..0.1、Z（深）0..0.05，比例 1。
// 主视图：图纸 X = 模型 X、图纸 Y = 模型 Y，模型原点在图纸 (0.1, 0.1)。
static ViewFrame FrontFrame() => new(new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new SheetPoint(0.1, 0.1), 1.0);

// 右视图：图纸 X = 模型 -Z、图纸 Y = 模型 Y，模型原点在图纸 (0.5, 0.1)——深度从右往左长。
static ViewFrame RightFrame() => new(new(0, 0, -1), new(0, 1, 0), new(1, 0, 0), new SheetPoint(0.5, 0.1), 1.0);

// 俯视图：图纸 X = 模型 X、图纸 Y = 模型 -Z，模型原点在图纸 (0.1, 0.5)。
static ViewFrame TopFrame() => new(new(1, 0, 0), new(0, 0, -1), new(0, 1, 0), new SheetPoint(0.1, 0.5), 1.0);

static DimensionAnchor HEdge(double y) => DimensionAnchor.Line(S(0, y, 1, y), true);

static void TestViewFrames()
{
    var front = FrontFrame();
    var right = RightFrame();
    // 主视图上边（图纸 Y 0.2）与右视图上边是同一个面（Y = 0.1）。
    True(front.Plane(PositionAxis.Vertical, 0.2).Same(right.Plane(PositionAxis.Vertical, 0.2)), "主视、右视的上边同面");
    True(!front.Plane(PositionAxis.Vertical, 0.2).Same(front.Plane(PositionAxis.Vertical, 0.1)), "上下边不同面");
    // 方向相反的视图：右视图图纸 X = -Z，X 0.45 是 Z = 0.05；俯视图图纸 Y = -Z，Y 0.45 也是 Z = 0.05。
    var plane = right.Plane(PositionAxis.Horizontal, 0.45);
    Near(1, plane.Direction.Z);
    Near(0.05, plane.Offset);
    True(plane.Same(TopFrame().Plane(PositionAxis.Vertical, 0.45)), "右视、俯视的同一深度面");
    True(!plane.Same(front.Plane(PositionAxis.Horizontal, 0.15)), "方向不同不算同面");

    True(!front.Axonometric && !right.Axonometric && !TopFrame().Axonometric, "三视图不是轴测图");
    var k = 1 / Math.Sqrt(3);
    var iso = new ViewFrame(new(0.707, 0, -0.707), new(-0.408, 0.816, -0.408), new(k, k, k), new SheetPoint(0.5, 0.5), 1.0);
    True(iso.Axonometric, "等轴测是轴测图");
    var trimetric = new ViewFrame(new(1, 0, 0), new(0, 1, 0), new ModelDirection(0.3, 0.5, 0.81).Normalized(), new SheetPoint(0, 0), 1.0);
    True(trimetric.Axonometric, "三等角是轴测图");
    var auxiliary = new ViewFrame(new(0, 1, 0), new(-0.707, 0, 0.707), new(0.707, 0, 0.707), new SheetPoint(0, 0), 1.0);
    True(!auxiliary.Axonometric, "辅助视图（视线垂直于 Y）不是轴测图");
}

static void TestOutlineCoverage()
{
    var front = FrontFrame();
    var right = RightFrame();
    var top = TopFrame();
    var coverage = new OutlineCoverage();
    // 主视图标了总高（上边 → 下边）和台阶（上边 → Y 0.06 的边，图纸 0.16）；还有一个连孔的、一个连孔标注的，不收。
    coverage.AddView(front,
    [
        new(0, 2, false, 0.1, [HEdge(0.2), HEdge(0.1)]),
        new(1, 2, false, 0.04, [HEdge(0.2), HEdge(0.16)]),
        new(2, 2, false, 0.03, [HEdge(0.2), C(0.15, 0.17)]),
        new(3, 0, true, double.NaN, [C(0.15, 0.17)]),
        new(4, 2, false, 0.07, [HEdge(0.2), HEdge(0.16)]),   // 值对不上：不是沿竖直量的，不收
    ]);
    Equal(2, coverage.Dimensions);
    // 右视图：上侧基准图纸 Y 0.2；高度、台阶都已定（主视图标过）——用户的例子。
    True(coverage.Determined(right, PositionAxis.Vertical, 0.2, 0.1), "高度主视图标过");
    True(coverage.Determined(right, PositionAxis.Vertical, 0.2, 0.16), "台阶主视图标过");
    True(!coverage.Determined(right, PositionAxis.Vertical, 0.2, 0.13), "别的高度没标");
    // 深度主视图看不到，右视图要标。
    True(!coverage.Determined(right, PositionAxis.Horizontal, 0.45, 0.5), "深度没标");

    OutlineStation Station(PositionAxis axis, double coordinate, double value) => new(axis, 0, coordinate, value);
    var (remaining, covered) = coverage.Split(right,
        [Station(PositionAxis.Horizontal, 0.5, 0.05), Station(PositionAxis.Vertical, 0.16, 0.04), Station(PositionAxis.Vertical, 0.1, 0.1)],
        left: 0.45, top: 0.2);
    Equal(1, remaining.Count);
    Equal(PositionAxis.Horizontal, remaining[0].Axis);
    Equal(2, covered.Count);

    // 俯视图标了深度（图纸竖直方向，Z = 0 在 Y 0.5、Z = 0.05 在 Y 0.45）：右视图的深度也就定了，方向相反也认。
    coverage.AddView(top, [new(0, 2, false, 0.05, [HEdge(0.5), HEdge(0.45)])]);
    True(coverage.Determined(right, PositionAxis.Horizontal, 0.45, 0.5), "深度俯视图标过");

    // 串起来也算：别的视图标的是「下边 → 台阶」与「上边 → 下边」，本视图从上边量台阶，位置已定。
    var chained = new OutlineCoverage();
    chained.AddView(front, [new(0, 2, false, 0.1, [HEdge(0.2), HEdge(0.1)]), new(1, 2, false, 0.06, [HEdge(0.1), HEdge(0.16)])]);
    True(chained.Determined(right, PositionAxis.Vertical, 0.2, 0.16), "经下边串起来");
    // 坐标尺寸也收：两头 [自己, 0 点]。
    var ordinates = new OutlineCoverage();
    ordinates.AddView(front, [new(0, 1, false, 0, [HEdge(0.2)]), new(1, 1, false, 0.1, [HEdge(0.1), HEdge(0.2)])]);
    True(ordinates.Determined(right, PositionAxis.Vertical, 0.2, 0.1), "坐标尺寸标的高度");
    // 同一个面不用标。
    True(new OutlineCoverage().Determined(right, PositionAxis.Vertical, 0.2, 0.2), "同一个面");
}

static void TestCoveredOrdinates()
{
    var right = RightFrame();
    var coverage = new OutlineCoverage();
    coverage.AddView(FrontFrame(), [new(0, 2, false, 0.1, [HEdge(0.2), HEdge(0.1)])]);
    Func<PositionAxis, double, double, bool> determined = (axis, from, to) => coverage.Determined(right, axis, from, to);
    // 右视图：上边 Y 0.2、下边 Y 0.1、左边 X 0.45、右边 X 0.5、台阶边 Y 0.16。
    SheetSegment[] outer = [S(0.45, 0.2, 0.5, 0.2), S(0.45, 0.1, 0.5, 0.1), S(0.45, 0.1, 0.45, 0.2), S(0.5, 0.1, 0.5, 0.2), S(0.45, 0.16, 0.48, 0.16)];
    DimensionAnchor E(int index) => DimensionAnchor.Line(outer[index], true);
    ViewDimension[] existing =
    [
        new(0, 1, false, 0, [E(0)]),                    // 竖直组 0 点（上边），成员只有总高 → 一起删
        new(1, 1, false, 0.1, [E(1), E(0)]),            //   总高：主视图标过，删
        new(2, 1, false, 0, [E(2)]),                    // 水平组 0 点（左边）
        new(3, 1, false, 0.05, [E(3), E(2)]),           //   深度：没标过，留
        new(4, 2, false, 0.1, [E(0), E(1)]),            // 线性总高（归 Obsolete 管，这里不出）
    ];
    Equal("0,1", string.Join(",", OutlinePlanner.CoveredOrdinates(existing, outer, 1.0, determined)));

    // 组里还有没标过的成员（台阶），0 点留着。
    ViewDimension[] mixed = [.. existing.Take(2), new(5, 1, false, 0.04, [E(4), E(0)])];
    Equal("1", string.Join(",", OutlinePlanner.CoveredOrdinates(mixed, outer, 1.0, determined)));
    // 孔的那组（成员连孔）不动。
    ViewDimension[] holes = [existing[0], new(1, 1, false, 0.03, [C(0.47, 0.17), E(0)])];
    Equal(string.Empty, string.Join(",", OutlinePlanner.CoveredOrdinates(holes, outer, 1.0, determined)));
}

static void TestDimensionCheck()
{
    var lines = Part();
    // 比例 1。两个同种孔 A(0.03,0.06)、B(0.03,0.03) 同一列；两个销孔 D(0.12,0.07)、E(0.13,0.07) 同一行。
    HoleEdge[] edges =
    [
        new(0, 0.03, 0.06, 0.003, "/Cut"),
        new(1, 0.03, 0.03, 0.003, "/Cut"),
        new(2, 0.12, 0.07, 0.002, "/销孔", Dowel: true),
        new(3, 0.13, 0.07, 0.002, "/销孔", Dowel: true),
    ];
    var left = DimensionAnchor.Line(lines[0], true);
    var top = DimensionAnchor.Line(lines[1], true);
    ViewDimension[] existing =
    [
        new(0, 0, true, double.NaN, [C(0.03, 0.06)]),                         // 孔标注（普通孔）
        new(1, 0, true, double.NaN, [C(0.12, 0.07)]),                         // 孔标注（销孔，带 H7）
        new(2, 2, false, 0.03, [left, C(0.03, 0.06)]),                        // 左边 → A 水平（B 同一列也算）
        new(3, 2, false, 0.04, [top, C(0.03, 0.06)]),                         // 上边 → A 竖直
        new(4, 2, false, 0.03, [C(0.03, 0.06), C(0.03, 0.03)]),               // A → B 竖直
        new(5, 2, false, 0.12, [left, C(0.12, 0.07)]),                        // 左边 → D 水平
        new(6, 2, false, 0.01, [C(0.12, 0.07), C(0.13, 0.07)]),               // D → E 水平（没有公差）
        new(7, 2, false, 0.2, [left, DimensionAnchor.Line(lines[8], true)]),  // 总长
    ];
    var check = DimensionCheckPlanner.Check(edges, lines, [], existing, new HashSet<int> { 1 }, 1.0);
    var texts = check.Issues.Select(issue => issue.Text).ToList();
    var all = string.Join(" | ", texts);
    Equal(4, check.HoleCount);
    // D、E 缺竖直位置；水平都定位了（E 由 D→E 那个尺寸定位）。
    True(texts.Count(t => t.Contains("缺竖直位置尺寸", StringComparison.Ordinal)) == 2, all);
    True(!texts.Any(t => t.Contains("缺水平", StringComparison.Ordinal)), all);
    True(texts.Any(t => t.Contains("没有 ±0.02", StringComparison.Ordinal)), "D→E 没公差：" + all);
    True(!texts.Any(t => t.Contains("没有孔标注", StringComparison.Ordinal)), "两种孔都有孔标注：" + all);
    True(!texts.Any(t => t.Contains("没有 H7", StringComparison.Ordinal)), "销孔孔标注已带 H7：" + all);
    // 外轮廓 7 站，只标了总长 → 6 站本视图没有。
    Equal(7, check.StationCount);
    Equal(6, check.Outline.Count);
    True(DimensionCheckPlanner.DescribeStation(check.Outline[0]).Contains("左起 50", StringComparison.Ordinal), DimensionCheckPlanner.DescribeStation(check.Outline[0]));

    // 阵列标法跨度里的孔算定位：前缀「4 x 10 =」，跨 0.02→0.06，中间 0.04 的孔不缺。
    ViewDimension[] pattern = [new(0, 2, false, 0.04, [C(0.02, 0.05), C(0.06, 0.05)], Prefix: "4 x 10 =")];
    True(DimensionCheckPlanner.Located(pattern, PositionAxis.Horizontal, 0.04, 1.0), "阵列跨度里");
    True(!DimensionCheckPlanner.Located(pattern, PositionAxis.Vertical, 0.05, 1.0), "阵列尺寸不管竖直");
    // 不带 H7 的销孔孔标注、没有孔标注的种都报出来。
    var bare = DimensionCheckPlanner.Check(edges, lines, [], existing.Skip(1).ToList(), new HashSet<int>(), 1.0);
    True(bare.Issues.Any(issue => issue.Text.Contains("没有 H7", StringComparison.Ordinal)), "没有 H7");
    True(bare.Issues.Any(issue => issue.Text.Contains("没有孔标注", StringComparison.Ordinal) && issue.EdgeIndex == 0), "普通孔那种没有孔标注");
}

static void TestDistinctHoles()
{
    // 相距 0.1 mm 的两个孔不能被当成同心。
    var plan = HoleCalloutPlanner.Plan(
        [new HoleEdge(0, 0.1, 0.1, 0.001), new HoleEdge(1, 0.1001, 0.1, 0.001)],
        []);
    Equal(2, plan.HoleCount);
}

static void TestTargetOrder()
{
    var plan = HoleCalloutPlanner.Plan(
        [
            new HoleEdge(0, 0.20, 0.05, 0.002, "A"),
            new HoleEdge(1, 0.10, 0.05, 0.002, "B"),
            new HoleEdge(2, 0.30, 0.15, 0.002, "C"),
        ],
        []);
    Equal("2,1,0", string.Join(",", plan.Targets.Select(target => target.EdgeIndex)));
}

static void TestPlacement()
{
    var hole = new HoleEdge(0, 0.1, 0.2, 0.004);
    var placement = HoleCalloutPlanner.Placement(hole);
    var distance = Math.Sqrt(Math.Pow(placement.X - hole.X, 2) + Math.Pow(placement.Y - hole.Y, 2));
    Near(hole.Radius + HoleCalloutPlanner.LeaderReach, distance);
    True(placement.X < hole.X && placement.Y > hole.Y, "引线折点在左上方");
    Near(placement.Y - hole.Y, hole.X - placement.X);
}

static void TestShoulderEnd()
{
    // 真机移动底板上用户摆好的「6× M6」孔标注的显示数据（米）：箭头、引线、箭头根、下划线。
    SheetSegment[] lines =
    [
        new(0.0916, 0.1988, 0.0933, 0.1958),
        new(0.0891, 0.2031, 0.0825, 0.2146),
        new(0.0916, 0.1988, 0.0891, 0.2031),
        new(0.0825, 0.2146, 0.0483, 0.2146),
    ];
    var end = HoleCalloutPlanner.ShoulderEnd(lines);
    True(end is not null, "应找到下划线");
    Near(0.0825, end!.Value.X);
    Near(0.2146, end.Value.Y);
    True(HoleCalloutPlanner.ShoulderEnd([new(0, 0, 0.01, 0.01)]) is null, "没有水平线时返回 null");
    // 文字在折点右边（1.6.0 避障换到右侧角位）：折点是下划线左端。
    Near(0.0483, HoleCalloutPlanner.ShoulderEnd(lines, textLeft: false)!.Value.X);
}

static void TestFacesViewer()
{
    True(HoleCalloutPlanner.FacesViewer(0, 0, 1), "正对");
    True(HoleCalloutPlanner.FacesViewer(0, 0, -3), "反向也算正对，长度不必归一");
    True(!HoleCalloutPlanner.FacesViewer(1, 0, 0), "侧视的孔不标");
    True(!HoleCalloutPlanner.FacesViewer(0.1, 0, 1), "斜着约 6° 不算正对");
    True(!HoleCalloutPlanner.FacesViewer(0, 0, 0), "零向量不算");
}

static void TestHoleWall()
{
    // 真机标定（SW 2025 SP5，WTJYQ-03-03 移动底板）：孔壁曲面法向背离轴线（点积 > 0），FaceInSurfaceSense = true。
    True(HoleCalloutPlanner.IsHoleWall(0.0025, 0.0025, faceInSurfaceSense: true, surfaceNormalDotRadial: 1), "实测的孔壁必须认成孔");
    True(!HoleCalloutPlanner.IsHoleWall(0.0025, 0.0025, faceInSurfaceSense: false, surfaceNormalDotRadial: 1), "面法向背离轴线的是凸台/轴");
    // 曲面法向若朝轴线，换算后结论跟着翻，不依赖圆柱曲面法向的朝向约定。
    True(HoleCalloutPlanner.IsHoleWall(0.0025, 0.0025, faceInSurfaceSense: false, surfaceNormalDotRadial: -1), "曲面法向朝轴线且同向：孔");
    True(!HoleCalloutPlanner.IsHoleWall(0.0025, 0.0025, faceInSurfaceSense: true, surfaceNormalDotRadial: -1), "曲面法向朝轴线且相反：凸台");
    True(!HoleCalloutPlanner.IsHoleWall(0.003, 0.0035, faceInSurfaceSense: true, surfaceNormalDotRadial: 1), "半径不同的圆柱面不是这个圆的");
}

static void TestPerpendicular()
{
    foreach (var (x, y, z) in new[] { (0.0, 0.0, -1.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.6, 0.0, 0.8) })
    {
        var (ux, uy, uz) = HoleCalloutPlanner.Perpendicular(x, y, z);
        Near(0, ux * x + uy * y + uz * z);
        Near(1, Math.Sqrt(ux * ux + uy * uy + uz * uz));
    }
}

// ---- 1.9.0 出图 ----

/// <summary>用户 2026-025 台面2机器 15 个零件：几何摘要（真机 PartScan 读出、压缩存放）与用户手工图的选择。</summary>
static List<(string Name, string Main, string Sheet, int Scale, PartGeometry Geometry)> RealParts()
{
    var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "台面2机器零件.json");
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    ModelDirection Vector(JsonElement v) => new(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble());
    var parts = new List<(string, string, string, int, PartGeometry)>();
    foreach (var part in document.RootElement.EnumerateArray())
    {
        var box = part.GetProperty("box").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var cylinders = part.GetProperty("cylinders").EnumerateArray()
            .Select(c => new PartCylinder(
                Vector(c.GetProperty("a")),
                Vector(c.GetProperty("p")),
                c.GetProperty("r").GetDouble(),
                c.GetProperty("c").GetBoolean(),
                c.GetProperty("f").GetBoolean(),
                c.GetProperty("k").GetString()!,
                c.GetProperty("o").EnumerateArray().Select(Vector).ToList()))
            .ToList();
        var planes = part.GetProperty("planes").EnumerateArray().Select(Vector).ToList();
        var windows = part.TryGetProperty("windows", out var found) ? found.EnumerateArray().Select(Vector).ToList() : [];
        // 1.10.0：平面倒角 [轴 x, y, z, 直角边 a, b]。
        var chamfers = part.TryGetProperty("chamfers", out var faces)
            ? faces.EnumerateArray().Select(c => new PartChamfer(new ModelDirection(c[0].GetDouble(), c[1].GetDouble(), c[2].GetDouble()), c[3].GetDouble(), c[4].GetDouble())).ToList()
            : [];
        parts.Add((part.GetProperty("name").GetString()!, part.GetProperty("main").GetString()!, part.GetProperty("sheet").GetString()!,
            part.GetProperty("scale").GetInt32(), new PartGeometry(new ModelBox(box[0], box[1], box[2], box[3], box[4], box[5]), cylinders, planes, windows, chamfers)));
    }

    return parts;
}

/// <summary>用户的「零件 - 高悦精密」模板（A4 横、A4 竖、A3、A2）与默认的装配体模板，排好序。</summary>
static IReadOnlyList<DrawingTemplate> UserTemplates()
    => DrawingTemplate.Rank(new[]
    {
        "零件 - 高悦精密A3.drwdot", "零件 - 高悦精密A4横向.drwdot", "零件 - 高悦精密A4纵向.drwdot", "零件 - 高悦精密A2.drwdot",
        "装配体 - 高悦精密A3.drwdot", "gb_a3.drwdot",
    }.Select(name => DrawingTemplate.FromFile(@"C:\templates\" + name)!));

static void TestDrawingTemplates()
{
    var a3 = DrawingTemplate.FromFile(@"C:\t\零件 - 高悦精密A3.drwdot")!;
    Equal(0.420, a3.Width);
    Equal(0.297, a3.Height);
    Equal("A3", a3.SizeName);
    var landscape = DrawingTemplate.FromFile(@"C:\t\零件 - 高悦精密A4横向.drwdot")!;
    Equal(0.297, landscape.Width);
    Equal("A4 横", landscape.SizeName);
    var portrait = DrawingTemplate.FromFile(@"C:\t\零件 - 高悦精密A4纵向.drwdot")!;
    Equal(0.210, portrait.Width);
    Equal("A4 竖", portrait.SizeName);
    True(DrawingTemplate.FromFile(@"C:\t\gb_a4.drwdot")!.Width < 0.25, "名字不写横竖的 A4 按竖放");
    True(DrawingTemplate.FromFile(@"C:\t\gb_a2.drwdot")!.Width > 0.5, "A3 以上不写横竖按横放");
    True(DrawingTemplate.FromFile(@"C:\t\标准模板.drwdot") is null, "认不出图幅的不算");
    True(DrawingTemplate.FromFile(@"C:\t\A10 图框.drwdot") is null, "A10 不是图幅");

    // 零件图只用名字带「零件」的；有横放的就只用横放的；图幅从小到大。
    var ranked = UserTemplates();
    Equal("零件 - 高悦精密A4横向,零件 - 高悦精密A3,零件 - 高悦精密A2", string.Join(",", ranked.Select(t => t.Name)));
    // 没有「零件」模板时退到不带「装配」的。
    var fallback = DrawingTemplate.Rank(new[] { "装配体 - A3.drwdot", "gb_a3.drwdot", "gb_a4.drwdot" }.Select(n => DrawingTemplate.FromFile(@"C:\t\" + n)!));
    Equal("gb_a3", string.Join(",", fallback.Select(t => t.Name)));
}

static void TestDrawingMainView()
{
    // 用户 15 张手工图的主视图。差异只在两处（外加两处同轴另一面）：
    // 外壳2 用户看的是投影面积最大的上视、孔却在侧面，本模块取孔与圆弧最多的那一面；连接件、外壳沿轴两面都没有孔口，取了正向的那一面。
    var accepted = new Dictionary<string, string[]>
    {
        ["XLL-HDYY-01-04-02 连接件"] = ["back", "front"],
        ["XLL-HDYY-01-04-09 外壳2"] = ["top", "right", "left"],
        ["XLL-HDYY-01-04-10 外壳"] = ["left", "right"],
    };
    var matched = 0;
    foreach (var (name, main, _, _, geometry) in RealParts())
    {
        var chosen = DrawingPlanner.MainView(geometry).View.Key;
        var ok = accepted.TryGetValue(name, out var allowed) ? allowed.Contains(chosen) : chosen == main;
        True(ok, $"{name}：主视图应为 {main}，实际 {chosen}");
        if (chosen == main)
            matched++;
    }

    True(matched >= 12, $"与手工图完全一致的主视图应至少 12 张，实际 {matched}");

    // 板件：孔最多的一面，沉头孔朝哪面看哪面。
    var plate = Plate(holesOpenTo: new ModelDirection(0, 0, -1));
    Equal("back", DrawingPlanner.MainView(plate).View.Key);
    // L 形件：截面上 3 个圆角（沿 Z），两条腿侧面各 2 个孔（沿 X、沿 Y）——看截面。
    Equal("front", DrawingPlanner.MainView(Bracket()).View.Key);
}

static void TestDrawingSheet()
{
    var templates = UserTemplates();
    var verbose = Environment.GetEnvironmentVariable("STRENUA_PLAN") == "1";
    var matched = 0;
    var lines = new List<string>();
    foreach (var (name, main, sheet, scale, geometry) in RealParts())
    {
        var chosen = DrawingPlanner.MainView(geometry).View;
        var frame = new ViewFrame(chosen.Right, chosen.Up, chosen.Normal, new SheetPoint(0, 0), 1);
        var sides = DrawingPlanner.SideViews(geometry, frame, firstAngle: true);
        var choice = DrawingPlanner.ChooseSheet(geometry, chosen, sides, templates, chain: false);
        True(choice is not null, $"{name}：哪个模板都放不下");
        var size = choice!.Template.SizeName.Split(' ')[0];
        var denominator = (int)Math.Round(1 / choice.Scale);
        if (size == sheet && denominator == scale)
            matched++;
        lines.Add($"{name}: 用户 {sheet} 1:{scale}，本次 {choice.Template.SizeName} {DrawingPlanner.ScaleText(choice.Scale)}（{chosen.Key}，{string.Join("/", choice.Slots)}）{choice.Reason}"
            + $"；轴测 ×{choice.Layout.IsoShrink:0.##}{(choice.Layout.Problem.Length > 0 ? "；" + choice.Layout.Problem : string.Empty)}");
        if (verbose)
        {
            var userTemplate = templates.First(t => t.SizeName.StartsWith(sheet, StringComparison.Ordinal));
            var request = DrawingPlanner.Estimate(geometry, chosen, choice.Slots, 1.0 / scale, chain: false);
            var layout = DrawingPlanner.Layout(SheetSpace.Standard(userTemplate.Width, userTemplate.Height), request);
            lines.Add($"    用户那张 {sheet} 1:{scale}：{(layout.Fits ? "放得下" : layout.Problem)}；主视图 {request.Main.Width * 1000:0}×{request.Main.Height * 1000:0} 留 {request.Main.MarginLeft * 1000:0}，"
                + string.Join("，", request.Sides.Select(p => $"{p.Key} {p.Value.Width * 1000:0}×{p.Value.Height * 1000:0} 留 {p.Value.MarginLeft * 1000:0}"))
                + $"，轴测 {request.Iso.Width * 1000:0}×{request.Iso.Height * 1000:0}");
        }
        // 每张都要真的排得下：视图组、轴测图、技术要求互不重叠、都在图框里、不压标题栏。
        True(choice.Layout.Fits, $"{name}：选出来的版面排不下 {choice.Layout.Problem}");
        // 1.10.0：技术要求都要有空地（挤不下时轴测图再缩一档；三个限位块多了倒角的下视图后原比例轴测图占了它的地方）。
        True(choice.Layout.Problem.Length == 0, $"{name}：{choice.Layout.Problem}");
    }

    if (verbose)
        Console.WriteLine(string.Join(Environment.NewLine, lines));
    // 1.10.0：投影视图在与主视图共用的方向上没有孔就不留尺寸空间、视图外框后面那 4 mm 可以压图框线，外壳2 也对上了（A4 1:5），只剩后板 A2。
    True(matched >= 14, $"图幅与比例和手工图一致的应至少 14 张，实际 {matched}：" + Environment.NewLine + string.Join(Environment.NewLine, lines));
}

static void TestDrawingSideViews()
{
    // 板件：侧面没有孔也没有圆弧面——只加一个看厚度的，宽板摆下边，也可换到右边。
    var plate = Plate(new ModelDirection(0, 0, -1));
    var back = StandardView.Of("back");
    var plateSides = DrawingPlanner.SideViews(plate, Frame(back), firstAngle: true);
    Equal(1, plateSides.Count);
    Equal(ViewSlot.Below, plateSides[0].Slot);
    Equal(ViewSlot.Right, plateSides[0].Alternative!.Value);
    Equal("Below|Right", string.Join("|", DrawingPlanner.Arrangements(plateSides).Select(slots => string.Join(",", slots))));

    // L 形件看截面：竖腿的孔沿图纸 X、孔口朝 −X——第一角投影里摆右边的视图看的正是零件左侧（−X）；
    // 横腿的孔沿图纸 Y、两头都通——摆下边（看零件上面）。第三角投影：右边看 +X，于是摆左边。
    var front = StandardView.Of("front");
    var first = DrawingPlanner.SideViews(Bracket(), Frame(front), firstAngle: true);
    Equal("Right,Below", string.Join(",", first.Select(side => side.Slot)));
    True(first.All(side => side.Alternative is null), "看孔的投影视图不能换边（换了就看不到孔口那一面）");
    var third = DrawingPlanner.SideViews(Bracket(), Frame(front), firstAngle: false);
    Equal("Left,Below", string.Join(",", third.Select(side => side.Slot)));

    // 侧面只有圆弧面：只加一个，加在圆弧面多的方向；两个方向都有时可换边（用户外壳只加了一个右视图）。
    var rounded = new PartGeometry(new ModelBox(0, 0, 0, 0.2, 0.1, 0.05),
    [
        new(new ModelDirection(1, 0, 0), new ModelDirection(0, 0.01, 0.01), 0.005, false, false, "Fillet1", []),
        new(new ModelDirection(1, 0, 0), new ModelDirection(0, 0.09, 0.01), 0.005, false, false, "Fillet1", []),
        new(new ModelDirection(0, 1, 0), new ModelDirection(0.01, 0, 0.01), 0.005, false, false, "Fillet2", []),
        new(new ModelDirection(0, 1, 0), new ModelDirection(0.19, 0, 0.01), 0.0008, false, false, "Fillet3", []),
    ], []);
    var arcs = DrawingPlanner.SideViews(rounded, Frame(front), firstAngle: true);
    Equal(1, arcs.Count);
    Equal(ViewSlot.Right, arcs[0].Slot);
    Equal(ViewSlot.Below, arcs[0].Alternative!.Value);
    True(arcs[0].Reason.Contains("圆弧面 2 张", StringComparison.Ordinal), "R1 以下的修边圆角不算：" + arcs[0].Reason);
}

static ViewFrame Frame(StandardView view) => new(view.Right, view.Up, view.Normal, new SheetPoint(0, 0), 1);

static void TestDrawingLayout()
{
    var a3 = SheetSpace.Standard(0.420, 0.297);
    var request = new LayoutRequest(
        new ViewBox(0.150, 0.100, 0.040, 0.040),
        new Dictionary<ViewSlot, ViewBox> { [ViewSlot.Right] = new(0.020, 0.100, 0.016, 0.016) },
        new ViewBox(0.080, 0.080, 0, 0),
        0.101, 0.042, [1.0, 0.5]);
    var layout = DrawingPlanner.Layout(a3, request);
    True(layout.Fits && layout.Problem.Length == 0, "A3 上一个 150×100 的主视图加右视图、轴测图、技术要求应排得开：" + layout.Problem);
    var main = SheetRect.Around(layout.Main, 0.150, 0.100);
    var right = SheetRect.Around(layout.Sides[ViewSlot.Right], 0.020, 0.100);
    var iso = SheetRect.Around(layout.Iso!.Value, 0.080 * layout.IsoShrink, 0.080 * layout.IsoShrink);
    var note = new SheetRect(layout.NoteTopLeft!.Value.X, layout.NoteTopLeft.Value.Y - 0.042, layout.NoteTopLeft.Value.X + 0.101, layout.NoteTopLeft.Value.Y);
    Near(layout.Main.Y, layout.Sides[ViewSlot.Right].Y);
    True(right.Left - main.Right >= 0.016, "右视图与主视图之间要给右视图的竖直尺寸留地方");
    True(main.Left - a3.Frame.Left >= 0.040 && a3.Frame.Top - main.Top >= 0.040, "主视图左边、上边留标注空间");
    foreach (var (name, rect) in new[] { ("主视图", main), ("右视图", right), ("轴测图", iso), ("技术要求", note) })
    {
        True(rect.Within(a3.Frame), $"{name}出了图框");
        True(!a3.KeepOuts.Any(rect.Overlaps), $"{name}压到标题栏等");
    }

    True(!iso.Overlaps(main) && !iso.Overlaps(right) && !note.Overlaps(iso) && !note.Overlaps(main), "轴测图、技术要求不压视图");
    True(note.Bottom >= a3.TitleBlock.Top && note.Right <= a3.TitleBlock.Right, "技术要求先放标题栏正上方");

    // 最左边是左视图、它上边的尺寸条会压到左上角图号框：整组往下（或往右）挪开，而不是判放不下（首版真机就栽在这）。
    var left = new LayoutRequest(
        new ViewBox(0.100, 0.060, 0.016, 0.016),
        new Dictionary<ViewSlot, ViewBox> { [ViewSlot.Left] = new(0.030, 0.060, 0.016, 0.016) },
        new ViewBox(0.050, 0.050, 0, 0), 0.101, 0.042, [1.0]);
    var shifted = DrawingPlanner.Layout(a3, left);
    True(shifted.Fits, "左视图压图号框时应挪开：" + shifted.Problem);
    var leftView = SheetRect.Around(shifted.Sides[ViewSlot.Left], 0.030, 0.060);
    var strip = new SheetRect(leftView.Left, leftView.Top, leftView.Right, leftView.Top + 0.016);
    True(!a3.KeepOuts.Any(strip.Overlaps), "左视图上边的尺寸条不能压图号框");

    // 主视图本身就比图框大：放不下，说清原因。
    var huge = DrawingPlanner.Layout(a3, request with { Main = new ViewBox(0.400, 0.260, 0.040, 0.040) });
    True(!huge.Fits && huge.Problem.Length > 0, "放不下要说原因");

    // 视图组放得下、技术要求找不到完全空的地方：照样算排得下，技术要求放在压得最少处并说明。
    var crowded = DrawingPlanner.Layout(a3, request with { NoteWidth = 0.380, NoteHeight = 0.100 });
    True(crowded.Fits && crowded.NoteTopLeft is not null && crowded.Problem.Contains("技术要求", StringComparison.Ordinal), "技术要求挤不下时放在压得最少处：" + crowded.Problem);
}

static void TestDrawingScales()
{
    Equal("1:5", DrawingPlanner.ScaleText(0.2));
    Equal("1:3", DrawingPlanner.ScaleText(1.0 / 3));
    Equal("2:1", DrawingPlanner.ScaleText(2));
    Equal((1, 3), DrawingPlanner.ScaleRatio(1.0 / 3));
    Equal((2, 1), DrawingPlanner.ScaleRatio(2));
    var plate = Plate(new ModelDirection(0, 0, 1));
    Equal(1.0, DrawingPlanner.Scales(plate)[0]);
    var small = new PartGeometry(new ModelBox(0, 0, 0, 0.03, 0.02, 0.01), [], []);
    Equal(2.0, DrawingPlanner.Scales(small)[0]);
    var tiny = new PartGeometry(new ModelBox(0, 0, 0, 0.012, 0.01, 0.005), [], []);
    Equal(5.0, DrawingPlanner.Scales(tiny)[0]);
    // 轴测图可缩成：原比例、再往下两档（1:2 → 1:3、1:5）。
    Equal("1,0.667,0.4", string.Join(",", DrawingPlanner.IsoFactors(plate, 0.5).Select(f => f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))));
    // 尺寸空间：一种孔一层；尺寸链模式也按种数留（孔标注往左上引出），只是最少 26 mm。
    Near(0.034, DrawingPlanner.AnnotationMargin(3, chain: false));
    Near(0.034, DrawingPlanner.AnnotationMargin(3, chain: true));
    Near(0.026, DrawingPlanner.AnnotationMargin(0, chain: true));
    Near(0.016, DrawingPlanner.AnnotationMargin(0, chain: false));
    Near(0.080, DrawingPlanner.AnnotationMargin(20, chain: false));
    // 标准视图的朝向（真机读回）：右手系、图纸 X × 图纸 Y = 朝看图的人。
    foreach (var view in StandardView.All)
    {
        var cross = PartHole.Cross(view.Right, view.Up);
        Near(1, cross.Dot(view.Normal));
    }
}

static void TestTechnicalNote()
{
    var (text, source) = DrawingNote.TechnicalNoteText(null);
    Equal(DrawingNote.DefaultTechnicalNote, text);
    True(text.StartsWith("        技术要求\n", StringComparison.Ordinal) && text.Contains("8、未注尺寸参考3D数模。", StringComparison.Ordinal), "默认就是用户那 8 条");
    True(source.Contains("默认", StringComparison.Ordinal), source);

    var directory = Path.Combine(Path.GetTempPath(), "strenua-test-" + Guid.NewGuid().ToString("N"));
    try
    {
        var (first, firstSource) = DrawingNote.TechnicalNoteText(directory);
        Equal(DrawingNote.DefaultTechnicalNote, first);
        var file = Path.Combine(directory, DrawingNote.TechnicalNoteFile);
        True(File.Exists(file) && firstSource.Contains("已写到", StringComparison.Ordinal), "第一次用时把默认内容写出来，方便用户改");
        File.WriteAllText(file, "技术要求\r\n1、去毛刺。\r\n", new System.Text.UTF8Encoding(false));
        var (custom, customSource) = DrawingNote.TechnicalNoteText(directory);
        Equal("技术要求\n1、去毛刺。", custom);
        True(customSource.Contains("取自", StringComparison.Ordinal), customSource);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestFilletPlan()
{
    FilletArc Arc(int index, double cx, double cy, double radius, double mx, double my, bool concave)
        => new(index, new SheetPoint(cx, cy), radius * 0.5, radius, new SheetPoint(mx, my), concave);

    var arcs = new List<FilletArc>
    {
        Arc(0, 0.050, 0.100, 0.005, 0.0482, 0.1018, false),   // R5 外圆角（左上）
        Arc(1, 0.150, 0.020, 0.005, 0.1518, 0.0182, false),   // R5 外圆角（右下）
        Arc(2, 0.100, 0.060, 0.010, 0.0965, 0.0565, true),    // R10 内圆角
        Arc(3, 0.100, 0.060, 0.010, 0.0965, 0.0565, true),    // 同一个 R10 被切成两段
        Arc(4, 0.120, 0.120, 0.001, 0.1203, 0.1203, false),   // R1：技术要求不标
    };
    var plan = FilletPlanner.Plan(arcs, [], []);
    Equal(4, plan.ArcCount);
    Equal(1, plan.DefaultCount);
    Equal("0,2,1", string.Join(",", plan.Targets.Select(t => t.Index)));
    True(plan.Targets.All(t => t.Count == 1 && t.Prefix.Length == 0), "两个 R5 不到 3 个，各标各的");

    // 已有 R 尺寸的跳过。
    var existing = FilletPlanner.Plan(arcs, [(new SheetPoint(0.050, 0.100), 0.0025)], []);
    Equal(1, existing.Dimensioned);
    True(existing.Targets.All(t => t.Index != 0), "已标的不再标");

    // 同半径 3 个以上合标「N x R」，标在最靠左上的那个上。
    var corners = Enumerable.Range(0, 4)
        .Select(i => Arc(i, 0.02 + 0.1 * (i % 2), 0.02 + 0.1 * (i / 2), 0.003, 0.02 + 0.1 * (i % 2) - 0.001, 0.02 + 0.1 * (i / 2) + 0.001, false))
        .ToList();
    var grouped = FilletPlanner.Plan(corners, [], []);
    Equal(1, grouped.Targets.Count);
    Equal(4, grouped.Targets[0].Count);
    Equal("4 x ", grouped.Targets[0].Prefix);
    Equal(2, grouped.Targets[0].Index);

    // 文字放在空处：外圆角往弧外，内圆角往圆心那边。
    var (ox, oy) = FilletPlanner.Outward(Arc(0, 0, 0, 0.004, 0.002, 0, false));
    True(ox > 0.99 && Math.Abs(oy) < 1e-9, "外圆角文字朝弧外");
    (ox, oy) = FilletPlanner.Outward(Arc(0, 0, 0, 0.004, 0.002, 0, true));
    True(ox < -0.99 && Math.Abs(oy) < 1e-9, "内圆角文字朝圆心那边");

    // 正方向压线就转开。
    var arc = Arc(0, 0.100, 0.100, 0.004, 0.102, 0.100, false);
    var blocked = FilletPlanner.Plan([arc], [], [new SheetSegment(0.110, 0.090, 0.110, 0.110)]);
    var at = blocked.Targets[0].TextAt;
    var box = new TextBox(at.X - 0.0026, at.Y - 0.00175, 0.0052, 0.0035);
    True(!ClearancePlanner.Hits(box, new SheetSegment(0.110, 0.090, 0.110, 0.110)), "R 文字不压线");
    Equal("5", FilletPlanner.Value(0.005));
    Equal("5.5", FilletPlanner.Value(0.0055));
}

static void TestDrawingSlotOf()
{
    // 第一角投影（国标）：看零件左侧（视线 −X）的摆右边、看右侧的摆左边、看上面（+Y）的摆下边、看下面的摆上边；第三角反过来。
    foreach (var view in StandardView.All)
    {
        var frame = Frame(view);
        ModelDirection Minus(ModelDirection d) => new(-d.X, -d.Y, -d.Z);
        Equal(ViewSlot.Right, DrawingPlanner.SlotOf(frame, Minus(view.Right), firstAngle: true)!.Value);
        Equal(ViewSlot.Left, DrawingPlanner.SlotOf(frame, view.Right, firstAngle: true)!.Value);
        Equal(ViewSlot.Below, DrawingPlanner.SlotOf(frame, view.Up, firstAngle: true)!.Value);
        Equal(ViewSlot.Above, DrawingPlanner.SlotOf(frame, Minus(view.Up), firstAngle: true)!.Value);
        Equal(ViewSlot.Left, DrawingPlanner.SlotOf(frame, Minus(view.Right), firstAngle: false)!.Value);
        Equal(ViewSlot.Above, DrawingPlanner.SlotOf(frame, view.Up, firstAngle: false)!.Value);
        // 同轴（前后视）、斜的（轴测图）不是投影位。
        True(DrawingPlanner.SlotOf(frame, view.Normal, firstAngle: true) is null, "主视图同向的不是投影位");
        True(DrawingPlanner.SlotOf(frame, Minus(view.Normal), firstAngle: true) is null, "主视图反向（后视）不是投影位");
        var (isoRight, _) = DrawingPlanner.IsoAxes(view.Normal, view.Right, view.Up);
        True(DrawingPlanner.SlotOf(frame, PartHole.Cross(isoRight, view.Up).Normalized(), firstAngle: true) is null, "斜的不是投影位");
    }

    // 与「要哪几个投影视图」同一套：L 形件第一角摆右边的那个，看的正是孔口那一面（−X）。
    var front = StandardView.Of("front");
    var sides = DrawingPlanner.SideViews(Bracket(), Frame(front), firstAngle: true);
    Equal(ViewSlot.Right, sides[0].Slot);
    Equal(ViewSlot.Right, DrawingPlanner.SlotOf(Frame(front), new ModelDirection(-1, 0, 0), firstAngle: true)!.Value);
    True(DrawingPlanner.Horizontal(ViewSlot.Left) && !DrawingPlanner.Horizontal(ViewSlot.Below), "左右视图看横向，上下视图看竖向");
}

static void TestDrawingBeside()
{
    // 单独加投影视图时贴着主视图放：间距（Gap + 该留的标注空间）与整页排版排出来的一样。
    var a3 = SheetSpace.Standard(0.420, 0.297);
    var mainBox = new ViewBox(0.120, 0.080, 0.030, 0.026);
    foreach (var slot in new[] { ViewSlot.Right, ViewSlot.Left, ViewSlot.Below, ViewSlot.Above })
    {
        var side = DrawingPlanner.Horizontal(slot) ? new ViewBox(0.020, 0.080, 0.016, 0.018) : new ViewBox(0.120, 0.020, 0.022, 0.016);
        var layout = DrawingPlanner.Layout(a3, new LayoutRequest(mainBox, new Dictionary<ViewSlot, ViewBox> { [slot] = side }, new ViewBox(0, 0, 0, 0), 0, 0));
        True(layout.Fits, $"{slot}：{layout.Problem}");
        var main = SheetRect.Around(layout.Main, mainBox.Width, mainBox.Height);
        var beside = DrawingPlanner.Beside(main, mainBox, slot, side);
        Near(layout.Sides[slot].X, beside.X);
        Near(layout.Sides[slot].Y, beside.Y);
    }
}

static void TestDrawingChooseSlots()
{
    // 「投影视图」单独按时，在当前图幅与比例下挑可换边视图的边——与「新建工程图」选图幅时估的那种摆法一致（15 个真零件）。
    var templates = UserTemplates();
    foreach (var (name, _, _, _, geometry) in RealParts())
    {
        var chosen = DrawingPlanner.MainView(geometry).View;
        var sides = DrawingPlanner.SideViews(geometry, Frame(chosen), firstAngle: true);
        var choice = DrawingPlanner.ChooseSheet(geometry, chosen, sides, templates, chain: false)!;
        var slots = DrawingPlanner.ChooseSlots(geometry, DrawingPlanner.ViewOf(Frame(chosen)), sides,
            SheetSpace.Standard(choice.Template.Width, choice.Template.Height), choice.Scale, chain: false);
        Equal(string.Join(",", choice.Slots), string.Join(",", slots));
    }

    // 宽板只看厚度：首选摆下边，下边那条压到 A3 的标题栏，换到右边（整组往下挪一点躲开修改栏）放得下。
    var wide = new PartGeometry(new ModelBox(0, 0, 0, 0.20, 0.19, 0.01), [], []);
    var front = StandardView.Of("front");
    var wideSides = DrawingPlanner.SideViews(wide, Frame(front), firstAngle: true);
    Equal(ViewSlot.Below, wideSides[0].Slot);
    Equal(ViewSlot.Right, DrawingPlanner.ChooseSlots(wide, front, wideSides, SheetSpace.Standard(0.420, 0.297), 1, chain: false)[0]);

    // 哪种摆法都放不下：用首选。
    var plate = Plate(new ModelDirection(0, 0, -1));
    var back = StandardView.Of("back");
    var plateSides = DrawingPlanner.SideViews(plate, Frame(back), firstAngle: true);
    var tiny = DrawingPlanner.ChooseSlots(plate, back, plateSides, SheetSpace.Standard(0.297, 0.210), 1, chain: false);
    Equal(plateSides[0].Slot, tiny[0]);
}

static void TestDrawingSpots()
{
    var a3 = SheetSpace.Standard(0.420, 0.297);
    var main = DrawingPlanner.Occupied(new SheetPoint(0.120, 0.180), new ViewBox(0.150, 0.100, 0.030, 0.030)).ToList();
    // 轴测图：原比例放得下就不缩，躲开视图与标题栏。
    var (center, shrink, free) = DrawingPlanner.IsoSpot(a3, main, new ViewBox(0.080, 0.070, 0, 0), [1.0, 0.5], new SheetPoint(0.330, 0.180));
    var iso = SheetRect.Around(center, 0.080, 0.070);
    True(free && shrink == 1 && iso.Within(a3.Frame) && !main.Any(iso.Overlaps) && !a3.KeepOuts.Any(iso.Overlaps), "轴测图应放进空地");
    // 原比例哪都放不下、缩一半放得下。
    var (_, half, halfFree) = DrawingPlanner.IsoSpot(a3, main, new ViewBox(0.300, 0.200, 0, 0), [1.0, 0.5], new SheetPoint(0.330, 0.180));
    True(halfFree && half == 0.5, "放不下就缩一档");
    // 缩到底也没有空地：放在压得最少处，说明不是空地。
    var (_, last, crowded) = DrawingPlanner.IsoSpot(a3, main, new ViewBox(0.500, 0.400, 0, 0), [1.0, 0.5], new SheetPoint(0.330, 0.180));
    True(!crowded && last == 0.5, "没有空地要说明");

    // 技术要求：先放标题栏正上方。
    var (topLeft, noteFree) = DrawingPlanner.NotePlace(a3, 0.101, 0.042, main);
    True(noteFree && topLeft.Y - 0.042 >= a3.TitleBlock.Top && topLeft.X + 0.101 <= a3.TitleBlock.Right, "技术要求先放标题栏正上方");
    var everywhere = new List<SheetRect> { a3.Frame };
    True(!DrawingPlanner.NotePlace(a3, 0.101, 0.042, everywhere).Free, "满了要说明");

    // 只排视图（没有轴测图、技术要求）：不给它们位置。
    var bare = DrawingPlanner.Layout(a3, new LayoutRequest(new ViewBox(0.150, 0.100, 0.030, 0.030), new Dictionary<ViewSlot, ViewBox>(), new ViewBox(0, 0, 0, 0), 0, 0));
    True(bare.Fits && bare.Iso is null && bare.NoteTopLeft is null, "没有轴测图、技术要求就不排它们");
}

static void TestDrawingSteps()
{
    // 一键出图 = 出图类各步 + 孔标注全流程（不拆）+ 全图圆角，顺序写在用法里；每一步都是一条单独的指令（用户定）。
    var auto = QuickCommands.All.Single(command => command.Key == "drawing-auto");
    string[] steps = ["新建工程图", "投影视图", "轴测图", "技术要求", "排版", "孔标注全流程", "全图圆角", "全图倒角"];
    var positions = steps.Select(step => auto.Usage.IndexOf(step, StringComparison.Ordinal)).ToList();
    True(positions.All(index => index >= 0) && positions.Zip(positions.Skip(1)).All(pair => pair.First < pair.Second), "一键出图用法里的步骤顺序：" + auto.Usage);
    var titles = QuickCommands.All.Select(command => command.Title).ToHashSet();
    True(steps.All(titles.Contains), "每一步都要有自己的按钮与指令");
    Equal("hole", QuickCommands.All.Single(command => command.Title == "孔标注全流程").CommandClass);
    True(QuickCommands.All.Where(command => command.CommandClass == "drawing").All(command => command.Title != "孔标注全流程"), "孔标注全流程留在孔类，不拆进出图类");
}

static void TestChamferPlan()
{
    // 端视图：20 × 30 的块，下边两个角各倒 C5（限位块右的下视图）；倒角斜边同时也在直边里（扫描时就是这样）。
    static double M(double mm) => mm / 1000;
    SheetSegment S(double x1, double y1, double x2, double y2) => new(M(x1), M(y1), M(x2), M(y2));
    var leftChamfer = S(0, 5, 5, 0);
    var rightChamfer = S(15, 0, 20, 5);
    var lines = new List<SheetSegment>
    {
        S(0, 5, 0, 30), S(20, 5, 20, 30), S(0, 30, 20, 30), S(5, 0, 15, 0), leftChamfer, rightChamfer,
    };
    var chamfers = new List<ChamferEdge>
    {
        new(0, leftChamfer, M(5), M(5), "A"),
        new(1, rightChamfer, M(5), M(5), "B"),
    };
    var none = new HashSet<string>();
    var plan = ChamferPlanner.Plan(chamfers, lines, lines, [], none);
    Equal(1, plan.Targets.Count);
    var target = plan.Targets[0];
    // 两个同尺寸合标「2 x C5」，标靠右的那个；用户定用普通线性尺寸：量横向那条直角边，尺寸线放在视图下边外 8 mm。
    Equal(1, target.Index);
    Equal(2, target.Count);
    Equal("2 x C", target.Prefix);
    Equal(1, target.Placements.Count);
    Equal(PositionAxis.Horizontal, target.Placements[0].Axis);
    Near(-0.008, target.Placements[0].At.Y);
    Near(M(17.5), target.Placements[0].At.X);
    // 被倒掉的角：右下角倒角在 (20, 0)，左下角在 (0, 0)。
    Equal(new SheetPoint(M(20), M(0)), ChamferPlanner.Corner(rightChamfer, lines)!.Value);
    Equal(new SheetPoint(M(0), M(0)), ChamferPlanner.Corner(leftChamfer, lines)!.Value);

    // 靠着图框：下边放不下（出图框）就换到右边量竖向那条直角边（1.10.0：限位块前的下视图贴着图框底）。
    var frame = new SheetRect(M(-10), M(-3), M(50), M(50));
    var framed = ChamferPlanner.Plan(chamfers, lines, lines, [], none, frame).Targets[0].Placements[0];
    Equal(PositionAxis.Vertical, framed.Axis);
    Near(M(28), framed.At.X);

    // C1 按技术要求不标。
    var withDefault = chamfers.Append(new ChamferEdge(2, S(19, 30, 20, 29), M(1), M(1), "C")).ToList();
    var defaults = ChamferPlanner.Plan(withDefault, lines, lines, [], none);
    Equal(1, defaults.DefaultCount);
    Equal(1, defaults.Targets.Count);
    True(ChamferPlanner.IsDefault(withDefault[2]) && !ChamferPlanner.IsDefault(chamfers[0]), "C1 判定");

    // 这个视图里标过其中一个：同尺寸的都算标过（用户限位块前两个 C5 只写了一个「C5」）。
    var labeled = ChamferPlanner.Plan(chamfers, lines, lines, [leftChamfer], none);
    Equal(0, labeled.Targets.Count);
    Equal(2, labeled.Dimensioned);
    // 别的视图标过同一张倒角面：跳过。
    var elsewhere = ChamferPlanner.Plan(chamfers, lines, lines, [], new HashSet<string> { "A", "B" });
    Equal(0, elsewhere.Targets.Count);
    Equal(2, elsewhere.Elsewhere);
    // 尺寸不同的各标各的，只写「C」不写「N x」。
    var mixed = ChamferPlanner.Plan([chamfers[0], new ChamferEdge(1, S(17, 0, 20, 3), M(3), M(3), "B")], [.. lines, S(15, 0, 17, 0)], lines, [], none);
    Equal(2, mixed.Targets.Count);
    True(mixed.Targets.All(t => t.Count == 1 && t.Prefix == "C"), "不同尺寸不合标，写「C」");
    // 不等边：两条直角边各标一个，不写 C。
    var uneven = ChamferPlanner.Plan([new ChamferEdge(1, S(12, 0, 20, 5), M(8), M(5), "U")], [S(0, 0, 12, 0), S(20, 5, 20, 30)], lines, [], none).Targets.Single();
    Equal(2, uneven.Placements.Count);
    Equal("", uneven.Prefix);
    // 两头接不上水平 / 竖直直边：认不出哪边在零件外，标不了，说明。
    var floating = ChamferPlanner.Plan([new ChamferEdge(0, S(50, 50, 55, 55), M(5), M(5), "F")], lines, lines, [], none);
    Equal(0, floating.Targets.Count);
    Equal(1, floating.NoLead);
}

static void TestChamferViews()
{
    // 限位块右：20 × 30 × 100，沿长度方向（Z）两条 C5，另有一圈 C1。主视图看上面（法向 +Y，图纸竖向是 −Z）：
    // C5 只有沿 Z 看才成斜线——要一个上 / 下视图（1.9.0 没有，倒角标不上）；C1 不标，也不为它加视图。
    var box = new ModelBox(-0.01, -0.015, 0, 0.01, 0.015, 0.1);
    PartChamfer C(ModelDirection axis, double leg) => new(axis, leg, leg);
    var z = new ModelDirection(0, 0, 1);
    var x = new ModelDirection(1, 0, 0);
    var block = new PartGeometry(box, [], [], [], [C(z, 0.005), C(z, 0.005), C(x, 0.001), C(z, 0.001)]);
    True(block.Chamfers[2].Default && !block.Chamfers[0].Default, "C1 判定");
    var top = StandardView.Of("top");
    var sides = DrawingPlanner.SideViews(block, Frame(top), firstAngle: true);
    Equal(1, sides.Count);
    Equal(ViewSlot.Below, sides[0].Slot);
    True(sides[0].Reason.Contains("倒角 2 个", StringComparison.Ordinal) && sides[0].Alternative is null, "看倒角的视图：" + sides[0].Reason);
    // 只有 C1：照旧只加一个看厚度的。
    var plain = new PartGeometry(box, [], [], [], [C(x, 0.001)]);
    True(DrawingPlanner.SideViews(plain, Frame(top), firstAngle: true).Single().Alternative is not null, "只有 C1 时还是看厚度、可换边");

    // 投影视图的尺寸空间：与主视图共用的方向上没有孔就不留（右视图的左边、下视图的上边）。
    var right = DrawingPlanner.SideBox(0.03, 0.04, ViewSlot.Right, 0, chain: false);
    Near(0, right.MarginLeft);
    Near(DrawingPlanner.AnnotationMargin(0, chain: false), right.MarginTop);
    var below = DrawingPlanner.SideBox(0.03, 0.04, ViewSlot.Below, 0, chain: true);
    Near(0, below.MarginTop);
    Near(DrawingPlanner.AnnotationMargin(0, chain: true), below.MarginLeft);
    var holes = DrawingPlanner.SideBox(0.03, 0.04, ViewSlot.Below, 2, chain: false);
    Near(DrawingPlanner.AnnotationMargin(2, chain: false), holes.MarginTop);
}

static void TestSnapshotName()
{
    Equal("Draw1 - 图纸1", DrawingSnapshot.SafeName("Draw1 - 图纸1"));
    Equal("a_b_c__", DrawingSnapshot.SafeName("a/b:c*?"));
}

/// <summary>300 × 200 × 10 的板，6 个沉头孔沿 Z，沉头朝 <paramref name="holesOpenTo"/>。</summary>
static PartGeometry Plate(ModelDirection holesOpenTo)
{
    var cylinders = new List<PartCylinder>();
    for (var i = 0; i < 6; i++)
    {
        var point = new ModelDirection(0.02 + 0.05 * i, 0.05, 0);
        cylinders.Add(new PartCylinder(new ModelDirection(0, 0, 1), point, 0.0033, true, true, "CBORE", [new(0, 0, 1), new(0, 0, -1)]));
        cylinders.Add(new PartCylinder(new ModelDirection(0, 0, 1), point, 0.0055, true, true, "CBORE", [holesOpenTo]));
    }

    return new PartGeometry(new ModelBox(0, 0, 0, 0.3, 0.2, 0.01), cylinders, []);
}

/// <summary>96 × 96 × 28 的 L 形件：截面 3 个圆角沿 Z；横腿 2 个孔沿 Y（朝 +Y）、竖腿 2 个孔沿 X（朝 −X）。</summary>
static PartGeometry Bracket()
{
    var cylinders = new List<PartCylinder>
    {
        new(new ModelDirection(0, 0, 1), new ModelDirection(0.005, 0.015, 0), 0.005, false, false, "Fillet3", []),
        new(new ModelDirection(0, 0, 1), new ModelDirection(0.086, 0.030, 0), 0.010, true, false, "Fillet1", []),
        new(new ModelDirection(0, 0, 1), new ModelDirection(0.091, 0.091, 0), 0.005, false, false, "Fillet2", []),
    };
    foreach (var x in new[] { 0.030, 0.060 })
        cylinders.Add(new PartCylinder(new ModelDirection(0, 1, 0), new ModelDirection(x, 0, 0.014), 0.0033, true, true, "CBORE1", [new(0, 1, 0), new(0, -1, 0)]));
    foreach (var y in new[] { 0.045, 0.075 })
        cylinders.Add(new PartCylinder(new ModelDirection(1, 0, 0), new ModelDirection(0, y, 0.014), 0.0033, true, true, "CBORE2", [new(-1, 0, 0)]));
    return new PartGeometry(new ModelBox(0, 0, 0, 0.096, 0.096, 0.028), cylinders, []);
}

/// <summary>全部后代里的对象节点（含自身）。只有对象才有属性可查。</summary>
static IEnumerable<JsonElement> Descendants(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.Object)
        yield return element;
    var children = element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Select(property => property.Value),
        JsonValueKind.Array => element.EnumerateArray(),
        _ => [],
    };
    foreach (var child in children)
    {
        foreach (var descendant in Descendants(child))
            yield return descendant;
    }
}

static void True(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"期望 {expected}，实际 {actual}");
}

static void Near(double expected, double actual)
{
    if (Math.Abs(expected - actual) > 1e-12)
        throw new InvalidOperationException($"期望 {expected}，实际 {actual}");
}

/// <summary>
/// 模块登记口的测试替身：只记下登记了哪些指令。宿主 6.0.0 起注册表是宿主内部类，
/// 登记口 ICommandRegistrar 就是模块能看到的全部；来源由宿主盖章，这里不再核对。
/// </summary>
sealed class Registrar : ICommandRegistrar
{
    private readonly Dictionary<string, CommandDescriptor> _commands = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<CommandDescriptor> All => _commands.Values;

    public void Register(CommandDescriptor descriptor)
    {
        if (!_commands.TryAdd(descriptor.Name, descriptor))
            throw new InvalidOperationException($"重复登记 {descriptor.Name}");
    }

    public bool TryGet(string name, out CommandDescriptor? descriptor) => _commands.TryGetValue(name, out descriptor);
}
