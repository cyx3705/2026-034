using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 把模型边变到图纸上。几何一律在零件自己的坐标系里返回：
/// 装配视图里先乘组件变换到装配空间，再乘视图的模型→图纸变换。
/// </summary>
internal sealed class ViewGeometry(SolidWorksApi api, object view)
{
    private readonly double[] _viewTransform = TransformData(api, view);
    private readonly bool _assembly = api.Call(view, "IView", "get_ReferencedDocument") is { } referenced
        && api.CallInt(referenced, "IModelDoc2", "GetType") == SwConst.DocumentAssembly;
    private readonly Dictionary<object, double[]> _componentTransforms = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, (object? Curve, double[]? Circle)> _curves = new(ReferenceEqualityComparer.Instance);

    /// <summary>视图变换的 16 个数（旋转 9、平移 3、比例 1），之后全在本地算，不再为每个点跨进程问一遍。</summary>
    internal static double[] TransformData(SolidWorksApi api, object view)
        => api.Call(view, "IView", "get_ModelToViewTransform") is { } transform
            ? api.CallDoubles(transform, "IMathTransform", "get_ArrayData")
            : throw new QuickCommandException("SolidWorks 没有返回视图变换。");

    /// <summary>边所属组件：零件视图没有组件，不用问。</summary>
    private object? Owner(object edge) => _assembly ? api.Call(edge, "IEntity", "GetComponent") : null;

    /// <summary>
    /// 是孔边就返回它在图纸上的圆心与半径（<c>Index</c> 由调用方补）。整圈的圆边是圆孔；
    /// 恰好半圈的圆弧是腰型孔的端头，另记下它朝哪边鼓，配对见 <see cref="SlotPlanner"/>。
    /// </summary>
    public HoleEdge? TryReadHole(object edge)
    {
        if (Circle(edge) is not { } circle)
            return null;
        // 整圈的边没有端点；腰型孔、槽口两端的半圆和被切掉一截的孔口都有——只留恰好半圈的。
        double[]? bulge = null;
        if (api.Call(edge, "IEdge", "GetStartVertex") is { } start
            && (bulge = SlotBulge(edge, circle, start)) is null)
            return null;
        if (HoleWall(edge, circle) is not { } wall)
            return null;

        var transforms = Transforms(edge);
        var axis = ToSheet(transforms, false, circle[3], circle[4], circle[5]);
        if (!HoleCalloutPlanner.FacesViewer(axis[0], axis[1], axis[2]))
            return null;

        var center = ToSheet(transforms, true, circle[0], circle[1], circle[2]);
        var (bx, by) = (0.0, 0.0);
        if (bulge is not null)
        {
            // 轴线正对图纸，鼓出方向就在图纸平面里；向量变换带着比例，重新归一。
            var sheet = ToSheet(transforms, false, bulge[0], bulge[1], bulge[2]);
            var length = Math.Sqrt(sheet[0] * sheet[0] + sheet[1] * sheet[1]);
            if (length <= 0)
                return null;
            (bx, by) = (sheet[0] / length, sheet[1] / length);
        }

        var (kind, dowel, component) = Kind(edge, wall);
        // 1.15.0：零件坐标里的轴线，跨视图认同一个孔（顶视图、底视图）。
        var model = HoleAxis.Of(component, new ModelDirection(circle[0], circle[1], circle[2]), new ModelDirection(circle[3], circle[4], circle[5]));
        return new HoleEdge(0, center[0], center[1], circle[6] * Scale, kind, bx, by, Dowel: dowel, Axis: model);
    }

    /// <summary>
    /// 恰好半圈的圆弧朝哪边鼓（零件坐标里的单位向量）；不是恰好半圈返回 null。
    /// 判法见 <see cref="SlotPlanner.Bulge"/>：在「圆心 + 半径 × 弦法向」处问圆弧上最近的点。
    /// </summary>
    private double[]? SlotBulge(object edge, double[] circle, object start)
    {
        if (api.Call(edge, "IEdge", "GetEndVertex") is not { } end)
            return null;
        var s = api.CallDoubles(start, "IVertex", "GetPoint");
        var e = api.CallDoubles(end, "IVertex", "GetPoint");
        if (s.Length < 3 || e.Length < 3 || !SlotPlanner.IsSemicircle(circle, s, e))
            return null;

        var normal = SlotPlanner.ChordNormal(circle, s, e);
        if (normal == (0, 0, 0))
            return null;
        var radius = circle[6];
        double[] probe = [circle[0] + radius * normal.X, circle[1] + radius * normal.Y, circle[2] + radius * normal.Z];
        var closest = api.CallDoubles(edge, "IEdge", "GetClosestPointOn", probe[0], probe[1], probe[2]);
        if (closest.Length < 3)
            return null;
        var (x, y, z) = SlotPlanner.Bulge(normal, probe, closest, radius);
        return [x, y, z];
    }

    /// <summary>
    /// 孔的「种」：组件 + 孔壁所属特征。同一个异形孔向导特征、同一次拉伸切除、同一个阵列里的孔是一种，
    /// SolidWorks 的孔标注会把它们数成「N×」。取不到特征时退回只按组件分（再由孔径细分）。
    /// 同时回答这是不是销钉孔（<see cref="IsDowelFeature"/>）与组件名（零件视图为空）。
    /// </summary>
    private (string Kind, bool Dowel, string Component) Kind(object edge, object wall)
    {
        var component = Owner(edge) is { } owner
            ? api.CallString(owner, "IComponent2", "get_Name2")
            : string.Empty;
        string feature;
        var dowel = false;
        try
        {
            if (api.Call(wall, "IFace2", "GetFeature") is { } owning)
            {
                feature = api.CallString(owning, "IFeature", "get_Name");
                var key = component + "/" + feature;
                if (!_dowelFeatures.TryGetValue(key, out dowel))
                    _dowelFeatures[key] = dowel = IsDowelFeature(owning);
            }
            else
            {
                feature = string.Empty;
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            feature = string.Empty;
        }

        return (component + "/" + feature, dowel, component);
    }

    private readonly Dictionary<string, bool> _dowelFeatures = new(StringComparer.Ordinal);

    /// <summary>
    /// 异形孔向导做的销钉孔：特征类型 <c>HoleWzd</c>，定义里的紧固件类型（<c>FastenerType2</c>）是各标准的
    /// 「DowelHole」（<c>swWzdHoleStandardFastenerTypes_e</c> 703–712）。<c>Type</c> 是孔的细类（如柱形沉头通孔 14），不用它判。
    /// </summary>
    private bool IsDowelFeature(object feature)
    {
        try
        {
            return api.CallString(feature, "IFeature", "GetTypeName2") == "HoleWzd"
                && api.Call(feature, "IFeature", "GetDefinition") is { } definition
                && DowelPlanner.IsDowelFastener(api.CallInt(definition, "IWizardHoleFeatureData2", "get_FastenerType2"));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            return false;
        }
    }

    /// <summary>
    /// 圆边的圆心在图纸上的位置；不是圆边返回 null。注解附着的对象可能已经失效（悬空尺寸连着的边被删），
    /// 这时 SolidWorks 给的对象不再是边，调用报类型不符——也当不是圆边。
    /// </summary>
    public SheetPoint? TryReadCircleCenter(object edge)
    {
        try
        {
            if (Circle(edge) is not { } circle)
                return null;
            var center = ToSheet(Transforms(edge), true, circle[0], circle[1], circle[2]);
            return new SheetPoint(center[0], center[1]);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or System.Reflection.TargetException)
        {
            return null;
        }
    }

    /// <summary>
    /// 圆角弧（1.9.0「圆角标注」）：圆弧（1.12.0 起也收没有端点的整圈，圆心角记 2π）、轴线正对图纸、旁边贴着一张同半径的圆柱面；返回图纸上的圆心、半径、弧中点，
    /// 模型半径，以及圆柱面是不是内凹（内圆角 / 凹弧的圆心在零件外，外圆角的圆心在零件里）。孔、腰型孔端头不归这里管（先过 <see cref="TryReadHole"/>）。
    /// </summary>
    public FilletArc? TryReadArc(object edge, int index)
    {
        try
        {
            if (Circle(edge) is not { } circle)
                return null;
            var start = api.Call(edge, "IEdge", "GetStartVertex");
            var end = api.Call(edge, "IEdge", "GetEndVertex");
            var transforms = Transforms(edge);
            var axis = ToSheet(transforms, false, circle[3], circle[4], circle[5]);
            if (!HoleCalloutPlanner.FacesViewer(axis[0], axis[1], axis[2]))
                return null;
            if (ArcFaceConcave(edge, circle) is not { } concave)
                return null;
            var center = ToSheet(transforms, true, circle[0], circle[1], circle[2]);
            if (start is null || end is null)
            {
                // 1.12.0：没有端点的整圈（凸台、轴端；内凹的整圈是孔，先被 TryReadHole 认走）也收进来，减掉孔以后标 Ø（FilletPlanner.WithoutCircles）。
                // 弧中点随便取圆上一点，标的时候文字方向由规划器按整圆另定。
                var (px, py, pz) = HoleCalloutPlanner.Perpendicular(circle[3], circle[4], circle[5]);
                var on = ToSheet(transforms, true, circle[0] + circle[6] * px, circle[1] + circle[6] * py, circle[2] + circle[6] * pz);
                return new FilletArc(index, new SheetPoint(center[0], center[1]), circle[6] * Scale, circle[6], new SheetPoint(on[0], on[1]), concave, 2 * Math.PI);
            }

            var s = api.CallDoubles(start, "IVertex", "GetPoint");
            var e = api.CallDoubles(end, "IVertex", "GetPoint");
            if (s.Length < 3 || e.Length < 3 || ArcMiddle(edge, circle, s, e) is not { } middle)
                return null;
            var mid = ToSheet(transforms, true, middle[0], middle[1], middle[2]);
            var from = ToSheet(transforms, true, s[0], s[1], s[2]);
            var to = ToSheet(transforms, true, e[0], e[1], e[2]);
            var sweep = FilletPlanner.SweepOf(new SheetPoint(center[0], center[1]), new SheetPoint(from[0], from[1]), new SheetPoint(to[0], to[1]), new SheetPoint(mid[0], mid[1]));
            return new FilletArc(index, new SheetPoint(center[0], center[1]), circle[6] * Scale, circle[6], new SheetPoint(mid[0], mid[1]), concave, sweep,
                new SheetPoint(from[0], from[1]), new SheetPoint(to[0], to[1]));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or System.Reflection.TargetException)
        {
            return null;
        }
    }

    /// <summary>
    /// 倒角斜边（1.10.0「倒角标注」）：图纸上斜着的直边，一侧是正对图纸的平面，另一侧是倒角特征做的平面且侧着（法向在图纸平面里）。
    /// 返回斜边、两条直角边（图纸上的横、竖跨度除以比例）与倒角面的标识；不是返回 null。
    /// </summary>
    /// <param name="edge">视图里的边。</param>
    /// <param name="index">回指调用方手里的那条边。</param>
    /// <param name="segment">这条边在图纸上的样子（<see cref="TryReadLine"/> 读过的）。</param>
    public ChamferEdge? TryReadChamfer(object edge, int index, SheetSegment segment)
    {
        try
        {
            var (dx, dy) = (Math.Abs(segment.X2 - segment.X1), Math.Abs(segment.Y2 - segment.Y1));
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0 || dx <= length * 0.02 || dy <= length * 0.02)
                return null;
            var transforms = Transforms(edge);
            var facing = false;
            object? chamfer = null;
            foreach (var face in api.CallArray(edge, "IEdge", "GetTwoAdjacentFaces2"))
            {
                if (face is null || api.Call(face, "IFace2", "GetSurface") is not { } surface || !api.CallBool(surface, "ISurface", "IsPlane"))
                    continue;
                var n = api.CallDoubles(face, "IFace2", "get_Normal");
                if (n.Length < 3)
                    continue;
                var v = ToSheet(transforms, false, n[0], n[1], n[2]);
                var norm = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
                if (norm <= 0)
                    continue;
                var z = Math.Abs(v[2] / norm);
                if (z >= 0.999)
                    facing = true;
                else if (z <= 0.02 && PartScan.IsChamferFeature(api, face))
                    chamfer = face;
            }

            return facing && chamfer is not null
                ? new ChamferEdge(index, segment, dx / Scale, dy / Scale, FaceKey(edge, chamfer))
                : null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or System.Reflection.TargetException)
        {
            return null;
        }
    }

    /// <summary>已有倒角尺寸连着的边属于哪张倒角面（<see cref="ChamferEdge.Key"/>）；不是倒角面的边、对象已失效返回 null。</summary>
    public string? TryChamferKey(object edge)
    {
        try
        {
            foreach (var face in api.CallArray(edge, "IEdge", "GetTwoAdjacentFaces2"))
            {
                if (face is not null && api.Call(face, "IFace2", "GetSurface") is { } surface && api.CallBool(surface, "ISurface", "IsPlane")
                    && PartScan.IsChamferFeature(api, face))
                    return FaceKey(edge, face);
            }

            return null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or System.Reflection.TargetException)
        {
            return null;
        }
    }

    /// <summary>面的标识：组件名 + 面在零件坐标里的包围盒（0.01 mm），跨视图认同一张倒角面用。</summary>
    private string FaceKey(object edge, object face)
    {
        var component = Owner(edge) is { } owner ? api.CallString(owner, "IComponent2", "get_Name2") : string.Empty;
        var box = api.CallDoubles(face, "IFace2", "GetBox");
        return component + "|" + string.Join(",", box.Select(v => Math.Round(v * 1e5).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>圆弧旁边同半径的圆柱面是不是内凹（判法同 <see cref="HoleWall"/>）；旁边没有同半径圆柱面返回 null。</summary>
    private bool? ArcFaceConcave(object edge, double[] circle)
    {
        var (ux, uy, uz) = HoleCalloutPlanner.Perpendicular(circle[3], circle[4], circle[5]);
        var radius = circle[6];
        var (px, py, pz) = (circle[0] + radius * ux, circle[1] + radius * uy, circle[2] + radius * uz);
        foreach (var face in api.CallArray(edge, "IEdge", "GetTwoAdjacentFaces2"))
        {
            if (face is null || api.Call(face, "IFace2", "GetSurface") is not { } surface || !api.CallBool(surface, "ISurface", "IsCylinder"))
                continue;
            var cylinder = api.CallDoubles(surface, "ISurface", "get_CylinderParams");
            var evaluated = api.CallDoubles(surface, "ISurface", "EvaluateAtPoint", px, py, pz);
            if (cylinder.Length < 7 || evaluated.Length < 3
                || Math.Abs(cylinder[6] - radius) > Math.Max(1e-9, radius * HoleCalloutPlanner.RadiusTolerance))
                continue;
            var dot = evaluated[0] * ux + evaluated[1] * uy + evaluated[2] * uz;
            return HoleCalloutPlanner.IsHoleWall(radius, cylinder[6], api.CallBool(face, "IFace2", "FaceInSurfaceSense"), dot);
        }

        return null;
    }

    /// <summary>圆或圆弧边在图纸上的圆心与半径；不是圆、或对象已失效返回 null（读已有 R / 直径尺寸连着的弧用，1.9.0）。</summary>
    public (SheetPoint Center, double Radius)? TryReadCircle(object edge)
    {
        try
        {
            if (Circle(edge) is not { } circle)
                return null;
            var center = ToSheet(Transforms(edge), true, circle[0], circle[1], circle[2]);
            return (new SheetPoint(center[0], center[1]), circle[6] * Scale);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or System.Reflection.TargetException)
        {
            return null;
        }
    }

    /// <summary>正对图纸的圆或圆弧的圆心与半径（1.32.0）；侧着看（投影成椭圆、直线）、不是圆返回 null。</summary>
    public (SheetPoint Center, double Radius)? TryReadFacingCircle(object edge)
    {
        try
        {
            if (Circle(edge) is not { } circle)
                return null;
            var axis = ToSheet(Transforms(edge), false, circle[3], circle[4], circle[5]);
            return HoleCalloutPlanner.FacesViewer(axis[0], axis[1], axis[2]) ? TryReadCircle(edge) : null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or System.Reflection.TargetException)
        {
            return null;
        }
    }

    /// <summary>顶点在图纸上的位置（1.31.0）；不是顶点返回 null。</summary>
    public SheetPoint? TryReadVertex(object entity)
    {
        try
        {
            var point = api.CallDoubles(entity, "IVertex", "GetPoint");
            if (point.Length < 3)
                return null;
            var sheet = ToSheet(Transforms(entity), true, point[0], point[1], point[2]);
            return new SheetPoint(sheet[0], sheet[1]);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or System.Reflection.TargetException)
        {
            return null;
        }
    }

    /// <summary>直边在图纸上的两个端点；不是直边（或没有端点）返回 null。</summary>
    public SheetSegment? TryReadLine(object edge)
    {
        var curve = CurveOf(edge).Curve;
        if (curve is null || !api.CallBool(curve, "ICurve", "IsLine"))
            return null;
        if (api.Call(edge, "IEdge", "GetStartVertex") is not { } start
            || api.Call(edge, "IEdge", "GetEndVertex") is not { } end)
            return null;
        var a = api.CallDoubles(start, "IVertex", "GetPoint");
        var b = api.CallDoubles(end, "IVertex", "GetPoint");
        if (a.Length < 3 || b.Length < 3)
            return null;

        var transforms = Transforms(edge);
        var p = ToSheet(transforms, true, a[0], a[1], a[2]);
        var q = ToSheet(transforms, true, b[0], b[1], b[2]);
        return new SheetSegment(p[0], p[1], q[0], q[1]);
    }

    /// <summary>
    /// 贴着这条直边、正对图纸的那张平面，有没有往图纸方向 (<paramref name="sx"/>, <paramref name="sy"/>) 延伸（1.17.0「方形槽」分槽与凸台）：
    /// 在边中点沿这个方向挪 0.2 mm（模型长度）取一点，它就在那张面所在的平面上，问面上离它最近的点——贴上了就是面往那边延伸。
    /// 不靠面法向的正负（<c>FaceInSurfaceSense</c> 与文档字面相反的坑见现行约定），只问点在不在面上。读不出返回 null。
    /// 只给零件视图用：方向按视图朝向（<see cref="Frame"/>）换回模型，装配视图的组件变换没算。
    /// </summary>
    public bool? MaterialToward(object edge, double sx, double sy)
    {
        try
        {
            if (api.Call(edge, "IEdge", "GetStartVertex") is not { } start || api.Call(edge, "IEdge", "GetEndVertex") is not { } end)
                return null;
            var a = api.CallDoubles(start, "IVertex", "GetPoint");
            var b = api.CallDoubles(end, "IVertex", "GetPoint");
            if (a.Length < 3 || b.Length < 3)
                return null;
            var frame = Frame;
            var d = new ModelDirection(
                sx * frame.SheetX.X + sy * frame.SheetY.X,
                sx * frame.SheetX.Y + sy * frame.SheetY.Y,
                sx * frame.SheetX.Z + sy * frame.SheetY.Z).Normalized();
            const double step = 2e-4;
            double[] probe = [(a[0] + b[0]) / 2 + d.X * step, (a[1] + b[1]) / 2 + d.Y * step, (a[2] + b[2]) / 2 + d.Z * step];
            bool? answer = null;
            foreach (var face in api.CallArray(edge, "IEdge", "GetTwoAdjacentFaces2"))
            {
                if (face is null || api.Call(face, "IFace2", "GetSurface") is not { } surface || !api.CallBool(surface, "ISurface", "IsPlane"))
                    continue;
                var n = api.CallDoubles(face, "IFace2", "get_Normal");
                if (n.Length < 3 || Math.Abs(new ModelDirection(n[0], n[1], n[2]).Normalized().Dot(frame.Normal)) < 0.999)
                    continue;
                var closest = api.CallDoubles(face, "IFace2", "GetClosestPointOn", probe[0], probe[1], probe[2]);
                if (closest.Length < 3)
                    continue;
                var gap = Math.Sqrt(Math.Pow(closest[0] - probe[0], 2) + Math.Pow(closest[1] - probe[1], 2) + Math.Pow(closest[2] - probe[2], 2));
                if (gap < step / 4)
                    return true;
                answer = false;
            }

            return answer;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or System.Reflection.TargetException)
        {
            return null;
        }
    }

    /// <summary>
    /// 钣金折弯切线（1.17.0，判据见 <see cref="SheetMetal"/>）：直边旁边贴着一张轴线与它平行的圆柱面，且那张面属于钣金体。
    /// 返回 null = 不是；否则 Radius 是那张圆柱面的半径（模型长度，定折弯区用），Hidden = 该去掉——
    /// 另一侧的平面侧着（法向垂直视线）时这条线就是折边侧着的外轮廓，Hidden 为 false 要留着。
    /// 直线、圆柱、面法向都在零件坐标里，视线用 <see cref="Frame"/>（零件视图；装配视图的组件变换没算）。
    /// 读不出来一律当不是——宁可多标一条，不能把真边丢了。
    /// </summary>
    public (bool Hidden, double Radius)? BendTangent(object edge)
    {
        try
        {
            if (CurveOf(edge).Curve is not { } curve)
                return null;
            var line = api.CallDoubles(curve, "ICurve", "get_LineParams");
            if (line.Length < 6)
                return null;
            var direction = new ModelDirection(line[3], line[4], line[5]);
            double? radius = null;
            var edgeOn = false;
            foreach (var face in api.CallArray(edge, "IEdge", "GetTwoAdjacentFaces2"))
            {
                if (face is null || api.Call(face, "IFace2", "GetSurface") is not { } surface)
                    continue;
                if (api.CallBool(surface, "ISurface", "IsPlane"))
                {
                    var n = api.CallDoubles(face, "IFace2", "get_Normal");
                    edgeOn |= n.Length >= 3 && SheetMetal.EdgeOn(new ModelDirection(n[0], n[1], n[2]), Frame.Normal);
                    continue;
                }

                if (!api.CallBool(surface, "ISurface", "IsCylinder"))
                    continue;
                var cylinder = api.CallDoubles(surface, "ISurface", "get_CylinderParams");
                if (cylinder.Length < 7 || !SheetMetal.AlongAxis(direction, new ModelDirection(cylinder[3], cylinder[4], cylinder[5])))
                    continue;
                if (api.Call(face, "IFace2", "GetBody") is { } body && api.CallBool(body, "IBody2", "IsSheetMetal"))
                    radius = Math.Max(radius ?? 0, cylinder[6]);
            }

            return radius is { } r ? (!edgeOn, r) : null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or System.Reflection.TargetException
                                       or MissingMethodException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// 不是直边也不是孔的边近似成的线段（图纸坐标）：圆弧 = 起点 → 弧中点 → 终点，整圆 = 外接八边形，
    /// 别的曲线 = 弦。弧中点用两个角平分方向各问一次 <c>GetClosestPointOn</c>，落在弧上的那个就是。读不出返回空。
    /// </summary>
    public IReadOnlyList<SheetSegment> CurveOutline(object edge)
    {
        try
        {
            var transforms = Transforms(edge);
            SheetPoint Sheet(double[] p)
            {
                var q = ToSheet(transforms, true, p[0], p[1], p[2]);
                return new SheetPoint(q[0], q[1]);
            }

            var circle = Circle(edge);
            if (api.Call(edge, "IEdge", "GetStartVertex") is not { } start
                || api.Call(edge, "IEdge", "GetEndVertex") is not { } end)
            {
                return circle is null
                    ? []
                    : ClearancePlanner.Octagon(Sheet([circle[0], circle[1], circle[2]]), circle[6] * Scale).ToList();
            }

            var s = api.CallDoubles(start, "IVertex", "GetPoint");
            var e = api.CallDoubles(end, "IVertex", "GetPoint");
            if (s.Length < 3 || e.Length < 3)
                return [];
            var (a, b) = (Sheet(s), Sheet(e));
            if (circle is not null && ArcMiddle(edge, circle, s, e) is { } middle)
            {
                var m = Sheet(middle);
                return [new SheetSegment(a.X, a.Y, m.X, m.Y), new SheetSegment(m.X, m.Y, b.X, b.Y)];
            }

            return [new SheetSegment(a.X, a.Y, b.X, b.Y)];
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException or System.Reflection.TargetException)
        {
            return [];
        }
    }

    /// <summary>圆弧的中点（零件坐标）：圆心沿弦中点方向的正反两侧各取一点问边上最近点，贴在弧上的那个。</summary>
    private double[]? ArcMiddle(object edge, double[] circle, double[] s, double[] e)
    {
        var (cx, cy, cz, r) = (circle[0], circle[1], circle[2], circle[6]);
        var (mx, my, mz) = ((s[0] + e[0]) / 2 - cx, (s[1] + e[1]) / 2 - cy, (s[2] + e[2]) / 2 - cz);
        var length = Math.Sqrt(mx * mx + my * my + mz * mz);
        if (length < r * 1e-6)
        {
            // 恰好半圈：弦过圆心，方向取轴 × 弦。
            var (ax, ay, az) = (circle[3], circle[4], circle[5]);
            var (dx, dy, dz) = (e[0] - s[0], e[1] - s[1], e[2] - s[2]);
            (mx, my, mz) = (ay * dz - az * dy, az * dx - ax * dz, ax * dy - ay * dx);
            length = Math.Sqrt(mx * mx + my * my + mz * mz);
            if (length <= 0)
                return null;
        }

        foreach (var sign in new[] { 1.0, -1.0 })
        {
            double[] probe = [cx + sign * r * mx / length, cy + sign * r * my / length, cz + sign * r * mz / length];
            var closest = api.CallDoubles(edge, "IEdge", "GetClosestPointOn", probe[0], probe[1], probe[2]);
            if (closest.Length >= 3
                && Math.Abs(closest[0] - probe[0]) + Math.Abs(closest[1] - probe[1]) + Math.Abs(closest[2] - probe[2]) < r * 1e-4)
                return probe;
        }

        return null;
    }

    /// <summary>视图比例（图纸长度 / 模型长度）。读一次记下。</summary>
    public double Scale => _scale ??= api.Call(view, "IView", "get_ScaleDecimal") is { } value ? Convert.ToDouble(value) : 1.0;

    private double? _scale;

    /// <summary>
    /// 视图草图点 → 图纸的 <c>GetXform</c>（[视图位置 x, y, 比例, …]）；视图旋转过时为 null（<c>GetXform</c> 不含旋转）。读一次记下（1.8.0：
    /// 读尺寸时每个附着对象都要用，逐个去问 SolidWorks 太慢）。
    /// </summary>
    public double[]? SketchXform
    {
        get
        {
            if (!_xformRead)
            {
                var angle = api.Call(view, "IView", "get_Angle") is { } value ? Convert.ToDouble(value) : 0.0;
                var xform = api.CallDoubles(view, "IView", "GetXform");
                _xform = Math.Abs(angle) > 1e-9 || xform.Length < 3 ? null : xform;
                _xformRead = true;
            }

            return _xform;
        }
    }

    private double[]? _xform;
    private bool _xformRead;

    /// <summary>视图所引用模型（顶层）坐标系里的一个点，变到图纸上。</summary>
    public SheetPoint ModelPointToSheet(double x, double y, double z)
    {
        var point = ToSheet([_viewTransform], true, x, y, z);
        return new SheetPoint(point[0], point[1]);
    }

    /// <summary>
    /// 视图的朝向（1.8.1）：把模型三根轴变到图纸上得 v_x、v_y、v_z（带比例），它们是旋转矩阵的三列，
    /// 图纸 X / Y / Z 在模型里的方向就是三行。读一次记下。
    /// </summary>
    public ViewFrame Frame => _frame ??= ReadFrame();

    private ViewFrame? _frame;

    private ViewFrame ReadFrame()
    {
        var vx = ToSheet([_viewTransform], false, 1, 0, 0);
        var vy = ToSheet([_viewTransform], false, 0, 1, 0);
        var vz = ToSheet([_viewTransform], false, 0, 0, 1);
        ModelDirection Row(int i) => new ModelDirection(vx[i], vy[i], vz[i]).Normalized();
        return new ViewFrame(Row(0), Row(1), Row(2), ModelPointToSheet(0, 0, 0), Scale);
    }

    /// <summary><c>CircleParams</c>：圆心 xyz、轴向 xyz、半径。不是圆返回 null。</summary>
    private double[]? Circle(object edge)
    {
        return CurveOf(edge).Circle;
    }

    /// <summary>边的曲线与圆参数，每条边只问一遍（一次识别里一条边要问好几次）。</summary>
    private (object? Curve, double[]? Circle) CurveOf(object edge)
    {
        if (_curves.TryGetValue(edge, out var known))
            return known;
        var curve = api.Call(edge, "IEdge", "GetCurve");
        double[]? circle = null;
        if (curve is not null && api.CallBool(curve, "ICurve", "IsCircle"))
        {
            var parameters = api.CallDoubles(curve, "ICurve", "get_CircleParams");
            circle = parameters.Length >= 7 ? parameters : null;
        }

        return _curves[edge] = (curve, circle);
    }

    /// <summary>
    /// 圆边旁边的孔壁（<c>IFace2</c>），没有就是 null。在圆上取一点（它也在圆柱面上），求那里的曲面法向，
    /// 与径向比方向，再按 <c>FaceInSurfaceSense</c> 换算成面法向——判据见
    /// <see cref="HoleCalloutPlanner.IsHoleWall"/>。
    /// </summary>
    private object? HoleWall(object edge, double[] circle)
    {
        var (ux, uy, uz) = HoleCalloutPlanner.Perpendicular(circle[3], circle[4], circle[5]);
        var radius = circle[6];
        var px = circle[0] + radius * ux;
        var py = circle[1] + radius * uy;
        var pz = circle[2] + radius * uz;
        foreach (var face in api.CallArray(edge, "IEdge", "GetTwoAdjacentFaces2"))
        {
            if (api.Call(face, "IFace2", "GetSurface") is not { } surface
                || !api.CallBool(surface, "ISurface", "IsCylinder"))
                continue;
            var cylinder = api.CallDoubles(surface, "ISurface", "get_CylinderParams");
            var evaluated = api.CallDoubles(surface, "ISurface", "EvaluateAtPoint", px, py, pz);
            if (cylinder.Length < 7 || evaluated.Length < 3)
                continue;
            // 径向取 radius·u 的方向即可；只看点积正负。
            var dot = evaluated[0] * ux + evaluated[1] * uy + evaluated[2] * uz;
            if (HoleCalloutPlanner.IsHoleWall(radius, cylinder[6], api.CallBool(face, "IFace2", "FaceInSurfaceSense"), dot))
                return face;
        }

        return null;
    }

    private List<double[]> Transforms(object edge)
    {
        var transforms = new List<double[]>(2);
        if (Owner(edge) is { } component)
        {
            if (!_componentTransforms.TryGetValue(component, out var data))
            {
                data = api.Call(component, "IComponent2", "get_Transform2") is { } componentTransform
                    ? api.CallDoubles(componentTransform, "IMathTransform", "get_ArrayData")
                    : [];
                _componentTransforms[component] = data;
            }

            if (data.Length >= 13)
                transforms.Add(data);
        }

        transforms.Add(_viewTransform);
        return transforms;
    }

    /// <summary>同 <c>IMathPoint/IMathVector.MultiplyTransform</c>（真机逐视图核对到 6 位小数）：p' = 比例·p·R + 平移，向量不加平移。</summary>
    internal static double[] ToSheet(IReadOnlyList<double[]> transforms, bool point, double x, double y, double z)
    {
        foreach (var t in transforms)
        {
            var (nx, ny, nz) = (
                t[12] * (t[0] * x + t[3] * y + t[6] * z),
                t[12] * (t[1] * x + t[4] * y + t[7] * z),
                t[12] * (t[2] * x + t[5] * y + t[8] * z));
            (x, y, z) = point ? (nx + t[9], ny + t[10], nz + t[11]) : (nx, ny, nz);
        }

        return [x, y, z];
    }
}
