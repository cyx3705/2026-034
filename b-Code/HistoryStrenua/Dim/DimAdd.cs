using System.Globalization;
using HistoryStrenua.SolidWorks;
using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「加尺寸」<c>strenua.dim.add</c>（1.25.0）：按图纸 SVG 的编号加一个水平 / 竖直尺寸，读回值和几何算的对不上就删掉报失败。
/// </summary>
internal static class DimAdd
{
    public static QuickCommand Command { get; } = new(
        Key: "dim-add",
        CommandName: StrenuaIdentity.Domain + ".dim.add",
        Title: "加尺寸",
        Summary: "按图纸 SVG 的编号（E3、H5）加一个水平或竖直尺寸到指定位置，读回值与几何核对，对不上删掉报失败。",
        Usage: "先用 strenua.check.svg view=视图名 查编号。「从」「到」填直边或孔（到可空：量一条直边自身的跨度），方向 h 水平、v 竖直，X、Y 是文字位置（图纸 mm）。不看避障开关，说放哪放哪。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            QuickArgs.View("视图名（视图列表的 name 列）"),
            new QuickParameter("from", "从", "一头：直边 E3 或孔 H5", true),
            new QuickParameter("to", "到", "另一头：直边或孔；省略时量「从」那条直边自身的跨度", false),
            new QuickParameter("axis", "方向", "h 水平、v 竖直", true),
            QuickArgs.X("文字位置 X（图纸 mm）"),
            QuickArgs.Y("文字位置 Y（图纸 mm）"),
            new QuickParameter("prefix", "前缀", "前缀文字，如 2 x C；省略不写", false, 2),
        ],
        Example: StrenuaIdentity.Domain + ".dim.add view=工程图视图1 from=E1 to=H26 axis=h x=120 y=60",
        Bind: Bind,
        SelfReport: true);

    private static Func<QuickCommandContext, QuickOutcome> Bind(CommandContext arguments)
    {
        var view = QuickArgs.Name(arguments, "view");
        var from = GeometryRef.Parse(QuickArgs.Name(arguments, "from"), "from");
        var to = QuickArgs.Optional(arguments, "to") is { } text ? GeometryRef.Parse(text, "to") : (GeometryRef?)null;
        var horizontal = Axis(QuickArgs.Name(arguments, "axis"));
        var x = QuickArgs.Millimeters(arguments.GetString("x"), "x") ?? throw new QuickArgumentException("缺参数 x=（文字位置，图纸 mm）");
        var y = QuickArgs.Millimeters(arguments.GetString("y"), "y") ?? throw new QuickArgumentException("缺参数 y=（文字位置，图纸 mm）");
        if (to is null && from.Hole)
            throw new QuickArgumentException("to= 省略时 from= 只能是直边（量它自身的跨度）。");
        var prefix = QuickArgs.Optional(arguments, "prefix");
        return context => Run(context, view, from, to, horizontal, new SheetPoint(x * QuickArgs.Mm, y * QuickArgs.Mm), prefix);
    }

    internal static bool Axis(string text) => text.Trim().ToLowerInvariant() switch
    {
        "h" or "水平" => true,
        "v" or "竖直" => false,
        _ => throw new QuickArgumentException($"axis={text} 写不对：h 水平、v 竖直。"),
    };

    private static QuickOutcome Run(QuickCommandContext context, string viewName, GeometryRef from, GeometryRef? to, bool horizontal, SheetPoint at, string? prefix)
    {
        var api = context.Api;
        var drawing = HoleScan.ActiveDrawing(context);
        var view = SheetLookup.View(api, drawing, viewName);
        context.SetState("加尺寸");
        var scan = HoleScan.Scan(context, "加尺寸", withLines: true, view: view, quiet: true);
        var scale = Convert.ToDouble(api.Call(view, "IView", "get_ScaleDecimal"), CultureInfo.InvariantCulture);
        var label = to is { } other ? $"{from} ↔ {other}" : from.ToString();
        if (Expected(scan, from, to, horizontal, scale) is not { } expected)
            return QuickOutcome.Fail($"加尺寸：{label} 量不出{(horizontal ? "水平" : "竖直")}距离（编号不存在，或直边和量的方向不垂直）。");

        _ = api.Call(drawing, "IDrawingDoc", "ActivateView", viewName);
        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        var selected = Select(api, scan, from, false) && (to is not { } end || Select(api, scan, end, true));
        var created = selected
            ? api.Call(drawing, "IModelDoc2", horizontal ? "AddHorizontalDimension2" : "AddVerticalDimension2", at.X, at.Y, 0.0)
            : null;
        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        if (created is null)
            return QuickOutcome.Fail($"加尺寸：SolidWorks 没有接受 {label}。");

        var actual = api.Call(created, "IDisplayDimension", "GetDimension2", 0) is { } dimension
            ? Convert.ToDouble(api.Call(dimension, "IDimension", "get_SystemValue"), CultureInfo.InvariantCulture)
            : double.NaN;
        if (!(Math.Abs(actual - expected) < ValueTolerance))
        {
            HolePosition.Discard(api, scan, created);
            return QuickOutcome.Fail($"加尺寸：{label} 建出来是 {QuickArgs.Format(actual)}，几何算的是 {QuickArgs.Format(expected)}，已删掉。");
        }

        if (prefix is not null)
            api.Call(created, "IDisplayDimension", "SetText", DimensionScan.TextPrefix, prefix);
        api.Call(drawing, "IModelDoc2", "GraphicsRedraw2");
        var name = api.Call(created, "IDisplayDimension", "GetAnnotation") is { } annotation ? api.CallString(annotation, "IAnnotation", "GetName") : "?";
        return QuickOutcome.Ok($"加尺寸：视图「{viewName}」{label} 加了「{name}」= {QuickArgs.Format(actual)}，文字在 {QuickArgs.Format(at)}。");
    }

    private const double ValueTolerance = 0.00002;

    private static bool Select(SolidWorksApi api, ScannedView scan, GeometryRef item, bool append)
        => HolePosition.SelectEdge(api, scan, item.Hole ? scan.Edges[item.Index] : scan.LineEdges[item.Index], append);

    /// <summary>几何算出的模型距离（米）；编号不存在或方向不对回 null。</summary>
    private static double? Expected(ScannedView scan, GeometryRef from, GeometryRef? to, bool horizontal, double scale)
    {
        if (to is { } end)
            return Coordinate(scan, from, horizontal) is { } a && Coordinate(scan, end, horizontal) is { } b ? Math.Abs(a - b) / scale : null;
        if (from.Index >= scan.Lines.Count)
            return null;
        var line = scan.Lines[from.Index];
        return Math.Abs(horizontal ? line.X1 - line.X2 : line.Y1 - line.Y2) / scale;
    }

    private static double? Coordinate(ScannedView scan, GeometryRef item, bool horizontal)
    {
        if (item.Hole)
            return item.Index < scan.Candidates.Count ? (horizontal ? scan.Candidates[item.Index].X : scan.Candidates[item.Index].Y) : null;
        return item.Index < scan.Lines.Count ? GeometryRef.Coordinate(scan.Lines[item.Index], horizontal) : null;
    }
}
