using System.Globalization;

namespace HistoryStrenua;

/// <summary>
/// 「新建工程图」（1.9.0）的纯几何部分：主视图朝哪、要哪几个投影视图、用哪个模板与比例、视图与技术要求摆在哪。
/// 不碰 SolidWorks，能离线测。
/// </summary>
/// <remarks>
/// <para>依据是用户 2026-025 台面2机器的 15 张手工图：主视图都是标准命名视图、不转角度；主视图看的是孔和圆弧最多的那一面
/// （板件看板面，L 形件看截面轮廓）；投影视图只加看得到侧面孔、看得到厚度的那几个；每张都有一个从主视图斜投影出来的轴测图；
/// 技术要求放在图纸里；标题栏由模板的属性链接带出。</para>
/// </remarks>
internal static partial class DrawingPlanner
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

    /// <summary>
    /// 按名字建的主视图朝向不对时（1.16.0），接着试哪几个命名视图：把建出来的那个实际朝向 <paramref name="actual"/> 当作零件自己的标准视图定义，
    /// 推出每个命名视图在这个零件里实际朝哪，实际视线最接近 <paramref name="desired"/> 的排最前（其余兜底）。
    /// </summary>
    /// <remarks>
    /// 真机滑动板：回执写「*前视，沿 Z 看」，建出来却是沿 +X 看——零件改过标准视图（「更新标准视图」），「*前视」不再是 +Z。
    /// 改过之后六个命名视图之间的关系不变（后视 = 前视反过来、上视 = 前视往下翻 90°……），所以知道一个就能推出全部。
    /// </remarks>
    /// <param name="tried">刚按名字建的那个命名视图。</param>
    /// <param name="actual">它建出来的实际朝向（真机读回）。</param>
    /// <param name="desired">主视图想要的视线（<see cref="MainView"/> 选的那一面）。</param>
    public static IReadOnlyList<StandardView> RetryViews(StandardView tried, ViewFrame actual, ModelDirection desired)
        => StandardView.All
            .Where(view => view.Key != tried.Key)
            .OrderByDescending(view => ActualNormal(tried, actual, view).Dot(desired))
            .ToList();

    /// <summary>
    /// 命名视图 <paramref name="target"/> 在这个零件里的实际视线：<paramref name="tried"/> 的三个方向按表（<see cref="StandardView.All"/>，前视 = Z / X / Y）
    /// 与实际读回的三个方向一一对应，<paramref name="target"/> 的视线在表里由 <paramref name="tried"/> 的三个方向怎么组合，实际也怎么组合。
    /// </summary>
    public static ModelDirection ActualNormal(StandardView tried, ViewFrame actual, StandardView target)
    {
        var (a, b, c) = (tried.Right.Dot(target.Normal), tried.Up.Dot(target.Normal), tried.Normal.Dot(target.Normal));
        return new ModelDirection(
            a * actual.SheetX.X + b * actual.SheetY.X + c * actual.Normal.X,
            a * actual.SheetX.Y + b * actual.SheetY.Y + c * actual.Normal.Y,
            a * actual.SheetX.Z + b * actual.SheetY.Z + c * actual.Normal.Z);
    }

    /// <summary>建出来的视图视线是不是想要的那面（同向，差 2.6° 以内）。</summary>
    public static bool Facing(ViewFrame actual, ModelDirection desired) => actual.Normal.Dot(desired) >= AxisCosine;

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

        // 沿图纸 X：摆右边的视图在第一角投影里看 −X 那一侧。窗口两面都看得到，不影响摆哪边；倒角（1.10.0）沿这个方向看才成斜线、才标得了。
        // 1.14.1：两头都有孔、位置对不上（OppositePlanner.FacesDiffer）就两边都加——从一边看只看得到朝它开口的那些。
        if (alongX.Holes + alongX.Windows + alongX.Chamfers > 0)
        {
            var rightShows = firstAngle ? alongX.Minus : alongX.Plus;
            var leftShows = firstAngle ? alongX.Plus : alongX.Minus;
            var reason = "侧面沿图纸横向有" + Features(alongX.Holes, alongX.Windows, alongX.Chamfers);
            if (OppositePlanner.FacesDiffer(part, r))
            {
                result.Add(new SideView(ViewSlot.Right, reason + "，两头的孔不一样", Exact: true));
                result.Add(new SideView(ViewSlot.Left, reason + "，两头的孔不一样", Exact: true));
            }
            else
            {
                result.Add(new SideView(rightShows >= leftShows ? ViewSlot.Right : ViewSlot.Left, reason));
            }
        }

        // 沿图纸 Y：摆下边的视图在第一角投影里看 +Y 那一侧（零件上面）。
        if (alongY.Holes + alongY.Windows + alongY.Chamfers > 0)
        {
            var belowShows = firstAngle ? alongY.Plus : alongY.Minus;
            var aboveShows = firstAngle ? alongY.Minus : alongY.Plus;
            var reason = "侧面沿图纸竖向有" + Features(alongY.Holes, alongY.Windows, alongY.Chamfers);
            if (OppositePlanner.FacesDiffer(part, u))
            {
                result.Add(new SideView(ViewSlot.Below, reason + "，两头的孔不一样", Exact: true));
                result.Add(new SideView(ViewSlot.Above, reason + "，两头的孔不一样", Exact: true));
            }
            else
            {
                result.Add(new SideView(belowShows >= aboveShows ? ViewSlot.Below : ViewSlot.Above, reason));
            }
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

    /// <summary>
    /// 在已定的图幅与比例下给「可换边」的投影视图挑边（「投影视图」单独用时）：与 <see cref="ChooseSheet"/> 同一规则——
    /// 按估计排版，取第一种放得下的摆法；都放不下用首选。
    /// </summary>
    public static IReadOnlyList<ViewSlot> ChooseSlots(PartGeometry part, StandardView main, IReadOnlyList<SideView> sides, SheetSpace sheet, double scale, bool chain)
        => Arrangements(sides).FirstOrDefault(slots => Layout(sheet, Estimate(part, main, slots, scale, chain)).Fits)
           ?? sides.Select(side => side.Slot).ToList();

    /// <summary>真机读回的视图朝向当成一个「标准视图」用（估大小只要朝向，不要名字）。</summary>
    public static StandardView ViewOf(ViewFrame frame) => new("actual", [], frame.Normal, frame.SheetX, frame.SheetY);

    /// <summary>
    /// 图纸上已有的视图摆在主视图哪一边（读现有图纸用：「投影视图」不重复加、「排版」认出谁是左右上下视图）：
    /// 视线沿主视图图纸 X 的是左 / 右视图、沿图纸 Y 的是上 / 下视图——第一角投影里看零件左侧（视线 −X）的摆右边、
    /// 看上面（+Y）的摆下边，第三角投影反过来（与 <see cref="SideViews"/> 同一套）。别的朝向（后视、斜的）返回 null。
    /// </summary>
    public static ViewSlot? SlotOf(ViewFrame main, ModelDirection normal, bool firstAngle)
    {
        var x = normal.Dot(main.SheetX);
        var y = normal.Dot(main.SheetY);
        if (x <= -AxisCosine) return firstAngle ? ViewSlot.Right : ViewSlot.Left;
        if (x >= AxisCosine) return firstAngle ? ViewSlot.Left : ViewSlot.Right;
        if (y >= AxisCosine) return firstAngle ? ViewSlot.Below : ViewSlot.Above;
        if (y <= -AxisCosine) return firstAngle ? ViewSlot.Above : ViewSlot.Below;
        return null;
    }

    /// <summary>左右视图看的是图纸横向，上下视图看的是竖向。</summary>
    public static bool Horizontal(ViewSlot slot) => slot is ViewSlot.Right or ViewSlot.Left;

    /// <summary>
    /// 新加的投影视图贴着主视图放在哪（「投影视图」单独用时，主视图不动）：中间留 <see cref="Gap"/> 与该留的标注空间，
    /// 与 <see cref="Layout"/> 排出来的间距一样。<paramref name="main"/> 是主视图实际外框。
    /// </summary>
    public static SheetPoint Beside(SheetRect main, ViewBox mainBox, ViewSlot slot, ViewBox side) => slot switch
    {
        ViewSlot.Right => new SheetPoint(main.Right + mainBox.MarginRight + Gap + side.MarginLeft + side.Width / 2, main.Center.Y),
        ViewSlot.Left => new SheetPoint(main.Left - mainBox.MarginLeft - Gap - side.MarginRight - side.Width / 2, main.Center.Y),
        ViewSlot.Below => new SheetPoint(main.Center.X, main.Bottom - mainBox.MarginBottom - Gap - side.MarginTop - side.Height / 2),
        _ => new SheetPoint(main.Center.X, main.Top + mainBox.MarginTop + Gap + side.MarginBottom + side.Height / 2),
    };

    private static string Features(int holes, int windows, int chamfers)
        => string.Join("、", new[]
        {
            holes > 0 ? $"孔 {holes} 个" : null,
            windows > 0 ? $"窗口 {windows} 个" : null,
            chamfers > 0 ? $"倒角 {chamfers} 个" : null,
        }.Where(text => text is not null));

    /// <summary>沿某方向的孔、窗口、圆弧面（R1 以上）、倒角（C1 以外，沿这个方向看成斜线的），以及孔口朝正 / 负向各几个。</summary>
    private static (int Holes, int Windows, int Arcs, int Chamfers, int Plus, int Minus, int Total) Count(PartGeometry part, IReadOnlyList<PartHole> holes, ModelDirection direction)
    {
        var along = holes.Where(hole => Along(hole.Axis, direction)).ToList();
        var arcs = part.Cylinders.Count(cylinder => !(cylinder.Concave && cylinder.Full) && cylinder.Radius >= ArcMinRadius && Along(cylinder.Axis, direction));
        var plus = along.Count(hole => hole.Openings.Any(o => o.Dot(direction) > AxisCosine));
        var minus = along.Count(hole => hole.Openings.Any(o => o.Dot(direction) < -AxisCosine));
        var windows = part.Windows.Count(normal => Along(normal, direction));
        var chamfers = part.Chamfers.Count(chamfer => !chamfer.Default && Along(chamfer.Axis, direction));
        return (along.Count, windows, arcs, chamfers, plus, minus, along.Count + windows + arcs + chamfers);
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

    /// <summary>
    /// 投影视图连同标注空间（1.10.0）：与主视图共用的那个方向（左右视图的竖直方向、上下视图的水平方向）上的外轮廓尺寸主视图已经标了
    /// （外轮廓跨视图不重复），只有视图里有孔时才要孔位尺寸——没有孔就不在那一边留尺寸空间。限位块右的下视图只为倒角而加，
    /// 两边都留时 A4 竖向差几毫米、退到了 A3（用户手工图是 A4）。
    /// </summary>
    public static ViewBox SideBox(double width, double height, ViewSlot slot, int holeKinds, bool chain)
    {
        var margin = AnnotationMargin(holeKinds, chain);
        var shared = holeKinds > 0 ? margin : 0;
        return Horizontal(slot) ? new ViewBox(width, height, shared, margin) : new ViewBox(width, height, margin, shared);
    }

    /// <summary>沿某个视线方向的孔有几种（这些孔在那个视图里是圆，各要一层位置尺寸）。</summary>
    public static int HoleKinds(PartGeometry part, ModelDirection normal)
        => part.Holes.Where(hole => Along(hole.Axis, normal)).Select(hole => hole.Kind).Distinct(StringComparer.Ordinal).Count();

    /// <summary>技术要求注释的估计大小（用户标准 8 条、字高 3.5 实测约 100 × 41 mm）。建出来后用实际大小重排。</summary>
    public const double DefaultNoteWidth = 0.101;

    public const double DefaultNoteHeight = 0.042;
}

/// <summary>
/// 一个投影视图：摆在主视图哪边、为什么要它；<see cref="Alternative"/> 不为空时也可以摆到那一边（看厚度、只看圆弧的视图）。
/// <see cref="Exact"/>（1.14.1）：两头的孔不一样、两边都要——只有正好这一边已有视图才算有了，对面那边的不算。
/// </summary>
internal sealed record SideView(ViewSlot Slot, string Reason, ViewSlot? Alternative = null, bool Exact = false);
