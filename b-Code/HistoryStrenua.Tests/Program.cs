using System.Text.Json;
using HistoryStrenua;
using HistoryVulcan.Core.Commands;

// 全部离线：不需要 SolidWorks，也不会去碰本机正在运行的那个。
// 真机验证（附着 SolidWorks、在工程图上加孔标注）没有自动化，见 b-Office/现行约定.md「真机才知道的」。
var tests = new (string Name, Action Run)[]
{
    ("command registration", TestCommandRegistration),
    ("page owner follows domain", TestPageOwner),
    ("buttons, actions and commands line up", TestPageWiring),
    ("list rows", TestListRows),
    ("toolbar: float, drag area, class, cancel", TestToolbar),
    ("switches: own panel at the bottom of the window, for every class", TestSwitchPanel),
    ("class panels: buttons only", TestClassPanels),
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
    ("hole position: symmetric views measure across the axis", TestPositionSymmetry),
    ("hole callout: opposite face holes", TestOppositeHoles),
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
    ("drawing steps: existing views by direction", TestDrawingSlotOf),
    ("drawing steps: side view beside the main view", TestDrawingBeside),
    ("drawing steps: flexible side on the current sheet", TestDrawingChooseSlots),
    ("drawing steps: iso and note on their own", TestDrawingSpots),
    ("drawing steps: one-click runs the steps in order", TestDrawingSteps),
    ("chamfer: which chamfers, grouping, which leg and where", TestChamferPlan),
    ("chamfer: side views and margins", TestChamferViews),
    ("chamfer: steps out and slides to avoid annotations (1.15.0)", TestChamferAvoid),
    ("holes: one callout and one position per sheet, counterbore side (1.15.0)", TestHoleCoverage),
    ("fillet: which arcs, grouping and text side", TestFilletPlan),
    ("fillet: holes subtracted from all circles, the rest get Ø", TestFilletCircles),
    ("arc center: tangency decides which positions to dimension", TestArcCenter),
    ("check: overlapping annotations", TestOverlapCheck),
    ("clearance switch: fillet, chamfer, note, side views", TestClearanceSwitch),
    ("snapshot: file name", TestSnapshotName),
    ("tech templates: note text out of the SW note style file", TestTechTemplateParse),
    ("tech templates: the user's general technical requirements", TestTechTemplateFolder),
    ("tech note: only above or left of the title block", TestTechNoteSlots),
    ("tech note: views move out of the way", TestTechNoteMakeRoom),
    ("tech note: replacing keeps the old slot", TestTechNoteReplace),
    ("tech ai: template items split and rejoined", TestTechAiParse),
    ("tech ai: restrained edits with limits", TestTechAiApply),
    ("tech ai: reading the model's answers", TestTechAiAnswers),
    ("tech ai: apollo command line and receipt", TestTechAiCommand),
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

static void TestPositionSymmetry()
{
    // 用户 XJ05A-01 安装板的下视图（View3，图纸米）：100 × 10 的板条，两个 M3 在 x=12.5 / 87.5、y=5。
    List<SheetSegment> strip =
    [
        new(0.11666, 0.12878, 0.11666, 0.13878),
        new(0.21666, 0.12878, 0.21666, 0.13878),
        new(0.11666, 0.13878, 0.21666, 0.13878),
        new(0.11666, 0.12878, 0.21666, 0.12878),
    ];
    List<HoleEdge> pair =
    [
        new(0, 0.12916, 0.13378, 0.00125, "/M3"),
        new(1, 0.20416, 0.13378, 0.00125, "/M3"),
    ];
    var axes = SymmetryPlanner.Axes(strip, [], pair);
    // 左右对称；孔全在水平中线上，那个方向不算（用户示范保留了「5」）。
    Equal(1, axes.Count);
    Equal(PositionAxis.Horizontal, axes[0].Axis);
    True(Math.Abs(axes[0].At - 0.16666) < 1e-9, "对称轴在板条正中");
    Equal("0,1", $"{axes[0].FirstLine},{axes[0].SecondLine}");
    True(SymmetryPlanner.Drawn(axes[0], [new SheetSegment(0.16666, 0.12378, 0.16666, 0.14378)]), "已有的中心线认得出");
    True(!SymmetryPlanner.Drawn(axes[0], [new SheetSegment(0.13, 0.12378, 0.13, 0.14378)]), "别处的中心线不算");

    // 以对称轴为基准：水平只标两孔之间的 75，不再从左边标 12.5；竖直照旧从上边标 5。
    var plan = HolePositionPlanner.Plan(pair, 0.11666, 0.13878, 1, symmetric: axes.Select(a => a.Axis).ToList());
    var horizontal = plan.Dimensions.Where(d => d.Axis == PositionAxis.Horizontal).ToList();
    Equal(1, horizontal.Count);
    Equal(0, horizontal[0].FromEdgeIndex);
    Equal(1, horizontal[0].ToEdgeIndex);
    Equal(1, plan.Dimensions.Count(d => d.Axis == PositionAxis.Vertical && d.FromEdgeIndex is null));
    Equal(2, HolePositionPlanner.Plan(pair, 0.11666, 0.13878, 1).Dimensions.Count(d => d.Axis == PositionAxis.Horizontal));

    // 边在投影里被切成两段照样算对称（按并集盖住）。
    List<SheetSegment> split = [strip[0], strip[1], new(0.11666, 0.13878, 0.15, 0.13878), new(0.15, 0.13878, 0.21666, 0.13878), strip[3]];
    Equal(1, SymmetryPlanner.Axes(split, [], pair).Count);
    // 孔不对称、或对称位置上是别种孔：不算。
    Equal(0, SymmetryPlanner.Axes(strip, [], [pair[0], pair[1] with { X = 0.20 }]).Count);
    Equal(0, SymmetryPlanner.Axes(strip, [], [pair[0], pair[1] with { Kind = "/M4" }]).Count);
    // 外形不对称（右下多一个缺口的斜边）：不算。
    Equal(0, SymmetryPlanner.Axes([.. strip, new(0.20666, 0.12878, 0.21666, 0.13378)], [], pair).Count);

    // 侧视图（View2）：10 × 60，三个 M4 在中线上 y=7.5/30/52.5——上下对称、中间那个正在轴上。
    List<SheetSegment> end =
    [
        new(0.03694, 0.18068, 0.03694, 0.24068),
        new(0.04694, 0.18068, 0.04694, 0.24068),
        new(0.03694, 0.24068, 0.04694, 0.24068),
        new(0.03694, 0.18068, 0.04694, 0.18068),
    ];
    List<HoleEdge> three =
    [
        new(0, 0.04194, 0.23318, 0.00165, "/M4"),
        new(1, 0.04194, 0.21068, 0.00165, "/M4"),
        new(2, 0.04194, 0.18818, 0.00165, "/M4"),
    ];
    var endAxes = SymmetryPlanner.Axes(end, [], three);
    Equal(1, endAxes.Count);
    Equal(PositionAxis.Vertical, endAxes[0].Axis);
    Equal("2,3", $"{endAxes[0].FirstLine},{endAxes[0].SecondLine}");
    // 同种互标：22.5 + 22.5 两段链，没有从上边到第一个孔的 7.5（用户示范）。
    var endPlan = HolePositionPlanner.Plan(three, 0.03694, 0.24068, 1, symmetric: endAxes.Select(a => a.Axis).ToList());
    var vertical = endPlan.Dimensions.Where(d => d.Axis == PositionAxis.Vertical).ToList();
    Equal(2, vertical.Count);
    True(vertical.All(d => d.FromEdgeIndex is not null), "对称方向不从基准边标");
    True(SymmetryPlanner.OnAxis(endAxes, PositionAxis.Vertical, 0.21068), "中间那个孔在对称轴上");

    // 两种孔各自对称：各自跨轴标，不互标。
    List<HoleEdge> mixed = [.. pair, new(2, 0.14166, 0.13378, 0.001, "/D2"), new(3, 0.19166, 0.13378, 0.001, "/D2")];
    var mixedPlan = HolePositionPlanner.Plan(mixed, 0.11666, 0.13878, 1, symmetric: [PositionAxis.Horizontal]);
    var spans = mixedPlan.Dimensions.Where(d => d.Axis == PositionAxis.Horizontal).Select(d => $"{d.FromEdgeIndex}-{d.ToEdgeIndex}").Order().ToList();
    Equal("0-1,2-3", string.Join(",", spans));
}

static void TestOppositeHoles()
{
    // 真机 XJ05A-01 安装板（100 × 60 × 10，模型米）：两端面各 3 个 M4 盲孔（轴沿 X，深 8），一头「M4螺纹孔1」、另一头是镜像出来的「镜向1」。
    static PartCylinder Hole(double x, double y, int opening, string feature)
        => new(new ModelDirection(1, 0, 0), new ModelDirection(x, y, 0.005), 0.00165, true, true, feature, [new ModelDirection(opening, 0, 0)],
            opening > 0 ? x - 0.008 : x, opening > 0 ? x : x + 0.008);
    var box = new ModelBox(0, 0, 0, 0.1, 0.06, 0.01);
    var ys = new[] { 0.0075, 0.03, 0.0525 };
    var same = ys.Select(y => Hole(0.1, y, 1, "M4螺纹孔1")).Concat(ys.Select(y => Hole(0, y, -1, "镜向1"))).ToList();
    var part = new PartGeometry(box, same, []);
    var normal = new ModelDirection(1, 0, 0);
    // 共轴却隔着整块板：是 6 个孔，不是 3 个。
    Equal(6, part.Holes.Count);
    Equal((OppositeResult.Same, 3), OppositePlanner.Judge(part, normal, "/M4螺纹孔1", 0.00165, 6));
    Equal((OppositeResult.Same, 3), OppositePlanner.Judge(part, normal, "/M4螺纹孔1", 0.00165, null));
    Equal((OppositeResult.Same, 3), OppositePlanner.Judge(part, new ModelDirection(-1, 0, 0), "/镜向1", 0.00165, 6));
    True(!OppositePlanner.FacesDiffer(part, normal), "两头一样不用两边都开视图");
    Equal((OppositeResult.None, 0), OppositePlanner.Judge(part, normal, "/别的特征", 0.00165, 6));
    Equal((OppositeResult.None, 0), OppositePlanner.Judge(part, normal, "/M4螺纹孔1", 0.0025, 6));
    // 读不到起止（旧夹具）照旧按共轴并。
    Equal(3, new PartGeometry(box, same.Select(c => c with { AxialMin = double.NaN }).ToList(), []).Holes.Count);
    // 沉头与底孔首尾相接：仍是一个孔（轴向反着存也认得）。
    var cbore = new PartCylinder(new ModelDirection(0, 0, 1), new ModelDirection(0.05, 0.03, 0), 0.003, true, true, "CB", [new ModelDirection(0, 0, 1)], 0.006, 0.01);
    var drill = new PartCylinder(new ModelDirection(0, 0, -1), new ModelDirection(0.05, 0.03, 0), 0.0017, true, true, "CB", [], -0.006, 0);
    Equal(1, new PartGeometry(box, [cbore, drill], []).Holes.Count);

    // 中间那个底孔打穿进了窗口（孔口两头都有）：按碰不碰外表面判，仍是只开在一头的盲孔。
    var window = same.Select(c => c.Point.Y == 0.03 && c.Feature == "镜向1" ? c with { Openings = [new(-1, 0, 0), new(1, 0, 0)] } : c).ToList();
    Equal(OppositeResult.Same, OppositePlanner.Judge(new PartGeometry(box, window, []), normal, "/M4螺纹孔1", 0.00165, 6).Result);

    // 对面一个挪了位置：不一致，要另开视图。
    var moved = ys.Select(y => Hole(0.1, y, 1, "M4螺纹孔1")).Concat(ys.Select(y => Hole(0, y == 0.03 ? 0.04 : y, -1, "镜向1"))).ToList();
    var movedPart = new PartGeometry(box, moved, []);
    Equal((OppositeResult.Different, 3), OppositePlanner.Judge(movedPart, normal, "/M4螺纹孔1", 0.00165, 6));
    Equal((OppositeResult.Different, 3), OppositePlanner.Judge(movedPart, normal, "/M4螺纹孔1", 0.00165, null));
    True(OppositePlanner.FacesDiffer(movedPart, normal), "两头不一样");
    // 对面少一个：也按不一致。
    var fewer = ys.Select(y => Hole(0.1, y, 1, "M4螺纹孔1")).Concat(ys.Take(2).Select(y => Hole(0, y, -1, "镜向1"))).ToList();
    Equal(OppositeResult.Different, OppositePlanner.Judge(new PartGeometry(box, fewer, []), normal, "/M4螺纹孔1", 0.00165, 5).Result);
    // 中间那个一头被窗口截短：深度不同不影响（不比长度）。
    var cut = same.Select(c => c.Point.Y == 0.03 && c.Feature == "镜向1" ? c with { AxialMax = 0.005 } : c).ToList();
    Equal(OppositeResult.Same, OppositePlanner.Judge(new PartGeometry(box, cut, []), normal, "/M4螺纹孔1", 0.00165, 6).Result);
    // 对面同位置同孔径、却是另一个特征单独标的（孔标注只数这一面的「3 x」）：不写「(含对面)」。
    Equal((OppositeResult.None, 0), OppositePlanner.Judge(part, normal, "/M4螺纹孔1", 0.00165, 3));
    // 对面孔径不同（另一种）：没有对面同种孔。
    var other = ys.Select(y => Hole(0.1, y, 1, "M4螺纹孔1")).Concat(ys.Select(y => Hole(0, y, -1, "别的") with { Radius = 0.002 })).ToList();
    Equal((OppositeResult.None, 0), OppositePlanner.Judge(new PartGeometry(box, other, []), normal, "/M4螺纹孔1", 0.00165, null));

    // M3：两头各打 7.5 深、板厚 10，连成一个通孔——模型里 2 个，孔标注写「4 x」：两倍就是两头都有。
    var m3 = new[] { 0.034, 0.006 }.Select(z => new PartCylinder(new ModelDirection(-1, 0, 0), new ModelDirection(0, 0.02, z), 0.00125, true, true, "M3螺纹孔1",
        [new(1, 0, 0), new(-1, 0, 0)], -0.1, 0)).ToList();
    var through = new PartGeometry(box, m3, []);
    Equal((OppositeResult.Same, 2), OppositePlanner.Judge(through, normal, "/M3螺纹孔1", 0.00125, 4));
    Equal((OppositeResult.None, 0), OppositePlanner.Judge(through, normal, "/M3螺纹孔1", 0.00125, 2));
    Equal((OppositeResult.None, 0), OppositePlanner.Judge(through, normal, "/M3螺纹孔1", 0.00125, null));
    True(!OppositePlanner.FacesDiffer(through, normal), "通孔不算两头不一样");

    True(OppositePlanner.KindIsFeature("子装配-1/零件-1/M4螺纹孔1", "M4螺纹孔1"), "组件名带「/」只比结尾");
    True(!OppositePlanner.KindIsFeature("/M4螺纹孔10", "M4螺纹孔1"), "特征名要整段对上");
    True(OppositePlanner.HasMarker(["6 x  M4 - 6H ", " 10.10 (含对面)"]), "显示文字里认得出后缀");
    True(!OppositePlanner.HasMarker(["6 x  M4 - 6H ", " 10.10"]), "没写过");
    Equal(6, OppositePlanner.CalloutCount(["6 x  M4 - 6H ", "<HOLE-DEPTH>"]));
    Equal(2, OppositePlanner.CalloutCount(["H7", "2 x ", "<MOD-DIAM>"]));
    Equal<int?>(null, OppositePlanner.CalloutCount(["<MOD-DIAM>", " 3.30 "]));

    // 投影视图：主视图看板面（前视），侧面沿图纸横向两头的孔不一样就左右都加，只认正好那一边已有的视图。
    var front = StandardView.Of("front");
    var differ = DrawingPlanner.SideViews(movedPart, Frame(front), firstAngle: true);
    Equal("Right,Left", string.Join(",", differ.Select(side => side.Slot)));
    True(differ.All(side => side.Exact && side.Alternative is null), "两边都要、各认各的");
    var alike = DrawingPlanner.SideViews(part, Frame(front), firstAngle: true);
    Equal(1, alike.Count);
    True(!alike[0].Exact, "两头一样只加一个");
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
        "strenua.drawing.arrange",
        "strenua.drawing.symmetry",
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
    foreach (var name in new[] { "strenua.drawing.create", "strenua.drawing.auto", "strenua.drawing.project", "strenua.drawing.iso", "strenua.tech.apply",
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
    Equal("技术要求", areas[1].GetProperty("case").GetString()!);
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
    Equal("孔", widgets[2].GetProperty("options")[0].GetString()!);
    Equal("孔", widgets[2].GetProperty("value").GetString()!);
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
    Equal("一键出图,新建工程图,投影视图,轴测图,排版,对称轴", Row("drawing"));
    // 1.15.0（用户定）：圆心、圆弧、倒角拆成单独的「倒圆倒角」类。
    Equal("全图圆心,全图圆弧,全图倒角,圆心位置,圆弧标注,倒角标注", Row("fillet"));
    // 1.13.0（用户定）：旧「技术要求」按钮删掉，「技术要求」类整支就是模板表格；1.14.0 的「写入」按钮不在这里，在最下面一行和 AI 开关并排（见 switches 那组）。
    var tech = branches.Single(branch => branch.GetProperty("case").GetString() == "技术要求");
    Equal("table", tech.GetProperty("type").GetString()!);
    Equal(StrenuaPage.TechTableId, tech.GetProperty("id").GetString()!);
    True(!panels.Any(panel => panel.GetProperty("id").GetString() == StrenuaPage.ClassPanelId("tech")), "技术要求类中间没有按钮面板");
    True(QuickCommands.All.All(command => command.Title != "技术要求"), "技术要求按钮已删");
    // 检查类 1.12.0 加「悬空标注」「注解重叠」。
    Equal("未标尺寸,悬空标注,注解重叠,图纸截图", Row("check"));
    Equal("孔,出图,倒圆倒角,技术要求,检查", string.Join(",", StrenuaPage.ClassOptions(QuickCommands.All)));
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
        + "drawing-auto,drawing-create,drawing-project,drawing-iso,drawing-arrange,symmetry-axes,arc-center-all,arc-all,chamfer-all,arc-center,arc,chamfer,check-dimension,check-dangling,check-overlap,snapshot,tech-ai",
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
    // 普通模式下 P→Q 这段与 A→B 重叠，去重只标一次：销孔间尺寸要写「(公差仅对销孔)」。
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
    Equal("0.1,0.14,0.4", string.Join(",", OutlinePlanner.MissingFromGroup(h, s => s.Value, [0.0, 0.3 + 1e-7]).Select(s => s.Value.ToString("0.##", invariant))));
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

static void TestClearanceSwitch()
{
    static double M(double mm) => mm / 1000;

    // 圆角：正方向第一档（弧中点外 8 mm）压着一段已有注解的文字（1.11.0 躲已有注解）——开着就转开，关着就放在那儿。
    var arc = new FilletArc(0, new SheetPoint(0.100, 0.100), 0.002, 0.004, new SheetPoint(0.102, 0.100), false);
    var defaultAt = FilletPlanner.Plan([arc], [], [], avoid: false).Targets[0].TextAt;
    Near(0.110, defaultAt.X);
    Near(0.100, defaultAt.Y);
    var callout = new TextBox(0.106, 0.099, 0.010, 0.0035);
    var avoided = FilletPlanner.Plan([arc], [], [], texts: [callout]).Targets[0].TextAt;
    var box = new TextBox(avoided.X - 0.0026, avoided.Y - 0.00175, 0.0052, 0.0035);
    True(!ClearancePlanner.Hits(box, callout), "避障开：R 文字不压已有注解的字");
    // 关着时线也不躲（与 TestFilletPlan「正方向压线就转开」同一条线）。
    var wall = new SheetSegment(0.110, 0.090, 0.110, 0.110);
    Equal(defaultAt, FilletPlanner.Plan([arc], [], [wall], texts: [callout], avoid: false).Targets[0].TextAt);

    // 倒角：下边那个位置压着已有注解的字——开着换到右边，关着照「下 → 右」的先后留在下边。
    SheetSegment S(double x1, double y1, double x2, double y2) => new(M(x1), M(y1), M(x2), M(y2));
    var chamferLine = S(15, 0, 20, 5);
    var lines = new List<SheetSegment> { S(0, 0, 0, 30), S(20, 5, 20, 30), S(0, 30, 20, 30), S(0, 0, 15, 0), chamferLine };
    var chamfers = new List<ChamferEdge> { new(0, chamferLine, M(5), M(5), "B") };
    var none = new HashSet<string>();
    var note = new TextBox(M(10), M(-7), M(20), M(4));
    var on = ChamferPlanner.Plan(chamfers, lines, lines, [], none, texts: [note]).Targets[0].Placements[0];
    Equal(PositionAxis.Vertical, on.Axis);
    var off = ChamferPlanner.Plan(chamfers, lines, lines, [], none, texts: [note], avoid: false).Targets[0].Placements[0];
    Equal(PositionAxis.Horizontal, off.Axis);

    // 技术要求关着避障：直接放标题栏正上方靠右（左上角定位）。
    var a3 = SheetSpace.Standard(0.420, 0.297);
    var topLeft = DrawingPlanner.NoteDefault(a3, 0.101, 0.042);
    Near(a3.TitleBlock.Right - DrawingPlanner.Gap - 0.101, topLeft.X);
    Near(a3.TitleBlock.Top + DrawingPlanner.Gap + 0.042, topLeft.Y);

    // 投影视图让开：不压原地不动；压着「其他」视图就往离开主视图的方向挪到不压；挪出图框还压返回 null。
    var side = new ViewBox(0.040, 0.060, 0.010, 0.010);
    var start = new SheetPoint(0.250, 0.180);
    Equal(start, DrawingPlanner.AwayFrom(start, ViewSlot.Right, side, [], a3.Frame)!.Value);
    var other = new SheetRect(0.255, 0.150, 0.275, 0.170);
    var moved = DrawingPlanner.AwayFrom(start, ViewSlot.Right, side, [other], a3.Frame)!.Value;
    True(moved.X > start.X && Math.Abs(moved.Y - start.Y) < 1e-12, "右视图往右让");
    True(!DrawingPlanner.Occupied(moved, side).Any(other.Overlaps), "让开后不再压");
    True(DrawingPlanner.Occupied(new SheetPoint(moved.X - DrawingPlanner.AwayStep, moved.Y), side).Any(other.Overlaps), "只让到刚好不压");
    var wallToEdge = new SheetRect(0.200, 0.100, 0.420, 0.300);
    True(DrawingPlanner.AwayFrom(start, ViewSlot.Right, side, [wallToEdge], a3.Frame) is null, "让到图框边还压返回 null");
    var below = DrawingPlanner.AwayFrom(start, ViewSlot.Below, side, [new SheetRect(0.240, 0.140, 0.260, 0.160)], a3.Frame)!.Value;
    True(below.Y < start.Y && Math.Abs(below.X - start.X) < 1e-12, "下视图往下让");

    // 开关说明里写到出图类（页面动作与指令自描述共用一句）。
    True(StrenuaPage.ClearanceSummary.Contains("圆弧") && StrenuaPage.ClearanceSummary.Contains("圆心位置") && StrenuaPage.ClearanceSummary.Contains("外轮廓"), "避障开关说明要写到出图类与外轮廓");
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
    // 一键出图 = 出图类各步 + 孔标注全流程（不拆）+ 全图圆心、圆弧、倒角，顺序写在用法里；每一步都是一条单独的指令（用户定）。
    var auto = QuickCommands.All.Single(command => command.Key == "drawing-auto");
    string[] steps = ["新建工程图", "投影视图", "轴测图", "技术要求", "排版", "孔标注全流程", "全图圆心", "全图圆弧", "全图倒角"];
    var positions = steps.Select(step => auto.Usage.IndexOf(step, StringComparison.Ordinal)).ToList();
    True(positions.All(index => index >= 0) && positions.Zip(positions.Skip(1)).All(pair => pair.First < pair.Second), "一键出图用法里的步骤顺序：" + auto.Usage);
    var titles = QuickCommands.All.Select(command => command.Title).ToHashSet();
    // 1.13.0：「技术要求」这一步没有自己的按钮了（用户删掉旧按钮，单独放走模板表格），一键出图里照旧有这一步。
    True(steps.Where(step => step != "技术要求").All(titles.Contains), "除技术要求外每一步都要有自己的按钮与指令");
    True(!titles.Contains("技术要求"), "技术要求按钮已删");
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
static byte[] NoteStyleBytes(string text, bool shortLength = false)
{
    // 照真文件的样子：前面一段缩略图与别的对象，注释文字是 MFC Unicode CString（FF FE FF + 长度 + UTF-16LE），后面还有东西。
    var body = System.Text.Encoding.Unicode.GetBytes(text);
    var length = text.Length;
    var bytes = new List<byte>();
    bytes.AddRange(Enumerable.Range(0, 300).Select(i => (byte)(i * 7)));
    bytes.AddRange([0xFF, 0xFE, 0xFF, 0x03]);
    bytes.AddRange(System.Text.Encoding.Unicode.GetBytes("abc"));
    bytes.AddRange([0xFF, 0xFE, 0xFF]);
    if (shortLength)
        bytes.Add((byte)length);
    else
        bytes.AddRange([0xFF, (byte)(length & 0xFF), (byte)(length >> 8)]);
    bytes.AddRange(body);
    bytes.AddRange([0x00, 0x00, 0x12, 0x34]);
    return bytes.ToArray();
}

static void TestTechAiParse()
{
    // 用户模板的真实形状（机加件-钢材）：缩进的标题、第一条前面一串 PARA 格式标记、「N、」编号、句末中英文分号混用。
    const string para = "<PARA  indent=0 findent=0 indentStep=10 number=off bullet=off paraSpace=0.001 lineSpace=0.001>";
    var text = "        技术要求\n" + para + "1、未注公差参照附表执行；\n2、未注沉头孔与螺纹孔位置公差为±0.2mm，定位销孔位置公差±0.02mm;\n3、镀件表面涂防锈油；\n4、未注尺寸参考3D数模。\n";
    var items = TechAiEdit.Parse(text);
    Equal("        技术要求", items.Heading);
    Equal(para, items.Prefix);
    Equal(4, items.Items.Count);
    Equal("未注沉头孔与螺纹孔位置公差为±0.2mm，定位销孔位置公差±0.02mm;", items.Items[1]);
    // 原样拼回：标点、格式标记一个字都不动（克制的前提）。
    Equal(text.TrimEnd('\n'), items.Text);
    // 没有格式标记的模板（焊接结构件）、两位数编号、续行接到上一条。
    var weld = TechAiEdit.Parse("        技术要求\n1、甲；\n2、乙\n  乙的续行\n10、丙。");
    Equal(string.Empty, weld.Prefix);
    Equal("甲；,乙\n  乙的续行,丙。", string.Join(",", weld.Items));
    Equal("        技术要求\n1、甲；\n2、乙\n  乙的续行\n3、丙。", weld.Text);
    Equal("1：未注公差参照附表执行；", TechAiEdit.Numbered(items)[0]);
    Equal("乙 乙的续行", TechAiEdit.Plain(weld.Items[1]));

    if (Directory.Exists(TechTemplates.Folder))
    {
        foreach (var template in TechTemplates.Load())
        {
            var parsed = TechAiEdit.Parse(template.Text);
            Equal(template.Items, parsed.Items.Count);
            Equal(template.Text, parsed.Text);
        }
    }
}

static void TestTechAiApply()
{
    var based = new TechItems("技术要求", "<P>", ["一", "二", "三", "四", "五"]);
    // 按原编号删、按原编号插：删 2、4，在原 1 后加「甲」，最前加「乙」，原 4（已删）后加「丙」。
    var outcome = TechAiEdit.Apply(based, [2, 4], [(1, "甲"), (0, "乙"), (4, "丙")]);
    Equal("乙,一,甲,三,丙,五", string.Join(",", outcome.Result.Items));
    Equal("技术要求\n<P>1、乙\n2、一\n3、甲\n4、三\n5、丙\n6、五", outcome.Result.Text);
    Equal("2:二,4:四", string.Join(",", outcome.Deleted.Select(d => $"{d.Number}:{d.Text}")));
    Equal(0, outcome.Dropped);

    // 克制：最多删 3、加 3；编号不存在、重复删、空文、与已有重复、自带编号的都处理掉。
    var greedy = TechAiEdit.Apply(based, [1, 2, 3, 4, 9, 0, 1], [(5, "a"), (5, "b"), (5, "c"), (5, "d"), (5, "  "), (5, "五。")]);
    Equal(3, greedy.Deleted.Count);
    Equal(3, greedy.Added.Count);
    Equal(4 + 3, greedy.Dropped);
    Equal("四,五,a,b,c", string.Join(",", greedy.Result.Items));
    var numbered = TechAiEdit.Apply(based, [], [(9, "6、未注倒角C0.5；")]);
    Equal("未注倒角C0.5；", numbered.Result.Items[^1]);
    Equal(5, numbered.Added[0].After);

    // 超长的截断（一条一句，长段落多半是模型在解释）。
    var longText = new string('长', TechAiEdit.MaxItemLength + 20);
    Equal(TechAiEdit.MaxItemLength, TechAiEdit.Apply(based, [], [(0, longText)]).Result.Items[0].Length);
}

static void TestTechAiAnswers()
{
    string[] names = ["机加件-钢材", "钣金-焊接"];
    // 选基础：带围栏、带用量脚注也读得出；null 与「无」都是「没有合适的」；不在候选里的算答错。
    var fenced = "```json\n{\"base\": \"钣金-焊接\", \"reason\": \"折弯件带焊缝\"}\n```\n\n— qwen/qwen-vl-max · 用量 1+2=3 tokens · 1.0s";
    Equal((true, (string?)"钣金-焊接", "折弯件带焊缝"), TechAiEdit.ReadChoice(fenced, names));
    Equal((true, (string?)null, "都不合适"), TechAiEdit.ReadChoice("{\"base\": null, \"reason\": \"都不合适\"}", names));
    Equal(true, TechAiEdit.ReadChoice("{\"base\": \"无\"}", names).Valid);
    Equal((false, (string?)"焊接结构件", string.Empty), TechAiEdit.ReadChoice("{\"base\": \"焊接结构件\"}", names));
    Equal(false, TechAiEdit.ReadChoice("我觉得是钣金", names).Valid);
    Equal("机加件-钢材", TechAiEdit.ReadChoice("{\"base\": \"「机加件-钢材」\"}", names).Base!);

    // 微调：delete / add，编号可能写成字符串；不合规的项计入没采用。
    var based = new TechItems("技术要求", string.Empty, ["一", "二", "三"]);
    var read = TechAiEdit.ReadEdit("{\"delete\": [\"3\", \"x\"], \"add\": [{\"after\": 1, \"text\": \"甲\"}, {\"text\": \"乙\"}, 5], \"reason\": \"图上没有螺纹\"}", based, fresh: false);
    True(read is not null, "应读出微调");
    Equal("一,甲,二,乙", string.Join(",", read!.Value.Outcome.Result.Items));
    Equal(2, read.Value.Outcome.Dropped);
    Equal("图上没有螺纹", read.Value.Reason);
    // 没有基础：items 是全文条目，最多 MaxFresh 条，标题与格式照模板。
    var many = string.Join(",", Enumerable.Range(1, TechAiEdit.MaxFresh + 2).Select(i => $"\"第{i}条\""));
    var fresh = TechAiEdit.ReadEdit($"{{\"items\": [{many}]}}", TechItems.Empty, fresh: true);
    Equal(TechAiEdit.MaxFresh, fresh!.Value.Outcome.Result.Items.Count);
    Equal(2, fresh.Value.Outcome.Dropped);
    True(fresh.Value.Outcome.Result.Text.StartsWith(TechItems.Empty.Heading + "\n" + TechItems.Empty.Prefix + "1、第1条", StringComparison.Ordinal), "没有基础时标题与格式照模板");
    Equal<object?>(null, TechAiEdit.ReadEdit("没有 JSON", based, fresh: false));
}

static void TestTechAiCommand()
{
    // 经总线发给 Apollo 的那一行：解析回来参数原样（引号、换行、等号都编码过），带图片走识图供应商。
    var system = "只输出 JSON：{\"base\": \"…\"}";
    var prompt = "候选：[\"机加件-钢材\"]。\n图纸信息 JSON：{\"图纸\":{\"比例\":\"1:2\"}}";
    var image = @"C:\Users\x\AppData\Roaming\HistoryVulcan\ModuleData\HistoryStrenua\snapshots\零件 1-20261006.png";
    var parsed = CommandParser.Parse(TechAi.BuildCommand(system, prompt, image));
    Equal("apollo.chat.ask", parsed.Name);
    Equal(image, parsed.Named["images"]);
    Equal(system, parsed.Named["system"]);
    Equal(prompt.Replace("\n", " "), parsed.Named["prompt"]);
    True(!parsed.Named.ContainsKey("provider"), "不写 provider：带图片时 Apollo 自己走识图供应商");
    Equal("module:HistoryStrenua", TechAi.CommandSource);

    // 回执说明：基础与理由、删了什么、加在哪、没采用几条。
    var based = new TechItems("技术要求", string.Empty, ["一", "螺纹嵌钢丝牙套；", "三"]);
    var outcome = TechAiEdit.Apply(based, [2, 7], [(3, "焊后去应力")]);
    var text = TechAi.Describe("机加件-有色金属", "铝合金机加件", outcome, "图上没有螺纹孔");
    Equal("以「机加件-有色金属」为基础（铝合金机加件）；删 第 2 条「螺纹嵌钢丝牙套；」；加「焊后去应力」（原第 3 条后）；AI 另给的 1 处超出上限或不合规，没采用；AI 说明：图上没有螺纹孔", text);
    Equal("以「机加件-钢材」为基础；未增删", TechAi.Describe("机加件-钢材", string.Empty, TechAiEdit.Apply(based, [], []), string.Empty));
    var fresh = TechAiEdit.Apply(TechItems.Empty, [], [(0, "甲"), (0, "乙")], TechAiEdit.MaxFresh);
    Equal("没有合适的模板，由 AI 撰写；写了 2 条", TechAi.Describe(null, string.Empty, fresh, string.Empty));

    // 按钮、开关的说明写清上限与退路。
    True(TechAi.Command.Usage.Contains($"最多删 {TechAiEdit.MaxDelete} 条、加 {TechAiEdit.MaxAdd} 条", StringComparison.Ordinal), TechAi.Command.Usage);
    True(StrenuaPage.TechAiSummary.Contains("AI 没成退回", StringComparison.Ordinal), StrenuaPage.TechAiSummary);
    Equal("tech", TechAi.Command.CommandClass);
}

static void TestTechTemplateParse()
{
    var text = "        技术要求\r\n<PARA  indent=0 findent=0 indentStep=10 number=off bullet=off paraSpace=0.001 lineSpace=0.001>1、未注倒角C1；\r\n2、去毛刺±0.02。\r\n";
    var parsed = TechTemplates.ParseNote(NoteStyleBytes(text));
    Equal("        技术要求\n<PARA  indent=0 findent=0 indentStep=10 number=off bullet=off paraSpace=0.001 lineSpace=0.001>1、未注倒角C1；\n2、去毛刺±0.02。", parsed);
    Equal(2, TechTemplates.CountItems(parsed!));
    Equal("1、未注倒角C1； 2、去毛刺±0.02。", TechTemplates.Summary(parsed!));
    // 一个字节的长度（短文字）也认。
    Equal("技术要求\n1、甲", TechTemplates.ParseNote(NoteStyleBytes("技术要求\r\n1、甲", shortLength: true)));
    // 没有「技术要求」的不是技术要求。
    Equal<string?>(null, TechTemplates.ParseNote(NoteStyleBytes("1、甲\r\n2、乙")));
    // 长度坏了（超出文件）不读。
    var broken = NoteStyleBytes(text);
    var at = broken.Length - 4 - System.Text.Encoding.Unicode.GetByteCount(text) - 2;
    broken[at] = 0xFF;
    broken[at + 1] = 0x7F;
    Equal<string?>(null, TechTemplates.ParseNote(broken));

    var directory = Path.Combine(Path.GetTempPath(), "strenua-tech-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "钣金.sldnotestl"), NoteStyleBytes(text));
        File.WriteAllBytes(Path.Combine(directory, "坏的.sldnotestl"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(directory, "别的.sldnotefvt"), NoteStyleBytes(text));
        var all = TechTemplates.Load(directory);
        Equal("钣金", string.Join(",", all.Select(t => t.Name)));
        Equal(2, all[0].Items);
        var row = TechApply.Rows("别的", directory).Single();
        Equal("钣金", row["name"]);
        Equal("设为默认", row["setting"]);
        Equal("默认", TechApply.Rows("钣金", directory).Single()["setting"]);
        Equal("2", row["items"]);
        Equal("1、未注倒角C1； 2、去毛刺±0.02。", row["content"]);
        True(TechTemplates.Find(" 钣金 ", directory) is not null && TechTemplates.Find("没有", directory) is null, "按名字找");
        Equal(0, TechTemplates.Load(Path.Combine(directory, "不存在")).Count);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestTechTemplateFolder()
{
    // 用户 z 级目录里的「通用技术要求」10 份（z 级目录长期存在；不在本机就跳过这一条的实际文件部分）。
    if (!Directory.Exists(TechTemplates.Folder))
    {
        Console.WriteLine($"  （跳过：本机没有 {TechTemplates.Folder}）");
        return;
    }

    var all = TechTemplates.Load();
    Equal(10, all.Count);
    Equal(Directory.GetFiles(TechTemplates.Folder, "*" + TechTemplates.Extension).Length, all.Count);
    True(all.All(t => t.Text.Contains("技术要求", StringComparison.Ordinal) && t.Items >= 5), "每份都读出了技术要求正文");
    var steel = all.Single(t => t.Name == "机加件-钢材");
    Equal(9, steel.Items);
    True(steel.Text.StartsWith("        技术要求\n<PARA", StringComparison.Ordinal) && steel.Text.EndsWith("9、未注尺寸参考3D数模。", StringComparison.Ordinal), steel.Text);
    Equal(14, all.Single(t => t.Name == "焊接基座-带门").Items);
    Equal(6, all.Single(t => t.Name == "装配图技术要求").Items);
    True(all.Single(t => t.Name == "铝型材-防护网").Text.Contains("30×30喷塑防护网", StringComparison.Ordinal), "× 这类非 ASCII 字符照原样");
}

static void TestTechNoteSlots()
{
    var a3 = SheetSpace.Standard(0.420, 0.297);
    var title = a3.TitleBlock;
    var gap = DrawingPlanner.Gap;
    var above = TechNotePlanner.Anchor(a3, NoteSlot.AboveTitle, 0.100, 0.050);
    Near(title.Right - gap, above.Right);
    Near(title.Top + gap, above.Bottom);
    var left = TechNotePlanner.Anchor(a3, NoteSlot.LeftOfTitle, 0.100, 0.050);
    Near(title.Left - gap, left.Right);
    Near(a3.Frame.Bottom + gap, left.Bottom);

    // 空图：放标题栏正上方，不挪任何东西。
    var empty = TechNotePlanner.Place(a3, [], 0.100, 0.050, null, avoid: true);
    True(empty.Free && empty.Slot == NoteSlot.AboveTitle && empty.Moves.Count == 0, "空图放上方");
    Equal(new SheetPoint(above.Left, above.Top), empty.TopLeft);

    // 上方压着视图、左侧空着：新加的先找空着的那处，不挪视图。
    var overAbove = new PlanView("主视图", null, ViewAxis.Free, [new SheetRect(0.300, 0.060, 0.400, 0.140)]);
    var plan = TechNotePlanner.Place(a3, [overAbove], 0.100, 0.050, null, avoid: true);
    True(plan.Free && plan.Slot == NoteSlot.LeftOfTitle && plan.Moves.Count == 0, "上方压着就放左侧");

    // 避障关：直接放上方，不躲不挪。
    var off = TechNotePlanner.Place(a3, [overAbove], 0.100, 0.050, null, avoid: false);
    True(off.Slot == NoteSlot.AboveTitle && off.Moves.Count == 0, "避障关直接放上方");
    Equal(new SheetPoint(above.Left, above.Top), off.TopLeft);

    // 图纸格式注解（「其余」粗糙度）压着上方：在上方这一处往上让，不跑到别处。
    var roughness = a3.With([new SheetRect(0.380, 0.050, 0.410, 0.060)]);
    var shifted = TechNotePlanner.Spot(roughness, NoteSlot.AboveTitle, 0.100, 0.050)!.Value;
    True(shifted.Bottom > 0.060 && Math.Abs(shifted.Right - above.Right) < 1e-12, "躲图纸格式注解只在本处往上让");

    // 已有的技术要求在哪一处。
    Equal(NoteSlot.LeftOfTitle, TechNotePlanner.SlotOf(a3, TechNotePlanner.Anchor(a3, NoteSlot.LeftOfTitle, 0.080, 0.030)));
    Equal(NoteSlot.AboveTitle, TechNotePlanner.SlotOf(a3, TechNotePlanner.Anchor(a3, NoteSlot.AboveTitle, 0.120, 0.060)));

    // 建图、排版估位置也只用这两处：上方压着给左侧；两处都压着说明不空，位置仍是两处之一（不跑到左下角之类的地方）。
    var (topLeft, free) = DrawingPlanner.NotePlace(a3, 0.100, 0.050, [overAbove.Rects[0]]);
    True(free && Math.Abs(topLeft.X - left.Left) < 1e-12 && Math.Abs(topLeft.Y - left.Top) < 1e-12, "估位置：上方压着给左侧");
    var (crowdedAt, crowdedFree) = DrawingPlanner.NotePlace(a3, 0.100, 0.050, [a3.Frame]);
    True(!crowdedFree && (crowdedAt == new SheetPoint(above.Left, above.Top) || crowdedAt == new SheetPoint(left.Left, left.Top)), "估位置：满了也只在两处之一");
}

static void TestTechNoteMakeRoom()
{
    var a3 = SheetSpace.Standard(0.420, 0.297);
    var keep = TechNotePlanner.Anchor(a3, NoteSlot.AboveTitle, 0.100, 0.050).Inflate(DrawingPlanner.Gap / 2);
    var left = TechNotePlanner.Anchor(a3, NoteSlot.LeftOfTitle, 0.100, 0.050).Inflate(DrawingPlanner.Gap / 2);

    // 两处都压着：上方那个视图往上挪（比往左挪得少），连同它的尺寸一起；挪完不压技术要求、不压别的视图。
    var main = new PlanView("主视图", null, ViewAxis.Free, [new SheetRect(0.300, 0.060, 0.400, 0.140), new SheetRect(0.290, 0.060, 0.300, 0.140)]);
    var other = new PlanView("左视图", null, ViewAxis.Free, [new SheetRect(0.050, 0.030, 0.200, 0.120)]);
    var plan = TechNotePlanner.Place(a3, [main, other], 0.100, 0.050, null, avoid: true);
    True(plan.Free && plan.Slot == NoteSlot.AboveTitle, plan.Problem);
    Equal(1, plan.Moves.Count);
    Equal("主视图", plan.Moves[0].Name);
    True(plan.Moves[0].Dx == 0 && plan.Moves[0].Dy > 0.044 && plan.Moves[0].Dy < 0.048, $"往上挪约 45 mm，实际 {plan.Moves[0].Dy}");
    var moved = main.Rects.Select(r => new SheetRect(r.Left, r.Bottom + plan.Moves[0].Dy, r.Right, r.Top + plan.Moves[0].Dy)).ToList();
    True(!moved.Any(keep.Overlaps) && !moved.Any(other.Rects[0].Overlaps) && moved.All(r => r.Within(a3.Frame)), "挪完不压");

    // 对齐的下视图压着上方：自己只能上下挪（往上撞主视图），就挪它的父视图（主视图带着它往左）。
    var parent = new PlanView("主视图", null, ViewAxis.Free, [new SheetRect(0.250, 0.140, 0.410, 0.250)]);
    var below = new PlanView("下视图", "主视图", ViewAxis.Vertical, [new SheetRect(0.300, 0.060, 0.400, 0.130)]);
    var blocker = new PlanView("局部", null, ViewAxis.Free, [new SheetRect(0.120, 0.010, 0.230, 0.060)]);
    var aligned = TechNotePlanner.Place(a3, [parent, below, blocker], 0.100, 0.050, NoteSlot.AboveTitle, avoid: true);
    True(aligned.Free && aligned.Slot == NoteSlot.AboveTitle, aligned.Problem);
    Equal("主视图", string.Join(",", aligned.Moves.Select(m => m.Name)));
    True(aligned.Moves[0].Dy == 0 && aligned.Moves[0].Dx < 0, "主视图带着下视图往左挪");
    var belowMoved = below.Rects[0];
    belowMoved = new SheetRect(belowMoved.Left + aligned.Moves[0].Dx, belowMoved.Bottom, belowMoved.Right + aligned.Moves[0].Dx, belowMoved.Top);
    True(!belowMoved.Overlaps(keep), "跟着走的下视图让开了");

    // 只能左右挪的右视图挡着、往左会撞主视图：不能往上（对齐约束），挪父视图。
    var mainRow = new PlanView("主视图", null, ViewAxis.Free, [new SheetRect(0.150, 0.060, 0.280, 0.140)]);
    var right = new PlanView("右视图", "主视图", ViewAxis.Horizontal, [new SheetRect(0.300, 0.060, 0.360, 0.140)]);
    var rowPlan = TechNotePlanner.Place(a3, [mainRow, right, blocker], 0.100, 0.050, NoteSlot.AboveTitle, avoid: true);
    True(rowPlan.Free && rowPlan.Moves.All(m => m.Name == "主视图" || m.Dy == 0), "对齐子视图不往对齐以外的方向挪");

    // 整页都是视图、挪不开：放原处（上方），说明压着谁。
    var full = new PlanView("大视图", null, ViewAxis.Free, [new SheetRect(0.006, 0.006, 0.414, 0.291)]);
    var stuck = TechNotePlanner.Place(a3, [full], 0.100, 0.050, null, avoid: true);
    True(!stuck.Free && stuck.Slot == NoteSlot.AboveTitle && stuck.Moves.Count == 0 && stuck.Problem.Contains("大视图", StringComparison.Ordinal), stuck.Problem);
    True(!left.Overlaps(keep), "两处本身不重叠");
}

static void TestTechNoteReplace()
{
    var a3 = SheetSpace.Standard(0.420, 0.297);
    // 原来在左侧：换一份（更大）仍放左侧，即使上方空着。
    var bigger = TechNotePlanner.Place(a3, [], 0.140, 0.070, NoteSlot.LeftOfTitle, avoid: true);
    True(bigger.Free && bigger.Slot == NoteSlot.LeftOfTitle && bigger.Moves.Count == 0, "原处空着就放原处");
    var rect = new SheetRect(bigger.TopLeft.X, bigger.TopLeft.Y - 0.070, bigger.TopLeft.X + 0.140, bigger.TopLeft.Y);
    Near(a3.TitleBlock.Left - DrawingPlanner.Gap, rect.Right);

    // 原处被视图压着：先在原处挪视图，不跑去另一处。
    var view = new PlanView("主视图", null, ViewAxis.Free, [new SheetRect(0.150, 0.050, 0.220, 0.120)]);
    var moved = TechNotePlanner.Place(a3, [view], 0.140, 0.070, NoteSlot.LeftOfTitle, avoid: true);
    True(moved.Free && moved.Slot == NoteSlot.LeftOfTitle && moved.Moves.Count == 1, "原处挪得开就在原处");
    // 新加的同样情况：上方空着就放上方，不挪视图。
    var fresh = TechNotePlanner.Place(a3, [view], 0.140, 0.070, null, avoid: true);
    True(fresh.Free && fresh.Slot == NoteSlot.AboveTitle && fresh.Moves.Count == 0, "新加的先找空着的");

    // 原处太宽放不下（比标题栏左边的空还宽）：换到上方。
    var wide = TechNotePlanner.Place(a3, [], 0.240, 0.040, NoteSlot.LeftOfTitle, avoid: true);
    True(wide.Free && wide.Slot == NoteSlot.AboveTitle, wide.Problem);
}

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
static void TestFilletCircles()
{
    // 圆心角：起点到弧中点夹角的两倍；起终点重合（带一个端点的整圈）是 2π。
    var c = new SheetPoint(0, 0);
    True(Math.Abs(FilletPlanner.SweepOf(c, new SheetPoint(1, 0), new SheetPoint(0, 1), new SheetPoint(Math.Sqrt(0.5), Math.Sqrt(0.5))) - Math.PI / 2) < 1e-9, "四分之一圆");
    True(Math.Abs(FilletPlanner.SweepOf(c, new SheetPoint(1, 0), new SheetPoint(0, 1), new SheetPoint(-Math.Sqrt(0.5), -Math.Sqrt(0.5))) - 1.5 * Math.PI) < 1e-9, "四分之三圆");
    True(Math.Abs(FilletPlanner.SweepOf(c, new SheetPoint(1, 0), new SheetPoint(1, 0), new SheetPoint(1, 0)) - 2 * Math.PI) < 1e-9, "带一个端点的整圈");

    FilletArc Arc(int index, double cx, double cy, double r, bool concave, double sweep)
        => new(index, new SheetPoint(cx, cy), r, r * 2, new SheetPoint(cx + r, cy), concave, sweep);

    var arcs = new List<FilletArc>
    {
        Arc(0, 0.050, 0.050, 0.005, false, Math.PI / 2),          // 外圆角 R：留
        Arc(1, 0.100, 0.050, 0.003, true, Math.PI / 2),           // 内圆角：留
        Arc(2, 0.150, 0.050, 0.004, true, 2 * Math.PI),           // 带一个端点的整圆孔：去
        Arc(3, 0.200, 0.050, 0.004, true, 0.6 * Math.PI),         // 被切成两段的孔（0.6π + 1.4π）：两段都去
        Arc(4, 0.200, 0.050, 0.004, true, 1.4 * Math.PI),
        Arc(5, 0.250, 0.050, 0.004, true, 1.2 * Math.PI),         // 被零件边切掉一截的孔口（内凹过半圈）：去
        Arc(6, 0.300, 0.050, 0.010, false, 1.2 * Math.PI),        // 外凸过半圈（耳板端头）：留
        Arc(7, 0.350, 0.050, 0.004, true, Math.PI / 3),           // 与认出的孔同圆：去
        Arc(8, 0.350, 0.050, 0.008, false, Math.PI),              // 与孔同心、半径不同的外凸端头 R：留
        Arc(9, 0.400, 0.050, 0.006, false, 2 * Math.PI),          // 外凸整圆（凸台）：留，标 Ø
        Arc(10, 0.450, 0.050, 0.005, true, 0),                    // 圆心角没量（0）：不参与整圆判定
    };
    // 用户定：识别所有圆，减去孔（认得出是孔的一律减，不替孔标注补漏）；剩下的整圆标 Ø。
    var (kept, excluded) = FilletPlanner.WithoutCircles(arcs, [new HoleEdge(0, 0.350, 0.050, 0.004)]);
    Equal("0,1,6,8,9,10", string.Join(",", kept.Select(arc => arc.Index)));
    Equal(5, excluded);

    // 剩下的整圆：标 Ø（不吃「未注圆角 R1」），同直径 3 个以上合标；文字从圆心往右上；被切成两段的凸台算一个。
    var bosses = new List<FilletArc>
    {
        Arc(0, 0.050, 0.150, 0.001, false, 2 * Math.PI),          // Ø4 凸台（半径正好 1：不按 R1 跳过）
        Arc(1, 0.100, 0.150, 0.003, false, 2 * Math.PI),          // Ø12 ×3
        Arc(2, 0.150, 0.150, 0.003, false, 2 * Math.PI),
        Arc(3, 0.200, 0.150, 0.003, false, 0.8 * Math.PI),        // 被切成两段的同一个凸台
        Arc(4, 0.200, 0.150, 0.003, false, 1.2 * Math.PI),
        Arc(5, 0.300, 0.150, 0.005, false, Math.PI / 2),          // 圆角 R10：照旧 R
    };
    var plan = FilletPlanner.Plan(bosses, [], []);
    Equal(1, plan.ArcCount);
    Equal(4, plan.CircleCount);
    Equal(0, plan.DefaultCount);
    Equal(3, plan.Targets.Count);
    var grouped = plan.Targets.Single(t => t.Count == 3);
    True(grouped.Diameter && grouped.Prefix == "3 x ", "同直径 3 个合标「3 x Ø」");
    var single = plan.Targets.Single(t => t.Index == 0);
    True(single.Diameter && single.TextAt.X > 0.050 && single.TextAt.Y > 0.150, "Ø 文字在右上");
    True(!plan.Targets.Single(t => t.Index == 5).Diameter, "圆角弧仍标 R");
    // 已有直径尺寸的整圆跳过。
    Equal(1, FilletPlanner.Plan([bosses[0]], [(new SheetPoint(0.050, 0.150), 0.001)], []).Dimensioned);
}

static void TestOverlapCheck()
{
    // 两个尺寸：A 的数字压在 B 的数字上、也压在 B 的尺寸界线上；C 压视图轮廓线；D 出了图框；E 干净；A 自己的尺寸线不算。
    CheckedAnnotation Note(int index, string name, double x, double y, params SheetSegment[] lines)
        => new(index, "工程图视图1", name, "尺寸", lines, [new TextBox(x, y, 0.006, 0.0035)]);

    var a = Note(0, "D1", 0.100, 0.100, new SheetSegment(0.095, 0.0995, 0.110, 0.0995));
    var b = Note(1, "D2", 0.103, 0.101, new SheetSegment(0.104, 0.090, 0.104, 0.110));
    var c = Note(2, "D3", 0.150, 0.100);
    var d = Note(3, "D4", 0.410, 0.100);
    var e = Note(4, "D5", 0.200, 0.200);
    var issues = AnnotationCheckPlanner.Overlaps([a, b, c, d, e],
        [new SheetSegment(0.153, 0.090, 0.153, 0.110)], [], [], new SheetRect(0.005, 0.005, 0.415, 0.292));

    True(issues.Count(i => i.Index == 0 && i.Other == 1 && i.Reason.Contains("文字重叠", StringComparison.Ordinal)) == 1, "A 与 B 文字重叠报一次");
    True(!issues.Any(i => i.Index == 1 && i.Other == 0 && i.Reason.Contains("文字重叠", StringComparison.Ordinal)), "一对只报一次");
    True(issues.Any(i => i.Index == 0 && i.Other == 1 && i.Reason.Contains("线上", StringComparison.Ordinal)), "A 压 B 的尺寸界线");
    True(!issues.Any(i => i.Index == 1 && i.Other == 0 && i.Reason.Contains("线上", StringComparison.Ordinal)), "B 没压 A 的尺寸线");
    True(!issues.Any(i => i.Index == i.Other), "自己的线不算");
    True(issues.Any(i => i.Index == 2 && i.Reason.Contains("视图轮廓线", StringComparison.Ordinal)), "C 压视图线");
    True(issues.Any(i => i.Index == 3 && i.Reason.Contains("出了图框", StringComparison.Ordinal)), "D 出图框");
    True(!issues.Any(i => i.Index == 4), "E 干净");

    // 压图纸上的注释（技术要求、标题栏的字）与图框 / 标题栏线。
    var f = Note(0, "D6", 0.300, 0.040);
    var sheet = AnnotationCheckPlanner.Overlaps([f], [], [new SheetSegment(0.290, 0.042, 0.320, 0.042)], [("技术要求", new SheetRect(0.302, 0.030, 0.340, 0.060))], null);
    True(sheet.Any(i => i.Reason.Contains("图纸注释「技术要求」", StringComparison.Ordinal)), "压技术要求");
    True(sheet.Any(i => i.Reason.Contains("图框 / 标题栏线", StringComparison.Ordinal)), "压标题栏线");
    Equal("中心符号线", AnnotationCheckPlanner.KindName(13));
}

static void TestArcCenter()
{
    // 1.14.2（用户定）：圆弧两端与直线相切不标圆心；都不相切标两个；一端相切只标一个——切线水平只标水平、竖直只标竖直、斜的取水平。
    // 板：左边 x = 0、上边 y = 0.100（图纸，比例 1:2）。
    const double scale = 0.5;
    FilletArc Arc(int index, double cx, double cy, double r, SheetPoint? start, SheetPoint? end, double sweep = Math.PI / 2)
        => new(index, new SheetPoint(cx, cy), r, r / scale, new SheetPoint(cx + r, cy), false, sweep, start, end);
    var left = new SheetSegment(0, 0, 0, 0.100);
    var top = new SheetSegment(0, 0.100, 0.200, 0.100);

    // 右上角 R5：上边接 (0.190,0.100)，右边接 (0.200,0.090)，两端都切。
    var corner = Arc(0, 0.190, 0.090, 0.010, new SheetPoint(0.190, 0.100), new SheetPoint(0.200, 0.090));
    var topToCorner = new SheetSegment(0, 0.100, 0.190, 0.100);
    var rightSide = new SheetSegment(0.200, 0.090, 0.200, 0);
    var lines = new List<SheetSegment> { left, topToCorner, rightSide };
    Equal(2, ArcCenterPlanner.Tangents(corner, lines).Count);
    Equal(0, ArcCenterPlanner.Needs(ArcCenterPlanner.Tangents(corner, lines)).Count);

    // 整圆凸台：没有端点 → 两个。
    var boss = Arc(1, 0.050, 0.050, 0.008, null, null, 2 * Math.PI);
    Equal("Horizontal,Vertical", string.Join(",", ArcCenterPlanner.Needs(ArcCenterPlanner.Tangents(boss, lines))));

    // 一端切水平线（另一端接斜线、不相切）→ 只标水平尺寸（以上侧基准的竖直尺寸不能标）。
    var bottom = new SheetSegment(0.100, 0, 0.140, 0);
    var halfTangent = Arc(2, 0.100, 0.010, 0.010, new SheetPoint(0.100, 0), new SheetPoint(0.090, 0.010));
    var slanted = new SheetSegment(0.090, 0.010, 0.080, 0.030);
    var needs = ArcCenterPlanner.Needs(ArcCenterPlanner.Tangents(halfTangent, [bottom, slanted]));
    Equal("Horizontal", string.Join(",", needs));
    // 一端切竖直线 → 只标竖直。
    var wall = new SheetSegment(0.150, 0.060, 0.150, 0.020);
    var verticalTangent = Arc(3, 0.140, 0.060, 0.010, new SheetPoint(0.150, 0.060), new SheetPoint(0.140, 0.070));
    Equal("Vertical", string.Join(",", ArcCenterPlanner.Needs(ArcCenterPlanner.Tangents(verticalTangent, [wall]))));
    // 一端切斜线 → 只标一个，取水平。
    var (ux, uy) = (Math.Sqrt(0.5), Math.Sqrt(0.5));
    var obliqueArc = Arc(4, 0.060, 0.060, 0.010, new SheetPoint(0.060 + 0.010 * ux, 0.060 - 0.010 * uy), new SheetPoint(0.050, 0.060));
    var oblique = new SheetSegment(0.060 + 0.010 * ux, 0.060 - 0.010 * uy, 0.060 + 0.010 * ux + 0.020 * ux, 0.060 - 0.010 * uy + 0.020 * uy);
    Equal("Horizontal", string.Join(",", ArcCenterPlanner.Needs(ArcCenterPlanner.Tangents(obliqueArc, [oblique]))));
    // 两端切的线平行（腰形端头切上下两条水平边）→ 按一端相切，只标水平。
    var slotEnd = Arc(5, 0.180, 0.050, 0.010, new SheetPoint(0.180, 0.060), new SheetPoint(0.180, 0.040), Math.PI);
    var upper = new SheetSegment(0.120, 0.060, 0.180, 0.060);
    var lower = new SheetSegment(0.120, 0.040, 0.180, 0.040);
    Equal("Horizontal", string.Join(",", ArcCenterPlanner.Needs(ArcCenterPlanner.Tangents(slotEnd, [upper, lower]))));
    // 端点接上了直线但不相切（直线沿半径方向）不算。
    var radial = new SheetSegment(0.150, 0.060, 0.170, 0.060);
    Equal(0, ArcCenterPlanner.Tangents(verticalTangent, [radial]).Count);

    // 整体规划：角 R 不标；凸台两个；与孔同心的跳过；已有水平尺寸量到凸台圆心的只补竖直。
    var ear = Arc(6, 0.120, 0.080, 0.012, new SheetPoint(0.130, 0.080), new SheetPoint(0.120, 0.090));
    var hole = new HoleEdge(0, 0.120, 0.080, 0.004);
    var plan = ArcCenterPlanner.Plan([corner, boss, ear, halfTangent], [left, top, topToCorner, rightSide, bottom, slanted], [hole],
        0, 0.100, scale, [], 0.020, 0.020);
    Equal(4, plan.CenterCount);
    Equal(1, plan.Tangent);
    Equal(1, plan.OnHole);
    Equal("Horizontal:1:0.1,Horizontal:2:0.2,Vertical:1:0.1", string.Join(",", plan.Targets.Select(target =>
        $"{target.Axis}:{target.ArcIndex}:{(target.Value).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}")));
    // 水平尺寸在上侧基准上方从 20 mm 起一层一层、近的在里；竖直尺寸在左侧基准左边。
    bool At(SheetPoint actual, double x, double y) => Math.Abs(actual.X - x) < 1e-12 && Math.Abs(actual.Y - y) < 1e-12;
    True(At(plan.Targets[0].TextAt, 0.025, 0.120), "第一个水平尺寸：圆心与基准正中、上侧基准上方 20 mm");
    True(At(plan.Targets[1].TextAt, 0.050, 0.120 + HolePositionPlanner.TierStep), "第二个水平尺寸外一层");
    True(At(plan.Targets[2].TextAt, -0.020, 0.075), "竖直尺寸在左侧基准左边");

    var measured = new ViewDimension(0, 2, false, 0.050 / scale, [L(0, 0, 0, 0.100), C(0.050, 0.050)]);
    var partial = ArcCenterPlanner.Plan([boss], [left, top], [], 0, 0.100, scale, [measured], 0.020, 0.020);
    Equal("Vertical", string.Join(",", partial.Targets.Select(target => target.Axis)));
    Equal(1, partial.Present);

    // 同心的两段弧：各自一端相切、切线一横一竖 → 合起来定死，不标。
    var a = Arc(7, 0.100, 0.050, 0.010, new SheetPoint(0.100, 0.040), new SheetPoint(0.090, 0.050));
    var b = Arc(8, 0.100, 0.050, 0.020, new SheetPoint(0.120, 0.050), new SheetPoint(0.100, 0.070));
    var together = ArcCenterPlanner.Plan([a, b], [left, top, new SheetSegment(0.100, 0.040, 0.110, 0.040), new SheetSegment(0.120, 0.050, 0.120, 0.030)], [],
        0, 0.100, scale, [], 0.020, 0.020);
    Equal(0, together.Targets.Count);
    Equal(1, together.Tangent);

    // 圆心落在对称轴上的那个方向不标。
    var onAxis = ArcCenterPlanner.Plan([boss], [left, top], [], 0, 0.100, scale, [], 0.020, 0.020, [new SymmetryAxis(PositionAxis.Horizontal, 0.050, 0, 1)]);
    Equal("Vertical", string.Join(",", onAxis.Targets.Select(target => target.Axis)));

    // 第二轮去重（用户截图：右视图两个圆心 X 相同标了两个「20」）：X 相同的只标上面那个，两个圆心连一根竖的中心线；Y 不同照标。
    var upperCircle = Arc(9, 0.040, 0.078, 0.004, null, null, 2 * Math.PI);
    var lowerCircle = Arc(10, 0.040, 0.044, 0.012, null, null, 2 * Math.PI);
    var stacked = ArcCenterPlanner.Plan([lowerCircle, upperCircle], [left, top], [], 0, 0.100, scale, [], 0.020, 0.020);
    Equal("Horizontal:9,Vertical:9,Vertical:10", string.Join(",", stacked.Targets.Select(target => $"{target.Axis}:{target.ArcIndex}")));
    Equal(1, stacked.Aligned);
    Equal(1, stacked.CenterLinks.Count);
    var link = stacked.CenterLinks[0];
    True(link.Axis == PositionAxis.Horizontal && link.First.ArcIndex == 10 && link.Second.ArcIndex == 9, "竖的中心线从下面的圆心连到上面的");
    // 已经有线串着就不再加：中心线在圆心处断开一点也算。
    var drawnLine = new SheetSegment(0.040, 0.044 + 0.001, 0.040, 0.078 + 0.003);
    True(ArcCenterPlanner.Covers(drawnLine, link), "竖线盖过两个圆心");
    True(!ArcCenterPlanner.Covers(new SheetSegment(0.041, 0.040, 0.041, 0.090), link), "不在同一 X 上不算");
    True(!ArcCenterPlanner.Covers(new SheetSegment(0.040, 0.060, 0.040, 0.090), link), "没盖到下面的圆心不算");
    var stackedDrawn = ArcCenterPlanner.Plan([lowerCircle, upperCircle], [left, top], [], 0, 0.100, scale, [], 0.020, 0.020, drawn: [drawnLine]);
    Equal(0, stackedDrawn.CenterLinks.Count);
    Equal(1, stackedDrawn.LinkPresent);
    Equal(3, stackedDrawn.Targets.Count);

    // 真机（2026-10-08 用户图右视图 Drawing View2，1:1，读回的图纸坐标 mm）：40 × 45 的块上一个键槽形型腔——
    // 下面 R13.68 被左右两条竖直平边截断，上面 R11.13 接两条斜边，两端都不相切；视图左右完全对称、没有孔。
    // 1.14.1 要有孔才加对称轴，这里一根都没加、两个圆心各标了一个「20」（用户截图）。1.14.2：有要定位的圆弧圆心也加轴，轴上的方向不标。
    static SheetSegment M(double x1, double y1, double x2, double y2) => new(x1 / 1000, y1 / 1000, x2 / 1000, y2 / 1000);
    static SheetPoint Pt(double x, double y) => new(x / 1000, y / 1000);
    List<SheetSegment> keyLines =
    [
        M(155.32, 172.68, 153.32, 174.68), M(155.32, 172.68, 155.32, 131.68), M(153.32, 129.68, 155.32, 131.68), M(153.32, 129.68, 117.32, 129.68),
        M(115.32, 131.68, 117.32, 129.68), M(117.32, 174.68, 153.32, 174.68), M(140.176, 171.883, 148.32, 157.632), M(122.32, 157.632, 130.464, 171.883),
        M(122.32, 148.419, 122.32, 157.632), M(148.32, 157.632, 148.32, 148.419), M(115.32, 131.68, 115.32, 172.68), M(117.32, 174.68, 115.32, 172.68),
    ];
    var topArc = new FilletArc(0, Pt(135.32, 161.874), 0.011125, 0.011125, Pt(135.32, 172.999), true, 0.903, Pt(130.464, 171.883), Pt(140.176, 171.883));
    var bottomArc = new FilletArc(1, Pt(135.32, 152.68), 0.013681, 0.013681, Pt(135.32, 138.999), true, 2.508, Pt(148.32, 148.419), Pt(122.32, 148.419));
    List<SheetSegment> keyCurves =
    [
        M(130.464, 171.883, 135.32, 172.999), M(135.32, 172.999, 140.176, 171.883),
        M(148.32, 148.419, 135.32, 138.999), M(135.32, 138.999, 122.32, 148.419),
    ];
    var keyArcs = new List<FilletArc> { topArc, bottomArc };
    Equal(0, ArcCenterPlanner.Tangents(topArc, keyLines).Count);
    Equal(0, ArcCenterPlanner.Tangents(bottomArc, keyLines).Count);
    var locatable = ArcCenterPlanner.Locatable(keyArcs, keyLines, []);
    Equal(2, locatable.Count);
    Equal(0, SymmetryPlanner.Axes(keyLines, keyCurves, []).Count);
    var keyAxes = SymmetryPlanner.Axes(keyLines, keyCurves, [], locatable);
    Equal(1, keyAxes.Count);
    True(keyAxes[0].Axis == PositionAxis.Horizontal && Math.Abs(keyAxes[0].At - 0.13532) < 1e-9, "竖直对称轴在 x = 135.32");
    Equal("10,1", $"{keyAxes[0].FirstLine},{keyAxes[0].SecondLine}");
    // 有轴：两个圆心都在轴上，水平不标、也不用连线；竖直照标 22 与 12.81。
    var keyPlan = ArcCenterPlanner.Plan(keyArcs, keyLines, [], 0.11532, 0.17468, 1, [], 0.020, 0.020, keyAxes);
    Equal("Vertical:0:12.806,Vertical:1:22", string.Join(",", keyPlan.Targets.Select(target =>
        $"{target.Axis}:{target.ArcIndex}:{(target.Value * 1000).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}")));
    Equal(0, keyPlan.CenterLinks.Count);
    // 没有轴（尺寸链模式）：X 都是 20，只标上面那个，两个圆心连一根竖的中心线。
    var keyChain = ArcCenterPlanner.Plan(keyArcs, keyLines, [], 0.11532, 0.17468, 1, [], 0.020, 0.020);
    Equal(1, keyChain.Targets.Count(target => target.Axis == PositionAxis.Horizontal));
    Equal(1, keyChain.CenterLinks.Count);

    // 与孔 X 相同：水平一个都不标，连到最近的孔（孔边下标）；竖直照标。
    var nearHole = new HoleEdge(3, 0.040, 0.020, 0.003);
    var farHole = new HoleEdge(4, 0.040, 0.090, 0.003);
    var withHole = ArcCenterPlanner.Plan([upperCircle], [left, top], [farHole, nearHole], 0, 0.100, scale, [], 0.020, 0.020);
    Equal("Vertical", string.Join(",", withHole.Targets.Select(target => target.Axis)));
    Equal(1, withHole.CenterLinks.Count);
    Equal(4, withHole.CenterLinks[0].Second.HoleIndex ?? -1);

    // 与已有尺寸量过的圆心 Y 相同：竖直不标，连到它。
    var measuredCircle = Arc(11, 0.090, 0.078, 0.004, null, null, 2 * Math.PI);
    var vertical = new ViewDimension(0, 2, false, 0.022 / scale, [L(0, 0.100, 0.200, 0.100), C(0.090, 0.078)]);
    var withMeasured = ArcCenterPlanner.Plan([upperCircle, measuredCircle], [left, top], [], 0, 0.100, scale, [vertical], 0.020, 0.020);
    Equal("Horizontal:9,Horizontal:11", string.Join(",", withMeasured.Targets.Select(target => $"{target.Axis}:{target.ArcIndex}")));
    Equal(1, withMeasured.CenterLinks.Count);
    True(withMeasured.CenterLinks[0].Axis == PositionAxis.Vertical, "Y 相同连横线");
}

static void TestChamferAvoid()
{
    // 1.15.0（用户：倒角标注没做避障）：近的那一档压着别的标注就往外让一档、或沿尺寸线滑开，不再只在两个固定位置里挑压得少的。
    static double M(double mm) => mm / 1000;
    SheetSegment S(double x1, double y1, double x2, double y2) => new(M(x1), M(y1), M(x2), M(y2));
    var chamferLine = S(15, 0, 20, 5);
    var lines = new List<SheetSegment> { S(0, 0, 0, 30), S(20, 5, 20, 30), S(0, 30, 20, 30), S(0, 0, 15, 0), chamferLine };
    var chamfers = new List<ChamferEdge> { new(0, chamferLine, M(5), M(5), "B") };
    var none = new HashSet<string>();
    // 右边整条被占着（例如外轮廓尺寸的字）：竖直尺寸放哪一档都压。
    var rightBlock = new TextBox(M(26), M(-2), M(4), M(10));

    // 下边近档压着一段文字，尺寸线、尺寸界线都会穿过它：往外让一档（-14 mm），不滑。
    var low = new TextBox(M(16), M(-9), M(3), M(3));
    var stepped = ChamferPlanner.Plan(chamfers, lines, lines, [], none, texts: [low, rightBlock]).Targets[0].Placements[0];
    Equal(PositionAxis.Horizontal, stepped.Axis);
    Near(-ChamferPlanner.Offsets[1], stepped.At.Y);
    Near(M(17.5), stepped.At.X);

    // 文字框压着、尺寸线不穿：同一档沿尺寸线滑开（往右 5 mm）。
    var high = new TextBox(M(16), M(-6.5), M(3), M(2));
    var slid = ChamferPlanner.Plan(chamfers, lines, lines, [], none, texts: [high, rightBlock]).Targets[0].Placements[0];
    Equal(PositionAxis.Horizontal, slid.Axis);
    Near(-ChamferPlanner.Offset, slid.At.Y);
    Near(M(17.5) + ChamferPlanner.Slides[1], slid.At.X);

    // 避障关：照旧近档不滑。
    var off = ChamferPlanner.Plan(chamfers, lines, lines, [], none, texts: [low, rightBlock], avoid: false).Targets[0].Placements[0];
    Equal(PositionAxis.Horizontal, off.Axis);
    Near(-ChamferPlanner.Offset, off.At.Y);

    // 尺寸自己的线：水平尺寸两条界线从斜边两端竖下来，尺寸线跨两条界线。
    var leaders = ChamferPlanner.Leaders(chamferLine, new ChamferPlacement(PositionAxis.Horizontal, new SheetPoint(M(17.5), M(-8))),
        new TextBox(M(15), M(-7), M(5), M(3))).ToList();
    Equal(3, leaders.Count);
    Equal(S(15, 0, 15, -8), leaders[0]);
    Equal(S(20, 5, 20, -8), leaders[1]);
}

static void TestHoleCoverage()
{
    // 1.15.0（用户定）：同一页视线平行的视图（顶视图对底视图）里，同一种孔只标一个孔标注、同一个孔的位置只标一处；
    // 沉孔在背面时孔标注留给看得见沉孔的视图，没有才写「(反面)」。
    var box = new ModelBox(0, 0, 0, 0.1, 0.06, 0.01);
    var cbore = new PartCylinder(new ModelDirection(0, 0, 1), new ModelDirection(0.05, 0.03, 0), 0.003, true, true, "CB", [new ModelDirection(0, 0, 1)], 0.006, 0.01);
    var drill = new PartCylinder(new ModelDirection(0, 0, -1), new ModelDirection(0.05, 0.03, 0), 0.0017, true, true, "CB", [], -0.006, 0);
    var part = new PartGeometry(box, [cbore, drill], []);
    // 沉孔朝 +Z（沉孔那截在 z 6–10，底孔在 0–6）。
    Equal(new ModelDirection(0, 0, 1), part.Holes.Single().Counterbore);
    True(new PartGeometry(box, [drill], []).Holes.Single().Counterbore is null, "单段孔不是沉孔");
    True(new PartGeometry(box, [cbore with { Feature = "扩口" }, drill], []).Holes.Single().Counterbore is null, "两个特征叠出来的不算（孔标注里没有沉孔那行）");

    // 同一个孔从两头看：轴向相反、取的点不同，仍是同一条轴线；别的组件、挪开 0.1 mm 的不是。
    var top = HoleAxis.Of("", new ModelDirection(0.05, 0.03, 0.01), new ModelDirection(0, 0, 1));
    var bottom = HoleAxis.Of("", new ModelDirection(0.05, 0.03, 0), new ModelDirection(0, 0, -1));
    True(top.Same(bottom), "两头看同一个孔");
    True(!top.Same(HoleAxis.Of("别的-1", new ModelDirection(0.05, 0.03, 0), new ModelDirection(0, 0, 1))), "别的组件");
    True(!top.Same(HoleAxis.Of("", new ModelDirection(0.0501, 0.03, 0), new ModelDirection(0, 0, 1))), "挪开 0.1 mm");
    True(top.Matches(part.Holes.Single()), "对得上零件里的孔");

    var hole = new HoleEdge(0, 0.05, 0.03, 0.0017, "/CB", Axis: top);
    IReadOnlyList<HoleEdge> kind = [hole];
    var up = new ModelDirection(0, 0, 1);
    var down = new ModelDirection(0, 0, -1);
    var counterbore = HoleCoveragePlanner.Counterbore(kind, part);
    Equal(up, counterbore);
    // 沉孔朝着看图人（顶视图）：标在这里。
    Equal((CalloutPlace.Here, (string?)null), HoleCoveragePlanner.Callout(kind, [], counterbore, up));
    // 底视图、这一页没有顶视图：标在这里写「(反面)」。
    Equal((CalloutPlace.HereBack, (string?)null), HoleCoveragePlanner.Callout(kind, [], counterbore, down));
    // 底视图、有看得见这个孔的顶视图：留给它。
    var topView = new CoveringView("顶视图", up, [top], [], []);
    Equal((CalloutPlace.Deferred, (string?)"顶视图"), HoleCoveragePlanner.Callout(kind, [topView], counterbore, down));
    // 顶视图看不见这个孔（别处的孔）：还是标在这里写「(反面)」。
    var blind = new CoveringView("顶视图", up, [HoleAxis.Of("", new ModelDirection(0.01, 0.01, 0), up)], [], []);
    Equal(CalloutPlace.HereBack, HoleCoveragePlanner.Callout(kind, [blind], counterbore, down).Place);
    // 别的视图已有这一种的孔标注：不论沉孔朝哪都不再标。
    var labeled = new CoveringView("底视图", down, [bottom], [bottom], [bottom]);
    Equal((CalloutPlace.Elsewhere, (string?)"底视图"), HoleCoveragePlanner.Callout(kind, [labeled], counterbore, up));
    Equal(CalloutPlace.Elsewhere, HoleCoveragePlanner.Callout(kind, [labeled], null, up).Place);
    // 位置：别的视图标过的孔去掉；读不到轴线的不算标过。
    True(HoleCoveragePlanner.PositionedElsewhere(hole, [labeled]), "底视图标过位置");
    True(!HoleCoveragePlanner.PositionedElsewhere(hole, [topView]), "顶视图只看得见、没标");
    True(!HoleCoveragePlanner.PositionedElsewhere(hole with { Axis = null }, [labeled]), "没有轴线的不算");
    // 阵列标法只连首尾两个孔（真机模组立板「4 x 75 =300」）：那个视图看得见、同种标过位置的，整种算标过；它看不见的（另一面的盲孔）不算。
    var middle = new HoleEdge(2, 0.06, 0.03, 0.0017, "/CB", Axis: HoleAxis.Of("", new ModelDirection(0.06, 0.03, 0), up));
    var pattern = new CoveringView("底视图", down, [bottom, middle.Axis!.Value], [], [bottom], new HashSet<string> { "/CB" });
    True(HoleCoveragePlanner.PositionedElsewhere(middle, [pattern]), "阵列中间的孔算标过");
    True(!HoleCoveragePlanner.PositionedElsewhere(middle, [pattern with { Holes = [bottom] }]), "那个视图看不见的不算");
    True(!HoleCoveragePlanner.PositionedElsewhere(middle with { Kind = "/M5" }, [pattern]), "别的种不算");

    // 孔标注规划：别处标过的种跳过、记下视图名；其余照旧。
    var other = new HoleEdge(1, 0.02, 0.03, 0.002, "/M5", Axis: HoleAxis.Of("", new ModelDirection(0.02, 0.03, 0), up));
    var plan = HoleCalloutPlanner.Plan([hole, other], [],
        k => k.Any(h => h.Kind == "/CB") ? HoleCoveragePlanner.Callout(k, [labeled], null, up) : (CalloutPlace.Here, null));
    Equal(1, plan.Targets.Count);
    Equal(1, plan.Targets[0].EdgeIndex);
    Equal(1, plan.Elsewhere!.Count);
    Equal((CalloutPlace.Elsewhere, "底视图"), plan.Elsewhere[0]);

    // 「(反面)」：只在沉孔朝背面时；认显示文字里有没有（空格不论）。
    True(HoleCoveragePlanner.IsBack(up, down) && !HoleCoveragePlanner.IsBack(up, up) && !HoleCoveragePlanner.IsBack(null, down), "背面判定");
    True(HoleCoveragePlanner.HasBackMarker(["6 x ⌀5.5 完全贯穿", "⌴⌀9.5 ↓5.5 ( 反面 )"]), "认得出「(反面)」");
    True(!HoleCoveragePlanner.HasBackMarker(["⌀3.30 ↓10.10 (含对面)"]), "「(含对面)」不是「(反面)」");
}

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
