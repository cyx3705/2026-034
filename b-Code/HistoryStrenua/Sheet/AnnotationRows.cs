using System.Globalization;
using System.Runtime.InteropServices;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>把注解读成一行（标注列表、SVG、未标尺寸共用）。</summary>
internal static class AnnotationRows
{
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

    /// <summary>多行字压成一行（回执一行一个注解），太长截断。</summary>
    internal static string Flatten(string text)
    {
        var flat = string.Join(" / ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return flat.Length <= 80 ? flat : flat[..80] + "…";
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

    /// <summary>图上显示的字（显示数据里各段字依次拼起来）；读不到为空。</summary>
    internal static string Shown(SolidWorksApi api, object annotation)
    {
        if (api.Call(annotation, "IAnnotation", "GetDisplayData") is not { } data)
            return string.Empty;
        var count = api.CallInt(data, "IDisplayData", "GetTextCount");
        return Flatten(Readable(string.Join(" ", Enumerable.Range(0, count).Select(i => api.CallString(data, "IDisplayData", "GetTextAtIndex", i)))));
    }

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
            if (type == SwConst.AnnotationDimension && specific is not null)
            {
                var callout = api.CallBool(specific, "IDisplayDimension", "IsHoleCallout");
                var dimensionType = api.CallInt(specific, "IDisplayDimension", "get_Type2");
                row["kind"] = callout ? "孔标注" : DimensionKind(dimensionType);
                if (!callout && api.Call(specific, "IDisplayDimension", "GetDimension2", 0) is { } dimension)
                {
                    var value = Convert.ToDouble(api.Call(dimension, "IDimension", "get_SystemValue"));
                    row["value"] = dimensionType == SwConst.AngularDimension
                        ? (value * 180 / Math.PI).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "°"
                        : QuickArgs.Format(value);
                }
            }

            row["text"] = type == SwConst.AnnotationNote && specific is not null
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

    /// <summary>注解类名（回执用）：尺寸里再分出孔标注。</summary>
    internal static string Kind(SolidWorksApi api, object annotation, int type)
        => type == SwConst.AnnotationDimension
            && api.Call(annotation, "IAnnotation", "GetSpecificAnnotation") is { } display
            && api.CallBool(display, "IDisplayDimension", "IsHoleCallout")
                ? AnnotationCheckPlanner.CalloutKind
                : AnnotationCheckPlanner.KindName(type);

    internal static double Meters(string mm) => double.Parse(mm, System.Globalization.CultureInfo.InvariantCulture) * QuickArgs.Mm;

    internal static readonly (string Code, string Symbol)[] Symbols =
    [
        ("<MOD-DIAM>", "Ø"),
        ("<MOD-DEG>", "°"),
        ("<MOD-PM>", "±"),
        ("<HOLE-SPOT>", "⌴"),
        ("<HOLE-SINK>", "⌵"),
        ("<HOLE-DEPTH>", "↧"),
    ];
}
