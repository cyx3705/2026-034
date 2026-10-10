using System.Text.Json;
using HistoryStrenua;
using HistoryVulcan.Core.Commands;

/// <summary>离线测试：孔类（Hole/）。</summary>
internal static partial class Tests
{
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

    static void TestFlowCommand()
    {
        var flow = QuickCommands.All.Single(command => command.Key == "hole-flow");
        Equal("strenua.hole.flow", flow.CommandName);
        Equal("孔标注全流程", flow.Title);
        // 用户定的顺序写在用法里，页面上看得到。
        var usage = flow.Usage;
        True(usage.IndexOf("销钉符号", StringComparison.Ordinal) < usage.IndexOf("中心符号线", StringComparison.Ordinal)
            && usage.IndexOf("中心符号线", StringComparison.Ordinal) < usage.IndexOf("孔位尺寸", StringComparison.Ordinal)
            && usage.IndexOf("孔位尺寸", StringComparison.Ordinal) < usage.IndexOf("→ 方形槽", StringComparison.Ordinal)
            && usage.IndexOf("→ 方形槽", StringComparison.Ordinal) < usage.IndexOf("→ 孔标注", StringComparison.Ordinal)
            && usage.IndexOf("→ 孔标注", StringComparison.Ordinal) < usage.IndexOf("→ 销孔标注", StringComparison.Ordinal), "步骤顺序");
        True(usage.Contains("当前图纸页", StringComparison.Ordinal), "范围是当前图纸页");
        Equal("onekey-drawing,onekey-sheetmetal,onekey-frame,onekey-machined,hole-flow,dowel-symbol,center-mark,hole-position,hole-callout,dowel-fit,square-slot,"
            + "drawing-basic,drawing-create,drawing-project,drawing-iso,drawing-arrange,symmetry-axes,outline-all,outline,fillet-flow,arc-center-all,arc-all,chamfer-all,arc-center,arc,chamfer,"
            + "check-dimension,check-decimal,check-dangling,check-overlap,snapshot,tech-ai",
            string.Join(",", QuickCommands.All.Select(command => command.Key)));
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

    static void TestPositionAvoidsKeepOuts()
    {
        // 1.16.0：孔位尺寸放上方会压修改栏（避障开）→ 水平尺寸整组换到视图下方；竖直尺寸左侧不压，照旧。
        HoleEdge[] holes = [new(0, 0.03, 0.02, 0.003, "/Cut"), new(1, 0.06, 0.02, 0.003, "/Cut")];
        var blocked = new List<SheetRect> { new(0, 0.052, 0.2, 0.08) };
        var space = new PositionSpace(0.1, 0, blocked, new SheetRect(-0.05, -0.05, 0.25, 0.1));
        var plan = HolePositionPlanner.Plan(holes, 0, 0.05, 1.0, space: space);
        var horizontal = plan.Dimensions.Where(d => d.Axis == PositionAxis.Horizontal).ToList();
        True(horizontal.Count > 0 && horizontal.All(d => d.TextAt.Y < 0), "水平尺寸换到下方：" + string.Join(",", horizontal.Select(d => d.TextAt.Y)));
        True(plan.Dimensions.Where(d => d.Axis == PositionAxis.Vertical).All(d => d.TextAt.X < 0), "竖直尺寸照旧在左侧");
        // 不给禁区：照旧在上方。
        True(HolePositionPlanner.Plan(holes, 0, 0.05, 1.0).Dimensions.Where(d => d.Axis == PositionAxis.Horizontal).All(d => d.TextAt.Y > 0.05), "默认在上方");
        // 两边都压时不换（换了不更好）。
        var both = new PositionSpace(0.1, 0, [new(0, 0.052, 0.2, 0.08), new(0, -0.03, 0.2, -0.002)]);
        True(HolePositionPlanner.Plan(holes, 0, 0.05, 1.0, space: both).Dimensions.Where(d => d.Axis == PositionAxis.Horizontal).All(d => d.TextAt.Y > 0.05), "一样压就不换");
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
}
