using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>REQ-DQ-02：全页标注完成后重读显示几何，移动文字消除后续步骤新引入的冲突；不改尺寸值和附着。</summary>
internal static class DrawingFinish
{
    public static QuickCommand Command { get; } = new(
        "drawing-finish", StrenuaIdentity.Domain + ".drawing.finish", "整理标注",
        "当前页标注完成后，按整页轮廓、注解和图框避让孔标注与线性尺寸，再按真实标注占位排版；未解冲突明确报告，不保存。",
        "不用点视图：标注后按一下，孔标注移到空白处，线性尺寸先沿线滑、必要时在视图外换层；不改数值、公差和附着。按全部注解真实占位排版，再复核避让。遵守避障开关；坐标尺寸不移动。仍有冲突则列出标注名称，请检查后保存。",
        context => Run(context, HoleScan.ActiveDrawing(context)));

    internal static QuickOutcome Run(QuickCommandContext context, object drawing)
    {
        if (!context.Options.Clearance) return QuickOutcome.Ok("整理标注：避障关闭，未移动标注。");
        context.SetState("整理标注");
        var first = Clear(context, drawing);
        var arranged = DrawingArrange.Run(context, DrawingSheet.Read(context, drawing, "整理标注"));
        var last = Clear(context, drawing);
        var message = $"整理标注：避让移动 {first.Moved + last.Moved} 次，剩余 {last.Stuck.Count} 个文字冲突。"
            + (last.Stuck.Count > 0 ? "\n· " + string.Join("、", last.Stuck) : string.Empty)
            + "\n" + arranged.Message;
        return last.Stuck.Count == 0 && arranged.Success ? QuickOutcome.Ok(message) : QuickOutcome.Fail(message);
    }

    private sealed record Shape(object Annotation, string Name, SheetRect View, bool Movable, bool Callout,
        IReadOnlyList<SheetSegment> Lines, IReadOnlyList<TextBox> Texts);

    private static (int Moved, List<string> Stuck) Clear(QuickCommandContext context, object drawing)
    {
        var api = context.Api;
        var shapes = new List<Shape>();
        var geometry = new List<SheetSegment>(DrawingSheet.FrameLines(api, drawing));
        foreach (var view in HoleScan.SheetViews(api, drawing))
        {
            var name = DrawingSheet.Name(api, view);
            var rect = DrawingSheet.Outline(api, view);
            if (api.Call(view, "IView", "get_ReferencedDocument") is not null && !HoleScan.Frame(context, view).Axonometric)
            {
                var scan = HoleScan.Scan(context, "整理标注", withLines: true, view: view, quiet: true);
                geometry.AddRange(scan.Lines);
                geometry.AddRange(scan.CurveSegments);
                foreach (var hole in scan.Candidates) geometry.AddRange(ClearancePlanner.Octagon(new SheetPoint(hole.X, hole.Y), hole.Radius));
            }
            foreach (var annotation in api.CallArray(view, "IView", "GetAnnotations"))
            {
                var (lines, texts) = Clearance.DisplayGeometry(api, annotation);
                var annotationType = api.CallInt(annotation, "IAnnotation", "GetType");
                var display = annotationType == 4
                    ? api.Call(annotation, "IAnnotation", "GetSpecificAnnotation") : null;
                var callout = display is not null && api.CallBool(display, "IDisplayDimension", "IsHoleCallout");
                var noteText = annotationType == 6 && api.Call(annotation, "IAnnotation", "GetSpecificAnnotation") is { } note
                    ? api.CallString(note, "INote", "GetText") : string.Empty;
                // 框架一键的“采用 xx 铝型材”是普通注释，不能留在轴测图边线上；它与尺寸一样可沿图纸移动，
                // 引线会跟随注释。其他用户普通注释保持原位。
                var movable = display is not null && (callout || HolePositionPlanner.IsLinear(api.CallInt(display, "IDisplayDimension", "get_Type2")))
                    || noteText.Contains("铝型材", StringComparison.Ordinal);
                if (movable && !callout && display is not null && api.Call(display, "IDisplayDimension", "GetDimension2", 0) is { } dimension)
                {
                    var mm = Convert.ToDouble(api.Call(dimension, "IDimension", "get_SystemValue")) * 1000;
                    // 只去掉精确整数/一位小数多显示的零；47.207 之类模型真实尺寸绝不四舍五入成 47.2。
                    if (ExactPrecision(mm) is { } precision)
                    {
                        api.Call(display, "IDisplayDimension", "SetPrecision3", precision,
                            api.CallInt(display, "IDisplayDimension", "GetAlternatePrecision2"),
                            api.CallInt(display, "IDisplayDimension", "GetPrimaryTolPrecision2"),
                            api.CallInt(display, "IDisplayDimension", "GetAlternateTolPrecision2"));
                        (lines, texts) = Clearance.DisplayGeometry(api, annotation);
                    }
                }
                shapes.Add(new Shape(annotation, name + "/" + api.CallString(annotation, "IAnnotation", "GetName"), rect, movable, callout, lines, texts));
            }
        }
        var keepouts = DrawingSheet.KeepOutRects(api, drawing).Select(r => new TextBox(r.Left, r.Bottom, r.Width, r.Height)).ToList();
        var frame = DrawingSheet.FrameRect(api, drawing);
        if (frame is null) throw new QuickCommandException("整理标注：读不到图框，未移动标注。");
        var moved = 0;
        Obstacles ObstaclesFor(int i) => new(geometry.Concat(shapes.Where((_, j) => i != j).SelectMany(s => s.Lines)).ToList(),
            keepouts.Concat(shapes.Where((_, j) => i != j).SelectMany(s => s.Texts)).ToList());
        int Hits(Shape s, Obstacles obstacles) => ClearancePlanner.Hits(s.Texts, obstacles)
            + (AnnotationPlacementPlanner.Within(s.Texts, frame.Value) ? 0 : 100);
        // 后加的尺寸界线也会压到先前文字，所以整页跑有上限的三轮。每次读回确认变好才保留，避免反复漂移。
        for (var pass = 0; pass < 3; pass++)
        {
            var changed = false;
            for (var i = 0; i < shapes.Count; i++)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                var shape = shapes[i];
                if (!shape.Movable || shape.Texts.Count == 0) continue;
                var obstacles = ObstaclesFor(i);
                var hits = Hits(shape, obstacles);
                if (hits == 0) continue;
                var position = api.CallDoubles(shape.Annotation, "IAnnotation", "GetPosition");
                if (position.Length < 3) continue;
                foreach (var offset in AnnotationPlacementPlanner.Offsets(shape.Texts, shape.View, shape.Callout))
                {
                    var shifted = ClearancePlanner.Shift(shape.Texts, offset.X, offset.Y);
                    if (!AnnotationPlacementPlanner.Within(shifted, frame.Value) || ClearancePlanner.Hits(shifted, obstacles) > 0) continue;
                    var accepted = false;
                    try
                    {
                        if (!api.CallBool(shape.Annotation, "IAnnotation", "SetPosition2", position[0] + offset.X, position[1] + offset.Y, position[2])) continue;
                        var (lines, texts) = Clearance.DisplayGeometry(api, shape.Annotation);
                        var after = shape with { Lines = lines, Texts = texts };
                        // SW 移动孔标注时会换引线端、重排公差文字；预测平移不等于实际显示，必须读回。
                        var crossing = lines.Count(line => obstacles.Texts.Any(text => ClearancePlanner.Hits(text, line)));
                        var beforeCrossing = shape.Lines.Count(line => obstacles.Texts.Any(text => ClearancePlanner.Hits(text, line)));
                        if (texts.Count > 0 && Hits(after, obstacles) < hits && crossing <= beforeCrossing)
                        {
                            shapes[i] = after; moved++; changed = true; accepted = true;
                        }
                    }
                    finally
                    {
                        // 中途取消、COM 失败或试探没改善都要恢复原位置；失败不能继续用过期几何。
                        if (!accepted && !Convert.ToBoolean(api.CallForCleanup(shape.Annotation, "IAnnotation", "SetPosition2", position[0], position[1], position[2])))
                            throw new QuickCommandException("整理标注：无法还原「" + shape.Name + "」的试探位置，请检查此标注。");
                    }
                    if (accepted) break;
                }
            }
            if (!changed) break;
        }
        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        api.Call(drawing, "IModelDoc2", "GraphicsRedraw2");
        return (moved, shapes.Where((s, i) => s.Texts.Count > 0 && Hits(s, ObstaclesFor(i)) > 0).Select(s => s.Name).ToList());
    }

    internal static int? ExactPrecision(double millimeters)
        => !double.IsFinite(millimeters) ? null
            : Math.Abs(millimeters - Math.Round(millimeters)) < 1e-7 ? 0
            : Math.Abs(millimeters - Math.Round(millimeters, 1)) < 1e-7 ? 1 : null;
}
