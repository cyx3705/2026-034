using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「中心符号线」：点一个工程图视图，视图里全部的孔重新标中心符号线，
/// 一律用「线性中心符号线 + 连接线」（SolidWorks 中心符号线属性里手工插入的第二种）。
/// </summary>
/// <remarks>
/// <para>认孔、分种与「孔标注」相同（<see cref="HoleScan"/>、<see cref="CenterMarkPlanner"/>）：一种孔一组，
/// 同一组的孔由连接线串起来；只有一个孔的种用单个中心符号线。
/// 腰型孔两端的圆弧各当一个孔（1.3.0），一个腰型孔在组里占两个位置。</para>
/// <para>「重新标记」：先删掉标着这些孔的旧中心符号线（单个的、成组的都删），再按本次分种重建。
/// 不在孔上的中心符号线不动。删除有时要第二遍才删干净（真机实测），所以删完回读、最多删三遍。</para>
/// </remarks>
internal static class CenterMark
{
    public static QuickCommand Command { get; } = new(
        Key: "center-mark",
        CommandName: StrenuaIdentity.Domain + ".hole.centermark",
        Title: "中心符号线",
        Usage: "在工程图里点一个视图（先点后按、先按后点都行，60 秒内），该视图里全部的孔删掉旧中心符号线后重标：每种孔一组线性中心符号线带连接线，单孔用单个；腰型孔两端各标一个。",
        Run: Run);

    // swAnnotationType_e / swCenterMarkStyle_e / swCenterMarkConnectionLine_e
    private const int AnnotationCenterMark = 13;
    private const int StyleSingle = 2;
    private const int StyleLinearGroup = 3;
    private const int LinearConnectLines = 1;

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var api = context.Api;
        var scan = HoleScan.Scan(context, "中心符号线");
        var (document, view, viewName) = (scan.Document, scan.View, scan.ViewName);

        var existing = ReadCenterMarks(api, scan.Geometry, view);
        var plan = CenterMarkPlanner.Plan(scan.Candidates, existing.Select(mark => mark.Mark).ToList());
        if (plan.HoleCount == 0)
            return QuickOutcome.Ok($"视图「{viewName}」里没有正对图纸的孔，中心符号线没有改动。");

        context.Report($"中心符号线：视图「{viewName}」认出 {plan.Summary}，"
            + $"删掉 {plan.Obsolete.Count} 个旧中心符号线后按种重标。");
        _ = api.Call(document, "IDrawingDoc", "ActivateView", viewName);
        var holes = HoleCalloutPlanner.Recognize(scan.Candidates);
        int removed;
        int leftover;
        var linear = 0;
        var single = 0;
        var failed = 0;
        try
        {
            context.SetState("删旧符号线");
            (removed, leftover) = DeleteObsolete(context, scan, holes);
            if (leftover > 0)
            {
                return QuickOutcome.Fail($"视图「{viewName}」：有 {leftover} 个旧中心符号线删不掉，没有重标（已删 {removed} 个）。"
                    + "请在 SolidWorks 里手工删掉后再按。");
            }

            context.SetState("加中心符号线");
            foreach (var group in plan.Groups)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                if (group.Linear && InsertLinear(api, scan, group))
                {
                    linear++;
                    continue;
                }

                // 单孔的种；或 SolidWorks 不接受这组孔排成线性组时，退回逐个单个中心符号线。
                foreach (var index in group.EdgeIndices)
                {
                    if (Insert(api, scan, [index], StyleSingle) is not null)
                        single++;
                    else
                        failed++;
                }
            }
        }
        finally
        {
            api.Call(document, "IModelDoc2", "ClearSelection2", true);
            api.Call(document, "IModelDoc2", "GraphicsRedraw2");
        }

        var message = $"视图「{viewName}」：{plan.Summary}，删掉旧中心符号线 {removed} 个，"
            + $"新加线性带连接线 {linear} 组、单个 {single} 个"
            + (failed > 0 ? $"，{failed} 个孔 SolidWorks 没有接受" : string.Empty)
            + "。";
        return linear + single == 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }

    /// <summary>删掉标着孔的旧中心符号线（<see cref="AnnotationEraser"/>：删完回读，最多三遍）。</summary>
    private static (int Removed, int Leftover) DeleteObsolete(QuickCommandContext context, ScannedView scan, IReadOnlyList<HoleEdge> holes)
        => AnnotationEraser.Erase(context, scan.Document, () =>
        {
            var marks = ReadCenterMarks(context.Api, scan.Geometry, scan.View);
            return CenterMarkPlanner.Obsolete(holes, marks.Select(mark => mark.Mark).ToList())
                .Select(index => marks[index].Annotation)
                .ToList();
        });

    /// <summary>一种孔插一组线性中心符号线并打开连接线。SolidWorks 不接受时返回 false，由调用方退回单个。</summary>
    private static bool InsertLinear(SolidWorksApi api, ScannedView scan, CenterMarkGroup group)
    {
        if (Insert(api, scan, group.EdgeIndices, StyleLinearGroup) is not { } mark)
            return false;
        api.Call(mark, "ICenterMark", "set_ConnectionLines", LinearConnectLines);
        return true;
    }

    private static object? Insert(SolidWorksApi api, ScannedView scan, IReadOnlyList<int> edgeIndices, int style)
    {
        api.Call(scan.Document, "IModelDoc2", "ClearSelection2", true);
        for (var i = 0; i < edgeIndices.Count; i++)
        {
            if (!api.CallBool(scan.View, "IView", "SelectEntity", scan.Edges[edgeIndices[i]], i > 0))
                return null;
        }

        return api.Call(scan.Document, "IDrawingDoc", "InsertCenterMark3", style, false, false);
    }

    /// <summary>
    /// 视图里的中心符号线。孔心优先取它附着的圆边；取不到（附着丢失）再用它自己记的位置，
    /// 那是视图所引用模型坐标系里的点，要过视图变换才到图纸上。
    /// </summary>
    private static List<(object Annotation, ExistingCenterMark Mark)> ReadCenterMarks(SolidWorksApi api, HoleScan.ViewGeometry geometry, object view)
    {
        var marks = new List<(object, ExistingCenterMark)>();
        foreach (var annotation in api.CallArray(view, "IView", "GetAnnotations"))
        {
            if (api.CallInt(annotation, "IAnnotation", "GetType") != AnnotationCenterMark)
                continue;
            var centers = HoleCallout.AttachedCircleCenters(api, geometry, annotation).ToList();
            if (centers.Count == 0 && api.Call(annotation, "IAnnotation", "GetSpecificAnnotation") is { } mark)
            {
                var count = Math.Max(1, api.CallInt(mark, "ICenterMark", "get_GroupCount"));
                for (var i = 0; i < count; i++)
                {
                    var position = api.CallDoubles(mark, "ICenterMark", "GetPosition", i);
                    if (position.Length >= 3)
                        centers.Add(geometry.ModelPointToSheet(position[0], position[1], position[2]));
                }
            }

            marks.Add((annotation, new ExistingCenterMark(marks.Count, centers)));
        }

        return marks;
    }
}
