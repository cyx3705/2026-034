namespace HistoryStrenua;

/// <summary>外轮廓上要标位置的一站：一条竖直边（水平方向）或一条水平边（竖直方向）。</summary>
/// <param name="Axis">方向：水平量 X（竖直边），竖直量 Y（水平边）。</param>
/// <param name="LineIndex">这一站选哪条边（<see cref="ScannedView.Lines"/> 的下标）。</param>
/// <param name="Coordinate">边的坐标（图纸，米）：竖直边的 X 或水平边的 Y。</param>
/// <param name="Value">离基准的距离（模型长度，米）。</param>
internal sealed record OutlineStation(PositionAxis Axis, int LineIndex, double Coordinate, double Value);

/// <summary>普通模式下要加的一个外轮廓尺寸：从基准边到 <see cref="OutlineStation"/> 那条边。</summary>
internal sealed record OutlineDimension(OutlineStation Station, SheetPoint TextAt);

/// <summary>
/// 「外轮廓」（1.8.0）的纯几何部分：视图里哪些直边在外轮廓上、每个台阶标在哪、哪些旧尺寸是外轮廓尺寸。
/// </summary>
/// <remarks>
/// <para>外轮廓边：边两侧各取一点（离边 <see cref="SideOffset"/>），有一侧从那点朝上下左右任一方向射出去什么都碰不到，
/// 就在外轮廓上。挡射线的是视图里全部直边与曲线边（圆角取两段折线、整圆取八边形）。所以开口的槽、台阶都算外轮廓，
/// 封闭的型腔、内孔不算。只看竖直与水平的边；斜边（倒角）、圆弧不标。</para>
/// <para>每个台阶都标（用户定）：竖直的外轮廓边按 X 归成站、水平的按 Y 归成站，基准（最左竖直边、最上水平边，同孔位尺寸）不算站，
/// 最远那站就是总长 / 总宽。一站有几条边取离尺寸近的（水平尺寸在上方取最高的，竖直尺寸在左侧取最左的）。</para>
/// <para>普通模式：每站一个从基准量起的尺寸，一站一层，短的在里，从现有尺寸的最外层再往外一层起排。
/// 尺寸链模式：这些站加进孔的那组坐标尺寸（同一个 0 点），已有同值的站不重复加。</para>
/// </remarks>
internal static class OutlinePlanner
{
    /// <summary>判外轮廓时在边两侧取点离边多远（图纸上 0.05 mm）。</summary>
    public const double SideOffset = 5e-5;

    /// <param name="lines">视图里的直边。</param>
    /// <param name="curves">曲线边近似成的线段。</param>
    /// <returns>在外轮廓上的竖直 / 水平直边下标。</returns>
    public static List<int> OuterLines(IReadOnlyList<SheetSegment> lines, IReadOnlyList<SheetSegment> curves)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(curves);
        var obstacles = lines.Concat(curves).ToList();
        var outer = new List<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            var anchor = DimensionAnchor.Line(lines[i], modelLine: true);
            if ((anchor.X is null) == (anchor.Y is null))
                continue; // 斜边或退化
            var line = lines[i];
            var (mx, my) = ((line.X1 + line.X2) / 2, (line.Y1 + line.Y2) / 2);
            var (nx, ny) = anchor.X is not null ? (1.0, 0.0) : (0.0, 1.0);
            if (Outside(new SheetPoint(mx + nx * SideOffset, my + ny * SideOffset), obstacles)
                || Outside(new SheetPoint(mx - nx * SideOffset, my - ny * SideOffset), obstacles))
                outer.Add(i);
        }

        return outer;
    }

    /// <summary>从 <paramref name="point"/> 朝上下左右有一个方向什么都碰不到。</summary>
    public static bool Outside(SheetPoint point, IReadOnlyList<SheetSegment> obstacles)
    {
        foreach (var (dx, dy) in new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0) })
        {
            if (!obstacles.Any(segment => RayHits(point, dx, dy, segment)))
                return true;
        }

        return false;
    }

    /// <summary>射线 point + t·(dx,dy)（t &gt; 0）与线段相交。平行的不算相交。</summary>
    public static bool RayHits(SheetPoint point, double dx, double dy, SheetSegment segment)
    {
        var (ex, ey) = (segment.X2 - segment.X1, segment.Y2 - segment.Y1);
        var denominator = dx * ey - dy * ex;
        if (Math.Abs(denominator) < 1e-18)
            return false;
        var (wx, wy) = (segment.X1 - point.X, segment.Y1 - point.Y);
        var t = (wx * ey - wy * ex) / denominator;
        var u = (wx * dy - wy * dx) / denominator;
        return t > 0 && u >= -1e-12 && u <= 1 + 1e-12;
    }

    /// <summary>外轮廓的站，先水平（按 X 由近到远）后竖直（按 Y 由近到远）；落在基准上的不算。</summary>
    public static List<OutlineStation> Stations(IReadOnlyList<SheetSegment> lines, IReadOnlyList<int> outer, double left, double top, double scale)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(outer);
        var stations = new List<OutlineStation>();
        foreach (var axis in new[] { PositionAxis.Horizontal, PositionAxis.Vertical })
        {
            var axisStations = new List<OutlineStation>();
            foreach (var index in outer)
            {
                var anchor = DimensionAnchor.Line(lines[index], modelLine: true);
                if (anchor.Along(axis) is not { } coordinate)
                    continue;
                var offset = axis == PositionAxis.Horizontal ? coordinate - left : top - coordinate;
                if (offset <= HoleCalloutPlanner.CenterTolerance)
                    continue;
                var found = axisStations.FindIndex(station => DimensionGeometry.Same(station.Coordinate, coordinate));
                var candidate = new OutlineStation(axis, index, coordinate, offset / scale);
                if (found < 0)
                    axisStations.Add(candidate);
                else if (Closer(lines[index], lines[axisStations[found].LineIndex], axis))
                    axisStations[found] = candidate;
            }

            stations.AddRange(axisStations.OrderBy(station => station.Value));
        }

        return stations;
    }

    /// <summary>离尺寸更近：水平尺寸在上方，比谁的上端高；竖直尺寸在左侧，比谁的左端靠左。</summary>
    private static bool Closer(SheetSegment a, SheetSegment b, PositionAxis axis)
        => axis == PositionAxis.Horizontal
            ? Math.Max(a.Y1, a.Y2) > Math.Max(b.Y1, b.Y2)
            : Math.Min(a.X1, a.X2) < Math.Min(b.X1, b.X2);

    /// <summary>
    /// 普通模式的外轮廓尺寸：每站一个、从基准量起，一站一层、近的在里；水平的第一层在上侧基准之上 <paramref name="firstHorizontal"/>，
    /// 竖直的第一层在左侧基准之左 <paramref name="firstVertical"/>，层距同孔位尺寸（<see cref="HolePositionPlanner.TierStep"/>）。
    /// </summary>
    public static List<OutlineDimension> Dimensions(
        IReadOnlyList<OutlineStation> stations, double left, double top, double firstHorizontal, double firstVertical)
    {
        var dimensions = new List<OutlineDimension>();
        foreach (var axis in new[] { PositionAxis.Horizontal, PositionAxis.Vertical })
        {
            var tier = 0;
            foreach (var station in stations.Where(station => station.Axis == axis).OrderBy(station => station.Value))
            {
                var textAt = axis == PositionAxis.Horizontal
                    ? new SheetPoint((left + station.Coordinate) / 2, top + firstHorizontal + tier * HolePositionPlanner.TierStep)
                    : new SheetPoint(left - firstVertical - tier * HolePositionPlanner.TierStep, (top + station.Coordinate) / 2);
                dimensions.Add(new OutlineDimension(station, textAt));
                tier++;
            }
        }

        return dimensions;
    }

    /// <summary>
    /// 第一层放多远：现有尺寸文字离基准最远的那个再往外一层；没有现有尺寸就是孔位尺寸的第一层（<see cref="HolePositionPlanner.FirstTier"/>）。
    /// </summary>
    /// <param name="distances">现有尺寸文字离基准边的距离（图纸，米；只算基准外侧的）。</param>
    public static double FirstTier(IEnumerable<double> distances)
    {
        var outermost = distances.Where(distance => distance > 0).DefaultIfEmpty(0).Max();
        return outermost <= 0 ? HolePositionPlanner.FirstTier : outermost + HolePositionPlanner.TierStep;
    }

    /// <summary>
    /// 旧的外轮廓尺寸（「重新标」时删）：线性尺寸，至少两头，每一头都是外轮廓直边（含基准）。孔位尺寸连着孔或中心线，不会认成它。
    /// </summary>
    /// <param name="existing">视图里已有的尺寸。</param>
    /// <param name="outerSegments">外轮廓直边。</param>
    /// <param name="includeOrdinates">
    /// 也删坐标尺寸里的外轮廓站（普通模式用，切回普通模式重标时不留尺寸链模式的）：两头都是外轮廓直边的坐标尺寸；
    /// 0 点（只连一条外轮廓边）只在它那组没有别的成员（孔）时才删——孔的那组留给孔位尺寸处理。
    /// </param>
    public static List<int> Obsolete(IReadOnlyList<ViewDimension> existing, IReadOnlyList<SheetSegment> outerSegments, bool includeOrdinates = false)
    {
        bool OnOutline(DimensionAnchor anchor)
            => anchor.ModelLine && anchor.Segment is { } segment && outerSegments.Any(outer => DimensionGeometry.SameSegment(outer, segment));
        bool AllOutline(ViewDimension dimension) => dimension.Anchors.Count >= 2 && dimension.Anchors.All(OnOutline);

        var obsolete = existing.Where(dimension => dimension.Linear && AllOutline(dimension)).Select(dimension => dimension.Index).ToList();
        if (!includeOrdinates)
            return obsolete;

        var ordinates = existing.Where(dimension => dimension.Ordinate).ToList();
        obsolete.AddRange(ordinates.Where(AllOutline).Select(dimension => dimension.Index));
        foreach (var zero in ordinates.Where(dimension => dimension.Anchors.Count == 1 && OnOutline(dimension.Anchors[0])))
        {
            var datum = zero.Anchors[0].Segment!.Value;
            var members = ordinates.Where(dimension => dimension != zero
                && dimension.Anchors.Any(anchor => anchor.Segment is { } segment && DimensionGeometry.SameSegment(segment, datum)));
            if (members.All(AllOutline))
                obsolete.Add(zero.Index);
        }

        return obsolete;
    }

    /// <summary>
    /// 尺寸链模式下组里那些别的视图已经定了的外轮廓站（1.8.1，重标时删；普通模式由 <see cref="Obsolete"/> 连同全部外轮廓坐标尺寸删）：
    /// 两头都是外轮廓直边、沿它量的方向两头的边 <paramref name="determined"/> 说已定了的坐标尺寸。
    /// 一组的成员（不含 0 点）要删光了，0 点一起删——还要标的站由 <c>Outline.ExtendGroup</c> 照常新建 0 点。
    /// </summary>
    /// <param name="existing">视图里已有的尺寸。</param>
    /// <param name="outerSegments">外轮廓直边。</param>
    /// <param name="scale">视图比例。</param>
    /// <param name="determined">沿某方向坐标为两个值的两条边之间，别的视图是不是已经定了（<see cref="OutlineCoverage.Determined"/>）。</param>
    public static List<int> CoveredOrdinates(
        IReadOnlyList<ViewDimension> existing, IReadOnlyList<SheetSegment> outerSegments, double scale,
        Func<PositionAxis, double, double, bool> determined)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(determined);
        bool OnOutline(DimensionAnchor anchor)
            => anchor.ModelLine && anchor.Segment is { } segment && outerSegments.Any(outer => DimensionGeometry.SameSegment(outer, segment));
        bool Covered(ViewDimension dimension)
            => dimension.Anchors.Count >= 2 && dimension.Anchors.Take(2).All(OnOutline)
               && new[] { PositionAxis.Horizontal, PositionAxis.Vertical }.Any(axis =>
                   DimensionGeometry.Span(dimension, axis) is { } span
                   && DimensionGeometry.Measures(dimension, axis, scale)
                   && determined(axis, span.From, span.To));

        var ordinates = existing.Where(dimension => dimension.Ordinate).ToList();
        var covered = ordinates.Where(Covered).Select(dimension => dimension.Index).ToHashSet();
        foreach (var zero in ordinates.Where(dimension => dimension.Anchors.Count == 1 && OnOutline(dimension.Anchors[0])))
        {
            var datum = zero.Anchors[0].Segment!.Value;
            var members = ordinates.Where(dimension => dimension != zero
                && dimension.Anchors.Any(anchor => anchor.Segment is { } segment && DimensionGeometry.SameSegment(segment, datum))).ToList();
            if (members.Count > 0 && members.All(member => covered.Contains(member.Index)))
                covered.Add(zero.Index);
        }

        return covered.Order().ToList();
    }

    /// <summary>坐标尺寸组里还没有的站：组里已有同值（孔、外轮廓或圆心）的就不再加。</summary>
    public static List<T> MissingFromGroup<T>(IReadOnlyList<T> stations, Func<T, double> valueOf, IReadOnlyList<double> groupValues)
        => stations
            .Where(station => !groupValues.Any(value => Math.Abs(value - valueOf(station)) <= DimensionGeometry.ValueTolerance))
            .ToList();
}
