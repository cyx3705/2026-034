using System.Globalization;

namespace HistoryStrenua;

/// <summary>出图规划：选比例与图幅。</summary>
internal static partial class DrawingPlanner
{
    /// <summary>
    /// 上 A2 之前的宽松判断。主视图是窄边时（真机后板：1100 × 20 的侧面上 22 个孔）面积只反映视图窄，不反映挤，
    /// 所以短边至少按长边的四分之一算；夹爪的 16 个「窗口」也不再把它从 A3 推到 A2。
    /// </summary>
    private static bool Readable(double longSide, double w, double h, int features, double? smallest, double scale)
    {
        var area = longSide * Math.Max(Math.Min(w, h), longSide / 4) * scale * scale;
        return longSide * scale >= MinMainSide - 1e-9
            && (features == 0 || area / features >= ReadableAreaPerFeature - 1e-12)
            && (smallest is null || smallest * scale >= ReadableHole - 1e-9);
    }

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

        // A2 门槛（用户定：A2 很少用，不该容易命中）：A3 及以下放得下、又没到真看不清（见 Readable）就不上 A2。
        if (chosen.Template is null || chosen.Template.Width >= LargeSheetLongSide)
        {
            var smaller = fits
                .Where(item => item.Template.Width < LargeSheetLongSide && Readable(longSide, w, h, features, smallest, item.Scale))
                .OrderByDescending(item => item.Scale)
                .ThenBy(item => item.Template.Width * item.Template.Height)
                .ToList();
            if (smaller.Count > 0)
            {
                var best = smaller[0];
                return new SheetChoice(best.Template, best.Scale, best.Layout, best.Slots,
                    $"A2 门槛：{best.Template.SizeName} 放得下也还看得清，不上 A2（主视图长边 {longSide * best.Scale * 1000:0} mm）");
            }
        }

        if (chosen.Template is not null)
            return new SheetChoice(chosen.Template, chosen.Scale, chosen.Layout, chosen.Slots,
                $"放得下且看得清的最小图幅（主视图长边 {longSide * chosen.Scale * 1000:0} mm"
                + (features > 0 ? $"，每个孔 / 圆弧 {w * h * chosen.Scale * chosen.Scale / features * 1e6:0} mm²" : string.Empty)
                + (smallest is { } s ? $"，最小孔 ⌀{s * chosen.Scale * 1000:0.#} mm" : string.Empty) + "）");

        chosen = fits.OrderByDescending(item => item.Scale).First();
        return new SheetChoice(chosen.Template, chosen.Scale, chosen.Layout, chosen.Slots, "哪个图幅都不够看清，取比例最大的");
    }
}
