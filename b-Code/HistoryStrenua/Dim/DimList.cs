using System.Runtime.InteropServices;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「标注列表」<c>strenua.dim.list</c>（1.20.0）：一个视图（不给就整页，含图纸页本身上的注释）里每个注解一行——
/// 名字、种类、显示的字、尺寸值、位置、外框。名字就是 <c>dim.delete / move / edit</c> 的 <c>name(s)=</c>。只读。
/// </summary>
internal static class DimList
{
    public static QuickCommand Command { get; } = new(
        Key: "dim-list",
        CommandName: StrenuaIdentity.Domain + ".dim.list",
        Title: "标注列表",
        Summary: "列出视图（省略则整页）里的尺寸与注解：名字、种类、显示文字、尺寸值、位置与外框（图纸 mm）；名字供删、挪、改标注用。",
        Usage: "「视图」填视图名只列这个视图，空着列整页（含图纸页本身上的注释）。每个尺寸 / 注解一行：名字、种类（线性 / 坐标 / 角度 / 半径 / 直径 / 倒角尺寸、孔标注、注释、中心符号线……）、图上显示的字、尺寸值、位置（文字中点）与外框，单位图纸 mm。名字就是「删标注」「挪标注」「改尺寸」的「名字」框要填的。图纸页上标题栏 / 修改栏 / 图号框里的字、图框外的（分区字母、模板备用的技术要求）和空注释不列，只报个数（格子里的字见图纸 SVG 的 data-text）。只读，不改图。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("view", "视图", "视图名（视图列表的 name 列）；省略时列当前图纸页全部视图与图纸页本身的注解", false),
        ],
        Example: StrenuaIdentity.Domain + ".dim.list view=工程图视图1",
        Readonly: true,
        Bind: arguments =>
        {
            var view = QuickArgs.Optional(arguments, "view");
            return context => Run(context, view);
        });

    // swAnnotationType_e
    private const int AnnotationDimension = 4;
    private const int AnnotationNote = 6;
    private const int AnnotationCenterMark = 13;

    /// <summary>中心符号线连着哪些孔（图纸 SVG 的编号，同心的只取最先那个）：要删一组、拆一组时不用靠外框猜。</summary>
    private static string MarkedHoles(SolidWorksApi api, ScannedView scan, object annotation)
    {
        var ids = HoleCallout.AttachedCircleCenters(api, scan.Geometry, annotation)
            .Select(center => scan.Candidates.Select((hole, i) => (hole, i))
                .Where(item => HoleCalloutPlanner.SameCenter(center, new SheetPoint(item.hole.X, item.hole.Y)))
                .Select(item => (int?)item.i).FirstOrDefault())
            .OfType<int>().Distinct().Select(i => "H" + i);
        return string.Join(",", ids);
    }

    private static QuickOutcome Run(QuickCommandContext context, string? viewName)
    {
        var api = context.Api;
        var drawing = HoleScan.ActiveDrawing(context);
        var views = viewName is null ? SheetLookup.Views(api, drawing) : [SheetLookup.View(api, drawing, viewName)];
        var space = DrawingSvg.Space(api, drawing).Space;
        var rows = new List<IReadOnlyDictionary<string, string>>();
        var lines = new List<string>();
        var hidden = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var view in views)
        {
            var name = DrawingSheet.Name(api, view);
            var sheet = SheetLookup.IsSheet(api, view);
            var listed = new List<Dictionary<string, string>>();
            ScannedView? scan = null;
            foreach (var (annotationName, annotation) in SheetLookup.Annotations(api, view))
            {
                context.Cancellation.ThrowIfCancellationRequested();
                var row = Row(api, name, annotationName, annotation);
                if (!sheet && api.CallInt(annotation, "IAnnotation", "GetType") == AnnotationCenterMark)
                {
                    scan ??= HoleScan.Scan(context, "标注列表", view: view, quiet: true);
                    row["holes"] = MarkedHoles(api, scan, annotation);
                }

                // 1.24.0 真机：整页列表 80 多行里九成是标题栏格子、公差表数字、分区字母和模板备用的技术要求。
                if (sheet && SheetPlace(row, space) is { } place)
                {
                    hidden[place] = hidden.GetValueOrDefault(place) + 1;
                    continue;
                }

                listed.Add(row);
            }

            if (listed.Count == 0 && viewName is null)
                continue;
            lines.Add($"视图「{name}」{listed.Count} 个：");
            rows.AddRange(listed);
            lines.AddRange(listed.Select(Line));
        }

        var scope = viewName is null ? "当前图纸页" : $"视图「{viewName}」";
        var skipped = hidden.Count == 0
            ? string.Empty
            : $"（没列：{string.Join("、", hidden.Select(pair => $"{pair.Key} {pair.Value} 个"))}）";
        return QuickOutcome.Ok($"标注列表：{scope}共 {rows.Count} 个（图纸 mm）{skipped}。" + Environment.NewLine + string.Join(Environment.NewLine, lines), rows);
    }

    /// <summary>
    /// 图纸页上的注释默认不列的那几类（1.24.0）：没字的、落在标题栏等格子里的、图框外的；要列的回 null。
    /// 位置取外框中心，没外框取文字位置，都没有当空注释。
    /// </summary>
    internal static string? SheetPlace(IReadOnlyDictionary<string, string> row, SheetSpace space)
    {
        if (row["text"].Length == 0)
            return "空注释";
        SheetPoint? at = row["left"].Length > 0
            ? new SheetRect(Meters(row["left"]), Meters(row["bottom"]), Meters(row["right"]), Meters(row["top"])).Center
            : row["x"].Length > 0 ? new SheetPoint(Meters(row["x"]), Meters(row["y"])) : null;
        if (at is not { } c)
            return "空注释";
        if (space.KeepOuts.Any(rect => c.X >= rect.Left && c.X <= rect.Right && c.Y >= rect.Bottom && c.Y <= rect.Top))
            return "标题栏等格子里的";
        var frame = space.Frame;
        return c.X < frame.Left || c.X > frame.Right || c.Y < frame.Bottom || c.Y > frame.Top ? "图框外的" : null;
    }

    private static double Meters(string mm) => double.Parse(mm, System.Globalization.CultureInfo.InvariantCulture) * QuickArgs.Mm;

    internal static Dictionary<string, string> Row(SolidWorksApi api, string view, string name, object annotation)
    {
        var type = api.CallInt(annotation, "IAnnotation", "GetType");
        var row = new Dictionary<string, string>
        {
            ["view"] = view,
            ["name"] = name,
            ["kind"] = AnnotationCheckPlanner.KindName(type),
            ["text"] = string.Empty,
            ["value"] = string.Empty,
            ["x"] = string.Empty,
            ["y"] = string.Empty,
            ["left"] = string.Empty,
            ["bottom"] = string.Empty,
            ["right"] = string.Empty,
            ["top"] = string.Empty,
            ["holes"] = string.Empty,
        };
        try
        {
            var specific = api.Call(annotation, "IAnnotation", "GetSpecificAnnotation");
            if (type == AnnotationDimension && specific is not null)
            {
                var callout = api.CallBool(specific, "IDisplayDimension", "IsHoleCallout");
                var dimensionType = api.CallInt(specific, "IDisplayDimension", "get_Type2");
                row["kind"] = callout ? "孔标注" : DimensionKind(dimensionType);
                if (!callout && api.Call(specific, "IDisplayDimension", "GetDimension2", 0) is { } dimension)
                {
                    var value = Convert.ToDouble(api.Call(dimension, "IDimension", "get_SystemValue"));
                    row["value"] = dimensionType == AngularDimension
                        ? (value * 180 / Math.PI).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "°"
                        : QuickArgs.Format(value);
                }
            }

            row["text"] = type == AnnotationNote && specific is not null
                ? Flatten(api.CallString(specific, "INote", "GetText"))
                : Shown(api, annotation);
            if (SheetLookup.Position(api, annotation, type) is { } position)
            {
                row["x"] = QuickArgs.Format(position.X);
                row["y"] = QuickArgs.Format(position.Y);
            }

            if (DrawingSheet.Footprint(api, annotation) is { } rect)
            {
                row["left"] = QuickArgs.Format(rect.Left);
                row["bottom"] = QuickArgs.Format(rect.Bottom);
                row["right"] = QuickArgs.Format(rect.Right);
                row["top"] = QuickArgs.Format(rect.Top);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or System.Reflection.TargetException)
        {
            // 读到一半失效（悬空、被重建）：已读到的照出。
        }

        return row;
    }

    // swDimensionType_e
    private const int AngularDimension = 3;

    /// <summary>尺寸的细类（<c>IDisplayDimension.Type2</c>）。</summary>
    internal static string DimensionKind(int type) => type switch
    {
        1 or 7 or 8 => "坐标尺寸",
        2 or 11 or 12 => "线性尺寸",
        3 => "角度尺寸",
        4 => "弧长尺寸",
        5 => "半径尺寸",
        6 => "直径尺寸",
        10 => "倒角尺寸",
        _ => "尺寸",
    };

    /// <summary>图上显示的字（显示数据里各段字依次拼起来）；读不到为空。</summary>
    internal static string Shown(SolidWorksApi api, object annotation)
    {
        if (api.Call(annotation, "IAnnotation", "GetDisplayData") is not { } data)
            return string.Empty;
        var count = api.CallInt(data, "IDisplayData", "GetTextCount");
        return Flatten(Readable(string.Join(" ", Enumerable.Range(0, count).Select(i => api.CallString(data, "IDisplayData", "GetTextAtIndex", i)))));
    }

    /// <summary>
    /// SolidWorks 显示数据里孔标注的符号是代码（1.21.0 真机：「4 x &lt;MOD-DIAM&gt; 11 完全贯穿 &lt;HOLE-SPOT&gt; &lt;MOD-DIAM&gt; 18 &lt;HOLE-DEPTH&gt; 11」），
    /// 换成图上画出来的符号；不认得的代码原样留着。
    /// </summary>
    internal static string Readable(string text)
    {
        foreach (var (code, symbol) in Symbols)
            text = text.Replace(code, symbol, StringComparison.Ordinal);
        // 1.22.0 真机：显示数据各段之间、符号前后带空格，拼起来是「4 x  Ø  11」——同一行里的连续空格压成一个。
        return System.Text.RegularExpressions.Regex.Replace(text, "[ \t]{2,}", " ");
    }

    private static readonly (string Code, string Symbol)[] Symbols =
    [
        ("<MOD-DIAM>", "Ø"),
        ("<MOD-DEG>", "°"),
        ("<MOD-PM>", "±"),
        ("<HOLE-SPOT>", "⌴"),
        ("<HOLE-SINK>", "⌵"),
        ("<HOLE-DEPTH>", "↧"),
    ];

    /// <summary>多行字压成一行（回执一行一个注解），太长截断。</summary>
    internal static string Flatten(string text)
    {
        var flat = string.Join(" / ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return flat.Length <= 80 ? flat : flat[..80] + "…";
    }

    /// <summary>回执一行，如「· D3  线性尺寸  「25」 值 25  位置 (120, 210)」。</summary>
    internal static string Line(IReadOnlyDictionary<string, string> row)
    {
        var parts = new List<string> { row["name"], row["kind"] };
        if (row["text"].Length > 0)
            parts.Add("「" + row["text"] + "」");
        if (row["value"].Length > 0 && row["value"] != row["text"])
            parts.Add("值 " + row["value"]);
        if (row["x"].Length > 0)
            parts.Add($"位置 ({row["x"]}, {row["y"]})");
        else if (row["left"].Length > 0)
            parts.Add($"外框 ({row["left"]}, {row["bottom"]})–({row["right"]}, {row["top"]})");
        if (row.TryGetValue("holes", out var holes) && holes.Length > 0)
            parts.Add("孔 " + holes);
        return "  · " + string.Join("  ", parts);
    }
}
