using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 一键螺丝（1.44.0）读 SolidWorks：总装里的螺纹孔（异形孔向导特征）、孔轴线上别的零件的孔面、下载螺钉件的尺寸。
/// </summary>
/// <remarks>
/// 真机（SolidWorks 2025）：<c>IWizardHoleFeatureData2</c> 的螺纹孔 <c>ThreadDiameter</c> &gt; 0（GB 359 / ISO 147 都是），
/// 盲孔给 <c>ThreadDepth</c>，贯穿 <c>ThreadEndCondition</c>=1（螺纹长 = 孔长）；特征 <c>GetFaces</c> 与 <c>GetBodies2</c> 一样是零件坐标，
/// 要乘零部件 <c>Transform2</c>。同一个特征的每个孔是一张底孔圆柱面（半径最小的那组）。
/// </remarks>
internal static class ScrewScan
{
    /// <summary>一个零部件里异形孔向导打的螺纹孔（总装 mm）：每个孔给两头朝外的两种写法（哪头朝外由过孔定），贯穿的螺纹长 = 孔长。</summary>
    public static IReadOnlyList<(TappedHole High, TappedHole Low, double Length)> TappedHoles(SolidWorksApi api, object component)
    {
        var owner = api.CallString(component, "IComponent2", "get_Name2");
        if (api.Call(component, "IComponent2", "GetModelDoc2") is not { } model)
            return [];
        var (rows, origin, scale) = AssemblyGeometry.Transform(api, component);
        Vec3 Point(double x, double y, double z) => (rows[0] * x + rows[1] * y + rows[2] * z) * (1000 * scale) + origin;

        var holes = new List<(TappedHole, TappedHole, double)>();
        for (var feature = api.Call(model, "IModelDoc2", "FirstFeature"); feature is not null; feature = api.Call(feature, "IFeature", "GetNextFeature"))
        {
            if (api.CallString(feature, "IFeature", "GetTypeName2") != "HoleWzd" || api.CallBool(feature, "IFeature", "IsSuppressed")
                || api.Call(feature, "IFeature", "GetDefinition") is not { } data)
                continue;
            var thread = Number(api.Call(data, "IWizardHoleFeatureData2", "get_ThreadDiameter")) * 1000;
            if (thread <= 0)
                continue;
            var diameter = Math.Round(thread, 1);
            var through = api.CallInt(data, "IWizardHoleFeatureData2", "get_ThreadEndCondition") == 1;
            var depth = Number(api.Call(data, "IWizardHoleFeatureData2", "get_ThreadDepth")) * 1000;
            var name = api.CallString(feature, "IFeature", "get_Name");
            var cylinders = api.CallArray(feature, "IFeature", "GetFaces")
                .Select(face => (Face: face, Surface: api.Call(face, "IFace2", "GetSurface")))
                .Where(item => item.Surface is not null && api.CallBool(item.Surface, "ISurface", "IsCylinder"))
                .Select(item => (item.Face, Params: api.CallDoubles(item.Surface!, "ISurface", "get_CylinderParams")))
                .Where(item => item.Params.Length >= 7)
                .ToList();
            if (cylinders.Count == 0)
                continue;
            var drill = cylinders.Min(item => item.Params[6]);
            foreach (var (face, p) in cylinders.Where(item => item.Params[6] < drill + 1e-6))
            {
                var axis = (rows[0] * p[3] + rows[1] * p[4] + rows[2] * p[5]).Normalized();
                var center = Point(p[0], p[1], p[2]);
                var t = api.CallDoubles(face, "IFace2", "GetTessTriangles", true);
                if (t.Length < 9)
                    continue;
                var along = new List<double>(t.Length / 3);
                for (var i = 0; i + 2 < t.Length; i += 3)
                    along.Add((Point(t[i], t[i + 1], t[i + 2]) - center).Dot(axis));
                var low = center + axis * along.Min();
                var high = center + axis * along.Max();
                var length = along.Max() - along.Min();
                var usable = through || depth <= 0 ? length : Math.Min(depth, length);
                holes.Add((new TappedHole(owner, name, high, axis, diameter, usable, through), new TappedHole(owner, name, low, -axis, diameter, usable, through), length));
            }
        }

        return holes;
    }

    /// <summary>
    /// 零部件全部圆柱 / 圆锥面（总装 mm，带三角面片顶点与孔壁 / 杆）：一个零部件读一次，过孔、已装螺钉、按底孔认螺纹孔都用它。
    /// </summary>
    public static IReadOnlyList<RoundFace> RoundFaces(SolidWorksApi api, object component)
    {
        var owner = api.CallString(component, "IComponent2", "get_Name2");
        var (rows, origin, scale) = AssemblyGeometry.Transform(api, component);
        Vec3 Point(double x, double y, double z) => (rows[0] * x + rows[1] * y + rows[2] * z) * (1000 * scale) + origin;
        Vec3 Turn(double x, double y, double z) => (rows[0] * x + rows[1] * y + rows[2] * z).Normalized();
        var faces = new List<RoundFace>();
        foreach (var face in AssemblyGeometry.AllFaces(api, component))
        {
            if (api.Call(face, "IFace2", "GetSurface") is not { } surface)
                continue;
            var cone = api.CallBool(surface, "ISurface", "IsCone");
            if (!cone && !api.CallBool(surface, "ISurface", "IsCylinder"))
                continue;
            var p = api.CallDoubles(surface, "ISurface", cone ? "get_ConeParams" : "get_CylinderParams");
            if (p.Length < 7)
                continue;
            var t = api.CallDoubles(face, "IFace2", "GetTessTriangles", true);
            var n = api.CallDoubles(face, "IFace2", "GetTessNorms");
            if (t.Length < 9 || n.Length < 3)
                continue;
            var axis = Turn(p[3], p[4], p[5]);
            var center = Point(p[0], p[1], p[2]);
            var points = new List<Vec3>(t.Length / 3);
            for (var i = 0; i + 2 < t.Length; i += 3)
                points.Add(Point(t[i], t[i + 1], t[i + 2]));
            // 孔壁还是杆：第一个顶点的法向朝不朝轴线（GetTessNorms 已按面朝向）。
            var first = points[0] - center;
            var radial = first - axis * first.Dot(axis);
            var hole = Turn(n[0], n[1], n[2]).Dot(radial) < 0;
            faces.Add(new RoundFace(owner, cone, axis, center, cone ? 0 : p[6] * 1000 * scale, hole, points));
        }

        return faces;
    }

    /// <summary>包围盒挨不挨孔轴线（螺纹孔口往外 <paramref name="span"/> mm、往里 <paramref name="depth"/> mm 这一段，放大 <paramref name="reach"/>）。</summary>
    public static bool Along((Vec3 Min, Vec3 Max) box, TappedHole hole, double depth, double span, double reach)
    {
        // 线段与放大盒的 slab 求交。
        var start = hole.Entry - hole.Outward * depth;
        var direction = hole.Outward * (depth + span);
        double t0 = 0, t1 = 1;
        foreach (var (s, d, lo, hi) in new[]
                 {
                     (start.X, direction.X, box.Min.X - reach, box.Max.X + reach),
                     (start.Y, direction.Y, box.Min.Y - reach, box.Max.Y + reach),
                     (start.Z, direction.Z, box.Min.Z - reach, box.Max.Z + reach),
                 })
        {
            if (Math.Abs(d) < 1e-9)
            {
                if (s < lo || s > hi)
                    return false;
                continue;
            }

            var a = (lo - s) / d;
            var b = (hi - s) / d;
            t0 = Math.Max(t0, Math.Min(a, b));
            t1 = Math.Min(t1, Math.Max(a, b));
            if (t0 > t1)
                return false;
        }

        return true;
    }

    /// <summary>
    /// 现量下载的螺钉件（零件坐标 mm）：杆 = 半径 0.4d–0.55d 里最长的圆柱，头在半径大的那头；
    /// 承压面 = 半径超过 0.65d 的顶点（含垫圈实体）离头顶最远处。
    /// </summary>
    public static ScrewProfile Profile(SolidWorksApi api, object part, double diameter, double length)
    {
        var bodies = api.CallArray(part, "IPartDoc", "GetBodies2", 0, true);
        (Vec3 Point, Vec3 Axis, double Length, object Body)? shank = null;
        var points = new List<(Vec3 Point, object Body)>();
        foreach (var body in bodies)
        {
            foreach (var face in api.CallArray(body, "IBody2", "GetFaces"))
            {
                var t = api.CallDoubles(face, "IFace2", "GetTessTriangles", true);
                for (var i = 0; i + 2 < t.Length; i += 3)
                    points.Add((new Vec3(t[i], t[i + 1], t[i + 2]) * 1000, body));
                if (api.Call(face, "IFace2", "GetSurface") is not { } surface || !api.CallBool(surface, "ISurface", "IsCylinder"))
                    continue;
                var p = api.CallDoubles(surface, "ISurface", "get_CylinderParams");
                var r = p.Length >= 7 ? p[6] * 1000 : 0;
                if (r < 0.4 * diameter || r > 0.55 * diameter)
                    continue;
                var axis = new Vec3(p[3], p[4], p[5]).Normalized();
                var b = api.CallDoubles(face, "IFace2", "GetBox");
                var extent = b.Length == 6 ? Math.Abs(new Vec3(b[3] - b[0], b[4] - b[1], b[5] - b[2]).Dot(axis)) * 1000 : 0;
                if (shank is null || extent > shank.Value.Length)
                    shank = (new Vec3(p[0], p[1], p[2]) * 1000, axis, extent, body);
            }
        }

        if (shank is not { } s || points.Count == 0)
            throw new QuickCommandException($"螺钉件读不出杆（没有半径约 {ModelSpec.Format(diameter / 2)} mm 的圆柱面），摆不了。");
        var rows = points.Select(item =>
        {
            var v = item.Point - s.Point;
            var along = v.Dot(s.Axis);
            return (Along: along, Radius: (v - s.Axis * along).Length, item.Body);
        }).ToList();
        var wide = rows.Where(row => row.Radius > 0.65 * diameter).ToList();
        if (wide.Count == 0)
            throw new QuickCommandException("螺钉件读不出头部（没有比杆粗的地方）。");
        // 头在粗的那头：粗处轴向平均值偏哪头。
        var axisSign = wide.Average(row => row.Along) <= rows.Average(row => row.Along) ? 1 : -1;
        var axisOut = s.Axis * axisSign;
        var along = rows.Select(row => (Along: row.Along * axisSign, row.Radius, row.Body)).ToList();
        var top = along.Min(row => row.Along);
        var tip = along.Max(row => row.Along) - top;
        var bearing = along.Where(row => row.Radius > 0.65 * diameter).Max(row => row.Along) - top;
        var headRadius = along.Where(row => ReferenceEquals(row.Body, s.Body)).Max(row => row.Radius);
        var bearingRadius = along.Max(row => row.Radius);
        return new ScrewProfile(axisOut, s.Point + axisOut * top, length, tip, bearing, headRadius, bearingRadius);
    }

    private static double Number(object? value) => value is null ? 0 : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
}
