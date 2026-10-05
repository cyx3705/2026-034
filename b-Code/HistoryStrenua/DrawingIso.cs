namespace HistoryStrenua;

/// <summary>
/// 快捷指令「轴测图」（1.9.0，出图类，从「新建工程图」拆出）：当前工程图里从主视图（或选中的视图）斜投影出一个轴测图，
/// 放进主视图右边的空地，放不下就缩一两档比例；图纸页上已有轴测图就不再加。
/// </summary>
/// <remarks>
/// 用户 15 张手工图每张都有一个从主视图斜投影出来的轴测图（真机：摆在主视图右上方投影出来的就是）。放哪见 <see cref="DrawingPlanner.IsoSpot"/>，
/// 只躲现有的视图（连同标注空间）、标题栏等，别的不动；整页重排是「排版」的事。
/// </remarks>
internal static class DrawingIso
{
    public static QuickCommand Command { get; } = new(
        Key: "drawing-iso",
        CommandName: StrenuaIdentity.Domain + ".drawing.iso",
        Title: "轴测图",
        Summary: "当前工程图：从主视图（或选中的视图）斜投影出一个轴测图，放进右边的空地（放不下缩一两档比例），已有就不再加。",
        Usage: "在工程图里按（选了视图就从它投影，没选就从主视图）：从主视图右上方斜投影出一个轴测图，放到离主视图那一行最近的空地，躲开现有视图连同它们的尺寸空间、标题栏等；原比例放不下就缩一档、两档（如 1:2 → 1:3 → 1:5）。图纸页上已有轴测图就不再加。别的视图不动；整页重排按「排版」。",
        Run: Run);

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var api = context.Api;
        var drawing = HoleScan.ActiveDrawing(context);
        var selected = HoleScan.PreselectedView(api, drawing);
        if (selected is not null && (api.Call(selected, "IView", "get_ReferencedDocument") is null || HoleScan.Frame(context, selected).Axonometric))
            selected = null;
        return Run(context, DrawingSheet.Read(context, drawing, "轴测图", selected));
    }

    internal static QuickOutcome Run(QuickCommandContext context, DrawingSheet sheet)
    {
        var api = context.Api;
        if (sheet.Iso is not null)
            return QuickOutcome.Ok($"轴测图：图纸页上已有轴测图「{DrawingSheet.Name(api, sheet.Iso)}」，没有再加。");

        context.SetState("加轴测图");
        var at = DrawingSheet.Outline(api, sheet.Main);
        var iso = DrawingSheet.Unfold(api, sheet.Drawing, sheet.MainName, at.Right + 0.08, at.Top + 0.04);
        if (iso is null)
            return QuickOutcome.Fail($"轴测图：SolidWorks 没有从视图「{sheet.MainName}」斜投影出视图。");
        var name = DrawingSheet.Name(api, iso);
        var axonometric = HoleScan.Frame(context, iso).Axonometric;

        api.Call(sheet.Drawing, "IModelDoc2", "EditRebuild3");
        var occupied = sheet.Occupied(context);
        var space = sheet.Space(context);
        var target = new SheetPoint((occupied.Max(rect => rect.Right) + space.Frame.Right) / 2, at.Center.Y);
        var (center, shrink, free) = DrawingPlanner.IsoSpot(space, occupied, sheet.Box(context, iso, annotations: false),
            DrawingPlanner.IsoFactors(sheet.Part, sheet.Scale), target);
        var factor = DrawingSheet.ScaleIso(api, iso, sheet.Scale, shrink, DrawingPlanner.Scales(sheet.Part));
        DrawingSheet.SetPosition(api, iso, center);
        api.Call(sheet.Drawing, "IModelDoc2", "EditRebuild3");
        api.Call(sheet.Drawing, "IModelDoc2", "ClearSelection2", true);

        var message = $"轴测图：从视图「{sheet.MainName}」斜投影出「{name}」，放在空地上"
            + (factor < 1 ? $"，比例缩成 {DrawingPlanner.ScaleText(sheet.Scale * factor)}" : string.Empty) + "。"
            + (free ? string.Empty : " 图上没有完全空的地方，放在了压得最少处，可按「排版」整页重排。")
            + (axonometric ? string.Empty : " 斜着投影出来的不是轴测图，照样留着。");
        return QuickOutcome.Ok(message);
    }
}
