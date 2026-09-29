namespace HistoryStrenua;

/// <summary>视图里已有的一个中心符号线（单个或一组），用它所标的孔心来描述。</summary>
/// <param name="Index">回指调用方手里那个注解。</param>
/// <param name="Centers">它标着的孔心（图纸坐标）；一组有几个就有几个。</param>
internal sealed record ExistingCenterMark(int Index, IReadOnlyList<SheetPoint> Centers);

/// <summary>要加的一组中心符号线：一种孔一组。</summary>
/// <param name="EdgeIndices">这一种里每个孔取一条边，按图纸上从上到下、从左到右排。</param>
internal sealed record CenterMarkGroup(IReadOnlyList<int> EdgeIndices)
{
    /// <summary>两个孔以上用「线性中心符号线 + 连接线」；只有一个孔就是单个中心符号线。</summary>
    public bool Linear => EdgeIndices.Count > 1;
}

/// <summary>规划结果。</summary>
/// <param name="Groups">要加的组，一种孔一组，按每组第一个孔在图纸上的阅读顺序排。</param>
/// <param name="Obsolete">要先删掉的旧中心符号线（<see cref="ExistingCenterMark.Index"/>）。</param>
/// <param name="HoleCount">视图里认出的孔数（同心的算一个）。</param>
/// <param name="KindCount">孔的种数。</param>
internal sealed record CenterMarkPlan(IReadOnlyList<CenterMarkGroup> Groups, IReadOnlyList<int> Obsolete, int HoleCount, int KindCount);

/// <summary>
/// 「中心符号线」的纯几何部分：孔怎么分组、哪些旧符号线要删。认孔与分种与孔标注完全相同
/// （<see cref="HoleCalloutPlanner.MergeConcentric"/>、<see cref="HoleCalloutPlanner.GroupKinds"/>）。
/// </summary>
internal static class CenterMarkPlanner
{
    /// <param name="edges">视图里正对图纸的孔边。</param>
    /// <param name="existing">视图里已有的中心符号线。</param>
    public static CenterMarkPlan Plan(IReadOnlyList<HoleEdge> edges, IReadOnlyList<ExistingCenterMark> existing)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(existing);

        var holes = HoleCalloutPlanner.MergeConcentric(edges);
        var kinds = HoleCalloutPlanner.GroupKinds(holes);
        var groups = kinds
            .OrderByDescending(kind => Math.Round(kind[0].Y / HoleCalloutPlanner.CenterTolerance))
            .ThenBy(kind => kind[0].X)
            .Select(kind => new CenterMarkGroup(kind.Select(hole => hole.Index).ToList()))
            .ToList();
        return new CenterMarkPlan(groups, Obsolete(holes, existing), holes.Count, kinds.Count);
    }

    /// <summary>
    /// 标着任何一个孔的旧中心符号线都要删——「重新标记」。一组里只要有一个孔心落在孔上，整组删：
    /// 新的一组会按本次的分种重建。不在孔上的（圆角、槽口的中心符号线）不动。
    /// </summary>
    public static List<int> Obsolete(IReadOnlyList<HoleEdge> holes, IReadOnlyList<ExistingCenterMark> existing)
        => existing
            .Where(mark => mark.Centers.Any(center =>
                holes.Any(hole => HoleCalloutPlanner.SameCenter(center, new SheetPoint(hole.X, hole.Y)))))
            .Select(mark => mark.Index)
            .ToList();
}
