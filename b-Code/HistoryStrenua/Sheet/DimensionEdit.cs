using System.Runtime.InteropServices;
using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>往视图里加、连、删尺寸的共用动作：选边、选孔、坐标尺寸组、外轮廓式线性尺寸。</summary>
internal static class DimensionEdit
{
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

    internal static bool SelectEdge(SolidWorksApi api, ScannedView scan, object edge, bool append)
        => api.CallBool(scan.View, "IView", "SelectEntity", edge, append);

    /// <summary>按图纸 SVG 的编号选：孔边（量到圆心）、直边或曲线，或它们的端点（SelectEntity 也收顶点）。编号不存在回 false。</summary>
    internal static bool SelectGeometry(SolidWorksApi api, ScannedView scan, GeometryRef item, bool append)
    {
        if (item.Hole)
            return item.Index < scan.Edges.Count && SelectEdge(api, scan, scan.Edges[item.Index], append);
        var edges = item.Curve ? scan.CurveEdges : scan.LineEdges;
        if (edges is null || item.Index >= edges.Count)
            return false;
        var edge = edges[item.Index];
        if (item.End == 0)
            return SelectEdge(api, scan, edge, append);
        // TryReadLine 的 X1,Y1 就是起点顶点。
        return api.Call(edge, "IEdge", item.End == 1 ? "GetStartVertex" : "GetEndVertex") is { } vertex
               && api.CallBool(scan.View, "IView", "SelectEntity", vertex, append);
    }

    /// <summary>删掉一组刚建错的尺寸。</summary>
    internal static void Discard(SolidWorksApi api, ScannedView scan, IReadOnlyList<object> displayDimensions)
    {
        if (displayDimensions.Count == 0)
            return;
        api.ClearSelection(scan.Document);
        var selected = 0;
        foreach (var displayDimension in displayDimensions)
        {
            if (api.Call(displayDimension, "IDisplayDimension", "GetAnnotation") is { } annotation
                && api.CallBool(annotation, "IAnnotation", "Select3", true, null))
                selected++;
        }

        if (selected > 0)
            api.Call(scan.Document, "IModelDoc2", "EditDelete");
        api.ClearSelection(scan.Document);
    }

    /// <summary>删掉刚建错的尺寸（例如 SolidWorks 建成了角度尺寸）。</summary>
    internal static void Discard(SolidWorksApi api, ScannedView scan, object displayDimension)
    {
        api.ClearSelection(scan.Document);
        if (api.Call(displayDimension, "IDisplayDimension", "GetAnnotation") is { } annotation
            && api.CallBool(annotation, "IAnnotation", "Select3", false, null))
            api.Call(scan.Document, "IModelDoc2", "EditDelete");
    }

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

    /// <summary>视图里不在 <paramref name="before"/> 里的坐标尺寸（<c>IDisplayDimension</c>）。</summary>
    internal static List<object> NewOrdinates(SolidWorksApi api, ScannedView scan, HashSet<string> before)
        => Ordinates(api, scan)
            .Where(dimension => api.Call(dimension, "IDisplayDimension", "GetAnnotation") is { } annotation
                                && !before.Contains(api.CallString(annotation, "IAnnotation", "GetName")))
            .ToList();

    /// <summary>视图里现有坐标尺寸的注解名。</summary>
    internal static HashSet<string> OrdinateNames(SolidWorksApi api, ScannedView scan)
        => Ordinates(api, scan)
            .Select(dimension => api.Call(dimension, "IDisplayDimension", "GetAnnotation"))
            .Where(annotation => annotation is not null)
            .Select(annotation => api.CallString(annotation!, "IAnnotation", "GetName"))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>一批尺寸的值（模型长度，米），排好序。</summary>
    internal static List<double> OrdinateValues(SolidWorksApi api, IEnumerable<object> displayDimensions)
        => displayDimensions
            .Select(dimension => api.Call(dimension, "IDisplayDimension", "GetDimension2", 0))
            .Select(dimension => dimension is null ? double.NaN : Convert.ToDouble(api.Call(dimension, "IDimension", "get_SystemValue")))
            .Order()
            .ToList();

    /// <summary>
    /// 往已有的坐标尺寸组里加站：选中组的 0 点尺寸，<paramref name="selectMembers"/> 追加选上要加的对象，
    /// <c>IModelDoc2.EditOrdinate</c> 一次加进去。加了哪些由调用方回读（<see cref="NewOrdinates"/>）。
    /// </summary>
    internal static void ExtendOrdinate(SolidWorksApi api, ScannedView scan, object zero, Func<bool> selectMembers)
    {
        api.ClearSelection(scan.Document);
        var selected = api.Call(zero, "IDisplayDimension", "GetAnnotation") is { } zeroAnnotation
                       && api.CallBool(zeroAnnotation, "IAnnotation", "Select3", false, null)
                       && selectMembers();
        if (selected)
            api.Call(scan.Document, "IModelDoc2", "EditOrdinate");
        api.Call(scan.Document, "IModelDoc2", "SetPickMode");
        api.ClearSelection(scan.Document);
    }

    /// <summary>
    /// 选基准边、<c>AddOrdinateDimension</c> 在 <paramref name="at"/> 建一个坐标尺寸组的 0 点（只建得出 0 点，见 <see cref="HolePosition.InsertOrdinate"/>），
    /// 退出加尺寸状态。建出来的不是恰好一个就删掉、返回 null。
    /// </summary>
    internal static object? CreateOrdinateZero(SolidWorksApi api, ScannedView scan, object datumEdge, bool horizontal, SheetPoint at)
    {
        if (api.Call(scan.Document, "IModelDoc2", "get_Extension") is not { } extension)
            return null;
        var before = OrdinateNames(api, scan);
        api.ClearSelection(scan.Document);
        if (!SelectEdge(api, scan, datumEdge, false))
            return null;
        var error = api.CallInt(extension, "IModelDocExtension", "AddOrdinateDimension",
            horizontal ? HorizontalOrdinate : VerticalOrdinate, at.X, at.Y, 0.0);
        api.ClearSelection(scan.Document);
        api.Call(scan.Document, "IModelDoc2", "SetPickMode");
        var created = NewOrdinates(api, scan, before);
        if (error == 0 && created.Count == 1)
            return created[0];
        Discard(api, scan, created);
        return null;
    }

    /// <summary>
    /// 位置类尺寸两侧都能放时（1.23.0）的四边：左、上是基准，右、下是零件轮廓最外沿；每边第一层在那一边现有尺寸文字最外的再往外一层
    /// （孔标注不算，同 <see cref="FirstTiers"/>）。
    /// </summary>
    internal static DimensionSides Sides(SolidWorksApi api, ScannedView scan, double left, double top)
    {
        var outline = scan.Lines.Concat(scan.CurveSegments).ToList();
        var right = outline.Count > 0 ? outline.Max(line => Math.Max(line.X1, line.X2)) : left;
        var bottom = outline.Count > 0 ? outline.Min(line => Math.Min(line.Y1, line.Y2)) : top;
        var (above, beside, below, after) = (new List<double>(), new List<double>(), new List<double>(), new List<double>());
        foreach (var item in DimensionScan.Read(api, scan).Where(item => !item.Geometry.HoleCallout))
        {
            var position = api.CallDoubles(item.Annotation, "IAnnotation", "GetPosition");
            if (position.Length < 2)
                continue;
            above.Add(position[1] - top);
            beside.Add(left - position[0]);
            below.Add(bottom - position[1]);
            after.Add(position[0] - right);
        }

        return new DimensionSides(left, top, right, bottom,
            OutlinePlanner.FirstTier(above), OutlinePlanner.FirstTier(beside), OutlinePlanner.FirstTier(below), OutlinePlanner.FirstTier(after));
    }

    /// <summary>
    /// 普通模式第一层放多远：视图里现有尺寸（孔标注除外）文字离上侧 / 左侧基准最远的再往外一层（<see cref="OutlinePlanner.FirstTier"/>）。
    /// </summary>
    internal static (double Horizontal, double Vertical) FirstTiers(SolidWorksApi api, ScannedView scan, double left, double top)
    {
        var above = new List<double>();
        var beside = new List<double>();
        foreach (var item in DimensionScan.Read(api, scan).Where(item => !item.Geometry.HoleCallout))
        {
            var position = api.CallDoubles(item.Annotation, "IAnnotation", "GetPosition");
            if (position.Length < 2)
                continue;
            above.Add(position[1] - top);
            beside.Add(left - position[0]);
        }

        return (OutlinePlanner.FirstTier(above), OutlinePlanner.FirstTier(beside));
    }

    /// <summary>选基准边与站的那条边，按方向加水平 / 竖直尺寸；建出来的不是线性尺寸就删掉（方形槽 1.17.0 也用）。</summary>
    internal static bool Insert(SolidWorksApi api, ScannedView scan, object datumEdge, object edge, PositionAxis axis, SheetPoint textAt)
    {
        api.ClearSelection(scan.Document);
        if (!DimensionEdit.SelectEdge(api, scan, datumEdge, false) || !DimensionEdit.SelectEdge(api, scan, edge, true))
            return false;
        var method = axis == PositionAxis.Horizontal ? "AddHorizontalDimension2" : "AddVerticalDimension2";
        if (api.Call(scan.Document, "IModelDoc2", method, textAt.X, textAt.Y, 0.0) is not { } created)
            return false;
        if (HolePositionPlanner.IsLinear(api.CallInt(created, "IDisplayDimension", "get_Type2")))
            return true;
        DimensionEdit.Discard(api, scan, created);
        return false;
    }

    /// <summary>视图里现有尺寸（注解）的名字：加之前、加之后各读一次，差就是这一轮新加的。</summary>
    internal static HashSet<string> DimensionNames(SolidWorksApi api, ScannedView scan)
        => DimensionScan.Read(api, scan).Select(item => api.CallString(item.Annotation, "IAnnotation", "GetName")).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// 尺寸链模式一个方向：找 0 点连在基准边上的那组坐标尺寸（孔位尺寸建的），没有就新建 0 点；把组里还没有的站一次加进去，核对值。
    /// 外轮廓的站（直边）与「圆心位置」（1.14.2）的圆弧共用。
    /// </summary>
    /// <returns>加上的个数、组里已有同值跳过的站数、没加上的站数。</returns>
    internal static (int Added, int Present, int Failed) ExtendGroup(
        SolidWorksApi api, ScannedView scan, IReadOnlyList<ScannedDimension> ordinates, PositionAxis axis, object datumEdge, SheetSegment datumLine,
        IReadOnlyList<GroupStop> stations, double left, double top)
    {
        bool OnDatum(DimensionAnchor anchor) => anchor.Segment is { } segment && DimensionGeometry.SameSegment(segment, datumLine);

        var members = ordinates.Where(item => item.Geometry.Anchors.Any(OnDatum)).ToList();
        var zero = members.FirstOrDefault(item => item.Geometry.Anchors.Count == 1)?.Display;
        var created = false;
        if (zero is null)
        {
            var at = axis == PositionAxis.Horizontal
                ? new SheetPoint(left, top + HolePositionPlanner.OrdinateOffset)
                : new SheetPoint(left - HolePositionPlanner.OrdinateOffset, top);
            zero = DimensionEdit.CreateOrdinateZero(api, scan, datumEdge, axis == PositionAxis.Horizontal, at);
            if (zero is null)
                return (0, 0, stations.Count);
            created = true;
            members = [];
        }

        var groupValues = members.Select(item => Math.Abs(item.Geometry.Value)).Append(0.0).ToList();
        var missing = OutlinePlanner.MissingFromGroup(stations, station => station.Value, groupValues);
        if (missing.Count == 0)
            return (0, stations.Count, 0);

        var before = DimensionEdit.OrdinateNames(api, scan);
        DimensionEdit.ExtendOrdinate(api, scan, zero, () =>
            missing.All(station => DimensionEdit.SelectEdge(api, scan, station.Edge, true)));
        var added = DimensionEdit.NewOrdinates(api, scan, before);
        // 圆弧的圆心可能在基准外侧（凹弧），值取绝对值比。
        var expected = missing.Select(station => station.Value).Order().ToList();
        if (HolePositionPlanner.SameValues(expected, DimensionEdit.OrdinateValues(api, added).Select(Math.Abs).Order().ToList()))
            return (added.Count + (created ? 1 : 0), stations.Count - missing.Count, 0);

        DimensionEdit.Discard(api, scan, added);
        if (created)
            DimensionEdit.Discard(api, scan, zero);
        return (0, stations.Count - missing.Count, missing.Count);
    }

    internal const int SelectCenterMark = 100;

    internal const int VerticalOrdinate = 2;

    internal const int HorizontalOrdinate = 3;
}

/// <summary>尺寸链模式往坐标尺寸组里加的一站：选哪条边、离基准多远（模型长度，米，正值）。</summary>
internal readonly record struct GroupStop(object Edge, double Value);
