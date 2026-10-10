using System.Text.Json;
using HistoryStrenua;
using HistoryVulcan.Core.Commands;

/// <summary>离线测试：基础类（Drawing/）。</summary>
internal static partial class Tests
{
    static void TestMainViewRetry()
    {
        // 1.16.0 真机滑动板：零件改过标准视图，「*前视」实际沿 +X 看（右 = −Z、上 = +Y）。想要沿 +Z 看 → 应改用「*左视」。
        var actual = new ViewFrame(new ModelDirection(0, 0, -1), new ModelDirection(0, 1, 0), new ModelDirection(1, 0, 0), new SheetPoint(0, 0), 1);
        var front = StandardView.Of("front");
        True(!DrawingPlanner.Facing(actual, front.Normal), "朝向不对要认出来");
        var retry = DrawingPlanner.RetryViews(front, actual, front.Normal);
        Equal("left", retry[0].Key);
        Equal(5, retry.Count);
        // 没改过的零件：表里每个视图实际就是表上的方向。
        var standard = new ViewFrame(front.Right, front.Up, front.Normal, new SheetPoint(0, 0), 1);
        foreach (var view in StandardView.All)
            True(DrawingPlanner.ActualNormal(front, standard, view).Dot(view.Normal) > 0.999, view.Key);
        Equal("+X", DrawingCreate.DescribeNormal(new ModelDirection(1, 0, 0)));
    }

    static void TestDrawingTemplates()
    {
        var a3 = DrawingTemplate.FromFile(@"C:\t\零件 - 高悦精密A3.drwdot")!;
        Equal(0.420, a3.Width);
        Equal(0.297, a3.Height);
        Equal("A3", a3.SizeName);
        var landscape = DrawingTemplate.FromFile(@"C:\t\零件 - 高悦精密A4横向.drwdot")!;
        Equal(0.297, landscape.Width);
        Equal("A4 横", landscape.SizeName);
        var portrait = DrawingTemplate.FromFile(@"C:\t\零件 - 高悦精密A4纵向.drwdot")!;
        Equal(0.210, portrait.Width);
        Equal("A4 竖", portrait.SizeName);
        True(DrawingTemplate.FromFile(@"C:\t\gb_a4.drwdot")!.Width < 0.25, "名字不写横竖的 A4 按竖放");
        True(DrawingTemplate.FromFile(@"C:\t\gb_a2.drwdot")!.Width > 0.5, "A3 以上不写横竖按横放");
        True(DrawingTemplate.FromFile(@"C:\t\标准模板.drwdot") is null, "认不出图幅的不算");
        True(DrawingTemplate.FromFile(@"C:\t\A10 图框.drwdot") is null, "A10 不是图幅");

        // 零件图只用名字带「零件」的；有横放的就只用横放的；图幅从小到大。
        var ranked = UserTemplates();
        Equal("零件 - 高悦精密A4横向,零件 - 高悦精密A3,零件 - 高悦精密A2", string.Join(",", ranked.Select(t => t.Name)));
        // 没有「零件」模板时退到不带「装配」的。
        var fallback = DrawingTemplate.Rank(new[] { "装配体 - A3.drwdot", "gb_a3.drwdot", "gb_a4.drwdot" }.Select(n => DrawingTemplate.FromFile(@"C:\t\" + n)!));
        Equal("gb_a3", string.Join(",", fallback.Select(t => t.Name)));
    }

    static void TestDrawingMainView()
    {
        // 用户 15 张手工图的主视图。差异只在两处（外加两处同轴另一面）：
        // 外壳2 用户看的是投影面积最大的上视、孔却在侧面，本模块取孔与圆弧最多的那一面；连接件、外壳沿轴两面都没有孔口，取了正向的那一面。
        var accepted = new Dictionary<string, string[]>
        {
            ["XLL-HDYY-01-04-02 连接件"] = ["back", "front"],
            ["XLL-HDYY-01-04-09 外壳2"] = ["top", "right", "left"],
            ["XLL-HDYY-01-04-10 外壳"] = ["left", "right"],
        };
        var matched = 0;
        foreach (var (name, main, _, _, geometry) in RealParts())
        {
            var chosen = DrawingPlanner.MainView(geometry).View.Key;
            var ok = accepted.TryGetValue(name, out var allowed) ? allowed.Contains(chosen) : chosen == main;
            True(ok, $"{name}：主视图应为 {main}，实际 {chosen}");
            if (chosen == main)
                matched++;
        }

        True(matched >= 12, $"与手工图完全一致的主视图应至少 12 张，实际 {matched}");

        // 板件：孔最多的一面，沉头孔朝哪面看哪面。
        var plate = Plate(holesOpenTo: new ModelDirection(0, 0, -1));
        Equal("back", DrawingPlanner.MainView(plate).View.Key);
        // L 形件：截面上 3 个圆角（沿 Z），两条腿侧面各 2 个孔（沿 X、沿 Y）——看截面。
        Equal("front", DrawingPlanner.MainView(Bracket()).View.Key);
    }

    static void TestDrawingSheet()
    {
        var templates = UserTemplates();
        var verbose = Environment.GetEnvironmentVariable("STRENUA_PLAN") == "1";
        var matched = 0;
        var lines = new List<string>();
        foreach (var (name, main, sheet, scale, geometry) in RealParts())
        {
            var chosen = DrawingPlanner.MainView(geometry).View;
            var frame = new ViewFrame(chosen.Right, chosen.Up, chosen.Normal, new SheetPoint(0, 0), 1);
            var sides = DrawingPlanner.SideViews(geometry, frame, firstAngle: true);
            var choice = DrawingPlanner.ChooseSheet(geometry, chosen, sides, templates, chain: false);
            True(choice is not null, $"{name}：哪个模板都放不下");
            var size = choice!.Template.SizeName.Split(' ')[0];
            var denominator = (int)Math.Round(1 / choice.Scale);
            if (size == sheet && denominator == scale)
                matched++;
            lines.Add($"{name}: 用户 {sheet} 1:{scale}，本次 {choice.Template.SizeName} {DrawingPlanner.ScaleText(choice.Scale)}（{chosen.Key}，{string.Join("/", choice.Slots)}）{choice.Reason}"
                + $"；轴测 ×{choice.Layout.IsoShrink:0.##}{(choice.Layout.Problem.Length > 0 ? "；" + choice.Layout.Problem : string.Empty)}");
            if (verbose)
            {
                var userTemplate = templates.First(t => t.SizeName.StartsWith(sheet, StringComparison.Ordinal));
                var request = DrawingPlanner.Estimate(geometry, chosen, choice.Slots, 1.0 / scale, chain: false);
                var layout = DrawingPlanner.Layout(SheetSpace.Standard(userTemplate.Width, userTemplate.Height), request);
                lines.Add($"    用户那张 {sheet} 1:{scale}：{(layout.Fits ? "放得下" : layout.Problem)}；主视图 {request.Main.Width * 1000:0}×{request.Main.Height * 1000:0} 留 {request.Main.MarginLeft * 1000:0}，"
                    + string.Join("，", request.Sides.Select(p => $"{p.Key} {p.Value.Width * 1000:0}×{p.Value.Height * 1000:0} 留 {p.Value.MarginLeft * 1000:0}"))
                    + $"，轴测 {request.Iso.Width * 1000:0}×{request.Iso.Height * 1000:0}");
            }
            // 每张都要真的排得下：视图组、轴测图、技术要求互不重叠、都在图框里、不压标题栏。
            True(choice.Layout.Fits, $"{name}：选出来的版面排不下 {choice.Layout.Problem}");
            // 1.10.0：技术要求都要有空地（挤不下时轴测图再缩一档；三个限位块多了倒角的下视图后原比例轴测图占了它的地方）。
            True(choice.Layout.Problem.Length == 0, $"{name}：{choice.Layout.Problem}");
        }

        if (verbose)
            Console.WriteLine(string.Join(Environment.NewLine, lines));
        // 1.10.0：投影视图在与主视图共用的方向上没有孔就不留尺寸空间、视图外框后面那 4 mm 可以压图框线，外壳2 也对上了（A4 1:5），只剩后板 A2。
        True(matched >= 14, $"图幅与比例和手工图一致的应至少 14 张，实际 {matched}：" + Environment.NewLine + string.Join(Environment.NewLine, lines));
    }

    static void TestDrawingSideViews()
    {
        // 板件：侧面没有孔也没有圆弧面——只加一个看厚度的，宽板摆下边，也可换到右边。
        var plate = Plate(new ModelDirection(0, 0, -1));
        var back = StandardView.Of("back");
        var plateSides = DrawingPlanner.SideViews(plate, Frame(back), firstAngle: true);
        Equal(1, plateSides.Count);
        Equal(ViewSlot.Below, plateSides[0].Slot);
        Equal(ViewSlot.Right, plateSides[0].Alternative!.Value);
        Equal("Below|Right", string.Join("|", DrawingPlanner.Arrangements(plateSides).Select(slots => string.Join(",", slots))));

        // L 形件看截面：竖腿的孔沿图纸 X、孔口朝 −X——第一角投影里摆右边的视图看的正是零件左侧（−X）；
        // 横腿的孔沿图纸 Y、两头都通——摆下边（看零件上面）。第三角投影：右边看 +X，于是摆左边。
        var front = StandardView.Of("front");
        var first = DrawingPlanner.SideViews(Bracket(), Frame(front), firstAngle: true);
        Equal("Right,Below", string.Join(",", first.Select(side => side.Slot)));
        True(first.All(side => side.Alternative is null), "看孔的投影视图不能换边（换了就看不到孔口那一面）");
        var third = DrawingPlanner.SideViews(Bracket(), Frame(front), firstAngle: false);
        Equal("Left,Below", string.Join(",", third.Select(side => side.Slot)));

        // 侧面只有圆弧面：只加一个，加在圆弧面多的方向；两个方向都有时可换边（用户外壳只加了一个右视图）。
        var rounded = new PartGeometry(new ModelBox(0, 0, 0, 0.2, 0.1, 0.05),
        [
            new(new ModelDirection(1, 0, 0), new ModelDirection(0, 0.01, 0.01), 0.005, false, false, "Fillet1", []),
            new(new ModelDirection(1, 0, 0), new ModelDirection(0, 0.09, 0.01), 0.005, false, false, "Fillet1", []),
            new(new ModelDirection(0, 1, 0), new ModelDirection(0.01, 0, 0.01), 0.005, false, false, "Fillet2", []),
            new(new ModelDirection(0, 1, 0), new ModelDirection(0.19, 0, 0.01), 0.0008, false, false, "Fillet3", []),
        ], []);
        var arcs = DrawingPlanner.SideViews(rounded, Frame(front), firstAngle: true);
        Equal(1, arcs.Count);
        Equal(ViewSlot.Right, arcs[0].Slot);
        Equal(ViewSlot.Below, arcs[0].Alternative!.Value);
        True(arcs[0].Reason.Contains("圆弧面 2 张", StringComparison.Ordinal), "R1 以下的修边圆角不算：" + arcs[0].Reason);
    }

    static void TestDrawingLayout()
    {
        var a3 = SheetSpace.Standard(0.420, 0.297);
        var request = new LayoutRequest(
            new ViewBox(0.150, 0.100, 0.040, 0.040),
            new Dictionary<ViewSlot, ViewBox> { [ViewSlot.Right] = new(0.020, 0.100, 0.016, 0.016) },
            new ViewBox(0.080, 0.080, 0, 0),
            0.101, 0.042, [1.0, 0.5]);
        var layout = DrawingPlanner.Layout(a3, request);
        True(layout.Fits && layout.Problem.Length == 0, "A3 上一个 150×100 的主视图加右视图、轴测图、技术要求应排得开：" + layout.Problem);
        var main = SheetRect.Around(layout.Main, 0.150, 0.100);
        var right = SheetRect.Around(layout.Sides[ViewSlot.Right], 0.020, 0.100);
        var iso = SheetRect.Around(layout.Iso!.Value, 0.080 * layout.IsoShrink, 0.080 * layout.IsoShrink);
        var note = new SheetRect(layout.NoteTopLeft!.Value.X, layout.NoteTopLeft.Value.Y - 0.042, layout.NoteTopLeft.Value.X + 0.101, layout.NoteTopLeft.Value.Y);
        Near(layout.Main.Y, layout.Sides[ViewSlot.Right].Y);
        True(right.Left - main.Right >= 0.016, "右视图与主视图之间要给右视图的竖直尺寸留地方");
        True(main.Left - a3.Frame.Left >= 0.040 && a3.Frame.Top - main.Top >= 0.040, "主视图左边、上边留标注空间");
        foreach (var (name, rect) in new[] { ("主视图", main), ("右视图", right), ("轴测图", iso), ("技术要求", note) })
        {
            True(rect.Within(a3.Frame), $"{name}出了图框");
            True(!a3.KeepOuts.Any(rect.Overlaps), $"{name}压到标题栏等");
        }

        True(!iso.Overlaps(main) && !iso.Overlaps(right) && !note.Overlaps(iso) && !note.Overlaps(main), "轴测图、技术要求不压视图");
        True(note.Bottom >= a3.TitleBlock.Top && note.Right <= a3.TitleBlock.Right, "技术要求先放标题栏正上方");

        // 最左边是左视图、它上边的尺寸条会压到左上角图号框：整组往下（或往右）挪开，而不是判放不下（首版真机就栽在这）。
        var left = new LayoutRequest(
            new ViewBox(0.100, 0.060, 0.016, 0.016),
            new Dictionary<ViewSlot, ViewBox> { [ViewSlot.Left] = new(0.030, 0.060, 0.016, 0.016) },
            new ViewBox(0.050, 0.050, 0, 0), 0.101, 0.042, [1.0]);
        var shifted = DrawingPlanner.Layout(a3, left);
        True(shifted.Fits, "左视图压图号框时应挪开：" + shifted.Problem);
        var leftView = SheetRect.Around(shifted.Sides[ViewSlot.Left], 0.030, 0.060);
        var strip = new SheetRect(leftView.Left, leftView.Top, leftView.Right, leftView.Top + 0.016);
        True(!a3.KeepOuts.Any(strip.Overlaps), "左视图上边的尺寸条不能压图号框");

        // 主视图本身就比图框大：放不下，说清原因。
        var huge = DrawingPlanner.Layout(a3, request with { Main = new ViewBox(0.400, 0.260, 0.040, 0.040) });
        True(!huge.Fits && huge.Problem.Length > 0, "放不下要说原因");

        // 视图组放得下、技术要求找不到完全空的地方：照样算排得下，技术要求放在压得最少处并说明。
        var crowded = DrawingPlanner.Layout(a3, request with { NoteWidth = 0.380, NoteHeight = 0.100 });
        True(crowded.Fits && crowded.NoteTopLeft is not null && crowded.Problem.Contains("技术要求", StringComparison.Ordinal), "技术要求挤不下时放在压得最少处：" + crowded.Problem);
    }

    static void TestDrawingScales()
    {
        Equal("1:5", DrawingPlanner.ScaleText(0.2));
        Equal("1:3", DrawingPlanner.ScaleText(1.0 / 3));
        Equal("2:1", DrawingPlanner.ScaleText(2));
        Equal((1, 3), DrawingPlanner.ScaleRatio(1.0 / 3));
        Equal((2, 1), DrawingPlanner.ScaleRatio(2));
        var plate = Plate(new ModelDirection(0, 0, 1));
        Equal(1.0, DrawingPlanner.Scales(plate)[0]);
        var small = new PartGeometry(new ModelBox(0, 0, 0, 0.03, 0.02, 0.01), [], []);
        Equal(2.0, DrawingPlanner.Scales(small)[0]);
        var tiny = new PartGeometry(new ModelBox(0, 0, 0, 0.012, 0.01, 0.005), [], []);
        Equal(5.0, DrawingPlanner.Scales(tiny)[0]);
        // 轴测图可缩成：原比例、再往下两档（1:2 → 1:3、1:5）。
        Equal("1,0.667,0.4", string.Join(",", DrawingPlanner.IsoFactors(plate, 0.5).Select(f => f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))));
        // 尺寸空间：一种孔一层；尺寸链模式也按种数留（孔标注往左上引出），只是最少 26 mm。
        Near(0.034, DrawingPlanner.AnnotationMargin(3, chain: false));
        Near(0.034, DrawingPlanner.AnnotationMargin(3, chain: true));
        Near(0.026, DrawingPlanner.AnnotationMargin(0, chain: true));
        Near(0.016, DrawingPlanner.AnnotationMargin(0, chain: false));
        Near(0.080, DrawingPlanner.AnnotationMargin(20, chain: false));
        // 标准视图的朝向（真机读回）：右手系、图纸 X × 图纸 Y = 朝看图的人。
        foreach (var view in StandardView.All)
        {
            var cross = PartHole.Cross(view.Right, view.Up);
            Near(1, cross.Dot(view.Normal));
        }
    }

    static void TestDrawingSlotOf()
    {
        // 第一角投影（国标）：看零件左侧（视线 −X）的摆右边、看右侧的摆左边、看上面（+Y）的摆下边、看下面的摆上边；第三角反过来。
        foreach (var view in StandardView.All)
        {
            var frame = Frame(view);
            ModelDirection Minus(ModelDirection d) => new(-d.X, -d.Y, -d.Z);
            Equal(ViewSlot.Right, DrawingPlanner.SlotOf(frame, Minus(view.Right), firstAngle: true)!.Value);
            Equal(ViewSlot.Left, DrawingPlanner.SlotOf(frame, view.Right, firstAngle: true)!.Value);
            Equal(ViewSlot.Below, DrawingPlanner.SlotOf(frame, view.Up, firstAngle: true)!.Value);
            Equal(ViewSlot.Above, DrawingPlanner.SlotOf(frame, Minus(view.Up), firstAngle: true)!.Value);
            Equal(ViewSlot.Left, DrawingPlanner.SlotOf(frame, Minus(view.Right), firstAngle: false)!.Value);
            Equal(ViewSlot.Above, DrawingPlanner.SlotOf(frame, view.Up, firstAngle: false)!.Value);
            // 同轴（前后视）、斜的（轴测图）不是投影位。
            True(DrawingPlanner.SlotOf(frame, view.Normal, firstAngle: true) is null, "主视图同向的不是投影位");
            True(DrawingPlanner.SlotOf(frame, Minus(view.Normal), firstAngle: true) is null, "主视图反向（后视）不是投影位");
            var (isoRight, _) = DrawingPlanner.IsoAxes(view.Normal, view.Right, view.Up);
            True(DrawingPlanner.SlotOf(frame, PartHole.Cross(isoRight, view.Up).Normalized(), firstAngle: true) is null, "斜的不是投影位");
        }

        // 与「要哪几个投影视图」同一套：L 形件第一角摆右边的那个，看的正是孔口那一面（−X）。
        var front = StandardView.Of("front");
        var sides = DrawingPlanner.SideViews(Bracket(), Frame(front), firstAngle: true);
        Equal(ViewSlot.Right, sides[0].Slot);
        Equal(ViewSlot.Right, DrawingPlanner.SlotOf(Frame(front), new ModelDirection(-1, 0, 0), firstAngle: true)!.Value);
        True(DrawingPlanner.Horizontal(ViewSlot.Left) && !DrawingPlanner.Horizontal(ViewSlot.Below), "左右视图看横向，上下视图看竖向");
    }

    static void TestDrawingBeside()
    {
        // 单独加投影视图时贴着主视图放：间距（Gap + 该留的标注空间）与整页排版排出来的一样。
        var a3 = SheetSpace.Standard(0.420, 0.297);
        var mainBox = new ViewBox(0.120, 0.080, 0.030, 0.026);
        foreach (var slot in new[] { ViewSlot.Right, ViewSlot.Left, ViewSlot.Below, ViewSlot.Above })
        {
            var side = DrawingPlanner.Horizontal(slot) ? new ViewBox(0.020, 0.080, 0.016, 0.018) : new ViewBox(0.120, 0.020, 0.022, 0.016);
            var layout = DrawingPlanner.Layout(a3, new LayoutRequest(mainBox, new Dictionary<ViewSlot, ViewBox> { [slot] = side }, new ViewBox(0, 0, 0, 0), 0, 0));
            True(layout.Fits, $"{slot}：{layout.Problem}");
            var main = SheetRect.Around(layout.Main, mainBox.Width, mainBox.Height);
            var beside = DrawingPlanner.Beside(main, mainBox, slot, side);
            Near(layout.Sides[slot].X, beside.X);
            Near(layout.Sides[slot].Y, beside.Y);
        }
    }

    static void TestDrawingChooseSlots()
    {
        // 「投影视图」单独按时，在当前图幅与比例下挑可换边视图的边——与「新建工程图」选图幅时估的那种摆法一致（15 个真零件）。
        var templates = UserTemplates();
        foreach (var (name, _, _, _, geometry) in RealParts())
        {
            var chosen = DrawingPlanner.MainView(geometry).View;
            var sides = DrawingPlanner.SideViews(geometry, Frame(chosen), firstAngle: true);
            var choice = DrawingPlanner.ChooseSheet(geometry, chosen, sides, templates, chain: false)!;
            var slots = DrawingPlanner.ChooseSlots(geometry, DrawingPlanner.ViewOf(Frame(chosen)), sides,
                SheetSpace.Standard(choice.Template.Width, choice.Template.Height), choice.Scale, chain: false);
            Equal(string.Join(",", choice.Slots), string.Join(",", slots));
        }

        // 宽板只看厚度：首选摆下边，下边那条压到 A3 的标题栏，换到右边（整组往下挪一点躲开修改栏）放得下。
        var wide = new PartGeometry(new ModelBox(0, 0, 0, 0.20, 0.19, 0.01), [], []);
        var front = StandardView.Of("front");
        var wideSides = DrawingPlanner.SideViews(wide, Frame(front), firstAngle: true);
        Equal(ViewSlot.Below, wideSides[0].Slot);
        Equal(ViewSlot.Right, DrawingPlanner.ChooseSlots(wide, front, wideSides, SheetSpace.Standard(0.420, 0.297), 1, chain: false)[0]);

        // 哪种摆法都放不下：用首选。
        var plate = Plate(new ModelDirection(0, 0, -1));
        var back = StandardView.Of("back");
        var plateSides = DrawingPlanner.SideViews(plate, Frame(back), firstAngle: true);
        var tiny = DrawingPlanner.ChooseSlots(plate, back, plateSides, SheetSpace.Standard(0.297, 0.210), 1, chain: false);
        Equal(plateSides[0].Slot, tiny[0]);
    }

    static void TestDrawingSpots()
    {
        var a3 = SheetSpace.Standard(0.420, 0.297);
        var main = DrawingPlanner.Occupied(new SheetPoint(0.120, 0.180), new ViewBox(0.150, 0.100, 0.030, 0.030)).ToList();
        // 轴测图：原比例放得下就不缩，躲开视图与标题栏。
        var (center, shrink, free) = DrawingPlanner.IsoSpot(a3, main, new ViewBox(0.080, 0.070, 0, 0), [1.0, 0.5], new SheetPoint(0.330, 0.180));
        var iso = SheetRect.Around(center, 0.080, 0.070);
        True(free && shrink == 1 && iso.Within(a3.Frame) && !main.Any(iso.Overlaps) && !a3.KeepOuts.Any(iso.Overlaps), "轴测图应放进空地");
        // 原比例哪都放不下、缩一半放得下。
        var (_, half, halfFree) = DrawingPlanner.IsoSpot(a3, main, new ViewBox(0.300, 0.200, 0, 0), [1.0, 0.5], new SheetPoint(0.330, 0.180));
        True(halfFree && half == 0.5, "放不下就缩一档");
        // 缩到底也没有空地：放在压得最少处，说明不是空地。
        var (_, last, crowded) = DrawingPlanner.IsoSpot(a3, main, new ViewBox(0.500, 0.400, 0, 0), [1.0, 0.5], new SheetPoint(0.330, 0.180));
        True(!crowded && last == 0.5, "没有空地要说明");

        // 技术要求：先放标题栏正上方。
        var (topLeft, noteFree) = DrawingPlanner.NotePlace(a3, 0.101, 0.042, main);
        True(noteFree && topLeft.Y - 0.042 >= a3.TitleBlock.Top && topLeft.X + 0.101 <= a3.TitleBlock.Right, "技术要求先放标题栏正上方");
        var everywhere = new List<SheetRect> { a3.Frame };
        True(!DrawingPlanner.NotePlace(a3, 0.101, 0.042, everywhere).Free, "满了要说明");

        // 只排视图（没有轴测图、技术要求）：不给它们位置。
        var bare = DrawingPlanner.Layout(a3, new LayoutRequest(new ViewBox(0.150, 0.100, 0.030, 0.030), new Dictionary<ViewSlot, ViewBox>(), new ViewBox(0, 0, 0, 0), 0, 0));
        True(bare.Fits && bare.Iso is null && bare.NoteTopLeft is null, "没有轴测图、技术要求就不排它们");
    }

    static void TestDrawingSteps()
    {
        // 1.16.0（用户定）：基础出图只做基础类各步；全图倒圆倒角做倒圆类三条全图；一键类的一键出图把各类的一键都做一遍。顺序写在用法里。
        void InOrder(string key, params string[] steps)
        {
            var command = QuickCommands.All.Single(item => item.Key == key);
            var positions = steps.Select(step => command.Usage.IndexOf(step, StringComparison.Ordinal)).ToList();
            True(positions.All(index => index >= 0) && positions.Zip(positions.Skip(1)).All(pair => pair.First < pair.Second), $"{command.Title}用法里的步骤顺序：" + command.Usage);
        }

        InOrder("drawing-basic", "新建工程图", "投影视图", "轴测图", "排版", "对称轴");
        InOrder("fillet-flow", "全图圆心", "全图圆弧", "全图倒角");
        // 1.17.0（用户定）：一键出图建好图后由 AI 判类别；三类各有一个按钮。外轮廓移出孔标注全流程，放在它后面。
        InOrder("onekey-drawing", "基础出图", "技术要求", "AI", "钣金", "框架", "加工件");
        InOrder("onekey-machined", "基础出图", "孔标注全流程", "全图外轮廓", "全图倒圆倒角");
        InOrder("onekey-sheetmetal", "基础出图", "孔标注全流程", "方形槽", "全图外轮廓");
        InOrder("onekey-frame", "基础出图", "全图外轮廓", "采用 xx 铝型材");
        True(!QuickCommands.All.Single(item => item.Key == "onekey-frame").Usage.Contains("孔标注全流程", StringComparison.Ordinal), "框架不标孔");
        foreach (var key in new[] { "onekey-sheetmetal", "onekey-frame", "onekey-machined" })
            Equal("onekey", QuickCommands.All.Single(item => item.Key == key).CommandClass);
        Equal("drawing", QuickCommands.All.Single(command => command.Key == "outline").CommandClass);
        Equal("drawing", QuickCommands.All.Single(command => command.Key == "outline-all").CommandClass);
        Equal("drawing", QuickCommands.All.Single(command => command.Key == "drawing-basic").CommandClass);
        Equal("fillet", QuickCommands.All.Single(command => command.Key == "fillet-flow").CommandClass);
        Equal("onekey", QuickCommands.All.Single(command => command.Key == "onekey-drawing").CommandClass);
        Equal("一键", QuickCommands.ClassTitle("onekey"));
        var titles = QuickCommands.All.Select(command => command.Title).ToHashSet();
        foreach (var step in new[] { "新建工程图", "投影视图", "轴测图", "排版", "对称轴", "全图圆心", "全图圆弧", "全图倒角", "孔标注全流程", "基础出图", "全图倒圆倒角" })
            True(titles.Contains(step), $"「{step}」要有自己的按钮与指令");
        True(!titles.Contains("技术要求"), "技术要求按钮已删（走模板表格）");
        Equal("hole", QuickCommands.All.Single(command => command.Title == "孔标注全流程").CommandClass);
        True(QuickCommands.All.Where(command => command.CommandClass == "drawing").All(command => command.Title != "孔标注全流程"), "孔标注全流程留在孔类");
    }
}
