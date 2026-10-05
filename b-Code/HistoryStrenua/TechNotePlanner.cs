namespace HistoryStrenua;

/// <summary>技术要求只许放的两处（1.13.0，用户定硬性要求）：右下角标题栏（图号所在）的正上方、正左侧。</summary>
internal enum NoteSlot
{
    /// <summary>标题栏正上方，右边与标题栏右边对齐。</summary>
    AboveTitle,

    /// <summary>标题栏左侧，贴着图框底边，右边挨着标题栏。</summary>
    LeftOfTitle,
}

/// <summary>视图能往哪挪：自由；与父视图左右对齐的只能左右挪；上下对齐的只能上下挪（SolidWorks 保持对齐）。</summary>
internal enum ViewAxis
{
    Free,
    Horizontal,
    Vertical,
}

/// <summary>规划用的一个视图：名字、与它对齐的父视图（挪父视图时它跟着走）、能挪的方向、连同注解占的地方。</summary>
internal sealed record PlanView(string Name, string? Parent, ViewAxis Axis, IReadOnlyList<SheetRect> Rects);

/// <summary>挪一个视图（连同与它对齐的子视图）。</summary>
internal sealed record ViewMove(string Name, double Dx, double Dy);

/// <summary>技术要求放哪（左上角，注释以左上角定位）、为它挪哪些视图；<see cref="Free"/> 为 false 时没让开、<see cref="Problem"/> 说压到了谁。</summary>
internal sealed record NotePlan(NoteSlot Slot, SheetPoint TopLeft, IReadOnlyList<ViewMove> Moves, bool Free, string Problem);

/// <summary>
/// 技术要求的落点规划（1.13.0）。用户定的硬性要求：技术要求只能在图纸右下角标题栏的上方或左侧，别处不行；
/// 「避障」开时压到视图（连同它的尺寸、注解）就把视图挪开让出地方，关着直接放、不躲不挪。
/// </summary>
/// <remarks>
/// <para>先后：有原来的技术要求（换一份）时先在它原来那一处——原处空着就放、原处挪得开就挪、再看另一处；新加的先上方后左侧，
/// 两处都空着取上方，都不空先试上方挪不挪得开。</para>
/// <para>图纸格式里的注解（「其余」粗糙度、公司名……）挪不了：落点压到就在这一处里让一点（上方往上、左侧往左，最多
/// <see cref="KeepOutShift"/>）。</para>
/// <para>挪视图：压着落点的视图往上或往左挪（下边是标题栏与图框，右边是图框），一步 <see cref="DrawingPlanner.AwayStep"/>，
/// 挪到不压为止且不新压别的视图、图框外、禁区；与父视图对齐的视图只能沿对齐方向挪，不行就挪它的父视图（子视图跟着走）。
/// 每次挑挪得最少的那个，压着的都让开为止。</para>
/// </remarks>
internal static class TechNotePlanner
{
    /// <summary>落点为躲图纸格式注解在本处最多让多远（图纸 30 mm）。</summary>
    public const double KeepOutShift = 0.030;

    /// <summary>挪一个视图最多挪多远（图纸 300 mm）。</summary>
    public const double MaxMove = 0.300;

    private const int MaxRounds = 30;

    /// <summary>一处的基本落点（不躲任何东西）。</summary>
    public static SheetRect Anchor(SheetSpace sheet, NoteSlot slot, double width, double height)
    {
        var title = sheet.TitleBlock;
        var gap = DrawingPlanner.Gap;
        return slot == NoteSlot.AboveTitle
            ? new SheetRect(title.Right - gap - width, title.Top + gap, title.Right - gap, title.Top + gap + height)
            : new SheetRect(title.Left - gap - width, sheet.Frame.Bottom + gap, title.Left - gap, sheet.Frame.Bottom + gap + height);
    }

    /// <summary>
    /// 一处的实际落点：基本落点压到图纸格式注解（禁区，标题栏除外）就在这一处里让（上方往上、左侧往左）；出了图框或让不开返回 null。
    /// </summary>
    public static SheetRect? Spot(SheetSpace sheet, NoteSlot slot, double width, double height)
    {
        var anchor = Anchor(sheet, slot, width, height);
        var (dx, dy) = slot == NoteSlot.AboveTitle ? (0.0, 1.0) : (-1.0, 0.0);
        for (var shift = 0.0; shift <= KeepOutShift + 1e-9; shift += DrawingPlanner.AwayStep)
        {
            var rect = new SheetRect(anchor.Left + dx * shift, anchor.Bottom + dy * shift, anchor.Right + dx * shift, anchor.Top + dy * shift);
            if (!rect.Within(sheet.Frame))
                return null;
            if (!sheet.KeepOuts.Any(rect.Inflate(DrawingPlanner.Gap / 2).Overlaps))
                return rect;
        }

        return null;
    }

    /// <summary>已有的技术要求在哪一处：右下角离哪一处的基本落点近就算哪一处。</summary>
    public static NoteSlot SlotOf(SheetSpace sheet, SheetRect note)
    {
        double Distance(NoteSlot slot)
        {
            var anchor = Anchor(sheet, slot, note.Width, note.Height);
            return Math.Pow(anchor.Right - note.Right, 2) + Math.Pow(anchor.Bottom - note.Bottom, 2);
        }

        return Distance(NoteSlot.LeftOfTitle) < Distance(NoteSlot.AboveTitle) ? NoteSlot.LeftOfTitle : NoteSlot.AboveTitle;
    }

    /// <summary>
    /// 规划技术要求放哪、挪哪些视图。<paramref name="preferred"/> 是原来那一处（换一份时），新加的给 null；
    /// <paramref name="avoid"/> 是「避障」开关。
    /// </summary>
    public static NotePlan Place(SheetSpace sheet, IReadOnlyList<PlanView> views, double width, double height, NoteSlot? preferred, bool avoid)
    {
        var first = preferred ?? NoteSlot.AboveTitle;
        var second = first == NoteSlot.AboveTitle ? NoteSlot.LeftOfTitle : NoteSlot.AboveTitle;
        if (!avoid)
            return new NotePlan(first, TopLeft(Anchor(sheet, first, width, height)), [], true, string.Empty);

        // 换一份时原处优先：原处空 → 原处挪 → 另一处空 → 另一处挪；新加的：上方空 → 左侧空 → 上方挪 → 左侧挪。
        (NoteSlot Slot, bool Move)[] tries = preferred is null
            ? [(first, false), (second, false), (first, true), (second, true)]
            : [(first, false), (first, true), (second, false), (second, true)];
        foreach (var (slot, move) in tries)
        {
            if (Spot(sheet, slot, width, height) is not { } rect)
                continue;
            var hit = views.Where(view => view.Rects.Any(r => r.Overlaps(rect.Inflate(DrawingPlanner.Gap / 2)))).ToList();
            if (hit.Count == 0)
                return new NotePlan(slot, TopLeft(rect), [], true, string.Empty);
            if (move && MakeRoom(sheet, views, rect) is { } moves)
                return new NotePlan(slot, TopLeft(rect), moves, true, string.Empty);
        }

        // 哪处都让不开：放在原处（新加的放上方），说压到了谁。
        var fallback = Spot(sheet, first, width, height) ?? Anchor(sheet, first, width, height);
        var blocking = views.Where(view => view.Rects.Any(r => r.Overlaps(fallback))).Select(view => "「" + view.Name + "」").ToList();
        var problem = blocking.Count > 0
            ? $"标题栏上方、左侧都让不出地方，压着视图 {string.Join("、", blocking)}"
            : "标题栏上方、左侧都放不下（图框里没有这么大的地方）";
        return new NotePlan(first, TopLeft(fallback), [], false, problem);
    }

    /// <summary>挪视图给 <paramref name="note"/> 让地方；让不开返回 null。返回的每一项是对一个视图（连同对齐子视图）的总挪动。</summary>
    internal static IReadOnlyList<ViewMove>? MakeRoom(SheetSpace sheet, IReadOnlyList<PlanView> views, SheetRect note)
    {
        var byName = views.GroupBy(view => view.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var offsets = views.ToDictionary(view => view.Name, _ => (Dx: 0.0, Dy: 0.0), StringComparer.Ordinal);
        var order = new List<string>();
        var moved = new Dictionary<string, (double Dx, double Dy)>(StringComparer.Ordinal);
        var keep = note.Inflate(DrawingPlanner.Gap / 2);

        IEnumerable<SheetRect> RectsOf(PlanView view, double dx = 0, double dy = 0)
        {
            var (ox, oy) = offsets[view.Name];
            return view.Rects.Select(r => new SheetRect(r.Left + ox + dx, r.Bottom + oy + dy, r.Right + ox + dx, r.Top + oy + dy));
        }

        // 跟着一起走的：自己与（多层）对齐子视图。
        List<PlanView> Group(PlanView root)
        {
            var group = new List<PlanView> { root };
            for (var i = 0; i < group.Count; i++)
                group.AddRange(views.Where(view => view.Parent == group[i].Name && view.Axis != ViewAxis.Free && !group.Contains(view)));
            return group;
        }

        for (var round = 0; round < MaxRounds; round++)
        {
            var hit = views.FirstOrDefault(view => RectsOf(view).Any(keep.Overlaps));
            if (hit is null)
                return order.Select(name => new ViewMove(name, moved[name].Dx, moved[name].Dy)).ToList();

            // 能挪的：它自己，或者它对齐着的父视图（再往上）。
            var units = new List<PlanView> { hit };
            for (var unit = hit; unit.Axis != ViewAxis.Free && unit.Parent is { } parent && byName.TryGetValue(parent, out var up) && !units.Contains(up); unit = up)
                units.Add(up);

            (PlanView Unit, double Dx, double Dy, double Distance)? best = null;
            foreach (var unit in units)
            {
                var group = Group(unit);
                var others = views.Where(view => !group.Contains(view)).SelectMany(view => RectsOf(view)).ToList();
                var before = Violation(sheet, group.SelectMany(view => RectsOf(view)).ToList(), others);
                foreach (var (ux, uy) in Directions(unit.Axis))
                {
                    for (var distance = DrawingPlanner.AwayStep; distance <= MaxMove + 1e-9; distance += DrawingPlanner.AwayStep)
                    {
                        if (best is { } b && distance >= b.Distance - 1e-12)
                            break;
                        var rects = group.SelectMany(view => RectsOf(view, ux * distance, uy * distance)).ToList();
                        if (rects.Any(keep.Overlaps))
                            continue;
                        if (Violation(sheet, rects, others) <= before + 1e-12)
                            best = (unit, ux * distance, uy * distance, distance);
                        break;
                    }
                }
            }

            if (best is not { } chosen)
                return null;
            foreach (var view in Group(chosen.Unit))
                offsets[view.Name] = (offsets[view.Name].Dx + chosen.Dx, offsets[view.Name].Dy + chosen.Dy);
            if (!moved.ContainsKey(chosen.Unit.Name))
                order.Add(chosen.Unit.Name);
            moved[chosen.Unit.Name] = moved.TryGetValue(chosen.Unit.Name, out var sum) ? (sum.Dx + chosen.Dx, sum.Dy + chosen.Dy) : (chosen.Dx, chosen.Dy);
        }

        return null;
    }

    /// <summary>让开的方向：往上、往左（下边是标题栏与图框底，右边是图框）；对齐的子视图只剩沿对齐的那一个。</summary>
    private static IEnumerable<(double Dx, double Dy)> Directions(ViewAxis axis)
    {
        if (axis != ViewAxis.Horizontal)
            yield return (0, 1);
        if (axis != ViewAxis.Vertical)
            yield return (-1, 0);
    }

    /// <summary>
    /// 挪完「坏不坏」：出图框的面积 + 压禁区的面积 + 压别的视图的面积。只要求不比挪前坏——视图的尺寸本来就可能贴着图框、压着别的。
    /// </summary>
    private static double Violation(SheetSpace sheet, IReadOnlyList<SheetRect> rects, IReadOnlyList<SheetRect> others)
    {
        var frame = sheet.Frame;
        var total = 0.0;
        foreach (var rect in rects)
        {
            var inside = Overlap(rect, frame);
            total += rect.Width * rect.Height - inside;
            total += sheet.KeepOuts.Sum(other => Overlap(rect, other));
            total += others.Sum(other => Overlap(rect, other));
        }

        return total;
    }

    private static double Overlap(SheetRect a, SheetRect b)
        => Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left)) * Math.Max(0, Math.Min(a.Top, b.Top) - Math.Max(a.Bottom, b.Bottom));

    private static SheetPoint TopLeft(SheetRect rect) => new(rect.Left, rect.Top);
}
