namespace HistoryStrenua;

/// <summary>
/// 孔标注引线的纯几何判定（1.24.0）：从显示数据的线里认出引线、量长度、数它和别的标注的线交叉几次。
/// 「整理标注」接受挪动、「注解重叠」报问题、图纸 SVG 写 <c>data-leader</c> 共用。单位图纸米。
/// </summary>
/// <remarks>
/// <para>1.23 真机（移动安装版2）：整理标注把两个孔标注推到视图下方 45 mm 外，引线互相交叉、横穿尺寸线，
/// 回执却是「剩余 0 个文字冲突」——原来只看文字框压没压东西，不看引线。</para>
/// <para>孔标注显示数据里的线：水平的下划线 + 斜着（或竖着）的引线 + 箭头的短笔画。不水平、又不是短笔画的就是引线。</para>
/// </remarks>
internal static class LeaderPlanner
{
    /// <summary>引线超过这么长（图纸 40 mm）就报「引线过长」；避障自己摆孔标注最远一档是半径 + 24 mm。</summary>
    public const double LongLeader = 0.040;

    /// <summary>短于这个的是箭头笔画，不算引线。</summary>
    private const double Stroke = 0.0015;

    /// <summary>两头高差小于这个算水平（下划线）。</summary>
    private const double Flat = 0.0002;

    /// <summary>交点离线段端点这么近不算交叉（引线箭头本来就落在孔边上，两条线首尾相接也不算）。</summary>
    private const double Touch = 0.0003;

    /// <summary>显示数据里的引线段。</summary>
    public static List<SheetSegment> Leaders(IEnumerable<SheetSegment> lines)
        => lines.Where(line => Math.Abs(line.Y1 - line.Y2) > Flat && Length(line) >= Stroke).ToList();

    public static double Length(SheetSegment line) => Math.Sqrt(Math.Pow(line.X2 - line.X1, 2) + Math.Pow(line.Y2 - line.Y1, 2));

    /// <summary>引线总长。</summary>
    public static double Length(IEnumerable<SheetSegment> leaders) => leaders.Sum(Length);

    /// <summary>
    /// 文字平移 (<paramref name="dx"/>, <paramref name="dy"/>) 后引线大概的样子：靠文字那头跟着走，箭头那头（离文字远的）钉在孔边不动。
    /// 只拿来筛候选位置——SolidWorks 挪孔标注会换引线端，真结果以读回为准。
    /// </summary>
    public static List<SheetSegment> Predict(IReadOnlyList<SheetSegment> leaders, IReadOnlyList<TextBox> texts, double dx, double dy)
    {
        if (texts.Count == 0)
            return [.. leaders];
        var corners = texts.SelectMany(text => text.Corners()).ToList();
        var (cx, cy) = (corners.Average(p => p.X), corners.Average(p => p.Y));
        double Distance(double x, double y) => (x - cx) * (x - cx) + (y - cy) * (y - cy);
        return leaders.Select(line => Distance(line.X1, line.Y1) <= Distance(line.X2, line.Y2)
                ? line with { X1 = line.X1 + dx, Y1 = line.Y1 + dy }
                : line with { X2 = line.X2 + dx, Y2 = line.Y2 + dy })
            .ToList();
    }

    /// <summary>引线的毛病数：与 <paramref name="others"/> 交叉几处，超过 <see cref="LongLeader"/> 再加一。</summary>
    public static int Problems(IReadOnlyList<SheetSegment> leaders, IReadOnlyList<SheetSegment> others)
        => Crossings(leaders, others) + (Length(leaders) > LongLeader ? 1 : 0);

    /// <summary><paramref name="leaders"/> 与 <paramref name="others"/> 交叉了几处。</summary>
    public static int Crossings(IReadOnlyList<SheetSegment> leaders, IEnumerable<SheetSegment> others)
        => others.Sum(other => leaders.Count(leader => Cross(leader, other)));

    /// <summary>两条线段在两者内部相交（端点附近的接触不算，平行重叠不算）。</summary>
    public static bool Cross(SheetSegment a, SheetSegment b)
    {
        var (dx1, dy1) = (a.X2 - a.X1, a.Y2 - a.Y1);
        var (dx2, dy2) = (b.X2 - b.X1, b.Y2 - b.Y1);
        var denominator = dx1 * dy2 - dy1 * dx2;
        if (Math.Abs(denominator) < 1e-14)
            return false;
        var t = ((b.X1 - a.X1) * dy2 - (b.Y1 - a.Y1) * dx2) / denominator;
        var u = ((b.X1 - a.X1) * dy1 - (b.Y1 - a.Y1) * dx1) / denominator;
        if (t < 0 || t > 1 || u < 0 || u > 1)
            return false;
        var x = a.X1 + t * dx1;
        var y = a.Y1 + t * dy1;
        return !Near(a.X1, a.Y1) && !Near(a.X2, a.Y2) && !Near(b.X1, b.Y1) && !Near(b.X2, b.Y2);

        bool Near(double px, double py) => Math.Abs(px - x) <= Touch && Math.Abs(py - y) <= Touch;
    }
}
