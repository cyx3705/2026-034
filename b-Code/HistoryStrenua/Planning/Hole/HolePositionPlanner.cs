using System.Globalization;

namespace HistoryStrenua;

/// <summary>
/// 「孔位尺寸」的纯几何部分：基准边、每种孔怎么标、尺寸放在哪、哪些旧尺寸要删。
/// 认孔与分种与孔标注完全相同（<see cref="HoleCalloutPlanner.Recognize"/>、<see cref="HoleCalloutPlanner.GroupKinds"/>）。
/// </summary>
/// <remarks>
/// <para>基准是视图里零件最左的竖直直边与最上的水平直边；视图整个关于一根轴对称时（1.14.1），那个方向改以对称轴为基准，见 <see cref="SymmetryPlanner"/>。</para>
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
/// 阵列标法也占一层（1.30.0，用户定：不从基准开始的尺寸必须在尺寸链上）：从基准到第一个孔的那个和阵列尺寸接成一行；1.2.0 起分两层是按旧样图，已作废。
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
    public const double FirstTier = 0.007;

    /// <summary>层与层之间多远（图纸上 6 mm；首版 8 mm）。</summary>
    public const double TierStep = 0.004;

    /// <summary>
    /// 尺寸链模式：坐标尺寸文字离零件左侧（上侧）直边多远（图纸上 14 mm）。取自用户的演示图——
    /// 文字端离左边 14 mm，SolidWorks 折弯（5 mm 平 + 5 mm 斜）后在零件边外就回到孔的那条线上。
    /// </summary>
    public const double OrdinateOffset = 0.014;

    /// <summary>判等距的容差：间距之差不超过 0.01 mm（模型尺寸）。</summary>
    public const double PitchTolerance = 1e-5;

    /// <summary>孔位尺寸读回值与规划值相差超过这么多（模型 0.1 mm）就算连错了。</summary>
    public const double ValueTolerance = 1e-4;

    /// <summary>
    /// 孔位尺寸连对了吗（1.32.4）：按孔心点选中心符号线可能选到旁边一列（真机底层1：x=70.4 的孔连成 x=71，标出 33、37，应为 30、40），
    /// 尺寸照样建得出来，只有值对不上。<paramref name="expected"/> 为 0 表示不核对。
    /// </summary>
    public static bool SameValue(double expected, double actual)
        => expected <= 0 || Math.Abs(expected - actual) <= ValueTolerance;

    /// <summary>直边算竖直 / 水平的斜率容差。</summary>
    private const double AxisTolerance = 1e-3;

    private const int OrdinateDimension = 1;
    private const int LinearDimension = 2;
    private const int HorizontalLinearDimension = 11;
    private const int VerticalLinearDimension = 12;

    /// <param name="edges">视图里正对图纸的孔边（含腰型孔端头的半圆）。</param>
    /// <param name="left">左侧基准边的 X（图纸坐标）。</param>
    /// <param name="top">上侧基准边的 Y（图纸坐标）。</param>
    /// <param name="scale">视图比例（图纸长度 / 模型长度），用来把间距换成模型尺寸写进文字。</param>
    /// <param name="chainMode">尺寸链模式（1.7.0，页面开关）：出坐标尺寸组而不是线性尺寸，见 <see cref="PlanChain"/>。</param>
    /// <param name="symmetric">
    /// 以对称轴为基准的方向（1.14.1，<see cref="SymmetryPlanner"/>）：这个方向上每种孔仍链式互标，但不再有从基准边到第一个孔的尺寸——
    /// 跨过对称轴的那段直接标两侧孔的距离。尺寸链模式不看它。
    /// </param>
    /// <param name="space">
    /// 视图的外沿（最右、最下）与图纸上不许压的地方（避障开着时才有，1.16.0）。给了就两侧都能放（1.23.0，用户定）：
    /// 水平尺寸上方或下方、竖直尺寸左侧或右侧，每种孔各挑一侧——先比压禁区几处，一样多比尺寸界线总长，选短的，见 <see cref="ChooseFar"/>。
    /// null 时照旧全在上方 / 左侧。
    /// </param>
    public static HolePositionPlan Plan(
        IReadOnlyList<HoleEdge> edges, double left, double top, double scale, bool chainMode = false, IReadOnlyCollection<PositionAxis>? symmetric = null,
        PositionSpace? space = null)
    {
        ArgumentNullException.ThrowIfNull(edges);
        if (scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale));

        var holes = HoleCalloutPlanner.Recognize(edges);
        var kinds = HoleCalloutPlanner.GroupKinds(HoleCalloutPlanner.Representatives(holes));
        if (chainMode)
            return PlanChain(holes, kinds, left, top, scale, space);

        var dimensions = new List<PositionDimension>();
        var patterns = 0;
        var datum = new Datum(left, top, scale);
        foreach (var axis in new[] { PositionAxis.Horizontal, PositionAxis.Vertical })
        {
            var chains = kinds
                .Select(kind => Chain(kind, axis, axis == PositionAxis.Horizontal ? left : top, scale))
                .Where(chain => chain.Stops.Count > 0)
                .OrderBy(chain => chain.Reach)
                .ThenBy(chain => chain.Stops[0].Offset)
                .ToList();

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

            // 1.34.0（用户定，推翻 1.32.4）：两组阵列数量不同就各标各的（侧板 5 x 200 和 10 x 100 都要），只有站完全相同才算重复。
            // 粗阵列每站都落在站更多的阵列上时，基准段留给细阵列，两段才在同一条链上。不靠排序：两种孔的跨度差着浮点误差，谁先谁后说不准。
            static bool OnStops(IReadOnlyList<Stop> stops, IReadOnlyList<Stop> others)
                => stops.All(stop => others.Any(other => Math.Abs(other.Offset - stop.Offset) <= HoleCalloutPlanner.CenterTolerance));
            bool Coarser(KindChain chain) => chain.Pattern
                && chains.Any(other => other.Pattern && other.Stops.Count > chain.Stops.Count && OnStops(chain.Stops, other.Stops));
            var patterned = new List<KindChain>();
            var mirrored = symmetric?.Contains(axis) == true;
            // 1.23.0：每种孔（一条链）各自挑放哪一侧，两侧各自由里到外排层。
            var tiers = new int[2];
            foreach (var chain in chains)
            {
                var stops = chain.Stops;
                var first = stops[0];
                // 真机侧板：粗阵列占了基准段，45 在上方、10 x 100 在下方，链就断了。
                var needDatum = !mirrored && !Coarser(chain) && Claim(0, first.Offset);
                var items = new List<Planned>();
                if (chain.Pattern)
                {
                    var last = stops[^1];
                    var located = patterned.Any(other => OnStops(stops, other.Stops) && OnStops(other.Stops, stops));
                    if (Claim(first.Offset, last.Offset) || !located)
                    {
                        var prefix = PatternPrefix(stops.Count - 1, (last.Offset - first.Offset) / (stops.Count - 1) / scale);
                        items.Add(new Planned(first, last, 0, prefix));
                        patterns++;
                    }

                    patterned.Add(chain);
                    if (needDatum)
                        items.Add(new Planned(null, first, 0, string.Empty));
                }
                else
                {
                    if (needDatum)
                        items.Add(new Planned(null, first, 0, string.Empty));

                    for (var i = 1; i < stops.Count; i++)
                    {
                        if (Claim(stops[i - 1].Offset, stops[i].Offset))
                            items.Add(new Planned(stops[i - 1], stops[i], 0, string.Empty));
                    }
                }

                if (items.Count == 0)
                    continue;
                var far = space is not null && ChooseFar(axis, items, tiers, datum, space);
                var side = far ? 1 : 0;
                dimensions.AddRange(items.Select(item => Dimension(axis, item with { Tier = tiers[side] + item.Tier, Far = far }, datum, space)));
                tiers[side] += items.Max(item => item.Tier) + 1;
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
    /// 给了 <paramref name="space"/>（1.23.0）整组可以换到下方 / 右侧（零件外沿外同样远）：各站孔引到文字那一列的界线总长短的一侧。
    /// </summary>
    private static HolePositionPlan PlanChain(
        IReadOnlyList<HoleEdge> holes, IReadOnlyList<IReadOnlyList<HoleEdge>> kinds, double left, double top, double scale, PositionSpace? space)
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
            var horizontal = axis == PositionAxis.Horizontal;
            var nearLine = horizontal ? top + OrdinateOffset : left - OrdinateOffset;
            var far = false;
            if (space is not null)
            {
                var farLine = horizontal ? space.Bottom - OrdinateOffset : space.Right + OrdinateOffset;
                far = DimensionSides.PreferFar(
                    0, stops.Sum(stop => DimensionSides.Leader(axis, nearLine, Center(stop.Hole))), 0,
                    0, stops.Sum(stop => DimensionSides.Leader(axis, farLine, Center(stop.Far))), 0);
                if (far)
                    nearLine = farLine;
            }

            var at = horizontal ? new SheetPoint(left, nearLine) : new SheetPoint(nearLine, top);
            groups.Add(new OrdinateGroup(
                axis, stops.Select(stop => (far ? stop.Far : stop.Hole).Index).ToList(), stops.Select(stop => stop.Offset / scale).ToList(), at));
        }

        var (holeCount, slotCount) = HoleCalloutPlanner.Count(holes);
        return new HolePositionPlan([], holeCount, kinds.Count, 0, slotCount, groups);
    }

    /// <summary>
    /// 普通模式下每种孔各自想标的「中心线 → 中心线」段（不含基准到第一站，1.8.0 销孔标注用）：
    /// 链式是相邻两站，阵列标法是第一站到最后一站。返回图纸坐标（水平段是两个 X、竖直段是两个 Y，小的在前）
    /// 与这种孔是不是销钉孔。同一段被几种孔想标，<see cref="Plan"/> 只标一次——销孔标注据此判断销孔间尺寸是否兼管别的孔。
    /// </summary>
    public static List<(PositionAxis Axis, double From, double To, bool Dowel)> RequestedSpans(
        IReadOnlyList<HoleEdge> edges, double left, double top, double scale)
    {
        ArgumentNullException.ThrowIfNull(edges);
        var kinds = HoleCalloutPlanner.GroupKinds(HoleCalloutPlanner.Representatives(HoleCalloutPlanner.Recognize(edges)));
        var spans = new List<(PositionAxis, double, double, bool)>();
        foreach (var axis in new[] { PositionAxis.Horizontal, PositionAxis.Vertical })
        {
            var datum = axis == PositionAxis.Horizontal ? left : top;
            double Sheet(double offset) => axis == PositionAxis.Horizontal ? datum + offset : datum - offset;
            foreach (var kind in kinds)
            {
                var chain = Chain(kind, axis, datum, scale);
                var stops = chain.Stops;
                var dowel = kind.Count > 0 && kind[0].Dowel;
                List<(Stop, Stop)> pairs = chain.Pattern
                    ? [(stops[0], stops[^1])]
                    : stops.Zip(stops.Skip(1)).ToList();
                foreach (var (a, b) in pairs)
                {
                    var (p, q) = (Sheet(a.Offset), Sheet(b.Offset));
                    spans.Add((axis, Math.Min(p, q), Math.Max(p, q), dowel));
                }
            }
        }

        return spans;
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

    /// <summary>一站：离基准多远；<see cref="Hole"/> 是尺寸放上方 / 左侧时挑的孔（最上 / 最左），<see cref="Far"/> 是放下方 / 右侧时挑的（最下 / 最右）。</summary>
    internal sealed record Stop(double Offset, HoleEdge Hole, HoleEdge Far)
    {
        public HoleEdge On(bool far) => far ? Far : Hole;
    }

    private sealed record KindChain(IReadOnlyList<Stop> Stops, bool Pattern)
    {
        public double Reach => Stops.Count == 0 ? 0 : Stops[^1].Offset;
    }

    /// <summary>
    /// 一种孔在一个方向上的「站」：同一坐标的孔算一站，按离基准由近到远排。
    /// 每站挑离尺寸最近的那个孔（水平尺寸在上方，挑最上面的；竖直尺寸在左侧，挑最左的），尺寸界线短；
    /// 尺寸放到另一侧时（1.23.0）挑最下 / 最右的那个（<see cref="Stop.Far"/>）。
    /// </summary>
    private static KindChain Chain(IReadOnlyList<HoleEdge> kind, PositionAxis axis, double datum, double scale)
    {
        var stops = new List<Stop>();
        foreach (var hole in kind)
        {
            var offset = axis == PositionAxis.Horizontal ? hole.X - datum : datum - hole.Y;
            var index = stops.FindIndex(stop => Math.Abs(stop.Offset - offset) <= HoleCalloutPlanner.CenterTolerance);
            if (index < 0)
            {
                stops.Add(new Stop(offset, hole, hole));
                continue;
            }

            var stop = stops[index];
            if (axis == PositionAxis.Horizontal ? hole.Y > stop.Hole.Y : hole.X < stop.Hole.X)
                stop = stop with { Hole = hole };
            if (axis == PositionAxis.Horizontal ? hole.Y < stop.Far.Y : hole.X > stop.Far.X)
                stop = stop with { Far = hole };
            stops[index] = stop;
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

    /// <summary>
    /// 水平尺寸放在上侧基准之上、竖直尺寸放在左侧基准之左，第 <see cref="Planned.Tier"/> 层；文字居中。
    /// <see cref="Planned.Far"/> 的放到视图另一边（1.16.0 起避禁区、1.23.0 起选引线短的一侧）：水平的在视图最下沿之下、竖直的在最右沿之右，层照样由里到外。
    /// </summary>
    private static PositionDimension Dimension(PositionAxis axis, Planned item, Datum datum, PositionSpace? space)
        => new(axis, item.From?.On(item.Far).Index, item.To.On(item.Far).Index, TextAt(axis, item, datum, space), item.Prefix,
            datum.Scale > 0 ? (item.To.Offset - (item.From?.Offset ?? 0)) / datum.Scale : 0);

    private static SheetPoint TextAt(PositionAxis axis, Planned item, Datum datum, PositionSpace? space)
    {
        var middle = ((item.From?.Offset ?? 0) + item.To.Offset) / 2;
        var away = FirstTier + item.Tier * TierStep;
        var far = item.Far && space is not null;
        return axis == PositionAxis.Horizontal
            ? new SheetPoint(datum.Left + middle, far ? space!.Bottom - away : datum.Top + away)
            : new SheetPoint(far ? space!.Right + away : datum.Left - away, datum.Top - middle);
    }

    /// <summary>
    /// 一种孔的这几个尺寸（<paramref name="items"/>，层号从 0 起）放不放到另一侧（下方 / 右侧），1.23.0 起逐种孔挑：
    /// 两侧各接在已排的层外面（<paramref name="tiers"/>：上方 / 左侧、下方 / 右侧各排了几层），先比压禁区与出图框几处，
    /// 一样多比尺寸界线（孔心引到尺寸线）总长，短的一侧；一样长放已排层数少的一侧，层数也一样留在上方 / 左侧。
    /// </summary>
    internal static bool ChooseFar(PositionAxis axis, IReadOnlyList<Planned> items, IReadOnlyList<int> tiers, Datum datum, PositionSpace space)
    {
        int Hits(bool far) => items.Sum(item =>
        {
            var box = Footprint(axis, item with { Tier = tiers[far ? 1 : 0] + item.Tier, Far = far }, datum, space);
            return space.Blocked.Count(box.Overlaps) + (space.Frame is { } frame && !box.Within(frame) ? 1 : 0);
        });

        double Length(bool far) => items.Sum(item =>
        {
            var placed = item with { Tier = tiers[far ? 1 : 0] + item.Tier, Far = far };
            var at = TextAt(axis, placed, datum, space);
            var line = axis == PositionAxis.Horizontal ? at.Y : at.X;
            return DimensionSides.Leader(axis, line, Center(item.To.On(far)))
                   + (item.From is { } from ? DimensionSides.Leader(axis, line, Center(from.On(far))) : 0);
        });

        return DimensionSides.PreferFar(Hits(false), Length(false), tiers[0], Hits(true), Length(true), tiers[1]);
    }

    private static SheetPoint Center(HoleEdge hole) => new(hole.X, hole.Y);

    /// <summary>
    /// 一个尺寸大约占的地方：沿尺寸线是两头之间（不足 <see cref="FootprintText"/> 按文字宽算，居中），横着是文字那一条
    /// （尺寸线下 1.5 mm 到上 4 mm，数字站在尺寸线上）。
    /// </summary>
    internal static SheetRect Footprint(PositionAxis axis, Planned item, Datum datum, PositionSpace? space)
    {
        var at = TextAt(axis, item, datum, space);
        var span = Math.Max(Math.Abs(item.To.Offset - (item.From?.Offset ?? 0)), FootprintText);
        return axis == PositionAxis.Horizontal
            ? new SheetRect(at.X - span / 2, at.Y - 0.0015, at.X + span / 2, at.Y + 0.004)
            : new SheetRect(at.X - 0.004, at.Y - span / 2, at.X + 0.0015, at.Y + span / 2);
    }

    /// <summary>估尺寸占地时数字最少算多宽（图纸 8 mm，「120」三位数加箭头）。</summary>
    private const double FootprintText = 0.008;

    /// <summary>规划好的一个尺寸：起点站（null = 基准）、终点站、第几层、前缀、放没放到另一侧（下方 / 右侧）。</summary>
    internal sealed record Planned(Stop? From, Stop To, int Tier, string Prefix, bool Far = false);

    internal readonly record struct Datum(double Left, double Top, double Scale = 0);
}
