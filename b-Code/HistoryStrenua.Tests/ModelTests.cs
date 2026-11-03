using HistoryStrenua;

/// <summary>1.38.0：螺纹孔写法、符号码、建模与装配参数、笔记上限。</summary>
internal static partial class Tests
{
    static void TestSymbolTokens()
    {
        Equal("4 x M6<HOLE-DEPTH>12 底孔 <MOD-DIAM>", SwText.Tokens("4 x M6↧12 底孔 Ø"));
        Equal("<MOD-DIAM>5 <MOD-PM>0.02", SwText.Tokens("⌀5 ±0.02"));
        Equal("2 x C", SwText.Tokens("2 x C"));
        Equal("4 x M6↧12", SwText.Readable("4 x M6<HOLE-DEPTH>12"));
    }

    static void TestThreadSpec()
    {
        var m6 = ThreadSpec.Parse("M6");
        Equal("M6", m6.Text);
        Equal(5.0, m6.Drill);
        True(m6.Fits(5) && !m6.Fits(6.6), "M6 底孔 Ø5");
        Equal(4.2, ThreadSpec.Parse("m5").Drill);
        Equal(10.2, ThreadSpec.Parse("M12").Drill);
        var fine = ThreadSpec.Parse("M8x1");
        Equal("M8x1", fine.Text);
        Equal(7.0, fine.Drill);
        Equal("M6-6H", ThreadSpec.Parse("M6-6H").Text);
        Throws(() => ThreadSpec.Parse("6"), "缺 M");
        Throws(() => ThreadSpec.Parse("M7"), "粗牙表里没有要写螺距");

        Equal(("4 x M6<HOLE-DEPTH>12", "孔<HOLE-DEPTH>15"), m6.Lines(4, 12, 15));
        Equal(("4 x M6 贯穿", string.Empty), m6.Lines(4, null, null));
        Equal(("M6<HOLE-DEPTH>12", string.Empty), m6.Lines(1, 12, null));
        Equal(("2 x M6<HOLE-DEPTH>12", string.Empty), m6.Lines(2, 12, 12));

        // 普通孔标注：第一个「↧数」是底孔深；已写成螺纹的（真机货叉托板手改的那条）底孔深是最后一个。
        Equal(15.0, ThreadSpec.ReadDrillDepth("4 x <MOD-DIAM>5<HOLE-DEPTH>15"));
        Equal(null, ThreadSpec.ReadDrillDepth("4 x <MOD-DIAM>6.60 贯穿"));
        Equal(15.0, ThreadSpec.ReadDrillDepth("4 x M6 ↧12 底孔 Ø5<HOLE-DEPTH>15"));
        Equal(15.0, ThreadSpec.ReadDrillDepth("4 x M6<HOLE-DEPTH>12 孔<HOLE-DEPTH>15"));
        Equal(null, ThreadSpec.ReadDrillDepth("4 x M6<HOLE-DEPTH>12"));
        Equal(4, ThreadSpec.ReadCount("4 x <MOD-DIAM>5"));
        Equal(1, ThreadSpec.ReadCount("<MOD-DIAM>5"));
    }

    static void TestModelSpec()
    {
        var shapes = ModelSpec.Shapes("rect:0,0,200,120; circles:6.6,-80,-40,80,-40 ;poly:0,0,10,0,0,10；slot:0,0,20,0,6");
        Equal(5, shapes.Count);
        True(shapes[0] is SketchShape.Rect { Width: 200, Height: 120 }, "中心矩形");
        True(shapes[1] is SketchShape.Circle { X: -80, Diameter: 6.6 } && shapes[2] is SketchShape.Circle { X: 80 }, "同径多孔拆成一个个圆");
        True(shapes[3] is SketchShape.Polygon { Points.Count: 3 }, "多边形");
        True(shapes[4] is SketchShape.Slot { Width: 6 }, "直槽");
        Throws(() => ModelSpec.Shapes("rect:0,0,10"), "数不够");
        Throws(() => ModelSpec.Shapes("hex:0,0,10"), "不认识的图形");
        Throws(() => ModelSpec.Shapes("circle:0,0,-3"), "直径为负");
        Throws(() => ModelSpec.Shapes("poly:0,0,1,1"), "多边形至少三点");

        // 摆位：零件 x→总装 X，y→−Z，z→Y（板件平放，2026-043 立柱底板就是这样摆的）。
        var up = ModelSpec.Axes("X,-Z,Y");
        Equal("X,-Z,Y", ModelSpec.AxesText(up));
        var data = ModelSpec.TransformData(up, (-600, 262, 0));
        Equal(-0.6, data[9]);
        Equal(0.262, data[10]);
        Equal(-1.0, data[5]);
        Throws(() => ModelSpec.Axes("X,Z,Y"), "左手系要拒");
        Throws(() => ModelSpec.Axes("X,X,Y"), "不垂直要拒");
        Throws(() => ModelSpec.Axes("X,Y"), "少一根轴");
        Equal("X,Y,Z", ModelSpec.AxesText(ModelSpec.Axes("1,0,0,0,2,0,0,0,1")));
        Equal((1.0, 2.0, -3.5), ModelSpec.Point("1, 2, -3.5", "at"));
        Equal(1, ModelSpec.StandardPlane("上视"));
        Equal(null, ModelSpec.StandardPlane("基准面1"));
    }

    static void TestNoteLimits()
    {
        var notes = new WorkNotes(null);
        True(notes.Write("dim", new string('字', WorkNotes.NoteLimit + 1), null) is not null, "一条太长要拒");
        for (var i = 0; i < WorkNotes.AreaLimit; i++)
            Equal(null, notes.Write("dim", $"第 {i + 1} 条", null));
        True(notes.Write("dim", "再多一条", null) is not null, "满了要拒");
        Equal(null, notes.Write("dim", "合并后的一条", 1));
        Equal(null, notes.Write("dim", QuickArgs.None, 2));
        Equal(WorkNotes.AreaLimit - 1, notes.Read("dim").Count);
        True(WorkNotes.Area("model") == "model" && WorkNotes.Area("装配") == "asm", "建模、装配也有分区");
    }
}
