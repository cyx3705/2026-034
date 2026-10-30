namespace HistoryStrenua;

/// <summary>选位时整页的障碍。线分开放，因为文字压线、引线穿线的代价不一样。</summary>
/// <param name="ViewLines">视图轮廓线、曲线近似段、孔圆近似段。</param>
/// <param name="Marks">中心符号线、中心线：字不许压，引线可以跨（孔本来就在它们上面）。</param>
/// <param name="DimensionLines">尺寸线、尺寸界线。</param>
/// <param name="Leaders">别的孔标注的引线。</param>
/// <param name="Texts">别的注解的字。</param>
/// <param name="Blocks">字整块不许进的地方：标题栏、修改栏、图纸注解、轴测图外框。</param>
/// <param name="Frame">图框；字不许出。</param>
internal sealed record CalloutField(
    IReadOnlyList<SheetSegment> ViewLines, IReadOnlyList<SheetSegment> Marks, IReadOnlyList<SheetSegment> DimensionLines,
    IReadOnlyList<SheetSegment> Leaders, IReadOnlyList<TextBox> Texts, IReadOnlyList<SheetRect> Blocks, SheetRect Frame);

/// <summary>选位结果：挂在哪个孔、折点与文字朝向、代价（越小越好）。</summary>
internal readonly record struct CalloutPick(HoleEdge Hole, CalloutSpot Spot, double Cost);

/// <summary>
/// 孔标注选位的纯几何部分（用户要改默认位置）：同种的每个孔、孔四周的折点网格都试一遍，按整页障碍打分取最小。
/// 只算一次、不经 SolidWorks 试挪——「整理标注」不挪孔标注就是因为逐个候选真挪一遍要 5 分钟。
/// </summary>
/// <remarks>
/// 字不能压东西是头等的，其次引线别穿尺寸和别人的字，再次引线短。板内空白和视图外一视同仁，
/// 只是引线出轮廓略加一点代价，所以近处有空地就放近处。
/// </remarks>
internal static class CalloutPlacePlanner
{
    /// <summary>折点网格：离孔心 ±40 mm、每 4 mm 一格。</summary>
    private const double GridStep = 0.004;
    private const int GridCount = 10;

    /// <summary>折点与孔心高差至少这么多，否则引线和下划线连成一条，看不出折。</summary>
    private const double MinRise = 0.003;

    /// <summary>一种孔最多试几个（挑离视图外框近的，靠里的孔引线一定更长）。</summary>
    private const int MaxHoles = 12;

    private const double TextHit = 1000;
    private const double LeaderThroughText = 500;
    private const double LeaderCrossLeader = 300;
    private const double LeaderCrossDimension = 100;
    private const double LeaderCrossEdge = 3;
    private const double LongLeader = 200;
    private const double PerMillimeter = 1;

    /// <summary>挑最好的位置；一个都放不下（都出框或进禁区）返回 null。</summary>
    public static CalloutPick? Choose(IReadOnlyList<TextBox> texts, double shoulderY, IReadOnlyList<HoleEdge> holes, CalloutField field)
    {
        CalloutPick? best = null;
        foreach (var hole in Nearest(holes))
        {
            for (var ix = -GridCount; ix <= GridCount; ix++)
            {
                for (var iy = -GridCount; iy <= GridCount; iy++)
                {
                    if (Math.Abs(iy * GridStep) < MinRise)
                        continue;
                    var at = new SheetPoint(hole.X + ix * GridStep, hole.Y + iy * GridStep);
                    foreach (var textLeft in new[] { true, false })
                    {
                        var spot = new CalloutSpot(at, textLeft);
                        var (dx, dy) = ClearancePlanner.CalloutShift(texts, shoulderY, spot);
                        var moved = ClearancePlanner.Shift(texts, dx, dy);
                        if (!OnCorrectSide(moved, hole.X, textLeft))
                            continue;
                        var cost = Cost(moved, Leader(hole, at), field, best?.Cost ?? double.MaxValue);
                        if (cost < (best?.Cost ?? double.MaxValue))
                            best = new CalloutPick(hole, spot, cost);
                    }
                }
            }
        }

        return best;
    }

    /// <summary>现在这个样子的代价，用来判断值不值得挪；出框按放不下算。</summary>
    public static double Cost(IReadOnlyList<TextBox> texts, IReadOnlyList<SheetSegment> leaders, CalloutField field, double limit = double.MaxValue)
    {
        var cost = 0.0;
        foreach (var text in texts)
        {
            if (text.Corners().Any(p => p.X < field.Frame.Left || p.X > field.Frame.Right || p.Y < field.Frame.Bottom || p.Y > field.Frame.Top))
                return double.MaxValue;
            if (field.Blocks.Any(rect => ClearancePlanner.Hits(text, new TextBox(rect.Left, rect.Bottom, rect.Width, rect.Height))))
                return double.MaxValue;
            cost += TextHit * (field.ViewLines.Count(line => ClearancePlanner.Hits(text, line))
                + field.Marks.Count(line => ClearancePlanner.Hits(text, line))
                + field.DimensionLines.Count(line => ClearancePlanner.Hits(text, line))
                + field.Leaders.Count(line => ClearancePlanner.Hits(text, line))
                + field.Texts.Count(other => ClearancePlanner.Hits(text, other)));
            if (cost >= limit)
                return cost;
        }

        var length = LeaderPlanner.Length(leaders);
        cost += length * 1000 * PerMillimeter + (length > LeaderPlanner.LongLeader ? LongLeader : 0);
        cost += LeaderCrossLeader * LeaderPlanner.Crossings(leaders, field.Leaders);
        cost += LeaderCrossDimension * LeaderPlanner.Crossings(leaders, field.DimensionLines);
        cost += LeaderCrossEdge * LeaderPlanner.Crossings(leaders, field.ViewLines);
        cost += LeaderThroughText * field.Texts.Count(other => leaders.Any(leader => ClearancePlanner.Hits(other, leader)));
        return cost;
    }

    /// <summary>字中点在孔的这一侧，SolidWorks 才会把引线接在我们要的那一头（真机：过了孔心它就换到另一头）。</summary>
    private static bool OnCorrectSide(IReadOnlyList<TextBox> texts, double holeX, bool textLeft)
    {
        var corners = texts.SelectMany(text => text.Corners()).ToList();
        if (corners.Count == 0)
            return false;
        var middle = (corners.Min(p => p.X) + corners.Max(p => p.X)) / 2;
        return textLeft ? middle < holeX : middle > holeX;
    }

    /// <summary>从孔边到折点的引线。</summary>
    private static IReadOnlyList<SheetSegment> Leader(HoleEdge hole, SheetPoint at)
    {
        var (dx, dy) = (at.X - hole.X, at.Y - hole.Y);
        var length = Math.Sqrt(dx * dx + dy * dy);
        return [new SheetSegment(hole.X + dx / length * hole.Radius, hole.Y + dy / length * hole.Radius, at.X, at.Y)];
    }

    private static IEnumerable<HoleEdge> Nearest(IReadOnlyList<HoleEdge> holes)
    {
        if (holes.Count <= MaxHoles)
            return holes;
        var (left, right, bottom, top) = (holes.Min(h => h.X), holes.Max(h => h.X), holes.Min(h => h.Y), holes.Max(h => h.Y));
        return holes.OrderBy(h => Math.Min(Math.Min(h.X - left, right - h.X), Math.Min(h.Y - bottom, top - h.Y))).Take(MaxHoles);
    }
}
