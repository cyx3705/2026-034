using HistoryStrenua;

internal static partial class Tests
{
    static void TestDrawingQuality()
    {
        Equal(1, DrawingFinish.ExactPrecision(17.5)!.Value);
        True(DrawingFinish.ExactPrecision(47.207) is null, "不能靠舍入真实尺寸掩盖两位小数警告");
        var outline = new SheetRect(0.05, 0.05, 0.15, 0.15);
        var texts = new List<SheetRect> { new(0.12, 0.03, 0.20, 0.06), new(0.02, 0.15, 0.07, 0.18) };
        var box = DrawingSheet.MeasuredBox(outline, new ViewBox(0.1, 0.1, 0.01, 0.01), texts);
        True(box.MarginRight >= 0.0499 && box.MarginBottom >= 0.0199, "右侧孔标注和下方尺寸必须留地方");
        var shifted = DrawingPlanner.Occupied(new SheetPoint(0.2, 0.2), box);
        True(shifted.Any(r => r.Left <= 0.120001 && r.Top >= 0.279999), "左上角文字不可被 L 形留白漏掉");
        var right = DrawingPlanner.Beside(outline, box, ViewSlot.Right, new ViewBox(0.02, 0.1, 0.01, 0));
        True(right.X - 0.01 > 0.20, "投影视图不能压到主视图右侧文字");
        var text = new TextBox(0.12, 0.09, 0.035, 0.007);
        var obstacle = new Obstacles([], [new TextBox(0.05, 0.05, 0.1, 0.1)]);
        var frame = new SheetRect(0, 0, 0.3, 0.2);
        True(AnnotationPlacementPlanner.Offsets([text], outline, true).Any(p =>
            AnnotationPlacementPlanner.Within([text.Shift(p.X, p.Y)], frame) && ClearancePlanner.Hits([text.Shift(p.X, p.Y)], obstacle) == 0), "孔标注应找到视图外空位");
        True(!AnnotationPlacementPlanner.Within([text.Shift(0.3, 0)], frame), "越框候选必须拒绝");
    }

    static void TestCountedChamfers()
    {
        var edges = new[] { new SheetSegment(0, 0.002, 0.002, 0), new SheetSegment(0.098, 0, 0.1, 0.002),
            new SheetSegment(0.1, 0.098, 0.098, 0.1), new SheetSegment(0.002, 0.1, 0, 0.098) };
        var dimension = new ViewDimension(0, 2, false, 0.002, [DimensionAnchor.Line(edges[0], true)], "4 x C");
        var view = new CheckView("test", "part", new ViewFrame(new(1,0,0), new(0,1,0), new(0,0,1), new(0,0), 1), edges, [], [], [dimension], []);
        Equal(4, DimensionCheckPlanner.CoveredChamfers(view).Count);
        Equal(1, DimensionCheckPlanner.CoveredChamfers(view with { Dimensions = [dimension with { Prefix = "C" }] }).Count);
        Equal(1, DimensionCheckPlanner.CoveredChamfers(view with { Dimensions = [dimension with { Prefix = "2 x C" }] }).Count);
        Equal(0, DimensionCheckPlanner.CoveredChamfers(view with { Dimensions = [dimension with { Prefix = "4 x " }] }).Count);
    }
}
