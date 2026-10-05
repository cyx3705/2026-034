using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 读零件的几何摘要（1.9.0「新建工程图」）：包围盒与全部圆柱面（孔壁、圆角、凸台外圆、腰型孔端头），交给 <see cref="DrawingPlanner"/> 挑视图。
/// </summary>
/// <remarks>
/// <para>凹凸的判法与工程图里认孔相同（<see cref="HoleCalloutPlanner.IsHoleWall"/>，DEC-005）：在圆柱面上取一点求曲面法向，
/// 按 <c>FaceInSurfaceSense</c> 换成面法向，指向轴线即内凹。</para>
/// <para>孔口朝向：孔壁每条整圆边旁边若是一张与轴线垂直的平面，且这张平面在圆柱面的这一头（面法向朝圆柱面外），
/// 孔口就朝这张平面的法向。通孔两头都有，沉头孔底孔在沉头那一头也算（从那面看得见），盲孔只有一头。</para>
/// </remarks>
internal static class PartScan
{
    // swBodyType_e.swSolidBody
    private const int SolidBody = 0;

    public static PartGeometry Read(QuickCommandContext context, object part)
    {
        var api = context.Api;
        var box = api.CallDoubles(part, "IPartDoc", "GetPartBox", true);
        if (box.Length < 6)
            throw new QuickCommandException("读不到零件的包围盒（零件里没有实体？）。");

        var cylinders = new List<PartCylinder>();
        var planes = new List<ModelDirection>();
        var windows = new List<ModelDirection>();
        var chamfers = new List<PartChamfer>();
        foreach (var body in api.CallArray(part, "IPartDoc", "GetBodies2", SolidBody, true))
        {
            foreach (var face in api.CallArray(body, "IBody2", "GetFaces"))
            {
                context.Cancellation.ThrowIfCancellationRequested();
                if (api.Call(face, "IFace2", "GetSurface") is not { } surface)
                    continue;
                if (api.CallBool(surface, "ISurface", "IsPlane"))
                {
                    var n = api.CallDoubles(face, "IFace2", "get_Normal");
                    if (n.Length < 3)
                        continue;
                    var normal = new ModelDirection(n[0], n[1], n[2]).Normalized();
                    planes.Add(normal);
                    for (var i = Windows(api, face); i > 0; i--)
                        windows.Add(normal);
                    if (ReadChamfer(api, face, normal) is { } chamfer)
                        chamfers.Add(chamfer);
                }
                else if (ReadCylinder(api, face, surface) is { } cylinder)
                {
                    cylinders.Add(cylinder);
                }
            }
        }

        return new PartGeometry(new ModelBox(box[0], box[1], box[2], box[3], box[4], box[5]), cylinders, planes, windows, chamfers);
    }

    /// <summary>
    /// 平面倒角（1.10.0）：所属特征是倒角（<c>Chamfer</c>），法向恰好有一个分量为 0——那根轴就是倒掉的棱的方向；
    /// 两条直角边取面的包围盒沿另两根轴的跨度（真机：限位块右 C5 包围盒 5 × 5 × 100、C1 为 1 × 1 × 100）。三个角上斜着的不算。
    /// </summary>
    private static PartChamfer? ReadChamfer(SolidWorksApi api, object face, ModelDirection normal)
    {
        double[] n = [normal.X, normal.Y, normal.Z];
        var zero = Enumerable.Range(0, 3).Where(i => Math.Abs(n[i]) < 1e-6).ToList();
        if (zero.Count != 1 || !IsChamferFeature(api, face))
            return null;
        var box = api.CallDoubles(face, "IFace2", "GetBox");
        if (box.Length < 6)
            return null;
        var axis = zero[0];
        var legs = Enumerable.Range(0, 3).Where(i => i != axis).Select(i => box[i + 3] - box[i]).ToList();
        double[] direction = [0, 0, 0];
        direction[axis] = 1;
        return new PartChamfer(new ModelDirection(direction[0], direction[1], direction[2]), legs[0], legs[1]);
    }

    /// <summary>面属于倒角特征（特征类型 <c>Chamfer</c>，真机读回）。</summary>
    internal static bool IsChamferFeature(SolidWorksApi api, object face)
    {
        try
        {
            return api.Call(face, "IFace2", "GetFeature") is { } feature && api.CallString(feature, "IFeature", "GetTypeName2") == ChamferFeatureType;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            return false;
        }
    }

    /// <summary>倒角特征的类型名。</summary>
    public const string ChamferFeatureType = "Chamfer";

    private static PartCylinder? ReadCylinder(SolidWorksApi api, object face, object surface)
    {
        if (!api.CallBool(surface, "ISurface", "IsCylinder"))
            return null;
        var p = api.CallDoubles(surface, "ISurface", "get_CylinderParams");
        if (p.Length < 7)
            return null;
        var axis = new ModelDirection(p[3], p[4], p[5]).Normalized();
        var point = new ModelDirection(p[0], p[1], p[2]);
        var radius = p[6];

        var (ux, uy, uz) = HoleCalloutPlanner.Perpendicular(axis.X, axis.Y, axis.Z);
        var evaluated = api.CallDoubles(surface, "ISurface", "EvaluateAtPoint", p[0] + radius * ux, p[1] + radius * uy, p[2] + radius * uz);
        if (evaluated.Length < 3)
            return null;
        var dot = evaluated[0] * ux + evaluated[1] * uy + evaluated[2] * uz;
        var concave = HoleCalloutPlanner.IsHoleWall(radius, radius, api.CallBool(face, "IFace2", "FaceInSurfaceSense"), dot);

        var circles = new List<(object Edge, double T)>();
        foreach (var edge in api.CallArray(face, "IFace2", "GetEdges"))
        {
            var curve = api.Call(edge, "IEdge", "GetCurve");
            if (curve is null || !api.CallBool(curve, "ICurve", "IsCircle") || api.Call(edge, "IEdge", "GetStartVertex") is not null)
                continue;
            var c = api.CallDoubles(curve, "ICurve", "get_CircleParams");
            if (c.Length >= 7)
                circles.Add((edge, (c[0] - p[0]) * axis.X + (c[1] - p[1]) * axis.Y + (c[2] - p[2]) * axis.Z));
        }

        var openings = new List<ModelDirection>();
        if (concave && circles.Count > 0 && Middle(api, face, point, axis) is { } middle)
        {
            foreach (var (edge, t) in circles)
            {
                foreach (var other in api.CallArray(edge, "IEdge", "GetTwoAdjacentFaces2"))
                {
                    if (other is null || api.Call(other, "IFace2", "GetSurface") is not { } plane || !api.CallBool(plane, "ISurface", "IsPlane"))
                        continue;
                    var n = api.CallDoubles(other, "IFace2", "get_Normal");
                    if (n.Length < 3)
                        continue;
                    var normal = new ModelDirection(n[0], n[1], n[2]).Normalized();
                    var along = normal.Dot(axis);
                    // 平面在圆柱面的这一头，且外法向背着圆柱面：孔口朝它。
                    if (Math.Abs(along) >= 0.999 && (t - middle) * along > 0 && !openings.Any(o => o.Dot(normal) > 0.999))
                        openings.Add(normal);
                }
            }
        }

        return new PartCylinder(axis, point, radius, concave, circles.Count > 0, FeatureName(api, face), openings);
    }

    /// <summary>平面上的窗口数：内环（<c>ILoop2.IsOuter</c> 为假）里不是单独一条整圆边的（单独整圆是圆孔，已按孔算）。</summary>
    private static int Windows(SolidWorksApi api, object face)
    {
        if (api.CallInt(face, "IFace2", "GetLoopCount") <= 1)
            return 0;
        var count = 0;
        foreach (var loop in api.CallArray(face, "IFace2", "GetLoops"))
        {
            if (loop is null || api.CallBool(loop, "ILoop2", "IsOuter"))
                continue;
            var edges = api.CallArray(loop, "ILoop2", "GetEdges");
            var circle = edges.Length == 1
                && api.Call(edges[0], "IEdge", "GetCurve") is { } curve
                && api.CallBool(curve, "ICurve", "IsCircle");
            if (!circle)
                count++;
        }

        return count;
    }

    /// <summary>圆柱面沿轴线的中点（取面的包围盒中心投到轴上），读不到返回 null。</summary>
    private static double? Middle(SolidWorksApi api, object face, ModelDirection point, ModelDirection axis)
    {
        var box = api.CallDoubles(face, "IFace2", "GetBox");
        if (box.Length < 6)
            return null;
        var (cx, cy, cz) = ((box[0] + box[3]) / 2, (box[1] + box[4]) / 2, (box[2] + box[5]) / 2);
        return (cx - point.X) * axis.X + (cy - point.Y) * axis.Y + (cz - point.Z) * axis.Z;
    }

    private static string FeatureName(SolidWorksApi api, object face)
    {
        try
        {
            return api.Call(face, "IFace2", "GetFeature") is { } feature ? api.CallString(feature, "IFeature", "get_Name") : string.Empty;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            return string.Empty;
        }
    }
}
