using System.Runtime.InteropServices;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 快捷指令「孔位尺寸」：点一个工程图视图，视图里全部的孔以左侧、上侧为基准标位置尺寸。
/// </summary>
/// <remarks>
/// <para>认孔、分种与「孔标注」相同（<see cref="HoleScan"/>）；怎么标见 <see cref="HolePositionPlanner"/>：
/// 同种孔接着前一个孔标，不同种孔从基准标；同种孔一个方向超过 4 个且等距用阵列标法「(N-1) x 间距 =总长」。
/// 视图整个关于一根轴对称时（1.14.1，<see cref="SymmetryPlanner"/>）先补上对称轴（<see cref="SymmetryAxes.Insert"/>，全图加轴另有出图类「对称轴」），
/// 那个方向不再从基准边标、直接标对称轴两侧孔的距离。
/// 页面「尺寸链」开关打开时（1.7.0）改为每个方向一组 SolidWorks「尺寸链」（坐标尺寸，<see cref="InsertOrdinate"/>）：
/// 0 点是左侧 / 上侧基准边，其后每列（行）孔一个坐标值。
/// 腰型孔只标上方那一端圆弧的圆心（1.3.0，用户定）。</para>
/// <para>「重新标」：先删掉连着这些孔的旧线性尺寸与视图里悬空的线性尺寸（孔标注、直径尺寸、外形尺寸不动），再全部重标。</para>
/// <para>最后做标注避障（1.6.0，<see cref="Clearance.ClearDimensions"/>）：尺寸数字压在别的孔相关注解上就沿尺寸线滑开。
/// 1.7.0 起受页面「避障」开关控制（默认开）。</para>
/// </remarks>
internal static class HolePosition
{
    public static QuickCommand Command { get; } = new(
        Key: "hole-position",
        CommandName: StrenuaIdentity.Domain + ".hole.position",
        Title: "孔位尺寸",
        Summary: "点一个工程图视图，删掉孔的旧位置尺寸后以零件左侧、上侧直边为基准重标全部孔位尺寸。",
        Usage: "在工程图里点一个视图（先点后按、先按后点都行，60 秒内），该视图里全部的孔删掉旧位置尺寸（含悬空的线性尺寸）后，以零件左侧、上侧直边为基准重标：同种孔接着前一个标，不同种从基准标；视图里的边和孔整个左右（或上下）对称、且有孔不在对称轴上时，先加对称轴（中心线），那个方向改以对称轴为基准、直接标对称轴两侧同种孔之间的距离（不同种照样不互标）；同种一个方向超过 4 个且等距时标「(N-1) x 间距 =总长」（页面「尺寸链」开关打开时改用 SolidWorks 尺寸链：每个方向一组坐标尺寸，0 点在零件左侧 / 上侧直边，文字排在零件外一列）；腰型孔标在上方那个圆上；「避障」开关开着时，数字压在别的孔的尺寸、中心符号线等线条上就沿尺寸线滑开。",
        Run: context => Run(context, null));

    // swDimensionTextParts_e / swSelectType_e / swAddOrdinateDims_e
    private const int TextPrefix = 1;
    private const int SelectCenterMark = 100;
    private const int VerticalOrdinate = 2;
    private const int HorizontalOrdinate = 3;

    /// <param name="context">快捷指令上下文。</param>
    /// <param name="view">直接处理这个视图（全流程用）；null 时取选中的或等用户点选。</param>
    internal static QuickOutcome Run(QuickCommandContext context, object? view)
    {
        var api = context.Api;
        var scan = HoleScan.Scan(context, "孔位尺寸", withLines: true, view: view);
        var (document, viewName) = (scan.Document, scan.ViewName);

        var holes = HoleCalloutPlanner.Recognize(scan.Candidates);
        if (holes.Count == 0)
            return QuickOutcome.Ok($"视图「{viewName}」里没有正对图纸的孔，没有加尺寸。");

        var (leftIndex, topIndex) = HolePositionPlanner.Datums(scan.Lines);
        if (leftIndex is not { } l || topIndex is not { } t)
        {
            return QuickOutcome.Fail($"视图「{viewName}」里找不到"
                + (leftIndex is null ? "竖直的直边做左侧基准" : "水平的直边做上侧基准")
                + "，孔位尺寸没有标。");
        }

        var chain = context.Options.Chain;
        // 1.14.1：整个视图关于某根轴对称就以对称轴为基准（尺寸链模式不管，照旧从直边量）。
        var symmetry = chain ? [] : SymmetryPlanner.Axes(scan.Lines, scan.CurveSegments, holes);
        var plan = HolePositionPlanner.Plan(scan.Candidates, scan.Lines[l].X1, scan.Lines[t].Y1, scan.Geometry.Scale, chain,
            symmetry.Select(axis => axis.Axis).ToList());
        context.Report($"孔位尺寸：视图「{viewName}」认出 {plan.Summary}，"
            + (symmetry.Count > 0 ? $"关于{string.Join("、", symmetry.Select(axis => axis.Name))}对称，" : string.Empty)
            + $"删掉孔上的旧位置尺寸后标 {plan.Count} 个"
            + (chain ? "（尺寸链模式）。" : $"（阵列 {plan.PatternCount} 个）。"));
        _ = api.Call(document, "IDrawingDoc", "ActivateView", viewName);

        int removed;
        var added = 0;
        var onCenterLines = 0;
        var failed = 0;
        var nativeChains = 0;
        var axesAdded = 0;
        var axesFailed = 0;
        var clearance = default(ClearanceResult);
        try
        {
            context.SetState("删旧尺寸");
            (removed, var leftover) = AnnotationEraser.Erase(context, document, () => ReadObsolete(api, scan, holes), scan.View);
            if (leftover > 0)
            {
                return QuickOutcome.Fail($"视图「{viewName}」：有 {leftover} 个旧位置尺寸删不掉，没有重标（已删 {removed} 个）。"
                    + "请在 SolidWorks 里手工删掉后再按。");
            }

            if (symmetry.Count > 0)
            {
                context.SetState("加对称轴");
                (axesAdded, axesFailed) = SymmetryAxes.Insert(api, scan, symmetry);
            }

            context.SetState("加尺寸");
            // 尺寸链模式（DEC-018）：每个方向一组 SolidWorks「尺寸链」（坐标尺寸）。
            foreach (var group in plan.OrdinateGroups)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                var datumEdge = scan.LineEdges[group.Axis == PositionAxis.Horizontal ? l : t];
                var (count, centerLines) = InsertOrdinate(api, scan, group, datumEdge);
                if (count == 0)
                {
                    failed += group.Count;
                    continue;
                }

                added += count;
                failed += group.Count - count;
                nativeChains++;
                if (centerLines)
                    onCenterLines += count;
            }

            foreach (var dimension in plan.Dimensions)
            {
                context.Cancellation.ThrowIfCancellationRequested();
                var datumEdge = scan.LineEdges[dimension.Axis == PositionAxis.Horizontal ? l : t];
                var (created, centerLines) = Insert(api, scan, datumEdge, dimension);
                if (created is null)
                {
                    failed++;
                    continue;
                }

                added++;
                if (centerLines)
                    onCenterLines++;
            }

            if (context.Options.Clearance)
            {
                context.SetState("避障");
                clearance = Clearance.ClearDimensions(context, scan, holes);
            }
        }
        finally
        {
            api.Call(document, "IModelDoc2", "ClearSelection2", true);
            api.Call(document, "IModelDoc2", "GraphicsRedraw2");
        }

        var message = $"视图「{viewName}」：{plan.Summary}，删掉旧位置尺寸 {removed} 个，"
            + $"新加 {added} 个（{(chain ? $"尺寸链模式，尺寸链 {nativeChains} 组" : $"阵列标法 {plan.PatternCount} 个")}；连在中心线上 {onCenterLines} 个，其余连在孔边上）"
            + (failed > 0 ? $"，{failed} 个 SolidWorks 没有接受" : string.Empty)
            + (symmetry.Count > 0
                ? $"；{string.Join("、", symmetry.Select(axis => axis.Name))}为基准、直接标两侧孔的距离"
                  + (axesAdded > 0 ? $"，加了 {axesAdded} 根对称轴" : "，对称轴已有")
                  + (axesFailed > 0 ? $"，{axesFailed} 根 SolidWorks 没有插上" : string.Empty)
                : string.Empty)
            + clearance.Describe("尺寸数字")
            + "。";
        return added == 0 && plan.Count > 0 ? QuickOutcome.Fail(message) : QuickOutcome.Ok(message);
    }

    /// <summary>
    /// 选起点（基准边或前一个孔）与终点孔，按方向加水平 / 竖直尺寸；阵列标法再写前缀。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 水平尺寸的孔那一头优先选孔上的中心符号线：SolidWorks 把尺寸连到符号线的竖线上，和手工「按中心线标」一样；
    /// 有一头选不到中心符号线（孔上没有），整条尺寸退回选孔边（量到圆心，值相同）。
    /// </para>
    /// <para>
    /// 竖直尺寸只选孔边。真机（SW 2025 SP5）实测：选中中心符号线加竖直尺寸，SolidWorks 不论点选偏移、选择先后，
    /// 一律拿符号的竖线，与水平基准边一起建成角度尺寸。API 也拿不到中心符号线的横线单独去选。
    /// </para>
    /// <para>建出来的不是线性尺寸就删掉，退回选孔边再建。</para>
    /// </remarks>
    /// <returns>加上了没有，以及是不是连在中心线上。</returns>
    internal static (object? Created, bool OnCenterLines) Insert(SolidWorksApi api, ScannedView scan, object? datumEdge, PositionDimension dimension)
    {
        var horizontal = dimension.Axis == PositionAxis.Horizontal;
        var method = horizontal ? "AddHorizontalDimension2" : "AddVerticalDimension2";
        foreach (var onCenterLines in horizontal ? new[] { true, false } : [false])
        {
            api.Call(scan.Document, "IModelDoc2", "ClearSelection2", true);
            var selected = (dimension.FromEdgeIndex is { } from ? SelectHole(api, scan, from, false, onCenterLines) : datumEdge is not null && SelectEdge(api, scan, datumEdge, false))
                && SelectHole(api, scan, dimension.ToEdgeIndex, true, onCenterLines);
            if (!selected)
                continue;
            if (api.Call(scan.Document, "IModelDoc2", method, dimension.TextAt.X, dimension.TextAt.Y, 0.0) is not { } created)
                continue;
            if (!HolePositionPlanner.IsLinear(api.CallInt(created, "IDisplayDimension", "get_Type2")))
            {
                Discard(api, scan, created);
                continue;
            }

            if (dimension.Prefix.Length > 0)
                api.Call(created, "IDisplayDimension", "SetText", TextPrefix, dimension.Prefix);
            return (created, onCenterLines);
        }

        return (null, false);
    }

    /// <summary>
    /// 一个方向的一组 SolidWorks「尺寸链」（坐标尺寸）：先选基准边在 <see cref="OrdinateGroup.At"/> 建 0 点
    /// （<c>IModelDocExtension.AddOrdinateDimension</c>），再选中这个 0 点尺寸与各站的孔，
    /// <c>IModelDoc2.EditOrdinate</c> 一次把整组加进去；挤在一起的文字 SolidWorks 自己折弯错开。
    /// </summary>
    /// <remarks>
    /// <para>真机（SW 2025 SP5，WTJYQ-01-08 底板 View2）：预选 0 点与全部孔再 <c>AddOrdinateDimension</c> 只建出 0 点，
    /// 之后用 API 再选也不会往组里加（那是界面交互）；<c>EditOrdinate</c> 才一次全建出来，位置、折弯与用户手工的演示一致。
    /// <c>AddOrdinateDimension</c> 之后要 <c>SetPickMode</c> 退出加尺寸状态。</para>
    /// <para>和演示一样优先连孔上的中心符号线（SolidWorks 取过孔心的那根横线 / 竖线，附着类型 24）；
    /// 孔挨得很近时按孔心点选可能选到邻孔的中心符号线——有一个选不上，或建出来的值和规划对不上，
    /// 整组删掉退回连孔边（量到圆心，值相同；引出线画到圆心）。</para>
    /// </remarks>
    /// <returns>加上的尺寸个数（含 0 点）与是不是连在中心线上；0 表示 SolidWorks 没接受。</returns>
    private static (int Added, bool OnCenterLines) InsertOrdinate(SolidWorksApi api, ScannedView scan, OrdinateGroup group, object datumEdge)
    {
        // 0 点与每一站离基准的模型距离（米），用来核对 SolidWorks 建出来的值。
        var expected = group.Values.Prepend(0.0).Order().ToList();

        foreach (var onCenterLines in new[] { true, false })
        {
            var before = OrdinateNames(api, scan);
            if (CreateOrdinateZero(api, scan, datumEdge, group.Axis == PositionAxis.Horizontal, group.At) is not { } zero)
                return (0, false);
            ExtendOrdinate(api, scan, zero, () =>
            {
                var selected = true;
                for (var i = 0; i < group.EdgeIndexes.Count && selected; i++)
                    selected = SelectHole(api, scan, group.EdgeIndexes[i], true, onCenterLines);
                return selected;
            });
            var created = NewOrdinates(api, scan, before);
            if (!HolePositionPlanner.SameValues(expected, OrdinateValues(api, created)))
            {
                Discard(api, scan, created);
                continue;
            }

            return (created.Count, onCenterLines);
        }

        return (0, false);
    }

    /// <summary>
    /// 选基准边、<c>AddOrdinateDimension</c> 在 <paramref name="at"/> 建一个坐标尺寸组的 0 点（只建得出 0 点，见 <see cref="InsertOrdinate"/>），
    /// 退出加尺寸状态。建出来的不是恰好一个就删掉、返回 null。
    /// </summary>
    internal static object? CreateOrdinateZero(SolidWorksApi api, ScannedView scan, object datumEdge, bool horizontal, SheetPoint at)
    {
        if (api.Call(scan.Document, "IModelDoc2", "get_Extension") is not { } extension)
            return null;
        var before = OrdinateNames(api, scan);
        api.Call(scan.Document, "IModelDoc2", "ClearSelection2", true);
        if (!SelectEdge(api, scan, datumEdge, false))
            return null;
        var error = api.CallInt(extension, "IModelDocExtension", "AddOrdinateDimension",
            horizontal ? HorizontalOrdinate : VerticalOrdinate, at.X, at.Y, 0.0);
        api.Call(scan.Document, "IModelDoc2", "ClearSelection2", true);
        api.Call(scan.Document, "IModelDoc2", "SetPickMode");
        var created = NewOrdinates(api, scan, before);
        if (error == 0 && created.Count == 1)
            return created[0];
        Discard(api, scan, created);
        return null;
    }

    /// <summary>
    /// 往已有的坐标尺寸组里加站：选中组的 0 点尺寸，<paramref name="selectMembers"/> 追加选上要加的对象，
    /// <c>IModelDoc2.EditOrdinate</c> 一次加进去。加了哪些由调用方回读（<see cref="NewOrdinates"/>）。
    /// </summary>
    internal static void ExtendOrdinate(SolidWorksApi api, ScannedView scan, object zero, Func<bool> selectMembers)
    {
        api.Call(scan.Document, "IModelDoc2", "ClearSelection2", true);
        var selected = api.Call(zero, "IDisplayDimension", "GetAnnotation") is { } zeroAnnotation
                       && api.CallBool(zeroAnnotation, "IAnnotation", "Select3", false, null)
                       && selectMembers();
        if (selected)
            api.Call(scan.Document, "IModelDoc2", "EditOrdinate");
        api.Call(scan.Document, "IModelDoc2", "SetPickMode");
        api.Call(scan.Document, "IModelDoc2", "ClearSelection2", true);
    }

    /// <summary>一批尺寸的值（模型长度，米），排好序。</summary>
    internal static List<double> OrdinateValues(SolidWorksApi api, IEnumerable<object> displayDimensions)
        => displayDimensions
            .Select(dimension => api.Call(dimension, "IDisplayDimension", "GetDimension2", 0))
            .Select(dimension => dimension is null ? double.NaN : Convert.ToDouble(api.Call(dimension, "IDimension", "get_SystemValue")))
            .Order()
            .ToList();

    /// <summary>视图里现有坐标尺寸的注解名。</summary>
    internal static HashSet<string> OrdinateNames(SolidWorksApi api, ScannedView scan)
        => Ordinates(api, scan)
            .Select(dimension => api.Call(dimension, "IDisplayDimension", "GetAnnotation"))
            .Where(annotation => annotation is not null)
            .Select(annotation => api.CallString(annotation!, "IAnnotation", "GetName"))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>视图里不在 <paramref name="before"/> 里的坐标尺寸（<c>IDisplayDimension</c>）。</summary>
    internal static List<object> NewOrdinates(SolidWorksApi api, ScannedView scan, HashSet<string> before)
        => Ordinates(api, scan)
            .Where(dimension => api.Call(dimension, "IDisplayDimension", "GetAnnotation") is { } annotation
                                && !before.Contains(api.CallString(annotation, "IAnnotation", "GetName")))
            .ToList();

    internal static IEnumerable<object> Ordinates(SolidWorksApi api, ScannedView scan)
    {
        var dimension = api.Call(scan.View, "IView", "GetFirstDisplayDimension5");
        while (dimension is not null)
        {
            if (HolePositionPlanner.IsOrdinate(api.CallInt(dimension, "IDisplayDimension", "get_Type2")))
                yield return dimension;
            dimension = api.Call(dimension, "IDisplayDimension", "GetNext5");
        }
    }

    /// <summary>删掉一组刚建错的尺寸。</summary>
    internal static void Discard(SolidWorksApi api, ScannedView scan, IReadOnlyList<object> displayDimensions)
    {
        if (displayDimensions.Count == 0)
            return;
        api.Call(scan.Document, "IModelDoc2", "ClearSelection2", true);
        var selected = 0;
        foreach (var displayDimension in displayDimensions)
        {
            if (api.Call(displayDimension, "IDisplayDimension", "GetAnnotation") is { } annotation
                && api.CallBool(annotation, "IAnnotation", "Select3", true, null))
                selected++;
        }

        if (selected > 0)
            api.Call(scan.Document, "IModelDoc2", "EditDelete");
        api.Call(scan.Document, "IModelDoc2", "ClearSelection2", true);
    }

    /// <summary>删掉刚建错的尺寸（例如 SolidWorks 建成了角度尺寸）。</summary>
    internal static void Discard(SolidWorksApi api, ScannedView scan, object displayDimension)
    {
        api.Call(scan.Document, "IModelDoc2", "ClearSelection2", true);
        if (api.Call(displayDimension, "IDisplayDimension", "GetAnnotation") is { } annotation
            && api.CallBool(annotation, "IAnnotation", "Select3", false, null))
            api.Call(scan.Document, "IModelDoc2", "EditDelete");
    }

    internal static bool SelectEdge(SolidWorksApi api, ScannedView scan, object edge, bool append)
        => api.CallBool(scan.View, "IView", "SelectEntity", edge, append);

    /// <summary>
    /// 选孔：<paramref name="centerMark"/> 时在孔心点选中心符号线，并核对选中的确实是中心符号线
    /// （点空了 SolidWorks 会改选整个视图）；否则选孔边。
    /// </summary>
    internal static bool SelectHole(SolidWorksApi api, ScannedView scan, int edgeIndex, bool append, bool centerMark)
    {
        if (!centerMark)
            return SelectEdge(api, scan, scan.Edges[edgeIndex], append);

        var hole = scan.Candidates[edgeIndex];
        var extension = api.Call(scan.Document, "IModelDoc2", "get_Extension");
        var selection = api.Call(scan.Document, "IModelDoc2", "get_SelectionManager");
        if (extension is null || selection is null)
            return false;
        var before = api.CallInt(selection, "ISelectionMgr", "GetSelectedObjectCount2", -1);
        if (!api.CallBool(extension, "IModelDocExtension", "SelectByID2", string.Empty, "CENTERMARKSYMS", hole.X, hole.Y, 0.0, append, 0, null, 0))
            return false;
        var count = api.CallInt(selection, "ISelectionMgr", "GetSelectedObjectCount2", -1);
        return count == before + 1
            && api.CallInt(selection, "ISelectionMgr", "GetSelectedObjectType3", count, -1) == SelectCenterMark;
    }

    /// <summary>
    /// 视图里连着这些孔的旧线性尺寸（<c>IAnnotation</c>）：连着孔边的，或连着穿过孔心的中心线的。孔标注不算。
    /// </summary>
    private static IReadOnlyList<object> ReadObsolete(SolidWorksApi api, ScannedView scan, IReadOnlyList<HoleEdge> holes)
    {
        var annotations = new List<object>();
        var existing = new List<ExistingDimension>();
        var dimension = api.Call(scan.View, "IView", "GetFirstDisplayDimension5");
        while (dimension is not null)
        {
            if (!api.CallBool(dimension, "IDisplayDimension", "IsHoleCallout")
                && api.Call(dimension, "IDisplayDimension", "GetAnnotation") is { } annotation)
            {
                var type = api.CallInt(dimension, "IDisplayDimension", "get_Type2");
                var centers = HoleCallout.AttachedCircleCenters(api, scan.Geometry, annotation).ToList();
                var attached = HolePositionPlanner.IsOrdinate(type) ? AttachedKeys(api, scan, annotation) : null;
                existing.Add(new ExistingDimension(
                    annotations.Count, type, centers, AttachedLines(api, scan, annotation), IsDangling(api, annotation), attached));
                annotations.Add(annotation);
            }

            dimension = api.Call(dimension, "IDisplayDimension", "GetNext5");
        }

        return HolePositionPlanner.Obsolete(holes, existing).Select(index => annotations[index]).ToList();
    }

    /// <summary>
    /// 坐标尺寸每个附着对象的图纸几何写成的键（模型直边两端、圆边圆心、视图草图线两端，0.01 mm 取整），
    /// 用来认同组的 0 点（见 <see cref="HolePositionPlanner.Obsolete"/>）。读不出的对象不出键。
    /// </summary>
    private static List<string> AttachedKeys(SolidWorksApi api, ScannedView scan, object annotation)
    {
        static string Mm(double value) => Math.Round(value * 1e5).ToString(System.Globalization.CultureInfo.InvariantCulture);
        static string Segment(char kind, SheetSegment s) => $"{kind}{Mm(s.X1)},{Mm(s.Y1)},{Mm(s.X2)},{Mm(s.Y2)}";

        var keys = new List<string>();
        foreach (var entity in api.CallArray(annotation, "IAnnotation", "GetAttachedEntities3"))
        {
            if (entity is null)
                continue;
            if (scan.Geometry.TryReadCircleCenter(entity) is { } center)
            {
                keys.Add($"C{Mm(center.X)},{Mm(center.Y)}");
                continue;
            }

            try
            {
                if (scan.Geometry.TryReadLine(entity) is { } line)
                {
                    keys.Add(Segment('L', line));
                    continue;
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or System.Reflection.TargetException)
            {
                // 不是模型边（视图草图线之类）：往下试。
            }

            if (SketchLineOnSheet(api, scan, entity) is { } sketch)
                keys.Add(Segment('S', sketch));
        }

        return keys;
    }

    /// <summary>
    /// 尺寸悬空了没有：<c>IAnnotation.IsDangling</c>；保险起见附着对象里有空的也算
    /// （真机上「中心符号线」删掉旧符号线后，挂在上面的尺寸附着类型读回 0、对象为空）。
    /// </summary>
    private static bool IsDangling(SolidWorksApi api, object annotation)
        => api.CallBool(annotation, "IAnnotation", "IsDangling")
            || api.CallArray(annotation, "IAnnotation", "GetAttachedEntities3").Any(entity => entity is null)
            || api.CallArray(annotation, "IAnnotation", "GetAttachedEntityTypes").Any(type => Convert.ToInt32(type) == 0);

    /// <summary>
    /// 注解连着的视图草图线（中心符号线、中心线）在图纸上的样子。草图坐标是模型尺寸、原点在视图位置，
    /// 图纸点 = 视图位置 + 草图点 × 比例；<c>GetXform</c> 不含旋转，所以旋转过的视图不认（返回空）。
    /// </summary>
    internal static List<SheetSegment> AttachedLines(SolidWorksApi api, ScannedView scan, object annotation)
    {
        // 不按附着类型码筛：真机（1.6.0 过渡板）上有尺寸的附着类型报 1（边），对象却是视图草图线段
        // （穿过孔心的中心线），按 24 筛就认不出它连着孔，重标时删不掉、新旧叠成两个。所以每个附着对象都试着当草图直线读。
        // 悬空（连着的中心符号线被删了）时对象读回 null。
        return api.CallArray(annotation, "IAnnotation", "GetAttachedEntities3")
            .Where(entity => entity is not null)
            .Select(entity => SketchLineOnSheet(api, scan, entity))
            .OfType<SheetSegment>()
            .ToList();
    }

    /// <summary>一个视图草图直线在图纸上的两端；不是草图直线、对象已失效或视图旋转过时为 null。</summary>
    internal static SheetSegment? SketchLineOnSheet(SolidWorksApi api, ScannedView scan, object entity)
    {
        if (scan.Geometry.SketchXform is not { } xform)
            return null;
        try
        {
            if (api.Call(entity, "ISketchLine", "GetStartPoint2") is not { } start
                || api.Call(entity, "ISketchLine", "GetEndPoint2") is not { } end)
                return null;
            double X(object point) => xform[0] + Convert.ToDouble(api.Call(point, "ISketchPoint", "get_X")) * xform[2];
            double Y(object point) => xform[1] + Convert.ToDouble(api.Call(point, "ISketchPoint", "get_Y")) * xform[2];
            return new SheetSegment(X(start), Y(start), X(end), Y(end));
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or System.Reflection.TargetException)
        {
            // 是模型边、不是直线（圆弧形的中心线之类），或对象已失效：不认。
            return null;
        }
    }
}
