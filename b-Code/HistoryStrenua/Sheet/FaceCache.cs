using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 一次扫描里每个零件的面只经 COM 读一次（1.45.0）。
/// </summary>
/// <remarks>
/// 真机（2026-043 总装，150 多个顶层零部件）：贴合树「扫贴合面」跑十分钟没完——<see cref="AssemblyContacts.Scan"/> 每一对挨着的零部件
/// 都把双方全部叶子零件的面重读一遍（取曲面、判类型、取面盒），一根横梁挨着十几个零件就读十几遍，子装配模组的面更多。
/// 这里第一次碰到一个零件时把面头（编号、平面/圆柱、总装坐标面盒）读全，之后按包围盒筛只在内存里做；
/// 三角化、法向、圆柱参数用到才读，同一个面只读一次。面编号与 <see cref="AssemblyGeometry.Face"/> 一致（各实体 <c>GetFaces</c> 依次排，别的曲面也占号）。
/// </remarks>
internal sealed class FaceCache(SolidWorksApi api)
{
    private sealed record Head(object Face, object Surface, int Number, bool Plane, Vec3 Min, Vec3 Max, bool HasBox);

    private sealed class Part(string owner, Vec3[] rows, Vec3 origin, double scale)
    {
        public string Owner { get; } = owner;
        public List<Head> Heads { get; } = [];
        public Dictionary<int, ContactFace?> Built { get; } = [];

        public Vec3 Point(double x, double y, double z) => (rows[0] * x + rows[1] * y + rows[2] * z) * (1000 * scale) + origin;

        public Vec3 Turn(double x, double y, double z) => (rows[0] * x + rows[1] * y + rows[2] * z).Normalized();

        public double Scale => scale;
    }

    private readonly Dictionary<string, Part> parts = new(StringComparer.Ordinal);

    /// <summary>零件的平面与圆柱面（总装 mm）；<paramref name="within"/> 给了就只要面盒离它不超过 <paramref name="near"/> 的。</summary>
    public IReadOnlyList<ContactFace> Faces(object component, (Vec3 Min, Vec3 Max)? within = null, double near = 0)
    {
        var part = Load(component);
        var faces = new List<ContactFace>();
        foreach (var head in part.Heads)
        {
            if (within is { } box && head.HasBox && !AssemblyGeometry.Touches([head.Min, head.Max], box, near))
                continue;
            if (!part.Built.TryGetValue(head.Number, out var face))
                part.Built[head.Number] = face = Build(part, head);
            if (face is not null)
                faces.Add(face);
        }

        return faces;
    }

    private Part Load(object component)
    {
        var owner = api.CallString(component, "IComponent2", "get_Name2");
        if (parts.TryGetValue(owner, out var cached))
            return cached;

        var (rows, origin, scale) = AssemblyGeometry.Transform(api, component);
        var part = new Part(owner, rows, origin, scale);
        var index = 0;
        foreach (var face in AssemblyGeometry.AllFaces(api, component))
        {
            var number = index++;
            if (api.Call(face, "IFace2", "GetSurface") is not { } surface)
                continue;
            var plane = api.CallBool(surface, "ISurface", "IsPlane");
            if (!plane && !api.CallBool(surface, "ISurface", "IsCylinder"))
                continue;
            if (api.CallDoubles(face, "IFace2", "GetBox") is { Length: 6 } fb)
            {
                var corners = AssemblyGeometry.Corners(fb).Select(c => part.Point(c.X, c.Y, c.Z)).ToList();
                var min = new Vec3(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Min(p => p.Z));
                var max = new Vec3(corners.Max(p => p.X), corners.Max(p => p.Y), corners.Max(p => p.Z));
                part.Heads.Add(new Head(face, surface, number, plane, min, max, true));
            }
            else
            {
                // 读不到面盒的不筛，照样三角化（与 1.44.0 一致）。
                part.Heads.Add(new Head(face, surface, number, plane, default, default, false));
            }
        }

        parts[owner] = part;
        return part;
    }

    private ContactFace? Build(Part part, Head head)
    {
        var t = api.CallDoubles(head.Face, "IFace2", "GetTessTriangles", true);
        if (t.Length < 9)
            return null;
        var triangles = new List<Vec3>(t.Length / 3);
        for (var i = 0; i + 2 < t.Length; i += 3)
            triangles.Add(part.Point(t[i], t[i + 1], t[i + 2]));
        if (head.Plane)
        {
            var n = api.CallDoubles(head.Face, "IFace2", "get_Normal");
            return n.Length < 3 ? null : new ContactFace(part.Owner, head.Number, ContactSurface.Plane, part.Turn(n[0], n[1], n[2]), triangles[0], 0, false, triangles);
        }

        var p = api.CallDoubles(head.Surface, "ISurface", "get_CylinderParams");
        var norms = api.CallDoubles(head.Face, "IFace2", "GetTessNorms");
        if (p.Length < 7 || norms.Length < 3)
            return null;
        // 孔壁还是轴：第一个面片顶点的法向朝不朝轴线（GetTessNorms 已按面朝向，不用再看 FaceInSurfaceSense）。
        var axis = new Vec3(p[3], p[4], p[5]).Normalized();
        var w = new Vec3(t[0] - p[0], t[1] - p[1], t[2] - p[2]);
        var radial = w - axis * w.Dot(axis);
        var hole = new Vec3(norms[0], norms[1], norms[2]).Dot(radial) < 0;
        return new ContactFace(part.Owner, head.Number, ContactSurface.Cylinder, part.Turn(p[3], p[4], p[5]), part.Point(p[0], p[1], p[2]), p[6] * 1000 * part.Scale, hole, triangles);
    }
}
