using System.Diagnostics;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「孔标注」：点一个工程图视图，把视图里看得见的孔全部加上孔标注。
/// </summary>
/// <remarks>
/// <para>
/// 选视图两种顺序都行：按按钮前已经在 SolidWorks 里选中了视图（或视图里的任何东西），直接用它；
/// 没选就等用户去点，最多 <see cref="SelectionTimeout"/>。
/// </para>
/// <para>
/// 「全部类型的孔」指：异形孔向导的各种孔（柱形沉头、锥形沉头、螺纹孔、直孔……）和手工切出来的圆孔。
/// 判据是几何而不是特征——完整一圈的圆边、旁边贴着一张内凹的同半径圆柱面、轴线正对图纸。
/// 所以导入件、镜像件、阵列出来的孔一样认得；槽口两端的半圆不是整圈，不算孔。
/// </para>
/// <para>
/// 已经有孔标注的孔跳过，连点两次不会出双份。
/// </para>
/// </remarks>
internal static class HoleCallout
{
    public static readonly TimeSpan SelectionTimeout = TimeSpan.FromSeconds(60);

    public static QuickCommand Command { get; } = new(
        Key: "hole-callout",
        CommandName: StrenuaIdentity.Domain + ".hole.callout",
        Title: "孔标注",
        Usage: "在工程图里点一个视图（先点后按、先按后点都行，60 秒内），该视图里看得见的孔全部加孔标注；已有标注的孔跳过。",
        Run: Run);

    // swDocumentTypes_e / swSelectType_e / swDrawingViewTypes_e / swViewEntityType_e
    private const int DocumentDrawing = 3;
    private const int DocumentAssembly = 2;
    private const int SelectDrawingView = 12;
    private const int SelectSheet = 19;
    private const int SelectEdge = 1;
    private const int ViewSheet = 1;
    private const int ViewEntityEdge = 1;

    private static QuickOutcome Run(QuickCommandContext context)
    {
        var api = context.Api;
        var document = context.Session.ActiveDocument()
            ?? throw new QuickCommandException("SolidWorks 里没有打开的文档。请先打开工程图。");
        if (api.CallInt(document, "IModelDoc2", "GetType") != DocumentDrawing)
            throw new QuickCommandException($"当前活动文档「{api.CallString(document, "IModelDoc2", "GetTitle")}」不是工程图。请切到工程图再按。");

        var view = WaitForView(context, document);
        var viewName = api.CallString(view, "IView", "get_Name");
        var model = api.Call(view, "IView", "get_ReferencedDocument")
            ?? throw new QuickCommandException($"视图「{viewName}」没有引用模型（空视图，或模型是轻化/未加载状态）。");

        context.SetState("识别孔");
        context.Report($"孔标注：正在识别视图「{viewName}」里的孔。");
        var geometry = new ViewGeometry(api, context.Session.Application, view);
        var edges = new List<object>();
        var candidates = new List<HoleEdge>();
        foreach (var component in VisibleComponents(api, view, model))
        {
            foreach (var edge in api.CallArray(view, "IView", "GetVisibleEntities2", component, ViewEntityEdge))
            {
                context.Cancellation.ThrowIfCancellationRequested();
                if (geometry.TryReadHole(edge) is not { } hole)
                    continue;
                candidates.Add(hole with { Index = edges.Count });
                edges.Add(edge);
            }
        }

        var plan = HoleCalloutPlanner.Plan(candidates, ExistingCalloutCenters(api, geometry, view));
        if (plan.HoleCount == 0)
            return QuickOutcome.Ok($"视图「{viewName}」里没有正对图纸的孔，没有加标注。");
        if (plan.Targets.Count == 0)
            return QuickOutcome.Ok($"视图「{viewName}」里 {plan.HoleCount} 个孔都已有孔标注，没有新加。");

        context.SetState("加标注");
        context.Report($"孔标注：视图「{viewName}」认出 {plan.HoleCount} 个孔，开始为 {plan.Targets.Count} 个加标注。");
        var added = 0;
        var failed = 0;
        _ = api.Call(document, "IDrawingDoc", "ActivateView", viewName);
        try
        {
            foreach (var target in plan.Targets)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                api.Call(document, "IModelDoc2", "ClearSelection2", true);
                var created = api.CallBool(view, "IView", "SelectEntity", edges[target.EdgeIndex], false)
                    && api.Call(document, "IDrawingDoc", "AddHoleCallout2", target.Placement.X, target.Placement.Y, 0.0) is not null;
                if (created)
                    added++;
                else
                    failed++;
            }
        }
        finally
        {
            api.Call(document, "IModelDoc2", "ClearSelection2", true);
            api.Call(document, "IModelDoc2", "GraphicsRedraw2");
        }

        var message = $"视图「{viewName}」：新加 {added} 个孔标注"
            + (plan.AlreadyAnnotated > 0 ? $"，{plan.AlreadyAnnotated} 个孔已有标注跳过" : string.Empty)
            + (failed > 0 ? $"，{failed} 个 SolidWorks 没有接受" : string.Empty)
            + "。";
        return added == 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }

    /// <summary>
    /// 当前选择里有视图就用它；没有就等用户去点。点在视图里的任何东西（边、尺寸）都算点了这个视图，
    /// 只点在图纸空白处不算。
    /// </summary>
    private static object WaitForView(QuickCommandContext context, object document)
    {
        var api = context.Api;
        var selection = api.Call(document, "IModelDoc2", "get_SelectionManager")
            ?? throw new QuickCommandException("SolidWorks 没有返回选择管理器。");
        if (SelectedView(api, selection) is { } preselected)
            return preselected;

        context.SetState("等待点选视图");
        context.Report($"孔标注：请在 SolidWorks 里点一下要标注的视图（{SelectionTimeout.TotalSeconds:0} 秒内）。");
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < SelectionTimeout)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            Thread.Sleep(250);
            if (SelectedView(api, selection) is { } picked)
                return picked;
        }

        throw new QuickCommandException($"{SelectionTimeout.TotalSeconds:0} 秒内没有点选视图，孔标注已放弃。");
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

    /// <summary>视图里已有孔标注各自指着的孔心。</summary>
    private static List<SheetPoint> ExistingCalloutCenters(SolidWorksApi api, ViewGeometry geometry, object view)
    {
        var centers = new List<SheetPoint>();
        var dimension = api.Call(view, "IView", "GetFirstDisplayDimension5");
        while (dimension is not null)
        {
            if (api.CallBool(dimension, "IDisplayDimension", "IsHoleCallout")
                && api.Call(dimension, "IDisplayDimension", "GetAnnotation") is { } annotation)
            {
                var types = api.CallArray(annotation, "IAnnotation", "GetAttachedEntityTypes")
                    .Select(Convert.ToInt32)
                    .ToArray();
                var entities = api.CallArray(annotation, "IAnnotation", "GetAttachedEntities3");
                for (var i = 0; i < entities.Length && i < types.Length; i++)
                {
                    if (types[i] == SelectEdge && geometry.TryReadCircleCenter(entities[i]) is { } center)
                        centers.Add(center);
                }
            }

            dimension = api.Call(dimension, "IDisplayDimension", "GetNext5");
        }

        return centers;
    }

    /// <summary>
    /// 把模型边变到图纸上。几何一律在零件自己的坐标系里返回：
    /// 装配视图里先乘组件变换到装配空间，再乘视图的模型→图纸变换。
    /// </summary>
    private sealed class ViewGeometry(SolidWorksApi api, object application, object view)
    {
        private readonly object _math = api.Call(application, "ISldWorks", "GetMathUtility")
            ?? throw new QuickCommandException("SolidWorks 没有返回 MathUtility。");
        private readonly object _viewTransform = api.Call(view, "IView", "get_ModelToViewTransform")
            ?? throw new QuickCommandException("SolidWorks 没有返回视图变换。");

        /// <summary>是孔边就返回它在图纸上的圆心与半径（<c>Index</c> 由调用方补）。</summary>
        public HoleEdge? TryReadHole(object edge)
        {
            if (Circle(edge) is not { } circle)
                return null;
            // 整圈的边没有端点；槽口两端的半圆、被切掉一截的孔口都有。
            if (api.Call(edge, "IEdge", "GetStartVertex") is not null)
                return null;
            if (!BesideHoleWall(edge, circle[6]))
                return null;

            var transforms = Transforms(edge);
            var axis = ToSheet(transforms, "CreateVector", "IMathVector", circle[3], circle[4], circle[5]);
            if (!HoleCalloutPlanner.FacesViewer(axis[0], axis[1], axis[2]))
                return null;

            var center = ToSheet(transforms, "CreatePoint", "IMathPoint", circle[0], circle[1], circle[2]);
            var scale = api.Call(view, "IView", "get_ScaleDecimal") is { } value ? Convert.ToDouble(value) : 1.0;
            return new HoleEdge(0, center[0], center[1], circle[6] * scale);
        }

        public SheetPoint? TryReadCircleCenter(object edge)
        {
            if (Circle(edge) is not { } circle)
                return null;
            var center = ToSheet(Transforms(edge), "CreatePoint", "IMathPoint", circle[0], circle[1], circle[2]);
            return new SheetPoint(center[0], center[1]);
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

        private bool BesideHoleWall(object edge, double radius)
        {
            foreach (var face in api.CallArray(edge, "IEdge", "GetTwoAdjacentFaces2"))
            {
                if (api.Call(face, "IFace2", "GetSurface") is not { } surface
                    || !api.CallBool(surface, "ISurface", "IsCylinder"))
                    continue;
                var cylinder = api.CallDoubles(surface, "ISurface", "get_CylinderParams");
                if (cylinder.Length >= 7
                    && HoleCalloutPlanner.IsHoleWall(radius, cylinder[6], api.CallBool(face, "IFace2", "FaceInSurfaceSense")))
                    return true;
            }

            return false;
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
