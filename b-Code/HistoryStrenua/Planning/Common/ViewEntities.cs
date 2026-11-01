namespace HistoryStrenua;

/// <summary>视图里一条候选孔边投影到图纸上的样子。坐标与半径都是图纸空间，单位米。</summary>
/// <param name="Index">回指调用方手里那条边。</param>
/// <param name="X">圆心 X。</param>
/// <param name="Y">圆心 Y。</param>
/// <param name="Radius">图纸上的半径（已乘视图比例）。</param>
/// <param name="Kind">
/// 孔的「种」：孔壁所属的组件与特征。同一种里同孔径的孔只标一次——SolidWorks 的孔标注
/// 会自己在前面写上「N×」，每个都标就是把同一行字重复 N 遍。
/// </param>
/// <param name="BulgeX">腰型孔端头半圆朝哪边鼓（图纸上的单位向量 X）；整圈的圆孔为 0。</param>
/// <param name="BulgeY">同上，Y。</param>
/// <param name="Slot">
/// 腰型孔编号（1.3.0 起）：配成一对的两个端头编号相同；圆孔为 -1。由 <see cref="SlotPlanner.Pair"/> 填。
/// </param>
/// <param name="Dowel">孔壁是异形孔向导的销钉孔（1.6.0 起，判法见 <c>ViewGeometry</c>）。</param>
/// <param name="Axis">孔在零件里的轴线（1.15.0，跨视图认同一个孔用，<see cref="HoleCoveragePlanner"/>）；读不到为 null。</param>
/// <param name="Split">整圆孔被切成两段半圆（1.25.0，<see cref="SplitHole"/>）：边只是其中一段，孔标注写不对，要标直径尺寸。</param>
internal readonly record struct HoleEdge(
    int Index, double X, double Y, double Radius, string Kind = "", double BulgeX = 0, double BulgeY = 0, int Slot = -1, bool Dowel = false,
    HoleAxis? Axis = null, bool Split = false)
{
    /// <summary>是腰型孔端头的半圆（不是整圈的圆孔）。</summary>
    public bool IsSlotEnd => BulgeX != 0 || BulgeY != 0;
}

/// <summary>视图里一段圆角弧（图纸坐标，米）。</summary>
/// <param name="Index">回指调用方手里那条边。</param>
/// <param name="Center">圆心。</param>
/// <param name="Radius">图纸上的半径（已乘比例）。</param>
/// <param name="ModelRadius">模型半径（尺寸值）。</param>
/// <param name="Middle">弧中点。</param>
/// <param name="Concave">内凹（内圆角、凹弧：圆心在零件外）；外圆角为 false。</param>
/// <param name="Sweep">圆心角（弧度，1.12.0）：带一个端点的整圈为 2π；0 = 没量（不参与整圆判定）。</param>
/// <param name="Start">起点（1.14.2「圆心位置」判两端相切用）；没有端点的整圈为 null。</param>
/// <param name="End">终点；没有端点的整圈为 null。</param>
internal readonly record struct FilletArc(
    int Index, SheetPoint Center, double Radius, double ModelRadius, SheetPoint Middle, bool Concave, double Sweep = 0,
    SheetPoint? Start = null, SheetPoint? End = null);

/// <summary>视图里一条倒角斜边（1.10.0）：倒角面侧着看成的那条斜线（图纸坐标，米）。</summary>
/// <param name="Index">回指调用方手里那条边。</param>
/// <param name="Segment">斜边在图纸上的两端。</param>
/// <param name="LegX">沿图纸横向的直角边（模型长度，米）。</param>
/// <param name="LegY">沿图纸竖向的直角边（模型长度，米）。</param>
/// <param name="Key">倒角面是哪一张（组件 + 面在零件坐标里的包围盒），跨视图不重复标用。</param>
internal readonly record struct ChamferEdge(int Index, SheetSegment Segment, double LegX, double LegY, string Key)
{
    public SheetPoint Middle => new((Segment.X1 + Segment.X2) / 2, (Segment.Y1 + Segment.Y2) / 2);

    /// <summary>两条直角边，短的在前（同尺寸的合标、C1 判定用）。</summary>
    public (double Short, double Long) Size => LegX <= LegY ? (LegX, LegY) : (LegY, LegX);

    /// <summary>两条直角边一样长（45° 倒角，写「C5」）。</summary>
    public bool Equal => Math.Abs(LegX - LegY) <= ChamferPlanner.SizeTolerance;
}

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
