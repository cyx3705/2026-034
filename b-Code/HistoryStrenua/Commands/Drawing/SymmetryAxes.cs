using HistoryStrenua.SolidWorks;
using static HistoryStrenua.CenterLineEdit;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「对称轴」（1.14.1，出图类，用户定为单独的全图指令）：当前图纸页全部视图（轴测图跳过）里，边和孔整个关于一根轴对称的，
/// 插上对称轴（中心线）；已经有落在轴上的中心线就不再加。
/// </summary>
/// <remarks>
/// 判法见 <see cref="SymmetryPlanner"/>（要有孔不在轴上，或有要定位的圆弧圆心（1.14.2）；孔全在轴上、又没有这样的圆弧的视图不加——每个矩形都对称，全加上就满图中心线）。
/// 「孔位尺寸」以对称轴为基准时也调这里的 <see cref="Insert"/>，补上它要的那根，所以单按孔类指令不会缺轴。
/// </remarks>
internal static class SymmetryAxes
{
    public static QuickCommand Command { get; } = new(
        Key: "symmetry-axes",
        CommandName: StrenuaIdentity.Domain + ".drawing.symmetry",
        Title: "对称轴",
        Summary: "当前图纸页全部视图（轴测图跳过）：边和孔整个左右或上下对称、且有孔不在轴上或有要定位的圆弧圆心的，加对称轴（中心线），已有的不再加。",
        Usage: "不用点视图：当前图纸页上的全部视图（轴测图跳过）逐个看，视图里的边和孔整个左右（或上下）对称、且有孔不在对称轴上（或有要定位的圆弧圆心：两端不都与直线相切、不与孔同心的圆弧）的，选一对对称的边插一根对称轴（中心线，两头伸出零件一点）；已经有落在轴上的中心线不再加。孔全在轴上又没有这样的圆弧、或同位置是别种孔的视图不加。「圆心位置」也会自己补它要的那根。「孔位尺寸」遇到对称的视图也会自己补上它要的那根并以它为基准标。某个视图没成不中断，最后汇总。",
        Run: Run,
        Row: 2, Page: "onekey");

    internal static QuickOutcome Run(QuickCommandContext context) => ViewFlow.EachView(context, "对称轴", RunView);

    private static QuickOutcome RunView(QuickCommandContext context, object? view)
    {
        var api = context.Api;
        var scan = HoleScan.Scan(context, "对称轴", withLines: true, view: view, withArcs: true);
        var holes = HoleCalloutPlanner.Recognize(scan.Candidates);
        var axes = SymmetryPlanner.Axes(scan.Lines, scan.CurveSegments, holes, ArcCenterPlanner.Locatable(scan.Arcs, scan.Lines, holes));
        if (axes.Count == 0)
            return QuickOutcome.Nothing($"视图「{scan.ViewName}」不对称（或孔都在轴上、没有要定位的圆弧），没有加对称轴。");
        var names = string.Join("、", axes.Select(axis => axis.Name));
        var (added, failed) = Insert(api, scan, axes);
        api.Redraw(scan.Document);
        var message = $"视图「{scan.ViewName}」：{names}"
            + (added > 0 ? $"，加了 {added} 根" : string.Empty)
            + (axes.Count - added - failed > 0 ? $"，{axes.Count - added - failed} 根已有" : string.Empty)
            + (failed > 0 ? $"，{failed} 根 SolidWorks 未生成贯穿视图的完整对称轴，已删除" : string.Empty) + "。";
        return failed > 0 && added == 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }
}
