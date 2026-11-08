using System.Globalization;
using System.Text;

namespace HistoryStrenua;

/// <summary>包络一个面的来源：跟零件包围盒、跟零件上某个面、手填数。</summary>
internal enum EnvelopeSource
{
    Box,
    Face,
    Manual,
}

/// <summary>
/// 包络的一侧（0 −X、1 +X、2 −Y、3 +Y、4 −Z、5 +Z）。<paramref name="Face"/> 是 F 序号（同 asm.contact），
/// <paramref name="Offset"/> 往外为正，<paramref name="Axis"/> 圆柱面取轴线（否则取切线），
/// <paramref name="Value"/> 是零件坐标 mm：手填就是它，跟面时是上次算出的值（面失效时用它、对比看面变没变）。
/// <paramref name="Tilt"/> 斜面的朝外法向（零件坐标，单位向量），正的面为 null；斜面的 <paramref name="Value"/> 是它在包围盒这一面中心那条线上的位置。
/// </summary>
internal sealed record EnvelopeSide(int Side, EnvelopeSource Source, int Face, double Offset, bool Axis, double Value, Vec3? Tilt = null);

/// <summary>贴面摆位的一对：这个零件包络的哪一侧，贴到哪个零部件的哪个面，隔多少（正 = 离开）。</summary>
internal sealed record EnvelopeSnap(int Side, MateTarget Onto, double Gap);

/// <summary>零件坐标里的一个面（平面：法向 + 面上一点；圆柱：轴向 + 轴上一点 + 半径 + 是不是孔）。</summary>
internal sealed record EnvelopeFaceGeometry(bool Plane, Vec3 Direction, Vec3 Point, double Radius, bool Hole);

/// <summary>
/// 装配包络（1.42.0，用户定：零件包围盒常和装配关系不大，要一个能把几个面改成真实装配面的盒）：纯文本与几何，离线可测。
/// </summary>
/// <remarks>
/// 盒在<b>零件坐标</b>里、轴对齐（我定：跟零件走，换个总装、换个摆法都不变），六个面各自记来源，没改的面跟包围盒。
/// 存在零件自定义属性 <see cref="PropertyName"/>（文件级），写法一行：<c>-X:盒;+X:盒;-Y:F12@0;+Y:F3+1.5@40;-Z:F7/轴@25;+Z:=120</c>——
/// <c>盒</c> 跟包围盒；<c>F序号[±偏移][/轴]@上次值</c> 跟面；<c>=数</c> 手填。
/// 认得「平面法向靠这一侧（斜 45° 以内）」和「圆柱轴线垂直这一侧」两种目标面，别的拒绝不猜；斜面写 <c>F12^nx,ny,nz@值</c>（用户定：斜的装配面在盒差时把零件转回去）。
/// </remarks>
internal static class EnvelopeSpec
{
    /// <summary>零件自定义属性名。</summary>
    public const string PropertyName = "Strenua包络";

    public static readonly string[] SideNames = ["-X", "+X", "-Y", "+Y", "-Z", "+Z"];

    /// <summary>平面算正对的余弦下限、圆柱算垂直的余弦上限、斜面最多斜多少（余弦下限，45°）。</summary>
    private const double Parallel = 0.999, Perpendicular = 0.001, MaxTilt = 0.7071;

    /// <summary>两个装配面的夹角和包围盒对应两面的夹角最多差多少度，超过就转不回去（两面不能同时贴上）。</summary>
    public const double AngleTolerance = 0.5;

    public static int AxisOf(int side) => side / 2;

    public static int SignOf(int side) => side % 2 == 0 ? -1 : 1;

    public static double Component(Vec3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    /// <summary>「+X / -y / X（=+X）/ x+」→ 侧号。</summary>
    public static int Side(string text, string parameter = "side")
    {
        var t = text.Trim().ToUpperInvariant().Replace("＋", "+").Replace("－", "-").Replace("—", "-");
        var sign = t.StartsWith('-') || t.EndsWith('-') ? -1 : 1;
        var letter = t.Trim('+', '-');
        var axis = letter switch { "X" => 0, "Y" => 1, "Z" => 2, _ => -1 };
        return axis < 0
            ? throw new QuickArgumentException($"{parameter}={text}：写 +X / -X / +Y / -Y / +Z / -Z 之一（零件坐标）。")
            : axis * 2 + (sign > 0 ? 1 : 0);
    }

    /// <summary>六面全跟包围盒。</summary>
    public static EnvelopeSide[] Default() => Enumerable.Range(0, 6).Select(side => new EnvelopeSide(side, EnvelopeSource.Box, -1, 0, false, 0)).ToArray();

    /// <summary>属性文本 → 六面；空、缺的面按包围盒。写坏的抛参数错（带原文，好让人去属性里改）。</summary>
    public static EnvelopeSide[] Parse(string? text)
    {
        var sides = Default();
        if (string.IsNullOrWhiteSpace(text))
            return sides;
        foreach (var item in text.Split([';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = item.IndexOf(':');
            if (colon <= 0)
                throw Bad(text, item);
            var side = Side(item[..colon], PropertyName);
            var body = item[(colon + 1)..].Trim();
            if (body == "盒")
                continue;
            if (body.StartsWith('='))
            {
                sides[side] = new EnvelopeSide(side, EnvelopeSource.Manual, -1, 0, false, Number(body[1..], text, item));
                continue;
            }

            if (!body.StartsWith('F') && !body.StartsWith('f'))
                throw Bad(text, item);
            var at = body.IndexOf('@');
            var value = at >= 0 ? Number(body[(at + 1)..], text, item) : 0;
            var head = at >= 0 ? body[1..at] : body[1..];
            Vec3? tilt = null;
            var caret = head.IndexOf('^');
            if (caret >= 0)
            {
                var parts = head[(caret + 1)..].Split(',');
                if (parts.Length != 3)
                    throw Bad(text, item);
                tilt = new Vec3(Number(parts[0], text, item), Number(parts[1], text, item), Number(parts[2], text, item)).Normalized();
                head = head[..caret];
            }

            var axis = head.EndsWith("/轴", StringComparison.Ordinal);
            if (axis)
                head = head[..^2];
            var split = head.IndexOfAny(['+', '-'], 1);
            var face = int.TryParse(split < 0 ? head : head[..split], NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : throw Bad(text, item);
            var offset = split < 0 ? 0 : Number(head[split..], text, item);
            sides[side] = new EnvelopeSide(side, EnvelopeSource.Face, face, offset, axis, value, tilt);
        }

        return sides;
    }

    /// <summary>六面 → 属性文本（六面都写，好在属性里一眼看全）。</summary>
    public static string Format(IReadOnlyList<EnvelopeSide> sides)
        => string.Join(";", sides.OrderBy(side => side.Side).Select(side => $"{SideNames[side.Side]}:{Body(side)}"));

    private static string Body(EnvelopeSide side) => side.Source switch
    {
        EnvelopeSource.Manual => "=" + Num(side.Value),
        EnvelopeSource.Face => $"F{side.Face}{(side.Offset != 0 ? (side.Offset > 0 ? "+" : string.Empty) + Num(side.Offset) : string.Empty)}{(side.Axis ? "/轴" : string.Empty)}"
                               + (side.Tilt is { } n ? $"^{Six(n.X)},{Six(n.Y)},{Six(n.Z)}" : string.Empty) + $"@{Num(side.Value)}",
        _ => "盒",
    };

    /// <summary>这一侧在回执里怎么说：「F12 平面」「F7 轴线 +1.5」「手填」「包围盒」。</summary>
    public static string Describe(EnvelopeSide side) => side.Source switch
    {
        EnvelopeSource.Manual => "手填",
        EnvelopeSource.Face => $"F{side.Face}{(side.Axis ? " 轴线" : string.Empty)}{(side.Tilt is { } n ? $" 斜 {Num(TiltDegrees(n, side.Side))}°" : string.Empty)}{(side.Offset != 0 ? $" 偏 {(side.Offset > 0 ? "+" : string.Empty)}{Num(side.Offset)}" : string.Empty)}",
        _ => "包围盒",
    };

    /// <summary>斜面法向和这一侧的夹角（度）。</summary>
    public static double TiltDegrees(Vec3 normal, int side) => Math.Acos(Math.Clamp(Math.Abs(Component(normal.Normalized(), AxisOf(side))), 0, 1)) * 180 / Math.PI;

    /// <summary>平面斜着就给朝外法向（零件坐标），正的给 null。</summary>
    public static Vec3? Tilt(EnvelopeFaceGeometry face, int side)
        => face.Plane && Math.Abs(Component(face.Direction.Normalized(), AxisOf(side))) < Parallel ? face.Direction.Normalized() : null;

    /// <summary>
    /// 这个面能定哪一侧：平面按法向最靠的那根轴定（斜 45° 以内都行，<paramref name="side"/> 省略时按法向朝向定侧），圆柱轴线要垂直 <paramref name="side"/>（圆柱必须给侧）。
    /// 返回侧号，不合格抛可纠正的错（说清楚面是什么样的）。
    /// </summary>
    public static int Check(EnvelopeFaceGeometry face, int? side, int index)
    {
        if (face.Plane)
        {
            var d = face.Direction.Normalized();
            var axis = Enumerable.Range(0, 3).OrderByDescending(a => Math.Abs(Component(d, a))).First();
            var c = Component(d, axis);
            if (Math.Abs(c) < MaxTilt)
                throw new QuickCommandException($"F{index} 斜得太多（法向 {Vector(d)}，离最近的轴超过 45°），包络认不出它定哪一侧。");
            var natural = axis * 2 + (c > 0 ? 1 : 0);
            if (side is { } wanted && AxisOf(wanted) != axis)
                throw new QuickCommandException($"F{index} 是法向 {SideNames[natural]} 的平面，定不了 {SideNames[wanted]}（只能定 {SideNames[axis * 2]} / {SideNames[axis * 2 + 1]}）。");
            return side ?? natural;
        }

        if (side is not { } given)
            throw new QuickArgumentException($"F{index} 是圆柱面，要写 side=（如 -Z）说它定哪一侧。");
        if (Math.Abs(Component(face.Direction.Normalized(), AxisOf(given))) > Perpendicular)
            throw new QuickCommandException($"F{index} 圆柱轴线 {Vector(face.Direction.Normalized())} 不垂直于 {SideNames[given]}，定不了这一侧（轴线垂直于这一侧的圆柱才能取切线 / 轴线）。");
        return given;
    }

    /// <summary>
    /// 面在这一侧的位置（零件 mm，未加偏移）：平面取面上的坐标；圆柱默认孔取轴线、轴取外侧切线，<paramref name="axis"/> 强制取轴线、为假强制取切线。
    /// </summary>
    public static (double Value, bool Axis) Locate(EnvelopeFaceGeometry face, int side, bool? axis, Vec3? boxCenter = null)
    {
        var k = AxisOf(side);
        if (face.Plane)
        {
            // 斜面：取它和「过包围盒中心、沿这根轴的线」的交点（包围盒这一面中心那条线上的位置）。
            var n = face.Direction.Normalized();
            if (Math.Abs(Component(n, k)) >= Parallel || boxCenter is not { } c)
                return (Component(face.Point, k), false);
            var others = Enumerable.Range(0, 3).Where(a => a != k).Sum(a => Component(n, a) * (Component(c, a) - Component(face.Point, a)));
            return (Component(face.Point, k) - others / Component(n, k), false);
        }
        var useAxis = axis ?? face.Hole;
        var center = Component(face.Point, k);
        return (useAxis ? center : center + SignOf(side) * face.Radius, useAxis);
    }

    /// <summary>加上往外的偏移。</summary>
    public static double Outward(int side, double value, double offset) => value + SignOf(side) * offset;

    /// <summary>
    /// 合成盒（零件 mm）：没改的面取包围盒，改过的取 <paramref name="values"/>。某根轴两侧交叉（min ≥ max）返回出错的轴名。
    /// </summary>
    public static (Vec3 Min, Vec3 Max, string? Crossed) Compose((Vec3 Min, Vec3 Max) box, IReadOnlyDictionary<int, double> values)
    {
        double Get(int side) => values.TryGetValue(side, out var v) ? v : Component(side % 2 == 0 ? box.Min : box.Max, AxisOf(side));
        var min = new Vec3(Get(0), Get(2), Get(4));
        var max = new Vec3(Get(1), Get(3), Get(5));
        string? crossed = null;
        for (var axis = 0; axis < 3; axis++)
        {
            if (Component(min, axis) >= Component(max, axis))
                crossed = "XYZ"[axis].ToString();
        }

        return (min, max, crossed);
    }

    /// <summary>
    /// 贴面摆位（1.42.0，用户定：角码照包围盒摆悬空 1.95 mm，要让包络面直接贴到目标面）：
    /// 零件三根轴 <paramref name="axes"/>（行）、原点 <paramref name="origin"/>（总装 mm）下，把盒的 <paramref name="side"/> 面贴到目标平面
    /// （面上一点 <paramref name="target"/>、朝外法向 <paramref name="normal"/>，总装坐标），隔 <paramref name="gap"/>（正 = 离开）。
    /// 只沿目标法向平移，别的方向不动；这一面朝外方向要和目标法向相反，否则抛可纠正的错（先用 axes= 转对）。返回新原点与挪了多少。
    /// </summary>
    public static ((double X, double Y, double Z) Origin, double Moved) Snap(double[,] axes, (double X, double Y, double Z) origin, (Vec3 Min, Vec3 Max) box, int side, Vec3 target, Vec3 normal, double gap)
    {
        var rows = new[] { new Vec3(axes[0, 0], axes[0, 1], axes[0, 2]), new Vec3(axes[1, 0], axes[1, 1], axes[1, 2]), new Vec3(axes[2, 0], axes[2, 1], axes[2, 2]) };
        var k = AxisOf(side);
        var outward = rows[k] * SignOf(side);
        var n = normal.Normalized();
        if (outward.Dot(n) > -Parallel)
        {
            var facing = Enumerable.Range(0, 6).Where(s => (rows[AxisOf(s)] * SignOf(s)).Dot(n) <= -Parallel).DefaultIfEmpty(-1).First();
            throw new QuickCommandException($"包络 {SideNames[side]} 面朝向 {Vector(outward)}，和目标面法向 {Vector(n)} 不是正对着，贴不上"
                                            + (facing >= 0 ? $"（现在正对目标的是 {SideNames[facing]}，要贴 {SideNames[side]} 先用 axes= 转）。" : "（先用 axes= 转对）。"));
        }

        // 这一面上任取一点（面中心），在总装里的位置。
        var center = (box.Min + box.Max) * 0.5;
        var local = k switch
        {
            0 => center with { X = side % 2 == 0 ? box.Min.X : box.Max.X },
            1 => center with { Y = side % 2 == 0 ? box.Min.Y : box.Max.Y },
            _ => center with { Z = side % 2 == 0 ? box.Min.Z : box.Max.Z },
        };
        var o = new Vec3(origin.X, origin.Y, origin.Z);
        var world = rows[0] * local.X + rows[1] * local.Y + rows[2] * local.Z + o;
        var moved = gap - (world - target).Dot(n);
        var next = o + n * moved;
        return ((next.X, next.Y, next.Z), moved);
    }

    /// <summary>
    /// 盒差改正（1.42.0，用户定的算法）：零件是按包围盒摆的，包络又是从包围盒改出来的，所以每一侧「包围盒面 → 包络面」差多少，
    /// 就把零件沿这一侧的零件轴反向挪多少，包络面正好落到原来包围盒面的位置；姿态不管，直接用零件三根轴换到总装。
    /// 返回总装 mm 的平移。同一根轴两侧都改过只能取一侧（参数层已拦）。不能重复做：做完再做会再挪一次。
    /// </summary>
    public static Vec3 BoxDelta(double[,] axes, (Vec3 Min, Vec3 Max) partBox, (Vec3 Min, Vec3 Max) envelope, IEnumerable<int> sides)
    {
        var rows = new[] { new Vec3(axes[0, 0], axes[0, 1], axes[0, 2]), new Vec3(axes[1, 0], axes[1, 1], axes[1, 2]), new Vec3(axes[2, 0], axes[2, 1], axes[2, 2]) };
        var delta = new Vec3(0, 0, 0);
        foreach (var side in sides)
        {
            var k = AxisOf(side);
            var b = Component(side % 2 == 0 ? partBox.Min : partBox.Max, k);
            var e = Component(side % 2 == 0 ? envelope.Min : envelope.Max, k);
            delta += rows[k] * (b - e);
        }

        return delta;
    }

    /// <summary>
    /// 带旋转的盒差（用户定的算法扩到斜面）：每个装配面 <paramref name="planes"/>（零件坐标：侧、朝外法向、面上一点）都要落到包围盒同一侧那个面的位置。
    /// 先求转动 Q 把装配面法向转成包围盒面法向——一个斜面取最小转角（绕它自己法向不转），两面以上用前两个不平行的定全姿态，
    /// 两面夹角和包围盒对应夹角差超过 <see cref="AngleTolerance"/>° 就拒绝（转不回去，两面不能同时贴上）；
    /// 再求平移：贴的那几根轴让面落到包围盒面上，别的轴让包围盒中心不动。零件三根轴、原点按「先 Q 再平移」换到总装。
    /// 全是正的面时 Q = 单位阵，结果等于逐侧平移（包围盒 − 包络）。只能做一次。
    /// </summary>
    public static (double[,] Axes, (double X, double Y, double Z) Origin, double Degrees) Correction(double[,] axes, (double X, double Y, double Z) origin, (Vec3 Min, Vec3 Max) partBox, IReadOnlyList<(int Side, Vec3 Normal, Vec3 Point)> planes)
    {
        var rows = new[] { new Vec3(axes[0, 0], axes[0, 1], axes[0, 2]), new Vec3(axes[1, 0], axes[1, 1], axes[1, 2]), new Vec3(axes[2, 0], axes[2, 1], axes[2, 2]) };
        var a = planes.Select(p => p.Normal.Normalized()).ToList();
        var b = planes.Select(p => Unit(p.Side)).ToList();
        var q = Identity();
        if (a.Zip(b).Any(pair => pair.First.Dot(pair.Second) < 1 - 1e-12))
        {
            if (planes.Count == 1)
            {
                q = Turn(a[0], b[0]);
            }
            else
            {
                var j = Enumerable.Range(1, planes.Count - 1).First(i => a[0].Cross(a[i]).Length > 1e-6);
                var real = Degrees(a[0], a[j]);
                var box = Degrees(b[0], b[j]);
                if (Math.Abs(real - box) > AngleTolerance)
                    throw new QuickCommandException($"装配面 {SideNames[planes[0].Side]} 和 {SideNames[planes[j].Side]} 夹角 {Num(real)}°，包围盒对应两面是 {Num(box)}°，差 {Num(Math.Abs(real - box))}°：零件转不到两面同时贴上，只改一面或分开处理。");
                q = Multiply(Frame(b[0], b[j]), Transpose(Frame(a[0], a[j])));
                for (var i = 0; i < planes.Count; i++)
                {
                    var off = Degrees(Apply(q, a[i]), b[i]);
                    if (off > AngleTolerance)
                        throw new QuickCommandException($"装配面 {SideNames[planes[i].Side]} 转完还差 {Num(off)}°：几个装配面之间的夹角和包围盒对不上，少选几面。");
                }
            }
        }

        var center = (partBox.Min + partBox.Max) * 0.5;
        var t = new double[3];
        var turnedCenter = Apply(q, center);
        for (var k = 0; k < 3; k++)
            t[k] = Component(center, k) - Component(turnedCenter, k);
        foreach (var plane in planes)
        {
            var k = AxisOf(plane.Side);
            var boxValue = Component(plane.Side % 2 == 0 ? partBox.Min : partBox.Max, k);
            // 转过以后装配面上那一点落在这根轴上的位置，要等于包围盒这一面。
            t[k] = boxValue - Component(Apply(q, plane.Point), k);
        }

        var next = new double[3, 3];
        for (var i = 0; i < 3; i++)
        {
            var column = rows[0] * q[0, i] + rows[1] * q[1, i] + rows[2] * q[2, i];
            next[i, 0] = column.X;
            next[i, 1] = column.Y;
            next[i, 2] = column.Z;
        }

        var shift = rows[0] * t[0] + rows[1] * t[1] + rows[2] * t[2];
        var trace = q[0, 0] + q[1, 1] + q[2, 2];
        var degrees = Math.Acos(Math.Clamp((trace - 1) / 2, -1, 1)) * 180 / Math.PI;
        return (next, (origin.X + shift.X, origin.Y + shift.Y, origin.Z + shift.Z), degrees);
    }

    /// <summary>侧的朝外单位向量（零件坐标）。</summary>
    public static Vec3 Unit(int side) => AxisOf(side) switch
    {
        0 => new Vec3(SignOf(side), 0, 0),
        1 => new Vec3(0, SignOf(side), 0),
        _ => new Vec3(0, 0, SignOf(side)),
    };

    private static double Degrees(Vec3 a, Vec3 b) => Math.Acos(Math.Clamp(a.Normalized().Dot(b.Normalized()), -1, 1)) * 180 / Math.PI;

    private static double[,] Identity() => new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

    private static Vec3 Apply(double[,] m, Vec3 v) => new(m[0, 0] * v.X + m[0, 1] * v.Y + m[0, 2] * v.Z, m[1, 0] * v.X + m[1, 1] * v.Y + m[1, 2] * v.Z, m[2, 0] * v.X + m[2, 1] * v.Y + m[2, 2] * v.Z);

    /// <summary>把单位向量 <paramref name="from"/> 转到 <paramref name="to"/> 的最小转动（罗德里格斯）。</summary>
    private static double[,] Turn(Vec3 from, Vec3 to)
    {
        var v = from.Cross(to);
        var s2 = v.Dot(v);
        var c = from.Dot(to);
        var k = (1 - c) / s2;
        double[,] x = { { 0, -v.Z, v.Y }, { v.Z, 0, -v.X }, { -v.Y, v.X, 0 } };
        var m = Identity();
        var x2 = Multiply(x, x);
        for (var i = 0; i < 3; i++)
        for (var j = 0; j < 3; j++)
            m[i, j] += x[i, j] + x2[i, j] * k;
        return m;
    }

    /// <summary>两个不平行的方向定一组正交基（列）：第一列 = <paramref name="first"/>。</summary>
    private static double[,] Frame(Vec3 first, Vec3 second)
    {
        var e1 = first.Normalized();
        var e2 = e1.Cross(second).Normalized();
        var e3 = e1.Cross(e2);
        return new double[3, 3] { { e1.X, e2.X, e3.X }, { e1.Y, e2.Y, e3.Y }, { e1.Z, e2.Z, e3.Z } };
    }

    private static double[,] Transpose(double[,] m)
    {
        var r = new double[3, 3];
        for (var i = 0; i < 3; i++)
        for (var j = 0; j < 3; j++)
            r[i, j] = m[j, i];
        return r;
    }

    private static double[,] Multiply(double[,] a, double[,] b)
    {
        var r = new double[3, 3];
        for (var i = 0; i < 3; i++)
        for (var j = 0; j < 3; j++)
            r[i, j] = a[i, 0] * b[0, j] + a[i, 1] * b[1, j] + a[i, 2] * b[2, j];
        return r;
    }

    /// <summary>
    /// 批量贴面自动找目标（1.42.0，用户定：同一种零件装配关系类似，一起贴）：盒的 <paramref name="side"/> 面前后 <paramref name="near"/> mm 内、
    /// 法向正对、在这一面上的投影和它有重叠的平面里，取离得最近的（一样近取重叠面积大的）。返回目标与距离（正 = 还差这么多，负 = 已压进去），没有为 null。
    /// </summary>
    public static (ContactFace Face, double Distance)? Nearest(double[,] axes, (double X, double Y, double Z) origin, (Vec3 Min, Vec3 Max) box, int side, IEnumerable<ContactFace> candidates, double near)
    {
        var rows = new[] { new Vec3(axes[0, 0], axes[0, 1], axes[0, 2]), new Vec3(axes[1, 0], axes[1, 1], axes[1, 2]), new Vec3(axes[2, 0], axes[2, 1], axes[2, 2]) };
        var k = AxisOf(side);
        var d = rows[k] * SignOf(side);
        var o = new Vec3(origin.X, origin.Y, origin.Z);
        var plane = Component(side % 2 == 0 ? box.Min : box.Max, k) * SignOf(side) + o.Dot(d);
        var others = Enumerable.Range(0, 3).Where(axis => axis != k).ToArray();
        (ContactFace Face, double Distance, double Area)? best = null;
        foreach (var face in candidates)
        {
            if (face.Surface != ContactSurface.Plane || face.Normal.Normalized().Dot(d) > -Parallel || face.Triangles.Count == 0)
                continue;
            var distance = face.Point.Dot(d) - plane;
            if (Math.Abs(distance) > near)
                continue;
            var area = 1.0;
            foreach (var axis in others)
            {
                var u = rows[axis];
                var lo = Component(box.Min, axis) + o.Dot(u);
                var hi = Component(box.Max, axis) + o.Dot(u);
                var overlap = Math.Min(hi, face.Triangles.Max(p => p.Dot(u))) - Math.Max(lo, face.Triangles.Min(p => p.Dot(u)));
                area *= Math.Max(0, overlap);
            }

            if (area <= 0.01)
                continue;
            if (best is not { } b || Math.Abs(distance) < Math.Abs(b.Distance) - 1e-6 || (Math.Abs(Math.Abs(distance) - Math.Abs(b.Distance)) <= 1e-6 && area > b.Area))
                best = (face, distance, area);
        }

        return best is { } found ? (found.Face, found.Distance) : null;
    }

    /// <summary>零件盒按零部件变换（三根轴行向量、原点 mm）换到总装，取轴对齐外接盒。</summary>
    public static (Vec3 Min, Vec3 Max) ToAssembly((Vec3 Min, Vec3 Max) box, double[,] axes, (double X, double Y, double Z) origin)
    {
        var rows = new[] { new Vec3(axes[0, 0], axes[0, 1], axes[0, 2]), new Vec3(axes[1, 0], axes[1, 1], axes[1, 2]), new Vec3(axes[2, 0], axes[2, 1], axes[2, 2]) };
        var o = new Vec3(origin.X, origin.Y, origin.Z);
        var points = new List<Vec3>(8);
        foreach (var x in new[] { box.Min.X, box.Max.X })
        foreach (var y in new[] { box.Min.Y, box.Max.Y })
        foreach (var z in new[] { box.Min.Z, box.Max.Z })
            points.Add(rows[0] * x + rows[1] * y + rows[2] * z + o);
        return (new Vec3(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)), new Vec3(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z)));
    }

    /// <summary>盒写成 minX,minY,minZ,maxX,maxY,maxZ（同 asm.list 的 box 顺序；保留 3 位——真机角码 146.95 按一位小数写成 147，正好把 1.95 mm 悬空藏掉）。</summary>
    public static string BoxText((Vec3 Min, Vec3 Max) box)
        => string.Join(",", new[] { box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z }.Select(Num));

    /// <summary>盒的 12 条棱（画线框用）。</summary>
    public static IReadOnlyList<(Vec3 A, Vec3 B)> Edges((Vec3 Min, Vec3 Max) box)
    {
        Vec3 P(int i) => new((i & 1) == 0 ? box.Min.X : box.Max.X, (i & 2) == 0 ? box.Min.Y : box.Max.Y, (i & 4) == 0 ? box.Min.Z : box.Max.Z);
        var edges = new List<(Vec3, Vec3)>(12);
        for (var i = 0; i < 8; i++)
        {
            foreach (var bit in new[] { 1, 2, 4 })
            {
                if ((i & bit) == 0)
                    edges.Add((P(i), P(i | bit)));
            }
        }

        return edges;
    }

    private static string Six(double value) => Math.Round(value, 6).ToString("0.######", CultureInfo.InvariantCulture);

    public static string Num(double value)
    {
        var text = Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
        return text == "-0" ? "0" : text;
    }

    private static string Vector(Vec3 v) => $"({ModelSpec.Format(v.X, "0.###")},{ModelSpec.Format(v.Y, "0.###")},{ModelSpec.Format(v.Z, "0.###")})";

    private static double Number(string text, string whole, string item)
        => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : throw Bad(whole, item);

    private static QuickCommandException Bad(string whole, string item)
        => new($"零件属性「{PropertyName}」写坏了：「{item}」看不懂（全文 {whole}）。用 strenua.asm.envelope reset=是 重建，或手改成 -X:盒;+Y:F3+1.5@40;+Z:=120 这种写法。");
}
