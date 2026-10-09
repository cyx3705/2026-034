using HistoryStrenua.SolidWorks;

namespace HistoryStrenua;

/// <summary>
/// 孔类跨视图不重复（1.15.0，用户定）的 SolidWorks 那一半：读同一页上视线与这个视图平行、引用同一个模型的别的视图（顶视图对底视图），
/// 它们看得见哪些孔、哪些孔已有孔标注、哪些孔已有位置尺寸。怎么用见 <see cref="HoleCoveragePlanner"/>。
/// </summary>
/// <remarks>
/// 别的视图的孔照「孔标注」一样认（<see cref="HoleScan.Scan"/>，不报进度），孔按零件里的轴线（<see cref="HoleAxis"/>）对到这个视图。
/// 位置尺寸 = 线性 / 坐标尺寸连着孔边或穿过孔心的线（中心符号线、中心线），与「孔位尺寸」删旧时的认法相同；孔标注不算位置尺寸。
/// 视线不平行的视图里孔是侧着的，不读（也省得每条指令把整页视图都认一遍）。
/// </remarks>
internal static class HoleCoverage
{
    /// <summary>同一页上与 <paramref name="scan"/> 视线平行、引用同一模型的别的视图。读不了的视图跳过。</summary>
    public static IReadOnlyList<CoveringView> Read(QuickCommandContext context, ScannedView scan)
    {
        var api = context.Api;
        var model = HoleScan.ModelKey(api, scan.View);
        var normal = scan.Geometry.Frame.Normal;
        var views = new List<CoveringView>();
        foreach (var view in HoleScan.SheetViews(api, scan.Document))
        {
            context.Cancellation.ThrowIfCancellationRequested();
            var name = DrawingSheet.Name(api, view);
            if (name == scan.ViewName || api.Call(view, "IView", "get_ReferencedDocument") is null || HoleScan.ModelKey(api, view) != model)
                continue;
            ScannedView other;
            try
            {
                if (Math.Abs(HoleScan.Frame(context, view).Normal.Dot(normal)) < HoleCoveragePlanner.ParallelCosine)
                    continue;
                other = HoleScan.Scan(context, string.Empty, view: view, quiet: true);
            }
            catch (QuickCommandException)
            {
                continue;
            }

            views.Add(Read(api, other));
        }

        return views;
    }

    private static CoveringView Read(SolidWorksApi api, ScannedView scan)
    {
        var holes = scan.Candidates;
        var calledOut = new List<HoleAxis>();
        var positioned = new List<HoleAxis>();
        var positionedKinds = new HashSet<string>(StringComparer.Ordinal);
        void Add(List<HoleAxis> into, Func<HoleEdge, bool> hit)
        {
            foreach (var hole in holes)
            {
                if (hole.Axis is not { } axis || !hit(hole))
                    continue;
                if (into == positioned)
                    positionedKinds.Add(hole.Kind);
                if (!into.Any(axis.Same))
                    into.Add(axis);
            }
        }

        for (var display = api.Call(scan.View, "IView", "GetFirstDisplayDimension5"); display is not null; display = api.Call(display, "IDisplayDimension", "GetNext5"))
        {
            if (api.Call(display, "IDisplayDimension", "GetAnnotation") is not { } annotation)
                continue;
            var centers = HoleCallout.AttachedCircleCenters(api, scan.Geometry, annotation).ToList();
            bool AtCenter(HoleEdge hole) => centers.Any(center => HoleCalloutPlanner.SameCenter(center, new SheetPoint(hole.X, hole.Y)));
            if (api.CallBool(display, "IDisplayDimension", "IsHoleCallout"))
            {
                Add(calledOut, AtCenter);
                continue;
            }

            if (!HolePositionPlanner.IsPosition(api.CallInt(display, "IDisplayDimension", "get_Type2")))
                continue;
            var lines = HolePosition.AttachedLines(api, scan, annotation);
            Add(positioned, hole => AtCenter(hole) || lines.Any(line => HolePositionPlanner.PassesThrough(line, hole)));
        }

        return new CoveringView(scan.ViewName, scan.Geometry.Frame.Normal,
            holes.Select(hole => hole.Axis).OfType<HoleAxis>().ToList(), calledOut, positioned, positionedKinds);
    }
}
