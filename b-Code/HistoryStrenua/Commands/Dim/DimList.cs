using System.Runtime.InteropServices;
using HistoryStrenua.SolidWorks;
using static HistoryStrenua.AnnotationRows;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「标注列表」<c>strenua.dim.list</c>（1.20.0）：一个视图（不给就整页，含图纸页本身上的注释）里每个注解一行——
/// 名字、种类、显示的字、尺寸值、位置、外框。名字就是 <c>dim.delete / move / edit</c> 的 <c>name(s)=</c>。只读。
/// </summary>
internal static class DimList
{
    public static QuickCommand Command { get; } = new(
        Key: "dim-list",
        CommandName: StrenuaIdentity.Domain + ".dim.list",
        Title: "标注列表",
        Summary: "列出视图（省略则整页）里的尺寸与注解：名字、种类、显示文字、尺寸值、位置与外框（图纸 mm）；名字供删、挪、改标注用。",
        Usage: "「视图」填视图名只列这个视图，空着列整页（含图纸页本身上的注释）。每个尺寸 / 注解一行：名字、种类（线性 / 坐标 / 角度 / 半径 / 直径 / 倒角尺寸、孔标注、注释、中心符号线……）、图上显示的字、尺寸值、位置（文字中点）与外框，单位图纸 mm。名字就是「删标注」「挪标注」「改尺寸」的「名字」框要填的。图纸页上标题栏 / 修改栏 / 图号框里的字、图框外的（分区字母、模板备用的技术要求）和空注释不列，只报个数（格子里的字见图纸 SVG 的 data-text）。只读，不改图。",
        Run: QuickCommand.Unbound,
        Parameters:
        [
            new QuickParameter("view", "视图", "视图名（视图列表的 name 列）；省略时列当前图纸页全部视图与图纸页本身的注解", false),
        ],
        Example: StrenuaIdentity.Domain + ".dim.list view=工程图视图1",
        Readonly: true,
        Bind: arguments =>
        {
            var view = QuickArgs.Optional(arguments, "view");
            return context => Run(context, view);
        });


    /// <summary>中心符号线连着哪些孔（图纸 SVG 的编号，同心的只取最先那个）：要删一组、拆一组时不用靠外框猜。</summary>
    private static string MarkedHoles(SolidWorksApi api, ScannedView scan, object annotation)
    {
        var ids = CalloutEdit.AttachedCircleCenters(api, scan.Geometry, annotation)
            .Select(center => scan.Candidates.Select((hole, i) => (hole, i))
                .Where(item => HoleCalloutPlanner.SameCenter(center, new SheetPoint(item.hole.X, item.hole.Y)))
                .Select(item => (int?)item.i).FirstOrDefault())
            .OfType<int>().Distinct().Select(i => "H" + i);
        return string.Join(",", ids);
    }

    private static QuickOutcome Run(QuickCommandContext context, string? viewName)
    {
        var api = context.Api;
        var drawing = DrawingAccess.ActiveDrawing(context);
        var views = viewName is null ? SheetLookup.Views(api, drawing) : [SheetLookup.View(api, drawing, viewName)];
        var space = DrawingSvg.Space(api, drawing).Space;
        var rows = new List<IReadOnlyDictionary<string, string>>();
        var lines = new List<string>();
        var hidden = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var view in views)
        {
            var name = DrawingSheet.Name(api, view);
            var sheet = SheetLookup.IsSheet(api, view);
            var listed = new List<Dictionary<string, string>>();
            ScannedView? scan = null;
            foreach (var (annotationName, annotation) in SheetLookup.Annotations(api, view))
            {
                context.Cancellation.ThrowIfCancellationRequested();
                var row = Row(api, name, annotationName, annotation);
                if (!sheet && api.CallInt(annotation, "IAnnotation", "GetType") == SwConst.AnnotationCenterMark)
                {
                    scan ??= HoleScan.Scan(context, "标注列表", view: view, quiet: true);
                    row["holes"] = MarkedHoles(api, scan, annotation);
                }

                // 1.24.0 真机：整页列表 80 多行里九成是标题栏格子、公差表数字、分区字母和模板备用的技术要求。
                if (sheet && SheetPlace(row, space) is { } place)
                {
                    hidden[place] = hidden.GetValueOrDefault(place) + 1;
                    continue;
                }

                listed.Add(row);
            }

            if (listed.Count == 0 && viewName is null)
                continue;
            lines.Add($"视图「{name}」{listed.Count} 个：");
            rows.AddRange(listed);
            lines.AddRange(listed.Select(Line));
        }

        var scope = viewName is null ? "当前图纸页" : $"视图「{viewName}」";
        var skipped = hidden.Count == 0
            ? string.Empty
            : $"（没列：{string.Join("、", hidden.Select(pair => $"{pair.Key} {pair.Value} 个"))}）";
        return QuickOutcome.Ok($"标注列表：{scope}共 {rows.Count} 个（图纸 mm）{skipped}。" + Environment.NewLine + string.Join(Environment.NewLine, lines), rows);
    }

}
