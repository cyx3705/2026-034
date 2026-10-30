using System.Globalization;

namespace HistoryStrenua;

/// <summary>
/// 切成两段半圆的整圆孔（1.25.0，导入件：真机夹爪每个孔都是两条半圆边、两张半圆柱面）。纯几何，离线可测。
/// </summary>
/// <remarks>
/// 以前半圆一律当腰型孔端头，配不上就丢，孔全认不出（夹爪2），配错了标成「R3.50 贯穿」（夹爪）。
/// SolidWorks 孔标注从半圆进去只会写 R，所以这种孔标直径尺寸，数量与沉孔写在前后缀里。
/// </remarks>
internal static class SplitHole
{
    /// <summary>同心、同半径、背对背鼓的两段半圆并成一个整圆孔（留第一条边）；别的原样，次序不变。</summary>
    public static List<HoleEdge> Merge(IReadOnlyList<HoleEdge> edges)
    {
        var result = new List<HoleEdge>(edges.Count);
        var used = new HashSet<int>();
        for (var i = 0; i < edges.Count; i++)
        {
            if (used.Contains(i))
                continue;
            var edge = edges[i];
            var twin = edge.IsSlotEnd ? FindTwin(edges, used, i) : -1;
            if (twin < 0)
            {
                result.Add(edge);
                continue;
            }

            used.Add(twin);
            result.Add(edge with { BulgeX = 0, BulgeY = 0, Split = true });
        }

        return result;
    }

    private static int FindTwin(IReadOnlyList<HoleEdge> edges, HashSet<int> used, int index)
    {
        var edge = edges[index];
        for (var j = index + 1; j < edges.Count; j++)
        {
            var other = edges[j];
            if (!used.Contains(j) && other.IsSlotEnd
                && Math.Abs(edge.Radius - other.Radius) <= HoleCalloutPlanner.CenterTolerance
                && HoleCalloutPlanner.SameCenter(edge.X, edge.Y, other.X, other.Y)
                && edge.BulgeX * other.BulgeX + edge.BulgeY * other.BulgeY <= -SlotPlanner.AlignCosine)
                return j;
        }

        return -1;
    }

    /// <summary>同轴、同半径、都不是整圈的内凹圆柱面并成一张整圈的（起止取并集），零件层面才认得出这个孔。</summary>
    public static IReadOnlyList<PartCylinder> Whole(IReadOnlyList<PartCylinder> cylinders)
    {
        var result = new List<PartCylinder>(cylinders.Count);
        var used = new HashSet<int>();
        for (var i = 0; i < cylinders.Count; i++)
        {
            if (used.Contains(i))
                continue;
            var cylinder = cylinders[i];
            var twin = !cylinder.Full && cylinder.Concave ? FindWall(cylinders, used, i) : -1;
            if (twin < 0)
            {
                result.Add(cylinder);
                continue;
            }

            used.Add(twin);
            var other = cylinders[twin];
            result.Add(cylinder with
            {
                Full = true,
                Openings = cylinder.Openings.Concat(other.Openings).ToList(),
                AxialMin = Math.Min(cylinder.AxialMin, other.AxialMin),
                AxialMax = Math.Max(cylinder.AxialMax, other.AxialMax),
            });
        }

        return result;
    }

    private static int FindWall(IReadOnlyList<PartCylinder> cylinders, HashSet<int> used, int index)
    {
        var wall = cylinders[index];
        for (var j = index + 1; j < cylinders.Count; j++)
        {
            var other = cylinders[j];
            if (!used.Contains(j) && !other.Full && other.Concave
                && Math.Abs(wall.Radius - other.Radius) <= wall.Radius * HoleCalloutPlanner.RadiusTolerance
                && PartHole.SameAxis(wall, other))
                return j;
        }

        return -1;
    }

    /// <summary>直径尺寸前缀：同种不止一个写「N x 」。真机：写了前缀 SolidWorks 就不再自动带 Ø，要自己写上。</summary>
    public static string Prefix(int count) => (count > 1 ? count.ToString(CultureInfo.InvariantCulture) + " x " : string.Empty) + "<MOD-DIAM>";

    /// <summary>沉孔那一行（SolidWorks 符号代码），如 ⌴Ø11 ↧6；<paramref name="depth"/> 单位米。</summary>
    public static string CounterboreLine(double diameter, double depth)
        => $"<HOLE-SPOT> <MOD-DIAM>{Mm(diameter)} <HOLE-DEPTH> {Mm(depth)}";

    public static string Mm(double meters) => (meters * 1000).ToString("0.##", CultureInfo.InvariantCulture);
}
