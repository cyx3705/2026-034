namespace HistoryStrenua;

/// <summary>参与尺寸链判断的一个线性尺寸。</summary>
/// <param name="Index"><see cref="ViewDimension.Index"/>。</param>
/// <param name="Axis">量的方向。</param>
/// <param name="From">沿量向小的一头（图纸，米）。</param>
/// <param name="To">沿量向大的一头。</param>
/// <param name="Line">尺寸线在哪一行：水平尺寸是 Y，竖直尺寸是 X（图纸，米）。</param>
internal sealed record ChainItem(int Index, PositionAxis Axis, double From, double To, double Line);

internal enum ChainFault
{
    /// <summary>和别的尺寸首尾相接，但不在一行。</summary>
    Wrapped,

    /// <summary>成了链，但整条链接不到基准。</summary>
    Unrooted,

    /// <summary>不接基准，也不接任何尺寸。</summary>
    Detached,
}

/// <summary>一个没从基准开始、也不在链上的尺寸。</summary>
/// <param name="Item">这个尺寸。</param>
/// <param name="Fault">哪一种。</param>
/// <param name="Peer">首尾相接的那个尺寸；<see cref="ChainFault.Detached"/> 没有。</param>
/// <param name="FromDatum">基准到它较远一头的距离（模型，米），改从基准标就是这个值。</param>
internal sealed record ChainIssue(ChainItem Item, ChainFault Fault, ChainItem? Peer, double FromDatum);

/// <summary>
/// 「尺寸链」（1.30.0，用户定：不从基准开始的尺寸必须在尺寸链上）的判定：同一视图、同一方向，从基准出发，沿「同一行、首尾相接」能走到的才算在链上。
/// </summary>
/// <remarks>
/// <para>基准同孔位尺寸：最左、最上的直边；贯穿视图的对称轴也算（两侧对称的跨度、从轴起量的都算从基准开始）。</para>
/// <para>不查：坐标尺寸（本身从 0 点起）、「C」倒角（量的是倒角自身）、带公差的（销孔间 ±0.02 是配合要求，贴孔放）。</para>
/// </remarks>
internal static partial class DimensionCheckPlanner
{
    /// <summary>两个尺寸算同一行的容差（图纸 0.5 mm）。</summary>
    public const double ChainLineTolerance = 5e-4;

    /// <summary>对称轴至少贯穿零件这么多才算基准（短中心线只是把圆心串起来）。</summary>
    private const double ThroughShare = 0.8;

    /// <summary>间距比这还窄（图纸 5 mm，一位数字都放不下）的尺寸可以破例离开这一行（1.32.6，用户定），链不算断。</summary>
    private const double SqueezedSpan = 5e-3;

    /// <param name="view">视图。</param>
    /// <param name="positions">尺寸文字位置（<see cref="ViewDimension.Index"/> → 图纸坐标）；读不到的不查。</param>
    public static List<ChainIssue> Chains(CheckView view, IReadOnlyDictionary<int, SheetPoint> positions)
    {
        ArgumentNullException.ThrowIfNull(view);
        var issues = new List<ChainIssue>();
        var (leftIndex, topIndex) = HolePositionPlanner.Datums(view.Lines);
        var items = ChainItems(view, positions);
        foreach (var axis in Axes)
        {
            if ((axis == PositionAxis.Horizontal ? leftIndex : topIndex) is not { } datumIndex)
                continue;
            var datum = axis == PositionAxis.Horizontal ? view.Lines[datumIndex].X1 : view.Lines[datumIndex].Y1;
            var here = items.Where(item => item.Axis == axis).ToList();
            var axes = ThroughAxes(view, axis);
            var rooted = here.Where(item => Rooted(item, datum, axes)).ToHashSet();
            // 1.34.0：同跨度的两组阵列（5 x 200 与 10 x 100）放不到一行，有一组在链上另一组就算在。
            bool Twins(ChainItem a, ChainItem b) => DimensionGeometry.Same(a.From, b.From) && DimensionGeometry.Same(a.To, b.To) && IsArray(view, a) && IsArray(view, b);
            for (var grew = true; grew;)
            {
                var next = here.Where(item => !rooted.Contains(item) && rooted.Any(other => ((SameLine(item, other) || Short(item) || Short(other)) && Abuts(item, other)) || Twins(item, other))).ToList();
                grew = next.Count > 0;
                rooted.UnionWith(next);
            }

            foreach (var item in here.Where(item => !rooted.Contains(item)))
            {
                var peer = here.Where(other => other != item && Abuts(item, other))
                    .OrderByDescending(rooted.Contains)
                    .FirstOrDefault();
                var fault = peer is null ? ChainFault.Detached : SameLine(item, peer) ? ChainFault.Unrooted : ChainFault.Wrapped;
                issues.Add(new ChainIssue(item, fault, peer, Math.Max(Math.Abs(item.From - datum), Math.Abs(item.To - datum)) / view.Frame.Scale));
            }
        }

        return issues;
    }

    /// <summary>
    /// 现在就在同一行、首尾相接的尺寸（<see cref="ViewDimension.Index"/>），每条链一组、至少两个：「整理标注」换行时要整条一起走。
    /// 在基准上碰头的不算（基准两侧的尺寸各走各的）。
    /// </summary>
    public static List<List<int>> ChainGroups(CheckView view, IReadOnlyDictionary<int, SheetPoint> positions)
    {
        ArgumentNullException.ThrowIfNull(view);
        var (leftIndex, topIndex) = HolePositionPlanner.Datums(view.Lines);
        var items = ChainItems(view, positions);
        var groups = new List<List<int>>();
        foreach (var axis in Axes)
        {
            var datumIndex = axis == PositionAxis.Horizontal ? leftIndex : topIndex;
            double? datum = datumIndex is { } found ? (axis == PositionAxis.Horizontal ? view.Lines[found].X1 : view.Lines[found].Y1) : null;
            var left = items.Where(item => item.Axis == axis).ToList();
            while (left.Count > 0)
            {
                var group = new List<ChainItem> { left[0] };
                left.RemoveAt(0);
                for (var i = 0; i < group.Count; i++)
                {
                    var joined = left.Where(other => SameLine(group[i], other) && Abuts(group[i], other) && !(datum is { } d && MeetAt(group[i], other, d))).ToList();
                    group.AddRange(joined);
                    left.RemoveAll(joined.Contains);
                }

                if (group.Count > 1)
                    groups.Add(group.Select(item => item.Index).ToList());
            }
        }

        return groups;
    }

    private static List<ChainItem> ChainItems(CheckView view, IReadOnlyDictionary<int, SheetPoint> positions)
    {
        var items = new List<ChainItem>();
        foreach (var dimension in view.Dimensions)
        {
            if (!dimension.Linear || dimension.ToleranceType != 0 || ChamferPrefix.IsMatch(dimension.Prefix) || !positions.TryGetValue(dimension.Index, out var at))
                continue;
            foreach (var axis in Axes)
            {
                if (MeasuredSpan(view, dimension, axis) is not { } span)
                    continue;
                items.Add(new ChainItem(dimension.Index, axis, span.From, span.To, axis == PositionAxis.Horizontal ? at.Y : at.X));
                break;
            }
        }

        return items;
    }

    private static bool IsArray(CheckView view, ChainItem item)
        => view.Dimensions.Any(dimension => dimension.Index == item.Index && ArrayPrefix.IsMatch(dimension.Prefix));

    private static bool Rooted(ChainItem item, double datum, IReadOnlyList<double> axes)
        => DimensionGeometry.Same(item.From, datum) || DimensionGeometry.Same(item.To, datum)
           || axes.Any(axis => DimensionGeometry.Same(item.From, axis) || DimensionGeometry.Same(item.To, axis) || DimensionGeometry.Same((item.From + item.To) / 2, axis));

    private static bool Short(ChainItem item) => item.To - item.From < SqueezedSpan;

    private static bool SameLine(ChainItem a, ChainItem b) => Math.Abs(a.Line - b.Line) <= ChainLineTolerance;

    private static bool Abuts(ChainItem a, ChainItem b)
        => DimensionGeometry.Same(a.To, b.From) || DimensionGeometry.Same(b.To, a.From);

    private static bool MeetAt(ChainItem a, ChainItem b, double datum)
        => (DimensionGeometry.Same(a.To, b.From) && DimensionGeometry.Same(a.To, datum))
           || (DimensionGeometry.Same(b.To, a.From) && DimensionGeometry.Same(b.To, datum));

    /// <summary>贯穿零件的对称轴沿 <paramref name="axis"/> 的坐标（水平量向看竖直的轴）。</summary>
    private static List<double> ThroughAxes(CheckView view, PositionAxis axis)
    {
        if (view.Lines.Count == 0)
            return [];
        var horizontal = axis == PositionAxis.Horizontal;
        var extent = horizontal
            ? view.Lines.Max(line => Math.Max(line.Y1, line.Y2)) - view.Lines.Min(line => Math.Min(line.Y1, line.Y2))
            : view.Lines.Max(line => Math.Max(line.X1, line.X2)) - view.Lines.Min(line => Math.Min(line.X1, line.X2));
        return view.CenterLines
            .Where(line => horizontal
                ? DimensionGeometry.Same(line.X1, line.X2) && Math.Abs(line.Y1 - line.Y2) >= extent * ThroughShare
                : DimensionGeometry.Same(line.Y1, line.Y2) && Math.Abs(line.X1 - line.X2) >= extent * ThroughShare)
            .Select(line => horizontal ? line.X1 : line.Y1)
            .ToList();
    }
}
