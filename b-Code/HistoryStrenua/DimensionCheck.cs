using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「未标尺寸」（1.8.0，检查类）：当前图纸页全部视图查孔与外轮廓有没有漏标的尺寸，列进控制台，并在 SolidWorks 里选中漏标的孔与边。
/// </summary>
/// <remarks>
/// <para>查什么、怎么算标过见 <see cref="DimensionCheckPlanner"/>。只读：不加、不删、不挪任何注解，只改选择。</para>
/// <para>范围同「孔标注全流程」：当前图纸页、不用点视图。外轮廓站本视图没标、而这一页别的视图里有同值的外轮廓尺寸（两头都是模型直边）也算标过。</para>
/// <para>查出缺漏也回执成功（检查本身做完了），消息里写明几处。</para>
/// </remarks>
internal static class DimensionCheck
{
    public static QuickCommand Command { get; } = new(
        Key: "check-dimension",
        CommandName: StrenuaIdentity.Domain + ".check.dimension",
        Title: "未标尺寸",
        Summary: "当前图纸页全部视图查孔（孔标注、两向位置、销孔 H7 与 ±0.02）和外轮廓台阶有没有漏标的尺寸，只列出不改图。",
        Usage: "不用点视图：当前图纸页上全部视图逐个检查——每种孔有没有孔标注、每个孔水平竖直有没有定位尺寸（按中心线算，阵列标法跨度里的也算）、销孔孔标注带没带 H7、相邻销孔间尺寸在不在且带 ±0.02、外轮廓每个台阶有没有尺寸（别的视图标过同值的也算）。缺的列进控制台，并在 SolidWorks 里选中漏标的孔和边；不加、不删任何尺寸。",
        Run: Run);

    // swCalloutVariableType_e
    private const int CalloutLength = 1;

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var api = context.Api;
        var document = HoleScan.ActiveDrawing(context);
        var views = HoleScan.SheetViews(api, document);
        if (views.Count == 0)
            return QuickOutcome.Ok("当前图纸页上没有视图，没有可检查的。");

        context.SetState("检查");
        var checks = new List<(ScannedView Scan, ViewCheck Check)>();
        var outlineValues = new List<double>();
        for (var i = 0; i < views.Count; i++)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var view = views[i];
            if (api.Call(view, "IView", "get_ReferencedDocument") is null)
                continue;
            context.Report($"未标尺寸：检查视图 {i + 1}/{views.Count}「{api.CallString(view, "IView", "get_Name")}」。");
            var scan = HoleScan.Scan(context, "未标尺寸", withLines: true, view: view);
            var dimensions = DimensionScan.Read(api, scan);
            var geometry = dimensions.Select(item => item.Geometry).ToList();
            var h7 = dimensions
                .Where(item => item.Geometry.HoleCallout && HasH7(api, item.Display))
                .Select(item => item.Geometry.Index)
                .ToHashSet();
            checks.Add((scan, DimensionCheckPlanner.Check(scan.Candidates, scan.Lines, scan.CurveSegments, geometry, h7, scan.Geometry.Scale)));
            outlineValues.AddRange(geometry
                .Where(dimension => (dimension.Linear || dimension.Ordinate) && dimension.Anchors.Any(anchor => anchor.ModelLine))
                .Select(dimension => Math.Abs(dimension.Value)));
        }

        var lines = new List<string>();
        var total = 0;
        api.Call(document, "IModelDoc2", "ClearSelection2", true);
        foreach (var (scan, check) in checks)
        {
            var issues = check.Issues
                .Concat(check.Outline
                    .Where(station => !outlineValues.Any(value => Math.Abs(value - station.Value) <= DimensionGeometry.ValueTolerance))
                    .Select(station => new CheckIssue(DimensionCheckPlanner.DescribeStation(station), LineIndex: station.LineIndex)))
                .ToList();
            if (issues.Count == 0)
            {
                lines.Add($"视图「{scan.ViewName}」：{check.HoleCount} 个孔、外轮廓 {check.StationCount} 站，没有漏标。");
                continue;
            }

            total += issues.Count;
            lines.Add($"视图「{scan.ViewName}」：{issues.Count} 处漏标——");
            lines.AddRange(issues.Select(issue => "  · " + issue.Text));
            foreach (var issue in issues)
            {
                var entity = issue.EdgeIndex is { } edge ? scan.Edges[edge] : issue.LineIndex is { } line ? scan.LineEdges[line] : null;
                if (entity is not null)
                    api.CallBool(scan.View, "IView", "SelectEntity", entity, true);
            }
        }

        api.Call(document, "IModelDoc2", "GraphicsRedraw2");
        var head = total == 0
            ? $"未标尺寸：当前图纸页 {checks.Count} 个视图都没有查出漏标。"
            : $"未标尺寸：当前图纸页 {checks.Count} 个视图共 {total} 处漏标，已在 SolidWorks 里选中漏标的孔和边。";
        return QuickOutcome.Ok(head + Environment.NewLine + string.Join(Environment.NewLine, lines));
    }

    /// <summary>孔标注里有没有长度变量是「配合（带公差）」H7。</summary>
    private static bool HasH7(SolidWorksApi api, object callout)
        => api.CallArray(callout, "IDisplayDimension", "GetHoleCalloutVariables").Any(variable =>
            variable is not null
            && api.CallInt(variable, "ICalloutVariable", "get_Type") == CalloutLength
            && api.CallInt(variable, "ICalloutVariable", "get_ToleranceType") is DimensionGeometry.ToleranceFitWithTolerance or 7 or 9
            && api.CallString(variable, "ICalloutVariable", "get_HoleFit") == DowelFitPlanner.HoleFit);
}
