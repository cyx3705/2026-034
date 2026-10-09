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
}
