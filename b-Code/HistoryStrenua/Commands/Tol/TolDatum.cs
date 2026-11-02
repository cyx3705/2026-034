using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>快捷指令「基准符号」<c>strenua.tol.datum</c>（1.36.0）：形位公差框里写的基准要在图上有符号。</summary>
internal static class TolDatum
{
    public static QuickCommand Command { get; } = new(
        Key: "tol-datum",
        CommandName: StrenuaIdentity.Domain + ".tol.datum",
        Title: "基准符号",
        Summary: "加基准符号：写字母，指向直边、孔、圆弧或尺寸；读回字母。",
        Usage: "填「视图」「字母」（如 A）、X、Y（符号位置，图纸 mm），和指向：on=E3 / H5 / C3（strenua.check.svg 的编号）或 dim=尺寸名，二选一。删、挪用 strenua.dim.delete、strenua.dim.move。不看避障开关。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            QuickArgs.View("视图名（视图列表的 name 列）"),
            new QuickParameter("label", "字母", "基准字母，如 A", true),
            QuickArgs.X("符号位置 X（图纸 mm）"),
            QuickArgs.Y("符号位置 Y（图纸 mm）"),
            new QuickParameter("on", "指向", "几何编号：直边 E3、孔 H5、曲线 C3；不能和 dim 同用", false, 2),
            new QuickParameter("dim", "指向尺寸", "尺寸名：基准是这个尺寸的要素（中心线、中心面）；不能和 on 同用", false, 2),
        ],
        Example: StrenuaIdentity.Domain + ".tol.datum view=工程图视图1 label=A on=E3 x=120 y=40",
        Bind: arguments =>
        {
            var view = QuickArgs.Name(arguments, "view");
            var label = ToleranceSpec.DatumLabel(QuickArgs.Name(arguments, "label"));
            var at = QuickArgs.Sheet(arguments);
            var (on, dim) = QuickArgs.Pointing(arguments);
            if (on is null && dim is null)
                throw new QuickArgumentException("基准符号要指向东西：给 on= 或 dim=。");
            return context => Run(context, view, label, at, on, dim);
        },
        SelfReport: true);

    private static QuickOutcome Run(QuickCommandContext context, string viewName, string label, SheetPoint at, GeometryRef? on, string? dim)
    {
        var api = context.Api;
        var drawing = DrawingAccess.ActiveDrawing(context);
        var view = SheetLookup.View(api, drawing, viewName);
        context.SetState("基准符号");
        _ = api.Call(drawing, "IDrawingDoc", "ActivateView", viewName);
        if (ToleranceEdit.SelectTarget(context, drawing, view, "基准符号", on, dim) is { } reason)
            return QuickOutcome.Fail($"基准符号：{reason}。");

        var tag = api.Call(drawing, "IModelDoc2", "InsertDatumTag2");
        api.ClearSelection(drawing);
        if (tag is null || api.Call(tag, "IDatumTag", "GetAnnotation") is not { } annotation)
            return QuickOutcome.Fail("基准符号：SolidWorks 没有建出基准符号。");

        api.Call(tag, "IDatumTag", "SetLabel", label);
        if (!string.Equals(api.CallString(tag, "IDatumTag", "GetLabel"), label, StringComparison.Ordinal))
        {
            ToleranceEdit.Discard(api, drawing, annotation);
            return QuickOutcome.Fail($"基准符号：字母 {label} 写不上（可能这个字母已用过），已删掉。");
        }

        api.Call(annotation, "IAnnotation", "SetPosition2", at.X, at.Y, 0.0);
        api.Redraw(drawing);
        var target = dim is not null ? $"尺寸「{dim}」" : on.ToString();
        return QuickOutcome.Ok($"基准符号：视图「{viewName}」加了「{api.CallString(annotation, "IAnnotation", "GetName")}」= {label}，指向 {target}，位置 {QuickArgs.Format(ToleranceEdit.Actual(api, annotation, at))}。");
    }
}
