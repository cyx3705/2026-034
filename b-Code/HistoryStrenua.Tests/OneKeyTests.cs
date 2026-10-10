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
        var dims = SquareSlotPlanner.Dimensions(slots, left: 0, top: 0.1, firstHorizontal: 0.01, firstVertical: 0.01);
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        string D(SlotDimension d) => $"{(d.Axis == PositionAxis.Horizontal ? "H" : "V")}{d.From}>{d.To}@{d.TextAt.X.ToString("0.###", invariant)},{d.TextAt.Y.ToString("0.###", invariant)}";
        // 每个槽每个方向一层：基准 → 近边、近边 → 远边并排；近的槽在里。
        Equal("H-1>8@0.025,0.11,H8>9@0.07,0.11,H-1>12@0.075,0.116,H12>13@0.165,0.116,"
            + "V-1>10@-0.01,0.08,V10>11@-0.01,0.045,V-1>14@-0.016,0.075,V14>15@-0.016,0.035",
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
        Equal<(PartKind, string)?>((PartKind.SheetMetal, "有折弯"), PartKindPlanner.Read("{\"kind\": \"钣金\", \"reason\": \"有折弯\"}"));
        Equal(PartKind.Frame, PartKindPlanner.Read("答复：{\"kind\": \"铝型材框架\"}")!.Value.Kind);
        Equal(PartKind.Machined, PartKindPlanner.Read("{\"kind\": \"机加件\"}")!.Value.Kind);
        True(PartKindPlanner.Read("{\"kind\": \"装配图\"}") is null && PartKindPlanner.Read("不知道") is null, "不是三类之一就读不出");

        Equal(PartKind.SheetMetal, PartKindPlanner.Guess("{\"模型\":{\"文件\":\"右防护板.SLDPRT\",\"特征类型计数\":{\"SheetMetal\":1,\"EdgeFlange\":2}}}"));
        Equal(PartKind.Frame, PartKindPlanner.Guess("{\"模型\":{\"文件\":\"骨架.SLDPRT\",\"特征类型计数\":{\"WeldMemberFeat\":12}}}"));
        Equal(PartKind.Frame, PartKindPlanner.Guess("{\"模型\":{\"文件\":\"WTJYQ-04-01 型材骨架.SLDPRT\",\"材料\":\"6061-T6 (GB)\"}}"));
        Equal(PartKind.Machined, PartKindPlanner.Guess("{\"模型\":{\"文件\":\"型材骨架.SLDPRT\",\"材料\":\"45钢\"}}"));
        Equal(PartKind.Machined, PartKindPlanner.Guess("{坏的"));
        Equal("钣金,框架,加工件", string.Join(",", PartKindPlanner.Titles));

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
}
