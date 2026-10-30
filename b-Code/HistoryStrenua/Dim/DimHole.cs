using HistoryVulcan.Core.Commands;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「加孔标注」<c>strenua.dim.hole</c>（1.25.0）：按编号给一个孔加孔标注，不经过认孔分种，导入件认不出的孔也能标。
/// </summary>
internal static class DimHole
{
    public static QuickCommand Command { get; } = new(
        Key: "dim-hole",
        CommandName: StrenuaIdentity.Domain + ".dim.hole",
        Title: "加孔标注",
        Summary: "按图纸 SVG 的孔编号（H5）给那个孔加一个孔标注，折点放到指定位置，读回显示文字。",
        Usage: "先用 strenua.check.svg view=视图名 查孔编号。X、Y 是引线折点（文字下划线靠孔的那一端，图纸 mm），在孔左边文字朝左，在右边朝右。规格、N x 由 SolidWorks 按孔特征写，读回看对不对。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            QuickArgs.View("视图名（视图列表的 name 列）"),
            new QuickParameter("hole", "孔", "孔编号，如 H5", true),
            QuickArgs.X("引线折点 X（图纸 mm）"),
            QuickArgs.Y("引线折点 Y（图纸 mm）"),
        ],
        Example: StrenuaIdentity.Domain + ".dim.hole view=工程图视图1 hole=H5 x=60 y=120",
        Bind: Bind,
        SelfReport: true);

    // AddHoleCallout2 的文字先落在折点左边这么远，再按实际下划线对准（同 HoleCallout）。
    private const double InitialTextOffset = 0.02;

    private static Func<QuickCommandContext, QuickOutcome> Bind(CommandContext arguments)
    {
        var view = QuickArgs.Name(arguments, "view");
        var hole = GeometryRef.Parse(QuickArgs.Name(arguments, "hole"), "hole");
        if (!hole.Hole)
            throw new QuickArgumentException($"hole={hole} 不是孔编号（要 H5 这样的）。");
        var x = QuickArgs.Millimeters(arguments.GetString("x"), "x") ?? throw new QuickArgumentException("缺参数 x=（折点，图纸 mm）");
        var y = QuickArgs.Millimeters(arguments.GetString("y"), "y") ?? throw new QuickArgumentException("缺参数 y=（折点，图纸 mm）");
        return context => Run(context, view, hole.Index, new SheetPoint(x * QuickArgs.Mm, y * QuickArgs.Mm));
    }

    private static QuickOutcome Run(QuickCommandContext context, string viewName, int index, SheetPoint at)
    {
        var api = context.Api;
        var drawing = HoleScan.ActiveDrawing(context);
        var view = SheetLookup.View(api, drawing, viewName);
        context.SetState("加孔标注");
        var scan = HoleScan.Scan(context, "加孔标注", view: view, quiet: true);
        if (index >= scan.Candidates.Count)
            return QuickOutcome.Fail($"加孔标注：视图「{viewName}」没有孔 H{index}（共 {scan.Candidates.Count} 个）。");

        _ = api.Call(drawing, "IDrawingDoc", "ActivateView", viewName);
        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        var textLeft = at.X < scan.Candidates[index].X;
        var callout = HolePosition.SelectEdge(api, scan, scan.Edges[index], false)
            ? api.Call(drawing, "IDrawingDoc", "AddHoleCallout2", at.X - (textLeft ? InitialTextOffset : -InitialTextOffset), at.Y, 0.0)
            : null;
        api.Call(drawing, "IModelDoc2", "ClearSelection2", true);
        if (callout is null || api.Call(callout, "IDisplayDimension", "GetAnnotation") is not { } annotation)
            return QuickOutcome.Fail($"加孔标注：SolidWorks 没有接受孔 H{index}（选到的不是孔边，或这里不能标）。");

        HoleCallout.AlignShoulder(api, annotation, at, textLeft);
        api.Call(drawing, "IModelDoc2", "GraphicsRedraw2");
        return QuickOutcome.Ok($"加孔标注：视图「{viewName}」孔 H{index} 加了「{api.CallString(annotation, "IAnnotation", "GetName")}」，显示「{DimList.Shown(api, annotation)}」。");
    }
}
