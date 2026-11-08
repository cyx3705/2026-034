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
/// </summary>
internal sealed record EnvelopeSide(int Side, EnvelopeSource Source, int Face, double Offset, bool Axis, double Value);

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
/// 只认得「平面法向平行这一侧」和「圆柱轴线垂直这一侧」两种目标面，别的拒绝不猜（我定：斜面、球面不做）。
/// </remarks>
internal static class EnvelopeSpec
{
    /// <summary>零件自定义属性名。</summary>
    public const string PropertyName = "Strenua包络";

    public static readonly string[] SideNames = ["-X", "+X", "-Y", "+Y", "-Z", "+Z"];

    /// <summary>平面算正对的余弦下限、圆柱算垂直的余弦上限。</summary>
    private const double Parallel = 0.999, Perpendicular = 0.001;

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
            var axis = head.EndsWith("/轴", StringComparison.Ordinal);
            if (axis)
                head = head[..^2];
            var split = head.IndexOfAny(['+', '-'], 1);
            var face = int.TryParse(split < 0 ? head : head[..split], NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : throw Bad(text, item);
            var offset = split < 0 ? 0 : Number(head[split..], text, item);
            sides[side] = new EnvelopeSide(side, EnvelopeSource.Face, face, offset, axis, value);
        }

        return sides;
    }

    /// <summary>六面 → 属性文本（六面都写，好在属性里一眼看全）。</summary>
    public static string Format(IReadOnlyList<EnvelopeSide> sides)
        => string.Join(";", sides.OrderBy(side => side.Side).Select(side => $"{SideNames[side.Side]}:{Body(side)}"));

    private static string Body(EnvelopeSide side) => side.Source switch
    {
        EnvelopeSource.Manual => "=" + Num(side.Value),
        EnvelopeSource.Face => $"F{side.Face}{(side.Offset != 0 ? (side.Offset > 0 ? "+" : string.Empty) + Num(side.Offset) : string.Empty)}{(side.Axis ? "/轴" : string.Empty)}@{Num(side.Value)}",
        _ => "盒",
    };

    /// <summary>这一侧在回执里怎么说：「F12 平面」「F7 轴线 +1.5」「手填」「包围盒」。</summary>
    public static string Describe(EnvelopeSide side) => side.Source switch
    {
        EnvelopeSource.Manual => "手填",
        EnvelopeSource.Face => $"F{side.Face}{(side.Axis ? " 轴线" : string.Empty)}{(side.Offset != 0 ? $" 偏 {(side.Offset > 0 ? "+" : string.Empty)}{Num(side.Offset)}" : string.Empty)}",
        _ => "包围盒",
    };

    /// <summary>
    /// 这个面能定哪一侧：平面法向要平行某根轴（<paramref name="side"/> 省略时按法向朝向定侧），圆柱轴线要垂直 <paramref name="side"/>（圆柱必须给侧）。
    /// 返回侧号，不合格抛可纠正的错（说清楚面是什么样的）。
    /// </summary>
    public static int Check(EnvelopeFaceGeometry face, int? side, int index)
    {
        if (face.Plane)
        {
            var d = face.Direction.Normalized();
            for (var axis = 0; axis < 3; axis++)
            {
                var c = Component(d, axis);
                if (Math.Abs(c) < Parallel)
                    continue;
                var natural = axis * 2 + (c > 0 ? 1 : 0);
                if (side is { } wanted && AxisOf(wanted) != axis)
                    throw new QuickCommandException($"F{index} 是法向 {SideNames[natural]} 的平面，定不了 {SideNames[wanted]}（只能定 {SideNames[axis * 2]} / {SideNames[axis * 2 + 1]}）。");
                return side ?? natural;
            }

            throw new QuickCommandException($"F{index} 是斜面（法向 {Vector(d)}），包络只认法向平行零件 X/Y/Z 轴的平面。");
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
    public static (double Value, bool Axis) Locate(EnvelopeFaceGeometry face, int side, bool? axis)
    {
        var k = AxisOf(side);
        if (face.Plane)
            return (Component(face.Point, k), false);
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
