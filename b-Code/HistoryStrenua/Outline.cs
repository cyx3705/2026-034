using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「外轮廓」（1.8.0）：点一个工程图视图，以零件左侧、上侧直边为基准标出外轮廓的每个台阶（含总长总宽）。
/// </summary>
/// <remarks>
/// <para>哪些边算外轮廓、站怎么归见 <see cref="OutlinePlanner"/>；基准与孔位尺寸相同（<see cref="HolePositionPlanner.Datums"/>）。</para>
/// <para>普通模式（「尺寸链」关）：先删旧的外轮廓尺寸（两头都是外轮廓直边的线性尺寸），再每站一个从基准量起的线性尺寸，
/// 排在现有尺寸最外层之外。尺寸链模式：每个方向把还没有的站加进孔的那组坐标尺寸（0 点是基准边的那组）；还没有组就照孔位尺寸的样子
/// 新建一组（0 点在基准边、文字离直边 14 mm）。孔位尺寸重标会连 0 点删掉整组（含这里加的站），所以全流程把本步放在孔位尺寸之后。</para>
/// <para>1.8.1 跨视图不重复（用户定：高度在视图 a 标了就别在视图 b 再标）：这一页别的视图（同一模型、不是轴测图）里已有的尺寸
/// 已经定了的站不标，本视图里这样的旧外轮廓尺寸重标时删掉（判法见 <see cref="OutlineCoverage"/>）。全流程按视图顺序做，
/// 所以排在前面的视图先标、后面的只补前面没有的。</para>
/// </remarks>
internal static class Outline
{
    public static QuickCommand Command { get; } = new(
        Key: "outline",
        CommandName: StrenuaIdentity.Domain + ".hole.outline",
        Title: "外轮廓",
        Summary: "点一个工程图视图，以零件左侧、上侧直边为基准标出外轮廓每个台阶的位置（含总长总宽），照「尺寸链」开关出线性或坐标尺寸。",
        Usage: "在工程图里点一个视图（先点后按、先按后点都行，60 秒内）。外轮廓上每条竖直边、水平边各算一站（开口槽、台阶都算，封闭型腔与孔不算，斜边圆弧不标），以零件最左、最上的直边为基准：「尺寸链」关时删掉旧外轮廓尺寸后每站一个尺寸，排在已有尺寸外面；「尺寸链」开时把还没有的站加进孔的那组坐标尺寸（没有就新建一组）。这一页别的视图已经标过的（如高度）不再重复标。",
        Run: context => Run(context, null));

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="view">直接处理这个视图（全流程用）；null 时取选中的或等用户点选。</param>
    internal static QuickOutcome Run(QuickCommandContext context, object? view)
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
        if (allStations.Count == 0)
            return QuickOutcome.Ok($"视图「{viewName}」的外轮廓除基准外没有竖直 / 水平边，没有加尺寸。");

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
                    var (count, already, lost) = ExtendGroup(api, scan, ordinates, axis, scan.LineEdges[datum], scan.Lines[datum], axisStations, left, top);
                    added += count;
                    present += already;
                    failed += lost;
                }
            }
            else
            {
                var (firstHorizontal, firstVertical) = FirstTiers(api, scan, left, top);
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
            }
        }
        finally
        {
            api.Call(document, "IModelDoc2", "ClearSelection2", true);
            api.Call(document, "IModelDoc2", "GraphicsRedraw2");
        }

        var message = $"视图「{viewName}」：外轮廓 {allStations.Count} 站，"
            + (skipped.Count > 0 ? $"{skipped.Count} 站别的视图已标跳过，" : string.Empty)
            + (chain
                ? $"尺寸链模式新加 {added} 个坐标尺寸" + (present > 0 ? $"，{present} 站组里已有同值跳过" : string.Empty)
                : $"新加 {added} 个")
            + (removed > 0 ? $"（先删掉旧外轮廓尺寸 {removed} 个）" : string.Empty)
            + (failed > 0 ? $"，{failed} 个 SolidWorks 没有接受" : string.Empty)
            + "。";
        return added == 0 && failed > 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }

    /// <summary>
    /// 尺寸链模式一个方向：找 0 点连在基准边上的那组坐标尺寸（孔位尺寸建的），没有就新建 0 点；把组里还没有的站一次加进去，核对值。
    /// </summary>
    /// <returns>加上的个数、组里已有同值跳过的站数、没加上的站数。</returns>
    private static (int Added, int Present, int Failed) ExtendGroup(
        SolidWorksApi api, ScannedView scan, IReadOnlyList<ScannedDimension> ordinates, PositionAxis axis, object datumEdge, SheetSegment datumLine,
        IReadOnlyList<OutlineStation> stations, double left, double top)
    {
        bool OnDatum(DimensionAnchor anchor) => anchor.Segment is { } segment && DimensionGeometry.SameSegment(segment, datumLine);

        var members = ordinates.Where(item => item.Geometry.Anchors.Any(OnDatum)).ToList();
        var zero = members.FirstOrDefault(item => item.Geometry.Anchors.Count == 1)?.Display;
        var created = false;
        if (zero is null)
        {
            var at = axis == PositionAxis.Horizontal
                ? new SheetPoint(left, top + HolePositionPlanner.OrdinateOffset)
                : new SheetPoint(left - HolePositionPlanner.OrdinateOffset, top);
            zero = HolePosition.CreateOrdinateZero(api, scan, datumEdge, axis == PositionAxis.Horizontal, at);
            if (zero is null)
                return (0, 0, stations.Count);
            created = true;
            members = [];
        }

        var groupValues = members.Select(item => item.Geometry.Value).Append(0.0).ToList();
        var missing = OutlinePlanner.MissingFromGroup(stations, groupValues);
        if (missing.Count == 0)
            return (0, stations.Count, 0);

        var before = HolePosition.OrdinateNames(api, scan);
        HolePosition.ExtendOrdinate(api, scan, zero, () =>
            missing.All(station => HolePosition.SelectEdge(api, scan, scan.LineEdges[station.LineIndex], true)));
        var added = HolePosition.NewOrdinates(api, scan, before);
        var expected = missing.Select(station => station.Value).Order().ToList();
        if (HolePositionPlanner.SameValues(expected, HolePosition.OrdinateValues(api, added)))
            return (added.Count + (created ? 1 : 0), stations.Count - missing.Count, 0);

        HolePosition.Discard(api, scan, added);
        if (created)
            HolePosition.Discard(api, scan, zero);
        return (0, stations.Count - missing.Count, missing.Count);
    }

    /// <summary>
    /// 当前图纸页别的视图里已有尺寸定了哪些面（1.8.1）：只看同一模型（文件 + 配置）、不是轴测图的视图，
    /// 每个视图只问朝向、读尺寸，不读边。
    /// </summary>
    internal static OutlineCoverage OtherViews(QuickCommandContext context, ScannedView scan)
    {
        var api = context.Api;
        var coverage = new OutlineCoverage();
        var model = HoleScan.ModelKey(api, scan.View);
        foreach (var view in HoleScan.SheetViews(api, scan.Document))
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var name = api.CallString(view, "IView", "get_Name");
            if (name == scan.ViewName
                || api.Call(view, "IView", "get_ReferencedDocument") is null
                || HoleScan.ModelKey(api, view) != model)
                continue;
            var geometry = new HoleScan.ViewGeometry(api, context.Session.Application, view);
            if (geometry.Frame.Axonometric)
                continue;
            var other = new ScannedView(scan.Document, view, name, geometry, [], [], [], []);
            coverage.AddView(geometry.Frame, DimensionScan.Read(api, other).Select(item => item.Geometry).ToList());
        }

        return coverage;
    }

    /// <summary>选基准边与站的那条边，按方向加水平 / 竖直尺寸；建出来的不是线性尺寸就删掉。</summary>
    private static bool Insert(SolidWorksApi api, ScannedView scan, object datumEdge, object edge, PositionAxis axis, SheetPoint textAt)
    {
        api.Call(scan.Document, "IModelDoc2", "ClearSelection2", true);
        if (!HolePosition.SelectEdge(api, scan, datumEdge, false) || !HolePosition.SelectEdge(api, scan, edge, true))
            return false;
        var method = axis == PositionAxis.Horizontal ? "AddHorizontalDimension2" : "AddVerticalDimension2";
        if (api.Call(scan.Document, "IModelDoc2", method, textAt.X, textAt.Y, 0.0) is not { } created)
            return false;
        if (HolePositionPlanner.IsLinear(api.CallInt(created, "IDisplayDimension", "get_Type2")))
            return true;
        HolePosition.Discard(api, scan, created);
        return false;
    }

    /// <summary>
    /// 普通模式第一层放多远：视图里现有尺寸（孔标注除外）文字离上侧 / 左侧基准最远的再往外一层（<see cref="OutlinePlanner.FirstTier"/>）。
    /// </summary>
    private static (double Horizontal, double Vertical) FirstTiers(SolidWorksApi api, ScannedView scan, double left, double top)
    {
        var above = new List<double>();
        var beside = new List<double>();
        foreach (var item in DimensionScan.Read(api, scan).Where(item => !item.Geometry.HoleCallout))
        {
            var position = api.CallDoubles(item.Annotation, "IAnnotation", "GetPosition");
            if (position.Length < 2)
                continue;
            above.Add(position[1] - top);
            beside.Add(left - position[0]);
        }

        return (OutlinePlanner.FirstTier(above), OutlinePlanner.FirstTier(beside));
    }
}
