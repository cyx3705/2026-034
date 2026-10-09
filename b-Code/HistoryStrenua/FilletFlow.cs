namespace HistoryStrenua;

/// <summary>
/// 快捷指令「全图倒圆倒角」（1.16.0，倒圆类，用户定）：一键做完这一类的全图工作——全图圆心 → 全图圆弧 → 全图倒角。
/// </summary>
/// <remarks>
/// 顺序同 1.14.2 起「一键出图」里的：圆心位置是从基准量起的线性尺寸，先放；圆弧、倒角的文字找空处时圆心尺寸已在位。
/// 某一步没成不中断，最后汇总；范围是当前图纸页（轴测图跳过），与三条全图指令各自一样。
/// </remarks>
internal static class FilletFlow
{
    public static QuickCommand Command { get; } = new(
        Key: "fillet-flow",
        CommandName: StrenuaIdentity.Domain + ".fillet.all",
        Title: "全图倒圆倒角",
        Summary: "当前图纸页全部视图（轴测图跳过）依次做全图圆心、全图圆弧、全图倒角，一次标完圆心位置、R / Ø 与 C 尺寸。",
        Usage: "不用点视图：当前图纸页上的全部视图（轴测图跳过）依次做「全图圆心 → 全图圆弧 → 全图倒角」——圆弧圆心定位尺寸、R（不是孔的整圆 Ø）尺寸、倒角 C 尺寸，各自的规则同那三个按钮（R1、C1 不标，同尺寸合标，已标的跳过），照避障、尺寸链开关。某一步没成不中断，最后汇总。",
        Run: Run);

    internal static QuickOutcome Run(QuickCommandContext context)
    {
        var steps = new StepLog(context);
        steps.Run("全图圆心", () => ArcCenterAll.Run(context));
        steps.Run("全图圆弧", () => FilletAll.Run(context));
        steps.Run("全图倒角", () => ChamferAll.Run(context));
        return steps.Finish("全图倒圆倒角", "当前图纸页三步都已做完");
    }
}
