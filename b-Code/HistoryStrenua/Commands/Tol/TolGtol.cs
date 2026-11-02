using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「形位公差」<c>strenua.tol.gtol</c>（1.36.0）：加一个公差框，形状、方向、位置、跳动、轮廓公差都走它。
/// </summary>
/// <remarks><c>IModelDoc2.InsertGtol</c> 的引线落点取自调用前的选择，所以先选指向的对象；新框默认在视图旁，再挪到给的位置。</remarks>
internal static class TolGtol
{
    public static QuickCommand Command { get; } = new(
        Key: "tol-gtol",
        CommandName: StrenuaIdentity.Domain + ".tol.gtol",
        Title: "形位公差",
        Summary: "加形状、方向、位置、跳动或轮廓公差框：可指向直边、孔、圆弧或尺寸，也可不带引线；读回显示文字。",
        Usage: "「公差框」写「符号,公差,基准…」（逗号或空格分开，控制台里用逗号）：符号是 直线度、平面度、圆度、圆柱度、线轮廓度、面轮廓度、平行度、垂直度、倾斜度、位置度、同轴度、对称度、圆跳动、全跳动；公差可带 Ø 和修饰符（Ø0.1(M)、0.05/100、0.2(P50) 延伸公差带）；基准最多三个（A、A-B、B(M)）。两行用分号分开。形状公差不许带基准，方向、同轴、对称、跳动必须带。指向：on=E3 / H5 / C3（strenua.check.svg 的编号）或 dim=尺寸名，都不填不带引线。X、Y 是框的位置（图纸 mm）。改错了用 strenua.dim.delete 删掉重加，用 strenua.dim.move 挪。不看避障开关。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            QuickArgs.View("视图名（视图列表的 name 列）"),
            new QuickParameter("frame", "公差框", "符号,公差,基准，如 位置度,Ø0.1(M),A,B,C；两行用分号分开", true),
            QuickArgs.X("框的位置 X（图纸 mm）"),
            QuickArgs.Y("框的位置 Y（图纸 mm）"),
            new QuickParameter("on", "指向", "几何编号：直边 E3、孔 H5、曲线 C3；不能和 dim 同用", false, 2),
            new QuickParameter("dim", "指向尺寸", "尺寸名：公差框指这个尺寸（要素的中心线、中心面）；不能和 on 同用", false, 2),
            new QuickParameter("above", "框上文字", "框上方的文字，如 4 x", false, 2),
            new QuickParameter("below", "框下文字", "框下方的文字", false, 2),
            new QuickParameter("allaround", "全周", "是：引线带全周符号；省略为否", false, 2),
        ],
        Example: StrenuaIdentity.Domain + ".tol.gtol view=工程图视图1 frame=平行度,0.02,A on=E3 x=120 y=60",
        Bind: Bind,
        SelfReport: true);

    private const int TextAbove = 3, TextBelow = 4;

    private static Func<QuickCommandContext, QuickOutcome> Bind(CommandContext arguments)
    {
        var view = QuickArgs.Name(arguments, "view");
        var frames = ToleranceSpec.Frames(QuickArgs.Name(arguments, "frame"));
        var at = QuickArgs.Sheet(arguments);
        var (on, dim) = QuickArgs.Pointing(arguments);
        var above = QuickArgs.Optional(arguments, "above");
        var below = QuickArgs.Optional(arguments, "below");
        var allAround = QuickArgs.Flag(arguments, "allaround");
        if (allAround && on is null && dim is null)
            throw new QuickArgumentException("全周符号在引线上，要给 on= 或 dim=。");
        return context => Run(context, view, frames, at, on, dim, above, below, allAround);
    }

    private static QuickOutcome Run(QuickCommandContext context, string viewName, IReadOnlyList<GtolFrame> frames, SheetPoint at, GeometryRef? on, string? dim, string? above, string? below, bool allAround)
    {
        var api = context.Api;
        var drawing = DrawingAccess.ActiveDrawing(context);
        var view = SheetLookup.View(api, drawing, viewName);
        context.SetState("形位公差");
        _ = api.Call(drawing, "IDrawingDoc", "ActivateView", viewName);
        if (ToleranceEdit.SelectTarget(context, drawing, view, "形位公差", on, dim) is { } reason)
            return QuickOutcome.Fail($"形位公差：{reason}。");

        var gtol = api.Call(drawing, "IModelDoc2", "InsertGtol");
        api.ClearSelection(drawing);
        if (gtol is null || api.Call(gtol, "IGtol", "GetAnnotation") is not { } annotation)
            return QuickOutcome.Fail("形位公差：SolidWorks 没有建出公差框。");

        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            var number = (short)(index + 1);
            string Datum(int i) => i < frame.Datums.Count ? frame.Datums[i] : string.Empty;
            api.Call(gtol, "IGtol", "SetFrameSymbols2", number, frame.Symbol, frame.Diameter, frame.Modifiers, false, string.Empty, string.Empty, string.Empty, string.Empty);
            if (!api.CallBool(gtol, "IGtol", "SetFrameValues2", number, frame.Value, string.Empty, Datum(0), Datum(1), Datum(2)))
            {
                ToleranceEdit.Discard(api, drawing, annotation);
                return QuickOutcome.Fail($"形位公差：第 {index + 1} 行「{frame.Title} {frame.Value}」SolidWorks 没接受，已删掉。");
            }

            if (frame.ProjectedZone is { } zone)
                api.Call(gtol, "IGtol", "SetPTZHeight2", (int)number, 1, true, zone);
        }

        if (above is not null)
            api.Call(gtol, "IGtol", "SetText", TextAbove, above);
        if (below is not null)
            api.Call(gtol, "IGtol", "SetText", TextBelow, below);
        if (allAround)
            api.Call(gtol, "IGtol", "SetLeader", true, api.CallInt(gtol, "IGtol", "GetLeaderSide"), api.CallBool(annotation, "IAnnotation", "GetBentLeader"), true);
        api.Call(annotation, "IAnnotation", "SetPosition2", at.X, at.Y, 0.0);
        api.Redraw(drawing);

        var name = api.CallString(annotation, "IAnnotation", "GetName");
        var target = dim is not null ? $"指向尺寸「{dim}」" : on is { } item ? $"指向 {item}" : "不带引线";
        return QuickOutcome.Ok($"形位公差：视图「{viewName}」加了「{name}」，{target}，显示「{AnnotationRows.Shown(api, annotation)}」，位置 {QuickArgs.Format(ToleranceEdit.Actual(api, annotation, at))}。");
    }
}
