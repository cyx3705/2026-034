using System.Globalization;

namespace HistoryStrenua;

/// <summary>
/// 图纸 SVG 里的几何编号（1.25.0）：直边 <c>E3</c>、孔 <c>H5</c>。加尺寸、加孔标注、加中心符号线按它寻址。
/// </summary>
internal readonly record struct GeometryRef(bool Hole, int Index)
{
    public override string ToString() => (Hole ? "H" : "E") + Index.ToString(CultureInfo.InvariantCulture);

    /// <summary>解析 E3 / H5（不分大小写）；写不对抛 <see cref="QuickArgumentException"/>。</summary>
    public static GeometryRef Parse(string text, string parameter)
    {
        var trimmed = text.Trim();
        if (trimmed.Length >= 2
            && char.ToUpperInvariant(trimmed[0]) is 'E' or 'H'
            && int.TryParse(trimmed[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            return new GeometryRef(char.ToUpperInvariant(trimmed[0]) == 'H', index);
        throw new QuickArgumentException($"{parameter}={text} 写不对：要图纸 SVG 里的编号，直边如 E3、孔如 H5。");
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
