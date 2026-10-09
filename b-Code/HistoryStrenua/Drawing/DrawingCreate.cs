using System.Diagnostics;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「新建工程图」（1.9.0，出图类）：给当前零件（或装配体里选中的零件）按用户的工程图模板建一张图——
/// 选图幅与比例、放主视图。不保存。投影视图、轴测图、技术要求、排版各是一条指令（<see cref="DrawingProject"/>、<see cref="DrawingIso"/>、
/// <see cref="TechApply"/>、<see cref="DrawingArrange"/>），「基础出图」（<see cref="DrawingBasic"/>）依次做完这一类，一键类「一键出图」（<see cref="OneKeyDrawing"/>）再加要求与标注。
/// </summary>
/// <remarks>
/// <para>依据是用户 2026-025 台面2机器 15 张手工图的共同做法（DEC-021）：标题栏由模板的属性链接带出（图号、名称、材料、表面处理、
/// 热处理、设计、日期、比例），「其余 6.3」与公司名在模板里，所以出图类只管视图与技术要求。</para>
/// <para>图幅与比例按整套视图估（主视图、该有的投影视图、轴测图、技术要求都要排得下，<see cref="DrawingPlanner.ChooseSheet"/>），
/// 所以只建主视图时纸看着偏大；主视图放在整套排好时它该在的位置。用户拆开用时（1.9.0 用户定）可以先改主视图、模板或比例再接着按后面几步。</para>
/// <para>不保存（PowerSW 不替用户存文件）。新图名字改成零件名（1.15.0，用户定：原来是「工程图1」），第一次保存时默认文件名就是它；「一键出图」也走这里。</para>
/// </remarks>
internal static class DrawingCreate
{
    public static QuickCommand Command { get; } = new(
        Key: "drawing-create",
        CommandName: StrenuaIdentity.Domain + ".drawing.create",
        Title: "新建工程图",
        Summary: "给当前零件（或装配体里选中的零件）按工程图模板建图：按整套视图自动选图幅与比例、放主视图，不保存。",
        Usage: "在 SolidWorks 里打开要出图的零件（或在装配体里选中一个零件，先选后按、先按后选都行，60 秒内）再按：按「零件」工程图模板建一张新图，主视图取孔、窗口和圆弧最多的那一面；图幅（A4 横 / A3 / A2）与比例按零件大小、孔的疏密，以及整套视图（主视图、该有的投影视图、轴测图、技术要求）排不排得下自动选，标题栏由模板的属性链接自动带出，新图名字与零件一致（保存时默认文件名）。只放主视图：接着按「投影视图」「轴测图」「技术要求」「排版」补齐摆好，或直接用「一键出图」全做并标注。新图不保存。",
        Run: context => Create(context).Outcome);

    /// <summary>建图的结论，另带新图（<c>IModelDoc2</c>）、主视图（<c>IView</c>）与零件几何给「一键出图」接着用；失败时为 null。</summary>
    internal sealed record Created(QuickOutcome Outcome, object? Drawing, object? Main, PartGeometry? Part);

    // swDocumentTypes_e
    private const int DocumentPart = 1;
    private const int DocumentAssembly = 2;

    // swRebuildOnActivation_e.swDontRebuildActiveDoc
    private const int DontRebuild = 1;

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="next">回执末尾提示接下来按哪几步（「一键出图」自己接着做，不提示）。</param>
    public static Created Create(QuickCommandContext context, bool next = true)
    {
        var api = context.Api;
        var application = context.Session.Application;
        var chain = context.Options.Chain;
        var stopwatch = Stopwatch.StartNew();

        var (part, partPath, partTitle) = ResolvePart(context);
        context.SetState("读零件");
        context.Report($"新建工程图：读零件「{partTitle}」的孔与圆弧面。");
        var geometry = PartScan.Read(context, part);
        var main = DrawingPlanner.MainView(geometry);
        var planned = new ViewFrame(main.View.Right, main.View.Up, main.View.Normal, new SheetPoint(0, 0), 1);
        var plannedSides = DrawingPlanner.SideViews(geometry, planned, firstAngle: true);

        var (templates, fallback) = DrawingTemplates.Find(api, application);
        var choice = templates.Count > 0 ? DrawingPlanner.ChooseSheet(geometry, main.View, plannedSides, templates, chain) : null;
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
        var named = Rename(api, drawing, Path.GetFileNameWithoutExtension(partPath));
        Activate(api, application, drawing);
        var sheet = api.Call(drawing, "IDrawingDoc", "GetCurrentSheet")
            ?? throw new QuickCommandException("新工程图没有图纸页。");
        var properties = api.CallDoubles(sheet, "ISheet", "GetProperties2");
        var firstAngle = properties.Length <= 4 || properties[4] != 0;
        var (width, height) = properties.Length > 6 ? (properties[5], properties[6]) : (choice?.Template.Width ?? 0.42, choice?.Template.Height ?? 0.297);

        // 没选出模板（用最大的或默认模板）时，在实际图纸上按估计找排得下的最大比例。
        var space = SheetSpace.Standard(width, height);
        var scales = DrawingPlanner.Scales(geometry);
        var plannedSlots = choice?.Slots ?? plannedSides.Select(side => side.Slot).ToList();
        var scale = choice?.Scale ?? scales.FirstOrDefault(s =>
            DrawingPlanner.Layout(space, DrawingPlanner.Estimate(geometry, main.View, plannedSlots, s, chain)).Fits, scales[^1]);
        var plan = choice?.Layout ?? DrawingPlanner.Layout(space, DrawingPlanner.Estimate(geometry, main.View, plannedSlots, scale, chain));
        DrawingSheet.SetSheetScale(api, sheet, scale);

        context.SetState("建主视图");
        context.Report($"新建工程图：主视图「{main.View.Names[0]}」（{main.Reason}）。");
        var (mainView, used, orientation) = CreateMain(context, drawing, partPath, main.View, plan.Main);
        // SolidWorks「自动缩放新视图」开着时，插第一个视图会把图纸比例改成它自己挑的（真机：连接件 A2 上被改成 1:1），这里设回来。
        DrawingSheet.SetSheetScale(api, sheet, scale);
        api.Call(drawing, "IModelDoc2", "EditRebuild3");
        // 主视图放在整套排好时它该在的位置（估计的），后面一步步加的视图贴着它放，最后「排版」按实际大小再排一遍。
        DrawingSheet.SetPosition(api, mainView, plan.Main);
        api.Call(drawing, "IModelDoc2", "EditRebuild3");
        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        api.Call(drawing, "IModelDoc2", "ViewZoomtofit2");

        var mainName = DrawingSheet.Name(api, mainView);
        var sides = DrawingPlanner.SideViews(geometry, HoleScan.Frame(context, mainView), firstAngle);
        var slots = DrawingPlanner.ChooseSlots(geometry, main.View, sides, space, scale, chain);
        var scaleText = DrawingPlanner.ScaleText(DrawingSheet.SheetScale(api, sheet, scale));
        var lines = new List<string>
        {
            $"新建工程图：零件「{partTitle}」→ 新图「{api.CallString(drawing, "IModelDoc2", "GetTitle")}」（未保存），" +
            $"模板「{Path.GetFileNameWithoutExtension(templatePath)}」{(choice is null ? string.Empty : "（" + choice.Template.SizeName + "）")}，比例 {scaleText}，用时 {stopwatch.Elapsed.TotalSeconds:0.0} 秒"
            + (named ? "。" : $"；图名没能改成零件名「{Path.GetFileNameWithoutExtension(partPath)}」（可能已开着同名的文档），保存时请自己改。"),
            $"· 主视图「{mainName}」{used.Names[0]}：{main.Reason}{orientation}。",
        };
        var plannedText = sides.Select((side, index) => $"{DrawingSheet.SlotName(slots[index])}投影视图（{side.Reason}）").Append("轴测图").Append("技术要求");
        lines.Add($"· 图幅与比例按整套视图估（主视图、{string.Join("、", plannedText)}）" + (choice is null ? "。" : $"：{choice.Reason}。"));
        if (next)
            lines.Add("· 接着按「投影视图」「轴测图」「技术要求」「排版」补齐摆好，或用「基础出图」一次建好排好、一键类「一键出图」连标注一起做完。");
        return new Created(QuickOutcome.Ok(string.Join(Environment.NewLine, lines)), drawing, mainView, geometry);
    }

    /// <summary>
    /// 建主视图并核对朝向（1.16.0）：按 <paramref name="wanted"/> 的名字建，读回实际视线；不是想要的那面（零件改过标准视图，真机滑动板「*前视」实际沿 +X），
    /// 删掉它，按读回的朝向推出该用哪个命名视图（<see cref="DrawingPlanner.RetryViews"/>）再建，逐个核对。都对不上就留第一个、回执说明。
    /// </summary>
    /// <returns>主视图、实际用的命名视图、接在回执理由后面的一句（没换名字为空）。</returns>
    private static (object View, StandardView Used, string Note) CreateMain(
        QuickCommandContext context, object drawing, string partPath, StandardView wanted, SheetPoint at)
    {
        var api = context.Api;
        object? Make(StandardView view) => view.Names
            .Select(name => api.Call(drawing, "IDrawingDoc", "CreateDrawViewFromModelView3", partPath, name, at.X, at.Y, 0.0))
            .FirstOrDefault(created => created is not null);

        var first = Make(wanted) ?? throw new QuickCommandException($"SolidWorks 没能建主视图（{string.Join(" / ", wanted.Names)}）。");
        var frame = HoleScan.Frame(context, first);
        if (DrawingPlanner.Facing(frame, wanted.Normal))
            return (first, wanted, string.Empty);

        var actual = DescribeNormal(frame.Normal);
        context.Report($"新建工程图：「{wanted.Names[0]}」在这个零件里实际是沿 {actual} 看（零件改过标准视图），换命名视图重建主视图。");
        Delete(api, drawing, first);
        foreach (var candidate in DrawingPlanner.RetryViews(wanted, frame, wanted.Normal))
        {
            if (Make(candidate) is not { } view)
                continue;
            if (DrawingPlanner.Facing(HoleScan.Frame(context, view), wanted.Normal))
                return (view, candidate, $"；零件改过标准视图，「{wanted.Names[0]}」实际沿 {actual} 看，改用「{candidate.Names[0]}」");
            Delete(api, drawing, view);
        }

        var fallback = Make(wanted) ?? throw new QuickCommandException($"SolidWorks 没能建主视图（{string.Join(" / ", wanted.Names)}）。");
        return (fallback, wanted, $"；零件改过标准视图，「{wanted.Names[0]}」实际沿 {actual} 看，六个命名视图都对不上想要的那面，请自己换主视图");
    }

    /// <summary>删掉一个视图（核对朝向不对的主视图）。</summary>
    private static void Delete(SolidWorksApi api, object drawing, object view)
    {
        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        var extension = api.Call(drawing, "IModelDoc2", "get_Extension");
        if (extension is not null
            && api.CallBool(extension, "IModelDocExtension", "SelectByID2", DrawingSheet.Name(api, view), "DRAWINGVIEW", 0.0, 0.0, 0.0, false, 0, null, 0))
            api.Call(drawing, "IModelDoc2", "EditDelete");
        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
    }

    /// <summary>视线写成「+X」「−Z」；不沿坐标轴时写三个分量。</summary>
    internal static string DescribeNormal(ModelDirection normal)
    {
        foreach (var (value, name) in new[] { (normal.X, "X"), (normal.Y, "Y"), (normal.Z, "Z") })
        {
            if (Math.Abs(value) >= 0.999)
                return (value > 0 ? "+" : "−") + name;
        }

        return $"({normal.X:0.##}, {normal.Y:0.##}, {normal.Z:0.##})";
    }

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

    /// <summary>
    /// 新图的名字改成零件名（1.15.0，用户定）：<c>IModelDoc2.SetTitle2</c> 只对没保存过的新文档有效，改的是窗口标题，
    /// 也是第一次保存时「另存为」里默认的文件名（不改就是「工程图1」）。读回标题核对，没改成返回 false。
    /// </summary>
    private static bool Rename(SolidWorksApi api, object drawing, string name)
    {
        api.Call(drawing, "IModelDoc2", "SetTitle2", name);
        return api.CallString(drawing, "IModelDoc2", "GetTitle").StartsWith(name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>把新图切成活动窗口（NewDocument 之后活动窗口有时还停在零件上，后面按活动文档干活的步骤会做错图）。</summary>
    private static void Activate(SolidWorksApi api, object application, object document)
    {
        var errors = 0;
        api.Call(application, "ISldWorks", "ActivateDoc3", api.CallString(document, "IModelDoc2", "GetTitle"), false, DontRebuild, errors);
    }
}
