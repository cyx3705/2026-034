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
    ("check: callouts, positions, dowels and outline", TestDimensionCheck),
    ("distinct holes stay distinct", TestDistinctHoles),
    ("targets read top-down, left-right", TestTargetOrder),
    ("placement sits up-left of the hole", TestPlacement),
    ("shoulder end is the right end of the underline", TestShoulderEnd),
    ("hole faces the viewer", TestFacesViewer),
    ("hole wall is concave and same radius", TestHoleWall),
    ("perpendicular is unit and orthogonal", TestPerpendicular),
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
        "strenua.check.dimension",
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
    // 检查类一块面板、只有按钮没有开关。
    var check = branches.Single(branch => branch.GetProperty("id").GetString() == StrenuaPage.ClassPanelId("check"));
    Equal("检查", check.GetProperty("case").GetString()!);
    Equal(1, check.GetProperty("rows").GetArrayLength());
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
    Equal("hole-flow,dowel-symbol,center-mark,hole-position,hole-callout,dowel-fit,outline,check-dimension",
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
