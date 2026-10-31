using System.Globalization;

namespace HistoryStrenua;

/// <summary>
/// 一条曲线边在图纸上的样子（1.32.0）：<paramref name="Segments"/> 是折线近似，圆弧从起点到终点；<paramref name="Center"/> 只有正对图纸的圆弧才有。
/// </summary>
internal sealed record SheetCurve(IReadOnlyList<SheetSegment> Segments, SheetPoint? Center, double Radius)
{
    /// <summary>有两个端点：圆弧两段、别的曲线一段；整圆是八边形，没有端点。</summary>
    public bool HasEnds => Segments.Count is 1 or 2;

    /// <summary>第 1 个或第 2 个端点。</summary>
    public SheetPoint? EndPoint(int end) => !HasEnds ? null
        : end == 1 ? new SheetPoint(Segments[0].X1, Segments[0].Y1)
        : new SheetPoint(Segments[^1].X2, Segments[^1].Y2);
}

/// <summary>
/// 图纸 SVG 里的几何编号（1.25.0）：直边 <c>E3</c>、孔 <c>H5</c>。加尺寸、加孔标注、加中心符号线按它寻址。
/// </summary>
internal readonly record struct GeometryRef(bool Hole, int Index, int End = 0, bool Curve = false)
{
    public override string ToString() => (Hole ? "H" : Curve ? "C" : "E") + Index.ToString(CultureInfo.InvariantCulture) + (End > 0 ? "." + End.ToString(CultureInfo.InvariantCulture) : string.Empty);

    /// <summary>解析 E3 / H5 / 直边端点 E3.1、E3.2（1.31.0；SVG 里 x1,y1 是 .1，x2,y2 是 .2）/ 曲线 C3（圆弧量圆心）、曲线端点 C3.1、C3.2（1.32.0；折线首尾）；不分大小写；写不对抛 <see cref="QuickArgumentException"/>。</summary>
    public static GeometryRef Parse(string text, string parameter)
    {
        var trimmed = text.Trim();
        var dot = trimmed.IndexOf('.');
        if (dot > 0 && char.ToUpperInvariant(trimmed[0]) is 'E' or 'C' && trimmed[(dot + 1)..] is "1" or "2"
            && int.TryParse(trimmed[1..dot], NumberStyles.None, CultureInfo.InvariantCulture, out var line))
            return new GeometryRef(false, line, trimmed[^1] - '0', char.ToUpperInvariant(trimmed[0]) == 'C');
        if (trimmed.Length >= 2
            && char.ToUpperInvariant(trimmed[0]) is 'E' or 'H' or 'C'
            && int.TryParse(trimmed[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            return new GeometryRef(char.ToUpperInvariant(trimmed[0]) == 'H', index, Curve: char.ToUpperInvariant(trimmed[0]) == 'C');
        throw new QuickArgumentException($"{parameter}={text} 写不对：要图纸 SVG 里的编号，直边如 E3、直边端点如 E3.1、孔如 H5、圆弧圆心如 C3、曲线端点如 C3.1。");
    }

    /// <summary>
    /// 一头在量的方向上的坐标（图纸米）。孔取圆心；直边必须和量的方向垂直，否则量不出距离，回 null。
    /// </summary>
    public static double? Coordinate(SheetSegment line, bool horizontal)
    {
        var (a, b) = horizontal ? (line.X1, line.X2) : (line.Y1, line.Y2);
        return Math.Abs(a - b) < ParallelTolerance ? (a + b) / 2 : null;
    }

    /// <summary>直边两头差不到 0.05 mm（图纸上）算垂直。</summary>
    private const double ParallelTolerance = 0.00005;
}
