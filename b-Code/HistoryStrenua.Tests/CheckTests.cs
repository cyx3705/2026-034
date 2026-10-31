using System.Text.Json;
using HistoryStrenua;
using HistoryVulcan.Core.Commands;

/// <summary>离线测试：检查类（Check/）。</summary>
internal static partial class Tests
{
    static void TestDimensionCheck()
    {
        // 1.16.0 重写：尺寸网。比例 1、视图正对 +Z，图纸坐标就是模型坐标。100 × 50 的板，右上角 R5 圆角（两头与直边相切），孔 A(30, 20) ⌀6。
        var frame = new ViewFrame(new ModelDirection(1, 0, 0), new ModelDirection(0, 1, 0), new ModelDirection(0, 0, 1), new SheetPoint(0, 0), 1);
        SheetSegment[] lines =
        [
            new(0, 0, 0, 0.05),          // 0 左
            new(0, 0.05, 0.095, 0.05),   // 1 上（到圆角）
            new(0.1, 0.045, 0.1, 0),     // 2 右（从圆角）
            new(0.1, 0, 0, 0),           // 3 下
        ];
        HoleEdge[] holes = [new(0, 0.03, 0.02, 0.003, "/Cut")];
        FilletArc[] arcs = [new(0, new SheetPoint(0.095, 0.045), 0.005, 0.005, new SheetPoint(0.0985, 0.0485), false, Math.PI / 2,
            new SheetPoint(0.1, 0.045), new SheetPoint(0.095, 0.05))];
        var left = DimensionAnchor.Line(lines[0], true);
        var right = DimensionAnchor.Line(lines[2], true);
        var top = DimensionAnchor.Line(lines[1], true);
        var bottom = DimensionAnchor.Line(lines[3], true);
        var dimensions = new List<ViewDimension>
        {
            new(0, 2, false, 0.1, [left, right]),          // 总长
            new(1, 2, false, 0.05, [bottom, top]),         // 总宽
            new(2, 2, false, 0.03, [left, C(0.03, 0.02)]), // 孔水平
            new(3, 0, true, double.NaN, [C(0.03, 0.02)]),  // 孔标注
        };
        CheckView View(IReadOnlyList<ViewDimension> d, IReadOnlyList<SheetSegment>? centerLines = null, IReadOnlyList<HoleEdge>? h = null)
            => new("前视", "PART|默认", frame, lines, h ?? holes, arcs, d, centerLines ?? []);

        var missing = DimensionCheckPlanner.Check([View(dimensions)]);
        var all = string.Join(" | ", missing.Select(item => item.Text));
        // 孔缺竖直位置；圆角缺 R，圆心两头相切跟着直边、不报位置；直边都在网里。
        True(missing.Count == 2, all);
        True(missing.Any(item => item.Target == CheckTarget.Hole && item.Text.Contains("缺竖直位置尺寸", StringComparison.Ordinal) && !item.Text.Contains("孔标注", StringComparison.Ordinal)), all);
        True(missing.Any(item => item.Target == CheckTarget.Arc && item.Text.Contains("没有 R 尺寸", StringComparison.Ordinal) && !item.Text.Contains("圆心缺", StringComparison.Ordinal)), all);

        dimensions.Add(new(4, 2, false, 0.03, [top, C(0.03, 0.02)]));
        dimensions.Add(new(5, 5, false, 0.005, [C(0.095, 0.045)]));
        True(DimensionCheckPlanner.Check([View(dimensions)]).Count == 0, "补齐后没有漏标");

        // 没有孔标注 → 报孔径；没有总宽 → 上下两条水平边自成一块，报漏标并一起选中（不在主网就报）。
        var bare = DimensionCheckPlanner.Check([View(dimensions.Where(d => d.Index is not 1 and not 3).ToList())]);
        all = string.Join(" | ", bare.Select(item => item.Text));
        True(bare.Any(item => item.Target == CheckTarget.Hole && item.Text.Contains("没有孔标注或 Ø 尺寸", StringComparison.Ordinal)), all);
        True(bare.Any(item => item.Target == CheckTarget.Line && item.Text.Contains("水平边", StringComparison.Ordinal)), all);

        // 跨视图：总宽标在同一模型的另一个视图里也算（同朝向、原点挪开）。
        var otherFrame = frame with { Origin = new SheetPoint(0.2, 0) };
        SheetSegment Shift(SheetSegment line) => new(line.X1 + 0.2, line.Y1, line.X2 + 0.2, line.Y2);
        var other = new CheckView("另一个", "PART|默认", otherFrame, lines.Select(Shift).ToList(), [], [],
            [new(0, 2, false, 0.05, [DimensionAnchor.Line(Shift(lines[3]), true), DimensionAnchor.Line(Shift(lines[1]), true)]),
             new(1, 2, false, 0.1, [DimensionAnchor.Line(Shift(lines[0]), true), DimensionAnchor.Line(Shift(lines[2]), true)])], []);
        var cross = DimensionCheckPlanner.Check([View(dimensions.Where(d => d.Index != 1).ToList()), other]);
        True(cross.Count == 0, "总宽在别的视图标了：" + string.Join(" | ", cross.Select(item => item.Text)));

        // 对称轴：x = 50 的中心线，两侧孔 B(20)、C(80) 互标 60，轴上的孔 D(50) 不报水平位置。
        HoleEdge[] symmetric = [new(0, 0.02, 0.02, 0.002, "/S"), new(1, 0.08, 0.02, 0.002, "/S"), new(2, 0.05, 0.02, 0.002, "/S")];
        var sym = new List<ViewDimension>
        {
            new(0, 2, false, 0.1, [left, right]),
            new(1, 2, false, 0.05, [bottom, top]),
            new(2, 2, false, 0.06, [C(0.02, 0.02), C(0.08, 0.02)]),
            new(3, 2, false, 0.03, [top, C(0.02, 0.02)]),
            new(4, 0, true, double.NaN, [C(0.02, 0.02)]),
            new(5, 5, false, 0.005, [C(0.095, 0.045)]),
        };
        var axis = DimensionCheckPlanner.Check([View(sym, [new SheetSegment(0.05, -0.005, 0.05, 0.055)], symmetric)]);
        True(axis.Count == 0, "对称轴两侧连通、轴上的孔跟着轴：" + string.Join(" | ", axis.Select(item => item.Text)));
        var noAxis = DimensionCheckPlanner.Check([View(sym, [], symmetric)]);
        True(noAxis.Count == 3 && noAxis.All(item => item.Text.Contains("缺水平位置尺寸", StringComparison.Ordinal)),
            "没有对称轴时三个孔的水平位置都没连进主网：" + string.Join(" | ", noAxis.Select(item => item.Text)));
    }

    static void TestDecimalCheck()
    {
        // 1.16.0「两位小数」：取与尺寸值对得上的那段字当主数字，公差不算。
        Equal("12.50", DecimalCheck.MainNumber(["12.50"], 12.5)!);
        Equal(2, DecimalCheck.Decimals("12.50"));
        Equal("12.81", DecimalCheck.MainNumber(["±0.02", "12.81"], 12.81)!);
        Equal(string.Empty, DecimalCheck.MainNumber(["30", "±0.02"], 30)!);
        Equal(0, DecimalCheck.Decimals(string.Empty));
        Equal("2.5", DecimalCheck.MainNumber(["4 x 2.5 =", "10"], 2.5)!);
        True(DecimalCheck.MainNumber([], 3) is null, "没有字算读不出");
    }

    static void TestSnapshotName()
    {
        Equal("Draw1 - 图纸1", DrawingSnapshot.SafeName("Draw1 - 图纸1"));
        Equal("a_b_c__", DrawingSnapshot.SafeName("a/b:c*?"));
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
    /// <summary>1.21.0：图纸 SVG——坐标是图纸 mm、几何带编号、标注方框加字、图框格子整块。</summary>
    static void TestDrawingSvg()
    {
        var sheet = new SvgSheet("底板", "图纸1", 0.420, 0.297, new SheetRect(0.005, 0.005, 0.415, 0.292),
            [new SvgBlock("title-block", "标题栏", new SheetRect(0.235, 0.005, 0.415, 0.049), "底板 | WTJYQ-01-08 | 6061")],
            [
                new SvgView("图纸1", "图纸页", "", "", null, [], [], [],
                    [new SvgAnnotation("注释1", "注释", "技术要求\n1. 未注圆角R1", "", new SheetRect(0.3, 0.06, 0.4, 0.09), new SheetPoint(0.3, 0.085), 0.0035, "", [])]),
                new SvgView("工程图视图1", "模型", "", "1:2", new SheetRect(0.05, 0.1, 0.15, 0.2),
                    [new SheetSegment(0.06, 0.11, 0.14, 0.11), new SheetSegment(0.06, 0.11, 0.06, 0.19)],
                    [new SheetSegment(0.14, 0.11, 0.145, 0.115), new SheetSegment(0.145, 0.115, 0.15, 0.12), new SheetSegment(0.1, 0.1, 0.1, 0.1001)],
                    [new HoleEdge(0, 0.08, 0.15, 0.003, "/M6")],
                    [
                        new SvgAnnotation("D1", "线性尺寸", "80", "80", new SheetRect(0.06, 0.2, 0.14, 0.206), new SheetPoint(0.095, 0.201), 0.0035, "x=60 ↔ x=140", [],
                            new SheetRect(0.095, 0.201, 0.1, 0.2045)),
                        new SvgAnnotation("D2", "线性尺寸", "80", "80", null, new SheetPoint(0.05, 0.13), 0.0035, "y=110 ↔ y=190", [], null, Math.PI / 2),
                        new SvgAnnotation("中心符号线1", "中心符号线", "", "", null, null, 0, "", [new SheetSegment(0.075, 0.15, 0.085, 0.15)]),
                    ],
                    "钣金折弯线 2 条没有画"),
            ]);
        var svg = DrawingSvgWriter.Write(sheet);
        var document = System.Xml.Linq.XDocument.Parse(svg);
        var ns = (System.Xml.Linq.XNamespace)"http://www.w3.org/2000/svg";
        var root = document.Root!;
        Equal("0 0 420 297", root.Attribute("viewBox")!.Value);
        Equal("bottom-left", root.Attribute("data-origin")!.Value);
        // 整页翻一次 Y，里面的数字就是图纸 mm。
        Equal("matrix(1 0 0 -1 0 297)", root.Element(ns + "g")!.Attribute("transform")!.Value);

        var edge = document.Descendants(ns + "g").Single(v => (string?)v.Attribute("id") == "工程图视图1" && (string?)v.Attribute("class") == "view").Descendants().Single(e => (string?)e.Attribute("id") == "E0");
        Equal("60,110,140,110", string.Join(",", new[] { "x1", "y1", "x2", "y2" }.Select(a => edge.Attribute(a)!.Value)));
        // 首尾相接的两小段并成一条折线，不相接的另起一条。
        var polylines = document.Descendants(ns + "polyline").ToList();
        Equal(2, polylines.Count);
        Equal("140,110 145,115 150,120", polylines[0].Attribute("points")!.Value);
        // 1.32.0：曲线带编号，圆弧带圆心和半径；不是圆的只有编号。
        var numberedSheet = sheet with { Views = [sheet.Views[1] with { Numbered = [
            new SheetCurve([new SheetSegment(0.14, 0.11, 0.145, 0.115), new SheetSegment(0.145, 0.115, 0.15, 0.12)], new SheetPoint(0.14, 0.12), 0.01),
            new SheetCurve([new SheetSegment(0.1, 0.1, 0.1, 0.1001)], null, 0)] }] };
        var numberedCurves = System.Xml.Linq.XDocument.Parse(DrawingSvgWriter.Write(numberedSheet)).Descendants(ns + "polyline").ToList();
        Equal("C0", numberedCurves[0].Attribute("id")!.Value);
        Equal("140,120", numberedCurves[0].Attribute("data-center")!.Value);
        Equal("10", numberedCurves[0].Attribute("data-r")!.Value);
        Equal("C1", numberedCurves[1].Attribute("id")!.Value);
        True(numberedCurves[1].Attribute("data-center") is null, "不是圆弧没有圆心");
        SheetCurve[] measuredCurves = [new([new SheetSegment(0.02, 0.03, 0.05, 0.06)], new SheetPoint(0.04, 0.04), 0.01)];
        Equal("C0 (40,40) ↔ C0.2 (50,60)", DrawingSvg.Measures(new ViewDimension(0, 2, false, 0.01, [DimensionAnchor.Center(new SheetPoint(0.04, 0.04)), DimensionAnchor.Point(new SheetPoint(0.05, 0.06))]), [], [], measuredCurves));

        SheetCurve[] concentric = [new([], new SheetPoint(0.03, 0.2), 0.05), new([], new SheetPoint(0.03, 0.2), 0.058)];
        Equal("C1 (30,200)", DrawingSvg.Measures(new ViewDimension(0, 5, false, 0.116, [DimensionAnchor.Center(new SheetPoint(0.03, 0.2))]), [], [], concentric, 0.5));

        var hole = document.Descendants(ns + "circle").Single();
        Equal("H0", hole.Attribute("id")!.Value);
        Equal("3", hole.Attribute("r")!.Value);
        Equal("/M6", hole.Attribute("data-kind")!.Value);

        var dimension = document.Descendants(ns + "g").Single(v => (string?)v.Attribute("id") == "工程图视图1" && (string?)v.Attribute("class") == "view").Descendants().Single(e => (string?)e.Attribute("id") == "D1");
        Equal("x=60 ↔ x=140", dimension.Attribute("data-measures")!.Value);
        Equal("60,200,140,206", dimension.Attribute("data-box")!.Value);
        // 方框画字占的地方（真机第一版画整个外框，一片虚线框叠在一起），整个外框留在 data-box。
        Equal("5", dimension.Element(ns + "rect")!.Attribute("width")!.Value);
        var vertical = document.Descendants(ns + "g").Single(v => (string?)v.Attribute("id") == "工程图视图1" && (string?)v.Attribute("class") == "view").Descendants().Single(e => (string?)e.Attribute("id") == "D2").Element(ns + "text")!;
        Equal("translate(50 130) rotate(90) scale(1 -1)", vertical.Attribute("transform")!.Value);
        var text = dimension.Element(ns + "text")!;
        Equal("80", text.Value);
        Equal("translate(95 201) scale(1 -1)", text.Attribute("transform")!.Value);
        // 中心符号线只给线的外框（1.24.0），不画线、不画方框。
        var mark = document.Descendants(ns + "g").Single(v => (string?)v.Attribute("id") == "工程图视图1" && (string?)v.Attribute("class") == "view").Descendants().Single(e => (string?)e.Attribute("id") == "中心符号线1");
        True(mark.Attribute("class")!.Value.Contains("center") && mark.Element(ns + "rect") is null && mark.Element(ns + "path") is null, "中心符号线");
        Equal("75,150,85,150", mark.Attribute("data-box")!.Value);
        // 多行注释拆 tspan，全文另在 data-text。
        var note = document.Descendants(ns + "g").Single(v => (string?)v.Attribute("id") == "图纸1" && (string?)v.Attribute("class") == "view").Descendants().Single(e => (string?)e.Attribute("id") == "注释1");
        Equal(2, note.Descendants(ns + "tspan").Count());
        Equal("技术要求 / 1. 未注圆角R1", note.Attribute("data-text")!.Value);
        var block = document.Descendants(ns + "g").Single(e => (string?)e.Attribute("id") == "title-block");
        Equal("底板 | WTJYQ-01-08 | 6061", block.Attribute("data-text")!.Value);
        True(svg.Contains("钣金折弯线 2 条没有画", StringComparison.Ordinal), "视图备注");
        True(!svg.Contains("\"-0", StringComparison.Ordinal) && !svg.Contains(" -0 ", StringComparison.Ordinal), "不出 -0");
        Equal("0", DrawingSvgWriter.Mm(-0.000001));
        Equal("12.35", DrawingSvgWriter.Mm(0.012346));

        var measured = new ViewDimension(0, 2, false, 0.08,
            [DimensionAnchor.Line(new SheetSegment(0.06, 0.1, 0.06, 0.2), true), DimensionAnchor.Center(new SheetPoint(0.14, 0.15)),
             DimensionAnchor.Line(new SheetSegment(0, 0, 0.01, 0.01), true)]);
        Equal("x=60 ↔ (140,150) ↔ 斜线", DrawingSvg.Measures(measured));
        // 落在视图的直边、孔上的写上它在 SVG 里的编号。
        Equal("E1 x=60 ↔ H0 (140,150) ↔ 斜线", DrawingSvg.Measures(measured,
            [new SheetSegment(0, 0, 0.1, 0), new SheetSegment(0.06, 0.2, 0.06, 0.1)], [new HoleEdge(0, 0.14, 0.15, 0.003)]));
        // 真机：孔标注显示数据里的符号是代码。
        Equal("4 x Ø 11 完全贯穿 ⌴ Ø 18 ↧ 11 <X-Y>", DimList.Readable("4 x <MOD-DIAM> 11 完全贯穿 <HOLE-SPOT> <MOD-DIAM> 18 <HOLE-DEPTH> 11 <X-Y>"));
    }

    /// <summary>1.21.0：每一步只回本步改了的——注解按名字比、跟着视图挪的不算改、新建 / 换比例的视图才带几何；预算按档降。</summary>
    static void TestStepDelta()
    {
        static SvgAnnotation Dim(string name, string text, double x) =>
            new(name, "线性尺寸", text, text, new SheetRect(x, 0.2, x + 0.01, 0.205), new SheetPoint(x, 0.2), 0.0035, "", [], new SheetRect(x, 0.2, x + 0.005, 0.2035));
        static SvgView View(string name, SheetRect outline, string scale, params SvgAnnotation[] annotations) =>
            new(name, "模型", "", scale, outline, [], [], [], annotations);
        static SvgSheet Sheet(string title, params SvgView[] views) => new(title, "图纸1", 0.42, 0.297, null, [], views);

        var main = new SheetRect(0.05, 0.1, 0.15, 0.2);
        var side = new SheetRect(0.2, 0.1, 0.22, 0.2);
        var before = Sheet("底板", View("V1", main, "1:2", Dim("D1", "80", 0.06), Dim("D2", "40", 0.08), Dim("D3", "20", 0.1)), View("V2", side, "1:2", Dim("D1", "16", 0.2)));

        // 没变化。
        True(SheetDelta.Diff(before, before).Empty, "没变化");

        // V1：D2 删了、D3 字变了、新加 D4；V2 整个往右挪 10 mm（它的 D1 跟着挪，不算改）。
        var shifted = new SheetRect(0.21, 0.1, 0.23, 0.2);
        var after = Sheet("底板",
            View("V1", main, "1:2", Dim("D1", "80", 0.06), Dim("D3", "25", 0.1), Dim("D4", "10", 0.12)),
            View("V2", shifted, "1:2", Dim("D1", "16", 0.21)));
        var diff = SheetDelta.Diff(before, after);
        Equal("V1,V2", string.Join(",", diff.Views.Select(v => v.Name)));
        var v1 = diff.Views[0];
        Equal("", v1.Change);
        Equal("D3:changed,D4:added,D2:removed", string.Join(",", v1.Annotations.Select(a => a.Name + ":" + a.Change)));
        var v2 = diff.Views[1];
        Equal("moved", v2.Change);
        Equal(0, v2.Annotations.Count);
        True(Math.Abs(v2.From!.Value.X - 0.21) < 1e-9 && Math.Abs(v2.From.Value.Y - 0.15) < 1e-9, "挪前的中心");
        True(diff.Geometry.Count == 0 && diff.Scan.SetEquals(["V1"]), "只为新尺寸的两头扫 V1，不带几何");
        Equal("V1 +1 ~1 -1；V2 挪了 (+10, 0)", diff.Summary);

        // 字没变、只挪了的记 moved：不重扫、差异里只给新方框不重复文字（真机：技术要求被挪，9 条全文又带了一遍）。
        var nudged = SheetDelta.Diff(before, Sheet("底板", View("V1", main, "1:2", Dim("D1", "80", 0.065), before.Views[0].Annotations[1], before.Views[0].Annotations[2]), before.Views[1]));
        Equal("D1:moved", string.Join(",", nudged.Views.Single().Annotations.Select(a => a.Name + ":" + a.Change)));
        True(nudged.Scan.Count == 0, "只挪了不重扫");
        Equal("V1 →1", nudged.Summary);
        var nudgedSvg = DrawingSvgWriter.Write(SheetDelta.Fill(before, nudged, new Dictionary<string, SvgView>()));
        True(nudgedSvg.Contains("data-change=\"moved\"><rect", StringComparison.Ordinal) && !nudgedSvg.Contains("<text", StringComparison.Ordinal), "moved 只给方框");

        // 换比例：带几何、全部注解重给。
        var rescaled = SheetDelta.Diff(before, Sheet("底板", View("V1", main with { Right = 0.2 }, "1:1", Dim("D1", "80", 0.06)), before.Views[1]));
        Equal("rescaled", rescaled.Views.Single().Change);
        True(rescaled.Geometry.SetEquals(["V1"]), "换比例带几何");

        // 换了一张图（一键出图从零件建新图）：整页都是新的。
        var fresh = SheetDelta.Diff(null, after);
        True(fresh.Views.All(v => v.Change == "added") && fresh.Geometry.SetEquals(["V1", "V2"]), "新图整页 added");

        // 填几何：只给带几何的视图；差异里的注解换成扫过的那份（带量的两头），变更标记留着。
        var scannedV1 = after.Views[0] with
        {
            Lines = [new SheetSegment(0.05, 0.1, 0.15, 0.1)],
            Annotations = after.Views[0].Annotations.Select(a => a with { Measures = "E0 y=100 ↔ x=1" }).ToList(),
        };
        var filled = SheetDelta.Fill(after, diff, new Dictionary<string, SvgView> { ["V1"] = scannedV1 });
        True(filled.Delta && filled.Blocks.Count == 0, "差异不画格子");
        Equal(0, filled.Views[0].Lines.Count);
        Equal("E0 y=100 ↔ x=1", filled.Views[0].Annotations.First(a => a.Name == "D4").Measures);
        Equal("added", filled.Views[0].Annotations.First(a => a.Name == "D4").Change);

        var svg = DrawingSvgWriter.Write(filled);
        True(svg.Contains("data-delta=\"true\"", StringComparison.Ordinal) && svg.Contains("id=\"D2\" data-kind=\"线性尺寸\" data-change=\"removed\"/>", StringComparison.Ordinal), "删掉的只留一个标记");
        True(svg.Contains("data-change=\"moved\" data-from=\"210,150\"", StringComparison.Ordinal), "挪了的视图给原中心");

        // 精简档：不出 text、不写 data-box，字进 data-text；只有标注档不出几何。
        var withGeometry = filled with { Views = [filled.Views[0] with { Lines = scannedV1.Lines }] };
        var compact = DrawingSvgWriter.Write(withGeometry, SvgDetail.Ladder[1]);
        True(!compact.Contains("<text", StringComparison.Ordinal) && !compact.Contains("data-box=\"60", StringComparison.Ordinal)
             && compact.Contains("data-text=\"10\"", StringComparison.Ordinal) && compact.Contains("id=\"E0\"", StringComparison.Ordinal) && !compact.Contains("<style", StringComparison.Ordinal), "精简档");
        True(!DrawingSvgWriter.Write(withGeometry, SvgDetail.Ladder[2]).Contains("id=\"E0\"", StringComparison.Ordinal), "只有标注档不出几何");
        True(DrawingSvgWriter.Write(withGeometry, SvgDetail.Ladder[0]).Length > compact.Length, "越往后越短");
        // 单取一个视图要的是几何：最后一档丢注解、留几何。
        var geometryOnly = DrawingSvgWriter.Write(withGeometry, SvgDetail.ViewLadder[2]);
        True(geometryOnly.Contains("id=\"E0\"", StringComparison.Ordinal) && !geometryOnly.Contains("data-kind=\"线性尺寸\"", StringComparison.Ordinal), "只有几何档");

        // 只对 MCP 来的调用带反馈。
        True(StepFeedback.Wanted("module:HistoryPortunus:MCP:client") && !StepFeedback.Wanted("ui") && !StepFeedback.Wanted(null), "来源");
    }
}
