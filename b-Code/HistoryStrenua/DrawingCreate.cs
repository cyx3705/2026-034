using System.Diagnostics;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「新建工程图」（1.9.0，出图类）：给当前零件（或装配体里选中的零件）按用户的工程图模板建一张图——
/// 选图幅与比例、主视图、看侧面孔与厚度的投影视图、轴测图，技术要求放进图纸，按实际外框排开不重叠。不保存。
/// </summary>
/// <remarks>
/// <para>依据是用户 2026-025 台面2机器 15 张手工图的共同做法（DEC-021）：标题栏由模板的属性链接带出（图号、名称、材料、表面处理、
/// 热处理、设计、日期、比例），「其余 6.3」与公司名在模板里，所以这里只管视图与技术要求。</para>
/// <para>挑视图、选模板与比例、排版都在 <see cref="DrawingPlanner"/>（离线可测）；这里只做 SolidWorks 调用：建图、建视图、
/// 量实际外框、插注释、落位。估出来放不下时按实际大小重排，还放不下就把整页比例降一档再排。</para>
/// <para>不保存（PowerSW 不替用户存文件）；SolidWorks 第一次保存时默认文件名就是零件名。</para>
/// </remarks>
internal static class DrawingCreate
{
    public static QuickCommand Command { get; } = new(
        Key: "drawing-create",
        CommandName: StrenuaIdentity.Domain + ".drawing.create",
        Title: "新建工程图",
        Summary: "给当前零件（或装配体里选中的零件）按工程图模板建图：自动选图幅比例、主视图、投影视图与轴测图，放技术要求，不保存。",
        Usage: "在 SolidWorks 里打开要出图的零件（或在装配体里选中一个零件，先选后按、先按后选都行，60 秒内）再按：按「零件」工程图模板建一张新图——图幅（A4 横 / A3 / A2）与比例按零件大小和孔的疏密自动选，主视图取孔和圆弧最多的那一面，侧面有孔的方向加投影视图、没有就加一个看厚度的，再加一个轴测图；技术要求放进图纸（内容取模块数据目录的「技术要求.txt」，没有就用默认那 8 条），标题栏由模板的属性链接自动带出。视图按实际大小排开不重叠，给左边、上边的尺寸留出地方。新图不保存。",
        Run: context => Create(context).Outcome);

    /// <summary>建图的结论，另带新图（<c>IModelDoc2</c>）给「一键出图」接着用；失败时为 null。</summary>
    internal sealed record Created(QuickOutcome Outcome, object? Drawing);

    // swDocumentTypes_e
    private const int DocumentPart = 1;
    private const int DocumentAssembly = 2;

    // swRebuildOnActivation_e.swDontRebuildActiveDoc
    private const int DontRebuild = 1;

    public static Created Create(QuickCommandContext context)
    {
        var api = context.Api;
        var application = context.Session.Application;
        var stopwatch = Stopwatch.StartNew();

        var (part, partPath, partTitle) = ResolvePart(context);
        context.SetState("读零件");
        context.Report($"新建工程图：读零件「{partTitle}」的孔与圆弧面。");
        var geometry = PartScan.Read(context, part);
        var main = DrawingPlanner.MainView(geometry);
        var planned = new ViewFrame(main.View.Right, main.View.Up, main.View.Normal, new SheetPoint(0, 0), 1);
        var plannedSides = DrawingPlanner.SideViews(geometry, planned, firstAngle: true);

        var (templates, fallback) = DrawingTemplates.Find(api, application);
        var choice = templates.Count > 0 ? DrawingPlanner.ChooseSheet(geometry, main.View, plannedSides, templates, context.Options.Chain) : null;
        string templatePath, templateNote;
        if (choice is not null)
            (templatePath, templateNote) = (choice.Template.Path, $"，比例 {DrawingPlanner.ScaleText(choice.Scale)}");
        else if (templates.Count > 0)
            (templatePath, templateNote) = (templates[^1].Path, "（估计哪个图幅都放不下，用最大的，比例建出来再定）");
        else if (fallback is not null)
            (templatePath, templateNote) = (fallback, "（模板目录里没有认得出图幅的模板，用默认工程图模板，比例建出来再定）");
        else
            throw new QuickCommandException("找不到工程图模板：SolidWorks「选项 → 文件位置 → 文件模板」里的目录没有 *.drwdot，也没有设默认工程图模板。");

        context.SetState("新建工程图");
        context.Report($"新建工程图：用模板「{Path.GetFileNameWithoutExtension(templatePath)}」{templateNote}。");
        var drawing = api.Call(application, "ISldWorks", "NewDocument", templatePath, 0, 0.0, 0.0)
            ?? throw new QuickCommandException($"SolidWorks 没能用模板新建工程图：{templatePath}");
        Activate(api, application, drawing);
        var sheet = api.Call(drawing, "IDrawingDoc", "GetCurrentSheet")
            ?? throw new QuickCommandException("新工程图没有图纸页。");
        var properties = api.CallDoubles(sheet, "ISheet", "GetProperties2");
        var firstAngle = properties.Length <= 4 || properties[4] != 0;
        var (width, height) = properties.Length > 6 ? (properties[5], properties[6]) : (choice?.Template.Width ?? 0.42, choice?.Template.Height ?? 0.297);

        var scales = DrawingPlanner.Scales(geometry);
        var scale = choice?.Scale ?? scales.FirstOrDefault(s =>
            DrawingPlanner.Layout(SheetSpace.Standard(width, height), DrawingPlanner.Estimate(geometry, main.View, plannedSides.Select(side => side.Slot).ToList(), s, context.Options.Chain)).Fits,
            scales[^1]);
        SetSheetScale(api, sheet, scale);

        context.SetState("建视图");
        context.Report($"新建工程图：主视图「{main.View.Names[0]}」（{main.Reason}）。");
        var mainView = main.View.Names
            .Select(name => api.Call(drawing, "IDrawingDoc", "CreateDrawViewFromModelView3", partPath, name, width * 0.3, height * 0.6, 0.0))
            .FirstOrDefault(view => view is not null)
            ?? throw new QuickCommandException($"SolidWorks 没能建主视图（{string.Join(" / ", main.View.Names)}）。");
        // SolidWorks「自动缩放新视图」开着时，插第一个视图会把图纸比例改成它自己挑的（真机：连接件 A2 上被改成 1:1），这里设回来。
        SetSheetScale(api, sheet, scale);
        api.Call(drawing, "IModelDoc2", "EditRebuild3");
        var mainName = api.CallString(mainView, "IView", "get_Name");
        var frame = HoleScan.Frame(context, mainView);
        // 真机朝向下再定一遍投影视图；「可换边」的照选图幅时排得下的那种摆法。
        var sides = DrawingPlanner.SideViews(geometry, frame, firstAngle)
            .Select((side, index) => choice is not null && side.Alternative is { } other && choice.Slots.Count > index && choice.Slots[index] == other
                ? side with { Slot = other }
                : side)
            .ToList();
        var sideViews = new Dictionary<ViewSlot, object>();
        foreach (var (slot, reason, _) in sides)
        {
            var at = Outline(api, mainView);
            var (x, y) = slot switch
            {
                ViewSlot.Right => (at.Right + 0.05, at.Center.Y),
                ViewSlot.Left => (at.Left - 0.05, at.Center.Y),
                ViewSlot.Below => (at.Center.X, at.Bottom - 0.04),
                _ => (at.Center.X, at.Top + 0.04),
            };
            if (Unfold(api, drawing, mainName, x, y) is { } view)
            {
                sideViews[slot] = view;
                context.Report($"新建工程图：{SlotName(slot)}加投影视图「{api.CallString(view, "IView", "get_Name")}」（{reason}）。");
            }
        }

        var outline = Outline(api, mainView);
        var iso = Unfold(api, drawing, mainName, outline.Right + 0.08, outline.Top + 0.04);
        if (iso is not null && !HoleScan.Frame(context, iso).Axonometric)
            context.Report("新建工程图：斜着投影出来的不是轴测图，照样留着。");

        var (note, noteText) = InsertTechnicalNote(context, drawing, width, height);
        context.SetState("排版");
        var placed = Arrange(context, drawing, sheet, geometry, mainView, sideViews, iso, note, width, height, ref scale);
        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        api.Call(drawing, "IModelDoc2", "ViewZoomtofit2");

        var actual = api.CallDoubles(sheet, "ISheet", "GetProperties2");
        var scaleText = actual.Length > 3 && actual[3] > 0 ? DrawingPlanner.ScaleText(actual[2] / actual[3]) : DrawingPlanner.ScaleText(scale);
        var lines = new List<string>
        {
            $"新建工程图：零件「{partTitle}」→ 新图「{api.CallString(drawing, "IModelDoc2", "GetTitle")}」（未保存），" +
            $"模板「{Path.GetFileNameWithoutExtension(templatePath)}」{(choice is null ? string.Empty : "（" + choice.Template.SizeName + "）")}，比例 {scaleText}，用时 {stopwatch.Elapsed.TotalSeconds:0.0} 秒。",
            $"· 主视图「{mainName}」{main.View.Names[0]}：{main.Reason}。",
        };
        lines.AddRange(sides.Where(item => sideViews.ContainsKey(item.Slot))
            .Select(item => $"· {SlotName(item.Slot)}投影视图「{api.CallString(sideViews[item.Slot], "IView", "get_Name")}」：{item.Reason}。"));
        if (iso is not null)
            lines.Add($"· 轴测图「{api.CallString(iso, "IView", "get_Name")}」" + (placed.IsoShrink < 1 ? $"，比例缩成 {DrawingPlanner.ScaleText(scale * placed.IsoShrink)}" : string.Empty) + "。");
        if (choice is not null)
            lines.Add($"· 图幅与比例：{choice.Reason}。");
        lines.Add(noteText);
        if (!placed.Fits)
            lines.Add($"· 排版：{placed.Problem}，已尽量摆开，请手工挪一下。");
        else if (placed.Problem.Length > 0)
            lines.Add($"· 排版：{placed.Problem}，请手工挪一下。");
        var missing = sides.Count(item => !sideViews.ContainsKey(item.Slot)) + (iso is null ? 1 : 0);
        if (missing > 0)
            lines.Add($"· SolidWorks 没有建出 {missing} 个视图。");
        return new Created(QuickOutcome.Ok(string.Join(Environment.NewLine, lines)), drawing);
    }

    private static string SlotName(ViewSlot slot) => slot switch
    {
        ViewSlot.Right => "右边",
        ViewSlot.Left => "左边",
        ViewSlot.Below => "下边",
        _ => "上边",
    };

    /// <summary>
    /// 要出图的零件：活动文档是零件就是它；是装配体就用选中的零件（先选后按、先按后选都行，最多等 <see cref="HoleScan.SelectionTimeout"/>）。
    /// 零件必须保存过（工程图按文件引用零件）。
    /// </summary>
    private static (object Part, string Path, string Title) ResolvePart(QuickCommandContext context)
    {
        var api = context.Api;
        var document = context.Session.ActiveDocument()
            ?? throw new QuickCommandException("SolidWorks 里没有打开的文档。请先打开要出图的零件。");
        var part = api.CallInt(document, "IModelDoc2", "GetType") switch
        {
            DocumentPart => document,
            DocumentAssembly => WaitForComponentPart(context, document),
            _ => throw new QuickCommandException($"当前活动文档「{api.CallString(document, "IModelDoc2", "GetTitle")}」是工程图。请切到要出图的零件（或在装配体里选中一个零件）再按。"),
        };
        var path = api.CallString(part, "IModelDoc2", "GetPathName");
        var title = api.CallString(part, "IModelDoc2", "GetTitle");
        if (string.IsNullOrWhiteSpace(path))
            throw new QuickCommandException($"零件「{title}」还没有保存过。工程图要按文件引用零件，先保存再出图。");
        return (part, Path.GetFullPath(path), title);
    }

    /// <summary>装配体里选中的零件（选中零件上的面、边也算）；没选就等用户去点。</summary>
    private static object WaitForComponentPart(QuickCommandContext context, object assembly)
    {
        var api = context.Api;
        var selection = api.Call(assembly, "IModelDoc2", "get_SelectionManager")
            ?? throw new QuickCommandException("SolidWorks 没有返回选择管理器。");
        object? Selected()
        {
            var count = api.CallInt(selection, "ISelectionMgr", "GetSelectedObjectCount2", -1);
            for (var index = 1; index <= count; index++)
            {
                if (api.Call(selection, "ISelectionMgr", "GetSelectedObjectsComponent4", index, -1) is not { } component)
                    continue;
                var model = api.Call(component, "IComponent2", "GetModelDoc2");
                if (model is null)
                    throw new QuickCommandException($"选中的零件「{api.CallString(component, "IComponent2", "get_Name2")}」是压缩或轻化状态，先还原再出图。");
                if (api.CallInt(model, "IModelDoc2", "GetType") == DocumentPart)
                    return model;
                throw new QuickCommandException($"选中的「{api.CallString(component, "IComponent2", "get_Name2")}」是子装配体。新建工程图只给零件出图，请选子装配体里的零件。");
            }

            return null;
        }

        if (Selected() is { } preselected)
            return preselected;
        context.SetState("等待点选零件");
        context.Report($"新建工程图：请在装配体里点一下要出图的零件（{HoleScan.SelectionTimeout.TotalSeconds:0} 秒内）。");
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < HoleScan.SelectionTimeout)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            Thread.Sleep(250);
            if (Selected() is { } picked)
                return picked;
        }

        throw new QuickCommandException($"{HoleScan.SelectionTimeout.TotalSeconds:0} 秒内没有在装配体里选零件，新建工程图已放弃。");
    }

    /// <summary>把新图切成活动窗口（NewDocument 之后活动窗口有时还停在零件上，后面按活动文档干活的步骤会做错图）。</summary>
    private static void Activate(SolidWorksApi api, object application, object document)
    {
        var errors = 0;
        api.Call(application, "ISldWorks", "ActivateDoc3", api.CallString(document, "IModelDoc2", "GetTitle"), false, DontRebuild, errors);
    }

    /// <summary>整页比例（视图默认跟图纸比例走，标题栏的「图纸比例」也读它）。</summary>
    private static void SetSheetScale(SolidWorksApi api, object sheet, double scale)
    {
        var (numerator, denominator) = DrawingPlanner.ScaleRatio(scale);
        api.Call(sheet, "ISheet", "SetScale", (double)numerator, (double)denominator, true, false);
    }

    /// <summary>视图外框（图纸坐标，米）。</summary>
    private static SheetRect Outline(SolidWorksApi api, object view)
    {
        var o = api.CallDoubles(view, "IView", "GetOutline");
        return o.Length >= 4 ? new SheetRect(o[0], o[1], o[2], o[3]) : default;
    }

    /// <summary>选中父视图，在 (x, y) 处投影一个视图（斜着放就是轴测图）。</summary>
    private static object? Unfold(SolidWorksApi api, object drawing, string parentName, double x, double y)
    {
        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        var extension = api.Call(drawing, "IModelDoc2", "get_Extension");
        if (extension is null || !api.CallBool(extension, "IModelDocExtension", "SelectByID2", parentName, "DRAWINGVIEW", 0.0, 0.0, 0.0, false, 0, null, 0))
            return null;
        var view = api.Call(drawing, "IDrawingDoc", "CreateUnfoldedViewAt3", x, y, 0.0, false);
        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        return view;
    }

    /// <summary>技术要求的文件名（模块数据目录里，用户可改，UTF-8）。</summary>
    public const string TechnicalNoteFile = "技术要求.txt";

    /// <summary>用户的标准技术要求（2026-025 台面2机器手工图原文，含 SolidWorks 段落格式标记，字体字高随模板）。</summary>
    public const string DefaultTechnicalNote =
        "        技术要求\n"
        + "<PARA  indent=0 findent=0 indentStep=10 number=off bullet=off paraSpace=0.001 lineSpace=0.001>1、未注公差参照附表执行；\n"
        + "2、未注沉头孔与螺纹孔位置公差为±0.2mm，定位销孔位置公差±0.02mm;\n"
        + "3、图形尺寸为表面处理后尺寸；\n"
        + "4、锐边倒钝，去毛刺，飞边;\n"
        + "5、未注倒角C1，未注圆角R1，螺纹孔及沉孔顶部倒角C0.5~C1;\n"
        + "6、零件表面不应有划痕、擦伤等损伤零件表面的缺陷；\n"
        + "7、加工的螺纹表面不应有黑皮、磕碰、乱扣和毛刺等缺陷；\n"
        + "8、未注尺寸参考3D数模。";

    // swAnnotationType_e
    private const int AnnotationNote = 6;

    /// <summary>
    /// 插技术要求：图框里已经有一条带「技术要求」的注释就不再加（模板备用的那几条摆在图框外，不算）。
    /// 内容取模块数据目录的 <see cref="TechnicalNoteFile"/>；没有就用 <see cref="DefaultTechnicalNote"/> 并顺手写出这个文件，方便用户改。
    /// </summary>
    private static (object? Note, string Line) InsertTechnicalNote(QuickCommandContext context, object drawing, double width, double height)
    {
        var api = context.Api;
        var inside = SheetSpace.Standard(width, height).Frame;
        if (SheetAnnotations(api, drawing).Any(item => item.Text.Contains("技术要求", StringComparison.Ordinal) && item.Rect.Overlaps(inside)))
            return (null, "· 技术要求：图纸里已经有了，没有再加。");

        var (content, source) = TechnicalNoteText(context.Options.DataDirectory);
        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        var note = api.Call(drawing, "IModelDoc2", "InsertNote", content);
        return note is null
            ? (null, "· 技术要求：SolidWorks 没有接受，没有加上。")
            : (note, $"· 技术要求：已放进图纸（{source}）。");
    }

    /// <summary>技术要求的内容与出处说明。</summary>
    internal static (string Content, string Source) TechnicalNoteText(string? dataDirectory)
    {
        if (dataDirectory is null)
            return (DefaultTechnicalNote, "默认 8 条");
        var path = Path.Combine(dataDirectory, TechnicalNoteFile);
        try
        {
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path, System.Text.Encoding.UTF8).Replace("\r\n", "\n").TrimEnd('\n', ' ');
                if (text.Length > 0)
                    return (text, $"取自 {path}");
            }

            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(path, DefaultTechnicalNote.Replace("\n", Environment.NewLine) + Environment.NewLine, new System.Text.UTF8Encoding(false));
            return (DefaultTechnicalNote, $"默认 8 条，已写到 {path}，改它就能换内容");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (DefaultTechnicalNote, "默认 8 条");
        }
    }

    /// <summary>图纸（含图纸格式）上的注解：名字、占的地方、注释文字（不是注释为空）。注释取它的外框，别的取位置前后 6 mm。</summary>
    private static List<(string Name, SheetRect Rect, string Text)> SheetAnnotations(SolidWorksApi api, object drawing)
    {
        var result = new List<(string, SheetRect, string)>();
        if (api.Call(drawing, "IDrawingDoc", "GetFirstView") is not { } sheetView)
            return result;
        foreach (var annotation in api.CallArray(sheetView, "IView", "GetAnnotations"))
        {
            var name = api.CallString(annotation, "IAnnotation", "GetName");
            var position = api.CallDoubles(annotation, "IAnnotation", "GetPosition");
            var text = string.Empty;
            SheetRect? rect = null;
            if (api.CallInt(annotation, "IAnnotation", "GetType") == AnnotationNote
                && api.Call(annotation, "IAnnotation", "GetSpecificAnnotation") is { } note)
            {
                text = api.CallString(note, "INote", "GetText");
                var extent = api.CallDoubles(note, "INote", "GetExtent");
                if (extent.Length >= 6 && extent[3] > extent[0])
                    rect = new SheetRect(extent[0], extent[1], extent[3], extent[4]);
            }

            if (rect is null && position.Length >= 2)
                rect = new SheetRect(position[0] - 0.006, position[1] - 0.006, position[0] + 0.006, position[1] + 0.006);
            if (rect is { } r)
                result.Add((name, r, text));
        }

        return result;
    }

    /// <summary>
    /// 实际图纸的可用空间：国标估计（<see cref="SheetSpace.Standard"/>）再加上图框里读到的每个注解（标题栏各格、修改栏、
    /// 「其余」粗糙度、公司名……），不同模板也躲得开。图框外的（模板备用的技术要求、分区字母）不算；刚插的技术要求不算。
    /// </summary>
    private static SheetSpace DetectSpace(SolidWorksApi api, object drawing, double width, double height, string? exclude)
    {
        var space = SheetSpace.Standard(width, height);
        var inner = space.Frame.Inflate(-0.001);
        var found = SheetAnnotations(api, drawing)
            .Where(item => item.Name != exclude && inner.Overlaps(item.Rect) && item.Rect.Center.X > inner.Left && item.Rect.Center.X < inner.Right
                           && item.Rect.Center.Y > inner.Bottom && item.Rect.Center.Y < inner.Top)
            .Select(item => item.Rect.Inflate(0.001));
        return space.With(found);
    }

    /// <summary>视图在图纸上占多大：实际外框，加左边、上边的标注空间（按这个视图里能看到的孔的种数估）。</summary>
    private static ViewBox Box(QuickCommandContext context, PartGeometry geometry, object view, bool chain, bool annotations = true)
    {
        var outline = Outline(context.Api, view);
        if (!annotations)
            return new ViewBox(outline.Width, outline.Height, 0, 0);
        var margin = DrawingPlanner.AnnotationMargin(DrawingPlanner.HoleKinds(geometry, HoleScan.Frame(context, view).Normal), chain);
        return new ViewBox(outline.Width, outline.Height, margin, margin);
    }

    private static (double Width, double Height) NoteSize(SolidWorksApi api, object? note)
    {
        if (note is null)
            return (0, 0);
        var extent = api.CallDoubles(note, "INote", "GetExtent");
        return extent.Length >= 6 && extent[3] > extent[0]
            ? (extent[3] - extent[0], extent[4] - extent[1])
            : (DrawingPlanner.DefaultNoteWidth, DrawingPlanner.DefaultNoteHeight);
    }

    /// <summary>
    /// 按实际外框排版并落位：排不下就把整页比例降一档再排（视图跟着图纸比例变），降到底还排不下就按最后一次的结果尽量摆。
    /// 主视图挪了，与它对齐的投影视图跟着走；投影视图只能沿对齐方向挪；轴测图不对齐、随便放，需要时比例缩一档。
    /// </summary>
    private static LayoutResult Arrange(QuickCommandContext context, object drawing, object sheet, PartGeometry geometry, object mainView,
        IReadOnlyDictionary<ViewSlot, object> sideViews, object? iso, object? note, double width, double height, ref double scale)
    {
        var api = context.Api;
        var chain = context.Options.Chain;
        var noteAnnotation = note is null ? null : api.Call(note, "INote", "GetAnnotation");
        var noteName = noteAnnotation is null ? null : api.CallString(noteAnnotation, "IAnnotation", "GetName");
        var scales = DrawingPlanner.Scales(geometry);
        LayoutResult layout;
        while (true)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            api.Call(drawing, "IModelDoc2", "EditRebuild3");
            var (noteWidth, noteHeight) = NoteSize(api, note);
            var request = new LayoutRequest(
                Box(context, geometry, mainView, chain),
                sideViews.ToDictionary(pair => pair.Key, pair => Box(context, geometry, pair.Value, chain)),
                iso is null ? new ViewBox(0, 0, 0, 0) : Box(context, geometry, iso, chain, annotations: false),
                noteWidth,
                noteHeight,
                DrawingPlanner.IsoFactors(geometry, scale));
            layout = DrawingPlanner.Layout(DetectSpace(api, drawing, width, height, noteName), request);
            var currentScale = scale;
            var smaller = scales.Where(s => s < currentScale - 1e-12).Select(s => (double?)s).FirstOrDefault();
            if (layout.Fits || smaller is null)
                break;
            context.Report($"新建工程图：按实际大小 {DrawingPlanner.ScaleText(scale)} 排不下（{layout.Problem}），比例降到 {DrawingPlanner.ScaleText(smaller.Value)} 再排。");
            scale = smaller.Value;
            SetSheetScale(api, sheet, scale);
        }

        SetPosition(api, mainView, layout.Main);
        foreach (var (slot, view) in sideViews)
            if (layout.Sides.TryGetValue(slot, out var center))
                SetPosition(api, view, center);
        if (iso is not null)
        {
            if (layout.IsoShrink < 1)
            {
                var target = scale * layout.IsoShrink;
                var isoScale = scales.FirstOrDefault(s => s <= target + 1e-12, target);
                var (numerator, denominator) = DrawingPlanner.ScaleRatio(isoScale);
                api.Call(iso, "IView", "set_UseSheetScale", 0);
                api.Call(iso, "IView", "set_ScaleRatio", new double[] { numerator, denominator });
                layout = layout with { IsoShrink = isoScale / scale };
            }

            if (layout.Iso is { } isoCenter)
                SetPosition(api, iso, isoCenter);
        }

        if (noteAnnotation is not null)
        {
            // 视图组排不下时排版不给技术要求的位置；插入时它落在图纸原点下方（图框外），至少挪到图框左下角。
            var (_, noteHeight) = NoteSize(api, note);
            var frame = SheetSpace.Standard(width, height).Frame;
            var topLeft = layout.NoteTopLeft ?? new SheetPoint(frame.Left + 2 * DrawingPlanner.Gap, frame.Bottom + 2 * DrawingPlanner.Gap + noteHeight);
            api.Call(noteAnnotation, "IAnnotation", "SetPosition2", topLeft.X, topLeft.Y, 0.0);
        }
        api.Call(drawing, "IModelDoc2", "EditRebuild3");
        return layout;
    }

    /// <summary>挪视图：<c>IView.Position</c> 就是外框中心（真机核对过）。</summary>
    private static void SetPosition(SolidWorksApi api, object view, SheetPoint center)
        => api.Call(view, "IView", "set_Position", new double[] { center.X, center.Y });
}
