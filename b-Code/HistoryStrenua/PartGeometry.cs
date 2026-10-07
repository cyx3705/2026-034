namespace HistoryStrenua;

/// <summary>模型坐标系里与坐标轴对齐的包围盒（米）。</summary>
internal readonly record struct ModelBox(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)
{
    /// <summary>沿某个方向的跨度（方向不必是轴向：八个角投影后取最大减最小）。</summary>
    public double Extent(ModelDirection direction)
    {
        var values = Corners().Select(corner => corner.Dot(direction)).ToList();
        return values.Max() - values.Min();
    }

    /// <summary>八个角，当作方向向量返回（便于点积）。</summary>
    public IEnumerable<ModelDirection> Corners()
    {
        foreach (var x in new[] { MinX, MaxX })
        foreach (var y in new[] { MinY, MaxY })
        foreach (var z in new[] { MinZ, MaxZ })
            yield return new ModelDirection(x, y, z);
    }
}

/// <summary>
/// 零件上一张圆柱面（1.9.0「新建工程图」挑视图用）：孔壁、圆角、凸台外圆、腰型孔端头。
/// </summary>
/// <param name="Axis">轴向（单位向量，符号不定）。</param>
/// <param name="Point">轴线上的一点（米，当作向量存）。</param>
/// <param name="Radius">半径（米）。</param>
/// <param name="Concave">内凹：面法向指向轴线（孔壁、内圆角）。判法与认孔相同（DEC-005）。</param>
/// <param name="Full">整圈：边里有一条没有端点的整圆（孔、凸台）；圆角、腰型孔端头只有一段弧。</param>
/// <param name="Feature">所属特征名，取不到为空。</param>
/// <param name="Openings">孔口朝哪些方向（只对内凹整圈的孔有意义）：整圆边旁边那张平面的外法向，且这张平面在圆柱面的这一头。</param>
/// <param name="AxialMin">
/// 面沿 <paramref name="Axis"/> 的起止（面包围盒八个角点乘轴向的最小 / 最大，米；1.14.1）。同一根轴线上的两段圆柱面只有首尾相接才是同一个孔——
/// 板两端面同位置的盲孔共轴却隔着整块板，首版不看这个，把它们并成一个孔。读不到（旧夹具）为 NaN，照旧按共轴并。
/// </param>
/// <param name="AxialMax">同上，最大。</param>
internal sealed record PartCylinder(
    ModelDirection Axis,
    ModelDirection Point,
    double Radius,
    bool Concave,
    bool Full,
    string Feature,
    IReadOnlyList<ModelDirection> Openings,
    double AxialMin = double.NaN,
    double AxialMax = double.NaN);

/// <summary>
/// 零件上一张平面倒角（1.10.0）：倒角特征做的平面。沿 <see cref="Axis"/> 看它侧着成一条斜线，倒角尺寸要标在这样的视图里。
/// </summary>
/// <param name="Axis">倒掉的那条棱的方向（沿坐标轴的单位向量，正向）。</param>
/// <param name="LegA">一条直角边（米）。</param>
/// <param name="LegB">另一条直角边（米）。</param>
internal sealed record PartChamfer(ModelDirection Axis, double LegA, double LegB)
{
    /// <summary>C1（技术要求「未注倒角C1」，不标，也不为它加视图）。</summary>
    public bool Default => Math.Abs(LegA - ChamferPlanner.DefaultLeg) <= ChamferPlanner.SizeTolerance
                           && Math.Abs(LegB - ChamferPlanner.DefaultLeg) <= ChamferPlanner.SizeTolerance;
}

/// <summary>
/// 零件的几何摘要：包围盒、全部圆柱面、全部平面的外法向、平面上的「窗口」、平面倒角。由 <c>PartScan</c> 从 SolidWorks 读出，规划器只看它。
/// </summary>
/// <param name="Box">包围盒。</param>
/// <param name="Cylinders">全部圆柱面。</param>
/// <param name="PlaneNormals">全部平面的外法向。</param>
/// <param name="WindowNormals">
/// 窗口（1.9.0）：平面上不是单个整圆的内环（方窗、异形切口、腰型孔口），每个记一次所在平面的外法向。
/// 外壳2 顶面两个方窗口只有沿这个方向看得到，首版只认孔和圆弧，整张图漏了这一面。
/// </param>
/// <param name="ChamferFaces">平面倒角（1.10.0）：限位块右的 C5 只有从端头看才成斜线，1.9.0 没有这个视图，倒角标不上。</param>
internal sealed record PartGeometry(
    ModelBox Box,
    IReadOnlyList<PartCylinder> Cylinders,
    IReadOnlyList<ModelDirection>? PlaneNormals = null,
    IReadOnlyList<ModelDirection>? WindowNormals = null,
    IReadOnlyList<PartChamfer>? ChamferFaces = null)
{
    /// <summary>平面倒角（没读时为空）。</summary>
    public IReadOnlyList<PartChamfer> Chamfers => ChamferFaces ?? [];

    /// <summary>平面的外法向（没读时为空）。</summary>
    public IReadOnlyList<ModelDirection> Planes => PlaneNormals ?? [];

    /// <summary>窗口所在平面的外法向，一个窗口一项（没读时为空）。</summary>
    public IReadOnlyList<ModelDirection> Windows => WindowNormals ?? [];

    /// <summary>孔：内凹整圈的圆柱面按同轴并成一个（沉头孔的沉头与底孔、通孔上下两截）。</summary>
    public IReadOnlyList<PartHole> Holes => _holes ??= PartHole.Group(Cylinders);

    private IReadOnlyList<PartHole>? _holes;
}

/// <summary>一个孔：同轴的几张内凹整圈圆柱面。</summary>
/// <param name="Axis">轴向。</param>
/// <param name="Radius">最小的半径（底孔 / 螺纹底孔）。</param>
/// <param name="Kind">种：最小那张面的特征 + 孔径，与工程图里「每种孔标一次」同一口径的近似。</param>
/// <param name="Openings">孔口朝向：取最粗那一截的（沉头孔只朝沉头那面，通孔两面）。</param>
/// <param name="Point">轴线上的一点（米，当作向量存；1.14.1 判对面孔位置用）。</param>
/// <param name="Feature">最小那张面的特征名（1.14.1，与工程图里孔的「种」对照用）。</param>
/// <param name="AxialMin">孔沿 <paramref name="Axis"/> 的起止（各段并起来，米；1.14.1）；读不到为 NaN。</param>
/// <param name="AxialMax">同上，最大。</param>
/// <param name="Shape">
/// 孔的形状（1.14.1）：各段半径由细到粗排起来（0.001 mm）。镜像、阵列出来的孔特征名不同、形状相同——
/// 安装板一头是「M4 螺纹孔1」、另一头是镜像出来的，SolidWorks 的孔标注照样数成「6 x」。不比长度：
/// 那块板中间的 M4 一头打进了窗口被截短、另一头没有，比长度就成了两种（是不是同一个孔标注里的，由「N x」把关，见 <see cref="OppositePlanner"/>）。
/// </param>
internal sealed record PartHole(
    ModelDirection Axis, double Radius, string Kind, IReadOnlyList<ModelDirection> Openings, ModelDirection Point = default, string Feature = "",
    double AxialMin = double.NaN, double AxialMax = double.NaN, string Shape = "")
{
    /// <summary>沿 <paramref name="direction"/> 的起止（方向与轴相反时翻过来）；读不到为 null。</summary>
    public (double Min, double Max)? RangeAlong(ModelDirection direction)
    {
        if (double.IsNaN(AxialMin) || double.IsNaN(AxialMax))
            return null;
        return Axis.Dot(direction) >= 0 ? (AxialMin, AxialMax) : (-AxialMax, -AxialMin);
    }

    /// <summary>两条轴线算同一条：方向平行、相距不到 0.02 mm。</summary>
    public const double AxisTolerance = 2e-5;

    public static IReadOnlyList<PartHole> Group(IReadOnlyList<PartCylinder> cylinders)
    {
        var walls = cylinders.Where(cylinder => cylinder.Concave && cylinder.Full).ToList();
        var groups = new List<List<PartCylinder>>();
        foreach (var wall in walls)
        {
            var group = groups.FirstOrDefault(existing => SameAxis(existing[0], wall) && existing.Any(member => Touch(member, wall)));
            if (group is null)
                groups.Add([wall]);
            else
                group.Add(wall);
        }

        return groups
            .Select(group =>
            {
                var smallest = group.MinBy(cylinder => cylinder.Radius)!;
                // 孔口看最粗的那一截：沉头孔只算沉头那一面（底孔在沉头底面那头也有「孔口」，那是从沉头里看进去的），
                // 通孔只有一截、两头都算。
                var widest = group.MaxBy(cylinder => cylinder.Radius)!;
                var kind = smallest.Feature + "/" + Math.Round(smallest.Radius * 2000, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var shape = string.Join(";", group.Select(member => Math.Round(member.Radius * 1e6)).Distinct().Order());
                var ranges = group.Select(member => Range(member, smallest.Axis)).ToList();
                if (ranges.Any(range => range is null))
                    return new PartHole(smallest.Axis, smallest.Radius, kind, widest.Openings, smallest.Point, smallest.Feature, Shape: shape);
                return new PartHole(smallest.Axis, smallest.Radius, kind, widest.Openings, smallest.Point, smallest.Feature,
                    ranges.Min(range => range!.Value.Min), ranges.Max(range => range!.Value.Max), shape);
            })
            .ToList();
    }

    /// <summary>圆柱面沿 <paramref name="axis"/> 的起止（方向相反时翻过来）；读不到为 null。</summary>
    private static (double Min, double Max)? Range(PartCylinder cylinder, ModelDirection axis)
    {
        if (double.IsNaN(cylinder.AxialMin) || double.IsNaN(cylinder.AxialMax))
            return null;
        return cylinder.Axis.Dot(axis) >= 0 ? (cylinder.AxialMin, cylinder.AxialMax) : (-cylinder.AxialMax, -cylinder.AxialMin);
    }

    /// <summary>两段共轴圆柱面沿轴首尾相接或重叠（相差不到 <see cref="AxisTolerance"/>）；读不到起止的当相接（1.14.1）。</summary>
    private static bool Touch(PartCylinder a, PartCylinder b)
    {
        if (double.IsNaN(a.AxialMin) || double.IsNaN(a.AxialMax) || double.IsNaN(b.AxialMin) || double.IsNaN(b.AxialMax))
            return true;
        // 起止按各自的轴向量，方向相反时翻过来。
        var (min, max) = a.Axis.Dot(b.Axis) >= 0 ? (b.AxialMin, b.AxialMax) : (-b.AxialMax, -b.AxialMin);
        return min <= a.AxialMax + AxisTolerance && max >= a.AxialMin - AxisTolerance;
    }

    private static bool SameAxis(PartCylinder a, PartCylinder b)
    {
        if (Math.Abs(a.Axis.Dot(b.Axis)) < 0.9999)
            return false;
        // 两轴平行时，b 的轴上一点到 a 的轴线的距离 = |(Pb − Pa) × a|。
        var d = new ModelDirection(b.Point.X - a.Point.X, b.Point.Y - a.Point.Y, b.Point.Z - a.Point.Z);
        var cross = Cross(d, a.Axis);
        return Math.Sqrt(cross.Dot(cross)) <= AxisTolerance;
    }

    internal static ModelDirection Cross(ModelDirection a, ModelDirection b)
        => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}
