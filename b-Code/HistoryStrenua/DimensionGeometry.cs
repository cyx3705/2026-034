namespace HistoryStrenua;

/// <summary>
/// 尺寸一头连着的东西在图纸上能给出的坐标：圆心两个都给；竖直线只给 X、水平线只给 Y；斜线什么都不给（两个都是 null）。
/// </summary>
/// <param name="X">这一头的 X（图纸坐标，米），给不出为 null。</param>
/// <param name="Y">这一头的 Y，给不出为 null。</param>
/// <param name="Hole">这一头是圆边（孔边）。</param>
/// <param name="ModelLine">这一头是模型直边（不是视图草图线、不是圆）——外轮廓尺寸两头都是。</param>
/// <param name="Segment">直线那一头的线段本身（圆心为 null），外轮廓按它认边。</param>
internal readonly record struct DimensionAnchor(double? X, double? Y, bool Hole = false, bool ModelLine = false, SheetSegment? Segment = null)
{
    /// <summary>圆心。</summary>
    public static DimensionAnchor Center(SheetPoint center) => new(center.X, center.Y, Hole: true);

    /// <summary>直线：竖直的给 X、水平的给 Y，斜的两个都不给。</summary>
    public static DimensionAnchor Line(SheetSegment line, bool modelLine)
    {
        var dx = line.X2 - line.X1;
        var dy = line.Y2 - line.Y1;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0)
            return new DimensionAnchor(line.X1, line.Y1, ModelLine: modelLine, Segment: line);
        if (Math.Abs(dx) / length < DimensionGeometry.AxisTolerance)
            return new DimensionAnchor((line.X1 + line.X2) / 2, null, ModelLine: modelLine, Segment: line);
        if (Math.Abs(dy) / length < DimensionGeometry.AxisTolerance)
            return new DimensionAnchor(null, (line.Y1 + line.Y2) / 2, ModelLine: modelLine, Segment: line);
        return new DimensionAnchor(null, null, ModelLine: modelLine, Segment: line);
    }

    /// <summary>沿 <paramref name="axis"/> 的坐标：水平尺寸量 X，竖直尺寸量 Y。</summary>
    public double? Along(PositionAxis axis) => axis == PositionAxis.Horizontal ? X : Y;
}

/// <summary>视图里已有的一个尺寸，读成几何（1.8.0，销孔标注、外轮廓、检查共用）。</summary>
/// <param name="Index">回指调用方手里那个尺寸。</param>
/// <param name="Type"><c>IDisplayDimension.Type2</c>。</param>
/// <param name="HoleCallout">是孔标注。</param>
/// <param name="Value">尺寸值（模型长度，米）；读不出为 NaN。</param>
/// <param name="Anchors">每个附着对象一头；读不出的对象不出。</param>
/// <param name="Prefix">尺寸文字前缀（阵列标法是「(N-1) x 间距 =」）。</param>
/// <param name="Suffix">尺寸文字后缀。</param>
/// <param name="ToleranceType"><c>IDimensionTolerance.Type</c>（<c>swTolType_e</c>）；读不出为 0。</param>
internal sealed record ViewDimension(
    int Index, int Type, bool HoleCallout, double Value, IReadOnlyList<DimensionAnchor> Anchors,
    string Prefix = "", string Suffix = "", int ToleranceType = 0)
{
    /// <summary>线性尺寸（水平、竖直、斜的）。</summary>
    public bool Linear => !HoleCallout && HolePositionPlanner.IsLinear(Type);

    /// <summary>坐标尺寸（SolidWorks 中文界面的「尺寸链」）。</summary>
    public bool Ordinate => !HoleCallout && HolePositionPlanner.IsOrdinate(Type);
}

/// <summary>
/// 已有尺寸的纯几何判断：量的是哪个方向、两头落在哪条中心线 / 哪条边上。不碰 SolidWorks。
/// </summary>
internal static class DimensionGeometry
{
    /// <summary>直线算竖直 / 水平的斜率容差（同 <see cref="HolePositionPlanner.Datums"/>）。</summary>
    public const double AxisTolerance = 1e-3;

    /// <summary>尺寸值与两头坐标差对得上的容差（模型长度 0.01 mm）。</summary>
    public const double ValueTolerance = 1e-5;

    // swTolType_e
    public const int ToleranceSymmetric = 4;
    public const int ToleranceFitWithTolerance = 8;

    /// <summary>
    /// 一个线性 / 坐标尺寸量的是哪个方向：两头沿这个方向都给得出坐标，且坐标差 ÷ 比例 = 尺寸值。
    /// 两个方向都对得上（两孔斜 45°）两个都回 true。坐标尺寸的两头是 [自己, 0 点]，同样判。
    /// </summary>
    public static (bool Horizontal, bool Vertical) Axes(ViewDimension dimension, double scale)
    {
        if (dimension.Anchors.Count < 2 || double.IsNaN(dimension.Value) || scale <= 0)
            return (false, false);
        return (Matches(PositionAxis.Horizontal), Matches(PositionAxis.Vertical));

        bool Matches(PositionAxis axis)
            => Span(dimension, axis) is { } span
               && Math.Abs((span.To - span.From) / scale - Math.Abs(dimension.Value)) <= ValueTolerance;
    }

    /// <summary>尺寸沿 <paramref name="axis"/> 两头的坐标（小的在前）；有一头给不出为 null。只看前两头。</summary>
    public static (double From, double To)? Span(ViewDimension dimension, PositionAxis axis)
    {
        if (dimension.Anchors.Count < 2
            || dimension.Anchors[0].Along(axis) is not { } a
            || dimension.Anchors[1].Along(axis) is not { } b)
            return null;
        return (Math.Min(a, b), Math.Max(a, b));
    }

    /// <summary>尺寸是不是沿 <paramref name="axis"/> 量的。</summary>
    public static bool Measures(ViewDimension dimension, PositionAxis axis, double scale)
    {
        var (horizontal, vertical) = Axes(dimension, scale);
        return axis == PositionAxis.Horizontal ? horizontal : vertical;
    }

    /// <summary>两个图纸坐标算同一条中心线 / 同一条边（<see cref="HoleCalloutPlanner.CenterTolerance"/>）。</summary>
    public static bool Same(double a, double b) => Math.Abs(a - b) <= HoleCalloutPlanner.CenterTolerance;

    /// <summary>两条线段是同一条边（两端对上，方向不论）。</summary>
    public static bool SameSegment(SheetSegment a, SheetSegment b)
        => (Same(a.X1, b.X1) && Same(a.Y1, b.Y1) && Same(a.X2, b.X2) && Same(a.Y2, b.Y2))
           || (Same(a.X1, b.X2) && Same(a.Y1, b.Y2) && Same(a.X2, b.X1) && Same(a.Y2, b.Y1));
}
