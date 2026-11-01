using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「加中心符号线」<c>strenua.dim.center</c>（1.25.0）：按编号给指定的孔加一组中心符号线，由 AI 决定哪些孔连在一起。
/// </summary>
/// <remarks>全自动那条按种成组，同种孔不在一条线上时连接线会斜拉（真机底层1）；这里点名几个孔就连几个，旧的用「删标注」删。</remarks>
internal static class DimCenter
{
    public static QuickCommand Command { get; } = new(
        Key: "dim-center",
        CommandName: StrenuaIdentity.Domain + ".dim.center",
        Title: "加中心符号线",
        Summary: "按孔编号给指定的孔加中心符号线：一个孔单个，多个孔一组线性带连接线；不删旧的。",
        Usage: "「孔组」填孔编号，逗号分开（如 H1,H2,H3）。一个孔建单个；多个孔建一组线性带连接线，连接线按填的顺序串，所以要排成一条线。孔上已有的旧符号线先用 strenua.dim.delete 删掉。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            QuickArgs.View("视图名（视图列表的 name 列）"),
            new QuickParameter("holes", "孔组", "孔编号，逗号分开，如 H1,H2,H3", true),
        ],
        Example: StrenuaIdentity.Domain + ".dim.center view=工程图视图1 holes=H1,H2,H3",
        Bind: arguments =>
        {
            var view = QuickArgs.Name(arguments, "view");
            var holes = QuickArgs.Names(arguments.GetString("holes"), "holes")
                .Select(text => GeometryRef.Parse(text, "holes"))
                .ToList();
            if (holes.Any(hole => !hole.Hole))
                throw new QuickArgumentException("holes= 只能填孔编号（H5 这样的）。");
            return context => Run(context, view, holes.Select(hole => hole.Index).ToList());
        },
        SelfReport: true);

    private static QuickOutcome Run(QuickCommandContext context, string viewName, IReadOnlyList<int> holes)
    {
        var api = context.Api;
        var drawing = DrawingAccess.ActiveDrawing(context);
        var view = SheetLookup.View(api, drawing, viewName);
        context.SetState("加中心符号线");
        var scan = HoleScan.Scan(context, "加中心符号线", view: view, quiet: true);
        var missing = holes.Where(index => index >= scan.Candidates.Count).ToList();
        if (missing.Count > 0)
            return QuickOutcome.Fail($"加中心符号线：视图「{viewName}」没有孔 {string.Join("、", missing.Select(index => "H" + index))}（共 {scan.Candidates.Count} 个）。");

        _ = api.Call(drawing, "IDrawingDoc", "ActivateView", viewName);
        var mark = CenterMarkEdit.InsertMarks(api, scan, holes);
        api.ClearSelection(drawing);
        api.Redraw(drawing);
        if (mark is null)
            return QuickOutcome.Fail($"加中心符号线：SolidWorks 没有接受这组孔（{holes.Count} 个）。");
        var name = api.Call(mark, "ICenterMark", "GetAnnotation") is { } annotation ? api.CallString(annotation, "IAnnotation", "GetName") : "?";
        return QuickOutcome.Ok($"加中心符号线：视图「{viewName}」{holes.Count} 个孔加了「{name}」。");
    }
}
