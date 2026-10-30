using HistoryStrenua;

internal static partial class Tests
{
    static void TestCalloutPlacement()
    {
        // 100 × 50 的板在图框中间，左边一排竖直尺寸线；同种两个孔，一左一右。
        static double Mm(double value) => value / 1000;
        var frame = new SheetRect(0, 0, Mm(300), Mm(200));
        var plate = new SheetSegment[]
        {
            new(Mm(100), Mm(100), Mm(200), Mm(100)), new(Mm(200), Mm(100), Mm(200), Mm(150)),
            new(Mm(200), Mm(150), Mm(100), Mm(150)), new(Mm(100), Mm(150), Mm(100), Mm(100)),
        };
        var dimensions = Enumerable.Range(0, 4).Select(i => new SheetSegment(Mm(92 - i * 4), Mm(90), Mm(92 - i * 4), Mm(160))).ToList();
        var left = new HoleEdge(0, Mm(105), Mm(125), Mm(2), "/Cut");
        var right = new HoleEdge(1, Mm(195), Mm(125), Mm(2), "/Cut");
        var field = new CalloutField(plate, [], dimensions, [], [], [], frame);
        var text = new TextBox(Mm(20), Mm(20), Mm(30), Mm(4));

        var pick = CalloutPlacePlanner.Choose([text], Mm(20), [left, right], field);
        True(pick is not null, "应该找得到位置");
        var (dx, dy) = ClearancePlanner.CalloutShift([text], Mm(20), pick!.Value.Spot);
        var moved = text.Shift(dx, dy);
        True(ClearancePlanner.Hits([moved], new Obstacles(plate.Concat(dimensions).ToList(), [])) == 0, "字不压线");
        var leader = new SheetSegment(pick.Value.Hole.X, pick.Value.Hole.Y, pick.Value.Spot.Shoulder.X, pick.Value.Spot.Shoulder.Y);
        True(LeaderPlanner.Crossings([leader], dimensions) == 0, "引线不穿尺寸");

        // 只有左边一个孔、字进不了板内也进不了左边时，宁可出去也不许出框、不许进禁区。
        var blocked = field with { Blocks = [new SheetRect(0, 0, Mm(300), Mm(95)), new SheetRect(0, Mm(155), Mm(300), Mm(200))] };
        var inside = CalloutPlacePlanner.Choose([text], Mm(20), [left], blocked);
        True(inside is not null && inside.Value.Spot.Shoulder.Y > Mm(95) && inside.Value.Spot.Shoulder.Y < Mm(155), "禁区不进");

        // 已经在好位置就不值得挪：现状代价不比候选差太多。
        var current = CalloutPlacePlanner.Cost([moved], [leader], field);
        True(current <= pick.Value.Cost + 5, "同一位置（引线从孔心量，多一个半径）不值得再挪");
    }

    static void TestDimensionCheckFalsePositives()
    {
        var frame = new ViewFrame(new ModelDirection(1, 0, 0), new ModelDirection(0, 1, 0), new ModelDirection(0, 0, 1), new SheetPoint(0, 0), 1);
        SheetSegment[] lines =
        [
            new(0, 0, 0, 0.05),        // 0 左
            new(0, 0.05, 0.1, 0.05),   // 1 上
            new(0.1, 0.05, 0.1, 0.02), // 2 右（只到 0.02，下面是斜边）
            new(0.1, 0.02, 0.07, 0),   // 3 斜边
            new(0.07, 0, 0, 0),        // 4 下
        ];
        var total = new ViewDimension(0, 2, false, 0.1, [DimensionAnchor.Line(lines[0], true), DimensionAnchor.Line(lines[2], true)]);
        var high = new ViewDimension(1, 2, false, 0.05, [DimensionAnchor.Line(lines[4], true), DimensionAnchor.Line(lines[1], true)]);
        CheckView View(params ViewDimension[] more) => new("前视", "PART|默认", frame, lines, [], [], [total, high, .. more], []);

        var bare = DimensionCheckPlanner.Check([View()]);
        True(bare.Any(item => item.Text.Contains("斜边", StringComparison.Ordinal)), "没标斜边两头时要报");
        var spans = DimensionCheckPlanner.Check([View(
            new ViewDimension(2, 2, false, 0.03, [DimensionAnchor.Line(lines[2], true)]),
            new ViewDimension(3, 2, false, 0.07, [DimensionAnchor.Line(lines[4], true)]))]);
        True(spans.Count == 0, "量边自身跨度的尺寸定了两头：" + string.Join(" | ", spans.Select(item => item.Text)));

        // 倒角顺着看：上面多一条 0.5 mm 的短边（C1 在 1:2 视图）。
        var half = frame with { Scale = 0.5 };
        SheetSegment[] chamfered =
        [
            new(0, 0, 0, 0.0245), new(0, 0.0245, 0.05, 0.0245), new(0.05, 0.0245, 0.05, 0), new(0.05, 0, 0, 0),
            new(0, 0.0245, 0, 0.025), new(0, 0.025, 0.05, 0.025),
        ];
        var chamferDims = new List<ViewDimension>
        {
            new(0, 2, false, 0.1, [DimensionAnchor.Line(chamfered[0], true), DimensionAnchor.Line(chamfered[2], true)]),
            new(1, 2, false, 0.05, [DimensionAnchor.Line(chamfered[3], true), DimensionAnchor.Line(chamfered[5], true)]),
        };
        var edge = DimensionCheckPlanner.Check([new CheckView("下视", "PART|默认", half, chamfered, [], [], chamferDims, [])]);
        True(edge.Count == 0, "未注倒角的内边不报：" + string.Join(" | ", edge.Select(item => item.Text)));
    }

    static void TestSheetA2Gate()
    {
        var templates = UserTemplates();
        // 真机后板的样子：主视图是窄边（1100 × 20），22 个孔排一条线。严格判法每个特征只分到 40 mm²，以前因此上 A2。
        var holes = Enumerable.Range(0, 22).Select(i => new PartCylinder(new ModelDirection(0, 0, 1),
            new ModelDirection(0.05 + i * 0.045, 0.01, 0), 0.0025, true, true, "HoleWzd", [])).ToList();
        var part = new PartGeometry(new ModelBox(0, 0, -0.15, 1.1, 0.02, 0.15), holes, []);
        var main = StandardView.Of("front");
        var choice = DrawingPlanner.ChooseSheet(part, main, [], templates, chain: false);
        True(choice is not null && choice.Template.Width < 0.59, $"放得下 A3 就不上 A2，实际 {choice?.Template.SizeName} {choice?.Reason}");
    }
}
