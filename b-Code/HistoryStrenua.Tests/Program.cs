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
    ("status rows", TestStatusRows),
    ("button rows wrap", TestButtonRowsWrap),
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
    ("slots: two facing half circles pair up", TestSlotPairing),
    ("slots: semicircle and bulge direction", TestSlotGeometry),
    ("slots: one callout per kind, on the upper end", TestSlotCallout),
    ("slots: center marks on both ends", TestSlotCenterMarks),
    ("slots: position on the upper end only", TestSlotPosition),
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
    HistoryStrenuaModule.Register(registry, new QuickCommandRunner(null));
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
        "strenua.quick.list",
        "strenua.quick.cancel",
        "strenua.ui.describe",
        "strenua.ui.actions",
        "strenua.ui.data",
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
    foreach (var method in new[] { "describe", "actions", "data" })
    {
        True(registry.TryGet("strenua.ui." + method, out var ui), $"缺少 strenua.ui.{method}");
        True(ui!.HiddenReason is not null, $"strenua.ui.{method} 是界面内部协议，必须 HiddenReason");
    }
}

static void TestPageOwner()
{
    using var description = JsonDocument.Parse(StrenuaPage.Describe());
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
        True(registry.TryGet(command, out _), $"动作 {id} 指向未注册的指令 {command}");
    }

    using var description = JsonDocument.Parse(StrenuaPage.Describe());
    var buttons = Descendants(description.RootElement)
        .Where(node => node.TryGetProperty("kind", out var kind) && kind.GetString() == "button")
        .Select(node => node.GetProperty("action").GetString()!)
        .ToList();
    foreach (var command in QuickCommands.All)
        True(buttons.Contains(command.ActionId), $"页面缺少「{command.Title}」按钮");
    True(buttons.Contains(StrenuaPage.CancelActionId), "页面缺少取消按钮");
    foreach (var button in buttons)
        True(declared.ContainsKey(button), $"按钮绑定了未声明的动作 {button}");

    var sources = Descendants(description.RootElement)
        .Where(node => node.TryGetProperty("dataSource", out _))
        .Select(node => node.GetProperty("dataSource").GetProperty("command").GetString()!)
        .ToList();
    True(sources.Count > 0, "页面应有状态表");
    foreach (var source in sources)
        True(registry.TryGet(source, out _), $"表格取数指向未注册的指令 {source}");
}

static void TestStatusRows()
{
    var rows = StrenuaPage.Rows(new QuickCommandRunner(null));
    Equal(QuickCommands.All.Count, rows.Count);
    var hole = rows.Single(row => row["id"] == "hole-callout");
    Equal("孔标注", hole["title"]);
    Equal("就绪", hole["state"]);
    Equal(string.Empty, hole["result"]);
    foreach (var row in rows)
        True(new[] { "id", "title", "usage", "state", "result", "time" }.All(row.ContainsKey), "状态行缺列");
}

static void TestButtonRowsWrap()
{
    var many = Enumerable.Range(0, StrenuaPage.ButtonsPerRow + 2)
        .Select(i => new QuickCommand($"k{i}", $"strenua.test.k{i}", $"指令{i}", "测试", _ => QuickOutcome.Ok("ok")))
        .ToList();
    using var description = JsonDocument.Parse(StrenuaPage.Describe(many));
    var panel = Descendants(description.RootElement)
        .First(node => node.TryGetProperty("id", out var id) && id.GetString() == StrenuaPage.PanelId);
    var rows = panel.GetProperty("rows").EnumerateArray().ToList();
    // 6 个指令 + 取消 = 7 个按钮，每行 4 个 → 两行。
    Equal(2, rows.Count);
    Equal(StrenuaPage.ButtonsPerRow, rows[0].GetProperty("widgets").GetArrayLength());
    var last = rows[^1].GetProperty("widgets").EnumerateArray().Last();
    Equal(StrenuaPage.CancelActionId, last.GetProperty("action").GetString()!);
}

static void TestCancelWhenIdle()
{
    var result = new QuickCommandRunner(null).Cancel();
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
    ];
    Equal("0,1,5,6,8", string.Join(",", HolePositionPlanner.Obsolete(holes, existing)));
    True(!HolePositionPlanner.PassesThrough(new SheetSegment(0.13, 0.07, 0.13, 0.08), holes[0]), "线段延长线过孔心不算");
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
