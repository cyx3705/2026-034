namespace HistoryStrenua;

/// <summary>
/// 快捷指令「全图倒角」（1.10.0，出图类）：当前图纸页全部视图（轴测图跳过）逐个做「倒角标注」（<see cref="ChamferDimension"/>）。
/// </summary>
/// <remarks>
/// 不用点视图（AI 经 MCP 调用时点不了视图），「一键出图」最后一步也是它。按视图顺序做：同一个倒角在前后两个视图里都成斜线时，
/// 前面的视图标了，后面的视图认出「别的视图已标」跳过。
/// </remarks>
internal static class ChamferAll
{
    public static QuickCommand Command { get; } = new(
        Key: "chamfer-all",
        CommandName: StrenuaIdentity.Domain + ".drawing.chamferall",
        Title: "全图倒角",
        Summary: "当前图纸页全部视图（轴测图跳过）逐个做倒角标注：C1 不标，同尺寸 2 个以上合标 N x C，已标的跳过。",
        Usage: "不用点视图：当前图纸页上的全部视图（轴测图跳过）逐个做「倒角标注」——侧着看成斜线的倒角每种尺寸标一个 C 尺寸，同一尺寸 2 个以上写「N x C…」；C1 不标（技术要求「未注倒角C1」）；已标过的、前面视图标过的同一个倒角跳过。某个视图没成不中断，最后汇总。",
        Run: Run);

    internal static QuickOutcome Run(QuickCommandContext context) => FilletAll.EachView(context, "全图倒角", ChamferDimension.Run);
}
