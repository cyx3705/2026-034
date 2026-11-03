using System.Globalization;
using System.Text;

namespace HistoryStrenua;

/// <summary>总装坐标（mm）里的一个点或方向。</summary>
internal readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);

    public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;

    public Vec3 Cross(Vec3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);

    public double Length => Math.Sqrt(Dot(this));

    public Vec3 Normalized() => Length > 0 ? this * (1 / Length) : this;

    /// <summary>规范符号：第一个不为 0 的分量为正（同一根轴两个朝向认成一样）。</summary>
    public Vec3 Canonical()
        => Math.Abs(X) > 1e-6 ? (X < 0 ? -this : this) : Math.Abs(Y) > 1e-6 ? (Y < 0 ? -this : this) : Z < 0 ? -this : this;
}

/// <summary>接触面检测关心的曲面种类。</summary>
internal enum ContactSurface
{
    Plane,
    Cylinder,
}

/// <summary>
/// 总装坐标（mm）里一个零件面：属于哪个零部件、面编号（零部件各实体 <c>GetFaces</c> 依次排下来的序号，<c>asm.mate</c> 认的就是它）、曲面参数、三角面片（每 3 点一个三角形）。
/// <c>Normal</c> 平面是朝实体外的法向、圆柱是轴向；<c>Point</c> 平面上一点 / 圆柱轴上一点；<c>Hole</c> 圆柱是孔壁（凹）还是轴（凸）。
/// </summary>
internal sealed record ContactFace(string Owner, int Index, ContactSurface Surface, Vec3 Normal, Vec3 Point, double Radius, bool Hole, IReadOnlyList<Vec3> Triangles)
{
    /// <summary>给配合指令用的写法「零部件名@F序号」。</summary>
    public string Ref => $"{Owner}@F{Index}";

    public (Vec3 Min, Vec3 Max) Box
    {
        get
        {
            if (Triangles.Count == 0)
                return (Point, Point);
            var min = Triangles[0];
            var max = Triangles[0];
            foreach (var p in Triangles)
            {
                min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
            }

            return (min, max);
        }
    }
}

/// <summary>
/// 一对面的接触：<see cref="Gap"/> 平面是两面距离（正 = 离开、负 = 压进去），圆柱是半径差（正 = 间隙、负 = 过盈）。
/// 平面的 <see cref="Amount"/> 是投影重叠面积（mm²），<see cref="Pieces"/> 是重叠区（截面坐标）；圆柱的 <see cref="Amount"/> 是轴向重叠长度，
/// <see cref="From"/>–<see cref="To"/> 是重叠段在轴上的位置。
/// </summary>
internal sealed record ContactPair(ContactFace Mine, ContactFace Other, double Gap, double Amount, IReadOnlyList<IReadOnlyList<(double U, double V)>> Pieces, double From = 0, double To = 0, double Offset = 0);

/// <summary>同一个面（同一平面 / 同一根轴同一半径）上的全部接触，一个面一个对象（用户定）。</summary>
internal sealed record ContactGroup(string Id, ContactSurface Surface, Vec3 Normal, Vec3 Point, double Radius, IReadOnlyList<ContactPair> Pairs);

/// <summary>
/// 装配类「接触面检测」<c>strenua.asm.contact</c>（1.39.0，用户定：摸清装配点是否符合预期）的纯几何部分：两面是否接触、重叠多大、按面分组、写截面 SVG。
/// </summary>
/// <remarks>
/// 只认平面对平面（法向相反、距离在 near 内、投影有重叠）与圆柱对圆柱（同轴、一孔一轴、半径差在 near 内、轴向有重叠）。
/// 重叠面积按三角面片两两求凸多边形交（Sutherland–Hodgman），不依赖 SolidWorks 的布尔运算；曲面别的组合（圆柱贴平面等）不报。
/// 截面坐标：法向沿总装坐标轴时取另两根总装轴（u、v 就是总装 mm），斜面取面内两根正交方向。
/// </remarks>
internal static class ContactPlanner
{
    /// <summary>两根方向当作平行的界限（约 1°）。</summary>
    private const double Parallel = 0.99985;

    /// <summary>重叠面积小于它（mm²）不算接触。</summary>
    private const double MinArea = 0.01;

    /// <summary>
    /// <paramref name="mine"/> 与 <paramref name="other"/> 是否接触或在 <paramref name="near"/>（mm）内差一点接触；<paramref name="tol"/> 以内算贴合（圆柱同轴的允差也用它）。
    /// </summary>
    public static ContactPair? Match(ContactFace mine, ContactFace other, double near, double tol)
    {
        if (mine.Surface != other.Surface)
            return null;
        return mine.Surface == ContactSurface.Plane ? MatchPlanes(mine, other, near) : MatchCylinders(mine, other, near, tol);
    }

    private static ContactPair? MatchPlanes(ContactFace mine, ContactFace other, double near)
    {
        if (mine.Normal.Dot(other.Normal) > -Parallel)
            return null;
        var gap = (other.Point - mine.Point).Dot(mine.Normal);
        if (Math.Abs(gap) > near)
            return null;
        var (u, v, _, _) = Frame(mine.Normal);
        var pieces = new List<IReadOnlyList<(double U, double V)>>();
        var area = 0.0;
        var a = Project(mine.Triangles, u, v);
        var b = Project(other.Triangles, u, v);
        foreach (var ta in a)
        {
            foreach (var tb in b)
            {
                if (ta.MaxU < tb.MinU || tb.MaxU < ta.MinU || ta.MaxV < tb.MinV || tb.MaxV < ta.MinV)
                    continue;
                var piece = Clip(ta.Points, tb.Points);
                var s = Math.Abs(Area(piece));
                if (s < 1e-9)
                    continue;
                area += s;
                pieces.Add(piece);
            }
        }

        return area < MinArea ? null : new ContactPair(mine, other, gap, area, pieces);
    }

    private static ContactPair? MatchCylinders(ContactFace mine, ContactFace other, double near, double tol)
    {
        if (Math.Abs(mine.Normal.Dot(other.Normal)) < Parallel || mine.Hole == other.Hole)
            return null;
        var axis = mine.Normal.Normalized();
        var gap = mine.Hole ? mine.Radius - other.Radius : other.Radius - mine.Radius;
        if (Math.Abs(gap) > near)
            return null;
        var w = other.Point - mine.Point;
        var offset = (w - axis * w.Dot(axis)).Length;
        if (offset > Math.Max(tol, gap))
            return null;
        var (aFrom, aTo) = Span(mine.Triangles, axis);
        var (bFrom, bTo) = Span(other.Triangles, axis);
        var from = Math.Max(aFrom, bFrom);
        var to = Math.Min(aTo, bTo);
        return to - from <= tol ? null : new ContactPair(mine, other, gap, to - from, [], from, to, offset);
    }

    /// <summary>
    /// 按「我这边的面」分组：同一平面（法向、位置相同）或同一根轴同一半径的合成一个对象，平面在前，组内按重叠从大到小。
    /// 对方那个面已经和我贴合了的，它和我别的面「差一点没贴上」不再报（真机 2026-043：型材 T 槽口凹进 1 mm 的面对着脚杯连接板，全是这种）；
    /// 同一平面上已经和我贴合的零件，它别的面差一点也不报。
    /// </summary>
    public static IReadOnlyList<ContactGroup> Group(IEnumerable<ContactPair> pairs, double tol)
    {
        var all = pairs.ToList();
        var touched = all.Where(pair => Math.Abs(pair.Gap) <= tol).Select(pair => pair.Other.Ref).ToHashSet(StringComparer.Ordinal);
        var groups = new List<(ContactSurface Surface, Vec3 Normal, Vec3 Point, double Radius, List<ContactPair> Pairs)>();
        foreach (var pair in all.Where(pair => Math.Abs(pair.Gap) <= tol || !touched.Contains(pair.Other.Ref)))
        {
            var face = pair.Mine;
            var index = groups.FindIndex(group => group.Surface == face.Surface && Same(group.Surface, group.Normal, group.Point, group.Radius, face));
            if (index < 0)
                groups.Add((face.Surface, face.Normal, face.Point, face.Radius, [pair]));
            else
                groups[index].Pairs.Add(pair);
        }

        var planes = groups.Where(group => group.Surface == ContactSurface.Plane).OrderByDescending(group => group.Pairs.Sum(pair => pair.Amount));
        var cylinders = groups.Where(group => group.Surface == ContactSurface.Cylinder).OrderByDescending(group => group.Radius);
        var result = new List<ContactGroup>();
        foreach (var group in planes.Concat(cylinders))
        {
            var id = (group.Surface == ContactSurface.Plane ? "P" : "A") + (result.Count(item => item.Surface == group.Surface) + 1);
            // 同一平面上已经和我贴合的零件，它凹进去的别的面也不报（真机：模组底座的减重槽面差 0.97 mm）。
            var owners = group.Pairs.Where(pair => Math.Abs(pair.Gap) <= tol).Select(pair => pair.Other.Owner).ToHashSet(StringComparer.Ordinal);
            var kept = group.Pairs.Where(pair => Math.Abs(pair.Gap) <= tol || !owners.Contains(pair.Other.Owner)).OrderByDescending(pair => pair.Amount).ToList();
            result.Add(new ContactGroup(id, group.Surface, group.Normal, group.Point, group.Radius, kept));
        }

        return result;
    }

    private static bool Same(ContactSurface surface, Vec3 normal, Vec3 point, double radius, ContactFace face)
    {
        if (surface == ContactSurface.Plane)
            return normal.Dot(face.Normal) > Parallel && Math.Abs((face.Point - point).Dot(normal)) < 0.01;
        if (Math.Abs(normal.Dot(face.Normal)) < Parallel || Math.Abs(radius - face.Radius) > 0.01)
            return false;
        var axis = normal.Normalized();
        var w = face.Point - point;
        return (w - axis * w.Dot(axis)).Length < 0.01;
    }

    /// <summary>
    /// 截面坐标：<paramref name="normal"/> 沿总装坐标轴时 u、v 取另两根总装轴（名字 X / Y / Z，坐标就是总装 mm），斜面取面内两根正交方向（名字写成向量）。
    /// </summary>
    public static (Vec3 U, Vec3 V, string UName, string VName) Frame(Vec3 normal)
    {
        var n = normal.Normalized();
        var axes = new[] { new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1) };
        var along = Array.FindIndex(axes, axis => Math.Abs(axis.Dot(n)) > Parallel);
        if (along >= 0)
        {
            var rest = Enumerable.Range(0, 3).Where(i => i != along).ToArray();
            return (axes[rest[0]], axes[rest[1]], "XYZ"[rest[0]].ToString(), "XYZ"[rest[1]].ToString());
        }

        var seed = axes.OrderBy(axis => Math.Abs(axis.Dot(n))).First();
        var u = n.Cross(seed).Normalized();
        var v = n.Cross(u).Normalized();
        return (u, v, Direction(u), Direction(v));
    }

    /// <summary>方向写成文字：沿坐标轴的写「+Y」「-X」，斜的写「(0.71,0,0.71)」。</summary>
    public static string Direction(Vec3 direction)
    {
        var d = direction.Normalized();
        var parts = new[] { d.X, d.Y, d.Z };
        var along = Array.FindIndex(parts, value => Math.Abs(Math.Abs(value) - 1) < 1 - Parallel);
        return along >= 0 ? (parts[along] < 0 ? "-" : "+") + "XYZ"[along] : $"({F(d.X, "0.###")},{F(d.Y, "0.###")},{F(d.Z, "0.###")})";
    }

    private sealed record Flat((double U, double V)[] Points, double MinU, double MaxU, double MinV, double MaxV);

    private static List<Flat> Project(IReadOnlyList<Vec3> triangles, Vec3 u, Vec3 v)
    {
        var flats = new List<Flat>(triangles.Count / 3);
        for (var i = 0; i + 2 < triangles.Count; i += 3)
        {
            var points = new[] { triangles[i], triangles[i + 1], triangles[i + 2] }.Select(p => (p.Dot(u), p.Dot(v))).ToArray();
            if (Area(points) < 0)
                Array.Reverse(points);
            flats.Add(new Flat(points, points.Min(p => p.Item1), points.Max(p => p.Item1), points.Min(p => p.Item2), points.Max(p => p.Item2)));
        }

        return flats;
    }

    /// <summary>凸多边形 <paramref name="subject"/> 被逆时针凸多边形 <paramref name="clip"/> 裁剪（Sutherland–Hodgman）。</summary>
    public static IReadOnlyList<(double U, double V)> Clip(IReadOnlyList<(double U, double V)> subject, IReadOnlyList<(double U, double V)> clip)
    {
        var output = subject.ToList();
        for (var i = 0; i < clip.Count && output.Count > 0; i++)
        {
            var a = clip[i];
            var b = clip[(i + 1) % clip.Count];
            var input = output;
            output = [];
            for (var j = 0; j < input.Count; j++)
            {
                var p = input[j];
                var q = input[(j + 1) % input.Count];
                var sp = Side(a, b, p);
                var sq = Side(a, b, q);
                if (sp >= 0)
                    output.Add(p);
                if (sp >= 0 != sq >= 0)
                {
                    var t = sp / (sp - sq);
                    output.Add((p.U + t * (q.U - p.U), p.V + t * (q.V - p.V)));
                }
            }
        }

        return output;
    }

    private static double Side((double U, double V) a, (double U, double V) b, (double U, double V) p)
        => (b.U - a.U) * (p.V - a.V) - (b.V - a.V) * (p.U - a.U);

    /// <summary>多边形有向面积（逆时针为正）。</summary>
    public static double Area(IReadOnlyList<(double U, double V)> polygon)
    {
        var sum = 0.0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var p = polygon[i];
            var q = polygon[(i + 1) % polygon.Count];
            sum += p.U * q.V - q.U * p.V;
        }

        return sum / 2;
    }

    private static (double From, double To) Span(IReadOnlyList<Vec3> points, Vec3 axis)
    {
        if (points.Count == 0)
            return (0, 0);
        var values = points.Select(p => p.Dot(axis)).ToList();
        return (values.Min(), values.Max());
    }

    /// <summary>
    /// 三角面片的外轮廓（含内孔）：只被一个三角形用到的边连成环，按截面坐标给出。
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<(double U, double V)>> Outline(IReadOnlyList<Vec3> triangles, Vec3 u, Vec3 v)
    {
        static (long, long, long) Key(Vec3 p) => ((long)Math.Round(p.X * 1000), (long)Math.Round(p.Y * 1000), (long)Math.Round(p.Z * 1000));
        var count = new Dictionary<((long, long, long), (long, long, long)), int>();
        var at = new Dictionary<(long, long, long), Vec3>();
        for (var i = 0; i + 2 < triangles.Count; i += 3)
        {
            for (var k = 0; k < 3; k++)
            {
                var p = Key(triangles[i + k]);
                var q = Key(triangles[i + (k + 1) % 3]);
                at[p] = triangles[i + k];
                if (p.Equals(q))
                    continue;
                var edge = Comparer<(long, long, long)>.Default.Compare(p, q) < 0 ? (p, q) : (q, p);
                count[edge] = count.GetValueOrDefault(edge) + 1;
            }
        }

        var next = new Dictionary<(long, long, long), List<(long, long, long)>>();
        foreach (var ((p, q), n) in count)
        {
            if (n != 1)
                continue;
            (next.TryGetValue(p, out var lp) ? lp : next[p] = []).Add(q);
            (next.TryGetValue(q, out var lq) ? lq : next[q] = []).Add(p);
        }

        var loops = new List<IReadOnlyList<(double U, double V)>>();
        while (next.Count > 0)
        {
            var start = next.Keys.First();
            var loop = new List<(double U, double V)>();
            var current = start;
            while (next.TryGetValue(current, out var list) && list.Count > 0)
            {
                loop.Add((at[current].Dot(u), at[current].Dot(v)));
                var step = list[0];
                list.RemoveAt(0);
                if (list.Count == 0)
                    next.Remove(current);
                if (next.TryGetValue(step, out var back))
                {
                    back.Remove(current);
                    if (back.Count == 0)
                        next.Remove(step);
                }

                current = step;
                if (current.Equals(start))
                    break;
            }

            if (loop.Count >= 2)
                loops.Add(loop);
        }

        return loops;
    }

    /// <summary>这个面写成文字：平面「Y=145 朝 +Y」，圆柱「孔 Ø6.6 轴线 Y 向，过 X-600 Z30」。</summary>
    public static string Describe(ContactGroup group)
    {
        if (group.Surface == ContactSurface.Plane)
        {
            var direction = Direction(group.Normal);
            return direction.Length == 2
                ? $"平面 {direction[1]}={F(group.Point.Dot(Axis(direction[1])))} 朝 {direction}"
                : $"平面 法向 {direction} 过 ({F(group.Point.X)},{F(group.Point.Y)},{F(group.Point.Z)})";
        }

        var hole = group.Pairs.Count > 0 && group.Pairs[0].Mine.Hole ? "孔" : "轴";
        var axis = Direction(group.Normal.Canonical());
        var (u, v, un, vn) = Frame(group.Normal);
        var line = axis.Length == 2
            ? $"轴线 {axis[1]} 向，过 {un}{F(group.Point.Dot(u))} {vn}{F(group.Point.Dot(v))}"
            : $"轴线 {axis} 过 ({F(group.Point.X)},{F(group.Point.Y)},{F(group.Point.Z)})";
        return $"{hole} Ø{F(group.Radius * 2)} {line}";
    }

    /// <summary>一对面的状态：平面「贴合」「差 0.5 没贴上」「压进 0.2」，圆柱「同径」「直径差 0.6」「过盈 0.02」，同轴偏了带「偏心」。</summary>
    public static string State(ContactPair pair, double tol)
    {
        if (pair.Mine.Surface == ContactSurface.Plane)
            return Math.Abs(pair.Gap) <= tol ? "贴合" : pair.Gap > 0 ? $"差 {F(pair.Gap, "0.###")} 没贴上" : $"压进 {F(-pair.Gap, "0.###")}";
        var state = Math.Abs(pair.Gap) <= tol ? "同径" : pair.Gap > 0 ? $"直径差 {F(pair.Gap * 2, "0.###")}" : $"过盈 {F(-pair.Gap * 2, "0.###")}";
        return pair.Offset > tol ? state + $"、偏心 {F(pair.Offset, "0.###")}" : state;
    }

    /// <summary>一个对象下按对方的面合并成行（我这边的面被槽、孔切成几块时，同一个对方面只写一行，面编号并列）。</summary>
    public static IReadOnlyList<string> Lines(ContactGroup group, double tol)
        => group.Pairs.GroupBy(pair => (pair.Other.Ref, State(pair, tol)))
            .Select(same => same.ToList())
            .Select(same => Line(same.Count == 1 ? same[0] : same[0] with
            {
                Amount = group.Surface == ContactSurface.Plane ? same.Sum(pair => pair.Amount) : same.Max(pair => pair.Amount),
                Pieces = same.SelectMany(pair => pair.Pieces).ToList(),
                From = same.Min(pair => pair.From),
                To = same.Max(pair => pair.To),
            }, tol, same.Select(pair => pair.Mine.Index).Distinct().Order()))
            .ToList();

    /// <summary>一对面写成一行：对方、状态、重叠多大在哪、两边的面编号。</summary>
    public static string Line(ContactPair pair, double tol, IEnumerable<int>? mine = null)
    {
        var (_, _, un, vn) = Frame(pair.Mine.Normal);
        string where;
        if (pair.Mine.Surface == ContactSurface.Plane)
        {
            var points = pair.Pieces.SelectMany(piece => piece).ToList();
            where = $"重叠 {F(pair.Amount, "0.#")} mm²";
            if (points.Count > 0 && un.Length == 1)
                where += $"（{un} {F(points.Min(p => p.U))}~{F(points.Max(p => p.U))}，{vn} {F(points.Min(p => p.V))}~{F(points.Max(p => p.V))}）";
        }
        else
        {
            var axis = Direction(pair.Mine.Normal.Canonical());
            var sign = pair.Mine.Normal.Normalized().Dot(pair.Mine.Normal.Canonical()) < 0 ? -1 : 1;
            var (a, b) = (sign * pair.From, sign * pair.To);
            where = $"轴向重叠 {F(pair.Amount)}" + (axis.Length == 2 ? $"（{axis[1]} {F(Math.Min(a, b))}~{F(Math.Max(a, b))}）" : string.Empty);
        }

        var shape = pair.Other.Surface == ContactSurface.Cylinder ? $" {(pair.Other.Hole ? "孔" : "轴")} Ø{F(pair.Other.Radius * 2)}" : string.Empty;
        return $"↔ {pair.Other.Owner}{shape}：{State(pair, tol)}，{where}  [{string.Join(",", (mine ?? [pair.Mine.Index]).Select(index => "F" + index))} ↔ {pair.Other.Ref}]";
    }

    private static Vec3 Axis(char name) => name switch
    {
        'X' => new Vec3(1, 0, 0),
        'Y' => new Vec3(0, 1, 0),
        _ => new Vec3(0, 0, 1),
    };

    /// <summary>
    /// 每个接触对象一块截面（平面：沿法向看，画我的面轮廓、对方的面轮廓、重叠区；圆柱：沿轴剖开，横向轴向、竖向半径）。
    /// 坐标是截面 mm（平面沿坐标轴时就是总装 mm），每块按重叠区取景（四周留 15% 或 20 mm）、内层翻 Y。重叠区橙色贴合、灰色没贴上、红色压进 / 过盈。
    /// </summary>
    public static string Svg(string title, IReadOnlyList<ContactGroup> groups, double tol)
    {
        const int Width = 300, Height = 280, Header = 34, Columns = 3;
        var rows = Math.Max(1, (groups.Count + Columns - 1) / Columns);
        var svg = new StringBuilder();
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Columns * Width}\" height=\"{rows * Height}\" font-family=\"sans-serif\" font-size=\"11\" data-title=\"{Escape(title)}\">");
        svg.Append("<rect width=\"100%\" height=\"100%\" fill=\"#FBFAF7\"/>");
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var plane = group.Surface == ContactSurface.Plane;
            var (u, v, un, vn) = Frame(group.Normal);
            svg.Append($"<g id=\"{group.Id}\" data-face=\"{Escape(Describe(group))}\" data-u=\"{(plane ? un : "轴向")}\" data-v=\"{(plane ? vn : "半径")}\" transform=\"translate({i % Columns * Width},{i / Columns * Height})\">");
            svg.Append($"<rect x=\"2\" y=\"2\" width=\"{Width - 4}\" height=\"{Height - 4}\" fill=\"none\" stroke=\"#D6D2C8\"/>");
            svg.Append($"<text x=\"8\" y=\"16\" font-weight=\"bold\">{group.Id} {Escape(Describe(group))}</text>");
            svg.Append($"<text x=\"8\" y=\"29\" fill=\"#6B675E\">横 {(plane ? un : "轴向")} → 竖 {(plane ? vn : "半径")} ↑（mm）</text>");
            var body = new StringBuilder();
            var extent = new List<(double U, double V)>();
            if (plane)
            {
                foreach (var face in group.Pairs.Select(pair => pair.Mine).DistinctBy(face => face.Index))
                {
                    var loops = Outline(face.Triangles, u, v);
                    body.Append($"<path data-ref=\"{Escape(face.Ref)}\" d=\"{Path(loops)}\" fill=\"none\" stroke=\"#38352F\" stroke-width=\"1.6\" vector-effect=\"non-scaling-stroke\"/>");
                }

                foreach (var pair in group.Pairs)
                {
                    body.Append($"<path data-ref=\"{Escape(pair.Other.Ref)}\" d=\"{Path(Outline(pair.Other.Triangles, u, v))}\" fill=\"none\" stroke=\"#2F6FB0\" stroke-width=\"1\" vector-effect=\"non-scaling-stroke\"/>");
                    extent.AddRange(pair.Pieces.SelectMany(piece => piece));
                    body.Append($"<path data-ref=\"{Escape(pair.Other.Ref)}\" data-state=\"{Escape(State(pair, tol))}\" data-area=\"{F(pair.Amount, "0.#")}\" d=\"{Path(pair.Pieces)}\" fill=\"{Fill(pair, tol)}\" fill-opacity=\"0.55\"/>");
                }
            }
            else
            {
                var axis = group.Normal.Normalized();
                foreach (var face in group.Pairs.Select(pair => pair.Mine).DistinctBy(face => face.Index))
                {
                    var (from, to) = Span(face.Triangles, axis);
                    extent.Add((from, -face.Radius));
                    extent.Add((to, face.Radius));
                    body.Append($"<rect data-ref=\"{Escape(face.Ref)}\" x=\"{F(from)}\" y=\"{F(-face.Radius)}\" width=\"{F(to - from)}\" height=\"{F(face.Radius * 2)}\" fill=\"none\" stroke=\"#38352F\" stroke-width=\"1.6\" vector-effect=\"non-scaling-stroke\"/>");
                }

                foreach (var pair in group.Pairs)
                {
                    var (from, to) = Span(pair.Other.Triangles, axis);
                    var r = pair.Other.Radius;
                    var inner = Math.Min(r, pair.Mine.Radius);
                    body.Append($"<rect data-ref=\"{Escape(pair.Other.Ref)}\" x=\"{F(from)}\" y=\"{F(-r)}\" width=\"{F(to - from)}\" height=\"{F(r * 2)}\" fill=\"none\" stroke=\"#2F6FB0\" stroke-width=\"1\" vector-effect=\"non-scaling-stroke\"/>");
                    body.Append($"<rect data-ref=\"{Escape(pair.Other.Ref)}\" data-state=\"{Escape(State(pair, tol))}\" x=\"{F(pair.From)}\" y=\"{F(-inner)}\" width=\"{F(pair.To - pair.From)}\" height=\"{F(inner * 2)}\" fill=\"{Fill(pair, tol)}\" fill-opacity=\"0.55\"/>");
                }
            }

            if (extent.Count > 0)
            {
                // 按重叠区取景（长型材上的接触区否则缩成一个点），零件轮廓超出的部分裁掉。
                var (minU, maxU) = (extent.Min(p => p.U), extent.Max(p => p.U));
                var (minV, maxV) = (extent.Min(p => p.V), extent.Max(p => p.V));
                var pad = Math.Max(Math.Max(maxU - minU, maxV - minV) * 0.15, 20);
                svg.Append($"<svg x=\"6\" y=\"{Header + 2}\" width=\"{Width - 12}\" height=\"{Height - Header - 8}\" viewBox=\"{F(minU - pad)} {F(-maxV - pad)} {F(maxU - minU + 2 * pad)} {F(maxV - minV + 2 * pad)}\">");
                svg.Append("<g transform=\"scale(1,-1)\">").Append(body).Append("</g></svg>");
            }

            svg.Append("</g>");
        }

        return svg.Append("</svg>").ToString();
    }

    private static string Fill(ContactPair pair, double tol)
        => Math.Abs(pair.Gap) <= tol ? "#D9822B" : pair.Gap > 0 ? "#8C8C8C" : "#C0392B";

    private static string Path(IEnumerable<IReadOnlyList<(double U, double V)>> loops)
    {
        var d = new StringBuilder();
        foreach (var loop in loops)
        {
            for (var i = 0; i < loop.Count; i++)
                d.Append(i == 0 ? 'M' : 'L').Append(F(loop[i].U)).Append(',').Append(F(loop[i].V)).Append(' ');
            d.Append("Z ");
        }

        return d.ToString().TrimEnd();
    }

    private static string Escape(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace("\"", "&quot;");

    private static string F(double value, string format = "0.##")
    {
        var text = value.ToString(format, CultureInfo.InvariantCulture);
        return text == "-0" ? "0" : text;
    }
}
