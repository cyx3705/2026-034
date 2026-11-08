using System.Globalization;

namespace HistoryStrenua;

/// <summary>螺钉头怎么落：圆柱头一类（能进沉孔）、沉头（只进锥形沉头孔）、低头（半圆头 / 盘头，只贴表面）。</summary>
internal enum ScrewHead
{
    Cap,
    Countersunk,
    Low,
}

/// <summary>过孔最外那个零件的孔口：平面、柱形沉孔、锥形沉头孔。</summary>
internal enum ScrewSeat
{
    Surface,
    Counterbore,
    Countersink,
}

/// <summary>
/// 一键螺丝（1.44.0）可选的螺钉：蚂蚁工场「紧固件 › 螺钉」栏里头部有承压面的 7 种（紧定螺钉没有头，不在内）。
/// </summary>
/// <remarks>
/// 模型约定是真机下载核对的：原点都在头顶中心，但轴向各家不一样（圆柱头 / 沉头 / 半圆头 / 盘头沿 −Z，十字沉头与两种组合螺丝沿 +X），
/// 所以摆位不按约定，按下载件的几何现量（<see cref="ScrewProfile"/>）。组合螺丝的垫圈是另外的实体、没压平，承压面取垫圈最低处。
/// </remarks>
internal sealed record ScrewKind(string Name, long Product, ScrewHead Head, bool Cross, double WasherEstimate, params string[] Aliases)
{
    public static readonly ScrewKind Cap = new("内六角圆柱头", 302177, ScrewHead.Cap, false, 0, "圆柱头", "杯头", "内六角", "内六角圆柱头螺钉", "SSC");
    public static readonly ScrewKind Spring = new("弹垫组合", 302162, ScrewHead.Cap, false, 0.52, "带弹垫", "弹垫", "内六角圆柱头带弹垫组合螺丝", "SZA");
    public static readonly ScrewKind FlatSpring = new("平弹垫组合", 302165, ScrewHead.Cap, false, 0.88, "带平弹垫", "平弹垫", "内六角圆柱头带平弹垫组合螺丝", "SZC");
    public static readonly ScrewKind Countersunk = new("内六角沉头", 302188, ScrewHead.Countersunk, false, 0, "沉头", "内六角沉头螺钉", "SSK");
    public static readonly ScrewKind Button = new("内六角半圆头", 302184, ScrewHead.Low, false, 0, "半圆头", "内六角半圆头螺钉", "SSB");
    public static readonly ScrewKind Pan = new("十字盘头", 302169, ScrewHead.Low, true, 0, "盘头", "十字槽盘头", "十字槽盘头机螺钉", "SSP");
    public static readonly ScrewKind CrossCountersunk = new("十字沉头", 302170, ScrewHead.Countersunk, true, 0, "十字槽沉头", "十字槽沉头机螺钉", "SST");

    public static IReadOnlyList<ScrewKind> All { get; } = [Cap, Spring, FlatSpring, Countersunk, Button, Pan, CrossCountersunk];

    public static string Names => string.Join(" / ", All.Select(kind => kind.Name));

    public static ScrewKind Parse(string text)
    {
        var key = text.Trim();
        return All.FirstOrDefault(kind => kind.Name == key)
               ?? All.FirstOrDefault(kind => kind.Aliases.Any(alias => string.Equals(alias, key, StringComparison.OrdinalIgnoreCase)))
               ?? throw new QuickArgumentException($"不认识的螺钉「{text}」，可选：{Names}。");
    }
}

/// <summary>一种螺钉一个规格下载件现量出来的尺寸（零件坐标 mm，轴向从头顶量起）。</summary>
/// <param name="Axis">零件里从头顶指向螺尾的方向。</param>
/// <param name="Top">头顶中心（零件坐标）。</param>
/// <param name="Length">这个件的公称长度 L。</param>
/// <param name="Tip">头顶到螺尾。</param>
/// <param name="Bearing">头顶到承压面（头下 / 垫圈最低处）。</param>
/// <param name="HeadRadius">头部最大半径（沉头孔算下沉量用）。</param>
/// <param name="BearingRadius">承压面最大半径（含垫圈，判进不进得了沉孔）。</param>
internal sealed record ScrewProfile(Vec3 Axis, Vec3 Top, double Length, double Tip, double Bearing, double HeadRadius, double BearingRadius)
{
    /// <summary>公称长度换成 <paramref name="length"/> 时头顶到螺尾（同一种同一规格只有杆长变）。</summary>
    public double TipFor(double length) => Tip + length - Length;
}

/// <summary>螺纹孔轴线上一个别的零件的孔面（总装 mm），轴向坐标从螺纹孔口量、朝外为正。</summary>
internal sealed record AxialFace(string Owner, bool Cone, bool Hole, double From, double To, double MinRadius, double MaxRadius);

/// <summary>零件上一张圆柱 / 圆锥面（总装 mm）：轴线方向与轴上一点、圆柱半径（圆锥为 0）、孔壁还是杆、三角面片顶点。</summary>
internal sealed record RoundFace(string Owner, bool Cone, Vec3 Axis, Vec3 Point, double Radius, bool Hole, IReadOnlyList<Vec3> Points);

/// <summary>螺纹孔往外穿过的那叠零件：最外那个零件、孔口种类、承压面位置（沉头孔是孔口平面）、沉孔 / 锥孔尺寸。</summary>
internal sealed record ScrewStack(IReadOnlyList<string> Owners, ScrewSeat Seat, double SeatAt, double SeatRadius, double ConeHalfAngle);

/// <summary>螺纹孔（总装 mm）：所属零部件、特征名、孔口点、朝外方向、公称直径、可用螺纹长、是否贯穿；<c>Drilled</c> = 按底孔认的（外购件没有异形孔特征，Ø 等于标准攻丝底孔就当螺纹孔）。</summary>
internal sealed record TappedHole(string Owner, string Feature, Vec3 Entry, Vec3 Outward, double Diameter, double Thread, bool Through, bool Drilled = false)
{
    public string Size => "M" + ModelSpec.Format(Diameter);
}

/// <summary>
/// 一键螺丝的纯规则（1.44.0，用户定）：一种类型装全部 / 按规格分类型 / 按沉头形状自动选类型；长度算出来（不暴露）；范围 = 全部 / 子装配 / 零件。
/// </summary>
internal static class ScrewSpec
{
    /// <summary>同轴判定：轴线间距 mm 以内。</summary>
    public const double Coaxial = 0.1;

    /// <summary>上下零件之间、过孔与螺纹孔口之间允许的空隙 mm（再大就不认是一叠）。</summary>
    public const double StackGap = 0.5;

    /// <summary>过孔半径上限（公称直径的倍数）；更大的同轴圆柱是沉孔。</summary>
    public const double ClearanceRatio = 0.75;

    /// <summary>粗牙标准攻丝底孔直径（GB/T 20330 / 常用钻头）：公称直径 → 底孔。</summary>
    public static readonly IReadOnlyDictionary<double, double> TapDrills = new Dictionary<double, double>
    {
        [2] = 1.6, [2.5] = 2.05, [3] = 2.5, [4] = 3.3, [5] = 4.2, [6] = 5.0, [8] = 6.8, [10] = 8.5, [12] = 10.2, [14] = 12, [16] = 14, [20] = 17.5, [24] = 21,
    };

    /// <summary>孔径是不是某个规格的攻丝底孔（差 0.05 内），是就回公称直径。</summary>
    public static double? TapSize(double holeDiameter)
        => TapDrills.Where(pair => Math.Abs(pair.Value - holeDiameter) <= 0.05).Select(pair => (double?)pair.Key).FirstOrDefault();

    /// <summary>
    /// 把一张面换成孔轴坐标（从 <paramref name="hole"/> 孔口量、朝外为正）：不同轴（方向差、轴线偏 &gt; <see cref="Coaxial"/>）回 null。
    /// </summary>
    public static AxialFace? Axial(RoundFace face, TappedHole hole)
    {
        if (Math.Abs(face.Axis.Dot(hole.Outward)) < 0.9999 || Offset(face.Point, hole) > Coaxial || face.Points.Count == 0)
            return null;
        double from = double.MaxValue, to = double.MinValue, rMin = double.MaxValue, rMax = 0;
        foreach (var point in face.Points)
        {
            var v = point - hole.Entry;
            var along = v.Dot(hole.Outward);
            var radius = (v - hole.Outward * along).Length;
            from = Math.Min(from, along);
            to = Math.Max(to, along);
            rMin = Math.Min(rMin, radius);
            rMax = Math.Max(rMax, radius);
        }

        return face.Cone
            ? new AxialFace(face.Owner, true, face.Hole, from, to, rMin, rMax)
            : new AxialFace(face.Owner, false, face.Hole, from, to, face.Radius, face.Radius);
    }

    /// <summary>点到孔轴线的距离。</summary>
    public static double Offset(Vec3 point, TappedHole hole)
    {
        var v = point - hole.Entry;
        return (v - hole.Outward * v.Dot(hole.Outward)).Length;
    }

    /// <summary>
    /// 按底孔认螺纹孔：孔壁圆柱直径等于标准攻丝底孔的，同一根轴线上首尾相接（缝 ≤ 0.05）的连成一段，一段一个孔、两头都给；
    /// 可用螺纹 = min(段长, 2d)（外购件攻丝深度不知道，取常用的 2 倍公称直径）。<paramref name="skip"/> 排掉已有异形孔螺纹的轴线。
    /// </summary>
    public static IReadOnlyList<(TappedHole High, TappedHole Low, double Length)> DrilledHoles(IReadOnlyList<RoundFace> faces, Func<Vec3, Vec3, bool>? skip = null)
    {
        var holes = new List<(TappedHole, TappedHole, double)>();
        var drills = faces.Where(face => face.Hole && !face.Cone && TapSize(face.Radius * 2) is not null && face.Points.Count > 0).ToList();
        var used = new bool[drills.Count];
        for (var i = 0; i < drills.Count; i++)
        {
            if (used[i])
                continue;
            var seed = drills[i];
            var axis = seed.Axis.Normalized();
            var probe = new TappedHole(seed.Owner, string.Empty, seed.Point, axis, 0, 0, false);
            var spans = new List<(double From, double To)>();
            for (var j = i; j < drills.Count; j++)
            {
                if (used[j] || Math.Abs(drills[j].Radius - seed.Radius) > 0.01 || Axial(drills[j], probe) is not { } axial)
                    continue;
                used[j] = true;
                spans.Add((axial.From, axial.To));
            }

            if (skip?.Invoke(seed.Point, axis) == true)
                continue;
            var d = TapSize(seed.Radius * 2)!.Value;
            foreach (var (from, to) in Merge(spans))
            {
                var length = to - from;
                var usable = Math.Min(length, 2 * d);
                var name = $"底孔Ø{ModelSpec.Format(seed.Radius * 2)}";
                var high = seed.Point + axis * to;
                var low = seed.Point + axis * from;
                holes.Add((new TappedHole(seed.Owner, name, high, axis, d, usable, false, true), new TappedHole(seed.Owner, name, low, -axis, d, usable, false, true), length));
            }
        }

        return holes;
    }

    private static IEnumerable<(double From, double To)> Merge(IEnumerable<(double From, double To)> spans)
    {
        (double From, double To)? current = null;
        foreach (var span in spans.OrderBy(span => span.From))
        {
            if (current is { } open && span.From <= open.To + 0.05)
            {
                current = (open.From, Math.Max(open.To, span.To));
                continue;
            }

            if (current is { } done)
                yield return done;
            current = span;
        }

        if (current is { } last)
            yield return last;
    }

    /// <summary>「按规格」写法 M3=十字盘头,M8=弹垫组合。</summary>
    public static IReadOnlyDictionary<double, ScrewKind> Sizes(string? text)
    {
        var map = new Dictionary<double, ScrewKind>();
        foreach (var (key, value) in MymoooRules.Pairs(text, "sizes"))
        {
            var size = Diameter(key) ?? throw new QuickArgumentException($"sizes= 的规格要写 M6 这样：{key}");
            if (!map.TryAdd(size, ScrewKind.Parse(value)))
                throw new QuickArgumentException($"sizes= 里 {key} 写了两次。");
        }

        return map;
    }

    /// <summary>「M6」「6」→ 6；不是规格为 null。</summary>
    public static double? Diameter(string text)
    {
        var trimmed = text.Trim().TrimStart('M', 'm');
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
    }

    /// <summary>
    /// 按孔口定类型。<paramref name="auto"/>（默认开）：锥形沉头孔一律沉头（十字系选十字沉头）、柱形沉孔用圆柱头一类（选的不是就换内六角圆柱头）、
    /// 平面孔口用选的（选了沉头就换内六角圆柱头）。关掉就照选的装，装不上的跳过。
    /// </summary>
    public static (ScrewKind? Kind, string? Note) Choose(ScrewKind wanted, ScrewSeat seat, bool auto)
    {
        if (!auto)
        {
            return (seat, wanted.Head) switch
            {
                (ScrewSeat.Countersink, not ScrewHead.Countersunk) => (null, $"锥形沉头孔装不了{wanted.Name}（auto=否）"),
                (not ScrewSeat.Countersink, ScrewHead.Countersunk) => (null, $"{wanted.Name}只能装锥形沉头孔（auto=否）"),
                _ => (wanted, null),
            };
        }

        return seat switch
        {
            ScrewSeat.Countersink => (wanted.Head == ScrewHead.Countersunk ? wanted : wanted.Cross ? ScrewKind.CrossCountersunk : ScrewKind.Countersunk, null),
            ScrewSeat.Counterbore => wanted.Head == ScrewHead.Cap ? (wanted, null) : (ScrewKind.Cap, null),
            _ => wanted.Head == ScrewHead.Countersunk ? (ScrewKind.Cap, $"平面孔口装不了{wanted.Name}，换内六角圆柱头") : (wanted, null),
        };
    }

    /// <summary>
    /// 沿螺纹孔朝外认那一叠零件：过孔（半径 d/2 ~ 0.75d 的孔壁）从孔口起一个接一个（间隙 ≤ <see cref="StackGap"/>），最外那个零件定孔口。
    /// 失败回 null 和原因。
    /// </summary>
    public static (ScrewStack? Stack, string? Reason) Stack(IReadOnlyList<AxialFace> faces, double diameter)
    {
        var half = diameter / 2;
        var limit = diameter * ClearanceRatio;
        var parts = faces
            .Where(face => face.Hole && face.To > -StackGap)
            .GroupBy(face => face.Owner)
            .Select(group =>
            {
                var clearance = group.Where(face => !face.Cone && face.MinRadius >= half - 0.05 && face.MinRadius <= limit).ToList();
                return (Owner: group.Key, Faces: group.ToList(), Clearance: clearance);
            })
            .Where(part => part.Clearance.Count > 0)
            .Select(part => (part.Owner, part.Faces, From: part.Faces.Min(face => face.From), To: part.Faces.Max(face => face.To)))
            .OrderBy(part => part.From)
            .ToList();
        if (parts.Count == 0)
        {
            var narrow = faces.Where(face => face.Hole && !face.Cone && face.To > 0 && face.MinRadius < half - 0.05).Select(face => face.Owner).Distinct().ToList();
            return (null, narrow.Count > 0 ? $"对面 {string.Join("、", narrow)} 的同轴孔比 {MSize(diameter)} 还小" : "孔口外面没有对上的过孔");
        }

        if (parts[0].From > StackGap)
            return (null, $"过孔（{parts[0].Owner}）离螺纹孔口 {ModelSpec.Format(parts[0].From)} mm，没贴上");

        var chain = new List<(string Owner, List<AxialFace> Faces, double From, double To)> { parts[0] };
        foreach (var part in parts.Skip(1))
        {
            if (part.From > chain[^1].To + StackGap)
                break;
            chain.Add(part);
        }

        var last = chain[^1];
        var owners = chain.Select(part => part.Owner).ToList();
        var bore = last.Faces.Where(face => !face.Cone && face.MinRadius > limit && face.To >= last.To - 0.05).OrderBy(face => face.From).FirstOrDefault();
        if (bore is not null)
            return (new ScrewStack(owners, ScrewSeat.Counterbore, bore.From, bore.MinRadius, 0), null);
        var sink = last.Faces.Where(face => face.Cone && face.MaxRadius > limit && face.To >= last.To - 0.05).OrderByDescending(face => face.MaxRadius).FirstOrDefault();
        if (sink is not null)
        {
            var angle = Math.Atan2(sink.MaxRadius - sink.MinRadius, Math.Max(sink.To - sink.From, 1e-6));
            return (new ScrewStack(owners, ScrewSeat.Countersink, sink.To, sink.MaxRadius, angle), null);
        }

        return (new ScrewStack(owners, ScrewSeat.Surface, last.To, 0, 0), null);
    }

    /// <summary>
    /// 头顶在孔轴上的位置（从螺纹孔口量、朝外为正）：圆柱头 / 低头 = 承压面 + 头顶到承压面；沉头 = 孔口平面往下沉 (锥孔半径 − 头半径) / tan(半角)。
    /// </summary>
    public static double TopAt(ScrewStack stack, ScrewProfile profile, ScrewHead head)
        => head == ScrewHead.Countersunk && stack.Seat == ScrewSeat.Countersink
            ? stack.SeatAt - (stack.SeatRadius - profile.HeadRadius) / Math.Tan(Math.Max(stack.ConeHalfAngle, 0.1))
            : stack.SeatAt + profile.Bearing;

    /// <summary>旋入深度窗口：最少 min(d, 可用螺纹)，最多可用螺纹，最好 1.5d。</summary>
    public static (double Min, double Ideal, double Max) Engagement(TappedHole hole)
    {
        var max = hole.Thread;
        var min = Math.Min(hole.Diameter, max);
        return (min, Math.Min(1.5 * hole.Diameter, max), max);
    }

    /// <summary>
    /// 从商品的长度表里挑：旋入深度落在窗口里、离 1.5d 最近（一样近取长的）。
    /// <paramref name="engage"/> 给长度 L 算旋入深度。没有合适的为 null。
    /// </summary>
    public static double? Length(IReadOnlyList<double> lengths, Func<double, double> engage, (double Min, double Ideal, double Max) window)
    {
        const double eps = 0.01;
        return lengths
            .Where(length => engage(length) >= window.Min - eps && engage(length) <= window.Max + eps)
            .OrderBy(length => Math.Abs(engage(length) - window.Ideal))
            .ThenByDescending(length => length)
            .Select(length => (double?)length)
            .FirstOrDefault();
    }

    /// <summary>没挑到长度时说清楚差在哪。</summary>
    public static string NoLength(IReadOnlyList<double> lengths, Func<double, double> engage, (double Min, double Ideal, double Max) window)
    {
        if (lengths.Count == 0)
            return "商品没有这个规格";
        var shortest = engage(lengths.Min());
        var longest = engage(lengths.Max());
        return longest < window.Min
            ? $"最长 L{ModelSpec.Format(lengths.Max())} 也只旋入 {ModelSpec.Format(longest)} mm（至少要 {ModelSpec.Format(window.Min)}）：夹得太厚"
            : $"最短 L{ModelSpec.Format(lengths.Min())} 旋入 {ModelSpec.Format(shortest)} mm 超过螺纹深 {ModelSpec.Format(window.Max)}：螺纹孔太浅";
    }

    /// <summary>
    /// 摆位：零件轴 <see cref="ScrewProfile.Axis"/> 转到 −<paramref name="outward"/>（朝孔里），头顶落在 <paramref name="top"/>。
    /// 回 axes（零件 x、y、z 轴在总装里的方向，行）与零件原点。
    /// </summary>
    public static (double[,] Axes, (double X, double Y, double Z) Origin) Place(ScrewProfile profile, Vec3 outward, Vec3 top)
    {
        var (e1, e2, e3) = Basis(profile.Axis.Normalized());
        var (f1, f2, f3) = Basis((-outward).Normalized());
        // R = f1 e1ᵀ + f2 e2ᵀ + f3 e3ᵀ：零件第 i 根轴的像 = R 的第 i 列。
        Vec3 Image(Vec3 v) => f1 * e1.Dot(v) + f2 * e2.Dot(v) + f3 * e3.Dot(v);
        var x = Image(new Vec3(1, 0, 0));
        var y = Image(new Vec3(0, 1, 0));
        var z = Image(new Vec3(0, 0, 1));
        var axes = new double[3, 3] { { x.X, x.Y, x.Z }, { y.X, y.Y, y.Z }, { z.X, z.Y, z.Z } };
        var origin = top - Image(profile.Top);
        return (axes, (origin.X, origin.Y, origin.Z));
    }

    /// <summary>以 <paramref name="a"/> 为第一根轴的右手正交基（第二根取离 a 最不平行的坐标轴）。</summary>
    private static (Vec3, Vec3, Vec3) Basis(Vec3 a)
    {
        var helper = Math.Abs(a.X) < 0.9 ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0);
        var b = (helper - a * a.Dot(helper)).Normalized();
        return (a, b, a.Cross(b));
    }

    /// <summary>下载前按经验估一个承压面（组合螺丝垫圈约 0.52d / 0.88d），用来先挑长度；下载后按现量再挑一次。</summary>
    public static ScrewProfile Estimate(ScrewKind kind, double diameter)
        => kind.Head == ScrewHead.Countersunk
            ? new ScrewProfile(new Vec3(0, 0, -1), default, 0, 0, 0, diameter, diameter)
            : new ScrewProfile(new Vec3(0, 0, -1), default, 0, kind.Head == ScrewHead.Cap ? diameter : 0.6 * diameter,
                (kind.Head == ScrewHead.Cap ? diameter : 0.6 * diameter) + kind.WasherEstimate * diameter, 0.8 * diameter, (0.8 + kind.WasherEstimate * 0.6) * diameter);

    private static string MSize(double diameter) => "M" + ModelSpec.Format(diameter);
}
