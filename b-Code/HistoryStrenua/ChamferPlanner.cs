using System.Globalization;

namespace HistoryStrenua;

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

/// <summary>倒角的一个线性尺寸：量斜边的横向（<see cref="PositionAxis.Horizontal"/>）或竖向跨度，尺寸放在 <see cref="At"/>。</summary>
internal readonly record struct ChamferPlacement(PositionAxis Axis, SheetPoint At);

/// <summary>
/// 要加的倒角尺寸：标哪条斜边、几个线性尺寸放哪（45° 倒角一个，写「C5」；不等边两个）、同尺寸几个合标。
/// </summary>
internal readonly record struct ChamferTarget(int Index, IReadOnlyList<ChamferPlacement> Placements, int Count, bool Equal)
{
    /// <summary>尺寸文字的前缀：「2 x C」「C」，不等边的只写「2 x 」或不写。</summary>
    public string Prefix => (Count > 1 ? $"{Count} x " : string.Empty) + (Equal ? "C" : string.Empty);
}

/// <summary>规划结果。</summary>
/// <param name="Targets">要加的倒角尺寸。</param>
/// <param name="ChamferCount">认出的倒角（同一张倒角面算一个）。</param>
/// <param name="DefaultCount">C1 按技术要求「未注倒角C1」不标的个数。</param>
/// <param name="Dimensioned">这个视图里已标（本身有尺寸，或同尺寸的已标过）跳过的个数。</param>
/// <param name="Elsewhere">这一页别的视图已标过同一个倒角、跳过的个数。</param>
/// <param name="NoLead">斜边两头接不上水平 / 竖直直边、认不出哪边是零件外、标不了的个数。</param>
internal sealed record ChamferPlan(IReadOnlyList<ChamferTarget> Targets, int ChamferCount, int DefaultCount, int Dimensioned, int Elsewhere, int NoLead);

/// <summary>
/// 倒角标注（1.10.0）的纯几何部分：哪些倒角要标、同尺寸的怎么合标、尺寸量哪条直角边、放哪。不碰 SolidWorks，能离线测。
/// </summary>
/// <remarks>
/// <para>依据用户手工图（限位块右、限位块1「2 x C5」，限位块前「C5」，升降底板、保持液存放工装「4 x C2」，手指片「5 x C2」）：
/// 在倒角侧着看成斜线的视图里标；同一视图里同尺寸的倒角只标一个、前面写「N x 」（2 个就合标，与圆角的 3 个不同）；
/// 标靠右的那个（右下、右上角）。技术要求写了「未注倒角C1」，C1 不标。</para>
/// <para>用户定（审 1.10.0 时）：不用 SolidWorks 的倒角尺寸（引线箭头那种），改成普通线性尺寸——量斜边的一条直角边，文字写「C5」「2 x C5」；
/// 不等边的倒角两条直角边各标一个、不写 C。</para>
/// <para>量哪条直角边：尺寸线放在被倒掉的那个角（两条直边延长线的交点，在零件外）那一侧、视图外 <see cref="Offset"/>，
/// 先下边、再右边、再上边、再左边（孔位尺寸与外轮廓都在上边和左边）；出了图框或压线多就换另一条。</para>
/// </remarks>
internal static class ChamferPlanner
{
    /// <summary>技术要求「未注倒角C1」：两条直角边都是 1 mm（差 0.005 mm 内）不标。</summary>
    public const double DefaultLeg = 0.001;

    /// <summary>同尺寸：两条直角边各差不到 0.005 mm（模型）。</summary>
    public const double SizeTolerance = 5e-6;

    /// <summary>同尺寸的倒角到这么多个就合标「N x C」（用户 2 个就合标）。</summary>
    public const int GroupThreshold = 2;

    /// <summary>直边端点离斜边端点不到这么远算相连（图纸 0.05 mm）。</summary>
    public const double EndTolerance = 5e-5;

    /// <summary>尺寸线离视图最外的线多远（图纸 8 mm）。</summary>
    public const double Offset = 0.008;

    /// <summary>文字估计：字高 3.5 mm，每个字宽 2.6 mm，文字在尺寸线外侧约 2.5 mm（与模板「汉仪长仿宋 3.5」相符）。</summary>
    private const double CharHeight = 0.0035;
    private const double CharWidth = 0.0026;
    private const double TextLift = 0.0025;

    /// <param name="chamfers">视图里的倒角斜边。</param>
    /// <param name="lines">视图里的直边（找相连的直边、定视图范围）。</param>
    /// <param name="obstacles">文字不要压的线（视图里的边、标题栏等的边）。</param>
    /// <param name="dimensioned">这个视图里已有倒角尺寸连着的斜边。</param>
    /// <param name="elsewhere">这一页别的视图里已有倒角尺寸的倒角面（<see cref="ChamferEdge.Key"/>）。</param>
    /// <param name="inside">尺寸文字要落在这里面（图框）；null 不限。</param>
    /// <param name="texts">已有注解的文字框，也要躲（1.11.0）。</param>
    /// <param name="avoid">「避障」开关（1.11.0）：false 时不看压不压，按 下 → 右 → 上 → 左 取第一个在图框里的。</param>
    public static ChamferPlan Plan(IReadOnlyList<ChamferEdge> chamfers, IReadOnlyList<SheetSegment> lines, IReadOnlyList<SheetSegment> obstacles,
        IReadOnlyList<SheetSegment> dimensioned, IReadOnlySet<string> elsewhere, SheetRect? inside = null, IReadOnlyList<TextBox>? texts = null, bool avoid = true)
    {
        var distinct = new List<ChamferEdge>();
        foreach (var chamfer in chamfers)
            if (!distinct.Any(known => known.Key == chamfer.Key || DimensionGeometry.SameSegment(known.Segment, chamfer.Segment)))
                distinct.Add(chamfer);

        var defaults = distinct.Count(IsDefault);
        var rest = distinct.Where(chamfer => !IsDefault(chamfer)).ToList();
        var labeled = rest.Where(chamfer => dimensioned.Any(segment => DimensionGeometry.SameSegment(segment, chamfer.Segment))).ToList();
        // 这个视图里某个尺寸已经标过，同尺寸的别的倒角也算标过（用户限位块前两个 C5 只标了一个「C5」）。
        var covered = rest.Where(chamfer => !labeled.Contains(chamfer) && labeled.Any(other => SameSize(other.Size, chamfer.Size))).ToList();
        var other = rest.Where(chamfer => !labeled.Contains(chamfer) && !covered.Contains(chamfer) && elsewhere.Contains(chamfer.Key)).ToList();
        var pending = rest.Except(labeled).Except(covered).Except(other).ToList();

        var groups = new List<List<ChamferEdge>>();
        foreach (var chamfer in pending)
        {
            var group = groups.FirstOrDefault(members => SameSize(members[0].Size, chamfer.Size));
            if (group is null)
                groups.Add([chamfer]);
            else
                group.Add(chamfer);
        }

        var extent = Extent(lines);
        var placed = new List<TextBox>(avoid ? texts ?? [] : []);
        var walls = avoid ? obstacles : [];
        var targets = new List<ChamferTarget>();
        var noLead = 0;
        foreach (var group in groups.OrderByDescending(members => members.Max(chamfer => chamfer.Middle.Y)))
        {
            // 标靠右的那个（再靠上），要认得出被倒掉的角在哪边。
            var candidates = group
                .Select(chamfer => (Chamfer: chamfer, Corner: Corner(chamfer.Segment, lines)))
                .Where(item => item.Corner is not null)
                .OrderByDescending(item => Math.Round(item.Chamfer.Middle.X / EndTolerance))
                .ThenByDescending(item => item.Chamfer.Middle.Y)
                .ToList();
            if (candidates.Count == 0)
            {
                noLead += group.Count;
                continue;
            }

            var (chosen, corner) = candidates[0];
            var count = group.Count >= GroupThreshold ? group.Count : 1;
            var target = new ChamferTarget(chosen.Index, [], count, chosen.Equal);
            var text = target.Prefix + Value(chosen.Size.Short);
            var placements = Place(chosen, corner!.Value, extent, text.Length, walls, avoid ? placed : [], inside);
            targets.Add(target with { Placements = placements });
        }

        return new ChamferPlan(targets, distinct.Count, defaults, labeled.Count + covered.Count, other.Count, noLead);
    }

    /// <summary>C1（两条直角边都是 1 mm）。</summary>
    public static bool IsDefault(ChamferEdge chamfer)
        => Math.Abs(chamfer.LegX - DefaultLeg) <= SizeTolerance && Math.Abs(chamfer.LegY - DefaultLeg) <= SizeTolerance;

    private static bool SameSize((double Short, double Long) a, (double Short, double Long) b)
        => Math.Abs(a.Short - b.Short) <= SizeTolerance && Math.Abs(a.Long - b.Long) <= SizeTolerance;

    /// <summary>尺寸值的写法：「5」「0.5」。</summary>
    public static string Value(double model) => (model * 1000).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// 被倒掉的那个角（两条直边延长线的交点，在零件外）：斜边某一头接着水平直边时，角在（另一头的 X，这一头的 Y）；接着竖直直边时在（这一头的 X，另一头的 Y）。
    /// 两头都接不上水平 / 竖直直边返回 null。
    /// </summary>
    public static SheetPoint? Corner(SheetSegment chamfer, IReadOnlyList<SheetSegment> lines)
    {
        foreach (var atStart in new[] { true, false })
        {
            var (px, py, qx, qy) = atStart ? (chamfer.X1, chamfer.Y1, chamfer.X2, chamfer.Y2) : (chamfer.X2, chamfer.Y2, chamfer.X1, chamfer.Y1);
            foreach (var line in lines)
            {
                if (DimensionGeometry.SameSegment(line, chamfer) || !(Near(line.X1, line.Y1, px, py) || Near(line.X2, line.Y2, px, py)))
                    continue;
                var (dx, dy) = (Math.Abs(line.X2 - line.X1), Math.Abs(line.Y2 - line.Y1));
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length <= EndTolerance)
                    continue;
                if (dy <= length * 1e-3)
                    return new SheetPoint(qx, py);
                if (dx <= length * 1e-3)
                    return new SheetPoint(px, qy);
            }
        }

        return null;
    }

    /// <summary>
    /// 尺寸放哪：被倒掉的角在下（上）就量横向跨度、尺寸线放在视图下（上）边外 <see cref="Offset"/>；在右（左）就量竖向跨度、放在视图右（左）边外。
    /// 两种按 下 → 右 → 上 → 左 排先后，取文字在 <paramref name="inside"/> 里、压线最少的那种（一样少取靠前的）。不等边的两条直角边都标。
    /// </summary>
    private static IReadOnlyList<ChamferPlacement> Place(ChamferEdge chamfer, SheetPoint corner, SheetRect extent, int characters,
        IReadOnlyList<SheetSegment> obstacles, List<TextBox> placed, SheetRect? inside)
    {
        var middle = chamfer.Middle;
        var below = corner.Y < middle.Y;
        var right = corner.X > middle.X;
        var horizontal = new ChamferPlacement(PositionAxis.Horizontal,
            new SheetPoint(middle.X, below ? extent.Bottom - Offset : extent.Top + Offset));
        var vertical = new ChamferPlacement(PositionAxis.Vertical,
            new SheetPoint(right ? extent.Right + Offset : extent.Left - Offset, middle.Y));
        // 下 0、右 1、上 2、左 3。
        var options = new[] { (Placement: horizontal, Rank: below ? 0 : 2), (Placement: vertical, Rank: right ? 1 : 3) }
            .OrderBy(option => option.Rank)
            .Select(option => option.Placement)
            .ToList();

        if (!chamfer.Equal)
        {
            // 不等边：两条直角边各一个，不写 C。
            foreach (var option in options)
                placed.Add(Box(option, characters));
            return options;
        }

        var scored = options
            .Select(option => Clamp(option, characters, inside))
            .Select((option, order) =>
            {
                var box = Box(option, characters);
                var outside = inside is { } frame && !new SheetRect(box.X, box.Y, box.X + box.Width, box.Y + box.Height).Within(frame);
                var hits = obstacles.Count(line => ClearancePlanner.Hits(box, line)) + placed.Count(other => ClearancePlanner.Hits(box, other));
                return (Option: option, Box: box, Outside: outside, Hits: hits, Order: order);
            })
            .OrderBy(item => item.Outside)
            .ThenBy(item => item.Hits)
            .ThenBy(item => item.Order)
            .First();
        placed.Add(scored.Box);
        return [scored.Option];
    }

    /// <summary>
    /// 文字沿尺寸线挪进图框（尺寸线本身的位置不动）：倒角靠着图框时，居中的文字会伸出去（限位块前下视图底边离图框只有几毫米）。
    /// </summary>
    private static ChamferPlacement Clamp(ChamferPlacement placement, int characters, SheetRect? inside)
    {
        if (inside is not { } frame)
            return placement;
        var half = characters * CharWidth / 2;
        var at = placement.At;
        return placement.Axis == PositionAxis.Horizontal
            ? placement with { At = at with { X = Math.Clamp(at.X, frame.Left + half, Math.Max(frame.Left + half, frame.Right - half)) } }
            : placement with { At = at with { Y = Math.Clamp(at.Y, frame.Bottom + half, Math.Max(frame.Bottom + half, frame.Top - half)) } };
    }

    /// <summary>尺寸文字估计占的地方：水平尺寸的字在尺寸线上方，竖直尺寸的字竖着在尺寸线左侧。</summary>
    private static TextBox Box(ChamferPlacement placement, int characters)
    {
        var length = characters * CharWidth;
        var at = placement.At;
        return placement.Axis == PositionAxis.Horizontal
            ? new TextBox(at.X - length / 2, at.Y + TextLift - CharHeight / 2, length, CharHeight)
            : new TextBox(at.X - TextLift - CharHeight / 2, at.Y - length / 2, CharHeight, length);
    }

    /// <summary>视图里全部直边的范围（图纸）。</summary>
    private static SheetRect Extent(IReadOnlyList<SheetSegment> lines)
    {
        if (lines.Count == 0)
            return default;
        return new SheetRect(
            lines.Min(line => Math.Min(line.X1, line.X2)),
            lines.Min(line => Math.Min(line.Y1, line.Y2)),
            lines.Max(line => Math.Max(line.X1, line.X2)),
            lines.Max(line => Math.Max(line.Y1, line.Y2)));
    }

    private static bool Near(double x1, double y1, double x2, double y2)
        => Math.Abs(x1 - x2) <= EndTolerance && Math.Abs(y1 - y2) <= EndTolerance;
}
