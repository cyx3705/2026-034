using System.Text.Json;
using HistoryStrenua;
using HistoryVulcan.Core.Commands;

/// <summary>孔位尺寸的离线测试。</summary>
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

        // 偏外侧有一对短竖边时，不能因离轴更远就优先选它们，应选贯穿视图的长边。
        List<SheetSegment> shortDecoys =
        [
            new(0.10, 0.00, 0.10, 0.02), new(0.20, 0.00, 0.20, 0.02),
            new(0.11, 0.00, 0.11, 0.10), new(0.19, 0.00, 0.19, 0.10),
            new(0.11, 0.00, 0.19, 0.00), new(0.11, 0.10, 0.19, 0.10),
        ];
        var decoyHoles = new[] { new HoleEdge(0, 0.12, 0.05, 0.001, "/M3"), new HoleEdge(1, 0.18, 0.05, 0.001, "/M3") };
        var longPairAxis = SymmetryPlanner.Axes(shortDecoys, [], decoyHoles);
        Equal("2,3", $"{longPairAxis.Single().FirstLine},{longPairAxis.Single().SecondLine}");

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

    static void TestPositionSameSpanPatterns()
    {
        // 1.34.0，真机侧板：6 个 Ø5 间距 200、11 个 Ø6.6 间距 100，跨度都是 1000。数量不同各标各的，不管哪种先扫到；基准段跟细阵列同一行。
        const double scale = 0.2;
        const double left = 0.05;
        const double top = 0.2;
        static HoleEdge H(int i, double x, double y, double d, string kind) => new(i, left + x / 1000 * scale, top - y / 1000 * scale, d / 2000 * scale, kind);
        var coarse = Enumerable.Range(0, 6).Select(i => H(i, 45 + 200 * i, 40, 5, "/M5")).ToList();
        var fine = Enumerable.Range(0, 11).Select(i => H(6 + i, 45 + 100 * i, 138, 6.6, "/CBore")).ToList();
        // 真机两种孔的跨度差着浮点误差（粗的略短或略长），不能靠排序。
        var shorter = coarse.Select(h => h.Index == 5 ? h with { X = h.X - 1e-9 } : h).ToList();
        var longer = coarse.Select(h => h.Index == 5 ? h with { X = h.X + 1e-9 } : h).ToList();
        foreach (var edges in new[] { coarse.Concat(fine).ToList(), fine.Concat(coarse).ToList(), shorter.Concat(fine).ToList(), longer.Concat(fine).ToList() })
        {
            var patterns = HolePositionPlanner.Plan(edges, left, top, scale).Dimensions.Where(d => d.Axis == PositionAxis.Horizontal && d.Prefix.Length > 0).ToList();
            Equal(2, patterns.Count);
            var fineArray = patterns.Single(d => d.Prefix == "10 x 100 =");
            True(patterns.Any(d => d.Prefix == "5 x 200 ="), string.Join(" / ", patterns.Select(d => d.Prefix)));
            Near(1.0, fineArray.Expected);
            var datumSpan = HolePositionPlanner.Plan(edges, left, top, scale).Dimensions.Single(d => d.Axis == PositionAxis.Horizontal && d.FromEdgeIndex is null);
            Near(fineArray.TextAt.Y, datumSpan.TextAt.Y);
        }

        // 粗阵列的孔不在细阵列的站上：两个都要标，否则粗阵列没有位置。
        var offStations = Enumerable.Range(0, 5).Select(i => H(i, 45 + 250 * i, 40, 5, "/M5")).Concat(fine).ToList();
        var both = HolePositionPlanner.Plan(offStations, left, top, scale).Dimensions.Where(d => d.Axis == PositionAxis.Horizontal && d.Prefix.Length > 0).Select(d => d.Prefix).ToList();
        True(both.Contains("10 x 100 =") && both.Contains("4 x 250 ="), string.Join(" / ", both));
    }

    static void TestPositionValueCheck()
    {
        // 1.32.4，真机底层1：应标 30，连到旁边一列读回 33。
        True(!HolePositionPlanner.SameValue(0.030, 0.033), "连错列要拦下");
        True(HolePositionPlanner.SameValue(0.030, 0.03001), "同一根线的读数误差不拦");
        True(HolePositionPlanner.SameValue(0, 0.033), "没给规划值不核对");
        var (holes, left, top, scale) = Strip(i => 10 + 60 * i);
        var plan = HolePositionPlanner.Plan(holes, left, top, scale);
        Near(0.6, plan.Dimensions[0].Expected);
        Near(0.01, plan.Dimensions[1].Expected);
        Near(0.03, plan.Dimensions[2].Expected);
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
        Near(top + HolePositionPlanner.FirstTier, fromDatum.TextAt.Y);

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
        // 两边命中数相同时，按尺寸界线总长选择更短的一侧。
        var both = new PositionSpace(0.1, 0, [new(0, 0.052, 0.2, 0.08), new(0, -0.03, 0.2, -0.002)]);
        True(HolePositionPlanner.Plan(holes, 0, 0.05, 1.0, space: both).Dimensions.Where(d => d.Axis == PositionAxis.Horizontal).All(d => d.TextAt.Y < 0), "命中数相同就选引线较短的下侧");
        var free = new PositionSpace(0.1, 0, []);
        True(HolePositionPlanner.Plan(holes, 0, 0.05, 1.0, space: free).Dimensions.Where(d => d.Axis == PositionAxis.Horizontal).All(d => d.TextAt.Y < 0), "两侧都空时按引线较短选择");
    }

    static void TestPositionBothSides()
    {
        // 1.23.0（用户定）：两侧都能放，每种孔挑引线短的一侧。靠上的一种（M6，Y 0.045）放上方、靠左；
        // 靠下的一种（M8，Y 0.005，偏右）水平的放下方、竖直的放右侧。板 X 0–0.1、Y 0–0.05。
        HoleEdge[] holes =
        [
            new(0, 0.03, 0.045, 0.003, "/M6"), new(1, 0.06, 0.045, 0.003, "/M6"),
            new(2, 0.04, 0.005, 0.004, "/M8"), new(3, 0.08, 0.005, 0.004, "/M8"),
        ];
        var plan = HolePositionPlanner.Plan(holes, 0, 0.05, 1.0, space: new PositionSpace(0.1, 0, []));
        bool Upper(PositionDimension d) => d.ToEdgeIndex is 0 or 1;
        var horizontal = plan.Dimensions.Where(d => d.Axis == PositionAxis.Horizontal).ToList();
        var vertical = plan.Dimensions.Where(d => d.Axis == PositionAxis.Vertical).ToList();
        True(horizontal.Where(Upper).All(d => d.TextAt.Y > 0.05) && horizontal.Count(Upper) == 2, "M6 水平尺寸在上方");
        True(horizontal.Where(d => !Upper(d)).All(d => d.TextAt.Y < 0) && horizontal.Count(d => !Upper(d)) == 2, "M8 水平尺寸在下方");
        Near(-HolePositionPlanner.FirstTier, horizontal.First(d => !Upper(d)).TextAt.Y);
        True(vertical.Single(Upper).TextAt.X < 0, "M6 竖直尺寸在左侧");
        Near(0.1 + HolePositionPlanner.FirstTier, vertical.Single(d => !Upper(d)).TextAt.X);
        // 另一侧挑最下 / 最右的那个孔连。
        Equal(3, vertical.Single(d => !Upper(d)).ToEdgeIndex);

        // 尺寸链模式：整组挑一侧。孔都靠下 → 水平组到下方。
        var chain = HolePositionPlanner.Plan(holes.Skip(2).ToList(), 0, 0.05, 1.0, chainMode: true, space: new PositionSpace(0.1, 0, []));
        Near(-HolePositionPlanner.OrdinateOffset, chain.OrdinateGroups.Single(g => g.Axis == PositionAxis.Horizontal).At.Y);
    }
}
