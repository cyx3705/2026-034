namespace HistoryStrenua;

/// <summary>
/// 钣金折弯处的线（1.17.0，用户定：折弯处的线不能标、不当基准，以外轮廓为基准）。WTJYQ-04-02 右防护板（t1.5、外 R2.5）真机读出两类：
/// <list type="bullet">
/// <item>折弯切线：正对着看的平板面与折弯圆柱面相切的那条边（侧板正视图离顶边 2.5 的那条）。</item>
/// <item>折弯区里的短边：端部折边的内表面（1.5）、侧板让位到 2.207、端板止于 447.793——都离外轮廓不到一个折弯外半径，
/// 量出来就是 2.21、447.79、52.79 这类两位小数。</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>切线的认法：直边旁边贴着一张圆柱面、边与圆柱轴线平行（直边落在圆柱面上只能是母线，即相切处），且这张面属于钣金体
/// （<c>IBody2.IsSheetMetal</c>）。孔壁也是圆柱面，但孔口是圆边、轴线垂直板面，不会对上；机加件的圆角切线不在这里管（不是钣金体）。
/// <b>但贴着的那张平面侧着（法向垂直视线）时不算</b>：这时切线和整块折边侧着的投影叠在一起，就是外轮廓本身
/// （俯视图最左那条；1.17.0 首版把它也去掉了，基准退到 2.207 的短边上，标出 60.79、225.79）。</para>
/// <para>折弯区：视图里见到的折弯圆柱面最大半径（外半径 = 内 R + 板厚）。离视图最外直边不到这么远、又不在最外上的竖直 / 水平边一律去掉（<see cref="InBendZone"/>）。</para>
/// <para>两类都在 <see cref="HoleScan.Scan"/> 收直边时去掉，所以基准、外轮廓、方形槽、孔位尺寸、未标尺寸都看不到它们。</para>
/// </remarks>
internal static class SheetMetal
{
    /// <summary>直边方向与圆柱轴线算平行的容差（夹角正弦，约 0.06°）。</summary>
    public const double ParallelTolerance = 1e-3;

    /// <summary>平面法向与视线算垂直（平面侧着）的容差（夹角余弦）。</summary>
    public const double EdgeOnTolerance = 1e-3;

    /// <summary>直边（方向 <paramref name="line"/>）是不是贴在这张圆柱面（轴线 <paramref name="axis"/>）上的切线：两者平行。</summary>
    public static bool AlongAxis(ModelDirection line, ModelDirection axis)
    {
        var (a, b) = (line.Normalized(), axis.Normalized());
        if (a.Dot(a) <= 0 || b.Dot(b) <= 0)
            return false;
        var (cx, cy, cz) = (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        return Math.Sqrt(cx * cx + cy * cy + cz * cz) <= ParallelTolerance;
    }

    /// <summary>平面（法向 <paramref name="planeNormal"/>）在这个视图（视线 <paramref name="viewNormal"/>）里侧着、投影成一条线。</summary>
    public static bool EdgeOn(ModelDirection planeNormal, ModelDirection viewNormal)
    {
        var (a, b) = (planeNormal.Normalized(), viewNormal.Normalized());
        return a.Dot(a) > 0 && b.Dot(b) > 0 && Math.Abs(a.Dot(b)) <= EdgeOnTolerance;
    }

    /// <summary>
    /// 折弯区里的直边（图纸坐标；<paramref name="zone"/> 是折弯外半径在图纸上的长度，<paramref name="scale"/> 是视图比例）：
    /// 竖直边离最左 / 最右竖直边、水平边离最上 / 最下水平边大于 0、不超过 <paramref name="zone"/>（各含 0.01 mm 的读数误差）。
    /// 最外那条自己不算；斜边不管。返回下标，从小到大。
    /// </summary>
    public static List<int> InBendZone(IReadOnlyList<SheetSegment> lines, double zone, double scale)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var result = new List<int>();
        if (zone <= 0 || lines.Count == 0)
            return result;
        var tolerance = 1e-5 * scale;
        var xs = lines.Where(Vertical).Select(s => s.X1).ToList();
        var ys = lines.Where(Horizontal).Select(s => s.Y1).ToList();
        bool Near(double value, List<double> extremes)
        {
            var gap = Math.Min(value - extremes.Min(), extremes.Max() - value);
            return gap > tolerance && gap <= zone + tolerance;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            if (Vertical(lines[i]) ? Near(lines[i].X1, xs) : Horizontal(lines[i]) && Near(lines[i].Y1, ys))
                result.Add(i);
        }

        return result;
    }

    private static bool Vertical(SheetSegment s) => Math.Abs(s.X1 - s.X2) <= 1e-7 && Math.Abs(s.Y1 - s.Y2) > 1e-7;

    private static bool Horizontal(SheetSegment s) => Math.Abs(s.Y1 - s.Y2) <= 1e-7 && Math.Abs(s.X1 - s.X2) > 1e-7;
}
