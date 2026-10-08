using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「圆心位置」（1.14.2；1.15.0 起倒圆倒角类）：点一个工程图视图，给圆弧（圆角、凹弧、孔以外的整圆）的圆心标位置尺寸，
/// 基准与孔位尺寸相同（左 / 上直边）；两端与直线相切的不标、一端相切的只标一个（规则见 <see cref="ArcCenterPlanner"/>）。
/// </summary>
/// <remarks>
/// <para>认弧与「圆弧标注」同一套（<see cref="HoleScan.ViewGeometry.TryReadArc"/>，孔先减掉）。普通模式：选基准边、再选圆弧，
/// <c>AddHorizontalDimension2</c> / <c>AddVerticalDimension2</c>（选圆弧 SolidWorks 量到圆心，同孔位尺寸连腰型孔端头），
/// 读回值与规划核对，对不上（量到了弧的切点）就删掉记作没接受；排在已有尺寸最外层之外。
/// 尺寸链模式：并进孔的那组坐标尺寸（同外轮廓，<see cref="Outline.ExtendGroup"/>），组里已有同值的不再加。</para>
/// <para>「避障」开着时，普通模式新加尺寸的数字压在别的尺寸、孔相关注解上就沿尺寸线滑开（同外轮廓，<see cref="Clearance.ClearOutline"/>）。</para>
/// <para>不删旧尺寸：已有尺寸量到这个圆心的方向直接跳过（圆弧的位置尺寸常是手标的，不替用户重标）。</para>
/// <para>去重（第二轮，用户定）：某个方向上坐标相同的圆心只标一个，其余用<b>中心线</b>连上（见 <see cref="InsertLink"/>）。</para>
/// </remarks>
internal static class ArcCenter
{
    public static QuickCommand Command { get; } = new(
        Key: "arc-center",
        CommandName: StrenuaIdentity.Domain + ".fillet.arccenter",
        Title: "圆心位置",
        Summary: "点一个工程图视图，给圆弧圆心标位置尺寸（基准同孔位尺寸）：两端与直线相切不标，一端相切标一个，都不相切标两个。",
        Usage: "在工程图里点一个视图（先点后按、先按后点都行，60 秒内）。视图里的圆弧（圆角、凹弧、孔以外的整圆）以零件最左、最上的直边为基准标圆心位置："
            + "两端都与直线相切的不标；两端都不相切的（含整圆）水平、竖直各标一个；只有一端相切的只标一个——切线水平只标水平尺寸、切线竖直只标竖直尺寸、切线斜的标水平尺寸；"
            + "两端切的两条线平行（腰形端头）也按一端相切。与孔同心的（孔位尺寸已定）、落在基准或对称轴上的方向、已有尺寸量到圆心的方向都跳过。"
            + "某个方向上坐标相同的圆心只标一个（与孔或已标的圆心对齐的不标），用中心线连上；已经有线串着的不再加。"
            + "「尺寸链」关时每个一层排在已有尺寸外面，开时并进孔的那组坐标尺寸；「避障」开着时新加尺寸的数字压线就沿尺寸线滑开。",
        Run: context => Run(context, null));

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="view">直接处理这个视图（「全图圆心」用）；null 时取选中的或等用户点选。</param>
    internal static QuickOutcome Run(QuickCommandContext context, object? view)
    {
        var api = context.Api;
        var scan = HoleScan.Scan(context, "圆心位置", withLines: true, view: view, withArcs: true);
        var (document, viewName) = (scan.Document, scan.ViewName);
        if (scan.Arcs.Count == 0)
            return QuickOutcome.Ok($"视图「{viewName}」里没有正对图纸的圆弧（孔以外），没有加尺寸。");

        var (leftIndex, topIndex) = HolePositionPlanner.Datums(scan.Lines);
        if (leftIndex is not { } l || topIndex is not { } t)
        {
            return QuickOutcome.Fail($"视图「{viewName}」里找不到"
                + (leftIndex is null ? "竖直的直边做左侧基准" : "水平的直边做上侧基准")
                + "，圆心位置没有标。");
        }

        var (left, top, scale) = (scan.Lines[l].X1, scan.Lines[t].Y1, scan.Geometry.Scale);
        var chain = context.Options.Chain;
        var holes = HoleCalloutPlanner.Recognize(scan.Candidates);
        // 对称视图里圆心正在轴上的那个方向不标，轴没有就补上（同孔位尺寸，全图加轴另有「对称轴」）；尺寸链模式不管对称。
        var symmetry = chain ? [] : SymmetryPlanner.Axes(scan.Lines, scan.CurveSegments, holes, ArcCenterPlanner.Locatable(scan.Arcs, scan.Lines, holes));
        var (axesAdded, axesFailed) = symmetry.Count > 0 ? SymmetryAxes.Insert(api, scan, symmetry) : (0, 0);
        if (axesFailed > 0)
            symmetry = [];
        var (firstHorizontal, firstVertical) = Outline.FirstTiers(api, scan, left, top);
        var plan = ArcCenterPlanner.Plan(scan.Arcs, scan.Lines, holes, left, top, scale,
            DimensionScan.Read(api, scan).Select(item => item.Geometry).ToList(), firstHorizontal, firstVertical, symmetry, DrawnLines(api, scan));
        var skipped = (plan.Tangent > 0 ? $"，{plan.Tangent} 个两端相切不用标" : string.Empty)
            + (plan.OnHole > 0 ? $"，{plan.OnHole} 个与孔同心（孔位尺寸已定）" : string.Empty)
            + (plan.Present > 0 ? $"，{plan.Present} 个方向已有尺寸或落在基准 / 对称轴上跳过" : string.Empty)
            + (plan.Aligned > 0 ? $"，{plan.Aligned} 个方向与别的圆心对齐不另标" : string.Empty)
            + (plan.LinkPresent > 0 ? $"，{plan.LinkPresent} 根对齐的中心线已有" : string.Empty);
        if (plan.Targets.Count == 0 && plan.CenterLinks.Count == 0)
            return QuickOutcome.Ok($"视图「{viewName}」：{plan.CenterCount} 个圆心{skipped}"
                + (axesAdded > 0 ? $"，补了 {axesAdded} 根对称轴" : string.Empty) + "，没有新加尺寸。");

        context.SetState("加圆心尺寸");
        context.Report($"圆心位置：视图「{viewName}」{plan.CenterCount} 个圆心，加 {plan.Targets.Count} 个位置尺寸"
            + (plan.CenterLinks.Count > 0 ? $"、{plan.CenterLinks.Count} 根中心线" : string.Empty) + (chain ? "（尺寸链模式）。" : "。"));
        _ = api.Call(document, "IDrawingDoc", "ActivateView", viewName);
        var added = 0;
        var present = 0;
        var failed = 0;
        var clearance = default(ClearanceResult);
        var (linked, sketched, linkFailed) = (0, 0, 0);
        try
        {
            // 中心线先加：它只是线，不占尺寸的位置；尺寸后加，避障时把它当障碍。
            context.SetState("加中心线");
            foreach (var link in plan.CenterLinks)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                switch (InsertLink(api, scan, link))
                {
                    case LinkResult.CenterLine:
                        linked++;
                        break;
                    case LinkResult.Sketch:
                        sketched++;
                        break;
                    default:
                        linkFailed++;
                        break;
                }
            }

            context.SetState("加圆心尺寸");
            if (chain)
            {
                var ordinates = DimensionScan.Read(api, scan).Where(item => item.Geometry.Ordinate).ToList();
                foreach (var axis in new[] { PositionAxis.Horizontal, PositionAxis.Vertical })
                {
                    context.Cancellation.ThrowIfCancellationRequested();
                    var stops = plan.Targets.Where(target => target.Axis == axis)
                        .Select(target => new GroupStop(scan.ArcEdges![target.ArcIndex], target.Value)).ToList();
                    if (stops.Count == 0)
                        continue;
                    var datum = axis == PositionAxis.Horizontal ? l : t;
                    var (count, already, lost) = Outline.ExtendGroup(api, scan, ordinates, axis, scan.LineEdges[datum], scan.Lines[datum], stops, left, top);
                    added += count;
                    present += already;
                    failed += lost;
                }
            }
            else
            {
                var before = Outline.DimensionNames(api, scan);
                foreach (var target in plan.Targets)
                {
                    context.Cancellation.ThrowIfCancellationRequested();
                    if (Insert(api, scan, scan.LineEdges[target.Axis == PositionAxis.Horizontal ? l : t], scan.ArcEdges![target.ArcIndex], target))
                        added++;
                    else
                        failed++;
                }

                if (context.Options.Clearance && added > 0)
                {
                    context.SetState("避障");
                    var fresh = Outline.DimensionNames(api, scan);
                    fresh.ExceptWith(before);
                    clearance = Clearance.ClearOutline(context, scan, holes, fresh);
                }
            }
        }
        finally
        {
            api.Call(document, "IModelDoc2", "ClearSelection2", true);
            api.Call(document, "IModelDoc2", "GraphicsRedraw2");
        }

        var message = $"视图「{viewName}」：{plan.CenterCount} 个圆心{skipped}，"
            + (axesAdded > 0 ? $"补了 {axesAdded} 根对称轴，" : string.Empty)
            + (chain
                ? $"尺寸链模式新加 {added} 个坐标尺寸" + (present > 0 ? $"，{present} 个组里已有同值跳过" : string.Empty)
                : $"新加 {added} 个位置尺寸")
            + (failed > 0 ? $"，{failed} 个 SolidWorks 没有接受" : string.Empty)
            + (linked + sketched > 0 ? $"，对齐的圆心连了 {linked + sketched} 根中心线" + (sketched > 0 ? $"（{sketched} 根 SolidWorks 不让插中心线，画的是中心线线型的草图线）" : string.Empty) : string.Empty)
            + (linkFailed > 0 ? $"，{linkFailed} 根中心线没连上" : string.Empty)
            + clearance.Describe("尺寸数字")
            + "。";
        return added + linked + sketched == 0 && failed + linkFailed > 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }

    // swLineStyles_e.swLineCENTER / swLineWeights_e.swLW_THIN
    private const int LineCenter = 4;
    private const int LineThin = 0;

    /// <summary>草图线退路两头伸出圆心多远（图纸 3 mm，与 SolidWorks 中心线伸出零件的样子相近）。</summary>
    private const double LinkOverhang = 0.003;

    internal enum LinkResult
    {
        Failed,
        CenterLine,
        Sketch,
    }

    /// <summary>
    /// 把对齐的圆心连上（用户定：用中心线）。先选两头的圆弧 / 孔边，<c>IDrawingDoc.InsertCenterLine2</c> 插中心线注解，
    /// 读回显示的线核对它沿对齐方向串过两头圆心；SolidWorks 不接受（中心线本来是选两条边、或一个圆柱面）或位置不对，
    /// 就删掉，改在视图草图里画一根直线、线型设成中心线（细点划线），两头各伸出 <see cref="LinkOverhang"/>，读回核对。
    /// </summary>
    /// <remarks>真机（SW 2025 SP5，2026-10-08 用户图右视图）：选两段圆弧 <c>InsertCenterLine2</c> 返回 null（中心线注解只认两条边或一个圆柱面），
    /// 圆弧之间实际都走草图退路；ActivateView 之后 <c>CreateLine</c> 画进了视图草图，读回位置对、<c>Style</c> 4、<c>Width</c> 0。
    /// 先试中心线注解是留给与孔相连时（孔边也是圆，多半同样不认，没试）。</remarks>
    internal static LinkResult InsertLink(SolidWorksApi api, ScannedView scan, ArcCenterLink link)
    {
        var document = scan.Document;
        api.Call(document, "IModelDoc2", "ClearSelection2", true);
        var centerLine = SelectCenter(api, scan, link.First, false) && SelectCenter(api, scan, link.Second, true)
            ? api.Call(document, "IDrawingDoc", "InsertCenterLine2")
            : null;
        api.Call(document, "IModelDoc2", "ClearSelection2", true);
        if (centerLine is not null && api.Call(centerLine, "ICenterLine", "GetAnnotation") is { } annotation)
        {
            if (SymmetryAxes.DisplayLines(api, annotation).Any(line => ArcCenterPlanner.Covers(line, link)))
                return LinkResult.CenterLine;
            if (api.CallBool(annotation, "IAnnotation", "Select3", false, null))
                api.Call(document, "IModelDoc2", "EditDelete");
            api.Call(document, "IModelDoc2", "ClearSelection2", true);
        }

        return DrawSketchLink(api, scan, link) ? LinkResult.Sketch : LinkResult.Failed;
    }

    private static bool SelectCenter(SolidWorksApi api, ScannedView scan, CenterRef center, bool append)
        => center.ArcIndex is { } arc
            ? HolePosition.SelectEdge(api, scan, scan.ArcEdges![arc], append)
            : center.HoleIndex is { } hole && HolePosition.SelectEdge(api, scan, scan.Edges[hole], append);

    /// <summary>退路：视图草图里画一根中心线线型的直线（草图点 = (图纸点 − 视图位置) ÷ 比例，<see cref="HolePosition.SketchLineOnSheet"/> 的反算）。</summary>
    private static bool DrawSketchLink(SolidWorksApi api, ScannedView scan, ArcCenterLink link)
    {
        if (scan.Geometry.SketchXform is not { } xform || api.Call(scan.Document, "IModelDoc2", "get_SketchManager") is not { } manager)
            return false;
        var (ax, ay, bx, by) = (link.First.Center.X, link.First.Center.Y, link.Second.Center.X, link.Second.Center.Y);
        var length = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        if (length <= 0)
            return false;
        var (ux, uy) = ((bx - ax) / length, (by - ay) / length);
        (ax, ay, bx, by) = (ax - ux * LinkOverhang, ay - uy * LinkOverhang, bx + ux * LinkOverhang, by + uy * LinkOverhang);
        double SketchX(double x) => (x - xform[0]) / xform[2];
        double SketchY(double y) => (y - xform[1]) / xform[2];

        // 关掉推理捕捉，免得线端被吸到附近的点上。
        var addToDb = api.CallBool(manager, "ISketchManager", "get_AddToDB");
        api.Call(manager, "ISketchManager", "set_AddToDB", true);
        object? segment;
        try
        {
            segment = api.Call(manager, "ISketchManager", "CreateLine", SketchX(ax), SketchY(ay), 0.0, SketchX(bx), SketchY(by), 0.0);
        }
        finally
        {
            api.Call(manager, "ISketchManager", "set_AddToDB", addToDb);
        }

        if (segment is null)
            return false;
        api.Call(segment, "ISketchSegment", "set_Style", LineCenter);
        api.Call(segment, "ISketchSegment", "set_Width", LineThin);
        api.Call(scan.Document, "IModelDoc2", "ClearSelection2", true);
        if (HolePosition.SketchLineOnSheet(api, scan, segment) is { } drawn && ArcCenterPlanner.Covers(drawn, link))
            return true;
        if (api.CallBool(segment, "ISketchSegment", "Select4", false, null))
            api.Call(scan.Document, "IModelDoc2", "EditDelete");
        api.Call(scan.Document, "IModelDoc2", "ClearSelection2", true);
        return false;
    }

    /// <summary>视图里已有的中心线注解与视图草图直线（图纸坐标）：已经把对齐的圆心串起来的就不再加中心线。</summary>
    private static List<SheetSegment> DrawnLines(SolidWorksApi api, ScannedView scan)
    {
        var lines = SymmetryAxes.CenterLines(api, scan.View);
        if (api.Call(scan.View, "IView", "GetSketch") is { } sketch)
        {
            foreach (var segment in api.CallArray(sketch, "ISketch", "GetSketchSegments"))
            {
                if (segment is not null && HolePosition.SketchLineOnSheet(api, scan, segment) is { } line)
                    lines.Add(line);
            }
        }

        return lines;
    }

    /// <summary>选基准边与圆弧，按方向加水平 / 竖直尺寸；建出来的不是线性尺寸、或值对不上（没量到圆心）就删掉。</summary>
    private static bool Insert(SolidWorksApi api, ScannedView scan, object datumEdge, object arcEdge, ArcCenterTarget target)
    {
        api.Call(scan.Document, "IModelDoc2", "ClearSelection2", true);
        if (!HolePosition.SelectEdge(api, scan, datumEdge, false) || !HolePosition.SelectEdge(api, scan, arcEdge, true))
            return false;
        var method = target.Axis == PositionAxis.Horizontal ? "AddHorizontalDimension2" : "AddVerticalDimension2";
        if (api.Call(scan.Document, "IModelDoc2", method, target.TextAt.X, target.TextAt.Y, 0.0) is not { } created)
            return false;
        var value = api.Call(created, "IDisplayDimension", "GetDimension2", 0) is { } dimension
            ? Math.Abs(Convert.ToDouble(api.Call(dimension, "IDimension", "get_SystemValue")))
            : double.NaN;
        if (HolePositionPlanner.IsLinear(api.CallInt(created, "IDisplayDimension", "get_Type2"))
            && Math.Abs(value - target.Value) <= DimensionGeometry.ValueTolerance)
            return true;
        HolePosition.Discard(api, scan, created);
        return false;
    }
}

/// <summary>
/// 快捷指令「全图圆心」（1.14.2；1.15.0 起倒圆倒角类）：当前图纸页全部视图（轴测图跳过）逐个做「圆心位置」（<see cref="ArcCenter"/>），一键出图里在全图圆弧之前。
/// </summary>
internal static class ArcCenterAll
{
    public static QuickCommand Command { get; } = new(
        Key: "arc-center-all",
        CommandName: StrenuaIdentity.Domain + ".fillet.arccenterall",
        Title: "全图圆心",
        Summary: "当前图纸页全部视图（轴测图跳过）逐个做圆心位置：两端与直线相切不标，一端相切标一个，都不相切标两个。",
        Usage: "不用点视图：当前图纸页上的全部视图（轴测图跳过）逐个做「圆心位置」——圆弧（圆角、凹弧、孔以外的整圆）以零件最左、最上的直边为基准标圆心："
            + "两端与直线相切不标，一端相切只标一个（切线水平标水平、切线竖直标竖直、斜的标水平），都不相切水平、竖直各一个；与孔同心、已有尺寸的跳过。某个视图没成不中断，最后汇总。",
        Run: Run);

    internal static QuickOutcome Run(QuickCommandContext context) => FilletAll.EachView(context, "全图圆心", ArcCenter.Run);
}
