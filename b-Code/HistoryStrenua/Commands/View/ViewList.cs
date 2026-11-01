using System.Globalization;
using System.Runtime.InteropServices;
using HistoryStrenua.SolidWorks;
using static HistoryStrenua.ViewRows;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「视图列表」<c>strenua.view.list</c>（1.20.0）：当前图纸页本身与它上面每个视图一行——名字、种类、父视图、比例、外框、中心。
/// 是 <c>strenua.view.move</c> 与 <c>strenua.dim.*</c> 的寻址表：视图名就是它们的 <c>view=</c>。只读。
/// </summary>
internal static class ViewList
{
    public static QuickCommand Command { get; } = new(
        Key: "view-list",
        CommandName: StrenuaIdentity.Domain + ".view.list",
        Title: "视图列表",
        Summary: "列出当前图纸页及其视图：名字、种类、父视图、比例、外框与中心（图纸 mm，左下角为原点）；视图名供视图、标注类的「视图」填。",
        Usage: "不用填：列出当前图纸页本身（第一行，外框 = 纸张）和上面每个视图——名字、种类（模型 / 投影 / 剖视……，轴测图另标）、父视图、引用的模型、比例、外框与中心，单位图纸 mm、左下角为原点，和图纸截图对得上。视图名就是「挪视图」与标注类各按钮的「视图」框要填的。只读，不改图。",
        Run: QuickCommand.Unbound,
        Parameters: [],
        Readonly: true,
        Bind: _ => Run);

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var api = context.Api;
        var drawing = DrawingAccess.ActiveDrawing(context);
        var rows = new List<IReadOnlyDictionary<string, string>>();
        var lines = new List<string>();
        foreach (var view in SheetLookup.Views(api, drawing))
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var row = Row(context, drawing, view);
            rows.Add(row);
            lines.Add(Line(row));
        }

        var title = api.CallString(drawing, "IModelDoc2", "GetTitle");
        var frame = DrawingSheet.FrameRect(api, drawing) is { } box
            ? $"；图框内可放区 ({QuickArgs.Format(box.Left)}, {QuickArgs.Format(box.Bottom)})–({QuickArgs.Format(box.Right)}, {QuickArgs.Format(box.Top)})"
            : string.Empty;
        return QuickOutcome.Ok($"视图列表：「{title}」当前图纸页 {rows.Count - 1} 个视图（图纸 mm，左下角为原点{frame}）。"
            + Environment.NewLine + string.Join(Environment.NewLine, lines), rows);
    }
}
