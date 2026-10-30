using System.Globalization;
using System.Runtime.InteropServices;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「图纸 SVG」<c>strenua.check.svg</c>（1.21.0，检查类，用户定：AI 调完 MCP 拿 SVG 代码当反馈，不看工程图截图）：
/// 当前图纸页读成一段 SVG，回执 Data 就是 SVG 文本，同时存一份文件。不改图。
/// </summary>
/// <remarks>
/// <para>写法与取舍见 <see cref="DrawingSvgWriter"/>。这里只管从 SolidWorks 读：每个视图的可见直边、曲线、孔沿用 <see cref="HoleScan.Scan"/>
/// （和标注指令看到的是同一份几何，编号也一致；钣金折弯线在那里就去掉了，SVG 里也没有），注解沿用 <see cref="DimList"/> 的读法，
/// 尺寸量的两头沿用 <see cref="DimensionScan"/>。</para>
/// <para>图纸页本身的注释：落在标题栏 / 修改栏 / 图号框里的并进那一块的字，落在图框外的（模板备用的技术要求）不出，其余（技术要求……）照注解出。</para>
/// </remarks>
internal static class DrawingSvg
{
    public static QuickCommand Command { get; } = new(
        Key: "svg",
        CommandName: StrenuaIdentity.Domain + ".check.svg",
        Title: "图纸 SVG",
        Summary: "把当前图纸页读成 SVG 代码回执（给 AI 当反馈）：零件边与孔精确带编号，标注画方框加显示文字，坐标为图纸 mm；不改图。",
        Usage: "不用点视图：读当前图纸页，回执的数据就是一段 SVG（同时存到模块数据目录 snapshots 里，只留最近 30 份，可用浏览器打开）。坐标是图纸 mm、左下角为原点，和视图、标注类的 X / Y 相同。每个视图一组：可见直边（id「视图名:E序号」）、曲线（折线近似）、孔（「视图名:H序号」，带孔种）；尺寸、孔标注、注释只画外框方框和图上显示的字，尺寸另写量的两头（data-measures）；中心符号线、中心线画成线。标题栏、修改栏、图号框各一个方框，格子里的字合在 data-text。不改图。",
        // 要存一份文件，所以和「图纸截图」一样不标只读（宿主的只读 = 不写文件）。
        Run: Run);

    // swAnnotationType_e
    private const int AnnotationNote = 6;
    private const int AnnotationCenterMark = 13;
    private const int AnnotationCenterLine = 15;

    private static readonly string[] BlockLabels = ["标题栏", "修改栏", "图号框"];
    private static readonly string[] BlockIds = ["title-block", "revision-block", "number-block"];

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var drawing = HoleScan.ActiveDrawing(context);
        var sheet = Read(context, drawing);
        var svg = DrawingSvgWriter.Write(sheet);
        var file = Save(context, sheet.Title, svg);
        var views = sheet.Views.Where(view => view.Outline is not null).ToList();
        var message = $"图纸 SVG：「{sheet.Title}」当前图纸页 {views.Count} 个视图、{views.Sum(view => view.Lines.Count)} 条直边、{views.Sum(view => view.Holes.Count)} 个孔、"
            + $"{sheet.Views.Sum(view => view.Annotations.Count)} 个注解，SVG {svg.Length} 字（数据里是全文），已存 {file}";
        return QuickOutcome.Ok(message, svg);
    }

    /// <summary>读当前图纸页（后续每一步回执 SVG 也从这里读）。</summary>
    internal static SvgSheet Read(QuickCommandContext context, object drawing)
    {
        var api = context.Api;
        context.SetState("读图纸");
        var title = api.CallString(drawing, "IModelDoc2", "GetTitle");
        var sheetObject = api.Call(drawing, "IDrawingDoc", "GetCurrentSheet");
        var properties = sheetObject is null ? [] : api.CallDoubles(sheetObject, "ISheet", "GetProperties2");
        var (width, height) = properties.Length > 6 && properties[5] > 0 ? (properties[5], properties[6]) : (0.420, 0.297);
        var space = SheetSpace.Standard(width, height);

        var views = new List<SvgView>();
        var blocks = new List<SvgBlock>();
        var sheetName = string.Empty;
        foreach (var view in SheetLookup.Views(api, drawing))
        {
            context.Cancellation.ThrowIfCancellationRequested();
            if (SheetLookup.IsSheet(api, view))
            {
                sheetName = DrawingSheet.Name(api, view);
                var (sheetView, sheetBlocks) = SheetNotes(api, view, sheetName, space);
                views.Add(sheetView);
                blocks.AddRange(sheetBlocks);
            }
            else
                views.Add(View(context, drawing, view));
        }

        return new SvgSheet(title, sheetName, width, height, space.Frame, blocks, views);
    }

    /// <summary>图纸页本身：标题栏 / 修改栏 / 图号框各一块（里面的注释并成一段字），图框外的不出，其余是注解。</summary>
    private static (SvgView View, List<SvgBlock> Blocks) SheetNotes(SolidWorksApi api, object view, string sheetName, SheetSpace space)
    {
        var texts = space.KeepOuts.Select(_ => new List<string>()).ToList();
        var annotations = new List<SvgAnnotation>();
        var outside = 0;
        foreach (var (name, annotation) in SheetLookup.Annotations(api, view))
        {
            var item = Annotation(api, sheetName, name, annotation, null);
            var center = item.Box?.Center ?? item.TextAt;
            if (center is not { } c)
                continue;
            var block = space.KeepOuts.ToList().FindIndex(rect => c.X >= rect.Left && c.X <= rect.Right && c.Y >= rect.Bottom && c.Y <= rect.Top);
            if (block >= 0)
            {
                if (item.Text.Length > 0)
                    texts[block].Add(item.Text.Replace("\n", " "));
            }
            else if (c.X < space.Frame.Left || c.X > space.Frame.Right || c.Y < space.Frame.Bottom || c.Y > space.Frame.Top)
                outside++;
            else
                annotations.Add(item);
        }

        var blocks = space.KeepOuts
            .Select((rect, i) => new SvgBlock(i < BlockIds.Length ? BlockIds[i] : $"block-{i}", i < BlockLabels.Length ? BlockLabels[i] : "格子", rect,
                string.Join(" | ", texts[i])))
            .ToList();
        var note = outside > 0 ? $"图框外 {outside} 个注释（模板备用的技术要求等）没有画" : string.Empty;
        return (new SvgView(sheetName, "图纸页", string.Empty, string.Empty, null, [], [], [], annotations, note), blocks);
    }

    /// <summary>一个视图：几何沿用 <see cref="HoleScan.Scan"/>（读不了的视图只出外框与注解，原因写进注释）。</summary>
    private static SvgView View(QuickCommandContext context, object drawing, object view)
    {
        var api = context.Api;
        var row = ViewList.Row(context, drawing, view);
        var name = row["name"];
        IReadOnlyList<SheetSegment> lines = [];
        IReadOnlyList<SheetSegment> curves = [];
        IReadOnlyList<HoleEdge> holes = [];
        var measures = new Dictionary<string, string>(StringComparer.Ordinal);
        var note = string.Empty;
        if (api.Call(view, "IView", "get_ReferencedDocument") is null)
            note = "没有引用模型（空视图或轻化），只画外框与注解";
        else if (row["kind"].EndsWith("·轴测", StringComparison.Ordinal))
            // 真机：轴测图的孔都是椭圆，折线近似出来占了整份 SVG 的三分之一，又没有谁按它标尺寸——只画外框。
            note = "轴测图只画外框与注解（不标尺寸，几何不出）";
        else
        {
            try
            {
                var scan = HoleScan.Scan(context, "图纸 SVG", withLines: true, view: view, quiet: true);
                lines = scan.Lines;
                curves = scan.CurveSegments;
                holes = scan.Candidates;
                foreach (var dimension in DimensionScan.Read(api, scan))
                    measures[api.CallString(dimension.Annotation, "IAnnotation", "GetName")] = Measures(dimension.Geometry, scan.Lines, scan.Candidates);
                if (scan.BendLines > 0)
                    note = $"钣金折弯线 {scan.BendLines} 条没有画（不当边、不当基准）";
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or QuickCommandException)
            {
                note = "几何没读出来（" + ex.Message + "），只画外框与注解";
            }
        }

        var annotations = SheetLookup.Annotations(api, view)
            .Select(item => Annotation(api, name, item.Name, item.Annotation, measures.GetValueOrDefault(item.Name)))
            .ToList();
        return new SvgView(name, row["kind"], row["parent"], row["scale"], DrawingSheet.Outline(api, view), lines, curves, holes, annotations, note);
    }

    /// <summary>一个注解：种类、字、值沿用 <see cref="DimList.Row"/>；注释的字取全文（多行）；中心符号线、中心线取显示数据里的线。</summary>
    private static SvgAnnotation Annotation(SolidWorksApi api, string view, string name, object annotation, string? measures)
    {
        var row = DimList.Row(api, view, name, annotation);
        var type = api.CallInt(annotation, "IAnnotation", "GetType");
        var text = row["text"];
        List<SheetSegment> lines = [];
        List<TextBox> texts = [];
        try
        {
            if (type == AnnotationNote && api.Call(annotation, "IAnnotation", "GetSpecificAnnotation") is { } note)
                text = string.Join("\n", DimList.Readable(api.CallString(note, "INote", "GetText"))
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            (lines, texts) = Clearance.DisplayGeometry(api, annotation);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or System.Reflection.TargetException)
        {
            // 读到一半失效：已读到的照出。
        }

        if (type is AnnotationCenterMark or AnnotationCenterLine)
            return new SvgAnnotation(name, row["kind"], string.Empty, string.Empty, null, null, 0, string.Empty, lines);
        var box = Rect(row);
        var first = texts.Count > 0 ? texts[0] : (TextBox?)null;
        // 真机第一版方框取整个外框（连尺寸界线、引线），一大片虚线框互相叠着，看着像标注占满了视图；方框改画字占的地方，整个外框留在 data-box。
        var corners = texts.SelectMany(t => t.Corners()).ToList();
        SheetRect? textBox = corners.Count == 0
            ? null
            : new SheetRect(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Max(p => p.X), corners.Max(p => p.Y));
        return new SvgAnnotation(name, row["kind"], text, row["value"], box,
            first is { } t ? new SheetPoint(t.X, t.Y) : null, first?.Height ?? 0, measures ?? string.Empty, [],
            textBox, first?.Angle ?? 0);
    }

    private static SheetRect? Rect(IReadOnlyDictionary<string, string> row)
        => row["left"].Length > 0
            ? new SheetRect(Meters(row["left"]), Meters(row["bottom"]), Meters(row["right"]), Meters(row["top"]))
            : null;

    private static double Meters(string mm) => double.Parse(mm, CultureInfo.InvariantCulture) * QuickArgs.Mm;

    /// <summary>
    /// 尺寸量的两头，如「E3 x=60.94 ↔ H5 (94.54,229.76)」：竖直边给 x、水平边给 y、圆心给 (x,y)、斜线写「斜线」；读不出的那头不写。
    /// 落在这个视图的某条直边、某个孔上的，前面写它在 SVG 里的编号（真机第一版只有坐标，孔标注指的是哪个孔要自己对坐标）。
    /// </summary>
    internal static string Measures(ViewDimension dimension, IReadOnlyList<SheetSegment>? lines = null, IReadOnlyList<HoleEdge>? holes = null)
    {
        return string.Join(" ↔ ", dimension.Anchors.Select(anchor => Id(anchor) + (anchor.X, anchor.Y) switch
        {
            ({ } x, { } y) => $"({DrawingSvgWriter.Mm(x)},{DrawingSvgWriter.Mm(y)})",
            ({ } x, null) => "x=" + DrawingSvgWriter.Mm(x),
            (null, { } y) => "y=" + DrawingSvgWriter.Mm(y),
            _ => "斜线",
        }));

        string Id(DimensionAnchor anchor)
        {
            if (anchor.Segment is { } segment && lines is not null)
            {
                for (var i = 0; i < lines.Count; i++)
                    if (DimensionGeometry.SameSegment(lines[i], segment))
                        return $"E{i} ";
            }

            if (anchor.Hole && anchor.X is { } x && anchor.Y is { } y && holes is not null)
            {
                foreach (var hole in holes)
                    if (DimensionGeometry.Same(hole.X, x) && DimensionGeometry.Same(hole.Y, y))
                        return $"H{hole.Index} ";
            }

            return string.Empty;
        }
    }

    /// <summary>存一份到模块数据目录的 snapshots（和截图同一处，只留最近 30 份 SVG）。</summary>
    private static string Save(QuickCommandContext context, string title, string svg)
    {
        var directory = Path.Combine(context.Options.DataDirectory ?? Path.GetTempPath(), DrawingSnapshot.Folder);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, $"{DrawingSnapshot.SafeName(title)}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.svg");
        File.WriteAllText(file, svg);
        DrawingSnapshot.Prune(directory, "*.svg");
        return file;
    }
}
