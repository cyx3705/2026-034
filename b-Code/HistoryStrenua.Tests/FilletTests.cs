using System.Text.Json;
using HistoryStrenua;
using HistoryVulcan.Core.Commands;

/// <summary>离线测试：倒圆类（Fillet/）。</summary>
internal static partial class Tests
{
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
}
