using System.Text.Json;
using HistoryStrenua;
using HistoryVulcan.Core.Commands;

/// <summary>离线测试：共用的避障、尺寸几何、视图朝向（Shared/）。</summary>
internal static partial class Tests
{
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
}
