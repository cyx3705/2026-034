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
/// <param name="Curves">
/// 其余不是孔的边（圆角、整圆凸台、样条……）近似成的线段（只在要求收集直边时有，1.8.0）：
/// 圆弧取起点 → 弧中点 → 终点两段，整圆取外接八边形，别的曲线取弦。外轮廓判定拿它挡射线。
/// </param>
/// <param name="ArcEdges">圆角弧的边本身（只在要求收集圆角时有，1.9.0）。</param>
/// <param name="FilletArcs">圆角弧在图纸上的样子，与 <paramref name="ArcEdges"/> 一一对应。</param>
/// <param name="ChamferEdges">倒角斜边本身（只在要求收集倒角时有，1.10.0）；它们同时也在直边里。</param>
/// <param name="ChamferItems">倒角斜边在图纸上的样子，与 <paramref name="ChamferEdges"/> 一一对应。</param>
/// <param name="CircleArcs">收集圆角时去掉的「其实是圆 / 孔」的弧段数（1.12.0，见 <see cref="FilletPlanner.WithoutCircles"/>）。</param>
/// <param name="BendLines">收集直边时去掉的钣金折弯处的线条数（折弯切线 + 折弯区里的短边，1.17.0，见 <see cref="SheetMetal"/>）。</param>
/// <param name="CurveEdges">曲线边本身（1.32.0，SVG 编号 C 的下标），与 <paramref name="CurveShapes"/> 一一对应。</param>
/// <param name="CurveShapes">曲线边在图纸上的样子（1.32.0）。</param>
internal sealed record ScannedView(
    object Document,
    object View,
    string ViewName,
    ViewGeometry Geometry,
    IReadOnlyList<object> Edges,
    IReadOnlyList<HoleEdge> Candidates,
    IReadOnlyList<object> LineEdges,
    IReadOnlyList<SheetSegment> Lines,
    IReadOnlyList<SheetSegment>? Curves = null,
    IReadOnlyList<object>? ArcEdges = null,
    IReadOnlyList<FilletArc>? FilletArcs = null,
    IReadOnlyList<object>? ChamferEdges = null,
    IReadOnlyList<ChamferEdge>? ChamferItems = null,
    int CircleArcs = 0,
    int BendLines = 0,
    IReadOnlyList<object>? CurveEdges = null,
    IReadOnlyList<SheetCurve>? CurveShapes = null)
{
    /// <summary>倒角斜边（只在要求收集倒角时有，1.10.0），<see cref="ChamferEdge.Index"/> 是 <see cref="ChamferEdges"/> 的下标。</summary>
    public IReadOnlyList<ChamferEdge> Chamfers => ChamferItems ?? [];

    /// <summary>曲线边近似成的线段（没有时为空表）。</summary>
    public IReadOnlyList<SheetSegment> CurveSegments => Curves ?? [];

    /// <summary>圆角弧（只在要求收集圆角时有，1.9.0），<see cref="FilletArc.Index"/> 是 <see cref="ArcEdges"/> 的下标。</summary>
    public IReadOnlyList<FilletArc> Arcs => FilletArcs ?? [];
}

/// <summary>
/// 孔类快捷指令共用的前半段：确认活动文档是工程图、取用户点的视图、认出视图里全部的孔。
/// </summary>
/// <remarks>
/// <para>
/// 选视图两种顺序都行：执行前已经在 SolidWorks 里选中了视图（或视图里的任何东西），直接用它；
/// 没选就等用户去点，最多 <see cref="DrawingAccess.SelectionTimeout"/>。
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
    private const int ViewEntityEdge = 1;

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="title">指令名，写进进度与失败消息，如「孔标注」。</param>
    /// <param name="withLines">同时收集视图里的直边（孔位尺寸找基准用）。</param>
    /// <param name="view">直接处理这个视图，不看选择、不等点选（「孔标注全流程」逐个视图调用时给）。</param>
    /// <param name="withArcs">同时收集圆角弧（1.9.0「圆角标注」用；要 <paramref name="withLines"/>）。</param>
    /// <param name="withChamfers">同时收集倒角斜边（1.10.0「倒角标注」用；要 <paramref name="withLines"/>）。</param>
    /// <param name="quiet">不报进度、不改状态（1.15.0：顺带读同一页别的视图时用，<see cref="HoleCoverage"/>）。</param>
    public static ScannedView Scan(QuickCommandContext context, string title, bool withLines = false, object? view = null, bool withArcs = false,
        bool withChamfers = false, bool quiet = false)
    {
        var api = context.Api;
        var document = DrawingAccess.ActiveDrawing(context);
        view ??= DrawingAccess.WaitForView(context, document, title);
        var viewName = api.CallString(view, "IView", "get_Name");
        var model = api.Call(view, "IView", "get_ReferencedDocument")
            ?? throw new QuickCommandException($"视图「{viewName}」没有引用模型（空视图，或模型是轻化/未加载状态）。");

        var what = withChamfers ? "倒角" : withArcs ? "圆角" : "孔";
        if (!quiet)
        {
            context.SetState("识别" + what);
            context.Report($"{title}：正在识别视图「{viewName}」里的{what}。");
        }

        // 一条指令里同一视图只读一遍边：视图没挪没缩、图纸没重建，边就没变；每次都按全部类别收，谁要什么取什么。
        var cacheKey = api.CallString(document, "IModelDoc2", "GetTitle") + "|" + viewName;
        var transform = ViewGeometry.TransformData(api, view);
        var scans = context.Scoped<ScanCache>();
        if (scans.TryGetValue(cacheKey, out var cached) && cached.Generation == api.Generation && cached.Transform.SequenceEqual(transform))
            return cached.Scan;
        (withLines, withArcs, withChamfers) = (true, true, true);

        var geometry = new ViewGeometry(api, view);
        var edges = new List<object>();
        var candidates = new List<HoleEdge>();
        var lineEdges = new List<object>();
        var lines = new List<SheetSegment>();
        var curves = new List<SheetSegment>();
        var curveEdges = new List<object>();
        var curveShapes = new List<SheetCurve>();
        var arcEdges = new List<object>();
        var arcs = new List<FilletArc>();
        var chamferEdges = new List<object>();
        var chamfers = new List<ChamferEdge>();
        var bendLines = 0;
        var bendZone = 0.0;
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
                    // 1.17.0：钣金折弯切线不是零件边界，谁都不该量到它；侧着的折边外切线是外轮廓，留着（SheetMetal）。
                    if (geometry.BendTangent(edge) is { } bend)
                    {
                        bendZone = Math.Max(bendZone, bend.Radius);
                        if (bend.Hidden)
                        {
                            bendLines++;
                            continue;
                        }
                    }

                    lines.Add(line);
                    lineEdges.Add(edge);
                    if (withChamfers && geometry.TryReadChamfer(edge, chamferEdges.Count, line) is { } chamfer)
                    {
                        chamfers.Add(chamfer);
                        chamferEdges.Add(edge);
                    }
                }
                else if (withLines)
                {
                    var outline = geometry.CurveOutline(edge);
                    curves.AddRange(outline);
                    var circle = geometry.TryReadFacingCircle(edge);
                    curveEdges.Add(edge);
                    curveShapes.Add(new SheetCurve(outline, circle?.Center, circle?.Radius ?? 0));
                    if (withArcs && geometry.TryReadArc(edge, arcEdges.Count) is { } arc)
                    {
                        arcs.Add(arc);
                        arcEdges.Add(edge);
                    }
                }
            }
        }

        // 1.17.0：离最外直边不到一个折弯外半径的竖直 / 水平边（折边内表面、让位缺口）也是折弯留下的，一并去掉。
        // 倒角斜边不会落进来，chamferEdges 的下标不受影响。
        if (withLines && bendZone > 0)
        {
            var inZone = SheetMetal.InBendZone(lines, bendZone * geometry.Scale, geometry.Scale);
            for (var k = inZone.Count - 1; k >= 0; k--)
            {
                lines.RemoveAt(inZone[k]);
                lineEdges.RemoveAt(inZone[k]);
            }

            bendLines += inZone.Count;
        }

        // 1.12.0：整圆、孔口残弧、与孔同圆的弧不是圆角（孔由孔类指令标），在这里就去掉。下标仍指 arcEdges。
        var (fillets, circleArcs) = FilletPlanner.WithoutCircles(arcs, candidates);
        var scanned = new ScannedView(document, view, viewName, geometry, edges, candidates, lineEdges, lines, curves, arcEdges, fillets, chamferEdges, chamfers, circleArcs, bendLines, curveEdges, curveShapes);
        scans[cacheKey] = (transform, api.Generation, scanned);
        return scanned;
    }

    /// <summary>零件视图只有一个「无组件」；装配视图逐个可见组件取边。</summary>
    private static IEnumerable<object?> VisibleComponents(SolidWorksApi api, object view, object model)
    {
        if (api.CallInt(model, "IModelDoc2", "GetType") != SwConst.DocumentAssembly)
            return [null];
        return api.CallArray(view, "IView", "GetVisibleComponents");
    }

}

/// <summary>一条指令里已识别的视图：视图没挪没缩、图纸没重建就不重读边。</summary>
internal sealed class ScanCache : Dictionary<string, (double[] Transform, int Generation, ScannedView Scan)>
{
    public ScanCache() : base(StringComparer.Ordinal)
    {
    }
}
