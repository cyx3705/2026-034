using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「倒角标注」（1.10.0；1.15.0 起倒圆倒角类）：点一个工程图视图，视图里侧着看成斜线的平面倒角各标一个线性尺寸、文字写「C5」，
/// 同一尺寸 2 个以上合标「N x C5」，C1 不标，已标的跳过。
/// </summary>
/// <remarks>
/// <para>认倒角见 <see cref="HoleScan.ViewGeometry.TryReadChamfer"/>（倒角特征做的平面、侧着看）；标哪些、量哪条直角边、尺寸放哪见 <see cref="ChamferPlanner"/>。
/// 只选斜边一条，<c>AddHorizontalDimension2</c> / <c>AddVerticalDimension2</c> 量它的横向 / 竖向跨度，再把前缀写成「C」「2 x C」。
/// 用户定（审 1.10.0 时）：不用 SolidWorks 的倒角尺寸（<c>AddChamferDim</c>，引线箭头那种），用普通尺寸。</para>
/// <para>不重复：这个视图里某个尺寸已经标过，同尺寸的都算标过；这一页别的视图里已有倒角尺寸的同一张倒角面也跳过（同一个倒角在前后两个视图里都成斜线）。
/// 轴端的锥面倒角（用户在剖视图里标）暂不认。</para>
/// </remarks>
internal static class ChamferDimension
{
    public static QuickCommand Command { get; } = new(
        Key: "chamfer",
        CommandName: StrenuaIdentity.Domain + ".fillet.chamfer",
        Title: "倒角标注",
        Summary: "点一个工程图视图，给侧着看成斜线的倒角标线性尺寸写「C5」（C1 按技术要求不标，同尺寸 2 个以上合标 N x C5），已标的跳过。",
        Usage: "在工程图里点一个视图（先点后按、先按后点都行，60 秒内）：视图里侧着看成斜线的倒角（倒角特征做的平面倒角）每种尺寸标一个线性尺寸，量它的一条直角边、文字写「C5」，同一尺寸有 2 个以上时写「N x C5」，标靠右的那个；尺寸放在倒角那一侧的视图外，先下边、右边（孔位与外轮廓尺寸在上边、左边），出图框就换一边；不等边的倒角两条直角边各标一个。C1 不标（技术要求「未注倒角C1」）。这个视图里已标过的尺寸、这一页别的视图已标过的同一个倒角都跳过。轴端的锥面倒角暂不认。「避障」开着时挑尺寸放哪边、离视图多远、文字沿尺寸线挪多少，要躲开这个视图与同一页别的视图里已有的标注和别的视图本身，关着时只看下、右、上、左的先后。",
        Run: context => Run(context, null));

    // swDimensionType_e.swChamferDimension
    internal const int ChamferDimensionType = 10;

    // swDimensionTextParts_e.swDimensionTextPrefix
    private const int TextPrefix = 1;

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="view">直接处理这个视图（「全图倒角」用）；null 时取选中的或等用户点选。</param>
    internal static QuickOutcome Run(QuickCommandContext context, object? view)
    {
        var api = context.Api;
        var scan = HoleScan.Scan(context, "倒角标注", withLines: true, view: view, withChamfers: true);
        var (document, viewName) = (scan.Document, scan.ViewName);
        if (scan.Chamfers.Count == 0)
            return QuickOutcome.Ok($"视图「{viewName}」里没有侧着看成斜线的倒角，没有加 C 尺寸。");

        var (here, elsewhere) = Existing(context, scan);
        // 1.11.0「避障」开着时连视图里已有注解的线与文字一起躲；1.15.0 起再躲同一页别的视图（外框当一块不许压的地方）和它们的注解——
        // 倒角尺寸放在视图外，往外让几档就可能伸进邻近视图。
        var avoid = context.Options.Clearance;
        var annotations = avoid ? SheetObstacles(api, scan) : Obstacles.Empty;
        var obstacles = scan.Lines.Concat(scan.CurveSegments).Concat(DrawingSheet.FrameLines(api, document)).Concat(annotations.Lines).ToList();
        var plan = ChamferPlanner.Plan(scan.Chamfers, scan.Lines, obstacles, here, elsewhere, DrawingSheet.FrameRect(api, document), annotations.Texts, avoid);
        var skipped = (plan.DefaultCount > 0 ? $"，{plan.DefaultCount} 个是 C1（技术要求未注倒角 C1）不标" : string.Empty)
            + (plan.Dimensioned > 0 ? $"，{plan.Dimensioned} 个已标过跳过" : string.Empty)
            + (plan.Elsewhere > 0 ? $"，{plan.Elsewhere} 个别的视图已标跳过" : string.Empty)
            + (plan.NoLead > 0 ? $"，{plan.NoLead} 个两头接不上直边、认不出哪边在零件外，没法标" : string.Empty);
        if (plan.Targets.Count == 0)
        {
            var none = $"视图「{viewName}」：{plan.ChamferCount} 个倒角{skipped}，没有新加。";
            return plan.NoLead > 0 ? QuickOutcome.Fail(none) : QuickOutcome.Ok(none);
        }

        context.SetState("加倒角尺寸");
        context.Report($"倒角标注：视图「{viewName}」认出 {plan.ChamferCount} 个倒角，加 {plan.Targets.Count} 个倒角尺寸。");
        var added = 0;
        var failed = 0;
        _ = api.Call(document, "IDrawingDoc", "ActivateView", viewName);
        try
        {
            foreach (var target in plan.Targets)
            {
                foreach (var placement in target.Placements)
                {
                    context.Cancellation.ThrowIfCancellationRequested();
                    api.Call(document, "IModelDoc2", "ClearSelection2", true);
                    // 只选斜边：水平 / 竖直尺寸量的就是它的横向 / 竖向跨度（一条直角边）。
                    var method = placement.Axis == PositionAxis.Horizontal ? "AddHorizontalDimension2" : "AddVerticalDimension2";
                    var dimension = api.CallBool(scan.View, "IView", "SelectEntity", scan.ChamferEdges![target.Index], false)
                        ? api.Call(document, "IModelDoc2", method, placement.At.X, placement.At.Y, 0.0)
                        : null;
                    if (dimension is null)
                    {
                        failed++;
                        continue;
                    }

                    if (!HolePositionPlanner.IsLinear(api.CallInt(dimension, "IDisplayDimension", "get_Type2")))
                    {
                        HolePosition.Discard(api, scan, dimension);
                        failed++;
                        continue;
                    }

                    added++;
                    if (target.Prefix.Length > 0)
                    {
                        var prefix = api.CallString(dimension, "IDisplayDimension", "GetText", TextPrefix);
                        api.Call(dimension, "IDisplayDimension", "SetText", TextPrefix, target.Prefix + prefix);
                    }
                }
            }
        }
        finally
        {
            api.Call(document, "IModelDoc2", "ClearSelection2", true);
            api.Call(document, "IModelDoc2", "GraphicsRedraw2");
        }

        var grouped = plan.Targets.Where(target => target.Count > 1).Sum(target => target.Count - 1);
        var message = $"视图「{viewName}」：{plan.ChamferCount} 个倒角，新加 {added} 个倒角尺寸"
            + (grouped > 0 ? $"（其中合标「N x」管了另外 {grouped} 个）" : string.Empty)
            + skipped
            + (failed > 0 ? $"，{failed} 个 SolidWorks 没有接受" : string.Empty) + "。";
        return added == 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }

    /// <summary>
    /// 避障要躲的（1.15.0）：这个视图的全部注解，同一页别的视图的全部注解，以及别的视图的外框（当一个文字框，整块不许压）。读不了的跳过。
    /// </summary>
    private static Obstacles SheetObstacles(SolidWorksApi api, ScannedView scan)
    {
        var own = Clearance.ViewObstacles(api, scan.View);
        var lines = new List<SheetSegment>(own.Lines);
        var texts = new List<TextBox>(own.Texts);
        foreach (var view in HoleScan.SheetViews(api, scan.Document))
        {
            if (DrawingSheet.Name(api, view) == scan.ViewName)
                continue;
            var outline = DrawingSheet.Outline(api, view);
            if (outline.Width > 0 && outline.Height > 0)
                texts.Add(new TextBox(outline.Left, outline.Bottom, outline.Width, outline.Height));
            var other = Clearance.ViewObstacles(api, view);
            lines.AddRange(other.Lines);
            texts.AddRange(other.Texts);
        }

        return new Obstacles(lines, texts);
    }

    /// <summary>
    /// 已有的倒角尺寸：这个视图里连着的斜边（图纸上），以及这一页别的视图（同一模型）里连着的倒角面（<see cref="ChamferEdge.Key"/>）。
    /// </summary>
    private static (List<SheetSegment> Here, HashSet<string> Elsewhere) Existing(QuickCommandContext context, ScannedView scan)
    {
        var api = context.Api;
        var here = new List<SheetSegment>();
        var elsewhere = new HashSet<string>(StringComparer.Ordinal);
        var model = HoleScan.ModelKey(api, scan.View);
        foreach (var view in HoleScan.SheetViews(api, scan.Document))
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var name = api.CallString(view, "IView", "get_Name");
            var self = name == scan.ViewName;
            if (!self && (api.Call(view, "IView", "get_ReferencedDocument") is null || HoleScan.ModelKey(api, view) != model))
                continue;
            var geometry = self ? scan.Geometry : new HoleScan.ViewGeometry(api, context.Session.Application, view);
            foreach (var edge in ChamferDimensionEdges(api, view))
            {
                if (self)
                {
                    if (ReadLine(geometry, edge) is { } segment)
                        here.Add(segment);
                }
                else if (geometry.TryChamferKey(edge) is { } key)
                {
                    elsewhere.Add(key);
                }
            }
        }

        return (here, elsewhere);
    }

    /// <summary>附着对象可能已失效（悬空尺寸），这时不是边。</summary>
    private static SheetSegment? ReadLine(HoleScan.ViewGeometry geometry, object edge)
    {
        try
        {
            return geometry.TryReadLine(edge);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or System.Reflection.TargetException)
        {
            return null;
        }
    }

    /// <summary>
    /// 视图里倒角尺寸连着的边（悬空、失效的跳过）：前缀带「C」的线性尺寸（本指令标的、用户照这样手标的），以及 SolidWorks 倒角尺寸（用户手工图里的引线那种）。
    /// 别的线性尺寸不算——外轮廓、孔位尺寸也可能连着挨着倒角面的边。
    /// </summary>
    private static IEnumerable<object> ChamferDimensionEdges(SolidWorksApi api, object view)
    {
        for (var display = api.Call(view, "IView", "GetFirstDisplayDimension5"); display is not null; display = api.Call(display, "IDisplayDimension", "GetNext5"))
        {
            var type = api.CallInt(display, "IDisplayDimension", "get_Type2");
            var chamfer = type == ChamferDimensionType
                || (HolePositionPlanner.IsLinear(type) && api.CallString(display, "IDisplayDimension", "GetText", TextPrefix).Contains('C'));
            if (!chamfer || api.Call(display, "IDisplayDimension", "GetAnnotation") is not { } annotation)
                continue;
            foreach (var entity in api.CallArray(annotation, "IAnnotation", "GetAttachedEntities3"))
                if (entity is not null)
                    yield return entity;
        }
    }
}
