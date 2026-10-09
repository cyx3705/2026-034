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

    /// <summary>孔标注的四个角位，按偏好排：左上（默认）、右上、左下、右下。</summary>
    private static readonly (int X, int Y)[] Corners = [(-1, 1), (1, 1), (-1, -1), (1, -1)];

    /// <summary><paramref name="texts"/> 压到了几样障碍（线条、文字各算一样）。</summary>
    public static int Hits(IReadOnlyList<TextBox> texts, Obstacles obstacles)
        => obstacles.Lines.Count(line => texts.Any(text => Hits(text, line)))
            + obstacles.Texts.Count(other => texts.Any(text => Hits(text, other)));

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
    /// 给压着线的孔标注挑角位：第一个一样都不压的；都压就挑压得最少、且比现在 <paramref name="currentHits"/> 少的；
    /// 再没有就 null（不挪）。
    /// </summary>
    public static CalloutSpot? ChooseCallout(
        IReadOnlyList<TextBox> texts, double shoulderY, HoleEdge hole, Obstacles obstacles, int currentHits)
    {
        CalloutSpot? best = null;
        var bestHits = currentHits;
        foreach (var spot in CalloutSpots(hole))
        {
            var (dx, dy) = CalloutShift(texts, shoulderY, spot);
            var hits = Hits(Shift(texts, dx, dy), obstacles);
            if (hits == 0)
                return spot;
            if (hits < bestHits)
                (best, bestHits) = (spot, hits);
        }

        return best;
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
