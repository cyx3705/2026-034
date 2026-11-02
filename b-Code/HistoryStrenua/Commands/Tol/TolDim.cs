using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>快捷指令「尺寸公差」<c>strenua.tol.dim</c>（1.36.0）：给已有的尺寸或孔标注写各种尺寸公差，只管写上去，值对不对由调用方定。</summary>
internal static class TolDim
{
    public static QuickCommand Command { get; } = new(
        Key: "tol-dim",
        CommandName: StrenuaIdentity.Domain + ".tol.dim",
        Title: "尺寸公差",
        Summary: "按名字给尺寸或孔标注写尺寸公差：对称、双向、极限、基本、最小、最大、配合，或去掉；读回显示文字。",
        Usage: "填「视图」「名字」「种类」（" + ToleranceSpec.TypeHelp + "）。对称写 plus=0.02；双向、极限写 plus= 上偏差和 minus= 下偏差（带符号 mm，如 plus=0.02 minus=-0.01）；配合类写 hole=H7 和 / 或 shaft=g6。孔标注默认写第一个直径变量，要写别的用 var= 变量名。名字用 strenua.dim.list 查。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            QuickArgs.View("视图名（视图列表的 name 列）"),
            new QuickParameter("name", "名字", "尺寸名（标注列表的 name 列），如 D1", true),
            new QuickParameter("type", "种类", ToleranceSpec.TypeHelp, true),
            new QuickParameter("plus", "上偏差", "上偏差 mm，带符号，如 0.02；对称公差只填它", false, 2),
            new QuickParameter("minus", "下偏差", "下偏差 mm，带符号，如 -0.01", false, 2),
            new QuickParameter("hole", "孔配合", "孔的公差带，如 H7", false, 2),
            new QuickParameter("shaft", "轴配合", "轴的公差带，如 g6", false, 2),
            new QuickParameter("var", "孔标注变量", "孔标注里写哪个变量；省略写第一个直径变量", false, 2),
        ],
        Example: StrenuaIdentity.Domain + ".tol.dim view=工程图视图1 name=D1 type=bilateral plus=0.02 minus=-0.01",
        Bind: Bind,
        SelfReport: true);

    private static Func<QuickCommandContext, QuickOutcome> Bind(CommandContext arguments)
    {
        var view = QuickArgs.Name(arguments, "view");
        var name = QuickArgs.Name(arguments, "name");
        var spec = ToleranceSpec.Dimension(QuickArgs.Name(arguments, "type"),
            QuickArgs.Millimeters(arguments.GetString("plus"), "plus"), QuickArgs.Millimeters(arguments.GetString("minus"), "minus"),
            QuickArgs.Optional(arguments, "hole"), QuickArgs.Optional(arguments, "shaft"));
        var variable = QuickArgs.Optional(arguments, "var");
        return context => Run(context, view, name, spec, variable);
    }

    private static QuickOutcome Run(QuickCommandContext context, string viewName, string name, DimensionToleranceSpec spec, string? variable)
    {
        var api = context.Api;
        var drawing = DrawingAccess.ActiveDrawing(context);
        var view = SheetLookup.View(api, drawing, viewName);
        var annotation = SheetLookup.Annotation(api, view, name);
        if (api.CallInt(annotation, "IAnnotation", "GetType") != SwConst.AnnotationDimension
            || api.Call(annotation, "IAnnotation", "GetSpecificAnnotation") is not { } display)
            return QuickOutcome.Fail($"尺寸公差：「{name}」不是尺寸。");

        context.SetState("尺寸公差");
        string? failure = null;
        if (api.CallBool(display, "IDisplayDimension", "IsHoleCallout"))
        {
            if (ToleranceEdit.WriteCallout(api, display, spec, variable, out var available) is null)
                failure = $"孔标注写不上（变量有：{string.Join("、", available)}）";
        }
        else if (variable is not null)
            failure = "var= 只对孔标注有用";
        else if (!ToleranceEdit.WriteDimension(api, display, spec))
            failure = "读回对不上";

        api.Call(drawing, "IModelDoc2", "EditRebuild3");
        api.Redraw(drawing);
        var shown = AnnotationRows.Shown(api, annotation);
        return failure is null
            ? QuickOutcome.Ok($"尺寸公差：视图「{viewName}」的「{name}」写了{spec.Title}，现在显示「{shown}」。")
            : QuickOutcome.Fail($"尺寸公差：「{name}」的{spec.Title}没写成，{failure}；现在显示「{shown}」。");
    }
}
