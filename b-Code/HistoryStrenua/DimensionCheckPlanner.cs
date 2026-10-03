using System.Globalization;

namespace HistoryStrenua;

/// <summary>检查出来的一处缺漏。</summary>
/// <param name="Text">给人看的一句（不含视图名）。</param>
/// <param name="EdgeIndex">缺的是哪个孔（<see cref="ScannedView.Edges"/> 下标），用来在 SolidWorks 里选中它；不是孔为 null。</param>
/// <param name="LineIndex">缺的是哪条外轮廓边（<see cref="ScannedView.Lines"/> 下标）；不是边为 null。</param>
internal sealed record CheckIssue(string Text, int? EdgeIndex = null, int? LineIndex = null);

/// <summary>一个视图的检查结果。</summary>
/// <param name="Issues">孔、销孔的缺漏。</param>
/// <param name="Outline">本视图里找不到尺寸的外轮廓站（是否别的视图标过由调用方再筛）。</param>
/// <param name="HoleCount">孔数（一个腰型孔算一个）。</param>
/// <param name="StationCount">外轮廓站数。</param>
internal sealed record ViewCheck(IReadOnlyList<CheckIssue> Issues, IReadOnlyList<OutlineStation> Outline, int HoleCount, int StationCount);

/// <summary>
/// 「未标尺寸」检查（1.8.0）的纯几何部分。SolidWorks 没有「工程图是否标全」的接口，这里按本模块自己的标注规则比对：
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>孔标注：每种孔（同孔标注的分种）要有一个孔标注连在这种的某个孔上。</item>
/// <item>孔位：每个孔（腰型孔取上端）水平、竖直各要有尺寸定位——有一个沿这个方向量的线性 / 坐标尺寸，某一头落在这个孔的中心线上
/// （连这个孔、连同一中心线上的别的孔、连穿过孔心的中心线都算，同孔位尺寸「按中心线标」）；阵列标法的尺寸跨度里的孔也算；
/// 正好在基准边上的不用标。</item>
/// <item>销孔：每种销孔的孔标注带 H7；相邻销孔之间的尺寸在且带对称公差（同 <see cref="DowelFitPlanner"/>）。</item>
/// <item>外轮廓：每站（<see cref="OutlinePlanner.Stations"/>）要有一个沿这个方向量的尺寸某一头落在这条边上。本视图没有的交给调用方，
/// 别的视图里有同值的外轮廓尺寸也算标过（总长总宽常在另一个视图里标）。</item>
/// </list>
/// </remarks>
internal static class DimensionCheckPlanner
{
    /// <param name="edges">视图里正对图纸的孔边。</param>
    /// <param name="lines">视图里的直边。</param>
    /// <param name="curves">曲线边近似成的线段。</param>
    /// <param name="existing">视图里已有的尺寸。</param>
    /// <param name="h7Callouts">带 H7 的孔标注在 <paramref name="existing"/> 里的下标。</param>
    /// <param name="scale">视图比例。</param>
    public static ViewCheck Check(
        IReadOnlyList<HoleEdge> edges, IReadOnlyList<SheetSegment> lines, IReadOnlyList<SheetSegment> curves,
        IReadOnlyList<ViewDimension> existing, IReadOnlySet<int> h7Callouts, double scale)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(h7Callouts);
        var issues = new List<CheckIssue>();
        var holes = HoleCalloutPlanner.Recognize(edges);
        var (leftIndex, topIndex) = HolePositionPlanner.Datums(lines);
        double? left = leftIndex is { } l ? lines[l].X1 : null;
        double? top = topIndex is { } t ? lines[t].Y1 : null;
        string Describe(HoleEdge hole) => $"⌀{Mm(2 * hole.Radius / scale)} 孔" + (left is { } x0 && top is { } y0
            ? $"（左起 {Mm((hole.X - x0) / scale)}、上起 {Mm((y0 - hole.Y) / scale)}）"
            : string.Empty);

        // 孔标注：每种一个。
        var callouts = existing.Where(dimension => dimension.HoleCallout).ToList();
        foreach (var kind in HoleCalloutPlanner.GroupKinds(holes))
        {
            if (!kind.Any(hole => CalloutOn(callouts, hole) is not null))
                issues.Add(new CheckIssue($"{Describe(kind[0])}等 {kind.Count} 个（同一种）没有孔标注", EdgeIndex: kind[0].Index));
        }

        // 孔位：每个孔两个方向。
        var positioned = existing.Where(dimension => dimension.Linear || dimension.Ordinate).ToList();
        foreach (var hole in HoleCalloutPlanner.Representatives(holes))
        {
            var missing = new List<string>();
            foreach (var axis in new[] { PositionAxis.Horizontal, PositionAxis.Vertical })
            {
                var coordinate = axis == PositionAxis.Horizontal ? hole.X : hole.Y;
                var datum = axis == PositionAxis.Horizontal ? left : top;
                if (datum is { } d && DimensionGeometry.Same(d, coordinate))
                    continue;
                if (!Located(positioned, axis, coordinate, scale))
                    missing.Add(axis == PositionAxis.Horizontal ? "水平" : "竖直");
            }

            if (missing.Count > 0)
                issues.Add(new CheckIssue($"{Describe(hole)}缺{string.Join("、", missing)}位置尺寸", EdgeIndex: hole.Index));
        }

        // 销孔：H7 与销孔间 ±0.02。
        var dowelPlan = DowelFitPlanner.Plan(edges, existing, scale, chainMode: true, left ?? 0, top ?? 0);
        foreach (var kind in dowelPlan.Kinds)
        {
            var callout = kind.Select(hole => CalloutOn(callouts, hole)).FirstOrDefault(found => found is not null);
            if (callout is not null && !h7Callouts.Contains(callout.Index))
                issues.Add(new CheckIssue($"销孔 {Describe(kind[0])}的孔标注没有 H7", EdgeIndex: kind[0].Index));
        }

        foreach (var span in dowelPlan.Spans)
        {
            var axis = span.Axis == PositionAxis.Horizontal ? "水平" : "竖直";
            var distance = Mm((span.To - span.From) / scale);
            if (span.Existing is not { } index)
                issues.Add(new CheckIssue($"相邻销孔之间缺{axis}尺寸（{distance}，应带 ±0.02）", EdgeIndex: span.FromEdgeIndex));
            else if (existing[index].ToleranceType != DimensionGeometry.ToleranceSymmetric)
                issues.Add(new CheckIssue($"销孔间{axis}尺寸 {distance} 没有 ±0.02", EdgeIndex: span.FromEdgeIndex));
        }

        // 外轮廓：本视图找不到的站。
        var stations = left is { } sl && top is { } st
            ? OutlinePlanner.Stations(lines, OutlinePlanner.OuterLines(lines, curves), sl, st, scale)
            : [];
        var uncovered = stations.Where(station => !Located(positioned, station.Axis, station.Coordinate, scale)).ToList();

        var (holeCount, _) = HoleCalloutPlanner.Count(holes);
        return new ViewCheck(issues, uncovered, holeCount, stations.Count);
    }

    /// <summary>外轮廓站的说明：「外轮廓竖直边（左起 120）」。</summary>
    public static string DescribeStation(OutlineStation station)
        => station.Axis == PositionAxis.Horizontal
            ? $"外轮廓竖直边（左起 {Mm(station.Value)}）缺水平尺寸"
            : $"外轮廓水平边（上起 {Mm(station.Value)}）缺竖直尺寸";

    /// <summary>
    /// 沿 <paramref name="axis"/> 在 <paramref name="coordinate"/> 处有没有尺寸定位：有尺寸沿这个方向量、某一头落在这条线上；
    /// 或阵列标法（前缀里有「x」）的尺寸跨度盖住它。
    /// </summary>
    public static bool Located(IReadOnlyList<ViewDimension> dimensions, PositionAxis axis, double coordinate, double scale)
        => dimensions.Any(dimension => DimensionGeometry.Measures(dimension, axis, scale)
                                       && (dimension.Anchors.Take(2).Any(anchor => anchor.Along(axis) is { } at && DimensionGeometry.Same(at, coordinate))
                                           || (dimension.Prefix.Contains(" x ", StringComparison.Ordinal)
                                               && DimensionGeometry.Span(dimension, axis) is { } span
                                               && coordinate > span.From && coordinate < span.To)));

    private static ViewDimension? CalloutOn(IEnumerable<ViewDimension> callouts, HoleEdge hole)
        => callouts.FirstOrDefault(callout => callout.Anchors.Any(anchor =>
            anchor is { Hole: true, X: { } x, Y: { } y } && HoleCalloutPlanner.SameCenter(new SheetPoint(x, y), new SheetPoint(hole.X, hole.Y))));

    /// <summary>模型长度（米）写成毫米，最多两位小数。</summary>
    public static string Mm(double meters) => Math.Round(meters * 1000, 2).ToString("0.##", CultureInfo.InvariantCulture);
}
