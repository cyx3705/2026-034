using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>图框里的一条技术要求注释（带「技术要求」字样）：注解、注释、名字、外框。</summary>
internal sealed record TechNoteOnSheet(object Annotation, object Note, string Name, SheetRect Rect);

/// <summary>
/// 把一条已插进图纸的技术要求注释放到标题栏上方或左侧（1.13.0，规划见 <see cref="TechNotePlanner"/>）：读当前图纸页的视图
/// （外框连同它的尺寸、注解）、图纸格式注解，「避障」开时挪视图让出地方，再放注释并按读回的外框校正一次。
/// 「技术要求」模板表格、「技术要求」按钮、「排版」共用。
/// </summary>
internal static class TechNotePlacement
{
    // swDrawingViewTypes_e
    private const int ProjectedView = 4;

    /// <summary>视图中心差在这以内算对齐（图纸 0.5 mm）。</summary>
    private const double AlignTolerance = 0.0005;

    /// <summary>当前图纸页图框里带「技术要求」的注释（图框外模板备用的不算）。</summary>
    public static List<TechNoteOnSheet> Notes(SolidWorksApi api, object drawing)
    {
        if (Space(api, drawing) is not { } space)
            return [];
        return DrawingSheet.Annotations(api, drawing)
            .Where(item => item.Note is not null && item.Text.Contains("技术要求", StringComparison.Ordinal) && item.Rect.Overlaps(space.Frame))
            .Select(item => new TechNoteOnSheet(item.Annotation, item.Note!, item.Name, item.Rect))
            .ToList();
    }

    /// <summary>图框里能放东西的地方：国标图框 + 图纸格式注解（技术要求本身与 <paramref name="exclude"/> 不算）。读不到图纸大小返回 null。</summary>
    public static SheetSpace? Space(SolidWorksApi api, object drawing, IReadOnlyCollection<string>? exclude = null)
    {
        if (api.Call(drawing, "IDrawingDoc", "GetCurrentSheet") is not { } sheet)
            return null;
        var properties = api.CallDoubles(sheet, "ISheet", "GetProperties2");
        var (width, height) = properties.Length > 6 && properties[5] > 0 && properties[6] > 0 ? (properties[5], properties[6]) : (0.420, 0.297);
        var space = SheetSpace.Standard(width, height);
        return exclude is null ? space : space.With(DrawingSheet.FormatKeepOuts(api, drawing, space, exclude));
    }

    /// <summary>
    /// 放注释：返回回执的一句（放在哪、挪了谁）与是否让开了。<paramref name="preferred"/> 是原来那一处（换一份时）。
    /// </summary>
    public static (string Where, bool Free) Place(QuickCommandContext context, object drawing, object note, object annotation, NoteSlot? preferred)
    {
        var api = context.Api;
        api.Call(drawing, "IModelDoc2", "EditRebuild3");
        var name = api.CallString(annotation, "IAnnotation", "GetName");
        var exclude = Notes(api, drawing).Select(item => item.Name).Append(name).ToHashSet(StringComparer.Ordinal);
        var space = Space(api, drawing, exclude) ?? throw new QuickCommandException("技术要求：读不到当前图纸页。");
        var (width, height) = DrawingSheet.NoteSize(api, note);
        var avoid = context.Options.Clearance;
        var views = avoid ? Views(api, drawing) : [];
        var plan = TechNotePlanner.Place(space, views.Select(item => item.Plan).ToList(), width, height, preferred, avoid);

        var moved = new List<string>();
        var stuck = new List<string>();
        foreach (var move in plan.Moves)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var view = views.First(item => item.Plan.Name == move.Name).View;
            var position = api.CallDoubles(view, "IView", "get_Position");
            if (position.Length < 2)
            {
                stuck.Add(move.Name);
                continue;
            }

            var target = new SheetPoint(position[0] + move.Dx, position[1] + move.Dy);
            DrawingSheet.SetPosition(api, view, target);
            var after = api.CallDoubles(view, "IView", "get_Position");
            if (after.Length < 2 || Math.Abs(after[0] - target.X) > AlignTolerance || Math.Abs(after[1] - target.Y) > AlignTolerance)
                stuck.Add(move.Name);
            else
                moved.Add($"「{move.Name}」{Direction(move)}");
        }

        SetTopLeft(api, annotation, note, plan.TopLeft);
        api.Call(drawing, "IModelDoc2", "EditRebuild3");
        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        api.Call(drawing, "IModelDoc2", "GraphicsRedraw2");

        var where = plan.Slot == NoteSlot.AboveTitle ? "标题栏上方" : "标题栏左侧";
        var text = !avoid ? $"放在{where}（避障关，没有躲视图）"
            : !plan.Free ? $"放在{where}，但{plan.Problem}，可按「排版」或换大图幅"
            : moved.Count > 0 ? $"放在{where}，为它挪了视图 {string.Join("、", moved)}"
            : $"放在{where}";
        if (stuck.Count > 0)
            text += $"；视图 {string.Join("、", stuck.Select(s => "「" + s + "」"))} 没挪动（位置锁定或对齐约束），可能还压着";
        return (text, plan.Free && stuck.Count == 0);
    }

    /// <summary>
    /// 注释左上角放到 <paramref name="topLeft"/>：<c>SetPosition2</c> 之后读回外框，差得多就照差值再挪一次
    /// （注释的定位点与外框左上角不一定重合，差多少随字体与段落格式）。
    /// </summary>
    private static void SetTopLeft(SolidWorksApi api, object annotation, object note, SheetPoint topLeft)
    {
        api.Call(annotation, "IAnnotation", "SetPosition2", topLeft.X, topLeft.Y, 0.0);
        var extent = api.CallDoubles(note, "INote", "GetExtent");
        var position = api.CallDoubles(annotation, "IAnnotation", "GetPosition");
        if (extent.Length < 6 || extent[3] <= extent[0] || position.Length < 2)
            return;
        var (dx, dy) = (topLeft.X - extent[0], topLeft.Y - extent[4]);
        if (Math.Abs(dx) > 0.0002 || Math.Abs(dy) > 0.0002)
            api.Call(annotation, "IAnnotation", "SetPosition2", position[0] + dx, position[1] + dy, 0.0);
    }

    /// <summary>当前图纸页的视图：外框连同它的每个注解的外框；与父视图对齐的投影视图记下父视图与能挪的方向。</summary>
    private static List<(object View, PlanView Plan)> Views(SolidWorksApi api, object drawing)
    {
        var items = HoleScan.SheetViews(api, drawing)
            .Select(view => (View: view, Name: DrawingSheet.Name(api, view), Outline: DrawingSheet.Outline(api, view)))
            .Where(item => item.Outline.Width > 0 && item.Outline.Height > 0)
            .ToList();
        var names = items.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var result = new List<(object, PlanView)>();
        foreach (var (view, name, outline) in items)
        {
            var rects = new List<SheetRect> { outline };
            foreach (var annotation in api.CallArray(view, "IView", "GetAnnotations"))
                if (annotation is not null && DrawingSheet.Footprint(api, annotation) is { } rect)
                    rects.Add(rect);

            string? parent = null;
            var axis = ViewAxis.Free;
            if (api.CallInt(view, "IView", "get_Type") == ProjectedView
                && api.Call(view, "IView", "GetBaseView") is { } baseView
                && DrawingSheet.Name(api, baseView) is var baseName && names.Contains(baseName))
            {
                var mine = outline.Center;
                var theirs = DrawingSheet.Outline(api, baseView).Center;
                if (Math.Abs(mine.Y - theirs.Y) < AlignTolerance)
                    (parent, axis) = (baseName, ViewAxis.Horizontal);
                else if (Math.Abs(mine.X - theirs.X) < AlignTolerance)
                    (parent, axis) = (baseName, ViewAxis.Vertical);
            }

            result.Add((view, new PlanView(name, parent, axis, rects)));
        }

        return result;
    }

    private static string Direction(ViewMove move)
    {
        var parts = new List<string>();
        if (move.Dy > 1e-9)
            parts.Add($"上移 {move.Dy * 1000:0.#} mm");
        if (move.Dx < -1e-9)
            parts.Add($"左移 {-move.Dx * 1000:0.#} mm");
        if (move.Dy < -1e-9)
            parts.Add($"下移 {-move.Dy * 1000:0.#} mm");
        if (move.Dx > 1e-9)
            parts.Add($"右移 {move.Dx * 1000:0.#} mm");
        return string.Join("、", parts);
    }
}
