using System.Text.Json;
using HistoryStrenua;
using HistoryVulcan.Core.Commands;

/// <summary>离线测试：测试登记表与共用的断言、样本。</summary>
internal static partial class Tests
{
    /// <summary>全部测试，按这个顺序跑。</summary>
    internal static readonly (string Name, Action Run)[] All =
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
        ("check: dimension network, sizes, cross-view and symmetry", TestDimensionCheck),
        ("check: two-decimal numbers", TestDecimalCheck),
        ("drawing: named view orientation is verified", TestMainViewRetry),
        ("hole position: avoids title block and revision block", TestPositionAvoidsKeepOuts),
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
        ("drawing steps: basic, fillet and one-key run the steps in order", TestDrawingSteps),
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

    static Registrar Registry()
    {
        var registry = new Registrar();
        HistoryStrenuaModule.Register(registry, new QuickCommandRunner(new StrenuaOptions()));
        return registry;
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

    static DimensionAnchor C(double x, double y) => DimensionAnchor.Center(new SheetPoint(x, y));

    static DimensionAnchor L(double x1, double y1, double x2, double y2, bool model = true)
        => DimensionAnchor.Line(new SheetSegment(x1, y1, x2, y2), model);

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

    // 1.8.1 跨视图去重的样件：长方体 X 0..0.2、Y（高）0..0.1、Z（深）0..0.05，比例 1。
    // 主视图：图纸 X = 模型 X、图纸 Y = 模型 Y，模型原点在图纸 (0.1, 0.1)。
    static ViewFrame FrontFrame() => new(new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new SheetPoint(0.1, 0.1), 1.0);

    // 右视图：图纸 X = 模型 -Z、图纸 Y = 模型 Y，模型原点在图纸 (0.5, 0.1)——深度从右往左长。
    static ViewFrame RightFrame() => new(new(0, 0, -1), new(0, 1, 0), new(1, 0, 0), new SheetPoint(0.5, 0.1), 1.0);

    // 俯视图：图纸 X = 模型 X、图纸 Y = 模型 -Z，模型原点在图纸 (0.1, 0.5)。
    static ViewFrame TopFrame() => new(new(1, 0, 0), new(0, 0, -1), new(0, 1, 0), new SheetPoint(0.1, 0.5), 1.0);

    static DimensionAnchor HEdge(double y) => DimensionAnchor.Line(S(0, y, 1, y), true);

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

    static ViewFrame Frame(StandardView view) => new(view.Right, view.Up, view.Normal, new SheetPoint(0, 0), 1);

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
