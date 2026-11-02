using System.Globalization;
using HistoryStrenua.SolidWorks;
using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「加尺寸」<c>strenua.dim.add</c>（1.25.0）：按图纸 SVG 的编号在两头之间加一个水平 / 竖直尺寸，读回值和几何算的对不上就删掉报失败。
/// 1.31.0（用户定）：必须给两头，不再量一条线自身——「这条线没标」不等于要标它的长度（真机夹爪2 的 21.62），要定的是它的端点离基准多远。
/// </summary>
internal static class DimAdd
{
    public static QuickCommand Command { get; } = new(
        Key: "dim-add",
        CommandName: StrenuaIdentity.Domain + ".dim.add",
        Title: "加尺寸",
        Summary: "按图纸 SVG 的编号在两头之间（线、线的端点、孔、圆弧圆心）加一个水平或竖直尺寸，读回值与几何核对，对不上删掉报失败。",
        Usage: "先用 strenua.check.svg view=视图名 查编号。「从」「到」都要填，各是直边 E3（线到线）、直边端点 E3.1 / E3.2（SVG 里 x1,y1 是 .1，x2,y2 是 .2）孔 H5（圆心）、圆弧 C3（圆心，SVG 里带 data-center 的才行）或曲线端点 C3.1 / C3.2（折线首尾）。斜边只能取端点。两头不能在同一条边上：不量线自身长度，要定一条线的端点就从基准或已标的线量到那个端点。方向 h 水平、v 竖直，X、Y 是文字位置（图纸 mm）。不看避障开关，说放哪放哪。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            QuickArgs.View("视图名（视图列表的 name 列）"),
            new QuickParameter("from", "从", "一头：直边 E3、直边端点 E3.1、孔 H5、圆弧圆心 C3 或曲线端点 C3.1", true),
            new QuickParameter("to", "到", "另一头：直边 E7、直边端点 E7.2、孔 H5、圆弧圆心 C3 或曲线端点 C3.2；不能和「从」在同一条边上", true),
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
        var to = GeometryRef.Parse(QuickArgs.Name(arguments, "to"), "to");
        var horizontal = Axis(QuickArgs.Name(arguments, "axis"));
        var x = QuickArgs.Millimeters(arguments.GetString("x"), "x") ?? throw new QuickArgumentException("缺参数 x=（文字位置，图纸 mm）");
        var y = QuickArgs.Millimeters(arguments.GetString("y"), "y") ?? throw new QuickArgumentException("缺参数 y=（文字位置，图纸 mm）");
        if (SameEdge(from, to))
            throw new QuickArgumentException($"from={from} 和 to={to} 在同一条边上：不量线自身长度。要定它的端点，就从基准或已标的线量到那个端点（如 from=基准边 to={(to.Curve ? "C" : "E")}{to.Index}.2）。");
        var prefix = QuickArgs.Optional(arguments, "prefix");
        return context => Run(context, view, from, to, horizontal, new SheetPoint(x * QuickArgs.Mm, y * QuickArgs.Mm), prefix);
    }

    /// <summary>两头是不是同一条直边（含它的端点）。</summary>
    internal static bool SameEdge(GeometryRef from, GeometryRef to) => !from.Hole && !to.Hole && from.Curve == to.Curve && from.Index == to.Index;

    internal static bool Axis(string text) => text.Trim().ToLowerInvariant() switch
    {
        "h" or "水平" => true,
        "v" or "竖直" => false,
        _ => throw new QuickArgumentException($"axis={text} 写不对：h 水平、v 竖直。"),
    };

    private static QuickOutcome Run(QuickCommandContext context, string viewName, GeometryRef from, GeometryRef to, bool horizontal, SheetPoint at, string? prefix)
    {
        var api = context.Api;
        var drawing = DrawingAccess.ActiveDrawing(context);
        var view = SheetLookup.View(api, drawing, viewName);
        context.SetState("加尺寸");
        var scan = HoleScan.Scan(context, "加尺寸", withLines: true, view: view, quiet: true);
        var scale = Convert.ToDouble(api.Call(view, "IView", "get_ScaleDecimal"), CultureInfo.InvariantCulture);
        var label = $"{from} ↔ {to}";
        if (Expected(scan, from, to, horizontal, scale) is not { } expected)
            return QuickOutcome.Fail($"加尺寸：{label} 量不出{(horizontal ? "水平" : "竖直")}距离（编号不存在、直边和量的方向不垂直要改取端点、曲线不是正对图纸的圆弧没有圆心，或两头在这个方向上重合）。");

        _ = api.Call(drawing, "IDrawingDoc", "ActivateView", viewName);
        api.ClearSelection(drawing);
        var selected = DimensionEdit.SelectGeometry(api, scan, from, false) && DimensionEdit.SelectGeometry(api, scan, to, true);
        var created = selected
            ? api.Call(drawing, "IModelDoc2", horizontal ? "AddHorizontalDimension2" : "AddVerticalDimension2", at.X, at.Y, 0.0)
            : null;
        api.ClearSelection(drawing);
        if (created is null)
            return QuickOutcome.Fail($"加尺寸：SolidWorks 没有接受 {label}。");

        var actual = api.Call(created, "IDisplayDimension", "GetDimension2", 0) is { } dimension
            ? Convert.ToDouble(api.Call(dimension, "IDimension", "get_SystemValue"), CultureInfo.InvariantCulture)
            : double.NaN;
        if (!(Math.Abs(actual - expected) < ValueTolerance))
        {
            DimensionEdit.Discard(api, scan, created);
            return QuickOutcome.Fail($"加尺寸：{label} 建出来是 {QuickArgs.Format(actual)}，几何算的是 {QuickArgs.Format(expected)}，已删掉。");
        }

        if (prefix is not null)
            api.Call(created, "IDisplayDimension", "SetText", SwConst.TextPrefix, prefix);
        api.Redraw(drawing);
        var name = api.Call(created, "IDisplayDimension", "GetAnnotation") is { } annotation ? api.CallString(annotation, "IAnnotation", "GetName") : "?";
        return QuickOutcome.Ok($"加尺寸：视图「{viewName}」{label} 加了「{name}」= {QuickArgs.Format(actual)}，文字在 {QuickArgs.Format(at)}。");
    }

    private const double ValueTolerance = 0.00002;

    /// <summary>几何算出的模型距离（米）；编号不存在或方向不对回 null。</summary>
    private static double? Expected(ScannedView scan, GeometryRef from, GeometryRef to, bool horizontal, double scale)
        => Coordinate(scan, from, horizontal) is { } a && Coordinate(scan, to, horizontal) is { } b && !DimensionGeometry.Same(a, b) ? Math.Abs(a - b) / scale : null;

    private static double? Coordinate(ScannedView scan, GeometryRef item, bool horizontal)
    {
        if (item.Hole)
            return item.Index < scan.Candidates.Count ? (horizontal ? scan.Candidates[item.Index].X : scan.Candidates[item.Index].Y) : null;
        if (item.Curve)
            return scan.CurveShapes is { } shapes && item.Index < shapes.Count ? CurveCoordinate(shapes[item.Index], item.End, horizontal) : null;
        if (item.Index >= scan.Lines.Count)
            return null;
        var line = scan.Lines[item.Index];
        return item.End switch
        {
            1 => horizontal ? line.X1 : line.Y1,
            2 => horizontal ? line.X2 : line.Y2,
            _ => GeometryRef.Coordinate(line, horizontal),
        };
    }

    /// <summary>曲线的一头：不带端点号是圆心（只有正对图纸的圆弧有），带端点号是折线首尾。</summary>
    internal static double? CurveCoordinate(SheetCurve curve, int end, bool horizontal)
        => (end == 0 ? curve.Center : curve.EndPoint(end)) is { } point ? (horizontal ? point.X : point.Y) : null;
}
