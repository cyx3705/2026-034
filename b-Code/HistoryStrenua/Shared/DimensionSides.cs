namespace HistoryStrenua;

/// <summary>
/// 位置类尺寸（孔位、方形槽、销孔间、圆心位置）放视图哪一侧（1.23.0，用户定）：水平尺寸可以放上方也可以放下方，竖直尺寸可以放左侧也可以放右侧，
/// 不再整组挤在一侧。逐组比：先比压到图纸禁区几处（避障开着时），一样多就量尺寸界线（引线）总长，选短的那一侧；
/// 一侧层数多了，再往这侧放的界线自然就长，两侧因此均衡。完全一样长时放上方 / 左侧（老位置）。
/// </summary>
/// <param name="Left">左侧基准 X（图纸，米）。</param>
/// <param name="Top">上侧基准 Y。</param>
/// <param name="Right">视图零件轮廓最右沿 X。</param>
/// <param name="Bottom">视图零件轮廓最下沿 Y。</param>
/// <param name="FirstTop">上方第一层离 <see cref="Top"/> 多远。</param>
/// <param name="FirstLeft">左侧第一层离 <see cref="Left"/> 多远。</param>
/// <param name="FirstBottom">下方第一层离 <see cref="Bottom"/> 多远。</param>
/// <param name="FirstRight">右侧第一层离 <see cref="Right"/> 多远。</param>
internal sealed record DimensionSides(
    double Left, double Top, double Right, double Bottom,
    double FirstTop, double FirstLeft, double FirstBottom, double FirstRight)
{
    /// <summary>四边第一层都是孔位尺寸的第一层（<see cref="HolePositionPlanner.FirstTier"/>）。</summary>
    public static DimensionSides Uniform(double left, double top, double right, double bottom)
        => new(left, top, right, bottom, HolePositionPlanner.FirstTier, HolePositionPlanner.FirstTier, HolePositionPlanner.FirstTier, HolePositionPlanner.FirstTier);

    /// <summary>
    /// 第 <paramref name="tier"/> 层尺寸线的位置：水平尺寸给 Y（上方往上、下方往下），竖直尺寸给 X（左侧往左、右侧往右）。
    /// </summary>
    /// <param name="axis">尺寸方向。</param>
    /// <param name="far">放下方 / 右侧。</param>
    /// <param name="tier">这一侧的第几层（0 起）。</param>
    public double Line(PositionAxis axis, bool far, int tier)
    {
        var step = tier * HolePositionPlanner.TierStep;
        return axis == PositionAxis.Horizontal
            ? far ? Bottom - FirstBottom - step : Top + FirstTop + step
            : far ? Right + FirstRight + step : Left - FirstLeft - step;
    }

    /// <summary>从图上一点（孔心、边端）引到尺寸线的界线长：水平尺寸量竖直距离，竖直尺寸量水平距离。</summary>
    public static double Leader(PositionAxis axis, double line, SheetPoint from)
        => axis == PositionAxis.Horizontal ? Math.Abs(from.Y - line) : Math.Abs(from.X - line);

    /// <summary>引线总长相差不到这么多（图纸 0.01 mm）算一样长。</summary>
    public const double LengthTolerance = 1e-5;

    /// <summary>
    /// 放另一侧（下方 / 右侧）更好吗：压禁区少的一侧优先，一样多时引线总长短的一侧优先，一样长留在上方 / 左侧。
    /// </summary>
    public static bool PreferFar(int nearHits, double nearLength, int farHits, double farLength)
        => farHits != nearHits ? farHits < nearHits : farLength < nearLength - LengthTolerance;
}
