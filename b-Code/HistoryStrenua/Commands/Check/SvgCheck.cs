using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>快捷指令「图纸 SVG」<c>strenua.check.svg</c>：读图与写法见 <see cref="DrawingSvg"/>、<see cref="DrawingSvgWriter"/>。不改图。</summary>
internal static class SvgCheck
{
    private static QuickOutcome Run(QuickCommandContext context, string? viewName)
    {
        var drawing = DrawingAccess.ActiveDrawing(context);
        if (viewName is not null)
            SheetLookup.View(context.Api, drawing, viewName);
        var sheet = DrawingSvg.Read(context, drawing, only: viewName);
        var file = DrawingSvg.Save(context, sheet.Title, DrawingSvgWriter.Write(sheet));
        var views = sheet.Views.Where(view => view.Outline is not null).ToList();
        var scope = viewName is null ? "当前图纸页" : $"视图「{viewName}」";
        var counts = $"{views.Count} 个视图、{views.Sum(view => view.Lines.Count)} 条直边、{views.Sum(view => view.Holes.Count)} 个孔、{sheet.Views.Sum(view => view.Annotations.Count)} 个注解";
        var (svg, how) = DrawingSvg.Budgeted(context, drawing, sheet, viewName is null ? SvgDetail.Ladder : SvgDetail.ViewLadder);
        var message = $"图纸 SVG：「{sheet.Title}」{scope} {counts}；{how}。完整 SVG 已存 {file}";
        return QuickOutcome.Ok(svg is null ? message : message + Environment.NewLine + svg);
    }

    public static QuickCommand Command { get; } = new(
        Key: "svg",
        CommandName: StrenuaIdentity.Domain + ".check.svg",
        Title: "图纸 SVG",
        Summary: "把当前图纸页（或一个视图）读成 SVG 代码回执给 AI：边与孔带编号，标注画方框加字，坐标为图纸 mm；超过 1 万字降级，最后回截图。",
        Usage: "「视图」空着读整页，填视图名只读那一个视图（要某个视图的完整几何就用它）。回执的数据是一段 SVG（完整的一份另存到模块数据目录 snapshots，可用浏览器打开）。坐标是图纸 mm、左下角为原点，和视图、标注类的 X / Y 相同。每个视图一组：可见直边（id「视图名:E序号」）、曲线（编号 C，折线近似，正对图纸的圆弧带圆心 data-center 和半径 data-r）、孔（「视图名:H序号」，带孔种）；尺寸、孔标注、注释只画字占的方框和图上显示的字，尺寸另写量的两头（data-measures）；中心符号线、中心线画成线。标题栏、修改栏、图号框各一个方框，格子里的字合在 data-text。回执超过 1 万字就依次降成「几何 + 精简标注」「只有标注」，还超就回整页截图的路径。不改图。",
        // 要存一份文件，所以和「图纸截图」一样不标只读（宿主的只读 = 不写文件）。
        Run: QuickCommand.Unbound,
        Parameters: [new QuickParameter("view", "视图", "视图名（视图列表的 name 列）；省略时读整页", false)],
        Example: StrenuaIdentity.Domain + ".check.svg view=工程图视图1",
        Bind: arguments =>
        {
            var view = QuickArgs.Optional(arguments, "view");
            return context => Run(context, view);
        });
}
