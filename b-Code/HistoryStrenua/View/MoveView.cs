using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 细粒度指令「挪视图」<c>strenua.view.move</c>（1.20.0，用户定：要能单独挪一个视图）：按名字挪一个视图，挪到 x/y 或挪动 dx/dy（图纸 mm）。
/// </summary>
/// <remarks>
/// <para><c>IView.Position</c> 就是外框中心（同排版）。已标的尺寸属于视图，跟着走；与它对齐的子视图 SolidWorks 也带着挪。</para>
/// <para>与父视图对齐的投影视图只能沿对齐方向动：SolidWorks 不报错，只是那个方向不动。所以挪完读回位置，没到的方向在回执里说、指令失败；
/// <c>unalign=true</c> 先解除对齐（<c>IView.RemoveAlignment</c>）再挪。压到别的视图、出图框只在回执里说，不拦——AI 说放哪就放哪。</para>
/// </remarks>
internal static class MoveView
{
    public static FineCommand Command { get; } = new(
        Key: "view-move",
        CommandName: StrenuaIdentity.Domain + ".view.move",
        Title: "挪视图",
        Summary: "按名字挪当前图纸页的一个视图：挪到 x/y 或挪动 dx/dy（图纸 mm，视图中心）；尺寸跟着走，对齐锁住的方向读回后报告。",
        Example: StrenuaIdentity.Domain + ".view.move name=工程图视图1 dx=10",
        Parameters:
        [
            FineArgs.Text("name", "视图名（strenua.view.list 的 name 列），如 工程图视图1", true, 0),
            FineArgs.Text("x", "挪到：视图中心的 X（图纸 mm，左下角为原点）；只给 x 则 Y 不动。不能和 dx/dy 同用", false, 1),
            FineArgs.Text("y", "挪到：视图中心的 Y（图纸 mm）；只给 y 则 X 不动", false, 2),
            FineArgs.Text("dx", "挪动：向右为正（mm），如 -15", false, 3),
            FineArgs.Text("dy", "挪动：向上为正（mm）", false, 4),
            FineArgs.Text("unalign", "true 先解除与父视图的对齐再挪（对齐的投影视图否则只能沿对齐方向动）；省略为 false", false, 5, ["true", "false"]),
        ],
        Readonly: false,
        Bind: Bind);

    /// <summary>到没到的判定容差（0.01 mm）。</summary>
    public const double Tolerance = 1e-5;

    private static Func<QuickCommandContext, QuickOutcome> Bind(HistoryVulcan.Core.Commands.CommandContext arguments)
    {
        var name = FineArgs.Name(arguments, "name");
        var request = FineArgs.Move(arguments);
        var unalign = arguments.GetString("unalign")?.Trim() is { Length: > 0 } text
            ? bool.TryParse(text, out var value) ? value : throw new FineArgumentException($"unalign 只能是 true 或 false：{text}")
            : false;
        return context => Run(context, name, request, unalign);
    }

    private static QuickOutcome Run(QuickCommandContext context, string name, MoveRequest request, bool unalign)
    {
        var api = context.Api;
        var drawing = HoleScan.ActiveDrawing(context);
        var view = FineLookup.View(api, drawing, name);
        if (FineLookup.IsSheet(api, view))
            throw new QuickCommandException($"「{name}」是图纸页本身，不能挪。");

        context.SetState("挪视图");
        var others = HoleScan.SheetViews(api, drawing).Where(other => DrawingSheet.Name(api, other) != name).ToList();
        var before = others.ToDictionary(other => DrawingSheet.Name(api, other), other => Position(api, other));
        var from = Position(api, view);
        var target = FineArgs.Target(request, from);
        var notes = new List<string>();
        if (unalign)
        {
            api.Call(view, "IView", "RemoveAlignment");
            notes.Add("已解除对齐");
        }

        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        DrawingSheet.SetPosition(api, view, target);
        api.Call(drawing, "IModelDoc2", "GraphicsRedraw2");
        var actual = Position(api, view);

        var missed = Missed(target, actual);
        if (missed.Count > 0)
            notes.Add($"{string.Join("、", missed)}方向没挪到（读回 {FineArgs.Format(actual)}，要 {FineArgs.Format(target)}）——多半是与父视图对齐锁住了，要挪就加 unalign=true");
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

        var message = $"挪视图：「{name}」中心 {FineArgs.Format(from)} → {FineArgs.Format(actual)}。"
            + string.Concat(notes.Select(note => Environment.NewLine + "· " + note + "。"));
        return missed.Count == 0 ? QuickOutcome.Ok(message) : QuickOutcome.Fail(message);
    }

    /// <summary>视图中心（<c>IView.Position</c>，图纸坐标，米）。</summary>
    public static SheetPoint Position(SolidWorksApi api, object view)
    {
        var position = api.CallDoubles(view, "IView", "get_Position");
        return position.Length >= 2 ? new SheetPoint(position[0], position[1]) : default;
    }

    /// <summary>没到 <paramref name="target"/> 的方向（「X」「Y」），差不到 <see cref="Tolerance"/> 算到了。</summary>
    internal static List<string> Missed(SheetPoint target, SheetPoint actual)
    {
        var missed = new List<string>();
        if (Math.Abs(target.X - actual.X) > Tolerance)
            missed.Add("X");
        if (Math.Abs(target.Y - actual.Y) > Tolerance)
            missed.Add("Y");
        return missed;
    }
}
