namespace HistoryStrenua;

/// <summary>模型（视图所引用的顶层模型）坐标系里的一个单位方向。</summary>
internal readonly record struct ModelDirection(double X, double Y, double Z)
{
    public double Dot(ModelDirection other) => X * other.X + Y * other.Y + Z * other.Z;

    /// <summary>归一；长度为 0 时原样返回。</summary>
    public ModelDirection Normalized()
    {
        var length = Math.Sqrt(Dot(this));
        return length > 0 ? new ModelDirection(X / length, Y / length, Z / length) : this;
    }
}

/// <summary>
/// 模型里垂直于 <see cref="Direction"/>、沿它离原点 <see cref="Offset"/>（米）的平面——外轮廓的一条竖直 / 水平边在模型里就是这样一个面。
/// 方向一律取规范符号（第一个不为 0 的分量为正），两个视图看同一个面方向相反（左视、右视）时也认得是同一个。
/// </summary>
internal readonly record struct ModelPlane(ModelDirection Direction, double Offset)
{
    /// <summary>方向分量当作 0 的界限。</summary>
    private const double Zero = 1e-6;

    public static ModelPlane Of(ModelDirection direction, double offset)
    {
        var flip = Math.Abs(direction.X) > Zero ? direction.X < 0
            : Math.Abs(direction.Y) > Zero ? direction.Y < 0
            : direction.Z < 0;
        return flip
            ? new ModelPlane(new ModelDirection(-direction.X, -direction.Y, -direction.Z), -offset)
            : new ModelPlane(direction, offset);
    }

    /// <summary>同一个面：方向平行、位置差在 <see cref="DimensionGeometry.ValueTolerance"/> 内。</summary>
    public bool Same(ModelPlane other)
        => Math.Abs(Direction.Dot(other.Direction)) >= 1 - Zero && Math.Abs(Offset - other.Offset) <= DimensionGeometry.ValueTolerance;
}

/// <summary>
/// 视图的朝向（1.8.1）：图纸 X、Y 方向与视线在视图所引用模型（装配视图是顶层装配）坐标系里是哪个方向，模型原点落在图纸哪里。
/// </summary>
/// <param name="SheetX">图纸 +X 在模型里的方向。</param>
/// <param name="SheetY">图纸 +Y 在模型里的方向。</param>
/// <param name="Normal">图纸 +Z（朝看图的人）在模型里的方向。</param>
/// <param name="Origin">模型原点在图纸上的位置。</param>
/// <param name="Scale">视图比例（图纸长度 / 模型长度）。</param>
internal sealed record ViewFrame(ModelDirection SheetX, ModelDirection SheetY, ModelDirection Normal, SheetPoint Origin, double Scale)
{
    /// <summary>视线分量超过它才算「不为 0」（约 1.1°）。</summary>
    public const double AxisComponent = 0.02;

    /// <summary>
    /// 轴测图：视线的三个分量都不为 0（等轴测、二等角、三等角，以及斜着摆的命名视图）。
    /// 正视、投影、剖视、局部视图的视线沿一根模型轴；辅助视图的视线垂直于某一根轴（有一个分量为 0）——都不是。
    /// </summary>
    public bool Axonometric
        => Math.Abs(Normal.X) > AxisComponent && Math.Abs(Normal.Y) > AxisComponent && Math.Abs(Normal.Z) > AxisComponent;

    /// <summary>图纸上沿 <paramref name="axis"/> 坐标为 <paramref name="coordinate"/> 的那条线（竖直边的 X、水平边的 Y）在模型里是哪个面。</summary>
    public ModelPlane Plane(PositionAxis axis, double coordinate)
    {
        var (direction, origin) = axis == PositionAxis.Horizontal ? (SheetX, Origin.X) : (SheetY, Origin.Y);
        return ModelPlane.Of(direction, (coordinate - origin) / Scale);
    }
}

/// <summary>
/// 外轮廓跨视图去重（1.8.1，用户定：高度在视图 a 标了就别在视图 b 再标）：别的视图里已有的尺寸把哪些模型面的相对位置定下来了。
/// </summary>
/// <remarks>
/// <para>每个两头都是模型直边的线性 / 坐标尺寸，把它两头那两个面连起来（并查集）。一个外轮廓站（基准面 → 站面）
/// 只要这两个面在别的视图里已经连通——直接标过，或经别的面串起来（总高 + 台阶）——就算标过，不再标。
/// 按面认而不是按数值认：左视图、右视图基准在零件两头，量出来的数不同，定的却是同一组面。</para>
/// <para>只收同一个模型（同一文件、同一配置）的视图；轴测图不收（它不在全流程里，尺寸也不该标在那儿）。连孔、连中心线的尺寸不收。</para>
/// </remarks>
internal sealed class OutlineCoverage
{
    private readonly List<ModelPlane> _planes = [];
    private readonly List<int> _parent = [];

    /// <summary>收进来的尺寸个数（回执用）。</summary>
    public int Dimensions { get; private set; }

    /// <summary>收一个视图里的尺寸。</summary>
    public void AddView(ViewFrame frame, IReadOnlyList<ViewDimension> dimensions)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(dimensions);
        foreach (var dimension in dimensions)
        {
            if (!(dimension.Linear || dimension.Ordinate) || dimension.Anchors.Count < 2
                || !dimension.Anchors[0].ModelLine || !dimension.Anchors[1].ModelLine)
                continue;
            var counted = false;
            foreach (var axis in new[] { PositionAxis.Horizontal, PositionAxis.Vertical })
            {
                if (!DimensionGeometry.Measures(dimension, axis, frame.Scale)
                    || dimension.Anchors[0].Along(axis) is not { } a
                    || dimension.Anchors[1].Along(axis) is not { } b)
                    continue;
                Connect(frame.Plane(axis, a), frame.Plane(axis, b));
                counted = true;
            }

            if (counted)
                Dimensions++;
        }
    }

    public void Connect(ModelPlane a, ModelPlane b)
    {
        var (x, y) = (Find(Node(a)), Find(Node(b)));
        if (x != y)
            _parent[x] = y;
    }

    /// <summary>两个面的相对位置已经定了（同一个面也算）。</summary>
    public bool Connected(ModelPlane a, ModelPlane b)
    {
        if (a.Same(b))
            return true;
        var x = _planes.FindIndex(plane => plane.Same(a));
        var y = _planes.FindIndex(plane => plane.Same(b));
        return x >= 0 && y >= 0 && Find(x) == Find(y);
    }

    /// <summary>
    /// 本视图里沿 <paramref name="axis"/> 坐标 <paramref name="from"/>、<paramref name="to"/> 的两条边之间的尺寸，别的视图是不是已经定了。
    /// </summary>
    public bool Determined(ViewFrame frame, PositionAxis axis, double from, double to)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return Connected(frame.Plane(axis, from), frame.Plane(axis, to));
    }

    /// <summary>站分成两份：还要标的、别的视图已经定了的（基准：竖直边量 <paramref name="left"/> 起，水平边量 <paramref name="top"/> 起）。</summary>
    public (List<OutlineStation> Remaining, List<OutlineStation> Covered) Split(
        ViewFrame frame, IReadOnlyList<OutlineStation> stations, double left, double top)
    {
        ArgumentNullException.ThrowIfNull(stations);
        var remaining = new List<OutlineStation>();
        var covered = new List<OutlineStation>();
        foreach (var station in stations)
        {
            var datum = station.Axis == PositionAxis.Horizontal ? left : top;
            (Determined(frame, station.Axis, datum, station.Coordinate) ? covered : remaining).Add(station);
        }

        return (remaining, covered);
    }

    /// <summary>这个面所在的连通块编号（没收过的面先收进来、自成一块）。1.16.0「未标尺寸」按它找主尺寸网。</summary>
    public int Component(ModelPlane plane) => Find(Node(plane));

    private int Node(ModelPlane plane)
    {
        var index = _planes.FindIndex(existing => existing.Same(plane));
        if (index >= 0)
            return index;
        _planes.Add(plane);
        _parent.Add(_parent.Count);
        return _planes.Count - 1;
    }

    private int Find(int index)
    {
        while (_parent[index] != index)
            index = _parent[index] = _parent[_parent[index]];
        return index;
    }
}
