namespace HistoryStrenua;

/// <summary>出图规划：估留白、排版、找空地。</summary>
internal static partial class DrawingPlanner
{
    /// <summary>
    /// 按包围盒估各视图在图纸上多大（选模板与比例时还没有视图可量）。标准视图与包围盒同轴，估出来就是实际外框；
    /// 轴测图取包围盒八个角在轴测平面上的投影。
    /// </summary>
    public static LayoutRequest Estimate(PartGeometry part, StandardView main, IReadOnlyList<ViewSlot> sides, double scale, bool chain,
        double noteWidth = DefaultNoteWidth, double noteHeight = DefaultNoteHeight)
    {
        var box = part.Box;
        var w = box.Extent(main.Right) * scale + 2 * ViewPad;
        var h = box.Extent(main.Up) * scale + 2 * ViewPad;
        var d = box.Extent(main.Normal) * scale + 2 * ViewPad;
        var mainMargin = AnnotationMargin(HoleKinds(part, main.Normal), chain);
        var mainBox = new ViewBox(w, h, mainMargin, mainMargin);
        var sideBoxes = new Dictionary<ViewSlot, ViewBox>();
        foreach (var slot in sides)
        {
            var horizontal = slot is ViewSlot.Right or ViewSlot.Left;
            var normal = horizontal ? main.Right : main.Up;
            sideBoxes[slot] = horizontal ? SideBox(d, h, slot, HoleKinds(part, normal), chain) : SideBox(w, d, slot, HoleKinds(part, normal), chain);
        }

        var (isoRight, isoUp) = IsoAxes(main.Normal, main.Right, main.Up);
        var corners = box.Corners().ToList();
        var isoW = (corners.Max(c => c.Dot(isoRight)) - corners.Min(c => c.Dot(isoRight))) * scale + 2 * ViewPad;
        var isoH = (corners.Max(c => c.Dot(isoUp)) - corners.Min(c => c.Dot(isoUp))) * scale + 2 * ViewPad;
        return new LayoutRequest(mainBox, sideBoxes, new ViewBox(isoW, isoH, 0, 0), noteWidth, noteHeight, IsoFactors(part, scale));
    }

    /// <summary>轴测图可以缩成的比例（相对图纸比例）：原比例，再往下两档标准比例（1:2 → 1:5、1:10；用户移动安装版5 轴测图就用了 1:5）。</summary>
    public static IReadOnlyList<double> IsoFactors(PartGeometry part, double scale)
        => new[] { 1.0 }.Concat(Scales(part).Where(s => s < scale - 1e-12).Take(2).Select(s => s / scale)).ToList();

    /// <summary>
    /// 排版：主视图连同左右、上下投影视图从图框左上角排起（左边、上边留标注空间），轴测图放进剩下的空地（离主视图右侧最近处），
    /// 放不下就缩成一半；技术要求只放标题栏正上方或左侧（1.13.0），放不下就把轴测图再缩一档重放、再不行先钉技术要求后放轴测图。不碰图框外、标题栏等禁区。
    /// </summary>
    /// <param name="sheet">图纸。</param>
    /// <param name="request">要排的东西。</param>
    /// <param name="force">视图组放不下也照排（1.25.0，用户定：超出图框优先调位置，别的标注由整理标注拉回框内）。</param>
    public static LayoutResult Layout(SheetSpace sheet, LayoutRequest request, bool force = false)
    {
        var frame = sheet.Frame;

        // 视图组从图框左上角排起；压到左上角图号框之类的禁区就整组往下、往右挪（2.5 mm 一步，最多 60 mm），
        // 先只往下、再只往右、再两边一起，挑挪得最少的。
        var block = Block(frame, request, 0, 0);
        var problem = BlockProblem(sheet, block.Occupied);
        for (var total = ShiftStep; problem.Length > 0 && total <= MaxShift + 1e-9; total += ShiftStep)
        {
            foreach (var (dx, dy) in Shifts(total))
            {
                var moved = Block(frame, request, dx, dy);
                if (BlockProblem(sheet, moved.Occupied).Length == 0)
                {
                    (block, problem) = (moved, string.Empty);
                    break;
                }
            }
        }

        var (mainCenter, centers, occupied) = block;
        if (problem.Length > 0 && !force)
            return new LayoutResult(false, mainCenter, centers, null, 1, null, problem);

        // 轴测图、技术要求：技术要求找不到空地时，轴测图从再小一档起重放（轴测图只是看个样子，技术要求要读；
        // 1.10.0 三个限位块为倒角多了下视图，A4 上原比例的轴测图占了技术要求的地方，技术要求压到了轴测图与孔标注）。
        var factors = request.IsoFactors ?? [1.0, 0.5];
        // REQ-DQ-03：先给只能靠标题栏的技术要求留位置，再试完整轴测比例。
        // 旧次序会在明明上方有空地时先把轴测图缩到 1:10（移动底板），才尝试交换放置顺序。
        if (request.NoteWidth > 0 && request.Iso.Width > 0)
        {
            var noteFirst = Extras(sheet, request, mainCenter, centers, occupied, factors, noteFirst: true);
            if (noteFirst.Problem.Length == 0) return noteFirst;
        }
        LayoutResult? first = null;
        for (var start = 0; start < (request.Iso.Width > 0 ? factors.Count : 1); start++)
        {
            var result = Extras(sheet, request, mainCenter, centers, occupied, factors.Skip(start).ToList());
            if (result.Problem.Length == 0)
                return result;
            first ??= result;
        }

        return first!;
    }

    /// <summary>视图组排好后放轴测图与技术要求；<paramref name="factors"/> 是这一轮轴测图依次试的缩放。</summary>
    private static LayoutResult Extras(SheetSpace sheet, LayoutRequest request, SheetPoint mainCenter, IReadOnlyDictionary<ViewSlot, SheetPoint> centers,
        IReadOnlyList<SheetRect> views, IReadOnlyList<double> factors, bool noteFirst = false)
    {
        var frame = sheet.Frame;
        var occupied = views.ToList();
        var crowded = new List<string>();

        // 技术要求（见 NotePlace）。没有技术要求（宽为 0）就不放。
        SheetPoint? note = null;
        void PlaceNote()
        {
            if (request.NoteWidth <= 0)
                return;
            var (topLeft, free) = NotePlace(sheet, request.NoteWidth, request.NoteHeight, occupied);
            note = topLeft;
            if (!free)
                crowded.Add("技术要求");
            occupied.Add(new SheetRect(topLeft.X, topLeft.Y - request.NoteHeight, topLeft.X + request.NoteWidth, topLeft.Y));
        }

        if (noteFirst)
            PlaceNote();

        // 轴测图：离「视图组右边那一片的中间、主视图那一行」最近的空地（见 IsoSpot）。没有轴测图（宽为 0）就不放。
        SheetPoint? iso = null;
        var shrink = 1.0;
        if (request.Iso.Width > 0)
        {
            var target = new SheetPoint((occupied.Max(r => r.Right) + frame.Right) / 2, mainCenter.Y);
            (var center, shrink, var free) = IsoSpot(sheet, occupied, request.Iso, factors, target);
            iso = center;
            if (!free)
                crowded.Add("轴测图");
            occupied.Add(SheetRect.Around(center, request.Iso.Width * shrink, request.Iso.Height * shrink));
        }

        if (!noteFirst)
            PlaceNote();

        var crowding = crowded.Count == 0 ? string.Empty : $"{string.Join("、", crowded)}找不到完全空的地方，放在了压得最少处";
        return new LayoutResult(true, mainCenter, centers, iso, shrink, note, crowding);
    }

    /// <summary>
    /// 轴测图放哪：原比例、再往下几档（<paramref name="factors"/>，相对图纸比例）依次找空地，离 <paramref name="target"/> 最近；
    /// 都找不到就用最小那档放到压得最少的地方（轴测图只是看个样子，不为它换大图幅），<c>Free</c> 为 false。
    /// </summary>
    public static (SheetPoint Center, double Shrink, bool Free) IsoSpot(SheetSpace sheet, IReadOnlyList<SheetRect> occupied, ViewBox iso,
        IReadOnlyList<double> factors, SheetPoint target)
    {
        var blocked = sheet.KeepOuts.Concat(occupied).ToList();
        foreach (var factor in factors)
            if (FreeSpot(sheet.Frame, iso.Width * factor, iso.Height * factor, blocked, Gap, target) is { } spot)
                return (spot, factor, true);
        var shrink = factors[^1];
        return (LeastCrowded(sheet.Frame, iso.Width * shrink, iso.Height * shrink, sheet.KeepOuts, occupied, target), shrink, false);
    }

    /// <summary>
    /// 技术要求放哪（返回左上角）：只许标题栏正上方或标题栏左侧（1.13.0，用户定硬性要求，见 <see cref="TechNotePlanner"/>），先上方后左侧；
    /// 两处都压着就取压得最少的那处，<c>Free</c> 为 false（真放的时候「避障」开着会挪视图让开）。
    /// </summary>
    public static (SheetPoint TopLeft, bool Free) NotePlace(SheetSpace sheet, double width, double height, IReadOnlyList<SheetRect> occupied)
    {
        if (NoteSpot(sheet, width, height, occupied) is { } spot)
            return (spot, true);
        var rects = new[] { NoteSlot.AboveTitle, NoteSlot.LeftOfTitle }
            .Select(slot => TechNotePlanner.Spot(sheet, slot, width, height) ?? TechNotePlanner.Anchor(sheet, slot, width, height))
            .ToList();
        var best = rects.OrderBy(rect => occupied.Sum(other => Overlap(rect, other)) + 4 * sheet.KeepOuts.Sum(other => Overlap(rect, other))).First();
        return (new SheetPoint(best.Left, best.Top), false);
    }

    /// <summary>
    /// 技术要求的默认位置（「避障」关，1.11.0）：标题栏正上方靠右，不看压不压别的（返回左上角）。
    /// </summary>
    public static SheetPoint NoteDefault(SheetSpace sheet, double width, double height)
    {
        var anchor = TechNotePlanner.Anchor(sheet, NoteSlot.AboveTitle, width, height);
        return new SheetPoint(anchor.Left, anchor.Top);
    }

    /// <summary>投影视图往外让一步挪多远（图纸 2.5 mm，与找空地的网格一样）。</summary>
    public const double AwayStep = 0.0025;

    /// <summary>
    /// 投影视图让开（「避障」开，1.11.0）：贴着主视图的位置 <paramref name="start"/> 连同标注空间压到 <paramref name="blocked"/>
    /// （别的视图连同尺寸空间、轴测图、图框里的注解、标题栏）就沿离开主视图的方向一步 <see cref="AwayStep"/> 往外挪，挪到不压为止。
    /// 本来不压返回 <paramref name="start"/>；挪到出了 <paramref name="frame"/> 还压返回 null（调用方原地不动、回执里说）。
    /// </summary>
    public static SheetPoint? AwayFrom(SheetPoint start, ViewSlot slot, ViewBox box, IReadOnlyList<SheetRect> blocked, SheetRect frame)
    {
        var (dx, dy) = slot switch
        {
            ViewSlot.Right => (1.0, 0.0),
            ViewSlot.Left => (-1.0, 0.0),
            ViewSlot.Below => (0.0, -1.0),
            _ => (0.0, 1.0),
        };
        for (var step = 0; step <= 400; step++)
        {
            var center = new SheetPoint(start.X + dx * step * AwayStep, start.Y + dy * step * AwayStep);
            var rects = Occupied(center, box);
            if (!rects.Any(rect => blocked.Any(rect.Overlaps)))
                return center;
            var whole = new SheetRect(rects.Min(r => r.Left), rects.Min(r => r.Bottom), rects.Max(r => r.Right), rects.Max(r => r.Top));
            if (step > 0 && !whole.Within(frame))
                return null;
        }

        return null;
    }

    /// <summary>
    /// 找不到空地时的退路：网格上每个位置算它与视图（含标注空间）、禁区重叠的面积（禁区算 4 倍），取最小的，一样小取离 <paramref name="target"/> 近的。
    /// </summary>
    internal static SheetPoint LeastCrowded(SheetRect frame, double width, double height, IReadOnlyList<SheetRect> keepOuts, IReadOnlyList<SheetRect> occupied, SheetPoint target)
    {
        const double Step = 0.0025;
        var best = new SheetPoint(frame.Center.X, frame.Center.Y);
        var bestCost = double.MaxValue;
        for (var cy = frame.Bottom + height / 2; cy <= frame.Top - height / 2 + 1e-9; cy += Step)
        {
            for (var cx = frame.Left + width / 2; cx <= frame.Right - width / 2 + 1e-9; cx += Step)
            {
                var rect = SheetRect.Around(new SheetPoint(cx, cy), width, height);
                var cost = occupied.Sum(other => Overlap(rect, other)) + 4 * keepOuts.Sum(other => Overlap(rect, other));
                cost += 1e-6 * Math.Sqrt(Math.Pow(cx - target.X, 2) + Math.Pow(cy - target.Y, 2));
                if (cost < bestCost)
                    (best, bestCost) = (new SheetPoint(cx, cy), cost);
            }
        }

        return best;
    }

    private static double Overlap(SheetRect a, SheetRect b)
        => Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left)) * Math.Max(0, Math.Min(a.Top, b.Top) - Math.Max(a.Bottom, b.Bottom));

    /// <summary>视图组挪位的步长与上限（图纸 2.5 mm、60 mm）。</summary>
    private const double ShiftStep = 0.0025;
    private const double MaxShift = 0.060;

    /// <summary>总共挪 <paramref name="total"/> 的几种挪法：先只往下、再只往右、再两边分。</summary>
    private static IEnumerable<(double Dx, double Dy)> Shifts(double total)
    {
        yield return (0, total);
        yield return (total, 0);
        for (var dx = ShiftStep; dx < total - 1e-9; dx += ShiftStep)
            yield return (dx, total - dx);
    }

    /// <summary>
    /// 视图组放不下的原因；放得下为空串。视图右边、下边那条 <see cref="Gap"/> 只为隔开下一个视图，可以压到图框线上
    /// （视图外框本身就比零件多出约 6 mm 留白，1.10.0：限位块前主视图加下视图在 A4 上差的正是这 2 mm，用户手工图的下视图外框就贴着图框）。
    /// </summary>
    private static string BlockProblem(SheetSpace sheet, IReadOnlyList<SheetRect> occupied)
        => occupied.Any(rect => !rect.Within(new SheetRect(sheet.Frame.Left, sheet.Frame.Bottom - Gap, sheet.Frame.Right + Gap, sheet.Frame.Top)))
            ? "视图连同标注空间超出图框"
            : occupied.Any(rect => sheet.KeepOuts.Any(rect.Overlaps)) ? "视图压到标题栏等图框内容"
            : string.Empty;

    /// <summary>
    /// 主视图连同左右、上下投影视图从 (图框左上角 + 右移 <paramref name="dx"/>、下移 <paramref name="dy"/>) 排起：
    /// 横向 左视图 | 主视图（上下视图与它同列）| 右视图，竖向 上视图 / 主视图（左右视图同行）/ 下视图，各自左边、上边留标注。
    /// </summary>
    private static (SheetPoint Main, Dictionary<ViewSlot, SheetPoint> Centers, List<SheetRect> Occupied) Block(SheetRect frame, LayoutRequest request, double dx, double dy)
    {
        request.Sides.TryGetValue(ViewSlot.Left, out var left);
        request.Sides.TryGetValue(ViewSlot.Right, out var right);
        request.Sides.TryGetValue(ViewSlot.Above, out var above);
        request.Sides.TryGetValue(ViewSlot.Below, out var below);
        var main = request.Main;

        var x = frame.Left + Gap + dx;
        double leftX = 0, rightX = 0;
        if (left is not null)
        {
            x += left.MarginLeft;
            leftX = x + left.Width / 2;
            x += left.Width + left.MarginRight + Gap;
        }

        x += Math.Max(main.MarginLeft, Math.Max(above?.MarginLeft ?? 0, below?.MarginLeft ?? 0));
        var mainX = x + main.Width / 2;
        x += main.Width + Math.Max(main.MarginRight, Math.Max(above?.MarginRight ?? 0, below?.MarginRight ?? 0)) + Gap;
        if (right is not null)
        {
            x += right.MarginLeft;
            rightX = x + right.Width / 2;
        }

        var y = frame.Top - Gap - dy;
        double aboveY = 0, belowY = 0;
        if (above is not null)
        {
            y -= above.MarginTop;
            aboveY = y - above.Height / 2;
            y -= above.Height + above.MarginBottom + Gap;
        }

        y -= Math.Max(main.MarginTop, Math.Max(left?.MarginTop ?? 0, right?.MarginTop ?? 0));
        var mainY = y - main.Height / 2;
        y -= main.Height + Math.Max(main.MarginBottom, Math.Max(left?.MarginBottom ?? 0, right?.MarginBottom ?? 0)) + Gap;
        if (below is not null)
        {
            y -= below.MarginTop;
            belowY = y - below.Height / 2;
        }

        var mainCenter = new SheetPoint(mainX, mainY);
        var centers = new Dictionary<ViewSlot, SheetPoint>();
        var occupied = new List<SheetRect>(Occupied(mainCenter, main));
        void Add(ViewSlot slot, ViewBox? box, double cx, double cy)
        {
            if (box is null)
                return;
            centers[slot] = new SheetPoint(cx, cy);
            occupied.AddRange(Occupied(centers[slot], box));
        }

        Add(ViewSlot.Left, left, leftX, mainY);
        Add(ViewSlot.Right, right, rightX, mainY);
        Add(ViewSlot.Above, above, mainX, aboveY);
        Add(ViewSlot.Below, below, mainX, belowY);
        return (mainCenter, centers, occupied);
    }

    /// <summary>
    /// 视图连同它的标注空间占的地方：视图本身（右边、下边留一点），左边一条（竖直尺寸）、上边一条（水平尺寸）。
    /// 左上角那一块尺寸用不到，不算——左上角图号框常落在那里（首轮真机按一整块算，永远压到图号框、退到 1:100）。
    /// </summary>
    internal static IReadOnlyList<SheetRect> Occupied(SheetPoint center, ViewBox box)
    {
        var view = new SheetRect(center.X - box.Width / 2, center.Y - box.Height / 2 - Gap, center.X + box.Width / 2 + Gap, center.Y + box.Height / 2);
        var occupied = new List<SheetRect>
        {
            view,
            new SheetRect(view.Left - box.MarginLeft, view.Bottom, view.Left, view.Top),
            new SheetRect(view.Left, view.Top, view.Right, view.Top + box.MarginTop),
            new SheetRect(view.Right, view.Bottom, view.Right + box.MarginRight, view.Top),
            new SheetRect(view.Left, view.Bottom - box.MarginBottom, view.Right, view.Bottom),
        };
        if (box.AnnotationBounds is not null)
            occupied.AddRange(box.AnnotationBounds.Select(r => new SheetRect(r.Left + center.X, r.Bottom + center.Y, r.Right + center.X, r.Top + center.Y)));
        return occupied.Where(r => r.Width > 1e-9 && r.Height > 1e-9).ToList();
    }

    /// <summary>网格找空地（2.5 mm 一格）：放得下且离 <paramref name="target"/> 最近的中心点；没有返回 null。</summary>
    internal static SheetPoint? FreeSpot(SheetRect frame, double width, double height, IReadOnlyList<SheetRect> blocked, double clearance, SheetPoint target)
    {
        const double Step = 0.0025;
        SheetPoint? best = null;
        var bestDistance = double.MaxValue;
        for (var cy = frame.Bottom + height / 2 + clearance; cy <= frame.Top - height / 2 - clearance + 1e-9; cy += Step)
        {
            for (var cx = frame.Left + width / 2 + clearance; cx <= frame.Right - width / 2 - clearance + 1e-9; cx += Step)
            {
                var rect = SheetRect.Around(new SheetPoint(cx, cy), width, height).Inflate(clearance);
                if (blocked.Any(rect.Overlaps))
                    continue;
                var distance = Math.Pow(cx - target.X, 2) + Math.Pow(cy - target.Y, 2);
                if (distance < bestDistance)
                    (best, bestDistance) = (new SheetPoint(cx, cy), distance);
            }
        }

        return best;
    }

    /// <summary>技术要求放哪（返回左上角，注释以左上角定位）：标题栏正上方、标题栏左侧，哪处空放哪处（1.13.0 起别处不放）。</summary>
    private static SheetPoint? NoteSpot(SheetSpace sheet, double width, double height, IReadOnlyList<SheetRect> occupied)
    {
        foreach (var slot in new[] { NoteSlot.AboveTitle, NoteSlot.LeftOfTitle })
            if (TechNotePlanner.Spot(sheet, slot, width, height) is { } rect && !occupied.Any(rect.Inflate(Gap / 2).Overlaps))
                return new SheetPoint(rect.Left, rect.Top);
        return null;
    }

    /// <summary>主视图长边在图纸上至少这么长（图纸 40 mm）。</summary>
    public const double MinMainSide = 0.040;

    /// <summary>主视图里最小的孔在图纸上至少这么大（直径 0.7 mm；用户底层1 的 M5 底孔 1:5 时 0.84 mm、外壳 1:10 时 0.7 mm 都接受）。</summary>
    public const double MinHoleOnPaper = 0.0007;

    /// <summary>主视图每个孔、圆弧面至少分到这么大的图面（300 mm²），再挤标注就叠成一团（夹爪 A4 1:3 只有 295 mm²，用户用了 A3 1:2；底层1 A3 1:5 是 303 mm²）。</summary>
    public const double MinAreaPerFeature = 300e-6;

    /// <summary>图幅长边到这么宽（米）算 A2 及以上。</summary>
    private const double LargeSheetLongSide = 0.590;

    /// <summary>够不上「看得清」但还不至于要上 A2 的下限：每个特征 100 mm²、最小孔 0.5 mm。</summary>
    private const double ReadableAreaPerFeature = 100e-6;
    private const double ReadableHole = 0.0005;
}
