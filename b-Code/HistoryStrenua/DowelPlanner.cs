namespace HistoryStrenua;

/// <summary>规划结果。</summary>
/// <param name="EdgeIndices">要加销钉符号的孔（每孔一条边），按图纸上从上到下、从左到右排。</param>
/// <param name="DowelCount">视图里认出的销钉孔个数。</param>
/// <param name="AlreadyMarked">已有销钉符号、本次跳过的个数。</param>
internal sealed record DowelPlan(IReadOnlyList<int> EdgeIndices, int DowelCount, int AlreadyMarked);

/// <summary>
/// 「销钉符号」的纯几何部分：视图里哪些孔是销钉孔、哪些还没有销钉符号。
/// 认孔、同心合并与另外三条孔类指令相同（<see cref="HoleCalloutPlanner.Recognize"/>）。
/// </summary>
/// <remarks>
/// 销钉孔只认异形孔向导做的（孔壁特征是销钉孔类型，见 <c>HoleScan.ViewGeometry</c>）：
/// 手工切出来的孔看不出是不是给销钉用的。销钉孔是圆孔，腰型孔的端头不算。
/// 销钉符号一孔一个，已有的不重复加（不删旧的，与中心符号线、孔位尺寸的「重新标」不同）。
/// </remarks>
internal static class DowelPlanner
{
    // swWzdHoleStandardFastenerTypes_e：swStandardAnsiInchDowelHole … swStandardKSDowelHole
    private const int FirstDowelFastener = 703;
    private const int LastDowelFastener = 712;

    /// <summary>异形孔向导的紧固件类型是不是某个标准的销钉孔。</summary>
    public static bool IsDowelFastener(int fastenerType) => fastenerType is >= FirstDowelFastener and <= LastDowelFastener;

    /// <param name="edges">视图里正对图纸的孔边。</param>
    /// <param name="markedCenters">视图里已有销钉符号所在的孔心（图纸坐标）。</param>
    public static DowelPlan Plan(IReadOnlyList<HoleEdge> edges, IReadOnlyList<SheetPoint> markedCenters)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(markedCenters);

        var dowels = HoleCalloutPlanner.ReadingOrder(HoleCalloutPlanner.Recognize(edges).Where(hole => hole.Dowel && hole.Slot < 0)).ToList();
        var todo = dowels
            .Where(hole => !markedCenters.Any(center => HoleCalloutPlanner.SameCenter(center, new SheetPoint(hole.X, hole.Y))))
            .Select(hole => hole.Index)
            .ToList();
        return new DowelPlan(todo, dowels.Count, dowels.Count - todo.Count);
    }

    /// <summary>
    /// <c>IDowelSymbol.GetArcPoints</c>（图纸坐标 起点 xy、中点 xy、终点 xy）所在的圆心：整圆时起点与终点重合、
    /// 中点在 180° 处，圆心是起点与中点的中点。读不出返回 null。
    /// </summary>
    public static SheetPoint? ArcCenter(IReadOnlyList<double> points)
        => points.Count >= 4 ? new SheetPoint((points[0] + points[2]) / 2, (points[1] + points[3]) / 2) : null;
}
