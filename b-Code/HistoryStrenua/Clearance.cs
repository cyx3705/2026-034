using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>一次避让的结果：挪开了几个、几个找不到空位（原样留着）。</summary>
internal readonly record struct ClearanceResult(int Moved, int Stuck)
{
    /// <summary>回执里接在后面的一句；什么都没动时为空。</summary>
    public string Describe(string what)
        => Moved + Stuck == 0
            ? string.Empty
            : $"，避让挪开 {Moved} 个{what}" + (Stuck > 0 ? $"（{Stuck} 个找不到空位、原样留着）" : string.Empty);
}

/// <summary>
/// 标注避障（1.6.0，用户定：不另开指令，并进「孔标注」与「孔位尺寸」）：本视图里孔标注的文字、孔位尺寸的数字
/// 压在别的孔相关注解的线条或文字上，就挪开。几何判定见 <see cref="ClearancePlanner"/>。
/// </summary>
/// <remarks>
/// <para>只看孔相关的注解（用户定「本视图仅孔相关」）：孔标注、连着孔的线性尺寸、标在孔上的中心符号线、
/// 销钉符号、穿过孔心的中心线。外形尺寸、注释、别的视图都不看、也不动。</para>
/// <para>孔位尺寸的数字只沿尺寸线滑；孔标注换角位（右上、左下、右下，再放远），折点仍按下划线对准。
/// 挪完回读核对，还压着就算找不到空位。</para>
/// </remarks>
internal static class Clearance
{
    // swAnnotationType_e
    private const int AnnotationDimension = 4;
    private const int AnnotationDowel = 10;
    private const int AnnotationCenterMark = 13;
    private const int AnnotationCenterLine = 15;

    private enum Role
    {
        Callout,
        Dimension,
        Other,
    }

    /// <summary>一个孔相关注解在图纸上的样子。</summary>
    private sealed record Shape(object Annotation, Role Role, IReadOnlyList<SheetSegment> Lines, IReadOnlyList<TextBox> Texts, IReadOnlyList<SheetPoint> Centers);

    /// <summary>孔位尺寸的数字压线就沿尺寸线滑开。</summary>
    public static ClearanceResult ClearDimensions(QuickCommandContext context, ScannedView scan, IReadOnlyList<HoleEdge> holes)
        => Clear(context, scan, holes, Role.Dimension, (api, shapes, index, obstacles, hits) =>
        {
            var shape = shapes[index];
            var angle = shape.Texts[0].Angle;
            var (ux, uy) = (Math.Cos(angle), Math.Sin(angle));
            if (ClearancePlanner.Slide(shape.Texts, ux, uy, obstacles) is not { } offset || offset == 0)
                return false;
            return MoveBy(api, shape.Annotation, offset * ux, offset * uy);
        });

    /// <summary>孔标注的文字压线就换角位。</summary>
    public static ClearanceResult ClearCallouts(QuickCommandContext context, ScannedView scan, IReadOnlyList<HoleEdge> holes)
        => Clear(context, scan, holes, Role.Callout, (api, shapes, index, obstacles, hits) =>
        {
            var shape = shapes[index];
            if (shape.Centers.Count == 0
                || holes.Where(hole => HoleCalloutPlanner.SameCenter(shape.Centers[0], new SheetPoint(hole.X, hole.Y)))
                    .Cast<HoleEdge?>().FirstOrDefault() is not { } hole
                || HoleCallout.Shoulder(api, shape.Annotation, textLeft: true) is not { } shoulder
                || ClearancePlanner.ChooseCallout(shape.Texts, shoulder.Y, hole, obstacles, hits) is not { } spot)
                return false;
            var (dx, dy) = ClearancePlanner.CalloutShift(shape.Texts, shoulder.Y, spot);
            if (!MoveBy(api, shape.Annotation, dx, dy))
                return false;
            HoleCallout.AlignShoulder(api, shape.Annotation, spot.Shoulder, spot.TextLeft);
            return true;
        });

    /// <summary>
    /// 逐个检查 <paramref name="role"/> 的注解：压着就交给 <paramref name="move"/> 挪，挪完重读它、核对。
    /// 障碍是其余孔相关注解此刻的样子（前面挪过的按挪后的算）。
    /// </summary>
    private static ClearanceResult Clear(
        QuickCommandContext context,
        ScannedView scan,
        IReadOnlyList<HoleEdge> holes,
        Role role,
        Func<SolidWorksApi, List<Shape>, int, Obstacles, int, bool> move)
    {
        var api = context.Api;
        var shapes = ReadShapes(api, scan, holes);
        var moved = 0;
        var stuck = 0;
        for (var i = 0; i < shapes.Count; i++)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            if (shapes[i].Role != role || shapes[i].Texts.Count == 0)
                continue;
            var obstacles = ObstaclesFor(shapes, i);
            var hits = ClearancePlanner.Hits(shapes[i].Texts, obstacles);
            if (hits == 0)
                continue;

            if (move(api, shapes, i, obstacles, hits) && ReadShape(api, scan, holes, shapes[i].Annotation) is { } after)
            {
                shapes[i] = after;
                if (ClearancePlanner.Hits(after.Texts, obstacles) == 0)
                {
                    moved++;
                    continue;
                }
            }

            stuck++;
        }

        return new ClearanceResult(moved, stuck);
    }

    /// <summary>
    /// 第 <paramref name="index"/> 个注解要躲的：其余孔相关注解的线条与文字；孔位尺寸另加自己的尺寸界线
    /// （真机过渡板：「9.50」跨度 4.75 mm 装不下 9.2 mm 的字，只躲别人的线会滑成横跨自己的界线）。
    /// </summary>
    private static Obstacles ObstaclesFor(List<Shape> shapes, int index)
    {
        var others = shapes.Where((_, i) => i != index).ToList();
        var lines = others.SelectMany(shape => shape.Lines).ToList();
        var own = shapes[index];
        if (own.Role == Role.Dimension && own.Texts.Count > 0)
            lines.AddRange(ClearancePlanner.CrossingLines(own.Lines, own.Texts[0].Angle));
        return new Obstacles(lines, others.SelectMany(shape => shape.Texts).ToList());
    }

    private static bool MoveBy(SolidWorksApi api, object annotation, double dx, double dy)
    {
        var position = api.CallDoubles(annotation, "IAnnotation", "GetPosition");
        return position.Length >= 3
            && api.CallBool(annotation, "IAnnotation", "SetPosition2", position[0] + dx, position[1] + dy, position[2]);
    }

    /// <summary>视图里全部孔相关注解。</summary>
    private static List<Shape> ReadShapes(SolidWorksApi api, ScannedView scan, IReadOnlyList<HoleEdge> holes)
        => api.CallArray(scan.View, "IView", "GetAnnotations")
            .Where(annotation => annotation is not null)
            .Select(annotation => ReadShape(api, scan, holes, annotation))
            .OfType<Shape>()
            .ToList();

    /// <summary>一个注解；不是孔相关的返回 null。</summary>
    private static Shape? ReadShape(SolidWorksApi api, ScannedView scan, IReadOnlyList<HoleEdge> holes, object annotation)
    {
        bool OnHole(SheetPoint center) => holes.Any(hole => HoleCalloutPlanner.SameCenter(center, new SheetPoint(hole.X, hole.Y)));

        var type = api.CallInt(annotation, "IAnnotation", "GetType");
        switch (type)
        {
            case AnnotationDimension:
            {
                if (api.Call(annotation, "IAnnotation", "GetSpecificAnnotation") is not { } dimension)
                    return null;
                var centers = HoleCallout.AttachedCircleCenters(api, scan.Geometry, annotation).ToList();
                if (api.CallBool(dimension, "IDisplayDimension", "IsHoleCallout"))
                    return centers.Any(OnHole) ? Read(Role.Callout, centers) : null;
                if (!HolePositionPlanner.IsLinear(api.CallInt(dimension, "IDisplayDimension", "get_Type2")))
                    return null;
                var related = centers.Any(OnHole)
                    || HolePosition.AttachedLines(api, scan, annotation).Any(line => holes.Any(hole => HolePositionPlanner.PassesThrough(line, hole)));
                return related ? Read(Role.Dimension, centers) : null;
            }

            case AnnotationCenterMark:
            {
                var centers = HoleCallout.AttachedCircleCenters(api, scan.Geometry, annotation).ToList();
                return centers.Any(OnHole) ? Read(Role.Other, centers) : null;
            }

            case AnnotationDowel:
            {
                if (api.Call(annotation, "IAnnotation", "GetSpecificAnnotation") is not { } symbol)
                    return null;
                var points = api.CallDoubles(symbol, "IDowelSymbol", "GetArcPoints");
                if (DowelPlanner.ArcCenter(points) is not { } center || !OnHole(center))
                    return null;
                var radius = Math.Sqrt(Math.Pow(points[0] - center.X, 2) + Math.Pow(points[1] - center.Y, 2));
                return new Shape(annotation, Role.Other, ClearancePlanner.Octagon(center, radius).ToList(), [], [center]);
            }

            case AnnotationCenterLine:
            {
                var shape = Read(Role.Other, []);
                return shape.Lines.Any(line => holes.Any(hole => HolePositionPlanner.PassesThrough(line, hole))) ? shape : null;
            }

            default:
                return null;
        }

        Shape Read(Role role, IReadOnlyList<SheetPoint> centers)
        {
            var (lines, texts) = DisplayGeometry(api, annotation);
            return new Shape(annotation, role, lines, texts, centers);
        }
    }

    /// <summary>注解显示数据里的直线与文字框（图纸坐标）。</summary>
    private static (List<SheetSegment> Lines, List<TextBox> Texts) DisplayGeometry(SolidWorksApi api, object annotation)
    {
        var lines = new List<SheetSegment>();
        var texts = new List<TextBox>();
        if (api.Call(annotation, "IAnnotation", "GetDisplayData") is not { } data)
            return (lines, texts);

        var lineCount = api.CallInt(data, "IDisplayData", "GetLineCount");
        for (var i = 0; i < lineCount; i++)
        {
            // [颜色, 线型, 线样式, 线宽, 起点 xyz, 终点 xyz]
            var line = api.CallDoubles(data, "IDisplayData", "GetLineAtIndex3", i);
            if (line.Length >= 10)
                lines.Add(new SheetSegment(line[4], line[5], line[7], line[8]));
        }

        var textCount = api.CallInt(data, "IDisplayData", "GetTextCount");
        for (var i = 0; i < textCount; i++)
        {
            var at = api.CallDoubles(data, "IDisplayData", "GetTextPositionAtIndex", i);
            var width = Convert.ToDouble(api.Call(data, "IDisplayData", "GetTextInBoxWidthAtIndex", i));
            var height = Convert.ToDouble(api.Call(data, "IDisplayData", "GetTextHeightAtIndex", i));
            var angle = Convert.ToDouble(api.Call(data, "IDisplayData", "GetTextAngleAtIndex", i));
            if (at.Length >= 2 && width > 0 && height > 0)
                texts.Add(new TextBox(at[0], at[1], width, height, angle));
        }

        return (lines, texts);
    }
}
