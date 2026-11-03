using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 接触面检测与配合共用的总装几何读取（1.39.0）：零部件的面换到总装坐标、按 F 序号找回面、找零部件的基准面与原点。
/// </summary>
/// <remarks>
/// 真机（SolidWorks 2025，2026-043 总装）：<c>IComponent2.GetBodies2</c> 拿到的实体几何是<b>零件坐标</b>（立柱底板体盒 −100..100，不是总装位置），
/// 要自己乘 <c>Transform2</c>；子装配里零件的 <c>Transform2</c> 是相对根总装的。这些面在总装里直接 <c>IEntity.Select4</c> 能选中（选中的零部件就是它）。
/// </remarks>
internal static class AssemblyGeometry
{
    private const int SolidBody = 0;

    /// <summary>零件（没有子零部件）层的全部零部件，名字是 <c>Name2</c>（子装配里的写成「子装配-1/零件-1」）；压缩的不要。</summary>
    public static IReadOnlyList<object> Leaves(SolidWorksApi api, object assembly)
        => api.CallArray(assembly, "IAssemblyDoc", "GetComponents", false)
            .Where(component => api.CallArray(component, "IComponent2", "GetChildren").Length == 0 && !api.CallBool(component, "IComponent2", "IsSuppressed"))
            .ToList();

    /// <summary>按名字找零部件（顶层或「子装配-1/零件-1」）；找不到抛参数错。</summary>
    public static object Component(SolidWorksApi api, object assembly, string name)
    {
        foreach (var component in api.CallArray(assembly, "IAssemblyDoc", "GetComponents", false))
        {
            if (string.Equals(api.CallString(component, "IComponent2", "get_Name2"), name, StringComparison.OrdinalIgnoreCase))
                return component;
        }

        return ModelAccess.Component(api, assembly, name);
    }

    /// <summary>零部件本身是零件就是它，是子装配就是它下面全部零件。</summary>
    public static IReadOnlyList<object> LeavesOf(SolidWorksApi api, object component)
    {
        var children = api.CallArray(component, "IComponent2", "GetChildren");
        return children.Length == 0 ? [component] : children.SelectMany(child => LeavesOf(api, child)).ToList();
    }

    /// <summary>零部件在总装里的包围盒（mm）；读不到为空。</summary>
    public static (Vec3 Min, Vec3 Max)? Box(SolidWorksApi api, object component)
        => ModelAccess.Box(api, component) is { Length: 6 } b ? (new Vec3(b[0], b[1], b[2]), new Vec3(b[3], b[4], b[5])) : null;

    /// <summary>
    /// 零件的平面与圆柱面（总装 mm），面编号按各实体 <c>GetFaces</c> 依次排（别的曲面也占号）。
    /// <paramref name="near"/> 给了就先用面的包围盒筛：离 <paramref name="within"/> 超过它的面不做三角化。
    /// </summary>
    public static IReadOnlyList<ContactFace> Faces(SolidWorksApi api, object component, (Vec3 Min, Vec3 Max)? within = null, double near = 0)
    {
        var owner = api.CallString(component, "IComponent2", "get_Name2");
        var (rows, origin, scale) = Transform(api, component);
        Vec3 Point(double x, double y, double z) => (rows[0] * x + rows[1] * y + rows[2] * z) * (1000 * scale) + origin;
        Vec3 Turn(double x, double y, double z) => (rows[0] * x + rows[1] * y + rows[2] * z).Normalized();

        var faces = new List<ContactFace>();
        var index = 0;
        foreach (var face in AllFaces(api, component))
        {
            var number = index++;
            if (api.Call(face, "IFace2", "GetSurface") is not { } surface)
                continue;
            var plane = api.CallBool(surface, "ISurface", "IsPlane");
            if (!plane && !api.CallBool(surface, "ISurface", "IsCylinder"))
                continue;
            if (within is { } box && api.CallDoubles(face, "IFace2", "GetBox") is { Length: 6 } fb && !Touches(Corners(fb).Select(c => Point(c.X, c.Y, c.Z)), box, near))
                continue;
            var t = api.CallDoubles(face, "IFace2", "GetTessTriangles", true);
            if (t.Length < 9)
                continue;
            var triangles = new List<Vec3>(t.Length / 3);
            for (var i = 0; i + 2 < t.Length; i += 3)
                triangles.Add(Point(t[i], t[i + 1], t[i + 2]));
            if (plane)
            {
                var n = api.CallDoubles(face, "IFace2", "get_Normal");
                if (n.Length < 3)
                    continue;
                faces.Add(new ContactFace(owner, number, ContactSurface.Plane, Turn(n[0], n[1], n[2]), triangles[0], 0, false, triangles));
            }
            else
            {
                var p = api.CallDoubles(surface, "ISurface", "get_CylinderParams");
                var norms = api.CallDoubles(face, "IFace2", "GetTessNorms");
                if (p.Length < 7 || norms.Length < 3)
                    continue;
                // 孔壁还是轴：第一个面片顶点的法向朝不朝轴线（GetTessNorms 已按面朝向，不用再看 FaceInSurfaceSense）。
                var axis = new Vec3(p[3], p[4], p[5]).Normalized();
                var w = new Vec3(t[0] - p[0], t[1] - p[1], t[2] - p[2]);
                var radial = w - axis * w.Dot(axis);
                var hole = new Vec3(norms[0], norms[1], norms[2]).Dot(radial) < 0;
                faces.Add(new ContactFace(owner, number, ContactSurface.Cylinder, Turn(p[3], p[4], p[5]), Point(p[0], p[1], p[2]), p[6] * 1000 * scale, hole, triangles));
            }
        }

        return faces;
    }

    /// <summary>零部件的第 <paramref name="index"/> 个面（编号同 <see cref="Faces"/>）；超出抛参数错。</summary>
    public static object Face(SolidWorksApi api, object component, int index)
    {
        var all = AllFaces(api, component);
        return index < all.Count ? all[index] : throw new QuickArgumentException($"「{api.CallString(component, "IComponent2", "get_Name2")}」只有 {all.Count} 个面（F0–F{all.Count - 1}），没有 F{index}。");
    }

    /// <summary><paramref name="owner"/>（<paramref name="isComponent"/> 为假时是总装自己）特征树里第 <paramref name="index"/> 个基准面（前视 0、上视 1、右视 2，不认名字）或原点。</summary>
    public static object Feature(SolidWorksApi api, object owner, bool isComponent, MateTarget.Kinds kind, int index)
    {
        var type = kind == MateTarget.Kinds.Origin ? "OriginProfileFeature" : "RefPlane";
        var seen = 0;
        var feature = api.Call(owner, isComponent ? "IComponent2" : "IModelDoc2", "FirstFeature");
        while (feature is not null)
        {
            if (api.CallString(feature, "IFeature", "GetTypeName2") == type && (kind == MateTarget.Kinds.Origin || seen++ == index))
                return feature;
            feature = api.Call(feature, "IFeature", "GetNextFeature");
        }

        throw new QuickCommandException($"特征树里找不到{(kind == MateTarget.Kinds.Origin ? "原点" : MateSpec.PlaneNames[index] + "基准面")}。");
    }

    private static List<object> AllFaces(SolidWorksApi api, object component)
        => api.CallArray(component, "IComponent2", "GetBodies2", SolidBody)
            .SelectMany(body => api.CallArray(body, "IBody2", "GetFaces"))
            .ToList();

    /// <summary>零部件变换：零件三根轴（行）、原点（mm）、比例。</summary>
    private static (Vec3[] Rows, Vec3 Origin, double Scale) Transform(SolidWorksApi api, object component)
    {
        var d = api.Call(component, "IComponent2", "get_Transform2") is { } transform ? api.CallDoubles(transform, "IMathTransform", "get_ArrayData") : [];
        if (d.Length < 13)
            return ([new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1)], default, 1);
        return ([new Vec3(d[0], d[1], d[2]), new Vec3(d[3], d[4], d[5]), new Vec3(d[6], d[7], d[8])], new Vec3(d[9], d[10], d[11]) * 1000, d[12]);
    }

    private static IEnumerable<Vec3> Corners(double[] b)
    {
        foreach (var x in new[] { b[0], b[3] })
        foreach (var y in new[] { b[1], b[4] })
        foreach (var z in new[] { b[2], b[5] })
            yield return new Vec3(x, y, z);
    }

    /// <summary>点集的外接框与 <paramref name="box"/> 放大 <paramref name="near"/> 后相交。</summary>
    public static bool Touches(IEnumerable<Vec3> points, (Vec3 Min, Vec3 Max) box, double near)
    {
        var list = points.ToList();
        return list.Max(p => p.X) >= box.Min.X - near && list.Min(p => p.X) <= box.Max.X + near
               && list.Max(p => p.Y) >= box.Min.Y - near && list.Min(p => p.Y) <= box.Max.Y + near
               && list.Max(p => p.Z) >= box.Min.Z - near && list.Min(p => p.Z) <= box.Max.Z + near;
    }
}
