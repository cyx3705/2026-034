namespace HistoryStrenua;

/// <summary>
/// 图纸上的一段文字：从左下基线点 (<paramref name="X"/>, <paramref name="Y"/>) 起，沿 <paramref name="Angle"/> 方向宽
/// <paramref name="Width"/>，往左手边（文字的「上」）高 <paramref name="Height"/>。单位米、弧度。
/// </summary>
/// <remarks>
/// 真机（SW 2025 SP5）：<c>IDisplayData.GetTextPositionAtIndex</c> 就是这个左下基线点（参考位 swLOWER_LEFT），
/// 宽取 <c>GetTextInBoxWidthAtIndex</c>（不带框的文字也有值），竖直尺寸角度 π/2、文字往 -X 长。
/// </remarks>
internal readonly record struct TextBox(double X, double Y, double Width, double Height, double Angle = 0)
{
    public TextBox Shift(double dx, double dy) => this with { X = X + dx, Y = Y + dy };

    /// <summary>四个角（基线左、基线右、顶右、顶左）。</summary>
    public IEnumerable<SheetPoint> Corners()
    {
        var (ux, uy) = (Math.Cos(Angle), Math.Sin(Angle));
        var (vx, vy) = (-uy, ux);
        yield return new SheetPoint(X, Y);
        yield return new SheetPoint(X + Width * ux, Y + Width * uy);
        yield return new SheetPoint(X + Width * ux + Height * vx, Y + Width * uy + Height * vy);
        yield return new SheetPoint(X + Height * vx, Y + Height * vy);
    }
}

/// <summary>避让时要躲开的东西：别的孔相关注解的线条与文字。</summary>
internal sealed record Obstacles(IReadOnlyList<SheetSegment> Lines, IReadOnlyList<TextBox> Texts)
{
    public static Obstacles Empty { get; } = new([], []);
}

/// <summary>孔标注挪到哪：引线折点（文字下划线的一端）与文字在折点哪一侧。</summary>
internal readonly record struct CalloutSpot(SheetPoint Shoulder, bool TextLeft);

/// <summary>
/// 标注避障（1.6.0）的纯几何部分：文字压没压到别的孔相关注解的线条或文字、孔位尺寸的数字沿尺寸线滑多远、
/// 孔标注换到孔的哪个角位。不碰 SolidWorks，能离线测。
/// </summary>
/// <remarks>
/// <para>「压到」= 线段穿过文字框，或两个文字框相交。文字框四周放宽 <see cref="Margin"/>，底边也放：
/// 字站在别的尺寸的尺寸线上（同层相邻尺寸、伸出箭头外的那段）读起来分不清是谁的数，也算压
/// （真机过渡板「9.50」滑到「96」的尺寸线上，1.6.0 首轮因底边豁免而放过）。自己的尺寸线 / 下划线不是障碍。</para>
/// <para>孔位尺寸只沿尺寸线方向滑（不换层，层由「孔位尺寸」排好）；孔标注保持折点到孔心的规则（半径 + 12 mm），
/// 依次换到右上、左下、右下，再放远一档。</para>
/// </remarks>
internal static class ClearancePlanner
{
    /// <summary>文字框四周留白（图纸 0.3 mm）。</summary>
    public const double Margin = 3e-4;

    /// <summary>孔标注下划线比文字多出的那一截（图纸 1.25 mm，真机读得）：折点 = 文字边缘外这么远。</summary>
    public const double ShoulderGap = 1.25e-3;

    /// <summary>尺寸数字每次滑多远（图纸 0.5 mm；1 mm 时真机「25」跨度里 6.9 mm 的空当被跨过去了）。</summary>
    public const double SlideStep = 5e-4;

    /// <summary>尺寸数字最多滑多远（图纸 30 mm）。</summary>
    public const double SlideLimit = 0.03;

    /// <summary>孔标注折点离孔心比半径多出的几档（图纸 12 / 18 / 24 mm），第一档就是 <see cref="HoleCalloutPlanner.LeaderReach"/>。</summary>
    public static readonly double[] CalloutReaches = [HoleCalloutPlanner.LeaderReach, 0.018, 0.024];

    /// <summary>孔标注的四个角位，按偏好排：左下优先，左侧空间不足再往上，最后考虑右侧。</summary>
    private static readonly (int X, int Y)[] Corners = [(-1, -1), (-1, 1), (1, -1), (1, 1)];

    /// <summary><paramref name="texts"/> 压到了几样障碍（线条、文字各算一样）。</summary>
    public static int Hits(IReadOnlyList<TextBox> texts, Obstacles obstacles)
    {
        var near = Near(obstacles, texts, 0);
        return near.Lines.Count(line => texts.Any(text => Hits(text, line)))
            + near.Texts.Count(other => texts.Any(text => Hits(text, other)));
    }

    /// <summary>
    /// 只留外接框挨着 <paramref name="texts"/>（四周再放宽 <paramref name="reach"/>）的障碍。整页几千条线，一段文字只可能碰到身边几条，
    /// 先按外接框筛掉，精确判定的次数从「文字 × 全部障碍」降到「文字 × 附近障碍」；滑动、换角位这种要试几十个位置的只筛一次。
    /// </summary>
    internal static Obstacles Near(Obstacles obstacles, IReadOnlyList<TextBox> texts, double reach)
    {
        if (texts.Count == 0)
            return Obstacles.Empty;
        var (left, right, bottom, top) = (double.MaxValue, double.MinValue, double.MaxValue, double.MinValue);
        foreach (var text in texts)
        {
            var (l, r, b, t) = Bounds(text);
            (left, right, bottom, top) = (Math.Min(left, l), Math.Max(right, r), Math.Min(bottom, b), Math.Max(top, t));
        }

        (left, right, bottom, top) = (left - reach - Margin, right + reach + Margin, bottom - reach - Margin, top + reach + Margin);
        bool Overlaps(double l, double r, double b, double t) => l <= right && r >= left && b <= top && t >= bottom;
        var lines = new List<SheetSegment>();
        foreach (var line in obstacles.Lines)
        {
            if (Overlaps(Math.Min(line.X1, line.X2), Math.Max(line.X1, line.X2), Math.Min(line.Y1, line.Y2), Math.Max(line.Y1, line.Y2)))
                lines.Add(line);
        }

        var others = new List<TextBox>();
        foreach (var other in obstacles.Texts)
        {
            var (l, r, b, t) = Bounds(other);
            if (Overlaps(l, r, b, t))
                others.Add(other);
        }

        return new Obstacles(lines, others);
    }

    /// <summary>文字框的外接框（可能带角度）。</summary>
    private static (double Left, double Right, double Bottom, double Top) Bounds(TextBox box)
    {
        var (ux, uy) = (Math.Cos(box.Angle), Math.Sin(box.Angle));
        var (x1, y1) = (box.Width * ux, box.Width * uy);
        var (x2, y2) = (-box.Height * uy, box.Height * ux);
        return (box.X + Math.Min(0, x1) + Math.Min(0, x2), box.X + Math.Max(0, x1) + Math.Max(0, x2),
            box.Y + Math.Min(0, y1) + Math.Min(0, y2), box.Y + Math.Max(0, y1) + Math.Max(0, y2));
    }

    /// <summary>线段是否穿过（放宽后的）文字框。</summary>
    public static bool Hits(TextBox box, SheetSegment line)
    {
        var (ax, ay) = Local(box, line.X1, line.Y1);
        var (bx, by) = Local(box, line.X2, line.Y2);
        return ClipsRectangle(ax, ay, bx, by, -Margin, box.Width + Margin, -Margin, box.Height + Margin);
    }

    /// <summary>两个文字框是否相交（<paramref name="box"/> 放宽，<paramref name="other"/> 按原样）。</summary>
    public static bool Hits(TextBox box, TextBox other)
    {
        // 外接框都不挨就不用算四条边。
        var (al, ar, ab, at) = Bounds(box);
        var (bl, br, bb, bt) = Bounds(other);
        if (al - Margin > br || ar + Margin < bl || ab - Margin > bt || at + Margin < bb)
            return false;
        var corners = other.Corners().ToArray();
        for (var i = 0; i < corners.Length; i++)
        {
            var next = corners[(i + 1) % corners.Length];
            if (Hits(box, new SheetSegment(corners[i].X, corners[i].Y, next.X, next.Y)))
                return true;
        }

        // 一个整个在另一个里面：边不相交，看 box 的一个角在不在 other 里。
        var probe = box.Corners().First();
        var (lx, ly) = Local(other, probe.X, probe.Y);
        return lx >= 0 && lx <= other.Width && ly >= 0 && ly <= other.Height;
    }

    /// <summary>
    /// 尺寸数字沿尺寸线（单位方向 <paramref name="ux"/>, <paramref name="uy"/>）滑多远才不压线：先近后远、先正后负，
    /// 每步 <see cref="SlideStep"/>，最远 <see cref="SlideLimit"/>。本来就不压返回 0；滑到头都压返回 null。
    /// </summary>
    public static double? Slide(IReadOnlyList<TextBox> texts, double ux, double uy, Obstacles obstacles)
    {
        if (Hits(texts, obstacles) == 0)
            return 0;
        obstacles = Near(obstacles, texts, SlideLimit);
        for (var step = 1; step * SlideStep <= SlideLimit + 1e-12; step++)
        {
            foreach (var sign in new[] { 1, -1 })
            {
                var offset = sign * step * SlideStep;
                if (Hits(Shift(texts, offset * ux, offset * uy), obstacles) == 0)
                    return offset;
            }
        }

        return null;
    }

    /// <summary>
    /// 字能不能留在自己两条尺寸界线之间（1.32.1，用户指出：底层1 的 33、37 放得下却被挪到界线外）：
    /// 字宽不超过界线间距就算放得下（文字框比字形宽，再宽出 <see cref="FitSlack"/> 也算，1.32.6 真机底层1 的 30：框 6.13、间距 6），
    /// 紧贴界线不算压，留白只对别的东西；同一行别的尺寸伸过来的箭头短线也不算压。
    /// 字已在界线之间就留在原处，在外面就放回正中。返回沿字方向要挪多远；放不下或那里有别的东西返回 null。
    /// </summary>
    /// <param name="texts">尺寸的文字，只认一段的。</param>
    /// <param name="own">尺寸自己的线：尺寸界线与尺寸线。</param>
    /// <param name="obstacles">要躲的东西。</param>
    public static double? Seat(IReadOnlyList<TextBox> texts, IReadOnlyList<SheetSegment> own, Obstacles obstacles)
    {
        if (texts.Count != 1)
            return null;
        var text = texts[0];
        var (ux, uy) = (Math.Cos(text.Angle), Math.Sin(text.Angle));
        var crossing = CrossingLines(own, text.Angle).ToList();
        var stations = crossing.Select(line => (line.X1 + line.X2) / 2 * ux + (line.Y1 + line.Y2) / 2 * uy).ToList();
        if (stations.Count < 2)
            return null;
        var (low, high) = (stations.Min(), stations.Max());
        var gap = high - low;
        if (gap + FitSlack < text.Width || text.Width <= 2 * Margin)
            return null;
        var start = text.X * ux + text.Y * uy;
        var offset = start >= low && start + text.Width <= high ? 0 : (low + high - text.Width) / 2 - start;
        if (Math.Abs(offset) < SeatTolerance)
            offset = 0;

        // 同一行 = 与自己的尺寸线共线；只跳过这种线，别的平行线（视图的边、别的层的尺寸线）照样算压。
        double Across(SheetSegment line) => -line.X1 * uy + line.Y1 * ux;
        var rows = own.Except(crossing).Select(Across).ToList();
        bool SameRow(SheetSegment line) => !CrossingLines([line], text.Angle).Any() && rows.Any(row => Math.Abs(Across(line) - row) < 1e-4);

        // Hits 会四周放宽留白：沿字方向先收回去，上下的留白不变；框比间距宽的连宽出的那截一起收。
        var inset = Math.Max(Margin, (text.Width - gap) / 2 + Margin + 1e-6);
        var inner = text with
        {
            X = text.X + (offset + inset) * ux,
            Y = text.Y + (offset + inset) * uy,
            Width = text.Width - 2 * inset,
        };
        var lines = obstacles.Lines.Where(line => !SameRow(line)).ToList();
        return Hits([inner], new Obstacles(lines, obstacles.Texts)) == 0 ? offset : null;
    }

    /// <summary>
    /// 算字能不能留在界线之间时要躲的（1.32.5，真机底层1：放不下的「4」挡在隔壁 8 mm 的间距里，放得下的 40 被挤到远处）：
    /// 同一行上放不下的字本来就要挪走，不算挡路，挪走轮到它自己。别的字、别的线照旧。
    /// </summary>
    /// <param name="obstacles">原来要躲的。</param>
    /// <param name="own">这个尺寸自己的线。</param>
    /// <param name="angle">这个尺寸的文字方向。</param>
    /// <param name="peers">别的尺寸的线与字。</param>
    public static Obstacles ForSeat(Obstacles obstacles, IReadOnlyList<SheetSegment> own, double angle, IEnumerable<(IReadOnlyList<SheetSegment> Lines, IReadOnlyList<TextBox> Texts)> peers)
    {
        var (ux, uy) = (Math.Cos(angle), Math.Sin(angle));
        double Across(SheetSegment line) => -line.X1 * uy + line.Y1 * ux;
        var crossing = CrossingLines(own, angle).ToList();
        var rows = own.Except(crossing).Select(Across).ToList();
        var squeezed = new HashSet<TextBox>();
        foreach (var (lines, texts) in peers)
        {
            if (texts.Count != 1 || Math.Abs(texts[0].Angle - angle) > 1e-3)
                continue;
            var peerCrossing = CrossingLines(lines, angle).ToList();
            var stations = peerCrossing.Select(line => (line.X1 + line.X2) / 2 * ux + (line.Y1 + line.Y2) / 2 * uy).ToList();
            if (Squeezed(texts, lines)
                && lines.Except(peerCrossing).Any(line => rows.Any(row => Math.Abs(Across(line) - row) < 1e-4)))
                squeezed.Add(texts[0]);
        }

        return squeezed.Count == 0 ? obstacles : new Obstacles(obstacles.Lines, obstacles.Texts.Where(text => !squeezed.Contains(text)).ToList());
    }

    /// <summary>文字框比界线间距宽出这么多还算放得下（图纸 0.5 mm）。</summary>
    public const double FitSlack = 5e-4;

    /// <summary>字离正中不到这么远就不用挪（图纸 0.05 mm，读回的误差）。</summary>
    private const double SeatTolerance = 5e-5;

    /// <summary>放不下的字贴在界线外时离界线多远（图纸 2 mm，留给箭头）。</summary>
    public const double BesideGap = 0.002;

    /// <summary>别人的尺寸线离自己这一行不到这么远（图纸 1 mm）算同一行。</summary>
    private const double NearRow = 1e-3;

    /// <summary>箭头短线最长算多长（图纸 6 mm）；再长就是别人的尺寸线。</summary>
    private const double StubLength = 0.006;

    /// <summary>
    /// 这一组尺寸（同一条链）整条平移 (<paramref name="dx"/>, <paramref name="dy"/>) 后，放得下的字都能留在界线之间吗（1.32.6）。放不下的字不算，它们之后自己找地方。
    /// </summary>
    public static bool RowFits(IReadOnlyList<(IReadOnlyList<SheetSegment> Lines, IReadOnlyList<TextBox> Texts)> group, double dx, double dy, Obstacles obstacles, SheetRect frame)
    {
        foreach (var (lines, texts) in group)
        {
            if (texts.Count != 1 || Squeezed(texts, lines))
                continue;
            var moved = lines.Select(line => new SheetSegment(line.X1 + dx, line.Y1 + dy, line.X2 + dx, line.Y2 + dy)).ToList();
            var shifted = Shift(texts, dx, dy);
            if (!AnnotationPlacementPlanner.Within(shifted, frame) || Seat(shifted, moved, obstacles) is null)
                return false;
        }

        return true;
    }

    /// <summary>
    /// 长尺寸整条平移后，尺寸线（和字平行的线）还压不压 <paramref name="text"/>（1.34.0：放不下的短尺寸跑到上面，长尺寸让路）。
    /// 尺寸界线不算：它们只是跟着变长，本来就在别处。
    /// </summary>
    public static bool StillBlocks(IReadOnlyList<SheetSegment> lines, double angle, double dx, double dy, TextBox text)
    {
        var (ux, uy) = (Math.Cos(angle), Math.Sin(angle));
        return lines
            .Where(line => Math.Abs(((line.X2 - line.X1) * uy) - ((line.Y2 - line.Y1) * ux)) <= 1e-6)
            .Any(line => Hits(text, new SheetSegment(line.X1 + dx, line.Y1 + dy, line.X2 + dx, line.Y2 + dy)));
    }

    /// <summary>离开尺寸链找地方时每步走多远（图纸 0.5 mm）。</summary>
    public const double SearchStep = 5e-4;

    /// <summary>放不下的字离开这一行时，垂直尺寸线方向至少留这么多：字高 3.5、字到自己尺寸线 0.25，加起来正好一个行距。</summary>
    private const double RowClearance = 7.5e-4;

    /// <summary>字沿尺寸线还对着自己的间距（和两条界线之间有重叠）。</summary>
    public static bool OverGap(IReadOnlyList<TextBox> texts, IReadOnlyList<SheetSegment> own)
    {
        if (texts.Count != 1)
            return false;
        var (ux, uy) = (Math.Cos(texts[0].Angle), Math.Sin(texts[0].Angle));
        var stations = CrossingLines(own, texts[0].Angle).Select(line => (line.X1 + line.X2) / 2 * ux + (line.Y1 + line.Y2) / 2 * uy).ToList();
        if (stations.Count < 2)
            return false;
        var start = texts[0].X * ux + texts[0].Y * uy;
        return start < stations.Max() && start + texts[0].Width > stations.Min();
    }

    /// <summary>两段字在同一行：方向相同、垂直于字的方向上差不到半个行距。</summary>
    public static bool SameRow(TextBox a, TextBox b)
    {
        if (Math.Abs(a.Angle - b.Angle) >= 1e-3)
            return false;
        var (vx, vy) = (-Math.Sin(a.Angle), Math.Cos(a.Angle));
        return Math.Abs((a.X - b.X) * vx + (a.Y - b.Y) * vy) < HolePositionPlanner.TierStep / 2;
    }

    /// <summary>这个尺寸的字放不进自己两条界线之间（只认一段字、有两条界线的）。</summary>
    public static bool Squeezed(IReadOnlyList<TextBox> texts, IReadOnlyList<SheetSegment> own)
    {
        if (texts.Count != 1)
            return false;
        var (ux, uy) = (Math.Cos(texts[0].Angle), Math.Sin(texts[0].Angle));
        var stations = CrossingLines(own, texts[0].Angle).Select(line => (line.X1 + line.X2) / 2 * ux + (line.Y1 + line.Y2) / 2 * uy).ToList();
        return stations.Count >= 2 && stations.Max() - stations.Min() + FitSlack < texts[0].Width;
    }

    /// <summary>
    /// 放不下的字放哪（1.32.6，用户定）：界线旁边有地方就贴着放，还在这一行；旁边都有东西（真机底层1 的 4 夹在两个 40 之间）就破例离开尺寸链，
    /// 字沿尺寸线对着自己的间距，从贴着视图外框往外一步步找，放得下就行（不留白）。返回要挪多少，已经在对的地方是 (0, 0)；哪都放不下返回 null。
    /// </summary>
    /// <param name="text">尺寸的字。</param>
    /// <param name="own">尺寸自己的线。</param>
    /// <param name="view">视图外框。</param>
    /// <param name="frame">图框。</param>
    /// <param name="obstacles">要躲的东西，不含这个尺寸自己的线。</param>
    /// <param name="onChain">还在尺寸链这一行（或本来就不在链上）：false 表示已经离开，不再回到旁边。</param>
    public static SheetPoint? PlaceSqueezed(TextBox text, IReadOnlyList<SheetSegment> own, SheetRect view, SheetRect frame, Obstacles obstacles, bool onChain = true)
    {
        var (ux, uy) = (Math.Cos(text.Angle), Math.Sin(text.Angle));
        var (vx, vy) = (-uy, ux);
        var crossing = CrossingLines(own, text.Angle).ToList();
        if (crossing.Count < 2)
            return null;
        var stations = crossing.Select(line => (line.X1 + line.X2) / 2 * ux + (line.Y1 + line.Y2) / 2 * uy).ToList();
        var (low, high) = (stations.Min(), stations.Max());
        var start = text.X * ux + text.Y * uy;
        var across = text.X * vx + text.Y * vy;

        double Row(SheetSegment line) => line.X1 * vx + line.Y1 * vy;
        var rows = own.Except(crossing).Select(Row).ToList();
        // 同一行只放过别人的箭头短线；别人的长尺寸线不能踩（真机底层1 的 31.5 贴到 618.5 的线上）。
        bool Stub(SheetSegment line) => !CrossingLines([line], text.Angle).Any() && rows.Any(row => Math.Abs(Row(line) - row) < 1e-4)
            && Math.Sqrt(Math.Pow(line.X2 - line.X1, 2) + Math.Pow(line.Y2 - line.Y1, 2)) <= StubLength;
        var others = new Obstacles(obstacles.Lines.Where(line => !Stub(line)).ToList(), obstacles.Texts);

        SheetPoint Along(double to) => new((to - start) * ux, (to - start) * uy);
        bool Free(double to)
        {
            var moved = text.Shift((to - start) * ux, (to - start) * uy);
            // 紧贴着别人的长尺寸线也算踩（行距差一点点，框没碰上，看着还是站在那条线上）。
            var onLong = obstacles.Lines.Any(line => !CrossingLines([line], text.Angle).Any() && !Stub(line)
                && rows.Any(row => Math.Abs(Row(line) - row) < NearRow)
                && Math.Max(line.X1 * ux + line.Y1 * uy, line.X2 * ux + line.Y2 * uy) > to
                && Math.Min(line.X1 * ux + line.Y1 * uy, line.X2 * ux + line.Y2 * uy) < to + text.Width);
            return !onLong && AnnotationPlacementPlanner.Within([moved], frame) && Hits([moved], others) == 0;
        }

        // 已经贴在旁边、没压东西：不动。
        var (left, right) = (low - text.Width - BesideGap, high + BesideGap);
        var beside = (start + text.Width <= low && start >= left - BesideGap) || (start >= high && start <= right + BesideGap);
        if (onChain && beside && Free(start))
            return new SheetPoint(0, 0);
        foreach (var to in !onChain ? [] : start < (low + high - text.Width) / 2 ? new[] { left, right } : [right, left])
        {
            if (Free(to))
                return Along(to);
        }

        // 和自己的尺寸界线共线的线（隔壁尺寸量到同一个孔）不算挡路：字本来就对着这两条线。
        var clear = new Obstacles(
            obstacles.Lines.Where(line => !CrossingLines([line], text.Angle).Any()
                || !stations.Any(station => Math.Abs((line.X1 + line.X2) / 2 * ux + (line.Y1 + line.Y2) / 2 * uy - station) < 2e-4)).ToList(),
            obstacles.Texts);

        var corners = new[] { (view.Left, view.Bottom), (view.Left, view.Top), (view.Right, view.Bottom), (view.Right, view.Top) }
            .Select(p => p.Item1 * vx + p.Item2 * vy)
            .ToList();
        var (nearest, farther) = (corners.Max(), corners.Min());
        var outward = across + text.Height / 2 >= (nearest + farther) / 2;
        var along = (low + high - text.Width) / 2 - start;
        for (var step = 0; ; step++)
        {
            var to = outward ? nearest + step * SearchStep : farther - text.Height - step * SearchStep;
            var (dx, dy) = (along * ux + (to - across) * vx, along * uy + (to - across) * vy);
            var moved = text.Shift(dx, dy);
            if (!AnnotationPlacementPlanner.Within([moved], frame))
                return null;
            // 沿线放得下就行；垂直尺寸线方向要留够，离开这一行后到相邻尺寸线也是一个行距（用户定）。
            var inner = moved with
            {
                X = moved.X + Margin * ux - RowClearance * vx,
                Y = moved.Y + Margin * uy - RowClearance * vy,
                Width = Math.Max(moved.Width - 2 * Margin, 1e-6),
                Height = moved.Height + 2 * RowClearance,
            };
            if (Hits([inner], clear) == 0)
                return Math.Abs(dx) + Math.Abs(dy) < SeatTolerance ? new SheetPoint(0, 0) : new SheetPoint(dx, dy);
        }
    }

    /// <summary>孔标注可去的角位，按偏好排：近的一档四个角，再远一档……</summary>
    public static IEnumerable<CalloutSpot> CalloutSpots(HoleEdge hole)
    {
        foreach (var reach in CalloutReaches)
        {
            var offset = (hole.Radius + reach) * Math.Sqrt(0.5);
            foreach (var (x, y) in Corners)
                yield return new CalloutSpot(new SheetPoint(hole.X + x * offset, hole.Y + y * offset), x < 0);
        }
    }

    /// <summary>
    /// 把孔标注的文字整块挪到 <paramref name="spot"/> 要平移多少。文字随标注位置刚性平移；
    /// 文字在折点左边时折点是文字右缘外 <see cref="ShoulderGap"/>，在右边时是左缘外（SolidWorks 自己换引线那一侧）。
    /// </summary>
    /// <param name="texts">标注现在的文字。</param>
    /// <param name="shoulderY">现在的下划线高度（第一行文字的基线）。</param>
    /// <param name="spot">要去的角位。</param>
    public static (double Dx, double Dy) CalloutShift(IReadOnlyList<TextBox> texts, double shoulderY, CalloutSpot spot)
    {
        var corners = texts.SelectMany(text => text.Corners()).ToList();
        if (corners.Count == 0)
            return (0, 0);
        var anchorX = spot.TextLeft ? corners.Max(p => p.X) + ShoulderGap : corners.Min(p => p.X) - ShoulderGap;
        return (spot.Shoulder.X - anchorX, spot.Shoulder.Y - shoulderY);
    }

    /// <summary>
    /// 与文字方向不平行的线：尺寸自己的尺寸界线。尺寸线（与文字平行，字本来就站在上面）不在内。
    /// </summary>
    public static IEnumerable<SheetSegment> CrossingLines(IEnumerable<SheetSegment> lines, double angle)
    {
        var (ux, uy) = (Math.Cos(angle), Math.Sin(angle));
        foreach (var line in lines)
        {
            var (dx, dy) = (line.X2 - line.X1, line.Y2 - line.Y1);
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length > 1e-9 && Math.Abs(dx * uy - dy * ux) / length > 1e-3)
                yield return line;
        }
    }

    public static IReadOnlyList<TextBox> Shift(IReadOnlyList<TextBox> texts, double dx, double dy)
        => texts.Select(text => text.Shift(dx, dy)).ToList();

    /// <summary>圆（销钉符号）近似成八段折线，当障碍线条用。</summary>
    public static IEnumerable<SheetSegment> Octagon(SheetPoint center, double radius)
    {
        for (var i = 0; i < 8; i++)
        {
            var a = i * Math.PI / 4;
            var b = (i + 1) * Math.PI / 4;
            yield return new SheetSegment(
                center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a),
                center.X + radius * Math.Cos(b), center.Y + radius * Math.Sin(b));
        }
    }

    /// <summary>图纸点在文字框自己的坐标系里：沿文字方向、沿文字的「上」。</summary>
    private static (double X, double Y) Local(TextBox box, double x, double y)
    {
        var (ux, uy) = (Math.Cos(box.Angle), Math.Sin(box.Angle));
        var (dx, dy) = (x - box.X, y - box.Y);
        return (dx * ux + dy * uy, -dx * uy + dy * ux);
    }

    /// <summary>线段与轴对齐矩形是否有交（Liang–Barsky 裁剪）。</summary>
    private static bool ClipsRectangle(double x1, double y1, double x2, double y2, double left, double right, double bottom, double top)
    {
        var (dx, dy) = (x2 - x1, y2 - y1);
        double t0 = 0, t1 = 1;
        foreach (var (p, q) in new[] { (-dx, x1 - left), (dx, right - x1), (-dy, y1 - bottom), (dy, top - y1) })
        {
            if (Math.Abs(p) < 1e-15)
            {
                if (q < 0)
                    return false;
                continue;
            }

            var t = q / p;
            if (p < 0)
                t0 = Math.Max(t0, t);
            else
                t1 = Math.Min(t1, t);
            if (t0 > t1)
                return false;
        }

        return true;
    }
}
