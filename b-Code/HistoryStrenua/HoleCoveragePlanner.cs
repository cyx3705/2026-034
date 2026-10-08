namespace HistoryStrenua;

/// <summary>
/// 孔在零件里是哪一个（1.15.0）：组件 + 孔轴线（零件坐标，米）。同一个孔在顶视图、底视图里各是一个圆，图纸坐标对不上，
/// 按轴线才认得是同一个。方向取规范符号（第一个不为 0 的分量为正），点取轴线上离原点最近的那点——两头看同一个孔得出同一条线。
/// </summary>
internal readonly record struct HoleAxis(string Component, ModelDirection Direction, ModelDirection Point)
{
    /// <summary>两条轴线算同一条：方向平行、相距不到 0.02 mm（与 <see cref="PartHole.AxisTolerance"/> 一致）。</summary>
    public const double Tolerance = 2e-5;

    /// <param name="component">组件名（零件视图为空）。</param>
    /// <param name="point">轴线上任意一点。</param>
    /// <param name="direction">轴向（不必归一）。</param>
    public static HoleAxis Of(string component, ModelDirection point, ModelDirection direction)
    {
        var a = direction.Normalized();
        var first = Math.Abs(a.X) > 1e-6 ? a.X : Math.Abs(a.Y) > 1e-6 ? a.Y : a.Z;
        if (first < 0)
            a = new ModelDirection(-a.X, -a.Y, -a.Z);
        var along = point.Dot(a);
        return new HoleAxis(component, a, new ModelDirection(point.X - along * a.X, point.Y - along * a.Y, point.Z - along * a.Z));
    }

    public bool Same(HoleAxis other)
    {
        if (!string.Equals(Component, other.Component, StringComparison.Ordinal) || Math.Abs(Direction.Dot(other.Direction)) < 0.9999)
            return false;
        // 两轴平行时，other 上一点到这条轴线的距离 = |(Q − P) × a|。
        var d = new ModelDirection(other.Point.X - Point.X, other.Point.Y - Point.Y, other.Point.Z - Point.Z);
        var cross = PartHole.Cross(d, Direction);
        return Math.Sqrt(cross.Dot(cross)) <= Tolerance;
    }

    /// <summary>零件里的这个孔（<see cref="PartHole"/>）是不是这条轴线上的。</summary>
    public bool Matches(PartHole hole) => Same(Of(Component, hole.Point, hole.Axis));
}

/// <summary>同一页上另一个看得见同一批孔的视图（1.15.0）：视线与这个视图平行（同向或相反，如顶视图与底视图）、引用同一个模型。</summary>
/// <param name="Name">视图名（回执用）。</param>
/// <param name="Normal">视线（图纸 +Z 在模型里的方向，朝看图人）。</param>
/// <param name="Holes">它看得见（正对图纸）的孔。</param>
/// <param name="CalledOut">它里面已有孔标注的孔。</param>
/// <param name="Positioned">它里面已有位置尺寸（线性 / 坐标尺寸连着孔边或穿过孔心的线）的孔。</param>
/// <param name="PositionedKinds">
/// 它里面有孔标过位置的种（<see cref="HoleEdge.Kind"/>）。阵列标法「4 x 75 =300」只连首尾两个孔，中间的孔没有尺寸连着却已定位（真机模组立板），
/// 所以按种算：这种孔在那个视图里标过位置，它看得见的同种孔都算标过。
/// </param>
internal sealed record CoveringView(
    string Name, ModelDirection Normal, IReadOnlyList<HoleAxis> Holes, IReadOnlyList<HoleAxis> CalledOut, IReadOnlyList<HoleAxis> Positioned,
    IReadOnlyCollection<string>? PositionedKinds = null);

/// <summary>一种孔的孔标注该不该标在这个视图里（1.15.0）。</summary>
internal enum CalloutPlace
{
    /// <summary>标在这里。</summary>
    Here,

    /// <summary>标在这里，沉孔在背面：孔标注后面写「(反面)」（这一页没有看得见沉孔那面的视图）。</summary>
    HereBack,

    /// <summary>别的视图已标过这种孔：不再标（用户定：同一孔标注不在两个视图里重复）。</summary>
    Elsewhere,

    /// <summary>沉孔在这个视图的背面，这一页另有看得见沉孔的视图：留给那个视图标。</summary>
    Deferred,
}

/// <summary>
/// 孔类跨视图不重复（1.15.0，用户定）：同一页上顶视图、底视图这类视线平行的视图都看得见同一批孔，孔标注与孔位尺寸只标一处——
/// 别的视图已标过的就不再标。沉孔在这一面的背面时，孔标注优先留给看得见沉孔的那个视图；没有那样的视图才标在这里、写「(反面)」。
/// 纯几何，不碰 SolidWorks，能离线测。
/// </summary>
/// <remarks>
/// 只比视线平行的视图：别的方向的视图里孔是侧着的两条线，不会有孔标注也不会有连孔边的位置尺寸。
/// 不删别的视图已有的标注（可能是用户手工标的）；先标先得，所以「孔标注全流程」按视图顺序做，后面的视图跳过前面标过的。
/// </remarks>
internal static class HoleCoveragePlanner
{
    /// <summary>两条视线算平行、沉孔算朝着看图人的余弦。</summary>
    public const double ParallelCosine = 0.999;

    /// <summary>
    /// 沉孔在背面时孔标注的后缀（用户定写在沉孔那一行）。写在 SolidWorks 孔标注的后缀里：后缀接在整段文字最后，
    /// 柱形沉头孔的孔标注最后一行就是沉孔那一行（「⌴⌀9.5↓5.5 (反面)」）。与「(含对面)」同在时排在它前面。
    /// </summary>
    public const string BackSuffix = " (反面)";

    /// <summary>认后缀时去掉空格比较的写法。</summary>
    public const string BackMarker = "(反面)";

    /// <summary>沉孔朝背面（离看图人那一头）。</summary>
    public static bool IsBack(ModelDirection? counterbore, ModelDirection normal)
        => counterbore is { } toward && toward.Dot(normal) <= -ParallelCosine;

    /// <summary>显示出来的文字里已经有「(反面)」了吗（空格不论）。</summary>
    public static bool HasBackMarker(IEnumerable<string> texts)
        => texts.Any(text => text.Replace(" ", string.Empty, StringComparison.Ordinal).Contains(BackMarker, StringComparison.Ordinal));

    /// <summary>
    /// 别的视图里标过位置的孔（<see cref="HoleEdge.Axis"/> 读不到的不算）：有尺寸连着它，或那个视图看得见它、且同种孔在那里标过位置
    /// （<see cref="CoveringView.PositionedKinds"/>）。
    /// </summary>
    public static bool PositionedElsewhere(HoleEdge hole, IReadOnlyList<CoveringView> views)
        => hole.Axis is { } axis && views.Any(view => view.Positioned.Any(axis.Same)
                                                  || ((view.PositionedKinds?.Contains(hole.Kind) ?? false) && view.Holes.Any(axis.Same)));

    /// <summary>
    /// 一种孔（<paramref name="kind"/>，同种的全部孔）的孔标注放哪。
    /// </summary>
    /// <param name="kind">这一种在这个视图里的孔。</param>
    /// <param name="views">同一页上视线平行的别的视图。</param>
    /// <param name="counterbore">
    /// 这种孔的沉孔朝哪头（零件坐标里沿轴、指向沉孔那一端的单位向量，<see cref="PartHole.Counterbore"/>）；不是沉孔为 null。
    /// </param>
    /// <param name="normal">这个视图的视线（朝看图人）。</param>
    public static (CalloutPlace Place, string? View) Callout(
        IReadOnlyList<HoleEdge> kind, IReadOnlyList<CoveringView> views, ModelDirection? counterbore, ModelDirection normal)
    {
        var axes = kind.Select(hole => hole.Axis).OfType<HoleAxis>().ToList();
        if (views.FirstOrDefault(view => view.CalledOut.Any(done => axes.Any(done.Same))) is { } labeled)
            return (CalloutPlace.Elsewhere, labeled.Name);
        if (counterbore is not { } toward || !IsBack(toward, normal))
            return (CalloutPlace.Here, null);
        // 沉孔在背面：这一页有从沉孔那面看、看得见这种孔的视图就留给它。
        var facing = views.FirstOrDefault(view => toward.Dot(view.Normal) >= ParallelCosine && view.Holes.Any(seen => axes.Any(seen.Same)));
        return facing is not null ? (CalloutPlace.Deferred, facing.Name) : (CalloutPlace.HereBack, null);
    }

    /// <summary>
    /// 视图里一种孔的沉孔朝哪头：这种孔对得上的零件孔（按轴线）里第一个是沉孔的。读不到为 null。
    /// </summary>
    public static ModelDirection? Counterbore(IReadOnlyList<HoleEdge> kind, PartGeometry? part)
    {
        if (part is null)
            return null;
        foreach (var hole in kind)
        {
            if (hole.Axis is not { } axis)
                continue;
            if (part.Holes.FirstOrDefault(axis.Matches) is { Counterbore: { } toward })
                return toward;
        }

        return null;
    }
}
