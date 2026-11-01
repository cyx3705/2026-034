using static HistoryStrenua.ViewFlow;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「全图圆弧」（1.9.0「全图圆角」，1.14.2 改名，从「一键出图」拆出；1.15.0 起倒圆倒角类）：当前图纸页全部视图（轴测图跳过）逐个做「圆弧标注」（<see cref="FilletDimension"/>）。
/// </summary>
/// <remarks>
/// 不用点视图（AI 经 MCP 调用时点不了视图）。某个视图没成不中断，最后汇总；范围只是当前图纸页，与「孔标注全流程」一样。
/// </remarks>
internal static class FilletAll
{
    public static QuickCommand Command { get; } = new(
        Key: "arc-all",
        CommandName: StrenuaIdentity.Domain + ".fillet.arcall",
        Title: "全图圆弧",
        Summary: "当前图纸页全部视图（轴测图跳过）逐个做圆弧标注：R1 不标，同半径 3 个以上合标 N x R，已标的跳过。",
        Usage: "不用点视图：当前图纸页上的全部视图（轴测图跳过）逐个做「圆弧标注」——正对图纸的圆弧各加一个 R 尺寸（不是孔的整圆加 Ø），文字放在零件外的空处；R1 不标（技术要求「未注圆角R1」），同一半径 3 个以上只标一个并写「N x R」，已有 R 或直径尺寸的跳过。某个视图没成不中断，最后汇总。",
        Run: Run);

    internal static QuickOutcome Run(QuickCommandContext context) => EachView(context, "全图圆弧", FilletDimension.Run);
}
