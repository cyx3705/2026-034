using System.Globalization;

namespace HistoryStrenua;

/// <summary>
/// SolidWorks 的一个标准命名视图：名字（中文版、英文版各一个）与它的朝向。
/// </summary>
/// <param name="Key">稳定标识，如 <c>front</c>。</param>
/// <param name="Names">依次试的视图名（<c>CreateDrawViewFromModelView3</c> 要与模型视图名完全一致，中文版是「*前视」）。</param>
/// <param name="Normal">朝看图人的方向（图纸 +Z 在模型里的方向）：这一面的孔在视图里是圆。</param>
/// <param name="Right">图纸 +X 在模型里的方向。</param>
/// <param name="Up">图纸 +Y 在模型里的方向。</param>
internal sealed record StandardView(string Key, IReadOnlyList<string> Names, ModelDirection Normal, ModelDirection Right, ModelDirection Up)
{
    /// <summary>六个标准视图（真机读回：前视 Normal=+Z、后视 −Z 且 Right=−X、上视 +Y 且 Up=−Z、左视 −X）。</summary>
    public static IReadOnlyList<StandardView> All { get; } =
    [
        new("front", ["*前视", "*Front"], new(0, 0, 1), new(1, 0, 0), new(0, 1, 0)),
        new("back", ["*后视", "*Back"], new(0, 0, -1), new(-1, 0, 0), new(0, 1, 0)),
        new("top", ["*上视", "*Top"], new(0, 1, 0), new(1, 0, 0), new(0, 0, -1)),
        new("bottom", ["*下视", "*Bottom"], new(0, -1, 0), new(1, 0, 0), new(0, 0, 1)),
        new("left", ["*左视", "*Left"], new(-1, 0, 0), new(0, 0, 1), new(0, 1, 0)),
        new("right", ["*右视", "*Right"], new(1, 0, 0), new(0, 0, -1), new(0, 1, 0)),
    ];

    public static StandardView Of(string key) => All.Single(view => view.Key == key);
}

/// <summary>投影视图摆在主视图的哪一边。</summary>
internal enum ViewSlot
{
    Right,
    Left,
    Below,
    Above,
}

/// <summary>主视图选择的结论与理由（回执里写）。</summary>
internal sealed record MainViewChoice(StandardView View, string Reason);

/// <summary>
/// 「新建工程图」（1.9.0）的纯几何部分：主视图朝哪、要哪几个投影视图、用哪个模板与比例、视图与技术要求摆在哪。
/// 不碰 SolidWorks，能离线测。
/// </summary>
/// <remarks>
/// <para>依据是用户 2026-025 台面2机器的 15 张手工图：主视图都是标准命名视图、不转角度；主视图看的是孔和圆弧最多的那一面
/// （板件看板面，L 形件看截面轮廓）；投影视图只加看得到侧面孔、看得到厚度的那几个；每张都有一个从主视图斜投影出来的轴测图；
/// 技术要求放在图纸里；标题栏由模板的属性链接带出。</para>
/// </remarks>
internal static class DrawingPlanner
{
    /// <summary>方向分量的绝对值到这个值才算沿这根轴。</summary>
    private const double AxisCosine = 0.999;

    /// <summary>圆弧面半径到这个值以上才算形状特征（R1 是「未注圆角R1」的修边，不影响挑视图）。</summary>
    public const double ArcMinRadius = 0.00105;

    /// <summary>一根轴上的统计：沿它的孔、窗口（平面上非圆的内环）、圆弧面（圆角、腰型孔端头、凸台），侧着看成线的平面（截面轮廓的边）。</summary>
    internal sealed record AxisStats(ModelDirection Axis, string Name, int Holes, int Windows, int Arcs, int Outline, double Thickness, int Score);

    /// <summary>
    /// 三根轴的统计与得分：孔、窗口、圆弧面各算 2 分（沿轴看的视图里它们成圆、成框、成弧，是要标的东西）；回转体的轴向记 −1（不看端面）。
    /// 侧着看成线的平面（轮廓边）只统计、不计分：首版给它加分，限位块、外壳这类带台阶的块状件主视图就从孔最多的那面被拉走了
    /// （用户手工图选的都是孔最多的那面）。
    /// </summary>
    public static IReadOnlyList<AxisStats> Stats(PartGeometry part)
    {
        var revolved = RevolvedAxis(part);
        return new[] { new ModelDirection(1, 0, 0), new ModelDirection(0, 1, 0), new ModelDirection(0, 0, 1) }
            .Select(axis =>
            {
                var holes = part.Holes.Count(hole => Along(hole.Axis, axis));
                var arcs = part.Cylinders.Count(c => !(c.Concave && c.Full) && c.Radius >= ArcMinRadius && Along(c.Axis, axis));
                var windows = part.Windows.Count(normal => Along(normal, axis));
                var outline = part.Planes.Count(normal => Math.Abs(normal.Dot(axis)) < 1e-3);
                var score = revolved is { } r && Along(r, axis) ? -1 : 2 * holes + 2 * windows + 2 * arcs;
                return new AxisStats(axis, AxisName(axis), holes, windows, arcs, outline, part.Box.Extent(axis), score);
            })
            .ToList();
    }

    /// <summary>
    /// 挑主视图：三根轴里得分（见 <see cref="Stats"/>）最高的那根；平分时取零件在它上面最薄的（板件的厚度方向、L 形件的拉伸方向）。
    /// 正反两面看哪一面：孔口朝哪面多就看哪面（沉头孔只算沉头那面），一样多取正向（前视 / 上视 / 右视）。
    /// </summary>
    /// <remarks>
    /// 对照用户 15 张手工图：板件看板面（孔最多）；块状件看孔最多的那面；连接件（L 形，两条腿侧面各 2 个孔、截面上 3 个圆角）看截面。
    /// </remarks>
    public static MainViewChoice MainView(PartGeometry part)
    {
        var stats = Stats(part);
        var best = stats.OrderByDescending(item => item.Score).ThenBy(item => item.Thickness).First();
        var holes = part.Holes;
        var plus = holes.Count(hole => Along(hole.Axis, best.Axis) && hole.Openings.Any(o => o.Dot(best.Axis) > AxisCosine));
        var minus = holes.Count(hole => Along(hole.Axis, best.Axis) && hole.Openings.Any(o => o.Dot(best.Axis) < -AxisCosine));
        var normal = minus > plus ? Negate(best.Axis) : best.Axis;
        var view = StandardView.All.First(item => item.Normal.Dot(normal) > AxisCosine);

        var reason = $"沿 {best.Name} 看：孔 {best.Holes} 个、" + (best.Windows > 0 ? $"窗口 {best.Windows} 个、" : string.Empty) + $"圆弧面 {best.Arcs} 张、轮廓边 {best.Outline} 条，得分最高"
            + $"（{string.Join(" / ", stats.Select(item => $"{item.Name} {item.Score}"))}）";
        if (best.Score <= 0)
            reason = $"没有孔和圆弧面，取零件最薄的方向（{best.Name}）";
        if (stats.Any(item => item.Score < 0))
            reason += "；回转体，不看端面";
        if (plus != minus)
            reason += $"；孔口朝这一面的多（{Math.Max(plus, minus)} 比 {Math.Min(plus, minus)}）";
        return new MainViewChoice(view, reason);
    }

    /// <summary>回转体的轴：最大的外圆柱面（凸、整圈）直径到另两个方向的跨度的 95% 以上。</summary>
    private static ModelDirection? RevolvedAxis(PartGeometry part)
    {
        foreach (var cylinder in part.Cylinders.Where(c => !c.Concave && c.Full).OrderByDescending(c => c.Radius))
        {
            var axis = Axial(cylinder.Axis);
            if (axis is null)
                continue;
            var others = new[] { new ModelDirection(1, 0, 0), new ModelDirection(0, 1, 0), new ModelDirection(0, 0, 1) }
                .Where(other => Math.Abs(other.Dot(axis.Value)) < 0.5)
                .Select(other => part.Box.Extent(other))
                .ToList();
            return others.All(extent => 2 * cylinder.Radius >= 0.95 * extent) ? axis : null;
        }

        return null;
    }

    /// <summary>方向沿某根坐标轴时返回那根轴（正向），否则 null。</summary>
    internal static ModelDirection? Axial(ModelDirection direction)
    {
        if (Math.Abs(direction.X) >= AxisCosine) return new ModelDirection(1, 0, 0);
        if (Math.Abs(direction.Y) >= AxisCosine) return new ModelDirection(0, 1, 0);
        if (Math.Abs(direction.Z) >= AxisCosine) return new ModelDirection(0, 0, 1);
        return null;
    }

    /// <summary>两个方向平行（同向或反向）。</summary>
    internal static bool Along(ModelDirection direction, ModelDirection axis) => Math.Abs(direction.Dot(axis)) >= AxisCosine;

    internal static ModelDirection Negate(ModelDirection d) => new(-d.X, -d.Y, -d.Z);

    private static string AxisName(ModelDirection axis)
        => Math.Abs(axis.X) > 0.5 ? "X" : Math.Abs(axis.Y) > 0.5 ? "Y" : "Z";

    /// <summary>
    /// 要哪几个投影视图（主视图朝向用真机读回的 <paramref name="frame"/>）：
    /// <list type="number">
    /// <item>沿图纸 X 方向有孔就加一个左 / 右视图，沿图纸 Y 方向有孔就加一个上 / 下视图；摆哪边看孔口朝哪边多——第一角投影（国标）里
    /// 摆右边的视图看的是零件左侧、摆下边的看的是零件上面，第三角投影反过来。</item>
    /// <item>两个方向都没有孔、但有圆弧面（圆角、凸台）：只加一个，加在圆弧面多的那个方向（两个方向都有时可换到另一个方向）。</item>
    /// <item>都没有：只加一个看厚度的——主视图宽就摆下边、高就摆右边，也可换边。</item>
    /// </list>
    /// 「可换边」的由选图幅时两种摆法都试（<see cref="ChooseSheet"/>），哪种放得下用哪种。
    /// </summary>
    /// <remarks>用户手工图：外壳侧面只有圆角，只加了一个右视图；移动安装版5 只看厚度。首版圆弧面也逐个方向加视图，外壳多出一个下视图、A4 放不下。</remarks>
    public static IReadOnlyList<SideView> SideViews(PartGeometry part, ViewFrame frame, bool firstAngle)
    {
        var result = new List<SideView>();
        var r = frame.SheetX;
        var u = frame.SheetY;
        var holes = part.Holes;
        var alongX = Count(part, holes, r);
        var alongY = Count(part, holes, u);

        // 沿图纸 X：摆右边的视图在第一角投影里看 −X 那一侧。窗口两面都看得到，不影响摆哪边。
        if (alongX.Holes + alongX.Windows > 0)
        {
            var rightShows = firstAngle ? alongX.Minus : alongX.Plus;
            var leftShows = firstAngle ? alongX.Plus : alongX.Minus;
            result.Add(new SideView(rightShows >= leftShows ? ViewSlot.Right : ViewSlot.Left, "侧面沿图纸横向有" + Features(alongX.Holes, alongX.Windows)));
        }

        // 沿图纸 Y：摆下边的视图在第一角投影里看 +Y 那一侧（零件上面）。
        if (alongY.Holes + alongY.Windows > 0)
        {
            var belowShows = firstAngle ? alongY.Plus : alongY.Minus;
            var aboveShows = firstAngle ? alongY.Minus : alongY.Plus;
            result.Add(new SideView(belowShows >= aboveShows ? ViewSlot.Below : ViewSlot.Above, "侧面沿图纸竖向有" + Features(alongY.Holes, alongY.Windows)));
        }

        if (result.Count > 0)
            return result;

        var wide = part.Box.Extent(r) >= part.Box.Extent(u);
        if (alongX.Arcs > 0 || alongY.Arcs > 0)
        {
            var horizontal = alongX.Arcs > alongY.Arcs || (alongX.Arcs == alongY.Arcs && !wide);
            var arcs = horizontal ? alongX.Arcs : alongY.Arcs;
            var both = alongX.Arcs > 0 && alongY.Arcs > 0;
            result.Add(horizontal
                ? new SideView(ViewSlot.Right, $"侧面沿图纸横向有圆弧面 {arcs} 张", both ? ViewSlot.Below : null)
                : new SideView(ViewSlot.Below, $"侧面沿图纸竖向有圆弧面 {arcs} 张", both ? ViewSlot.Right : null));
            return result;
        }

        result.Add(wide ? new SideView(ViewSlot.Below, "看厚度", ViewSlot.Right) : new SideView(ViewSlot.Right, "看厚度", ViewSlot.Below));
        return result;
    }

    /// <summary>投影视图的几种摆法：只有「可换边」的那个有两种。</summary>
    public static IEnumerable<IReadOnlyList<ViewSlot>> Arrangements(IReadOnlyList<SideView> sides)
    {
        yield return sides.Select(side => side.Slot).ToList();
        if (sides.Any(side => side.Alternative is not null))
            yield return sides.Select(side => side.Alternative ?? side.Slot).ToList();
    }

    private static string Features(int holes, int windows)
        => string.Join("、", new[] { holes > 0 ? $"孔 {holes} 个" : null, windows > 0 ? $"窗口 {windows} 个" : null }.Where(text => text is not null));

    /// <summary>沿某方向的孔、窗口、圆弧面（R1 以上），以及孔口朝正 / 负向各几个。</summary>
    private static (int Holes, int Windows, int Arcs, int Plus, int Minus, int Total) Count(PartGeometry part, IReadOnlyList<PartHole> holes, ModelDirection direction)
    {
        var along = holes.Where(hole => Along(hole.Axis, direction)).ToList();
        var arcs = part.Cylinders.Count(cylinder => !(cylinder.Concave && cylinder.Full) && cylinder.Radius >= ArcMinRadius && Along(cylinder.Axis, direction));
        var plus = along.Count(hole => hole.Openings.Any(o => o.Dot(direction) > AxisCosine));
        var minus = along.Count(hole => hole.Openings.Any(o => o.Dot(direction) < -AxisCosine));
        var windows = part.Windows.Count(normal => Along(normal, direction));
        return (along.Count, windows, arcs, plus, minus, along.Count + windows + arcs);
    }

    /// <summary>
    /// 主视图斜投影出来的轴测图朝向（真机：摆在主视图右上方，视线 = 主视图视线 + 图纸 X + 图纸 Y 归一，图纸 Y 取主视图 Y 在轴测平面上的投影）。
    /// 只用来估轴测图有多大。
    /// </summary>
    public static (ModelDirection Right, ModelDirection Up) IsoAxes(ModelDirection normal, ModelDirection right, ModelDirection upward)
    {
        var look = new ModelDirection(normal.X + right.X + upward.X, normal.Y + right.Y + upward.Y, normal.Z + right.Z + upward.Z).Normalized();
        var dot = upward.Dot(look);
        var up = new ModelDirection(upward.X - dot * look.X, upward.Y - dot * look.Y, upward.Z - dot * look.Z).Normalized();
        var isoRight = PartHole.Cross(up, look).Normalized();
        return (isoRight, up);
    }

    /// <summary>SolidWorks 视图外框比几何每边多出的留白（真机 A3 1:5、A4 1:2 读得约 5.9 mm）。</summary>
    public const double ViewPad = 0.006;

    /// <summary>视图之间、视图与图框之间至少空这么多（图纸 4 mm）。</summary>
    public const double Gap = 0.004;

    /// <summary>
    /// 一个视图左边、上边要给尺寸留多宽（孔位尺寸与外轮廓尺寸都排在左边和上边）：第一层 8 mm、每种孔一层 6 mm、
    /// 外轮廓再一层，最少 16 mm、最多 80 mm（底层1 九种孔真机叠了约 65 mm，首版封顶 50 mm 时尺寸顶到了图框）。
    /// 尺寸链模式同样留：坐标尺寸只占 26 mm，但孔标注往左上引出、带 H7 后更宽，首版尺寸链模式只留 26 mm 时
    /// 移动安装版2 靠左的销孔标注伸出了图框（<paramref name="chain"/> 因此不再影响结果，留作调用方说明用途）。
    /// </summary>
    public static double AnnotationMargin(int holeKinds, bool chain)
        => Math.Clamp(0.010 + 0.006 * holeKinds + 0.006, chain ? 0.026 : 0.016, 0.080);

    /// <summary>沿某个视线方向的孔有几种（这些孔在那个视图里是圆，各要一层位置尺寸）。</summary>
    public static int HoleKinds(PartGeometry part, ModelDirection normal)
        => part.Holes.Where(hole => Along(hole.Axis, normal)).Select(hole => hole.Kind).Distinct(StringComparer.Ordinal).Count();

    /// <summary>技术要求注释的估计大小（用户标准 8 条、字高 3.5 实测约 100 × 41 mm）。建出来后用实际大小重排。</summary>
    public const double DefaultNoteWidth = 0.101;

    public const double DefaultNoteHeight = 0.042;

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
            var margin = AnnotationMargin(HoleKinds(part, normal), chain);
            sideBoxes[slot] = horizontal ? new ViewBox(d, h, margin, margin) : new ViewBox(w, d, margin, margin);
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
    /// 放不下就缩成一半；技术要求先试标题栏正上方、再试左下角、再找任何空地。不碰图框外、标题栏等禁区。
    /// </summary>
    public static LayoutResult Layout(SheetSpace sheet, LayoutRequest request)
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
        var mainY = mainCenter.Y;
        if (problem.Length > 0)
            return new LayoutResult(false, mainCenter, centers, null, 1, null, problem);

        // 轴测图：原比例、再往下两档比例（IsoFactors）依次找空地，离「视图组右边那一片的中间、主视图那一行」最近；
        // 都找不到就用最小那档放到压得最少的地方（轴测图只是看个样子，不为它换大图幅）。
        var factors = request.IsoFactors ?? [1.0, 0.5];
        var target = new SheetPoint((occupied.Max(r => r.Right) + frame.Right) / 2, mainY);
        SheetPoint? iso = null;
        var shrink = factors[^1];
        foreach (var factor in factors)
        {
            iso = FreeSpot(frame, request.Iso.Width * factor, request.Iso.Height * factor, sheet.KeepOuts.Concat(occupied).ToList(), Gap, target);
            if (iso is not null)
            {
                shrink = factor;
                break;
            }
        }

        var crowded = new List<string>();
        if (iso is null)
        {
            iso = LeastCrowded(frame, request.Iso.Width * shrink, request.Iso.Height * shrink, sheet.KeepOuts, occupied, target);
            crowded.Add("轴测图");
        }

        occupied.Add(SheetRect.Around(iso.Value, request.Iso.Width * shrink, request.Iso.Height * shrink));

        // 技术要求：标题栏正上方靠右 → 左下角 → 任何空地（靠下靠右）→ 都没有就放到压得最少的地方（靠右下）。
        var note = NoteSpot(sheet, request.NoteWidth, request.NoteHeight, occupied);
        if (note is null && request.NoteWidth > 0)
        {
            var center = LeastCrowded(frame, request.NoteWidth, request.NoteHeight, sheet.KeepOuts, occupied, new SheetPoint(frame.Right, frame.Bottom));
            note = new SheetPoint(center.X - request.NoteWidth / 2, center.Y + request.NoteHeight / 2);
            crowded.Add("技术要求");
        }

        var crowding = crowded.Count == 0 ? string.Empty : $"{string.Join("、", crowded)}找不到完全空的地方，放在了压得最少处";
        return new LayoutResult(true, mainCenter, centers, iso, shrink, note, crowding);
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

    /// <summary>视图组放不下的原因；放得下为空串。</summary>
    private static string BlockProblem(SheetSpace sheet, IReadOnlyList<SheetRect> occupied)
        => occupied.Any(rect => !rect.Within(sheet.Frame)) ? "视图连同标注空间超出图框"
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
            x += left.Width + Gap;
        }

        x += Math.Max(main.MarginLeft, Math.Max(above?.MarginLeft ?? 0, below?.MarginLeft ?? 0));
        var mainX = x + main.Width / 2;
        x += main.Width + Gap;
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
            y -= above.Height + Gap;
        }

        y -= Math.Max(main.MarginTop, Math.Max(left?.MarginTop ?? 0, right?.MarginTop ?? 0));
        var mainY = y - main.Height / 2;
        y -= main.Height + Gap;
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
        return
        [
            view,
            new SheetRect(view.Left - box.MarginLeft, view.Bottom, view.Left, view.Top),
            new SheetRect(view.Left, view.Top, view.Right, view.Top + box.MarginTop),
        ];
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

    /// <summary>技术要求放哪（返回左上角，注释以左上角定位）：标题栏正上方靠右、左下角、再找空地（越靠下越靠右越好）。</summary>
    private static SheetPoint? NoteSpot(SheetSpace sheet, double width, double height, IReadOnlyList<SheetRect> occupied)
    {
        var frame = sheet.Frame;
        var blocked = sheet.KeepOuts.Concat(occupied).ToList();
        var title = sheet.TitleBlock;
        SheetRect[] preferred =
        [
            new(title.Right - Gap - width, title.Top + Gap, title.Right - Gap, title.Top + Gap + height),
            new(frame.Left + 2 * Gap, frame.Bottom + 2 * Gap, frame.Left + 2 * Gap + width, frame.Bottom + 2 * Gap + height),
        ];
        foreach (var rect in preferred)
            if (rect.Within(frame) && !blocked.Any(rect.Inflate(Gap / 2).Overlaps))
                return new SheetPoint(rect.Left, rect.Top);

        var spot = FreeSpot(frame, width, height, blocked, Gap / 2, new SheetPoint(frame.Right, frame.Bottom));
        return spot is { } c ? new SheetPoint(c.X - width / 2, c.Y + height / 2) : null;
    }

    /// <summary>主视图长边在图纸上至少这么长（图纸 40 mm）。</summary>
    public const double MinMainSide = 0.040;

    /// <summary>主视图里最小的孔在图纸上至少这么大（直径 0.7 mm；用户底层1 的 M5 底孔 1:5 时 0.84 mm、外壳 1:10 时 0.7 mm 都接受）。</summary>
    public const double MinHoleOnPaper = 0.0007;

    /// <summary>主视图每个孔、圆弧面至少分到这么大的图面（300 mm²），再挤标注就叠成一团（夹爪 A4 1:3 只有 295 mm²，用户用了 A3 1:2；底层1 A3 1:5 是 303 mm²）。</summary>
    public const double MinAreaPerFeature = 300e-6;

    /// <summary>
    /// 比例，从大到小：国标优先比例，加上用户用过的 1:3（移动安装板1，国标「允许选用」系列）；放大比例只给很小的零件
    /// （最大跨度 40 mm 以下 2:1，16 mm 以下 5:1）。
    /// </summary>
    public static IReadOnlyList<double> Scales(PartGeometry part)
    {
        var box = part.Box;
        var size = Math.Max(Math.Max(box.MaxX - box.MinX, box.MaxY - box.MinY), box.MaxZ - box.MinZ);
        var scales = new List<double>();
        if (size <= 0.016)
            scales.Add(5);
        if (size <= 0.040)
            scales.Add(2);
        scales.AddRange([1, 0.5, 1.0 / 3, 0.2, 0.1, 0.05, 0.02, 0.01]);
        return scales;
    }

    /// <summary>比例写法：「1:5」「2:1」。</summary>
    public static string ScaleText(double scale)
        => scale >= 1 ? $"{scale.ToString("0.##", CultureInfo.InvariantCulture)}:1" : $"1:{(1 / scale).ToString("0.##", CultureInfo.InvariantCulture)}";

    /// <summary>比例拆成 SolidWorks 要的两个整数（1:5 → 1, 5；2:1 → 2, 1）。</summary>
    public static (int Numerator, int Denominator) ScaleRatio(double scale)
        => scale >= 1 ? ((int)Math.Round(scale), 1) : (1, (int)Math.Round(1 / scale));

    /// <summary>
    /// 选模板与比例。<paramref name="templates"/> 按偏好排好（图幅从小到大）：每个模板取放得下的最大比例；
    /// 取第一个「看得清」的——主视图长边不短于 40 mm、主视图里每个孔与圆弧面至少分到 250 mm² 图面、最小的孔直径在图纸上不小于 0.7 mm；
    /// 都看不清就取比例最大的（一样大取图幅小的）。一个都放不下返回 null。
    /// </summary>
    /// <remarks>
    /// 对照用户 15 张手工图：孔多的板件（底层1 33 个孔）A3 1:5，A4 1:10 每个孔只分到 75 mm²；夹爪（6 孔 6 圆角）A3 1:2 而不是 A4 1:5；
    /// 零件小、特征少的（连接件、限位块）A4；大外壳 A4 1:10（孔在图纸上 0.7 mm 也接受）。
    /// </remarks>
    public static SheetChoice? ChooseSheet(PartGeometry part, StandardView main, IReadOnlyList<SideView> sides, IReadOnlyList<DrawingTemplate> templates, bool chain)
    {
        var mainHoles = part.Holes.Where(hole => Along(hole.Axis, main.Normal)).ToList();
        var mainArcs = part.Cylinders.Count(c => !(c.Concave && c.Full) && c.Radius >= ArcMinRadius && Along(c.Axis, main.Normal));
        var features = mainHoles.Count + mainArcs + part.Windows.Count(normal => Along(normal, main.Normal));
        var (w, h) = (part.Box.Extent(main.Right), part.Box.Extent(main.Up));
        var longSide = Math.Max(w, h);
        double? smallest = mainHoles.Count == 0 ? null : mainHoles.Min(hole => 2 * hole.Radius);

        var fits = new List<(DrawingTemplate Template, double Scale, LayoutResult Layout, bool Legible, IReadOnlyList<ViewSlot> Slots)>();
        foreach (var template in templates)
        {
            foreach (var scale in Scales(part))
            {
                var placed = Arrangements(sides)
                    .Select(slots => (Slots: slots, Layout: Layout(SheetSpace.Standard(template.Width, template.Height), Estimate(part, main, slots, scale, chain))))
                    .FirstOrDefault(item => item.Layout.Fits);
                if (placed.Layout is null)
                    continue;
                var legible = longSide * scale >= MinMainSide - 1e-9
                    && (features == 0 || w * h * scale * scale / features >= MinAreaPerFeature - 1e-12)
                    && (smallest is null || smallest * scale >= MinHoleOnPaper - 1e-9);
                fits.Add((template, scale, placed.Layout, legible, placed.Slots));
                break;
            }
        }

        if (fits.Count == 0)
            return null;
        var chosen = fits.FirstOrDefault(item => item.Legible);
        if (chosen.Template is not null)
            return new SheetChoice(chosen.Template, chosen.Scale, chosen.Layout, chosen.Slots,
                $"放得下且看得清的最小图幅（主视图长边 {longSide * chosen.Scale * 1000:0} mm"
                + (features > 0 ? $"，每个孔 / 圆弧 {w * h * chosen.Scale * chosen.Scale / features * 1e6:0} mm²" : string.Empty)
                + (smallest is { } s ? $"，最小孔 ⌀{s * chosen.Scale * 1000:0.#} mm" : string.Empty) + "）");

        chosen = fits.OrderByDescending(item => item.Scale).First();
        return new SheetChoice(chosen.Template, chosen.Scale, chosen.Layout, chosen.Slots, "哪个图幅都不够看清，取比例最大的");
    }
}

/// <summary>图纸上一个矩形（米）。</summary>
internal readonly record struct SheetRect(double Left, double Bottom, double Right, double Top)
{
    public double Width => Right - Left;

    public double Height => Top - Bottom;

    public SheetPoint Center => new((Left + Right) / 2, (Bottom + Top) / 2);

    /// <summary>两个矩形有公共面积（只碰边不算）。</summary>
    public bool Overlaps(SheetRect other) => Left < other.Right && other.Left < Right && Bottom < other.Top && other.Bottom < Top;

    /// <summary>整个在 <paramref name="outer"/> 里。</summary>
    public bool Within(SheetRect outer)
        => Left >= outer.Left - 1e-9 && Right <= outer.Right + 1e-9 && Bottom >= outer.Bottom - 1e-9 && Top <= outer.Top + 1e-9;

    public SheetRect Inflate(double distance) => new(Left - distance, Bottom - distance, Right + distance, Top + distance);

    public static SheetRect Around(SheetPoint center, double width, double height)
        => new(center.X - width / 2, center.Y - height / 2, center.X + width / 2, center.Y + height / 2);
}

/// <summary>一张图纸能放东西的地方：纸张大小、内框、内框里不能放的地方（第一个是标题栏）。</summary>
internal sealed record SheetSpace(double Width, double Height, SheetRect Frame, IReadOnlyList<SheetRect> KeepOuts)
{
    /// <summary>
    /// 用户「高悦精密」模板实测的国标图框（A4 横放与 A3 相同）：内框四边 5 mm；标题栏 180 × 44 在右下，修改栏 160 × 13 在右上，
    /// 图号框 41 × 11 在左上。选模板时只知道纸张大小，就按这个估；建出图纸后再加上读到的图纸注解（见 <c>DrawingCreate</c>）。
    /// </summary>
    public static SheetSpace Standard(double width, double height)
    {
        var frame = new SheetRect(0.005, 0.005, width - 0.005, height - 0.005);
        return new SheetSpace(width, height, frame,
        [
            new SheetRect(frame.Right - 0.180, frame.Bottom, frame.Right, frame.Bottom + 0.044),
            new SheetRect(frame.Right - 0.160, frame.Top - 0.013, frame.Right, frame.Top),
            new SheetRect(frame.Left, frame.Top - 0.011, frame.Left + 0.041, frame.Top),
        ]);
    }

    /// <summary>标题栏（禁区第一项）。</summary>
    public SheetRect TitleBlock => KeepOuts[0];

    /// <summary>再加几块禁区（标题栏仍是第一项）。</summary>
    public SheetSpace With(IEnumerable<SheetRect> more) => this with { KeepOuts = KeepOuts.Concat(more).ToList() };
}

/// <summary>一个视图在图纸上占多大：外框（含 SolidWorks 视图留白），以及左边、上边要留给尺寸的宽度。</summary>
internal sealed record ViewBox(double Width, double Height, double MarginLeft, double MarginTop);

/// <summary>排版要放的东西：主视图、各投影视图、轴测图（及它可以缩成原来的几分之几，依次试）、技术要求注释（宽、高）。</summary>
internal sealed record LayoutRequest(ViewBox Main, IReadOnlyDictionary<ViewSlot, ViewBox> Sides, ViewBox Iso, double NoteWidth, double NoteHeight,
    IReadOnlyList<double>? IsoFactors = null);

/// <summary>排版结果：各视图中心、轴测图缩成几分之几、技术要求左上角；放不下时 <see cref="Problem"/> 说哪里放不下。</summary>
internal sealed record LayoutResult(
    bool Fits,
    SheetPoint Main,
    IReadOnlyDictionary<ViewSlot, SheetPoint> Sides,
    SheetPoint? Iso,
    double IsoShrink,
    SheetPoint? NoteTopLeft,
    string Problem);

/// <summary>一个工程图模板文件与它的图幅（米，横放时宽 &gt; 高）。</summary>
internal sealed record DrawingTemplate(string Path, string Name, double Width, double Height)
{
    private static readonly (double Long, double Short)[] Sizes =
    [
        (1.189, 0.841), (0.841, 0.594), (0.594, 0.420), (0.420, 0.297), (0.297, 0.210),
    ];

    /// <summary>图幅名，如「A3」「A4 横」。</summary>
    public string SizeName
    {
        get
        {
            var index = Array.FindIndex(Sizes, size => Math.Abs(size.Long - Math.Max(Width, Height)) < 0.002);
            var name = index < 0 ? $"{Width * 1000:0}×{Height * 1000:0}" : "A" + index;
            return index == 4 ? name + (Width > Height ? " 横" : " 竖") : name;
        }
    }

    /// <summary>
    /// 从文件名认图幅：A0–A4；名字里有「横 / landscape」横放、「纵 / 竖 / portrait」竖放，不写时 A4 竖放、A3 以上横放。
    /// 认不出返回 null。
    /// </summary>
    public static DrawingTemplate? FromFile(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var match = System.Text.RegularExpressions.Regex.Match(name, "[Aa]([0-4])(?![0-9])");
        if (!match.Success)
            return null;
        var size = Sizes[match.Groups[1].Value[0] - '0'];
        var lower = name.ToLowerInvariant();
        var landscape = lower.Contains('横') || lower.Contains("landscape") ? true
            : lower.Contains('纵') || lower.Contains('竖') || lower.Contains("portrait") ? false
            : size.Long < 0.3 ? false : true;
        return landscape
            ? new DrawingTemplate(path, name, size.Long, size.Short)
            : new DrawingTemplate(path, name, size.Short, size.Long);
    }

    /// <summary>
    /// 候选模板排序：零件图优先用名字带「零件」的（都没有才用别的，带「装配」的最后）；有横放的就只用横放的（用户 15 张手工图全是横放）；
    /// 图幅从小到大，同图幅取名字短的那个。
    /// </summary>
    public static IReadOnlyList<DrawingTemplate> Rank(IEnumerable<DrawingTemplate> templates)
    {
        var list = templates.ToList();
        var parts = list.Where(t => t.Name.Contains("零件") || t.Name.Contains("part", StringComparison.OrdinalIgnoreCase)).ToList();
        if (parts.Count == 0)
            parts = list.Where(t => !t.Name.Contains("装配") && !t.Name.Contains("assem", StringComparison.OrdinalIgnoreCase)).ToList();
        if (parts.Count == 0)
            parts = list;
        if (parts.Any(t => t.Width > t.Height))
            parts = parts.Where(t => t.Width > t.Height).ToList();
        return parts
            .GroupBy(t => (Math.Round(t.Width * t.Height, 4), t.Width > t.Height))
            .Select(group => group.OrderBy(t => t.Name.Length).First())
            .OrderBy(t => t.Width * t.Height)
            .ThenByDescending(t => t.Width > t.Height)
            .ToList();
    }
}

/// <summary>选定的模板、比例、投影视图摆法与按估计排出的版面。</summary>
internal sealed record SheetChoice(DrawingTemplate Template, double Scale, LayoutResult Layout, IReadOnlyList<ViewSlot> Slots, string Reason);

/// <summary>一个投影视图：摆在主视图哪边、为什么要它；<see cref="Alternative"/> 不为空时也可以摆到那一边（看厚度、只看圆弧的视图）。</summary>
internal sealed record SideView(ViewSlot Slot, string Reason, ViewSlot? Alternative = null);
