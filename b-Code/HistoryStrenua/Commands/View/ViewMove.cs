using HistoryStrenua.SolidWorks;
using static HistoryStrenua.SheetLookup;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「挪视图」<c>strenua.view.move</c>（1.20.0，用户定：要能单独挪一个视图）：按名字挪一个视图，挪到 x/y 或挪动 dx/dy（图纸 mm）。
/// </summary>
/// <remarks>
/// <para><c>IView.Position</c> 就是外框中心（同排版）。已标的尺寸属于视图，跟着走；与它对齐的子视图 SolidWorks 也带着挪。</para>
/// <para>与父视图对齐的投影视图只能沿对齐方向动：SolidWorks 不报错，只是那个方向不动。所以挪完读回位置，没到的方向在回执里说、指令失败；
/// <c>unalign=是</c> 先解除对齐（<c>IView.RemoveAlignment</c>）再挪。压到别的视图、出图框只在回执里说，不拦——AI 说放哪就放哪。</para>
/// </remarks>
internal static class ViewMove
{
    public static QuickCommand Command { get; } = new(
        Key: "view-move",
        CommandName: StrenuaIdentity.Domain + ".view.move",
        Title: "挪视图",
        Summary: "按名字挪当前图纸页的一个视图：挪到 x/y 或挪动 dx/dy（图纸 mm，视图中心）；尺寸跟着走，对齐锁住的方向读回后报告。",
        Usage: "填「视图」（视图列表里的名字），再填 X / Y（挪到哪，视图中心，图纸 mm，可只填一个）或 右移 / 上移（挪多少 mm，负数反向），两种只能用一种。已标的尺寸跟着视图走，对齐的子视图 SolidWorks 也带着挪。与父视图对齐的投影视图只能沿对齐方向动：挪完读回，没到的方向回执失败并说明；「解除对齐」选「是」先解除对齐再挪。压到别的视图、出图框只在回执里说，不拦，也不看避障开关。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            QuickArgs.View("视图名（视图列表的 name 列），如 工程图视图1"),
            QuickArgs.X("挪到：视图中心的 X（图纸 mm，左下角为原点）；只给 x 则 Y 不动。不能和 dx/dy 同用"),
            QuickArgs.Y("挪到：视图中心的 Y（图纸 mm）；只给 y 则 X 不动"),
            QuickArgs.Dx("挪动：向右为正（mm），如 -15"),
            QuickArgs.Dy("挪动：向上为正（mm）"),
            new QuickParameter("unalign", "解除对齐", "是 / true 先解除与父视图的对齐再挪（对齐的投影视图否则只能沿对齐方向动）；省略为否", false, 1, ["否", "是"]),
        ],
        Example: StrenuaIdentity.Domain + ".view.move view=工程图视图1 dx=10",
        Bind: Bind);

    private static Func<QuickCommandContext, QuickOutcome> Bind(HistoryVulcan.Core.Commands.CommandContext arguments)
    {
        var name = QuickArgs.Name(arguments, "view");
        var request = QuickArgs.Move(arguments);
        var unalign = QuickArgs.Flag(arguments, "unalign");
        return context => Run(context, name, request, unalign);
    }

    private static QuickOutcome Run(QuickCommandContext context, string name, MoveRequest request, bool unalign)
    {
        var api = context.Api;
        var drawing = DrawingAccess.ActiveDrawing(context);
        var view = SheetLookup.View(api, drawing, name);
        if (SheetLookup.IsSheet(api, view))
            throw new QuickCommandException($"「{name}」是图纸页本身，不能挪。");

        context.SetState("挪视图");
        var others = DrawingAccess.SheetViews(api, drawing).Where(other => DrawingSheet.Name(api, other) != name).ToList();
        var before = others.ToDictionary(other => DrawingSheet.Name(api, other), other => Position(api, other));
        var from = Position(api, view);
        var target = QuickArgs.Target(request, from);
        var notes = new List<string>();
        if (unalign)
        {
            api.Call(view, "IView", "RemoveAlignment");
            notes.Add("已解除对齐");
        }

        api.ClearSelection(drawing);
        DrawingSheet.SetPosition(api, view, target);
        api.Redraw(drawing);
        var actual = Position(api, view);

        var missed = Missed(target, actual);
        if (missed.Count > 0)
            notes.Add($"{string.Join("、", missed)}方向没挪到（读回 {QuickArgs.Format(actual)}，要 {QuickArgs.Format(target)}）——多半是与父视图对齐锁住了，要挪就把「解除对齐」选是（unalign=是）");
        var followed = others
            .Where(other => Missed(before[DrawingSheet.Name(api, other)], Position(api, other)).Count > 0)
            .Select(other => "「" + DrawingSheet.Name(api, other) + "」")
            .ToList();
        if (followed.Count > 0)
            notes.Add($"对齐的 {string.Join("、", followed)} 跟着挪了");
        var outline = DrawingSheet.Outline(api, view);
        var overlapped = others
            .Where(other => DrawingSheet.Outline(api, other).Overlaps(outline))
            .Select(other => "「" + DrawingSheet.Name(api, other) + "」")
            .ToList();
        if (overlapped.Count > 0)
            notes.Add($"外框压到 {string.Join("、", overlapped)}");
        if (DrawingSheet.FrameRect(api, drawing) is { } frame && !outline.Within(frame))
            notes.Add("外框出了图框");

        var message = $"挪视图：「{name}」中心 {QuickArgs.Format(from)} → {QuickArgs.Format(actual)}。"
            + string.Concat(notes.Select(note => Environment.NewLine + "· " + note + "。"));
        return missed.Count == 0 ? QuickOutcome.Ok(message) : QuickOutcome.Fail(message);
    }
}
