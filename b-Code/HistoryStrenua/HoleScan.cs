using System.Diagnostics;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>一次识别的结果：用户点的那个视图，以及视图里的候选孔边。</summary>
/// <param name="Document">活动工程图（<c>IModelDoc2</c> / <c>IDrawingDoc</c>）。</param>
/// <param name="View">视图（<c>IView</c>）。</param>
/// <param name="ViewName">视图名。</param>
/// <param name="Geometry">把模型边、模型点变到图纸上的工具。</param>
/// <param name="Edges">候选孔边本身，<see cref="HoleEdge.Index"/> 是这里的下标。</param>
/// <param name="Candidates">候选孔边在图纸上的样子（同心的尚未合并）。</param>
/// <param name="LineEdges">视图里的直边本身（只在要求收集直边时有）。</param>
/// <param name="Lines">直边在图纸上的样子，与 <paramref name="LineEdges"/> 一一对应。</param>
internal sealed record ScannedView(
    object Document,
    object View,
    string ViewName,
    HoleScan.ViewGeometry Geometry,
    IReadOnlyList<object> Edges,
    IReadOnlyList<HoleEdge> Candidates,
    IReadOnlyList<object> LineEdges,
    IReadOnlyList<SheetSegment> Lines);

/// <summary>
/// 孔类快捷指令共用的前半段：确认活动文档是工程图、取用户点的视图、认出视图里全部的孔。
/// </summary>
/// <remarks>
/// <para>
/// 选视图两种顺序都行：执行前已经在 SolidWorks 里选中了视图（或视图里的任何东西），直接用它；
/// 没选就等用户去点，最多 <see cref="SelectionTimeout"/>。
/// </para>
/// <para>
/// 「全部类型的孔」指：异形孔向导的各种孔（柱形沉头、锥形沉头、螺纹孔、直孔……）和手工切出来的圆孔。
/// 判据是几何而不是特征——完整一圈的圆边、旁边贴着一张内凹的同半径圆柱面、轴线正对图纸。
/// 所以导入件、镜像件、阵列出来的孔一样认得。
/// </para>
/// <para>
/// 腰型孔（1.3.0 起）：两端恰好半圈的圆弧、同样贴着内凹的同半径圆柱面，各当一个候选，再由
/// <see cref="SlotPlanner"/> 两两配对；配不上的半圆（开口槽口的端头）仍不算孔。
/// </para>
/// </remarks>
internal static class HoleScan
{
    public static readonly TimeSpan SelectionTimeout = TimeSpan.FromSeconds(60);

    // swDocumentTypes_e / swSelectType_e / swDrawingViewTypes_e / swViewEntityType_e
    private const int DocumentDrawing = 3;
    private const int DocumentAssembly = 2;
    private const int SelectDrawingView = 12;
    private const int SelectSheet = 19;
    private const int ViewSheet = 1;
    private const int ViewEntityEdge = 1;

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="title">指令名，写进进度与失败消息，如「孔标注」。</param>
    /// <param name="withLines">同时收集视图里的直边（孔位尺寸找基准用）。</param>
    public static ScannedView Scan(QuickCommandContext context, string title, bool withLines = false)
    {
        var api = context.Api;
        var document = context.Session.ActiveDocument()
            ?? throw new QuickCommandException("SolidWorks 里没有打开的文档。请先打开工程图。");
        if (api.CallInt(document, "IModelDoc2", "GetType") != DocumentDrawing)
            throw new QuickCommandException($"当前活动文档「{api.CallString(document, "IModelDoc2", "GetTitle")}」不是工程图。请切到工程图再按。");

        var view = WaitForView(context, document, title);
        var viewName = api.CallString(view, "IView", "get_Name");
        var model = api.Call(view, "IView", "get_ReferencedDocument")
            ?? throw new QuickCommandException($"视图「{viewName}」没有引用模型（空视图，或模型是轻化/未加载状态）。");

        context.SetState("识别孔");
        context.Report($"{title}：正在识别视图「{viewName}」里的孔。");
        var geometry = new ViewGeometry(api, context.Session.Application, view);
        var edges = new List<object>();
        var candidates = new List<HoleEdge>();
        var lineEdges = new List<object>();
        var lines = new List<SheetSegment>();
        foreach (var component in VisibleComponents(api, view, model))
        {
            foreach (var edge in api.CallArray(view, "IView", "GetVisibleEntities2", component, ViewEntityEdge))
            {
                context.Cancellation.ThrowIfCancellationRequested();
                if (geometry.TryReadHole(edge) is { } hole)
                {
                    candidates.Add(hole with { Index = edges.Count });
                    edges.Add(edge);
                }
                else if (withLines && geometry.TryReadLine(edge) is { } line)
                {
                    lines.Add(line);
                    lineEdges.Add(edge);
                }
            }
        }

        return new ScannedView(document, view, viewName, geometry, edges, candidates, lineEdges, lines);
    }

    /// <summary>
    /// 当前选择里有视图就用它；没有就等用户去点。点在视图里的任何东西（边、尺寸）都算点了这个视图，
    /// 只点在图纸空白处不算。
    /// </summary>
    private static object WaitForView(QuickCommandContext context, object document, string title)
    {
        var api = context.Api;
        var selection = api.Call(document, "IModelDoc2", "get_SelectionManager")
            ?? throw new QuickCommandException("SolidWorks 没有返回选择管理器。");
        if (SelectedView(api, selection) is { } preselected)
            return preselected;

        context.SetState("等待点选视图");
        context.Report($"{title}：请在 SolidWorks 里点一下要处理的视图（{SelectionTimeout.TotalSeconds:0} 秒内）。");
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < SelectionTimeout)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            Thread.Sleep(250);
            if (SelectedView(api, selection) is { } picked)
                return picked;
        }

        throw new QuickCommandException($"{SelectionTimeout.TotalSeconds:0} 秒内没有点选视图，{title}已放弃。");
    }

    private static object? SelectedView(SolidWorksApi api, object selection)
    {
        var count = api.CallInt(selection, "ISelectionMgr", "GetSelectedObjectCount2", -1);
        for (var index = 1; index <= count; index++)
        {
            var type = api.CallInt(selection, "ISelectionMgr", "GetSelectedObjectType3", index, -1);
            var view = type switch
            {
                SelectDrawingView => api.Call(selection, "ISelectionMgr", "GetSelectedObject6", index, -1),
                SelectSheet => null,
                _ => api.Call(selection, "ISelectionMgr", "GetSelectedObjectsDrawingView2", index, -1),
            };
            if (view is not null && api.CallInt(view, "IView", "get_Type") != ViewSheet)
                return view;
        }

        return null;
    }

    /// <summary>零件视图只有一个「无组件」；装配视图逐个可见组件取边。</summary>
    private static IEnumerable<object?> VisibleComponents(SolidWorksApi api, object view, object model)
    {
        if (api.CallInt(model, "IModelDoc2", "GetType") != DocumentAssembly)
            return [null];
        return api.CallArray(view, "IView", "GetVisibleComponents");
    }

    /// <summary>
    /// 把模型边变到图纸上。几何一律在零件自己的坐标系里返回：
    /// 装配视图里先乘组件变换到装配空间，再乘视图的模型→图纸变换。
    /// </summary>
    internal sealed class ViewGeometry(SolidWorksApi api, object application, object view)
    {
        private readonly object _math = api.Call(application, "ISldWorks", "GetMathUtility")
            ?? throw new QuickCommandException("SolidWorks 没有返回 MathUtility。");
        private readonly object _viewTransform = api.Call(view, "IView", "get_ModelToViewTransform")
            ?? throw new QuickCommandException("SolidWorks 没有返回视图变换。");

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
            var axis = ToSheet(transforms, "CreateVector", "IMathVector", circle[3], circle[4], circle[5]);
            if (!HoleCalloutPlanner.FacesViewer(axis[0], axis[1], axis[2]))
                return null;

            var center = ToSheet(transforms, "CreatePoint", "IMathPoint", circle[0], circle[1], circle[2]);
            var (bx, by) = (0.0, 0.0);
            if (bulge is not null)
            {
                // 轴线正对图纸，鼓出方向就在图纸平面里；向量变换带着比例，重新归一。
                var sheet = ToSheet(transforms, "CreateVector", "IMathVector", bulge[0], bulge[1], bulge[2]);
                var length = Math.Sqrt(sheet[0] * sheet[0] + sheet[1] * sheet[1]);
                if (length <= 0)
                    return null;
                (bx, by) = (sheet[0] / length, sheet[1] / length);
            }

            return new HoleEdge(0, center[0], center[1], circle[6] * Scale, Kind(edge, wall), bx, by);
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
        /// </summary>
        private string Kind(object edge, object wall)
        {
            var component = api.Call(edge, "IEntity", "GetComponent") is { } owner
                ? api.CallString(owner, "IComponent2", "get_Name2")
                : string.Empty;
            string feature;
            try
            {
                feature = api.Call(wall, "IFace2", "GetFeature") is { } owning
                    ? api.CallString(owning, "IFeature", "get_Name")
                    : string.Empty;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
            {
                feature = string.Empty;
            }

            return component + "/" + feature;
        }

        /// <summary>圆边的圆心在图纸上的位置；不是圆边返回 null。</summary>
        public SheetPoint? TryReadCircleCenter(object edge)
        {
            if (Circle(edge) is not { } circle)
                return null;
            var center = ToSheet(Transforms(edge), "CreatePoint", "IMathPoint", circle[0], circle[1], circle[2]);
            return new SheetPoint(center[0], center[1]);
        }

        /// <summary>直边在图纸上的两个端点；不是直边（或没有端点）返回 null。</summary>
        public SheetSegment? TryReadLine(object edge)
        {
            var curve = api.Call(edge, "IEdge", "GetCurve");
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
            var p = ToSheet(transforms, "CreatePoint", "IMathPoint", a[0], a[1], a[2]);
            var q = ToSheet(transforms, "CreatePoint", "IMathPoint", b[0], b[1], b[2]);
            return new SheetSegment(p[0], p[1], q[0], q[1]);
        }

        /// <summary>视图比例（图纸长度 / 模型长度）。</summary>
        public double Scale => api.Call(view, "IView", "get_ScaleDecimal") is { } value ? Convert.ToDouble(value) : 1.0;

        /// <summary>视图所引用模型（顶层）坐标系里的一个点，变到图纸上。</summary>
        public SheetPoint ModelPointToSheet(double x, double y, double z)
        {
            var point = ToSheet([_viewTransform], "CreatePoint", "IMathPoint", x, y, z);
            return new SheetPoint(point[0], point[1]);
        }

        /// <summary><c>CircleParams</c>：圆心 xyz、轴向 xyz、半径。不是圆返回 null。</summary>
        private double[]? Circle(object edge)
        {
            var curve = api.Call(edge, "IEdge", "GetCurve");
            if (curve is null || !api.CallBool(curve, "ICurve", "IsCircle"))
                return null;
            var circle = api.CallDoubles(curve, "ICurve", "get_CircleParams");
            return circle.Length >= 7 ? circle : null;
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

        private List<object> Transforms(object edge)
        {
            var transforms = new List<object>(2);
            if (api.Call(edge, "IEntity", "GetComponent") is { } component
                && api.Call(component, "IComponent2", "get_Transform2") is { } componentTransform)
                transforms.Add(componentTransform);
            transforms.Add(_viewTransform);
            return transforms;
        }

        private double[] ToSheet(List<object> transforms, string create, string kind, double x, double y, double z)
        {
            var value = api.Call(_math, "IMathUtility", create, new[] { x, y, z })
                ?? throw new QuickCommandException("SolidWorks 没有创建出数学对象。");
            foreach (var transform in transforms)
            {
                value = api.Call(value, kind, "MultiplyTransform", transform)
                    ?? throw new QuickCommandException("SolidWorks 坐标变换失败。");
            }

            return api.CallDoubles(value, kind, "get_ArrayData");
        }
    }
}
