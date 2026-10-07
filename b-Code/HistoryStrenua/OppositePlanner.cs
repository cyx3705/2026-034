using System.Text.RegularExpressions;

namespace HistoryStrenua;

/// <summary>一种孔在视图对面（背面）的情况。</summary>
internal enum OppositeResult
{
    /// <summary>对面没有这种孔。</summary>
    None,

    /// <summary>对面的这种孔与这一面一一对上（同种、同位置）：孔标注写「(含对面)」，不用另开视图。</summary>
    Same,

    /// <summary>对面也有这种孔但位置（或个数）对不上：这一面的视图看不全，要从对面再开一个视图。</summary>
    Different,
}

/// <summary>
/// 「对面孔」（1.14.1，用户定）：视图只看得到朝自己开口的孔，SolidWorks 的孔标注「N×」却把整个特征的孔都数进去——
/// 侧面视图标「6 x M4」只看得见 3 个。对面的同种孔与这一面位置一致，就在孔标注后面写「(含对面)」；
/// 不一致就要从对面再投影一个视图（「投影视图」据此两边都加，见 <see cref="DrawingPlanner.SideViews"/>）。
/// </summary>
/// <remarks>
/// <para>真机（XJ05A-01 安装板）定下的判法：</para>
/// <list type="bullet">
/// <item>孔在哪一面：沿视线看孔的起止碰不碰零件两端的外表面（<see cref="SideOf"/>）。不用孔口平面判——
/// 中间那个 M4 底孔打穿进了窗口，窗口壁也是「孔口」，会被当成通孔。读不到起止（旧夹具）才退回孔口。</item>
/// <item>同种：同形状（<see cref="PartHole.Shape"/>，各段孔径），不看特征名——一头是「M4 螺纹孔1」，另一头是镜像出来的；
/// 也不看深度——中间那个一头被窗口截短了。是不是同一个孔标注数进去的，看「N x」是不是这一面孔数的两倍。</item>
/// <item>两头各打一半连成通孔的（M3：两头各深 7.5、板厚 10），模型里只剩一个通孔，对面没有单独的孔可比；
/// 这时看孔标注上的「N x」：正好是这一面孔数的两倍，就是两头都有、位置自然一致。</item>
/// <item>一面只对上一部分、个数对不上：按不一致（孔标注的「N×」说不清是哪几个）。</item>
/// </list>
/// </remarks>
internal static class OppositePlanner
{
    /// <summary>孔标注后缀（SolidWorks 孔标注的后缀写在整段文字最后，真机核对过）。</summary>
    public const string Suffix = " (含对面)";

    /// <summary>认后缀时去掉空格比较的写法。</summary>
    public const string Marker = "(含对面)";

    /// <summary>两孔投影后算同一位置的距离（模型 0.02 mm）；孔端算碰到外表面的距离同此。</summary>
    public const double PositionTolerance = 2e-5;

    /// <summary>孔轴、孔口方向与视线算平行的余弦。</summary>
    private const double AxisCosine = 0.999;

    /// <summary>孔沿某个方向开在哪一面。</summary>
    internal enum Side
    {
        /// <summary>轴不沿这个方向，或两头都不碰外表面（台阶上、型腔里的孔）。</summary>
        Neither,

        /// <summary>只开在 + 那一面。</summary>
        Plus,

        /// <summary>只开在 − 那一面。</summary>
        Minus,

        /// <summary>两面都开（通孔，或两头各打一半连通的）。</summary>
        Through,
    }

    /// <summary>
    /// 视线 <paramref name="normal"/>（图纸 +Z 在模型里的方向，朝看图人）上，视图里的一种孔（<see cref="HoleEdge.Kind"/> 为
    /// <paramref name="kind"/>、模型半径 <paramref name="radius"/> 米）的对面情况，以及对面有几个。
    /// </summary>
    /// <param name="part">零件。</param>
    /// <param name="normal">视线。</param>
    /// <param name="kind">视图里的种。</param>
    /// <param name="radius">模型半径（米）。</param>
    /// <param name="calloutCount">这种的孔标注上的「N x」；读不出为 null。</param>
    public static (OppositeResult Result, int Back) Judge(PartGeometry part, ModelDirection normal, string kind, double radius, int? calloutCount)
    {
        ArgumentNullException.ThrowIfNull(part);
        var sides = part.Holes.Select(hole => (Hole: hole, Side: SideOf(hole, part.Box, normal))).ToList();
        var front = sides
            .Where(item => item.Side is Side.Plus or Side.Through && KindIsFeature(kind, item.Hole.Feature) && SameRadius(item.Hole.Radius, radius))
            .ToList();
        if (front.Count == 0)
            return (OppositeResult.None, 0);

        var shapes = front.Select(item => item.Hole.Shape).ToHashSet(StringComparer.Ordinal);
        var unused = sides.Where(item => item.Side == Side.Minus && shapes.Contains(item.Hole.Shape)).Select(item => item.Hole).ToList();
        var pool = unused.Count;
        var missing = 0;
        foreach (var (hole, side) in front)
        {
            if (side == Side.Through)
                continue;
            var mate = unused.FindIndex(other => other.Shape == hole.Shape && SamePosition(hole.Point, other.Point, normal));
            if (mate < 0)
                missing++;
            else
                unused.RemoveAt(mate);
        }

        var matched = pool - unused.Count;
        if (calloutCount is { } n)
        {
            if (n == 2 * front.Count && missing == 0 && unused.Count == 0)
                return (OppositeResult.Same, front.Count);
            return n > front.Count ? (OppositeResult.Different, Math.Max(n - front.Count, pool)) : (OppositeResult.None, 0);
        }

        if (matched == front.Count && unused.Count == 0)
            return (OppositeResult.Same, matched);
        return pool > 0 ? (OppositeResult.Different, pool) : (OppositeResult.None, 0);
    }

    /// <summary>
    /// 沿 <paramref name="direction"/> 的两面都有只开在那一面的孔、且两面对不上（不论种）：
    /// 只从一边投影看不全，两边都要开视图（<see cref="DrawingPlanner.SideViews"/>）。
    /// </summary>
    public static bool FacesDiffer(PartGeometry part, ModelDirection direction)
    {
        ArgumentNullException.ThrowIfNull(part);
        var plus = new List<PartHole>();
        var minus = new List<PartHole>();
        foreach (var hole in part.Holes)
        {
            var side = SideOf(hole, part.Box, direction);
            if (side == Side.Plus)
                plus.Add(hole);
            else if (side == Side.Minus)
                minus.Add(hole);
        }

        if (plus.Count == 0 || minus.Count == 0)
            return false;
        if (plus.Count != minus.Count)
            return true;
        foreach (var hole in plus)
        {
            var mate = minus.FindIndex(other => other.Shape == hole.Shape && SamePosition(hole.Point, other.Point, direction));
            if (mate < 0)
                return true;
            minus.RemoveAt(mate);
        }

        return false;
    }

    /// <summary>
    /// 孔沿 <paramref name="direction"/> 开在哪一面：孔的起止碰到零件包围盒那一头（外表面）就算开在那面；
    /// 读不到起止退回孔口方向（<see cref="PartHole.Openings"/>）。
    /// </summary>
    internal static Side SideOf(PartHole hole, ModelBox box, ModelDirection direction)
    {
        if (Math.Abs(hole.Axis.Dot(direction)) < AxisCosine)
            return Side.Neither;
        bool plus, minus;
        if (hole.RangeAlong(direction) is { } range)
        {
            var ends = box.Corners().Select(corner => corner.Dot(direction)).ToList();
            plus = range.Max >= ends.Max() - PositionTolerance;
            minus = range.Min <= ends.Min() + PositionTolerance;
        }
        else
        {
            plus = hole.Openings.Any(o => o.Dot(direction) >= AxisCosine);
            minus = hole.Openings.Any(o => o.Dot(direction) <= -AxisCosine);
        }

        return (plus, minus) switch
        {
            (true, true) => Side.Through,
            (true, false) => Side.Plus,
            (false, true) => Side.Minus,
            _ => Side.Neither,
        };
    }

    /// <summary>视图里的种（<see cref="HoleEdge.Kind"/> = 组件 + "/" + 特征）是不是这个特征的。组件名可能含「/」，只比结尾。</summary>
    public static bool KindIsFeature(string kind, string feature)
        => feature.Length > 0 && kind.EndsWith("/" + feature, StringComparison.Ordinal);

    /// <summary>显示出来的文字里已经有「(含对面)」了吗（空格不论）。</summary>
    public static bool HasMarker(IEnumerable<string> texts)
        => texts.Any(text => text.Replace(" ", string.Empty, StringComparison.Ordinal).Contains(Marker, StringComparison.Ordinal));

    /// <summary>孔标注显示文字里的「N x」（第一段以数字加 x / × 开头的）；没有为 null。</summary>
    public static int? CalloutCount(IEnumerable<string> texts)
    {
        foreach (var text in texts)
        {
            var match = Regex.Match(text, @"^\s*(\d+)\s*[xX×]");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var count))
                return count;
        }

        return null;
    }

    private static bool SamePosition(ModelDirection p, ModelDirection q, ModelDirection direction)
    {
        var d = new ModelDirection(p.X - q.X, p.Y - q.Y, p.Z - q.Z);
        var along = d.Dot(direction);
        var across = new ModelDirection(d.X - along * direction.X, d.Y - along * direction.Y, d.Z - along * direction.Z);
        return Math.Sqrt(across.Dot(across)) <= PositionTolerance;
    }

    private static bool SameRadius(double a, double b) => Math.Abs(a - b) <= Math.Max(1e-7, b * HoleCalloutPlanner.RadiusTolerance * 10);
}
