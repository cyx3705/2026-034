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
        Usage: "「视图」填视图名只列这个视图，空着列整页（含图纸页本身上的注释）。每个尺寸 / 注解一行：名字、种类（线性 / 坐标 / 角度 / 半径 / 直径 / 倒角尺寸、孔标注、注释、中心符号线……）、图上显示的字、尺寸值、位置（文字中点）与外框，单位图纸 mm。名字就是「删标注」「挪标注」「改尺寸」的「名字」框要填的。只读，不改图。",
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

    private static QuickOutcome Run(QuickCommandContext context, string? viewName)
    {
        var api = context.Api;
        var drawing = HoleScan.ActiveDrawing(context);
        var views = viewName is null ? SheetLookup.Views(api, drawing) : [SheetLookup.View(api, drawing, viewName)];
        var rows = new List<IReadOnlyDictionary<string, string>>();
        var lines = new List<string>();
        foreach (var view in views)
        {
            var name = DrawingSheet.Name(api, view);
            var annotations = SheetLookup.Annotations(api, view);
            if (annotations.Count == 0 && viewName is null)
                continue;
            lines.Add($"视图「{name}」{annotations.Count} 个：");
            foreach (var (annotationName, annotation) in annotations)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                var row = Row(api, name, annotationName, annotation);
                rows.Add(row);
                lines.Add(Line(row));
            }
        }

        var scope = viewName is null ? "当前图纸页" : $"视图「{viewName}」";
        return QuickOutcome.Ok($"标注列表：{scope}共 {rows.Count} 个（图纸 mm）。" + Environment.NewLine + string.Join(Environment.NewLine, lines), rows);
    }

    private static Dictionary<string, string> Row(SolidWorksApi api, string view, string name, object annotation)
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
        return Flatten(string.Join(" ", Enumerable.Range(0, count).Select(i => api.CallString(data, "IDisplayData", "GetTextAtIndex", i))));
    }

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
        return "  · " + string.Join("  ", parts);
    }
}
