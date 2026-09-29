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
    ("distinct holes stay distinct", TestDistinctHoles),
    ("targets read top-down, left-right", TestTargetOrder),
    ("placement sits outside the hole", TestPlacement),
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

static CommandRegistry Registry()
{
    var registry = new CommandRegistry();
    HistoryStrenuaModule.Register(registry, new QuickCommandRunner(null));
    return registry;
}

static void TestCommandRegistration()
{
    var registry = Registry();
    string[] expected =
    [
        "strenua.hole.callout",
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
        Equal(StrenuaIdentity.Source, registry.GetSource(name)!);
    }

    Equal(expected.Length, registry.All().Count(d => d.Name.StartsWith("strenua.", StringComparison.Ordinal)));

    True(registry.TryGet("strenua.hole.callout", out var hole), "缺少孔标注");
    True(!hole!.Readonly, "孔标注会改工程图，不是只读");
    True(hole.HiddenReason is null, "快捷指令要能在控制台直接敲");
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
    Near(hole.Radius + HoleCalloutPlanner.PlacementGap, distance);
    True(placement.X > hole.X && placement.Y > hole.Y, "标注放在右上方");
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
