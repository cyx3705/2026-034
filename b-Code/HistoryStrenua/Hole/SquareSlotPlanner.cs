namespace HistoryStrenua;

/// <summary>视图里一个方形槽（封闭的矩形窗口）：四条边各是 <see cref="ScannedView.Lines"/> 的哪一条，以及它在图纸上占的矩形（不含圆角）。</summary>
internal sealed record SlotRect(int Left, int Right, int Top, int Bottom, SheetRect Rect);

/// <summary>普通模式下要加的一个方形槽尺寸：从 <see cref="From"/> 那条边量到 <see cref="To"/> 那条边（-1 表示基准）。</summary>
internal sealed record SlotDimension(PositionAxis Axis, int From, int To, SheetPoint TextAt);

/// <summary>
/// 「方形槽」（1.17.0，孔类，用户定，样图 WTJYQ-04-02 右防护板）的纯几何部分：视图里哪些直边围成封闭的矩形槽、每个槽标哪几个尺寸、哪些旧尺寸是方形槽尺寸。
/// </summary>
/// <remarks>
/// <para>认法：不在外轮廓上（<see cref="OutlinePlanner.OuterLines"/> 判，开口槽归外轮廓）的两条竖直边、两条水平边围成矩形——
/// 两条竖直边上下端对齐，两条水平边左右端对齐，四个角缺的一样多（直角缺 0，带圆角缺圆角半径）。是槽还是凸台图上分不出，
/// 由执行体问 SolidWorks：贴着边、正对图纸的那张面往矩形里面延伸就是凸台，不标。</para>
/// <para>标法（我定）：基准同孔位尺寸（零件最左、最上直边）。每个槽每个方向一层链：基准 → 近边（位置）、近边 → 远边（槽宽 / 槽高），
/// 一层两个尺寸并排；层排在现有尺寸最外层再往外（同外轮廓 <see cref="OutlinePlanner.FirstTier"/>），近的槽在里。
/// 尺寸链模式两条边都加进孔的那组坐标尺寸。</para>
/// </remarks>
internal static class SquareSlotPlanner
{
    /// <summary>矩形最小边长（图纸 1 mm）：再小的是孔口残线、槽口之类，不当方形槽。</summary>
    public const double MinSide = 0.001;

    /// <summary>端点对齐的容差（图纸 0.05 mm）。</summary>
    public const double Tolerance = 5e-5;

    /// <param name="lines">视图里的直边。</param>
    /// <param name="outer">外轮廓直边的下标（这些不参与）。</param>
    public static List<SlotRect> Find(IReadOnlyList<SheetSegment> lines, IReadOnlyCollection<int> outer)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(outer);
        var verticals = new List<(int Index, double X, double Low, double High)>();
        var horizontals = new List<(int Index, double Y, double Low, double High)>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (outer.Contains(i))
                continue;
            var anchor = DimensionAnchor.Line(lines[i], modelLine: true);
            var line = lines[i];
            if (anchor.X is { } x && Math.Abs(line.Y2 - line.Y1) >= MinSide)
                verticals.Add((i, x, Math.Min(line.Y1, line.Y2), Math.Max(line.Y1, line.Y2)));
            else if (anchor.Y is { } y && Math.Abs(line.X2 - line.X1) >= MinSide)
                horizontals.Add((i, y, Math.Min(line.X1, line.X2), Math.Max(line.X1, line.X2)));
        }

        var slots = new List<SlotRect>();
        var used = new HashSet<int>();
        foreach (var left in verticals.OrderBy(v => v.X))
        {
            foreach (var right in verticals.Where(v => v.X > left.X + MinSide && Near(v.Low, left.Low) && Near(v.High, left.High)).OrderBy(v => v.X))
            {
                if (used.Contains(left.Index) || used.Contains(right.Index))
                    break;
                var t = horizontals.FindIndex(h => !used.Contains(h.Index) && Corner(h, left.X, right.X, h.Y - left.High));
                var b = horizontals.FindIndex(h => !used.Contains(h.Index) && Corner(h, left.X, right.X, left.Low - h.Y));
                if (t < 0 || b < 0 || t == b)
                    continue;
                var (top, bottom) = (horizontals[t], horizontals[b]);
                // 四个角缺的一样多：竖直边离水平边、水平边离竖直边都是同一个圆角半径。
                var radius = top.Y - left.High;
                if (!Near(left.Low - bottom.Y, radius))
                    continue;
                slots.Add(new SlotRect(left.Index, right.Index, top.Index, bottom.Index, new SheetRect(left.X, bottom.Y, right.X, top.Y)));
                used.UnionWith([left.Index, right.Index, top.Index, bottom.Index]);
                break;
            }
        }

        return slots;

        // 水平边两端各离两条竖直边 radius（≥0，不超过一半边长）。
        static bool Corner((int Index, double Y, double Low, double High) h, double leftX, double rightX, double radius)
            => radius >= -Tolerance && radius <= (rightX - leftX) / 2
               && Near(h.Low - leftX, radius) && Near(rightX - h.High, radius);
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) <= Tolerance;

    /// <summary>
    /// 普通模式的尺寸：每个槽每个方向一层（离基准近的槽在里），基准 → 近边、近边 → 远边两个尺寸并排在这一层上；
    /// 近边就在基准上（槽贴着基准边，不会是封闭槽，防御）时只标槽宽。
    /// </summary>
    /// <param name="slots">方形槽。</param>
    /// <param name="left">左侧基准 X（图纸）。</param>
    /// <param name="top">上侧基准 Y（图纸）。</param>
    /// <param name="firstHorizontal">水平尺寸第一层在上侧基准之上多远。</param>
    /// <param name="firstVertical">竖直尺寸第一层在左侧基准之左多远。</param>
    public static List<SlotDimension> Dimensions(IReadOnlyList<SlotRect> slots, double left, double top, double firstHorizontal, double firstVertical)
    {
        ArgumentNullException.ThrowIfNull(slots);
        var dimensions = new List<SlotDimension>();
        var tier = 0;
        foreach (var slot in slots.OrderBy(slot => slot.Rect.Left))
        {
            var y = top + firstHorizontal + tier++ * HolePositionPlanner.TierStep;
            if (slot.Rect.Left - left > HoleCalloutPlanner.CenterTolerance)
                dimensions.Add(new SlotDimension(PositionAxis.Horizontal, -1, slot.Left, new SheetPoint((left + slot.Rect.Left) / 2, y)));
            dimensions.Add(new SlotDimension(PositionAxis.Horizontal, slot.Left, slot.Right, new SheetPoint((slot.Rect.Left + slot.Rect.Right) / 2, y)));
        }

        tier = 0;
        foreach (var slot in slots.OrderByDescending(slot => slot.Rect.Top))
        {
            var x = left - firstVertical - tier++ * HolePositionPlanner.TierStep;
            if (top - slot.Rect.Top > HoleCalloutPlanner.CenterTolerance)
                dimensions.Add(new SlotDimension(PositionAxis.Vertical, -1, slot.Top, new SheetPoint(x, (top + slot.Rect.Top) / 2)));
            dimensions.Add(new SlotDimension(PositionAxis.Vertical, slot.Top, slot.Bottom, new SheetPoint(x, (slot.Rect.Top + slot.Rect.Bottom) / 2)));
        }

        return dimensions;
    }

    /// <summary>
    /// 旧的方形槽尺寸（重标时删）：线性尺寸，至少两头，每一头都是槽边或基准边，且至少一头是槽边。外轮廓尺寸两头都在外轮廓上，孔位尺寸连孔，都不会认成它。
    /// </summary>
    public static List<int> Obsolete(IReadOnlyList<ViewDimension> existing, IReadOnlyList<SheetSegment> slotLines, IReadOnlyList<SheetSegment> datumLines)
    {
        ArgumentNullException.ThrowIfNull(existing);
        bool On(DimensionAnchor anchor, IReadOnlyList<SheetSegment> set)
            => anchor.ModelLine && anchor.Segment is { } segment && set.Any(line => DimensionGeometry.SameSegment(line, segment));
        return existing
            .Where(dimension => dimension.Linear && dimension.Anchors.Count >= 2
                                && dimension.Anchors.All(anchor => On(anchor, slotLines) || On(anchor, datumLines))
                                && dimension.Anchors.Any(anchor => On(anchor, slotLines)))
            .Select(dimension => dimension.Index)
            .ToList();
    }
}
