namespace HistoryStrenua;

/// <summary>
/// 快捷指令「投影视图」（1.9.0，出图类，从「新建工程图」拆出）：当前工程图里从主视图（或选中的视图）投影出看侧面孔、窗口或厚度的视图，
/// 已经有视图看着的方向不再加；新视图贴着主视图放，别的不动。
/// </summary>
/// <remarks>
/// 要哪几个、摆哪边见 <see cref="DrawingPlanner.SideViews"/>；「可换边」的按当前图幅与比例挑放得下的那边（<see cref="DrawingPlanner.ChooseSlots"/>），
/// 与「新建工程图」选图幅时的估计一致。离主视图多远见 <see cref="DrawingPlanner.Beside"/>。整页重排是「排版」的事。
/// </remarks>
internal static class DrawingProject
{
    public static QuickCommand Command { get; } = new(
        Key: "drawing-project",
        CommandName: StrenuaIdentity.Domain + ".drawing.project",
        Title: "投影视图",
        Summary: "当前工程图：从主视图（或选中的视图）投影出看侧面孔、窗口或厚度的视图，已有的方向不再加，贴着主视图放。",
        Usage: "在工程图里按（选了视图就从它投影，没选就从主视图）：侧面沿图纸横向有孔或窗口就加左 / 右视图、沿竖向有就加上 / 下视图，摆哪边看孔口朝哪边多（照图纸的第一 / 第三角投影）；都没有就只加一个看厚度的（只有圆弧面时加在圆弧面多的方向）。那个方向已经有视图看着的不再加。新视图贴着主视图放、给尺寸留出地方，别的视图不动；整页重排按「排版」。",
        Run: Run);

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var api = context.Api;
        var drawing = HoleScan.ActiveDrawing(context);
        var selected = HoleScan.PreselectedView(api, drawing);
        if (selected is not null && (api.Call(selected, "IView", "get_ReferencedDocument") is null || HoleScan.Frame(context, selected).Axonometric))
            selected = null;
        return Run(context, DrawingSheet.Read(context, drawing, "投影视图", selected));
    }

    internal static QuickOutcome Run(QuickCommandContext context, DrawingSheet sheet)
    {
        var api = context.Api;
        var planned = DrawingPlanner.SideViews(sheet.Part, sheet.MainFrame, sheet.FirstAngle);
        var slots = DrawingPlanner.ChooseSlots(sheet.Part, DrawingPlanner.ViewOf(sheet.MainFrame), planned,
            SheetSpace.Standard(sheet.Width, sheet.Height), sheet.Scale, context.Options.Chain);
        var existing = new Dictionary<ViewSlot, object>(sheet.Sides);
        var lines = new List<string>();
        var added = new List<object>();
        var failed = 0;
        context.SetState("加投影视图");
        for (var i = 0; i < planned.Count; i++)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var (_, reason, alternative) = planned[i];
            var slot = slots[i];
            // 那个方向（可换边的两个方向都算）已经有视图看着就不再加：横向的孔左视图、右视图都看得到。
            var directions = new[] { planned[i].Slot, alternative ?? planned[i].Slot }.Select(DrawingPlanner.Horizontal).ToHashSet();
            if (existing.FirstOrDefault(pair => directions.Contains(DrawingPlanner.Horizontal(pair.Key))) is { Value: not null } seen)
            {
                lines.Add($"· {reason}：{DrawingSheet.SlotName(seen.Key)}已有「{DrawingSheet.Name(api, seen.Value)}」，不再加。");
                continue;
            }

            var at = DrawingSheet.Outline(api, sheet.Main);
            var (x, y) = slot switch
            {
                ViewSlot.Right => (at.Right + 0.05, at.Center.Y),
                ViewSlot.Left => (at.Left - 0.05, at.Center.Y),
                ViewSlot.Below => (at.Center.X, at.Bottom - 0.04),
                _ => (at.Center.X, at.Top + 0.04),
            };
            context.Report($"投影视图：{DrawingSheet.SlotName(slot)}加一个（{reason}）。");
            if (DrawingSheet.Unfold(api, sheet.Drawing, sheet.MainName, x, y) is not { } view)
            {
                failed++;
                lines.Add($"· {reason}：SolidWorks 没有投影出{DrawingSheet.SlotName(slot)}的视图。");
                continue;
            }

            var box = sheet.Box(context, view);
            if (box.Width > 0)
                DrawingSheet.SetPosition(api, view, DrawingPlanner.Beside(at, sheet.Box(context, sheet.Main), slot, box));
            existing[slot] = view;
            added.Add(view);
            lines.Add($"· {DrawingSheet.SlotName(slot)}「{DrawingSheet.Name(api, view)}」：{reason}。");
        }

        api.Call(sheet.Drawing, "IModelDoc2", "EditRebuild3");
        api.Call(sheet.Drawing, "IModelDoc2", "ClearSelection2", true);
        var space = sheet.Space(context);
        var outside = added.Count(view =>
        {
            var rect = DrawingSheet.Outline(api, view);
            return !rect.Within(space.Frame) || space.KeepOuts.Any(rect.Overlaps);
        });
        if (outside > 0)
            lines.Add($"· {outside} 个超出图框或压到标题栏等，按「排版」整页重排。");

        var head = added.Count == 0 && failed == 0
            ? $"投影视图：视图「{sheet.MainName}」该有的投影视图都有了，没有再加。"
            : $"投影视图：从视图「{sheet.MainName}」加了 {added.Count} 个，贴着它放（别的视图没动，整页重排按「排版」）"
              + (failed > 0 ? $"，{failed} 个 SolidWorks 没有投影出来" : string.Empty) + "。";
        var message = head + Environment.NewLine + string.Join(Environment.NewLine, lines);
        return failed > 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }
}
