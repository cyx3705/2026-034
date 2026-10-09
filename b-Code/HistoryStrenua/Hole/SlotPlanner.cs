namespace HistoryStrenua;

/// <summary>
/// 腰型孔（两端半圆、中间直边的长圆孔）的纯几何部分：端头半圆怎么认、两个端头怎么配成一个腰型孔。
/// 不碰 SolidWorks，所以能离线测。
/// </summary>
/// <remarks>
/// <para>
/// 视图里的腰型孔是两条半圆边（各贴一张内凹的半圆柱面）加两条直边。端头半圆按孔的判据认（内凹、同半径、正对图纸），
/// 只是不要求整圈、改要求<b>恰好半圈</b>，并记下它朝哪边鼓（<see cref="HoleEdge.BulgeX"/>）。
/// </para>
/// <para>
/// 两个端头配成一对的条件：同一种（组件 + 特征）、同半径、互相背对着鼓——A 朝远离 B 的方向鼓、B 朝远离 A 的方向鼓，
/// 且都沿两圆心连线。一排首尾相接的腰型孔里，相邻两个孔相对的端头是互相朝着鼓的，不会配错。
/// 配不上的半圆（开口槽口的端头、圆弧槽）丢掉，与 1.2 以前一样不算孔。
/// </para>
/// </remarks>
internal static class SlotPlanner
{
    /// <summary>弦中点离圆心不超过半径的这个比例，才算恰好半圈。</summary>
    public const double SemicircleTolerance = 1e-3;

    /// <summary>端头鼓出方向与两圆心连线的夹角余弦至少这么大，才算沿连线背对着鼓。</summary>
    public const double AlignCosine = 0.999;

    /// <summary>
    /// 圆弧是不是恰好半圈：两个端点的中点落在圆心上。
    /// </summary>
    /// <param name="circle"><c>CircleParams</c>：圆心 xyz、轴向 xyz、半径。</param>
    /// <param name="start">起点 xyz。</param>
    /// <param name="end">终点 xyz。</param>
    public static bool IsSemicircle(double[] circle, double[] start, double[] end)
    {
        var dx = (start[0] + end[0]) / 2 - circle[0];
        var dy = (start[1] + end[1]) / 2 - circle[1];
        var dz = (start[2] + end[2]) / 2 - circle[2];
        return Math.Sqrt(dx * dx + dy * dy + dz * dz) <= circle[6] * SemicircleTolerance;
    }

    /// <summary>
    /// 半圆可能鼓出的方向之一：轴线 × 弦，单位向量。真正鼓向这边还是反过来，要看圆弧上有没有
    /// 「圆心 + 半径 × 这个方向」那个点（<see cref="Bulge"/>）。
    /// </summary>
    public static (double X, double Y, double Z) ChordNormal(double[] circle, double[] start, double[] end)
    {
        var (ax, ay, az) = (circle[3], circle[4], circle[5]);
        var (kx, ky, kz) = (end[0] - start[0], end[1] - start[1], end[2] - start[2]);
        var nx = ay * kz - az * ky;
        var ny = az * kx - ax * kz;
        var nz = ax * ky - ay * kx;
        var length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        return length > 0 ? (nx / length, ny / length, nz / length) : (0, 0, 0);
    }

    /// <summary>
    /// 在圆弧上找离「圆心 + 半径 × <paramref name="normal"/>」最近的点（<c>IEdge.GetClosestPointOn</c>）：
    /// 那个点就在弧上（离得近）说明弧往 <paramref name="normal"/> 这边鼓，否则最近点落在端点上（约 √2 倍半径远），是反方向。
    /// </summary>
    /// <param name="normal"><see cref="ChordNormal"/>。</param>
    /// <param name="probe">圆心 + 半径 × <paramref name="normal"/>。</param>
    /// <param name="closest">圆弧上离 <paramref name="probe"/> 最近的点。</param>
    /// <param name="radius">半径。</param>
    public static (double X, double Y, double Z) Bulge(
        (double X, double Y, double Z) normal, double[] probe, double[] closest, double radius)
    {
        var dx = closest[0] - probe[0];
        var dy = closest[1] - probe[1];
        var dz = closest[2] - probe[2];
        return Math.Sqrt(dx * dx + dy * dy + dz * dz) < radius / 2
            ? normal
            : (-normal.X, -normal.Y, -normal.Z);
    }

    /// <summary>
    /// 把合并过同心的孔里那些腰型孔端头配成对：配上的两端共用一个 <see cref="HoleEdge.Slot"/> 编号，
    /// 种名后面加上「/腰 + 两端距离」（同一特征里长短不同的腰型孔是两种，也不和同特征的圆孔混成一种）；
    /// 配不上的端头丢掉。圆孔原样保留，先后次序不变。
    /// </summary>
    public static List<HoleEdge> Pair(IReadOnlyList<HoleEdge> holes)
    {
        ArgumentNullException.ThrowIfNull(holes);
        var candidates = new List<(int A, int B, double Length)>();
        for (var a = 0; a < holes.Count; a++)
        {
            for (var b = a + 1; b < holes.Count; b++)
            {
                if (Facing(holes[a], holes[b]) is { } length)
                    candidates.Add((a, b, length));
            }
        }

        // 近的先配：一个端头在几对里都说得通时，取最近的那个伙伴。
        var slots = new Dictionary<int, (int Slot, double Length)>();
        foreach (var (a, b, length) in candidates.OrderBy(pair => pair.Length))
        {
            if (slots.ContainsKey(a) || slots.ContainsKey(b))
                continue;
            var slot = slots.Count / 2;
            slots[a] = (slot, length);
            slots[b] = (slot, length);
        }

        var result = new List<HoleEdge>(holes.Count);
        for (var i = 0; i < holes.Count; i++)
        {
            var hole = holes[i];
            if (!hole.IsSlotEnd)
                result.Add(hole);
            else if (slots.TryGetValue(i, out var slot))
                result.Add(hole with { Slot = slot.Slot, Kind = $"{hole.Kind}/腰{Math.Round(slot.Length / 1e-6)}" });
        }

        return result;
    }

    /// <summary>两个端头能不能配成一个腰型孔；能就返回两圆心的距离（图纸）。</summary>
    private static double? Facing(HoleEdge a, HoleEdge b)
    {
        if (!a.IsSlotEnd || !b.IsSlotEnd || a.Kind != b.Kind
            || Math.Abs(a.Radius - b.Radius) > HoleCalloutPlanner.CenterTolerance)
            return null;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= HoleCalloutPlanner.CenterTolerance)
            return null;
        // A 背着 B 鼓、B 背着 A 鼓。
        var awayA = -(a.BulgeX * dx + a.BulgeY * dy) / length;
        var awayB = (b.BulgeX * dx + b.BulgeY * dy) / length;
        return awayA >= AlignCosine && awayB >= AlignCosine ? length : null;
    }
}
