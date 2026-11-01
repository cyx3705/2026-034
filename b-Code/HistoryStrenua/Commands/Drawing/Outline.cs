using HistoryStrenua.SolidWorks;
using static HistoryStrenua.DimensionEdit;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「外轮廓」（1.8.0）：点一个工程图视图，以零件左侧、上侧直边为基准标出外轮廓的每个台阶（含总长总宽）。
/// </summary>
/// <remarks>
/// <para>1.17.0（用户定）：从孔类挪到基础类（<c>strenua.hole.outline</c> → <c>strenua.drawing.outline</c>），「孔标注全流程」不再含这一步；
/// 全页做一遍是「全图外轮廓」（<see cref="OutlineAll"/>），一键类的各条在孔标注全流程之后调它（尺寸链模式下它往孔的那组里加站，必须在孔位尺寸之后）。
/// 钣金折弯处的线（折弯切线、折弯区里的短边）不在 <see cref="ScannedView.Lines"/> 里（<see cref="SheetMetal"/>），这里自然不标、也不当基准。</para>
/// <para>哪些边算外轮廓、站怎么归见 <see cref="OutlinePlanner"/>；基准与孔位尺寸相同（<see cref="HolePositionPlanner.Datums"/>）。</para>
/// <para>普通模式（「尺寸链」关）：先删旧的外轮廓尺寸（两头都是外轮廓直边的线性尺寸），再每站一个从基准量起的线性尺寸，
/// 排在现有尺寸最外层之外。尺寸链模式：每个方向把还没有的站加进孔的那组坐标尺寸（0 点是基准边的那组）；还没有组就照孔位尺寸的样子
/// 新建一组（0 点在基准边、文字离直边 14 mm）。孔位尺寸重标会连 0 点删掉整组（含这里加的站），所以全流程把本步放在孔位尺寸之后。</para>
/// <para>1.8.1 跨视图不重复（用户定：高度在视图 a 标了就别在视图 b 再标）：这一页别的视图（同一模型、不是轴测图）里已有的尺寸
/// 已经定了的站不标，本视图里这样的旧外轮廓尺寸重标时删掉（判法见 <see cref="OutlineCoverage"/>）。全流程按视图顺序做，
/// 所以排在前面的视图先标、后面的只补前面没有的。</para>
/// <para>1.11.0 挂上避障（用户定：加东西的指令都挂上）：普通模式加完后，「避障」开着就把这一轮新加的外轮廓尺寸里数字压在孔相关注解
/// 或别的线性尺寸上的沿尺寸线滑开（<see cref="Clearance.ClearOutline"/>）。尺寸链模式是坐标尺寸，SolidWorks 自己折弯排开，不挪。</para>
/// </remarks>
internal static class Outline
{
    public static QuickCommand Command { get; } = new(
        Key: "outline",
        CommandName: StrenuaIdentity.Domain + ".drawing.outline",
        Title: "外轮廓",
        Summary: "点一个工程图视图，以零件左侧、上侧直边为基准标出外轮廓每个台阶的位置（含总长总宽），照「尺寸链」开关出线性或坐标尺寸。",
        Usage: "在工程图里点一个视图（先点后按、先按后点都行，60 秒内）。外轮廓上每条竖直边、水平边各算一站（开口槽、台阶都算，封闭型腔与孔不算，斜边圆弧不标；钣金折弯切线与离外轮廓不到一个折弯外半径的短边不算），以零件最左、最上的直边为基准：「尺寸链」关时删掉旧外轮廓尺寸后每站一个尺寸，排在已有尺寸外面；「尺寸链」开时把还没有的站加进孔的那组坐标尺寸（没有就新建一组，所以要在孔位尺寸之后按）。这一页别的视图已经标过的（如高度）不再重复标。「避障」开着时，新加尺寸的数字压在别的尺寸、孔标注、中心符号线上就沿尺寸线滑开。",
        Run: context => Run(context, null),
        Row: 2, Page: "onekey");

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="view">直接处理这个视图（全图外轮廓用）；null 时取选中的或等用户点选。</param>
    /// <param name="minGap">
    /// 框架（1.17.0，「一键出框架」）：离已留下的站（或基准）不超过这么远的站不标（模型长度，米；0 不筛），见 <see cref="OutlinePlanner.Sparse"/>。
    /// </param>
    internal static QuickOutcome Run(QuickCommandContext context, object? view, double minGap = 0)
    {
        var api = context.Api;
        var scan = HoleScan.Scan(context, "外轮廓", withLines: true, view: view);
        var (document, viewName) = (scan.Document, scan.ViewName);
        var (leftIndex, topIndex) = HolePositionPlanner.Datums(scan.Lines);
        if (leftIndex is not { } l || topIndex is not { } t)
        {
            return QuickOutcome.Fail($"视图「{viewName}」里找不到"
                + (leftIndex is null ? "竖直的直边做左侧基准" : "水平的直边做上侧基准")
                + "，外轮廓没有标。");
        }

        var (left, top, scale) = (scan.Lines[l].X1, scan.Lines[t].Y1, scan.Geometry.Scale);
        var outer = OutlinePlanner.OuterLines(scan.Lines, scan.CurveSegments);
        var outerSegments = outer.Select(index => scan.Lines[index]).ToList();
        var allStations = OutlinePlanner.Stations(scan.Lines, outer, left, top, scale);
        var sparse = 0;
        if (minGap > 0)
        {
            var kept = OutlinePlanner.Sparse(allStations, minGap);
            sparse = allStations.Count - kept.Count;
            allStations = kept;
        }

        if (allStations.Count == 0)
            return QuickOutcome.Nothing($"视图「{viewName}」的外轮廓除基准外没有竖直 / 水平边，没有加尺寸。");

        // 1.8.1：别的视图（同一模型、不是轴测图）已经定了的站不再标；这一页别的视图读一遍尺寸。
        context.SetState("读别的视图");
        var frame = scan.Geometry.Frame;
        var coverage = OtherViews(context, scan);
        var (stations, skipped) = coverage.Split(frame, allStations, left, top);

        var chain = context.Options.Chain;
        var horizontalCount = allStations.Count(station => station.Axis == PositionAxis.Horizontal);
        context.Report($"外轮廓：视图「{viewName}」外轮廓 {outer.Count} 条直边，水平 {horizontalCount} 站、竖直 {allStations.Count - horizontalCount} 站"
            + (skipped.Count > 0 ? $"，其中 {skipped.Count} 站别的视图已标" : string.Empty)
            + (chain ? "（尺寸链模式）。" : "。"));
        _ = api.Call(document, "IDrawingDoc", "ActivateView", viewName);

        var added = 0;
        var present = 0;
        var removed = 0;
        var failed = 0;
        var clearance = default(ClearanceResult);
        try
        {
            // 两种模式都先删旧的外轮廓线性尺寸（来回切换不留另一种的）；普通模式连尺寸链模式加的外轮廓坐标尺寸一起删。
            context.SetState("删旧尺寸");
            (removed, var leftover) = AnnotationEraser.Erase(context, document, () =>
            {
                var dimensions = DimensionScan.Read(api, scan);
                var geometry = dimensions.Select(item => item.Geometry).ToList();
                var obsolete = OutlinePlanner.Obsolete(geometry, outerSegments, includeOrdinates: !chain);
                if (chain)
                {
                    // 尺寸链模式不删组，只把组里别的视图已经定了的站（连同删光了成员的 0 点）拿掉。
                    obsolete.AddRange(OutlinePlanner.CoveredOrdinates(geometry, outerSegments, scale,
                        (axis, from, to) => coverage.Determined(frame, axis, from, to)));
                }

                return obsolete.Distinct().Select(index => dimensions[index].Annotation).ToList();
            });
            if (leftover > 0)
            {
                return QuickOutcome.Fail($"视图「{viewName}」：有 {leftover} 个旧外轮廓尺寸删不掉，没有重标（已删 {removed} 个）。"
                    + "请在 SolidWorks 里手工删掉后再按。");
            }

            context.SetState("加尺寸");
            if (chain)
            {
                // 现有坐标尺寸读一次，两个方向共用（各方向的组互不相干，加了水平组不影响竖直组的判断）。
                var ordinates = DimensionScan.Read(api, scan).Where(item => item.Geometry.Ordinate).ToList();
                foreach (var axis in new[] { PositionAxis.Horizontal, PositionAxis.Vertical })
                {
                    context.Cancellation.ThrowIfCancellationRequested();
                    var axisStations = stations.Where(station => station.Axis == axis).ToList();
                    if (axisStations.Count == 0)
                        continue;
                    var datum = axis == PositionAxis.Horizontal ? l : t;
                    var stops = axisStations.Select(station => new GroupStop(scan.LineEdges[station.LineIndex], station.Value)).ToList();
                    var (count, already, lost) = ExtendGroup(api, scan, ordinates, axis, scan.LineEdges[datum], scan.Lines[datum], stops, left, top);
                    added += count;
                    present += already;
                    failed += lost;
                }
            }
            else
            {
                var (firstHorizontal, firstVertical) = FirstTiers(api, scan, left, top);
                var before = DimensionNames(api, scan);
                foreach (var dimension in OutlinePlanner.Dimensions(stations, left, top, firstHorizontal, firstVertical))
                {
                    context.Cancellation.ThrowIfCancellationRequested();
                    var station = dimension.Station;
                    var datum = scan.LineEdges[station.Axis == PositionAxis.Horizontal ? l : t];
                    if (Insert(api, scan, datum, scan.LineEdges[station.LineIndex], station.Axis, dimension.TextAt))
                        added++;
                    else
                        failed++;
                }

                if (context.Options.Clearance && added > 0)
                {
                    context.SetState("避障");
                    var fresh = DimensionNames(api, scan);
                    fresh.ExceptWith(before);
                    clearance = Clearance.ClearOutline(context, scan, HoleCalloutPlanner.Recognize(scan.Candidates), fresh);
                }
            }
        }
        finally
        {
            api.ClearSelection(document);
            api.Redraw(document);
        }

        var message = $"视图「{viewName}」：外轮廓 {allStations.Count} 站，"
            + (scan.BendLines > 0 ? $"钣金折弯处的线 {scan.BendLines} 条不算，" : string.Empty)
            + (sparse > 0 ? $"离别的站不到型材宽度的 {sparse} 站不标，" : string.Empty)
            + (skipped.Count > 0 ? $"{skipped.Count} 站别的视图已标跳过，" : string.Empty)
            + (chain
                ? $"尺寸链模式新加 {added} 个坐标尺寸" + (present > 0 ? $"，{present} 站组里已有同值跳过" : string.Empty)
                : $"新加 {added} 个")
            + (removed > 0 ? $"（先删掉旧外轮廓尺寸 {removed} 个）" : string.Empty)
            + (failed > 0 ? $"，{failed} 个 SolidWorks 没有接受" : string.Empty)
            + clearance.Describe("尺寸数字")
            + "。";
        return added == 0 && failed > 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }

    /// <summary>
    /// 当前图纸页别的视图里已有尺寸定了哪些面（1.8.1）：只看同一模型（文件 + 配置）、不是轴测图的视图，
    /// 每个视图只问朝向、读尺寸，不读边。
    /// </summary>
    internal static OutlineCoverage OtherViews(QuickCommandContext context, ScannedView scan)
    {
        var api = context.Api;
        var coverage = new OutlineCoverage();
        var model = DrawingAccess.ModelKey(api, scan.View);
        foreach (var view in DrawingAccess.SheetViews(api, scan.Document))
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var name = api.CallString(view, "IView", "get_Name");
            if (name == scan.ViewName
                || api.Call(view, "IView", "get_ReferencedDocument") is null
                || DrawingAccess.ModelKey(api, view) != model)
                continue;
            var geometry = new ViewGeometry(api, view);
            if (geometry.Frame.Axonometric)
                continue;
            var other = new ScannedView(scan.Document, view, name, geometry, [], [], [], []);
            coverage.AddView(geometry.Frame, DimensionScan.Read(api, other).Select(item => item.Geometry).ToList());
        }

        return coverage;
    }
}

