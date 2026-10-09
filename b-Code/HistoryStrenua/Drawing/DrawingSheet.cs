using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>图框里的技术要求注释：<c>INote</c>、它的 <c>IAnnotation</c>（挪位置用）与名字（排版时不把它当障碍）。</summary>
internal sealed record SheetNote(object Note, object Annotation, string Name);

/// <summary>
/// 出图类各步（「投影视图」「轴测图」「技术要求」「排版」，从「新建工程图」拆出）读到的当前图纸页：图纸大小、投影角、比例；
/// 主视图与它引用的零件几何；摆在主视图四边的投影视图、轴测图、其他视图；图框里的技术要求。
/// </summary>
/// <remarks>
/// <para>主视图：调用方指定的（选中的视图、「新建工程图」刚建的），否则取图纸页上第一个不是投影 / 辅助 / 剖视 / 局部视图、
/// 也不是轴测图的模型视图，再没有就取第一个不是轴测图的视图。</para>
/// <para>其余视图按朝向归位（<see cref="DrawingPlanner.SlotOf"/>）：同一个零件、视线沿主视图图纸 X / Y 的是左右 / 上下视图（每边一个），
/// 轴测图取第一个；别的（剖视、局部、后视、别的零件、从别的视图投影出来的、同一边的第二个）归「其他」，排版时原地不动、当障碍躲开。</para>
/// <para>目前只认零件图：主视图引用的是装配体时报错（装配体总图还没做）。</para>
/// </remarks>
internal sealed record DrawingSheet(
    object Drawing,
    object Sheet,
    double Width,
    double Height,
    bool FirstAngle,
    double Scale,
    object Main,
    string MainName,
    ViewFrame MainFrame,
    PartGeometry Part,
    IReadOnlyDictionary<ViewSlot, object> Sides,
    object? Iso,
    IReadOnlyList<object> Others,
    SheetNote? Note)
{
    // swDocumentTypes_e / swDrawingViewTypes_e
    private const int DocumentPart = 1;
    private const int SectionView = 2;
    private const int DetailView = 3;
    private const int ProjectedView = 4;
    private const int AuxiliaryView = 5;

    // swAnnotationType_e
    private const int AnnotationNote = 6;

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="drawing">工程图（<c>IModelDoc2</c>）。</param>
    /// <param name="title">指令名，写进进度与失败消息。</param>
    /// <param name="main">指定主视图（选中的视图、刚建的主视图）；null 时自动认。</param>
    /// <param name="part">主视图零件的几何已经读过就给（「一键出图」逐步调用时不重复读零件）。</param>
    public static DrawingSheet Read(QuickCommandContext context, object drawing, string title, object? main = null, PartGeometry? part = null)
    {
        var api = context.Api;
        var sheet = api.Call(drawing, "IDrawingDoc", "GetCurrentSheet")
            ?? throw new QuickCommandException("工程图没有图纸页。");
        var properties = api.CallDoubles(sheet, "ISheet", "GetProperties2");
        var firstAngle = properties.Length <= 4 || properties[4] != 0;
        var (width, height) = properties.Length > 6 ? (properties[5], properties[6]) : (0.420, 0.297);
        var scale = properties.Length > 3 && properties[2] > 0 && properties[3] > 0 ? properties[2] / properties[3] : 1;

        var views = HoleScan.SheetViews(api, drawing)
            .Where(view => api.Call(view, "IView", "get_ReferencedDocument") is not null)
            .Select(view => (View: view, Name: api.CallString(view, "IView", "get_Name"), Frame: HoleScan.Frame(context, view)))
            .ToList();
        var orthographic = views.Where(item => !item.Frame.Axonometric).Select(item => item.View).ToList();
        main ??= orthographic.FirstOrDefault(view => api.CallInt(view, "IView", "get_Type") is not (ProjectedView or AuxiliaryView or SectionView or DetailView))
                 ?? orthographic.FirstOrDefault()
                 ?? throw new QuickCommandException($"{title}：当前图纸页上没有引用零件的正交视图。先用「新建工程图」建图，或手工放一个模型视图。");

        var mainName = api.CallString(main, "IView", "get_Name");
        var model = api.Call(main, "IView", "get_ReferencedDocument")
            ?? throw new QuickCommandException($"视图「{mainName}」没有引用模型（空视图，或模型是轻化 / 未加载状态）。");
        if (api.CallInt(model, "IModelDoc2", "GetType") != DocumentPart)
            throw new QuickCommandException($"视图「{mainName}」引用的是装配体。基础类目前只给零件图出图（装配体总图还没做）。");
        if (part is null)
        {
            context.SetState("读零件");
            context.Report($"{title}：读零件「{api.CallString(model, "IModelDoc2", "GetTitle")}」的孔与圆弧面。");
            part = PartScan.Read(context, model);
        }

        var mainFrame = HoleScan.Frame(context, main);
        var mainKey = HoleScan.ModelKey(api, main);
        var sides = new Dictionary<ViewSlot, object>();
        object? iso = null;
        var others = new List<object>();
        foreach (var (view, name, frame) in views)
        {
            if (name == mainName)
                continue;
            if (HoleScan.ModelKey(api, view) != mainKey)
                others.Add(view);
            else if (frame.Axonometric)
            {
                if (iso is null)
                    iso = view;
                else
                    others.Add(view);
            }
            else if (api.Call(view, "IView", "GetBaseView") is { } parent && api.CallString(parent, "IView", "get_Name") != mainName)
            {
                // 从别的视图投影出来的（如左视图下面再投一个）跟着它的父视图对齐，排版挪不到主视图四边去（真机：误当成主视图的下视图，比例被降了一档）。
                others.Add(view);
            }
            else if (DrawingPlanner.SlotOf(mainFrame, frame.Normal, firstAngle) is { } slot && !sides.ContainsKey(slot))
                sides[slot] = view;
            else
                others.Add(view);
        }

        var inside = SheetSpace.Standard(width, height).Frame;
        var note = Annotations(api, drawing)
            .Where(item => item.Note is not null && item.Text.Contains("技术要求", StringComparison.Ordinal) && item.Rect.Overlaps(inside))
            .Select(item => new SheetNote(item.Note!, item.Annotation, item.Name))
            .FirstOrDefault();
        return new DrawingSheet(drawing, sheet, width, height, firstAngle, scale, main, mainName, mainFrame, part, sides, iso, others, note);
    }

    /// <summary>
    /// 实际图纸能放东西的地方：国标估计（<see cref="SheetSpace.Standard"/>）再加上图框里读到的每个注解（标题栏各格、修改栏、
    /// 「其余」粗糙度、公司名……），不同模板也躲得开；图框外的（模板备用的技术要求、分区字母）不算；技术要求本身与
    /// <paramref name="exclude"/> 不算。「其他」视图连同标注空间也算禁区（排版不动它们）。
    /// </summary>
    public SheetSpace Space(QuickCommandContext context, string? exclude = null)
    {
        var space = SheetSpace.Standard(Width, Height);
        var excluded = new List<string>();
        if (Note is not null)
            excluded.Add(Note.Name);
        if (exclude is not null)
            excluded.Add(exclude);
        var found = FormatKeepOuts(context.Api, Drawing, space, excluded);
        var views = Others.SelectMany(view => DrawingPlanner.Occupied(Outline(context.Api, view).Center, Box(context, view)));
        return space.With(found.Concat(views));
    }

    /// <summary>
    /// 图框里的图纸注解（标题栏各格、修改栏、「其余」粗糙度、公司名……）各占的地方，往外放 1 mm；中心在图框外的（模板备用的技术要求、
    /// 分区字母）不算，<paramref name="exclude"/> 里的名字不算。
    /// </summary>
    public static List<SheetRect> FormatKeepOuts(SolidWorksApi api, object drawing, SheetSpace space, IReadOnlyCollection<string> exclude)
    {
        var inner = space.Frame.Inflate(-0.001);
        return Annotations(api, drawing)
            .Where(item => !exclude.Contains(item.Name) && inner.Overlaps(item.Rect)
                           && item.Rect.Center.X > inner.Left && item.Rect.Center.X < inner.Right
                           && item.Rect.Center.Y > inner.Bottom && item.Rect.Center.Y < inner.Top)
            .Select(item => item.Rect.Inflate(0.001))
            .ToList();
    }

    /// <summary>主视图、投影视图现在连同标注空间占的地方，加上轴测图的外框：新加东西找空地时躲开它们（「其他」视图在 <see cref="Space"/> 里）。</summary>
    public List<SheetRect> Occupied(QuickCommandContext context)
    {
        var api = context.Api;
        var result = new List<SheetRect>();
        result.AddRange(DrawingPlanner.Occupied(Outline(api, Main).Center, Box(context, Main)));
        foreach (var (slot, view) in Sides)
            result.AddRange(DrawingPlanner.Occupied(Outline(api, view).Center, Box(context, view, slot: slot)));
        if (Iso is not null)
            result.Add(Outline(api, Iso));
        return result;
    }

    /// <summary>
    /// 视图在图纸上占多大：实际外框，加左边、上边的标注空间（按这个视图里能看到的孔的种数估）；给了 <paramref name="slot"/> 的投影视图
    /// 在与主视图共用的方向上没有孔就不留（<see cref="DrawingPlanner.SideBox"/>）。
    /// </summary>
    public ViewBox Box(QuickCommandContext context, object view, bool annotations = true, ViewSlot? slot = null)
    {
        var outline = Outline(context.Api, view);
        if (!annotations)
            return new ViewBox(outline.Width, outline.Height, 0, 0);
        var kinds = DrawingPlanner.HoleKinds(Part, HoleScan.Frame(context, view).Normal);
        if (slot is { } side)
            return DrawingPlanner.SideBox(outline.Width, outline.Height, side, kinds, context.Options.Chain);
        var margin = DrawingPlanner.AnnotationMargin(kinds, context.Options.Chain);
        return new ViewBox(outline.Width, outline.Height, margin, margin);
    }

    /// <summary>
    /// 当前图纸页的图框线与标题栏、修改栏、图号框的边（按国标估计，<see cref="SheetSpace.Standard"/>），圆角、倒角文字找空处时也躲它们
    /// （1.10.0：限位块前 A4 上的「2 x C5」贴在了图框底线上）。读不到图纸大小返回空。
    /// </summary>
    public static IReadOnlyList<SheetSegment> FrameLines(SolidWorksApi api, object drawing)
        => StandardSpace(api, drawing) is { } space ? space.KeepOuts.Prepend(space.Frame).SelectMany(Edges).ToList() : [];

    /// <summary>
    /// 圆角、倒角文字要落在哪里面：图框（国标估计）往里缩 3 mm——文字下面还有引线的横线，估的文字框贴着图框时真机画出来压在图框线上
    /// （限位块前的「2 x C5」）。读不到图纸大小返回 null。
    /// </summary>
    public static SheetRect? FrameRect(SolidWorksApi api, object drawing) => StandardSpace(api, drawing)?.Frame.Inflate(-TextFrameMargin);

    /// <summary>
    /// 图框里整块不许压的地方（1.15.0，倒角避障用）：标题栏、修改栏、图号框（国标估计），以及图纸上的注解（标题栏各格、技术要求、「其余」粗糙度……）。
    /// 只躲边线不够：真机移动底板「3 x C2」整个落进修改栏的格子里，没碰到任何一条边。读不到图纸大小返回空。
    /// </summary>
    public static IReadOnlyList<SheetRect> KeepOutRects(SolidWorksApi api, object drawing)
        => StandardSpace(api, drawing) is { } space ? space.KeepOuts.Concat(FormatKeepOuts(api, drawing, space, [])).ToList() : [];

    /// <summary>圆角、倒角文字离图框至少这么远（图纸 3 mm）。</summary>
    public const double TextFrameMargin = 0.003;

    private static SheetSpace? StandardSpace(SolidWorksApi api, object drawing)
    {
        if (api.Call(drawing, "IDrawingDoc", "GetCurrentSheet") is not { } sheet)
            return null;
        var properties = api.CallDoubles(sheet, "ISheet", "GetProperties2");
        return properties.Length > 6 && properties[5] > 0 && properties[6] > 0 ? SheetSpace.Standard(properties[5], properties[6]) : null;
    }

    private static IEnumerable<SheetSegment> Edges(SheetRect rect)
    {
        yield return new SheetSegment(rect.Left, rect.Bottom, rect.Right, rect.Bottom);
        yield return new SheetSegment(rect.Right, rect.Bottom, rect.Right, rect.Top);
        yield return new SheetSegment(rect.Right, rect.Top, rect.Left, rect.Top);
        yield return new SheetSegment(rect.Left, rect.Top, rect.Left, rect.Bottom);
    }

    /// <summary>视图名，回执用。</summary>
    public static string Name(SolidWorksApi api, object view) => api.CallString(view, "IView", "get_Name");

    /// <summary>视图外框（图纸坐标，米）。</summary>
    public static SheetRect Outline(SolidWorksApi api, object view)
    {
        var o = api.CallDoubles(view, "IView", "GetOutline");
        return o.Length >= 4 ? new SheetRect(o[0], o[1], o[2], o[3]) : default;
    }

    /// <summary>挪视图：<c>IView.Position</c> 就是外框中心（真机核对过）。与父视图对齐的投影视图只沿对齐方向动。</summary>
    public static void SetPosition(SolidWorksApi api, object view, SheetPoint center)
        => api.Call(view, "IView", "set_Position", new double[] { center.X, center.Y });

    /// <summary>选中父视图，在 (x, y) 处投影一个视图（斜着放就是轴测图）。</summary>
    public static object? Unfold(SolidWorksApi api, object drawing, string parentName, double x, double y)
    {
        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        var extension = api.Call(drawing, "IModelDoc2", "get_Extension");
        if (extension is null || !api.CallBool(extension, "IModelDocExtension", "SelectByID2", parentName, "DRAWINGVIEW", 0.0, 0.0, 0.0, false, 0, null, 0))
            return null;
        var view = api.Call(drawing, "IDrawingDoc", "CreateUnfoldedViewAt3", x, y, 0.0, false);
        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        return view;
    }

    /// <summary>整页比例（视图默认跟图纸比例走，标题栏的「图纸比例」也读它）。</summary>
    public static void SetSheetScale(SolidWorksApi api, object sheet, double scale)
    {
        var (numerator, denominator) = DrawingPlanner.ScaleRatio(scale);
        api.Call(sheet, "ISheet", "SetScale", (double)numerator, (double)denominator, true, false);
    }

    /// <summary>图纸页现在的比例（读回的，回执用）。</summary>
    public static double SheetScale(SolidWorksApi api, object sheet, double fallback)
    {
        var properties = api.CallDoubles(sheet, "ISheet", "GetProperties2");
        return properties.Length > 3 && properties[2] > 0 && properties[3] > 0 ? properties[2] / properties[3] : fallback;
    }

    /// <summary>
    /// 轴测图比例：<paramref name="shrink"/> 为 1 时跟图纸比例走；小于 1 时用不大于「图纸比例 × shrink」的那档标准比例。
    /// 返回实际缩成图纸比例的几分之几。
    /// </summary>
    public static double ScaleIso(SolidWorksApi api, object iso, double sheetScale, double shrink, IReadOnlyList<double> scales)
    {
        if (shrink >= 1 - 1e-9)
        {
            api.Call(iso, "IView", "set_UseSheetScale", 1);
            return 1;
        }

        var target = sheetScale * shrink;
        var isoScale = scales.FirstOrDefault(s => s <= target + 1e-12, target);
        var (numerator, denominator) = DrawingPlanner.ScaleRatio(isoScale);
        api.Call(iso, "IView", "set_UseSheetScale", 0);
        api.Call(iso, "IView", "set_ScaleRatio", new double[] { numerator, denominator });
        return isoScale / sheetScale;
    }

    /// <summary>注释的宽、高（<c>INote.GetExtent</c>）；读不到用用户标准 8 条的估计大小。</summary>
    public static (double Width, double Height) NoteSize(SolidWorksApi api, object? note)
    {
        if (note is null)
            return (0, 0);
        var extent = api.CallDoubles(note, "INote", "GetExtent");
        return extent.Length >= 6 && extent[3] > extent[0]
            ? (extent[3] - extent[0], extent[4] - extent[1])
            : (DrawingPlanner.DefaultNoteWidth, DrawingPlanner.DefaultNoteHeight);
    }

    /// <summary>投影视图在主视图哪一边，回执用。</summary>
    public static string SlotName(ViewSlot slot) => slot switch
    {
        ViewSlot.Right => "右边",
        ViewSlot.Left => "左边",
        ViewSlot.Below => "下边",
        _ => "上边",
    };

    /// <summary>
    /// 图纸（含图纸格式）上的注解：名字、占的地方、注释文字、<c>IAnnotation</c>，是注释的再带 <c>INote</c>。
    /// 注释取它的外框；别的取显示数据里线与文字的外框（1.10.0：「其余 6.3」粗糙度的定位点在符号上，「其余」两个字在左边 10 mm，
    /// 按定位点前后 6 mm 估时技术要求贴着「其余」放），读不到再取位置前后 6 mm。
    /// </summary>
    public static List<(string Name, SheetRect Rect, string Text, object Annotation, object? Note)> Annotations(SolidWorksApi api, object drawing)
    {
        var result = new List<(string, SheetRect, string, object, object?)>();
        if (api.Call(drawing, "IDrawingDoc", "GetFirstView") is not { } sheetView)
            return result;
        foreach (var annotation in api.CallArray(sheetView, "IView", "GetAnnotations"))
        {
            if (annotation is null)
                continue;
            var name = api.CallString(annotation, "IAnnotation", "GetName");
            var position = api.CallDoubles(annotation, "IAnnotation", "GetPosition");
            var text = string.Empty;
            object? note = null;
            SheetRect? rect = null;
            if (api.CallInt(annotation, "IAnnotation", "GetType") == AnnotationNote
                && api.Call(annotation, "IAnnotation", "GetSpecificAnnotation") is { } specific)
            {
                note = specific;
                text = api.CallString(specific, "INote", "GetText");
                var extent = api.CallDoubles(specific, "INote", "GetExtent");
                if (extent.Length >= 6 && extent[3] > extent[0])
                    rect = new SheetRect(extent[0], extent[1], extent[3], extent[4]);
            }

            rect ??= Footprint(api, annotation);
            if (rect is null && position.Length >= 2)
                rect = new SheetRect(position[0] - 0.006, position[1] - 0.006, position[0] + 0.006, position[1] + 0.006);
            if (rect is { } r)
                result.Add((name, r, text, annotation, note));
        }

        return result;
    }

    /// <summary>注解显示数据里线与文字框的外框；什么都没有（或读不到）返回 null。</summary>
    internal static SheetRect? Footprint(SolidWorksApi api, object annotation)
    {
        List<SheetPoint> points;
        try
        {
            var (lines, texts) = Clearance.DisplayGeometry(api, annotation);
            points = lines.SelectMany(line => new[] { new SheetPoint(line.X1, line.Y1), new SheetPoint(line.X2, line.Y2) })
                .Concat(texts.SelectMany(text => text.Corners()))
                .ToList();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or System.Reflection.TargetException)
        {
            return null;
        }

        return points.Count == 0
            ? null
            : new SheetRect(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
    }
}
