using System.Globalization;

namespace HistoryStrenua;

/// <summary>「未标尺寸」检查的一个视图：几何（直边、孔、圆弧）、已有尺寸、对称轴，以及朝向与模型（跨视图按模型面认）。</summary>
/// <param name="Name">视图名（回执用）。</param>
/// <param name="Model">引用的模型（<see cref="HoleScan.ModelKey"/>）；同一模型的视图共用一张尺寸网。</param>
/// <param name="Frame">视图朝向（图纸坐标 ↔ 模型面）。</param>
/// <param name="Lines">直边（模型边）。</param>
/// <param name="Holes">孔边原样（含同心的几圈、腰型孔端头），下标就是回指 SolidWorks 边的下标。</param>
/// <param name="Arcs">不是孔的圆弧与整圆（<see cref="FilletArc.Index"/> 回指圆弧边）。</param>
/// <param name="Dimensions">视图里已有的尺寸（含孔标注）。</param>
/// <param name="CenterLines">中心线注解（对称轴）的线条。</param>
internal sealed record CheckView(
    string Name, string Model, ViewFrame Frame,
    IReadOnlyList<SheetSegment> Lines, IReadOnlyList<HoleEdge> Holes, IReadOnlyList<FilletArc> Arcs,
    IReadOnlyList<ViewDimension> Dimensions, IReadOnlyList<SheetSegment> CenterLines);

/// <summary>漏标的东西是哪一类（选中它用哪张表的下标）。</summary>
internal enum CheckTarget
{
    Line,
    Hole,
    Arc,
}

/// <summary>查出来的一处漏标。</summary>
/// <param name="View">在第几个 <see cref="CheckView"/> 里。</param>
/// <param name="Text">给人看的一句（不含视图名）。</param>
/// <param name="Target">漏标的是直边、孔还是圆弧。</param>
/// <param name="Indexes">要在 SolidWorks 里选中（高亮）的边：<see cref="CheckView.Lines"/> / <see cref="CheckView.Holes"/> 的下标、圆弧是 <see cref="FilletArc.Index"/>。</param>
internal sealed record MissingDimension(int View, string Text, CheckTarget Target, IReadOnlyList<int> Indexes);

/// <summary>
/// 「未标尺寸」检查（1.16.0 重写，用户定：不依赖标注那一侧的规则，独立查有没有东西没标）的纯几何部分。
/// </summary>
/// <remarks>
/// <para>1.8.0–1.15.0 按本模块自己怎么标去比对（每种孔一个孔标注、按孔位规则的两向位置、外轮廓站……），标注规则错了检查也跟着错，
/// 而且只查孔与外轮廓。现在换成与标注规则无关的判法：<b>尺寸网</b>。</para>
/// <list type="bullet">
/// <item>每条竖直边、每个孔心、每个圆弧圆心在图纸上的一个坐标，就是模型里的一个面（<see cref="ViewFrame.Plane"/>）。
/// 每个线性 / 坐标尺寸把它两头那两个面连起来；同一模型的全部视图（轴测图除外）连成一张网，所以别的视图标过也算。</item>
/// <item>还有几样「不用尺寸也定了」的连接：圆弧一端与水平 / 竖直直边相切，圆心在那个方向上跟着那条边（差一个半径）；
/// 腰型孔两端互相定（长度在孔标注里）；阵列标法「N x 间距 =」跨度里的孔、边跟着它的起点；对称轴（中心线注解）两侧连通的一对面定了轴，轴上的东西跟着轴。</item>
/// <item>每个方向取连起来东西最多、且真有尺寸的那一块当主网。竖直边要它的 X 在主网里，水平边要 Y，孔心、圆弧圆心两个都要；不在的就是漏了位置尺寸。</item>
/// <item>大小：孔要有孔标注或 Ø 尺寸（同一模型里同直径的孔有一个标了就算，「N x」本来就合着标），R1 以上的圆弧要有 R 尺寸（同半径有一个标了就算）。
/// R1 及以下按技术要求「未注圆角 R1」不查。</item>
/// <item>斜边：直接连着尺寸（倒角 C、角度），或两端点的 X、Y 都在主网里；两条直角边都不超过 1 mm 的 45° 斜边按「未注倒角 C1」不查。</item>
/// </list>
/// <para>同一视图里坐标相同的几条边只报一次、一起选中。</para>
/// </remarks>
internal static class DimensionCheckPlanner
{
    /// <summary>R 到这个值（模型，米）以下按「未注圆角 R1」不查。</summary>
    public const double DefaultFillet = 0.00105;

    /// <summary>斜边两条直角边都不超过它（模型，米）且 45° 按「未注倒角 C1」不查。</summary>
    public const double DefaultChamfer = 0.00105;

    // swDimensionType_e
    private const int RadialDimension = 5;
    private const int DiameterDimension = 6;

    public static List<MissingDimension> Check(IReadOnlyList<CheckView> views)
    {
        ArgumentNullException.ThrowIfNull(views);
        var result = new List<MissingDimension>();
        foreach (var model in views.Select((view, index) => (view, index)).GroupBy(item => item.view.Model))
            result.AddRange(CheckModel(model.ToList()));
        return result.OrderBy(item => item.View).ToList();
    }

    private static List<MissingDimension> CheckModel(List<(CheckView View, int Index)> views)
    {
        var net = new OutlineCoverage();
        var dimensioned = new List<ModelPlane>();
        var sizes = new List<double>();

        // 1. 尺寸把两头的面连起来；大小尺寸记下半径。
        foreach (var (view, _) in views)
        {
            foreach (var dimension in view.Dimensions)
            {
                if (dimension.HoleCallout)
                {
                    foreach (var anchor in dimension.Anchors.Where(anchor => anchor is { Hole: true, X: not null, Y: not null }))
                    {
                        if (OuterRadius(view, new SheetPoint(anchor.X!.Value, anchor.Y!.Value)) is { } radius)
                            sizes.Add(radius / view.Frame.Scale);
                    }

                    continue;
                }

                if (dimension.Type is RadialDimension or DiameterDimension && !double.IsNaN(dimension.Value))
                {
                    sizes.Add(dimension.Type == DiameterDimension ? dimension.Value / 2 : dimension.Value);
                    continue;
                }

                if (!(dimension.Linear || dimension.Ordinate) || dimension.Anchors.Count < 2)
                    continue;
                foreach (var axis in Axes)
                {
                    if (!Measured(dimension, axis, view.Frame.Scale) || DimensionGeometry.Span(dimension, axis) is not { } span)
                        continue;
                    var (from, to) = (view.Frame.Plane(axis, span.From), view.Frame.Plane(axis, span.To));
                    net.Connect(from, to);
                    dimensioned.Add(from);
                    dimensioned.Add(to);
                    // 阵列标法只连首尾两头，跨度里等距的那些跟着它定。
                    if (dimension.Prefix.Contains(" x ", StringComparison.Ordinal))
                    {
                        foreach (var inside in Coordinates(view, axis).Where(c => c > span.From && c < span.To))
                            net.Connect(view.Frame.Plane(axis, inside), from);
                    }
                }
            }
        }

        // 2. 不用尺寸也定了的：相切、腰型孔两端、对称轴。
        foreach (var (view, _) in views)
        {
            foreach (var arc in view.Arcs)
                ConnectTangents(net, view, arc);
            foreach (var slot in HoleCalloutPlanner.Recognize(view.Holes).Where(hole => hole.Slot >= 0).GroupBy(hole => hole.Slot))
            {
                var ends = slot.ToList();
                for (var i = 1; i < ends.Count; i++)
                {
                    net.Connect(view.Frame.Plane(PositionAxis.Horizontal, ends[0].X), view.Frame.Plane(PositionAxis.Horizontal, ends[i].X));
                    net.Connect(view.Frame.Plane(PositionAxis.Vertical, ends[0].Y), view.Frame.Plane(PositionAxis.Vertical, ends[i].Y));
                }
            }
        }

        foreach (var (view, _) in views)
            ConnectSymmetry(net, view);

        // 3. 每个方向的主网：连起来的要求最多、且真有尺寸的那一块。
        var requirements = new List<ModelPlane>();
        foreach (var (view, _) in views)
        {
            foreach (var axis in Axes)
                requirements.AddRange(Coordinates(view, axis).Select(c => view.Frame.Plane(axis, c)));
        }

        var dimensionedRoots = dimensioned.Select(net.Component).ToHashSet();
        var mains = new List<(ModelDirection Direction, int Root)>();
        foreach (var direction in requirements.GroupBy(plane => DirectionIndex(requirements, plane)))
        {
            var best = direction
                .GroupBy(net.Component)
                .Where(group => dimensionedRoots.Contains(group.Key))
                .OrderByDescending(group => group.Count())
                .FirstOrDefault();
            if (best is not null)
                mains.Add((direction.First().Direction, best.Key));
        }

        bool InMain(CheckView view, PositionAxis axis, double coordinate)
        {
            var plane = view.Frame.Plane(axis, coordinate);
            var root = net.Component(plane);
            return mains.Any(main => Math.Abs(main.Direction.Dot(plane.Direction)) >= 0.999 && main.Root == root);
        }

        bool Sized(double modelRadius) => sizes.Any(size => Math.Abs(size - modelRadius) <= DimensionGeometry.ValueTolerance);

        // 4. 逐个视图查。
        var result = new List<MissingDimension>();
        foreach (var (view, index) in views)
        {
            var scale = view.Frame.Scale;
            var left = view.Lines.Count > 0 ? view.Lines.Min(line => Math.Min(line.X1, line.X2)) : 0;
            var top = view.Lines.Count > 0 ? view.Lines.Max(line => Math.Max(line.Y1, line.Y2)) : 0;
            string FromLeft(double x) => Mm((x - left) / scale);
            string FromTop(double y) => Mm((top - y) / scale);
            string Where(SheetPoint point) => $"左起 {FromLeft(point.X)}、上起 {FromTop(point.Y)}";
            List<string> MissingAxes(SheetPoint point)
                => Axes.Where(axis => !InMain(view, axis, axis == PositionAxis.Horizontal ? point.X : point.Y))
                    .Select(axis => axis == PositionAxis.Horizontal ? "水平" : "竖直")
                    .ToList();

            // 直边：竖直边要 X、水平边要 Y；坐标相同的几条一起报。
            foreach (var axis in Axes)
            {
                var straight = view.Lines.Select((line, i) => (line, i))
                    .Where(item => Orientation(item.line) == (axis == PositionAxis.Horizontal ? LineOrientation.Vertical : LineOrientation.Horizontal))
                    .Select(item => (Coordinate: axis == PositionAxis.Horizontal ? item.line.X1 : item.line.Y1, item.i))
                    .ToList();
                foreach (var group in Cluster(straight, item => item.Coordinate))
                {
                    if (InMain(view, axis, group[0].Coordinate))
                        continue;
                    var text = axis == PositionAxis.Horizontal
                        ? $"竖直边（左起 {FromLeft(group[0].Coordinate)}）缺水平位置尺寸"
                        : $"水平边（上起 {FromTop(group[0].Coordinate)}）缺竖直位置尺寸";
                    result.Add(new MissingDimension(index, text + (group.Count > 1 ? $"（{group.Count} 段）" : string.Empty), CheckTarget.Line, group.Select(item => item.i).ToList()));
                }
            }

            // 斜边：连着尺寸，或两端都定了。
            for (var i = 0; i < view.Lines.Count; i++)
            {
                var line = view.Lines[i];
                if (Orientation(line) != LineOrientation.Slanted)
                    continue;
                var (dx, dy) = (Math.Abs(line.X2 - line.X1) / scale, Math.Abs(line.Y2 - line.Y1) / scale);
                if (dx <= DefaultChamfer && dy <= DefaultChamfer && Math.Abs(dx - dy) <= 0.1 * Math.Max(dx, dy))
                    continue;
                if (view.Dimensions.Any(dimension => dimension.Anchors.Any(anchor => anchor.Segment is { } segment && DimensionGeometry.SameSegment(segment, line))))
                    continue;
                if (InMain(view, PositionAxis.Horizontal, line.X1) && InMain(view, PositionAxis.Horizontal, line.X2)
                    && InMain(view, PositionAxis.Vertical, line.Y1) && InMain(view, PositionAxis.Vertical, line.Y2))
                    continue;
                result.Add(new MissingDimension(index,
                    $"斜边（左起 {FromLeft(Math.Min(line.X1, line.X2))}–{FromLeft(Math.Max(line.X1, line.X2))}、上起 {FromTop(Math.Max(line.Y1, line.Y2))}–{FromTop(Math.Min(line.Y1, line.Y2))}）没有尺寸定（倒角 C、角度或两端位置）",
                    CheckTarget.Line, [i]));
            }

            // 孔：两向位置与孔径；腰型孔两端互相定，只报一端。
            foreach (var hole in HoleCalloutPlanner.Representatives(HoleCalloutPlanner.Recognize(view.Holes)))
            {
                var center = new SheetPoint(hole.X, hole.Y);
                var radius = OuterRadius(view, center) ?? hole.Radius;
                var problems = new List<string>();
                if (MissingAxes(center) is { Count: > 0 } axes)
                    problems.Add($"缺{string.Join("、", axes)}位置尺寸");
                if (!Sized(radius / scale) && !Sized(hole.Radius / scale))
                    problems.Add("没有孔标注或 Ø 尺寸");
                if (problems.Count == 0)
                    continue;
                var edges = view.Holes.Select((edge, i) => (edge, i))
                    .Where(item => HoleCalloutPlanner.SameCenter(new SheetPoint(item.edge.X, item.edge.Y), center)
                                   || (hole.Slot >= 0 && item.edge.Slot == hole.Slot))
                    .Select(item => item.i)
                    .DefaultIfEmpty(hole.Index)
                    .ToList();
                result.Add(new MissingDimension(index, $"{(hole.Slot >= 0 ? "腰型孔" : $"⌀{Mm(2 * radius / scale)} 孔")}（{Where(center)}）{string.Join("；", problems)}", CheckTarget.Hole, edges));
            }

            // 圆弧（R1 以上）：半径（整圆是直径）与圆心两向位置；相切的方向已由直边连上。
            foreach (var arc in view.Arcs.Where(arc => arc.ModelRadius > DefaultFillet))
            {
                var full = arc.Sweep >= 2 * Math.PI - 1e-6;
                var problems = new List<string>();
                if (!Sized(arc.ModelRadius))
                    problems.Add(full ? "没有 Ø 尺寸" : "没有 R 尺寸");
                if (MissingAxes(arc.Center) is { Count: > 0 } axes)
                    problems.Add($"圆心缺{string.Join("、", axes)}位置尺寸");
                if (problems.Count > 0)
                    result.Add(new MissingDimension(index, $"{(full ? "Ø" + Mm(2 * arc.ModelRadius) + " 圆" : "R" + Mm(arc.ModelRadius) + " 圆弧")}（圆心{Where(arc.Center)}）{string.Join("；", problems)}", CheckTarget.Arc, [arc.Index]));
            }
        }

        return result;
    }

    private static readonly PositionAxis[] Axes = [PositionAxis.Horizontal, PositionAxis.Vertical];

    private enum LineOrientation
    {
        Horizontal,
        Vertical,
        Slanted,
    }

    private static LineOrientation Orientation(SheetSegment line)
    {
        var (dx, dy) = (line.X2 - line.X1, line.Y2 - line.Y1);
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0 || Math.Abs(dx) / length < DimensionGeometry.AxisTolerance)
            return LineOrientation.Vertical;
        return Math.Abs(dy) / length < DimensionGeometry.AxisTolerance ? LineOrientation.Horizontal : LineOrientation.Slanted;
    }

    /// <summary>
    /// 尺寸是不是沿 <paramref name="axis"/> 量的：值对得上（<see cref="DimensionGeometry.Measures"/>），或两头只在这个方向给得出坐标
    /// （值读不出、或量的是斜边投影时，按附着的东西判）。
    /// </summary>
    private static bool Measured(ViewDimension dimension, PositionAxis axis, double scale)
    {
        if (DimensionGeometry.Measures(dimension, axis, scale))
            return true;
        var other = axis == PositionAxis.Horizontal ? PositionAxis.Vertical : PositionAxis.Horizontal;
        return DimensionGeometry.Span(dimension, axis) is not null && DimensionGeometry.Span(dimension, other) is null;
    }

    /// <summary>视图里沿 <paramref name="axis"/> 要定位的坐标：竖直边的 X（水平边的 Y）、孔心、圆弧圆心。</summary>
    private static IEnumerable<double> Coordinates(CheckView view, PositionAxis axis)
    {
        var horizontal = axis == PositionAxis.Horizontal;
        var wanted = horizontal ? LineOrientation.Vertical : LineOrientation.Horizontal;
        return view.Lines.Where(line => Orientation(line) == wanted).Select(line => horizontal ? line.X1 : line.Y1)
            .Concat(view.Holes.Select(hole => horizontal ? hole.X : hole.Y))
            .Concat(view.Arcs.Where(arc => arc.ModelRadius > DefaultFillet).Select(arc => horizontal ? arc.Center.X : arc.Center.Y));
    }

    /// <summary>圆弧一端与水平 / 竖直直边相切：圆心在那条边的法向上跟着它（差一个半径）。</summary>
    private static void ConnectTangents(OutlineCoverage net, CheckView view, FilletArc arc)
    {
        foreach (var end in new[] { arc.Start, arc.End })
        {
            if (end is not { } point)
                continue;
            var (rx, ry) = (point.X - arc.Center.X, point.Y - arc.Center.Y);
            var radius = Math.Sqrt(rx * rx + ry * ry);
            if (radius <= 0)
                continue;
            foreach (var line in view.Lines)
            {
                var touches = HoleCalloutPlanner.SameCenter(new SheetPoint(line.X1, line.Y1), point)
                              || HoleCalloutPlanner.SameCenter(new SheetPoint(line.X2, line.Y2), point);
                if (!touches)
                    continue;
                var (dx, dy) = (line.X2 - line.X1, line.Y2 - line.Y1);
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length <= 0 || Math.Abs((dx * rx + dy * ry) / (length * radius)) > 0.02)
                    continue;
                switch (Orientation(line))
                {
                    case LineOrientation.Horizontal:
                        net.Connect(view.Frame.Plane(PositionAxis.Vertical, arc.Center.Y), view.Frame.Plane(PositionAxis.Vertical, line.Y1));
                        break;
                    case LineOrientation.Vertical:
                        net.Connect(view.Frame.Plane(PositionAxis.Horizontal, arc.Center.X), view.Frame.Plane(PositionAxis.Horizontal, line.X1));
                        break;
                }
            }
        }
    }

    /// <summary>
    /// 对称轴：轴两侧一对已连通的面（中点落在轴上）把轴定下来，轴上的东西跟着它。每一对都连上——外轮廓一对把轴连进主网，
    /// 两侧互标的孔那一对再经轴连进来（只连第一对时，两侧孔自成一块）。
    /// </summary>
    private static void ConnectSymmetry(OutlineCoverage net, CheckView view)
    {
        foreach (var axisLine in view.CenterLines)
        {
            var orientation = Orientation(axisLine);
            if (orientation == LineOrientation.Slanted)
                continue;
            var axis = orientation == LineOrientation.Vertical ? PositionAxis.Horizontal : PositionAxis.Vertical;
            var c = axis == PositionAxis.Horizontal ? axisLine.X1 : axisLine.Y1;
            var coordinates = Coordinates(view, axis).Distinct().ToList();
            var center = view.Frame.Plane(axis, c);
            foreach (var p in coordinates)
            {
                var q = 2 * c - p;
                if (p < c && coordinates.Any(other => DimensionGeometry.Same(other, q))
                    && net.Connected(view.Frame.Plane(axis, p), view.Frame.Plane(axis, q)))
                {
                    net.Connect(center, view.Frame.Plane(axis, p));
                }
            }
        }
    }

    /// <summary>这个孔心上最大的那圈孔边半径（图纸）；没有为 null。</summary>
    private static double? OuterRadius(CheckView view, SheetPoint center)
    {
        var radii = view.Holes.Where(hole => HoleCalloutPlanner.SameCenter(new SheetPoint(hole.X, hole.Y), center)).Select(hole => hole.Radius).ToList();
        return radii.Count > 0 ? radii.Max() : null;
    }

    /// <summary>方向相同（平行）的面归同一组：取第一个与它平行的面的下标当组号。</summary>
    private static int DirectionIndex(IReadOnlyList<ModelPlane> planes, ModelPlane plane)
    {
        for (var i = 0; i < planes.Count; i++)
        {
            if (Math.Abs(planes[i].Direction.Dot(plane.Direction)) >= 0.999)
                return i;
        }

        return -1;
    }

    /// <summary>按坐标归堆（同一条线上的几段边）。</summary>
    private static List<List<T>> Cluster<T>(IEnumerable<T> items, Func<T, double> coordinate)
    {
        var groups = new List<List<T>>();
        foreach (var item in items.OrderBy(coordinate))
        {
            if (groups.Count > 0 && DimensionGeometry.Same(coordinate(groups[^1][0]), coordinate(item)))
                groups[^1].Add(item);
            else
                groups.Add([item]);
        }

        return groups;
    }

    /// <summary>模型长度（米）写成毫米，最多两位小数。</summary>
    public static string Mm(double meters) => Math.Round(meters * 1000, 2).ToString("0.##", CultureInfo.InvariantCulture);
}
