using System.Globalization;

namespace HistoryStrenua;

/// <summary>孔位尺寸量的是哪个方向。</summary>
internal enum PositionAxis
{
    /// <summary>水平尺寸：量孔离左侧基准的 X 位置，放在视图上方。</summary>
    Horizontal,

    /// <summary>竖直尺寸：量孔离上侧基准的 Y 位置，放在视图左侧。</summary>
    Vertical,
}

/// <summary>要加的一个孔位尺寸。</summary>
/// <param name="Axis">方向。</param>
/// <param name="FromEdgeIndex">起点孔边的下标；null 表示从基准边量。</param>
/// <param name="ToEdgeIndex">终点孔边的下标。</param>
/// <param name="TextAt">尺寸文字放在图纸上的位置（米）。</param>
/// <param name="Prefix">尺寸值前面的文字；阵列标法是「(N-1) x 间距 =」，其余为空。</param>
internal sealed record PositionDimension(PositionAxis Axis, int? FromEdgeIndex, int ToEdgeIndex, SheetPoint TextAt, string Prefix);

/// <summary>
/// 尺寸链模式下一个方向的一组 SolidWorks「尺寸链」（坐标尺寸，<c>swOrdinateDimension</c>）：
/// 0 点是基准边（水平组用左侧直边、竖直组用上侧直边），其后依次是 <paramref name="EdgeIndexes"/> 的孔，在 <paramref name="At"/> 一次建成整组。
/// </summary>
/// <param name="Axis">方向：水平组在视图上方量各列的 X，竖直组在视图左侧量各行的 Y。</param>
/// <param name="EdgeIndexes">每一列（行）一个孔边下标，按离基准由近到远（不含 0 点）。</param>
/// <param name="Values">每一站离基准的距离（模型长度，米），与 <paramref name="EdgeIndexes"/> 一一对应，用来核对建出来的值。</param>
/// <param name="At">0 点那个尺寸文字的位置（图纸坐标，米）；整组文字排成一列，SolidWorks 自己折弯错开。</param>
internal sealed record OrdinateGroup(PositionAxis Axis, IReadOnlyList<int> EdgeIndexes, IReadOnlyList<double> Values, SheetPoint At)
{
    /// <summary>这组的尺寸个数：0 点一个，加每站一个。</summary>
    public int Count => EdgeIndexes.Count + 1;
}

/// <summary>视图里已有的一个尺寸：它的类型（<c>swDimensionType_e</c>）与它连着的圆边圆心、中心线。</summary>
/// <param name="Index">回指调用方手里那个尺寸。</param>
/// <param name="Type"><c>IDisplayDimension.Type2</c>。</param>
/// <param name="Centers">它附着的圆边的圆心（图纸坐标）。</param>
/// <param name="Lines">它附着的视图草图线（中心符号线的线、中心线），图纸坐标。</param>
/// <param name="Dangling">悬空：至少一头的附着丢了（<c>IAnnotation.IsDangling</c>，或附着对象读出来是空的）。</param>
/// <param name="Attached">坐标尺寸每个附着对象的几何写成的键（直边两端、圆心或草图线）；同组的坐标尺寸都含组的 0 点那个键。其余尺寸为 null。</param>
internal sealed record ExistingDimension(
    int Index, int Type, IReadOnlyList<SheetPoint> Centers, IReadOnlyList<SheetSegment>? Lines = null, bool Dangling = false,
    IReadOnlyList<string>? Attached = null);

/// <summary>规划结果。</summary>
/// <param name="Dimensions">要加的线性尺寸，先水平后竖直，同方向按层由里到外；尺寸链模式下为空。</param>
/// <param name="HoleCount">视图里认出的孔数（同心的算一个，一个腰型孔算一个）。</param>
/// <param name="KindCount">孔的种数。</param>
/// <param name="PatternCount">用了阵列标法的尺寸个数。</param>
/// <param name="SlotCount">其中腰型孔的个数。</param>
/// <param name="Ordinates">尺寸链模式（1.7.0）的坐标尺寸组，先水平后竖直；其余模式为空。</param>
internal sealed record HolePositionPlan(
    IReadOnlyList<PositionDimension> Dimensions, int HoleCount, int KindCount, int PatternCount, int SlotCount = 0,
    IReadOnlyList<OrdinateGroup>? Ordinates = null)
{
    /// <summary>回执里的「N 个孔（含 M 个腰型孔）共 K 种」。</summary>
    public string Summary => HoleCalloutPlanner.Summary(HoleCount, SlotCount, KindCount);

    /// <summary>坐标尺寸组（没有时为空表）。</summary>
    public IReadOnlyList<OrdinateGroup> OrdinateGroups => Ordinates ?? [];

    /// <summary>要加的尺寸总数：线性尺寸，加上坐标尺寸组里每一站（含 0 点）一个。</summary>
    public int Count => Dimensions.Count + OrdinateGroups.Sum(group => group.Count);
}

/// <summary>
/// 「孔位尺寸」的纯几何部分：基准边、每种孔怎么标、尺寸放在哪、哪些旧尺寸要删。
/// 认孔与分种与孔标注完全相同（<see cref="HoleCalloutPlanner.Recognize"/>、<see cref="HoleCalloutPlanner.GroupKinds"/>）。
/// </summary>
/// <remarks>
/// <para>基准是视图里零件最左的竖直直边与最上的水平直边。</para>
/// <para>
/// 每个方向、每种孔单独标：这一种里的孔按坐标归成列（水平方向）或行（竖直方向），
/// 第一列从基准标，其余每列从前一列标（链式）——同种孔可以接着前一个孔标，不同种孔一律从基准起。
/// 同种孔在这个方向上超过 <see cref="PatternThreshold"/> 列且等距时用阵列标法：第一列从基准标，
/// 再从第一列一个尺寸标到最后一列，文字写成「(N-1) x 间距 =总长」。
/// </para>
/// <para>
/// 按中心线标、不按孔标：同一段「中心线 → 中心线」（含基准到中心线）全视图只标一次，
/// 几种孔并排在同一对中心线上时不出重复尺寸。尺寸仍连在孔边上，SolidWorks 量到圆心，值就是中心线间距。
/// </para>
/// <para>
/// 尺寸一种孔占一层，水平的在视图上方、竖直的在视图左侧；跨度短的层在里面，免得尺寸界线互相穿过。
/// 阵列标法那一种占两层：阵列尺寸在里、从基准到第一个孔的那个在外（用户给的样图就是这样）。
/// </para>
/// <para>
/// 腰型孔（1.3.0）只取上方那一端圆弧的圆心当孔位（一样高取左边那端，<see cref="HoleCalloutPlanner.Representatives"/>），
/// 其余规格由孔标注一次写全，不另标长度。
/// </para>
/// </remarks>
internal static class HolePositionPlanner
{
    /// <summary>同种孔在一个方向上超过这么多列（且等距）才用阵列标法。</summary>
    public const int PatternThreshold = 4;

    /// <summary>第一层尺寸离基准边多远（图纸上 8 mm；1.2.0 首版 10 mm，用户嫌远）。</summary>
    public const double FirstTier = 0.008;

    /// <summary>层与层之间多远（图纸上 6 mm；首版 8 mm）。</summary>
    public const double TierStep = 0.006;

    /// <summary>
    /// 尺寸链模式：坐标尺寸文字离零件左侧（上侧）直边多远（图纸上 14 mm）。取自用户的演示图——
    /// 文字端离左边 14 mm，SolidWorks 折弯（5 mm 平 + 5 mm 斜）后在零件边外就回到孔的那条线上。
    /// </summary>
    public const double OrdinateOffset = 0.014;

    /// <summary>判等距的容差：间距之差不超过 0.01 mm（模型尺寸）。</summary>
    public const double PitchTolerance = 1e-5;

    /// <summary>直边算竖直 / 水平的斜率容差。</summary>
    private const double AxisTolerance = 1e-3;

    // swDimensionType_e
    private const int OrdinateDimension = 1;
    private const int LinearDimension = 2;
    private const int HorizontalLinearDimension = 11;
    private const int VerticalLinearDimension = 12;

    /// <param name="edges">视图里正对图纸的孔边（含腰型孔端头的半圆）。</param>
    /// <param name="left">左侧基准边的 X（图纸坐标）。</param>
    /// <param name="top">上侧基准边的 Y（图纸坐标）。</param>
    /// <param name="scale">视图比例（图纸长度 / 模型长度），用来把间距换成模型尺寸写进文字。</param>
    /// <param name="chainMode">尺寸链模式（1.7.0，页面开关）：出坐标尺寸组而不是线性尺寸，见 <see cref="PlanChain"/>。</param>
    public static HolePositionPlan Plan(IReadOnlyList<HoleEdge> edges, double left, double top, double scale, bool chainMode = false)
    {
        ArgumentNullException.ThrowIfNull(edges);
        if (scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale));

        var holes = HoleCalloutPlanner.Recognize(edges);
        var kinds = HoleCalloutPlanner.GroupKinds(HoleCalloutPlanner.Representatives(holes));
        if (chainMode)
            return PlanChain(holes, kinds, left, top, scale);

        var dimensions = new List<PositionDimension>();
        var patterns = 0;
        var datum = new Datum(left, top);
        foreach (var axis in new[] { PositionAxis.Horizontal, PositionAxis.Vertical })
        {
            var chains = kinds
                .Select(kind => Chain(kind, axis, axis == PositionAxis.Horizontal ? left : top, scale))
                .Where(chain => chain.Stops.Count > 0)
                .OrderBy(chain => chain.Reach)
                .ThenBy(chain => chain.Stops[0].Offset)
                .ToList();

            var tier = 0;
            // 已经标过的「中心线 → 中心线」段（基准记为 0）。尺寸按中心线标、不按孔标：
            // 并排的几种孔落在同一对中心线上时，那一段只标一次。
            var spans = new List<(double From, double To)>();
            bool Claim(double from, double to)
            {
                if (to - from <= HoleCalloutPlanner.CenterTolerance
                    || spans.Any(span => Math.Abs(span.From - from) <= HoleCalloutPlanner.CenterTolerance
                                         && Math.Abs(span.To - to) <= HoleCalloutPlanner.CenterTolerance))
                    return false;
                spans.Add((from, to));
                return true;
            }

            foreach (var chain in chains)
            {
                var stops = chain.Stops;
                var first = stops[0];
                var needDatum = Claim(0, first.Offset);
                if (chain.Pattern)
                {
                    var last = stops[^1];
                    var used = 0;
                    if (Claim(first.Offset, last.Offset))
                    {
                        var prefix = PatternPrefix(stops.Count - 1, (last.Offset - first.Offset) / (stops.Count - 1) / scale);
                        dimensions.Add(Dimension(axis, first.Hole.Index, last.Hole, first.Offset, last.Offset, datum, tier, prefix));
                        patterns++;
                        used++;
                    }

                    if (needDatum)
                    {
                        dimensions.Add(Dimension(axis, null, first.Hole, 0, first.Offset, datum, tier + used, string.Empty));
                        used++;
                    }

                    tier += used;
                    continue;
                }

                var added = 0;
                if (needDatum)
                {
                    dimensions.Add(Dimension(axis, null, first.Hole, 0, first.Offset, datum, tier, string.Empty));
                    added++;
                }

                for (var i = 1; i < stops.Count; i++)
                {
                    if (!Claim(stops[i - 1].Offset, stops[i].Offset))
                        continue;
                    dimensions.Add(Dimension(axis, stops[i - 1].Hole.Index, stops[i].Hole, stops[i - 1].Offset, stops[i].Offset, datum, tier, string.Empty));
                    added++;
                }

                if (added > 0)
                    tier++;
            }
        }

        var (holeCount, slotCount) = HoleCalloutPlanner.Count(holes);
        return new HolePositionPlan(dimensions, holeCount, kinds.Count, patterns, slotCount);
    }

    /// <summary>
    /// 尺寸链模式（1.7.0，用户定，按用户给的演示图）：每个方向一组 SolidWorks「尺寸链」（坐标尺寸）——
    /// 0 点是基准边（水平组左侧直边、竖直组上侧直边，用户第四轮指出不是孔），其后每一列（行）孔一个坐标值，
    /// 不分孔的种类、不用阵列写法。同一坐标上的几个孔（不论哪种）算一站；正好落在基准边上的孔不另标。
    /// 竖直组的文字排在左侧直边左边 <see cref="OrdinateOffset"/>，水平组排在上侧直边上方同样远。
    /// </summary>
    private static HolePositionPlan PlanChain(
        IReadOnlyList<HoleEdge> holes, IReadOnlyList<IReadOnlyList<HoleEdge>> kinds, double left, double top, double scale)
    {
        var all = kinds.SelectMany(kind => kind).ToList();
        var groups = new List<OrdinateGroup>();
        foreach (var axis in new[] { PositionAxis.Horizontal, PositionAxis.Vertical })
        {
            var stops = Chain(all, axis, axis == PositionAxis.Horizontal ? left : top, scale).Stops
                .Where(stop => stop.Offset > HoleCalloutPlanner.CenterTolerance)
                .ToList();
            if (stops.Count == 0)
                continue;
            var at = axis == PositionAxis.Horizontal
                ? new SheetPoint(left, top + OrdinateOffset)
                : new SheetPoint(left - OrdinateOffset, top);
            groups.Add(new OrdinateGroup(
                axis, stops.Select(stop => stop.Hole.Index).ToList(), stops.Select(stop => stop.Offset / scale).ToList(), at));
        }

        var (holeCount, slotCount) = HoleCalloutPlanner.Count(holes);
        return new HolePositionPlan([], holeCount, kinds.Count, 0, slotCount, groups);
    }

    /// <summary>阵列标法的文字前缀：「10 x 60 =」，后面紧跟 SolidWorks 自己的尺寸值（总长）。</summary>
    /// <param name="gaps">间距个数（孔数 - 1）。</param>
    /// <param name="pitch">间距，模型长度（米）。</param>
    public static string PatternPrefix(int gaps, double pitch)
        => $"{gaps} x {Math.Round(pitch * 1000, 2).ToString("0.##", CultureInfo.InvariantCulture)} =";

    /// <summary>
    /// 基准边：竖直直边里最靠左的一条、水平直边里最靠上的一条；同一位置有几条取最长的。
    /// </summary>
    /// <returns>两条基准边在 <paramref name="lines"/> 里的下标，找不到为 null。</returns>
    public static (int? Left, int? Top) Datums(IReadOnlyList<SheetSegment> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        int? left = null;
        int? top = null;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var dx = line.X2 - line.X1;
            var dy = line.Y2 - line.Y1;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= HoleCalloutPlanner.CenterTolerance)
                continue;
            if (Math.Abs(dx) / length < AxisTolerance && Better(i, left, line.X1, -1, length))
                left = i;
            else if (Math.Abs(dy) / length < AxisTolerance && Better(i, top, line.Y1, +1, length))
                top = i;
        }

        return (left, top);

        bool Better(int index, int? current, double position, int sign, double length)
        {
            if (current is not { } other)
                return true;
            var o = lines[other];
            var otherPosition = sign < 0 ? o.X1 : o.Y1;
            if (Math.Abs(position - otherPosition) > HoleCalloutPlanner.CenterTolerance)
                return sign * (position - otherPosition) > 0;
            return length > Math.Sqrt(Math.Pow(o.X2 - o.X1, 2) + Math.Pow(o.Y2 - o.Y1, 2));
        }
    }

    /// <summary>
    /// 连着孔的旧孔位尺寸都要删——「重新标」。连着孔 = 连着孔边，或连着穿过孔心的中心线（手工按中心线标的、
    /// 本指令连在中心符号线上的）。只动线性尺寸（水平、竖直、斜的）；孔标注、直径尺寸不是这里的类型，不动；
    /// 不连着孔的尺寸（外形尺寸）也不动。
    /// </summary>
    /// <remarks>
    /// 视图里<b>悬空</b>的线性尺寸也删（1.3.0，用户定）：「中心符号线」删旧符号线时，挂在上面的位置尺寸就悬空了，
    /// 悬空尺寸连着什么已经读不出来，不删就和重标的新尺寸叠在一起。别的原因悬空的线性尺寸会被一并删掉。
    /// <para>坐标尺寸（尺寸链模式标的，或手工标的尺寸链）照线性尺寸一样处理：两种模式来回切换重标时不留旧的。
    /// 0 点那个坐标尺寸只连着基准边、不连孔：要删的坐标尺寸连着的对象（<see cref="ExistingDimension.Attached"/>），
    /// 别的坐标尺寸也连着它，就是同组的 0 点，跟着删。不靠附着顺序：真机上 <c>EditOrdinate</c> 加的成员是 [自己, 0 点]，
    /// 手工标的是 [0 点, 自己]。</para>
    /// </remarks>
    public static List<int> Obsolete(IReadOnlyList<HoleEdge> holes, IReadOnlyList<ExistingDimension> existing)
    {
        var obsolete = existing
            .Where(dimension => IsPosition(dimension.Type))
            .Where(dimension => dimension.Dangling || holes.Any(hole =>
                dimension.Centers.Any(center => HoleCalloutPlanner.SameCenter(center, new SheetPoint(hole.X, hole.Y)))
                || (dimension.Lines ?? []).Any(line => PassesThrough(line, hole))))
            .ToList();
        var shared = obsolete
            .Where(dimension => IsOrdinate(dimension.Type))
            .SelectMany(dimension => dimension.Attached ?? [])
            .ToHashSet(StringComparer.Ordinal);
        return existing
            .Where(dimension => obsolete.Contains(dimension)
                                || (IsOrdinate(dimension.Type) && (dimension.Attached ?? []).Any(shared.Contains)))
            .Select(dimension => dimension.Index)
            .ToList();
    }

    /// <summary>
    /// 是不是线性尺寸（<c>swDimensionType_e</c> 的线性、水平线性、竖直线性）。
    /// 真机上 <c>AddHorizontalDimension2</c> / <c>AddVerticalDimension2</c> 建出来的读回都是 2（线性）。
    /// </summary>
    public static bool IsLinear(int type) => type is LinearDimension or HorizontalLinearDimension or VerticalLinearDimension;

    /// <summary>是不是坐标尺寸（<c>swOrdinateDimension</c>，SolidWorks 中文界面叫「尺寸链」）。</summary>
    public static bool IsOrdinate(int type) => type == OrdinateDimension;

    /// <summary>孔位尺寸会删、会重标的类型：线性尺寸与坐标尺寸。</summary>
    public static bool IsPosition(int type) => IsLinear(type) || IsOrdinate(type);

    /// <summary>
    /// 一组坐标尺寸建出来的值（排好序，模型长度）与规划的一致吗：个数相同、逐个相差不超过 <see cref="PitchTolerance"/>。
    /// 连中心符号线时 SolidWorks 若拿了另一根线（竖直组拿到竖线），值就对不上。
    /// </summary>
    public static bool SameValues(IReadOnlyList<double> expected, IReadOnlyList<double> actual)
        => expected.Count == actual.Count
           && expected.Zip(actual).All(pair => Math.Abs(pair.First - pair.Second) <= PitchTolerance);

    /// <summary>
    /// 中心符号线的线在孔心处留的空隙最多这么长（图纸上 2 mm）：线性中心符号线的连接线一段段断在孔心前，
    /// 真机底板上断开约 1.1 mm（1.7.0）。
    /// </summary>
    public const double CenterGap = 0.002;

    /// <summary>
    /// 线段是否穿过孔心：孔心在线段所在直线上（距离在 <see cref="HoleCalloutPlanner.CenterTolerance"/> 内），
    /// 且落在线段上或离线段端点不超过 <see cref="CenterGap"/>。
    /// </summary>
    public static bool PassesThrough(SheetSegment line, HoleEdge hole)
    {
        var dx = line.X2 - line.X1;
        var dy = line.Y2 - line.Y1;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0)
            return Math.Abs(hole.X - line.X1) <= HoleCalloutPlanner.CenterTolerance && Math.Abs(hole.Y - line.Y1) <= HoleCalloutPlanner.CenterTolerance;
        var (ux, uy) = (dx / length, dy / length);
        var along = (hole.X - line.X1) * ux + (hole.Y - line.Y1) * uy;
        var across = Math.Abs((hole.X - line.X1) * uy - (hole.Y - line.Y1) * ux);
        return across <= HoleCalloutPlanner.CenterTolerance && along >= -CenterGap && along <= length + CenterGap;
    }

    private sealed record Stop(double Offset, HoleEdge Hole);

    private sealed record KindChain(IReadOnlyList<Stop> Stops, bool Pattern)
    {
        public double Reach => Stops.Count == 0 ? 0 : Stops[^1].Offset;
    }

    /// <summary>
    /// 一种孔在一个方向上的「站」：同一坐标的孔算一站，按离基准由近到远排。
    /// 每站挑离尺寸最近的那个孔（水平尺寸在上方，挑最上面的；竖直尺寸在左侧，挑最左的），尺寸界线短。
    /// </summary>
    private static KindChain Chain(IReadOnlyList<HoleEdge> kind, PositionAxis axis, double datum, double scale)
    {
        var stops = new List<Stop>();
        foreach (var hole in kind)
        {
            var offset = axis == PositionAxis.Horizontal ? hole.X - datum : datum - hole.Y;
            var index = stops.FindIndex(stop => Math.Abs(stop.Offset - offset) <= HoleCalloutPlanner.CenterTolerance);
            if (index < 0)
                stops.Add(new Stop(offset, hole));
            else if (axis == PositionAxis.Horizontal ? hole.Y > stops[index].Hole.Y : hole.X < stops[index].Hole.X)
                stops[index] = stops[index] with { Hole = hole };
        }

        stops.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        return new KindChain(stops, stops.Count > PatternThreshold && EvenlySpaced(stops, scale));
    }

    private static bool EvenlySpaced(IReadOnlyList<Stop> stops, double scale)
    {
        var pitch = (stops[^1].Offset - stops[0].Offset) / (stops.Count - 1);
        for (var i = 1; i < stops.Count; i++)
        {
            if (Math.Abs(stops[i].Offset - stops[i - 1].Offset - pitch) / scale > PitchTolerance)
                return false;
        }

        return true;
    }

    /// <summary>水平尺寸放在上侧基准之上、竖直尺寸放在左侧基准之左，第 <paramref name="tier"/> 层；文字居中。</summary>
    private static PositionDimension Dimension(
        PositionAxis axis, int? from, HoleEdge to, double fromOffset, double toOffset, Datum datum, int tier, string prefix)
    {
        var middle = (fromOffset + toOffset) / 2;
        var away = FirstTier + tier * TierStep;
        var textAt = axis == PositionAxis.Horizontal
            ? new SheetPoint(datum.Left + middle, datum.Top + away)
            : new SheetPoint(datum.Left - away, datum.Top - middle);
        return new PositionDimension(axis, from, to.Index, textAt, prefix);
    }

    private readonly record struct Datum(double Left, double Top);
}
