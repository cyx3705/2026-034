namespace HistoryStrenua;

/// <summary>
/// 快捷指令「全图外轮廓」（1.17.0，基础类）：当前图纸页全部视图（轴测图跳过）逐个做「外轮廓」（<see cref="Outline"/>）。
/// </summary>
/// <remarks>
/// <para>外轮廓从「孔标注全流程」里拿出来（用户定：外轮廓不是孔类的事）之后，一键类的各条要一个不用点视图的版本，就是这条。
/// 按视图顺序做，前面视图标过的后面不再标（跨视图去重照旧）。</para>
/// <para>不并进「基础出图」：尺寸链模式下外轮廓往孔的那组坐标尺寸里加站，孔位尺寸重标会连 0 点删掉整组，所以必须在孔标注全流程之后；
/// 基础出图在孔标注之前做，放进去就会被冲掉（我定）。</para>
/// </remarks>
internal static class OutlineAll
{
    public static QuickCommand Command { get; } = new(
        Key: "outline-all",
        CommandName: StrenuaIdentity.Domain + ".drawing.outlineall",
        Title: "全图外轮廓",
        Summary: "当前图纸页全部视图（轴测图跳过）逐个标外轮廓每个台阶的位置，别的视图标过的不再标，照「尺寸链」开关出线性或坐标尺寸。",
        Usage: "不用点视图：当前图纸页上的全部视图（轴测图跳过）按顺序逐个做「外轮廓」——以零件最左、最上的直边为基准标出外轮廓上每条竖直边、水平边（钣金折弯切线与离外轮廓不到一个折弯外半径的短边不算），前面视图标过的后面不再标；「尺寸链」开着时并进孔的那组坐标尺寸，所以要在孔标注全流程之后按。某个视图没成不中断，最后汇总。",
        Run: context => Run(context));

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="minGap">框架：离别的站不到型材宽的站不标（模型长度，米；0 不筛，见 <see cref="OutlinePlanner.Sparse"/>）。</param>
    internal static QuickOutcome Run(QuickCommandContext context, double minGap = 0)
        => FilletAll.EachView(context, "全图外轮廓", (inner, view) => Outline.Run(inner, view, minGap));
}
