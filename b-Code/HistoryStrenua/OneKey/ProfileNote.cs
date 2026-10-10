using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 「一键出框架」的最后一步（1.17.0，用户定，样图 WTJYQ-04-01 型材骨架）：轴测图上加一条带引线的注释「采用 xx 铝型材」。不单独成按钮（用户定：框架只在一键类里）。
/// </summary>
/// <remarks>
/// <para>规格（xx）：先看焊件结构构件（特征 <c>WeldMemberFeat</c>）的型材文件名（<c>IStructuralMemberFeatureData.WeldmentProfilePath</c>，
/// 写法见 <see cref="PartKindPlanner.ProfileName"/>）；没有焊件就按每个实体的包围盒猜截面（<see cref="PartKindPlanner.ProfileFromBodies"/>）；都没有只写「采用铝型材」。</para>
/// <para>放法（我定，照样图）：引线指到轴测图最右上角那条边上，文字放在轴测图外框右边 8 mm、比那一点高 10 mm；出了图框就放到轴测图左边。
/// 轴测图里已有带「铝型材」的注释先删掉再加（重按不叠）。不躲别的注解。</para>
/// </remarks>
internal static class ProfileNote
{
    // swAnnotationType_e
    private const int AnnotationNote = 6;

    /// <summary>文字离轴测图外框多远（图纸 8 mm）、比引线落点高多少（图纸 10 mm）。</summary>
    private const double Gap = 0.008;

    private const double Rise = 0.010;

    /// <summary>零件用的型材规格（如「2020」「2020欧标」）；读不出返回空串。</summary>
    internal static string Read(QuickCommandContext context, object model)
    {
        var api = context.Api;
        var names = new List<string>();
        var feature = Try(() => api.Call(model, "IModelDoc2", "FirstFeature"));
        for (var guard = 0; feature is not null && guard < 5000; guard++)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var current = feature;
            if (Try(() => api.CallString(current, "IFeature", "GetTypeName2")) == "WeldMemberFeat"
                && Try(() => api.Call(current, "IFeature", "GetDefinition")) is { } definition
                && Try(() => api.CallString(definition, "IStructuralMemberFeatureData", "get_WeldmentProfilePath")) is { Length: > 0 } path
                && PartKindPlanner.ProfileName(path) is { Length: > 0 } name
                && !names.Contains(name))
                names.Add(name);
            feature = Try(() => api.Call(current, "IFeature", "GetNextFeature"));
        }

        if (names.Count > 0)
            return string.Join("、", names);

        // swBodyType_e.swSolidBody = 0；GetBodyBox = [xmin, ymin, zmin, xmax, ymax, zmax]。
        var boxes = new List<(double, double, double)>();
        foreach (var body in Try(() => api.CallArray(model, "IPartDoc", "GetBodies2", 0, true)) ?? [])
        {
            if (body is null || Try(() => api.CallDoubles(body, "IBody2", "GetBodyBox")) is not { Length: >= 6 } box)
                continue;
            boxes.Add((box[3] - box[0], box[4] - box[1], box[5] - box[2]));
        }

        return PartKindPlanner.ProfileFromBodies(boxes);
    }

    /// <summary>在当前图纸页的轴测图上加「采用 xx 铝型材」。</summary>
    internal static QuickOutcome Insert(QuickCommandContext context, object drawing, string profile)
    {
        var api = context.Api;
        var text = PartKindPlanner.ProfileNote(profile);
        var iso = HoleScan.SheetViews(api, drawing)
            .FirstOrDefault(view => api.Call(view, "IView", "get_ReferencedDocument") is not null && HoleScan.Frame(context, view).Axonometric);
        if (iso is null)
            return QuickOutcome.Fail($"型材说明：当前图纸页上没有轴测图，「{text}」没有加。");

        var scan = HoleScan.Scan(context, "型材说明", withLines: true, view: iso, quiet: true);
        if (scan.Lines.Count == 0)
            return QuickOutcome.Fail($"型材说明：轴测图「{scan.ViewName}」里读不到直边，「{text}」没有加。");

        context.SetState("型材说明");
        var (removed, leftover) = AnnotationEraser.Erase(context, drawing, () => OldNotes(api, iso));
        if (leftover > 0)
            return QuickOutcome.Fail($"型材说明：轴测图里原来的型材说明有 {leftover} 条删不掉，没有重加。");

        // 引线落点：最右上角的端点所在那条边上、离端点 30% 处（点在边上才选得中这条边）。
        var (index, corner) = scan.Lines
            .SelectMany((line, i) => new[] { (i, new SheetPoint(line.X1, line.Y1)), (i, new SheetPoint(line.X2, line.Y2)) })
            .MaxBy(item => item.Item2.X + item.Item2.Y);
        var segment = scan.Lines[index];
        var other = DimensionGeometry.Same(segment.X1, corner.X) && DimensionGeometry.Same(segment.Y1, corner.Y)
            ? new SheetPoint(segment.X2, segment.Y2)
            : new SheetPoint(segment.X1, segment.Y1);
        var at = new SheetPoint(corner.X + (other.X - corner.X) * 0.3, corner.Y + (other.Y - corner.Y) * 0.3);

        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        var selected = api.Call(drawing, "IModelDoc2", "get_Extension") is { } extension
                       && api.CallBool(extension, "IModelDocExtension", "SelectByID2", string.Empty, "EDGE", at.X, at.Y, 0.0, false, 0, null, 0);
        if (!selected)
            selected = HolePosition.SelectEdge(api, scan, scan.LineEdges[index], false);
        var note = api.Call(drawing, "IModelDoc2", "InsertNote", text);
        if (note is null || api.Call(note, "INote", "GetAnnotation") is not { } annotation)
        {
            api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
            return QuickOutcome.Fail($"型材说明：SolidWorks 没有接受注释，「{text}」没有加。");
        }

        var outline = DrawingSheet.Outline(api, iso);
        var (width, _) = DrawingSheet.NoteSize(api, note);
        var position = new SheetPoint(outline.Right + Gap, at.Y + Rise);
        if (DrawingSheet.FrameRect(api, drawing) is { } frame && position.X + width > frame.Right)
            position = new SheetPoint(Math.Max(frame.Left, outline.Left - Gap - width), position.Y);
        if (DrawingSheet.FrameRect(api, drawing) is { } box && position.Y > box.Top)
            position = position with { Y = box.Top };
        api.Call(annotation, "IAnnotation", "SetPosition2", position.X, position.Y, 0.0);
        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        api.Call(drawing, "IModelDoc2", "GraphicsRedraw2");

        var leader = Try(() => api.CallInt(note, "INote", "GetLeaderCount")) > 0;
        return QuickOutcome.Ok($"型材说明：轴测图「{scan.ViewName}」加上「{text}」"
            + (removed > 0 ? $"（先删掉原来的 {removed} 条）" : string.Empty)
            + (profile.Length == 0 ? "，读不出型材规格，请手工补上" : string.Empty)
            + (!selected || !leader ? "，引线没有连上边，请手工拖一下" : string.Empty)
            + "。");
    }

    /// <summary>轴测图里已有的型材说明：带「铝型材」字样的注释。</summary>
    private static List<object> OldNotes(SolidWorksApi api, object view)
        => api.CallArray(view, "IView", "GetAnnotations")
            .Where(annotation => annotation is not null
                                 && api.CallInt(annotation, "IAnnotation", "GetType") == AnnotationNote
                                 && api.Call(annotation, "IAnnotation", "GetSpecificAnnotation") is { } specific
                                 && api.CallString(specific, "INote", "GetText").Contains("铝型材", StringComparison.Ordinal))
            .Select(annotation => annotation!)
            .ToList();

    private static T? Try<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return default;
        }
    }
}
