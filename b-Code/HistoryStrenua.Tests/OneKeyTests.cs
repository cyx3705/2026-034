using HistoryStrenua;

/// <summary>离线测试：1.17.0 一键分类（OneKey/）、方形槽（Hole/）、外轮廓筛站与钣金折弯切线。</summary>
internal static partial class Tests
{
    /// <summary>0.2 × 0.1 的板：上边一个开口槽口（归外轮廓），里面一个四角 R2 的窗口。</summary>
    static List<SheetSegment> SlotPlate() =>
    [
        S(0, 0.1, 0.12, 0.1), S(0.14, 0.1, 0.2, 0.1),                        // 0,1 上边（断在槽口）
        S(0.12, 0.1, 0.12, 0.08), S(0.12, 0.08, 0.14, 0.08), S(0.14, 0.08, 0.14, 0.1), // 2,3,4 开口槽口
        S(0.2, 0.1, 0.2, 0), S(0.2, 0, 0, 0), S(0, 0, 0, 0.1),               // 5,6,7 右、下、左
        S(0.05, 0.032, 0.05, 0.058), S(0.09, 0.032, 0.09, 0.058),            // 8,9 窗口左右（四角 R2）
        S(0.052, 0.06, 0.088, 0.06), S(0.052, 0.03, 0.088, 0.03),            // 10,11 窗口上下
    ];

    static void TestSquareSlotFind()
    {
        var lines = SlotPlate();
        var outer = OutlinePlanner.OuterLines(lines, []);
        True(outer.Contains(3) && !outer.Contains(8), "开口槽口在外轮廓上，窗口不在");
        var slots = SquareSlotPlanner.Find(lines, outer);
        Equal(1, slots.Count);
        Equal((8, 9, 10, 11), (slots[0].Left, slots[0].Right, slots[0].Top, slots[0].Bottom));
        Equal(new SheetRect(0.05, 0.03, 0.09, 0.06), slots[0].Rect);

        // 再加一个直角窗口；另有一对竖线没有上下边，不算。
        lines.AddRange([S(0.15, 0.02, 0.15, 0.05), S(0.18, 0.02, 0.18, 0.05), S(0.15, 0.05, 0.18, 0.05), S(0.15, 0.02, 0.18, 0.02)]); // 12–15
        lines.AddRange([S(0.01, 0.01, 0.01, 0.02), S(0.02, 0.01, 0.02, 0.02)]); // 16,17
        slots = SquareSlotPlanner.Find(lines, OutlinePlanner.OuterLines(lines, []));
        Equal("8-9-10-11,12-13-14-15", string.Join(",", slots.Select(s => $"{s.Left}-{s.Right}-{s.Top}-{s.Bottom}")));

        // 四角缺的不一样多（一条竖边短了一截）不是矩形槽。
        List<SheetSegment> skew = [.. SlotPlate()];
        skew[9] = S(0.09, 0.032, 0.09, 0.05);
        Equal(0, SquareSlotPlanner.Find(skew, OutlinePlanner.OuterLines(skew, [])).Count);
    }

    static void TestSquareSlotDimensions()
    {
        var lines = SlotPlate();
        lines.AddRange([S(0.15, 0.02, 0.15, 0.05), S(0.18, 0.02, 0.18, 0.05), S(0.15, 0.05, 0.18, 0.05), S(0.15, 0.02, 0.18, 0.02)]);
        var slots = SquareSlotPlanner.Find(lines, OutlinePlanner.OuterLines(lines, []));
        var dims = SquareSlotPlanner.Dimensions(slots, new DimensionSides(0, 0.1, 0.2, 0, 0.01, 0.01, 0.01, 0.01));
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        string D(SlotDimension d) => $"{(d.Axis == PositionAxis.Horizontal ? "H" : "V")}{d.From}>{d.To}@{d.TextAt.X.ToString("0.###", invariant)},{d.TextAt.Y.ToString("0.###", invariant)}";
        // 每个槽每个方向一层：基准 → 近边、近边 → 远边并排；近的槽在里。1.23.0：每个槽挑引线短的一侧——
        // 两个槽都在板的下半，水平的都放下方（第二个在外一层）；竖直的左边那个放左侧、靠右那个放右侧。
        Equal("H-1>8@0.025,-0.01,H8>9@0.07,-0.01,H-1>12@0.075,-0.014,H12>13@0.165,-0.014,"
            + "V-1>10@-0.01,0.08,V10>11@-0.01,0.045,V-1>14@0.21,0.075,V14>15@0.21,0.035",
            string.Join(",", dims.Select(D)));

        DimensionAnchor E(int index) => DimensionAnchor.Line(lines[index], true);
        ViewDimension[] existing =
        [
            new(0, 2, false, 0.05, [E(7), E(8)]),          // 基准 → 槽左边：删
            new(1, 2, false, 0.04, [E(8), E(9)]),          // 槽宽：删
            new(2, 2, false, 0.2, [E(7), E(5)]),           // 外轮廓总长：不动
            new(3, 2, false, 0.03, [E(8), C(0.02, 0.02)]), // 槽边 → 孔：不动
            new(4, 1, false, 0.05, [E(8), E(7)]),          // 坐标尺寸：不归这里删
        ];
        var slotLines = slots.SelectMany(s => new[] { s.Left, s.Right, s.Top, s.Bottom }).Select(i => lines[i]).ToList();
        Equal("0,1", string.Join(",", SquareSlotPlanner.Obsolete(existing, slotLines, [lines[7], lines[0]])));
    }

    static void TestOutlineSparse()
    {
        OutlineStation H(double value) => new(PositionAxis.Horizontal, 0, value, value);
        OutlineStation V(double value) => new(PositionAxis.Vertical, 0, value, value);
        // 型材骨架：腿内侧 20、中间一根 400、右腿内侧 860、总长 880；高 690、横梁底 500、槽口小台阶 6.2。
        var stations = new List<OutlineStation> { H(0.02), H(0.4), H(0.86), H(0.88), V(0.0062), V(0.5), V(0.69) };
        var kept = OutlinePlanner.Sparse(stations, 0.02);
        Equal("0.4,0.88,0.5,0.69", string.Join(",", kept.Select(s => s.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        Equal(stations.Count, OutlinePlanner.Sparse(stations, 0).Count);
    }

    static void TestPartKind()
    {
        Equal("2020", PartKindPlanner.ProfileName(@"C:\weldment profiles\20x20.sldlfp"));
        Equal("2020欧标", PartKindPlanner.ProfileName("2020欧标.SLDLFP"));
        Equal("4040", PartKindPlanner.ProfileName("4040铝型材.sldlfp"));
        Equal(string.Empty, PartKindPlanner.ProfileName(string.Empty));
        // 三根 2020、一根 2020（躺着）、一根 4040；1.5 厚的板不像型材。
        Equal("2020、4040", PartKindPlanner.ProfileFromBodies(
            [(0.88, 0.02, 0.02), (0.02, 0.445, 0.02), (0.02, 0.02, 0.69), (0.02, 0.02, 0.69), (0.04, 0.04, 0.5), (0.45, 0.7, 0.0015)]));
        Equal(string.Empty, PartKindPlanner.ProfileFromBodies([(0.45, 0.7, 0.0015)]));
        Near(0.02, PartKindPlanner.ProfileWidth("2020欧标"));
        Near(0.04, PartKindPlanner.ProfileWidth("4080"));
        Near(0, PartKindPlanner.ProfileWidth("铝型材"));
        Equal("采用2020欧标铝型材", PartKindPlanner.ProfileNote("2020欧标"));
        Equal("采用铝型材", PartKindPlanner.ProfileNote(string.Empty));
    }

    static void TestBendTangent()
    {
        True(SheetMetal.AlongAxis(new ModelDirection(0, 0, 1), new ModelDirection(0, 0, -2)), "母线与轴线反向也算平行");
        True(!SheetMetal.AlongAxis(new ModelDirection(1, 0, 0), new ModelDirection(0, 0, 1)), "孔口方向不对");
        True(!SheetMetal.AlongAxis(new ModelDirection(1, 0, 0.01), new ModelDirection(1, 0, 0)), "斜 0.6° 不算");
        True(!SheetMetal.AlongAxis(new ModelDirection(0, 0, 0), new ModelDirection(0, 0, 1)), "退化");

        // 切线旁的平面侧着 = 折边侧着的外轮廓，要留着（右防护板俯视图最左那条）；正对着 = 板面上的折弯切线，去掉。
        True(SheetMetal.EdgeOn(new ModelDirection(1, 0, 0), new ModelDirection(0, 0, 1)), "折边侧着");
        True(!SheetMetal.EdgeOn(new ModelDirection(0, 0, 1), new ModelDirection(0, 0, -1)), "顶板正对着");
        True(!SheetMetal.EdgeOn(new ModelDirection(0, 0, 0), new ModelDirection(0, 0, 1)), "退化");

        // 右防护板真机读数（1:5，毫米，外 R2.5），已去掉正对着的切线。俯视图：端部折边止于 2.207 / 447.793、侧板让位到 2.207，都离外轮廓不到 2.5。
        const double scale = 0.2;
        SheetSegment M(double x1, double y1, double x2, double y2) => S(x1 / 1000 * scale, y1 / 1000 * scale, x2 / 1000 * scale, y2 / 1000 * scale);
        List<SheetSegment> top =
        [
            M(0, 2.207, 0, 447.793), M(2.207, 450, 2.207, 448.5), M(2.207, 0, 2.207, 1.5), M(63, 128, 63, 208), M(228, 208, 228, 128), M(700, 447.5, 700, 2.5),
            M(0, 447.793, 1.5, 447.793), M(0, 2.207, 1.5, 2.207), M(700, 450, 2.207, 450), M(700, 0, 2.207, 0), M(63, 208, 228, 208), M(228, 128, 63, 128),
        ];
        var zone = 0.0025 * scale;
        Equal("1,2,6,7", string.Join(",", SheetMetal.InBendZone(top, zone, scale)));
        // 侧板正视图：端部折边内表面 1.5、侧板端 2.207、折边顶上到切线的 2.5 都去掉；底边 125、顶边 0（顶板侧着）留着。
        List<SheetSegment> side =
        [
            M(0, 2.5, 0, 125), M(1.5, 2.5, 1.5, 125), M(2.207, 125, 2.207, 2.5), M(700, 2.5, 700, 125),
            M(1.5, 125, 0, 125), M(0, 2.5, 1.5, 2.5), M(700, 125, 2.207, 125), M(2.5, 0, 700, 0),
        ];
        Equal("1,2,5", string.Join(",", SheetMetal.InBendZone(side, zone, scale)));
        Equal(0, SheetMetal.InBendZone(side, 0, scale).Count);
    }

    static void TestWorkNotes()
    {
        var notes = new WorkNotes(null);
        Equal(string.Empty, notes.Section("onekey"));
        Equal(null, notes.Write("onekey", "第一条", null));
        Equal(null, notes.Write(WorkNotes.Common, "通用一条", null));
        Equal(null, notes.Write("hole", "孔的", null));
        var section = notes.Section("onekey");
        True(section.Contains("【笔记区·通用】") && section.Contains("【笔记区·一键】") && section.Contains("1. 第一条") && !section.Contains("孔的"), "只带通用与本类：" + section);
        Equal(null, notes.Write("onekey", "改过", 1));
        Equal("改过", notes.Read("onekey")[0]);
        True(notes.Write("onekey", "x", 5) is not null, "序号不存在要报");
        Equal(null, notes.Write("onekey", QuickArgs.None, 1));
        Equal(0, notes.Read("onekey").Count);
        Equal("onekey", WorkNotes.Area("一键"));
        Equal(null, WorkNotes.Area("没有"));

        // 笔记放回执最上面，成败和数据不变。
        var ok = notes.Prepend("hole", HistoryVulcan.Core.Commands.CommandResult.Ok("结果", "数据"));
        True(ok.Success && ok.Message.StartsWith("【笔记区·通用】", StringComparison.Ordinal) && ok.Message.EndsWith("结果", StringComparison.Ordinal) && Equals(ok.Data, "数据"), ok.Message);
        True(!notes.Prepend("hole", HistoryVulcan.Core.Commands.CommandResult.Fail("坏了")).Success, "失败还是失败");

        var dir = Path.Combine(Path.GetTempPath(), "strenua-notes-" + Guid.NewGuid().ToString("N"));
        try
        {
            Equal(null, new WorkNotes(dir).Write("onekey", "跨重启", null));
            Equal("跨重启", new WorkNotes(dir).Read("onekey")[0]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    static void TestOneKeyFix()
    {
        // 1.28.0（用户定）：没有 0 级的一键出图；三类各有修正要点，里面提到的指令都得真有。
        True(QuickCommands.All.All(command => command.Key != "onekey-drawing"), "一键出图已删");
        var registry = Registry();
        foreach (var kind in Enum.GetValues<PartKind>())
        {
            var text = OneKeyFix.Text(kind);
            True(text.Contains("【修正要点·" + PartKindPlanner.Title(kind)), text);
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"strenua\.[a-z]+\.[a-z]+"))
                True(registry.TryGet(match.Value, out _), "修正要点里的指令不存在：" + match.Value);
        }

        True(OneKeyFix.Text(PartKind.SheetMetal).Contains("板厚"), "钣金查板厚");
        True(OneKeyFix.Text(PartKind.Frame).Contains("型材"), "框架查型材");
        True(OneKeyFix.Text(PartKind.Machined).Contains("导入件"), "加工件查导入件孔识别");
    }

    static void TestChain()
    {
        // 比例 1 正对 +Z。100 × 50 的板，左边 x=0、上边 y=0.05，孔 (10, 20)、(40, 20)。
        var frame = new ViewFrame(new ModelDirection(1, 0, 0), new ModelDirection(0, 1, 0), new ModelDirection(0, 0, 1), new SheetPoint(0, 0), 1);
        SheetSegment[] lines = [new(0, 0, 0, 0.05), new(0, 0.05, 0.1, 0.05), new(0.1, 0.05, 0.1, 0), new(0.1, 0, 0, 0)];
        var (left, top) = (DimensionAnchor.Line(lines[0], true), DimensionAnchor.Line(lines[1], true));
        var (h1, h2) = (C(0.01, 0.02), C(0.04, 0.02));
        ViewDimension first = new(0, 2, false, 0.01, [left, h1]);
        ViewDimension second = new(1, 2, false, 0.03, [h1, h2]);
        CheckView View(SheetSegment[] centers, params ViewDimension[] d) => new("前", "PART|默认", frame, lines, [], [], d, centers);
        Dictionary<int, SheetPoint> At(params double[] rows) => rows.Select((y, i) => (y, i)).ToDictionary(item => item.i, item => new SheetPoint(0.02, item.y));

        // 从基准起 + 同一行接上：都在链上。
        Equal(0, DimensionCheckPlanner.Chains(View([], first, second), At(0.07, 0.07)).Count);
        var groups = DimensionCheckPlanner.ChainGroups(View([], first, second), At(0.07, 0.07));
        Equal(1, groups.Count);
        Equal(2, groups[0].Count);

        // 同一条链被换了行：报第二段，指回第一段那一行；组也拆开了。
        var wrapped = DimensionCheckPlanner.Chains(View([], first, second), At(0.07, 0.08));
        Equal(1, wrapped.Count);
        Equal(ChainFault.Wrapped, wrapped[0].Fault);
        Equal(1, wrapped[0].Item.Index);
        Equal(0, wrapped[0].Peer!.Index);
        Equal(0, DimensionCheckPlanner.ChainGroups(View([], first, second), At(0.07, 0.08)).Count);

        // 量一条边自身：真机夹爪2 的 21.62，边 y 从 0.04 到 0.02，基准在上边 0.05，改从基准标是 30。
        SheetSegment inner = new(0.08, 0.04, 0.08, 0.02);
        var detached = DimensionCheckPlanner.Chains(View([], new ViewDimension(0, 2, false, 0.02, [DimensionAnchor.Line(inner, true)])), At(0.07));
        Equal(1, detached.Count);
        Equal(ChainFault.Detached, detached[0].Fault);
        True(Math.Abs(detached[0].FromDatum - 0.03) < 1e-9, "从基准的值");

        // 两段接成链但接不到基准。
        var h3 = C(0.07, 0.02);
        var loose = DimensionCheckPlanner.Chains(View([], second with { Index = 0 }, new ViewDimension(1, 2, false, 0.03, [h2, h3])), At(0.07, 0.07));
        Equal(2, loose.Count);
        True(loose.All(issue => issue.Fault == ChainFault.Unrooted), "整条链没接基准");

        // 贯穿的对称轴算基准，串圆心的短中心线不算。
        ViewDimension span = new(0, 2, false, 0.03, [C(0.035, 0.02), C(0.065, 0.02)]);
        Equal(0, DimensionCheckPlanner.Chains(View([new(0.05, -0.001, 0.05, 0.051)], span), At(0.07)).Count);
        Equal(1, DimensionCheckPlanner.Chains(View([new(0.05, 0.015, 0.05, 0.025)], span), At(0.07)).Count);

        // 倒角、带公差的不查；基准两侧碰头的不成组。
        Equal(0, DimensionCheckPlanner.Chains(View([], new ViewDimension(0, 2, false, 0.03, [h1, h2], "2 x C")), At(0.07)).Count);
        Equal(0, DimensionCheckPlanner.Chains(View([], new ViewDimension(0, 2, false, 0.03, [h1, h2], ToleranceType: 4)), At(0.07)).Count);
        SheetSegment outside = new(0, 0.07, 0.02, 0.07);
        ViewDimension above = new(0, 2, false, 0.02, [top, DimensionAnchor.Line(outside, true)]);
        ViewDimension below = new(1, 2, false, 0.03, [top, DimensionAnchor.Line(new SheetSegment(0, 0.02, 0.02, 0.02), true)]);
        Dictionary<int, SheetPoint> sameColumn = new() { [0] = new SheetPoint(-0.01, 0.06), [1] = new SheetPoint(-0.01, 0.035) };
        Equal(0, DimensionCheckPlanner.ChainGroups(View([], above, below), sameColumn).Count);
    }

    static void TestRedundant()
    {
        // 比例 1 正对 +Z。100 × 50 的板，孔 (30, 20)。
        var frame = new ViewFrame(new ModelDirection(1, 0, 0), new ModelDirection(0, 1, 0), new ModelDirection(0, 0, 1), new SheetPoint(0, 0), 1);
        SheetSegment[] lines = [new(0, 0, 0, 0.05), new(0, 0.05, 0.1, 0.05), new(0.1, 0.05, 0.1, 0), new(0.1, 0, 0, 0)];
        var (left, top, right, bottom) = (DimensionAnchor.Line(lines[0], true), DimensionAnchor.Line(lines[1], true), DimensionAnchor.Line(lines[2], true), DimensionAnchor.Line(lines[3], true));
        HoleEdge[] holes = [new(0, 0.03, 0.02, 0.003, "/Cut")];
        CheckView View(string name, params ViewDimension[] d) => new(name, "PART|默认", frame, lines, holes, [], d, []);
        string N(RedundantMember m) => "D" + m.Index;

        // 定位互不相关：没有重标（桥不算）。
        ViewDimension total = new(0, 2, false, 0.1, [left, right]);
        ViewDimension holeLeft = new(1, 2, false, 0.03, [left, C(0.03, 0.02)]);
        Equal(0, DimensionCheckPlanner.Redundant([View("前", total, holeLeft, new(2, 2, false, 0.05, [bottom, top]))]).Count);

        // 总长 = 孔左 + 孔右：三个成环多一个，总尺寸留下。
        var loop = DimensionCheckPlanner.Redundant([View("前", total, holeLeft, new(2, 2, false, 0.07, [C(0.03, 0.02), right]))]);
        Equal(1, loop.Count);
        Equal(3, loop[0].Members.Count);
        Equal(1, loop[0].Surplus);
        Equal(1, loop[0].Suggested.Count);
        True(loop[0].Suggested[0].Target.Index != 0, "总尺寸留下");
        var text = RedundantCheck.Formula(loop[0].Suggested[0], N);
        True(text.Contains('−') && !text.Contains("相切"), text);

        // 带公差的优先留下。
        var kept = DimensionCheckPlanner.Redundant([View("前", total, new(1, 2, false, 0.03, [left, C(0.03, 0.02)], ToleranceType: 4), new(2, 2, false, 0.07, [C(0.03, 0.02), right]))]);
        True(kept[0].Suggested[0].Target.Index == 2 || kept[0].Suggested[0].Target.Index == 0, "公差留下");
        True(kept[0].Suggested[0].Target.Index != 1, "公差留下");

        // 跨视图同一段标了两遍：留前面视图的，算式就是另一个。
        var cross = DimensionCheckPlanner.Redundant([View("前", total), View("后", new ViewDimension(0, 2, false, 0.1, [left, right]))]);
        Equal(1, cross.Count);
        Equal(1, cross[0].Suggested[0].Target.View);
        Equal(1, cross[0].Suggested[0].Terms.Count);

        // 两个环共用一条边：一组、多两个。
        var two = DimensionCheckPlanner.Redundant([View("前", total, holeLeft, new(2, 2, false, 0.07, [C(0.03, 0.02), right]), new(3, 2, false, 0.07, [C(0.03, 0.02), right]))]);
        Equal(1, two.Count);
        Equal(2, two[0].Surplus);
        Equal(2, two[0].Suggested.Count);

        // 孔径不进图。
        Equal(0, DimensionCheckPlanner.Redundant([View("前", total, new(1, 6, false, 0.006, [C(0.03, 0.02)]))]).Count);

        // 真机夹爪2 右视图（比例 1 换算）：18、圆心 13、R5，弧只画到一半、够不到切线；13 = 18 − R5，R 留下。
        SheetSegment[] jaw = [new(0, 0.1, 0, 0.0255), new(0, 0.0255, 0.013, 0.0255), new(0.018, 0.0195, 0.018, 0), new(0.018, 0, 0.04, 0)];
        FilletArc[] arc = [new(0, new SheetPoint(0.013, 0.0205), 0.005, 0.005, new SheetPoint(0.0145, 0.025), false, Math.PI / 4, new SheetPoint(0.013, 0.0255), new SheetPoint(0.016, 0.0245))];
        var jawView = new CheckView("右", "PART|默认", frame, jaw, [], arc,
            [new(0, 2, false, 0.018, [DimensionAnchor.Line(jaw[0], true), DimensionAnchor.Line(jaw[2], true)]),
             new(1, 2, false, 0.013, [DimensionAnchor.Line(jaw[0], true), C(0.013, 0.0205)]),
             new(2, 5, false, 0.005, [C(0.013, 0.0205)])], []);
        var tangent = DimensionCheckPlanner.Redundant([jawView]);
        Equal(1, tangent.Count);
        Equal(1, tangent[0].Suggested[0].Target.Index);
        Equal("D1 = D0(18) − D2(5)", RedundantCheck.Formula(tangent[0].Suggested[0], N));
        Equal(1, ArcCenterPlanner.Tangents(arc[0], jaw).Count(t => Math.Abs(t.Y) > 0.9));

        // 两处 R5 等高等大、各标各的：全是 R 的环不报。
        var twinA = new CheckView("主", "PART|默认", frame, jaw, [], [arc[0]], [new(0, 5, false, 0.005, [C(0.013, 0.0205)])], []);
        var twinB = new CheckView("右", "PART|默认", frame, [jaw[1]], [], [arc[0]], [new(0, 5, false, 0.005, [C(0.013, 0.0205)])], []);
        Equal(0, DimensionCheckPlanner.Redundant([twinA, twinB]).Count);

        // 没标 R 时相切算已定，圆心到切线的尺寸自己就多余。
        var noRadius = jawView with { Dimensions = [new(0, 2, false, 0.005, [C(0.013, 0.0205), DimensionAnchor.Line(jaw[2], true)])] };
        True(RedundantCheck.Formula(DimensionCheckPlanner.Redundant([noRadius])[0].Suggested[0], N).Contains("相切"), "自环写明原因");

        // 倒角 C：总长 100 = 平直段 95 + C5。
        SheetSegment[] cut = [new(0, 0, 0, 0.05), new(0, 0.05, 0.095, 0.05), new(0.095, 0.05, 0.1, 0.045), new(0.1, 0.045, 0.1, 0)];
        var cutView = new CheckView("前", "PART|默认", frame, cut, [], [],
            [new(0, 2, false, 0.1, [DimensionAnchor.Line(cut[0], true), DimensionAnchor.Line(cut[3], true)]),
             new(1, 2, false, 0.095, [DimensionAnchor.Line(cut[0], true), new DimensionAnchor(0.095, null)]),
             new(2, 2, false, 0.005, [DimensionAnchor.Line(cut[2], true)], "C")], []);
        var chamfered = DimensionCheckPlanner.Redundant([cutView]);
        Equal(1, chamfered.Count);
        Equal(1, chamfered[0].Suggested[0].Target.Index);

        // 阵列跨度里的孔跟着起点，再标到它就算重标。
        HoleEdge[] row = [new(0, 0.01, 0.02, 0.003, "/C"), new(1, 0.03, 0.02, 0.003, "/C"), new(2, 0.05, 0.02, 0.003, "/C")];
        var arrayView = new CheckView("前", "PART|默认", frame, lines, row, [],
            [new(0, 2, false, 0.04, [C(0.01, 0.02), C(0.05, 0.02)], "2 x "), new(1, 2, false, 0.02, [C(0.01, 0.02), C(0.03, 0.02)])], []);
        Equal(1, DimensionCheckPlanner.Redundant([arrayView]).Count);
    }
}
