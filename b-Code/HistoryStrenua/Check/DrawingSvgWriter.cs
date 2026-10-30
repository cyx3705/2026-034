using System.Globalization;
using System.Security;
using System.Text;

namespace HistoryStrenua;

/// <summary>读出来的一页图纸，写 SVG 用（纯数据，图纸坐标米）。</summary>
/// <param name="Title">工程图标题。</param>
/// <param name="SheetName">图纸页名（也是图纸上注释所在「视图」的名字）。</param>
/// <param name="Width">纸宽。</param>
/// <param name="Height">纸高。</param>
/// <param name="Frame">图框（国标估计）；读不到为 null。</param>
/// <param name="Blocks">图框里整块的格子：标题栏、修改栏、图号框，内容合成一段字。</param>
/// <param name="Views">图纸页本身（放图纸上的注释）与各视图。</param>
/// <param name="Delta">只是这一步改了的东西（<see cref="SheetDelta"/>）：不画图框、格子，元素带 <c>data-change</c>。</param>
internal sealed record SvgSheet(string Title, string SheetName, double Width, double Height, SheetRect? Frame,
    IReadOnlyList<SvgBlock> Blocks, IReadOnlyList<SvgView> Views, bool Delta = false);

/// <summary>图框里的一块（标题栏……）：只画方框 + 标签，格子里的字合成一段放在 <c>data-text</c>。</summary>
internal sealed record SvgBlock(string Id, string Label, SheetRect Rect, string Text);

/// <summary>
/// 一个视图：外框、可见直边（编号 E）、曲线（折线近似）、孔（编号 H）、注解。图纸页本身没有几何，只有注解。
/// 差异里 <paramref name="Change"/> 是 added / removed / moved / rescaled（空 = 视图本身没变，只是里面的注解变了），moved 时 <paramref name="From"/> 是原来的中心。
/// </summary>
internal sealed record SvgView(string Name, string Kind, string Parent, string Scale, SheetRect? Outline,
    IReadOnlyList<SheetSegment> Lines, IReadOnlyList<SheetSegment> Curves, IReadOnlyList<HoleEdge> Holes,
    IReadOnlyList<SvgAnnotation> Annotations, string Note = "", string Change = "", SheetPoint? From = null);

/// <summary>
/// 一个注解：方框画字占的地方（<paramref name="TextBox"/>，没有就用整个外框），<c>data-box</c> 另写连尺寸界线、引线在内的整个外框（<paramref name="Box"/>）；
/// 尺寸再带它量的两头（<paramref name="Measures"/>）。中心符号线、中心线本身就是线，画 <paramref name="Lines"/>（并成一条 path），不画方框。
/// 竖直尺寸的字按 <paramref name="Angle"/>（弧度，逆时针）转过来。差异里 <paramref name="Change"/> 是 added / removed / changed。
/// </summary>
internal sealed record SvgAnnotation(string Name, string Kind, string Text, string Value, SheetRect? Box, SheetPoint? TextAt,
    double TextHeight, string Measures, IReadOnlyList<SheetSegment> Lines, SheetRect? TextBox = null, double Angle = 0, string Change = "");

/// <summary>
/// 写多细（1.21.0，用户定：回执超过 1 万字就不如一张截图，按预算一级级降）。
/// </summary>
/// <param name="Geometry">画零件几何（直边、曲线、孔）。几何是静的，视图建好、换比例时给一次，之后按编号引用。</param>
/// <param name="Compact">注解只留方框与属性（字放进 <c>data-text</c>、不出 text 元素、不写 data-box），不出中心符号线、中心线（由孔推得出），不带样式与说明注释。</param>
/// <param name="Annotations">出注解。</param>
internal sealed record SvgDetail(bool Geometry = true, bool Compact = false, bool Annotations = true)
{
    public static SvgDetail Full { get; } = new();

    /// <summary>整页、每步差异：预算从细到粗依次试的几档（先丢几何——几何是静的，读过一次就有）；都超了就降成截图。</summary>
    public static IReadOnlyList<SvgDetail> Ladder { get; } = [Full, new(true, true), new(false, true)];

    /// <summary>
    /// 单取一个视图（<c>check.svg view=</c>）：要的就是几何，先丢注解（1.22.0 真机：主视图按整页那套阶梯落到「只有标注」，几何反而没了）。
    /// </summary>
    public static IReadOnlyList<SvgDetail> ViewLadder { get; } = [Full, new(true, true), new(true, true, false)];

    public string Label => (Geometry, Compact, Annotations) switch
    {
        (true, false, _) => "完整",
        (true, true, true) => "几何 + 精简标注",
        (true, _, false) => "只有几何",
        _ => "只有标注",
    };
}

/// <summary>
/// 把一页图纸写成 SVG（1.21.0，用户定：AI 调完 MCP 拿 SVG 代码当反馈，不看图片）。纯文本，离线可测。
/// </summary>
/// <remarks>
/// <para><b>坐标就是图纸 mm、左下角为原点</b>，和 <c>view.move / dim.move</c> 的 x/y、<c>view.list / dim.list</c> 的数一致：
/// 整页套一层 <c>matrix(1 0 0 -1 0 H)</c> 翻 Y，几何里的数字不换算；文字各自再翻回来，浏览器打开也是正的。</para>
/// <para>取舍（用户问、我定）：读的人是 AI，看的是代码不是图像——零件几何要精确（直边、孔、曲线，带编号，将来加尺寸按编号点），
/// 标注的「装饰」（箭头、尺寸界线、引线）不画，只给方框 + 显示的字 + 量的哪两头；标题栏、修改栏、图号框整块一个方框，格子里的字合成一段。</para>
/// </remarks>
internal static class DrawingSvgWriter
{
    public static string Write(SvgSheet sheet, SvgDetail? detail = null)
    {
        detail ??= SvgDetail.Full;
        var w = Mm(sheet.Width);
        var h = Mm(sheet.Height);
        var svg = new StringBuilder();
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {w} {h}\" width=\"{w}mm\" height=\"{h}mm\"")
            .Append($" data-title=\"{Escape(sheet.Title)}\" data-sheet=\"{Escape(sheet.SheetName)}\" data-units=\"mm\" data-origin=\"bottom-left\"")
            .Append(sheet.Delta ? " data-delta=\"true\"" : string.Empty)
            .Append($" data-detail=\"{detail.Label}\">\n");
        // 真机：单步差异 1442 字里 600 多字是固定的样式和说明，对读代码的 AI 没用（约定在自描述里）——差异和精简档都不带（1.22.0 起精简档也不带）。
        var header = !sheet.Delta && !detail.Compact;
        if (header)
            WriteHeader(svg);
        svg.Append($"<g transform=\"matrix(1 0 0 -1 0 {h})\">\n");
        if (header)
            svg.Append($"<rect class=\"sheet\" x=\"0\" y=\"0\" width=\"{w}\" height=\"{h}\" fill=\"#fff\"/>\n");
        if (sheet.Frame is { } frame)
            svg.Append($"<rect class=\"frame\" id=\"frame\"{Rect(frame)}/>\n");
        foreach (var block in sheet.Blocks)
        {
            svg.Append($"<g class=\"block\" id=\"{Escape(block.Id)}\" data-label=\"{Escape(block.Label)}\"");
            if (block.Text.Length > 0)
                svg.Append($" data-text=\"{Escape(block.Text)}\"");
            svg.Append($"><rect{Rect(block.Rect)}/>")
                .Append(Text(block.Rect.Center, Math.Min(0.005, block.Rect.Height / 3), block.Label))
                .Append("</g>\n");
        }

        foreach (var view in sheet.Views)
            WriteView(svg, view, detail, sheet.Delta);
        svg.Append("</g>\n</svg>\n");
        return svg.ToString();
    }

    private static void WriteHeader(StringBuilder svg)
    {
        svg.Append("<!-- PowerSW 图纸 SVG：坐标为图纸 mm、左下角为原点（与 strenua.view.* / dim.* 的 x、y 相同）。id 形如视图组内的「E3」（直边）、「H0」（孔）、「RD1」（注解名），视图组 id 是视图名。"
              + "标注只画方框与显示的字，data-measures 是尺寸量的两头；标题栏等图框格子整块一个方框，字在 data-text。"
              + "改图指令经 MCP 调用时回执只给本步差异（data-delta，data-change=added/removed/changed/moved/rescaled，几何只在新建、换比例的视图里给）。 -->\n");
        svg.Append("<style>")
            .Append(".frame{fill:none;stroke:#000;stroke-width:.5}.block{fill:none;stroke:#888;stroke-width:.25}")
            .Append(".outline{fill:none;stroke:#bbb;stroke-width:.1;stroke-dasharray:2 1}.edge,.curve{fill:none;stroke:#000;stroke-width:.35}")
            .Append(".hole{fill:none;stroke:#000;stroke-width:.25}.ann rect{fill:none;stroke:#06c;stroke-width:.15;stroke-dasharray:1 .5}")
            .Append(".center{fill:none;stroke:#c00;stroke-width:.13;stroke-dasharray:3 1 .5 1}text{font-family:sans-serif;fill:#06c}")
            .Append(".block text{fill:#888}[data-change=removed] rect{stroke:#c00}</style>\n");
    }

    private static void WriteView(StringBuilder svg, SvgView view, SvgDetail detail, bool delta)
    {
        svg.Append($"<g class=\"view\" id=\"{Escape(view.Name)}\" data-kind=\"{Escape(view.Kind)}\"");
        if (view.Change.Length > 0)
            svg.Append($" data-change=\"{view.Change}\"");
        if (view.From is { } from)
            svg.Append($" data-from=\"{Mm(from.X)},{Mm(from.Y)}\"");
        if (view.Parent.Length > 0)
            svg.Append($" data-parent=\"{Escape(view.Parent)}\"");
        if (view.Scale.Length > 0)
            svg.Append($" data-scale=\"{Escape(view.Scale)}\"");
        if (view.Outline is { } outline)
            svg.Append($" data-box=\"{Box(outline)}\"");
        svg.Append(">\n");
        if (view.Note.Length > 0)
            svg.Append($"<!-- {Escape(view.Note)} -->\n");
        // 差异里视图本身没变的，外框已在 data-box，不再画一遍。
        if (view.Outline is { } box && view.Change != "removed" && (!delta || view.Change.Length > 0))
            svg.Append($"<rect class=\"outline\"{Rect(box)}/>\n");
        if (detail.Geometry)
        {
            for (var i = 0; i < view.Lines.Count; i++)
            {
                var line = view.Lines[i];
                svg.Append($"<line class=\"edge\" id=\"E{i}\" x1=\"{Mm(line.X1)}\" y1=\"{Mm(line.Y1)}\" x2=\"{Mm(line.X2)}\" y2=\"{Mm(line.Y2)}\"/>\n");
            }

            foreach (var points in Polylines(view.Curves))
                svg.Append($"<polyline class=\"curve\" points=\"{string.Join(" ", points.Select(p => Mm(p.X) + "," + Mm(p.Y)))}\"/>\n");
            foreach (var hole in view.Holes)
            {
                svg.Append($"<circle class=\"hole\" id=\"H{hole.Index}\" cx=\"{Mm(hole.X)}\" cy=\"{Mm(hole.Y)}\" r=\"{Mm(hole.Radius)}\"");
                if (hole.Kind.Length > 0)
                    svg.Append($" data-kind=\"{Escape(hole.Kind)}\"");
                svg.Append("/>\n");
            }
        }

        // 1.22.0：id 只在视图组内编号（E3、H0、RD1），不再每个都带「视图名:」前缀——单个视图几十个孔、几十个尺寸，前缀就占上千字。
        if (detail.Annotations)
            foreach (var annotation in view.Annotations.Where(a => !detail.Compact || a.Change == "removed" || !(a.Lines.Count > 0 && a.Box is null)))
                WriteAnnotation(svg, annotation, detail);
        svg.Append("</g>\n");
    }

    private static void WriteAnnotation(StringBuilder svg, SvgAnnotation annotation, SvgDetail detail)
    {
        var center = annotation.Lines.Count > 0 && annotation.Box is null;
        svg.Append($"<g class=\"ann{(center ? " center" : string.Empty)}\" id=\"{Escape(annotation.Name)}\" data-kind=\"{Escape(annotation.Kind)}\"");
        if (annotation.Change.Length > 0)
            svg.Append($" data-change=\"{annotation.Change}\"");
        if (annotation.Change == "removed")
        {
            svg.Append("/>\n");
            return;
        }

        if (annotation.Value.Length > 0)
            svg.Append($" data-value=\"{Escape(annotation.Value)}\"");
        if (annotation.Measures.Length > 0)
            svg.Append($" data-measures=\"{Escape(annotation.Measures)}\"");
        if (!detail.Compact && annotation.Box is { } box)
            svg.Append($" data-box=\"{Box(box)}\"");
        if (annotation.Text.Length > 0 && (detail.Compact || annotation.Text.Contains('\n')))
            svg.Append($" data-text=\"{Escape(annotation.Text.Replace("\n", " / "))}\"");
        svg.Append('>');
        if (annotation.Lines.Count > 0 && !detail.Compact)
            svg.Append($"<path d=\"{Path(annotation.Lines)}\"/>");
        if ((annotation.TextBox ?? annotation.Box) is { } rect)
            svg.Append($"<rect{Rect(rect)}/>");
        if (!detail.Compact && annotation.Text.Length > 0 && (annotation.TextAt ?? annotation.Box?.Center) is { } at)
            svg.Append(Text(at, annotation.TextHeight > 0 ? annotation.TextHeight : 0.0035, annotation.Text,
                anchor: annotation.TextAt is null ? "middle" : "start", angle: annotation.Angle));
        svg.Append("</g>\n");
    }

    /// <summary>首尾相接的线段连成折线（曲线边近似出来的就是一串首尾相接的小段）。</summary>
    internal static List<List<SheetPoint>> Polylines(IReadOnlyList<SheetSegment> segments)
    {
        var result = new List<List<SheetPoint>>();
        foreach (var segment in segments)
        {
            var start = new SheetPoint(segment.X1, segment.Y1);
            var end = new SheetPoint(segment.X2, segment.Y2);
            if (result.Count > 0 && Near(result[^1][^1], start))
                result[^1].Add(end);
            else
                result.Add([start, end]);
        }

        return result;
    }

    private static bool Near(SheetPoint a, SheetPoint b) => Math.Abs(a.X - b.X) < 1e-7 && Math.Abs(a.Y - b.Y) < 1e-7;

    /// <summary>
    /// 文字：外层已翻 Y，这里在落点处再翻回来（<c>translate(x y) scale(1 -1)</c>），读到的 x、y 仍是图纸 mm；多行拆成 tspan，第一行基线在落点。
    /// </summary>
    private static string Text(SheetPoint at, double height, string text, string anchor = "middle", double angle = 0)
    {
        var rotate = Math.Abs(angle) > 1e-6 ? $" rotate({(angle * 180 / Math.PI).ToString("0.#", CultureInfo.InvariantCulture)})" : string.Empty;
        var size = Mm(height);
        var lines = text.Split('\n');
        var body = lines.Length == 1
            ? Escape(text)
            : string.Concat(lines.Select((line, i) => $"<tspan x=\"0\" dy=\"{(i == 0 ? "0" : Mm(height * 1.5))}\">{Escape(line)}</tspan>"));
        return $"<text transform=\"translate({Mm(at.X)} {Mm(at.Y)}){rotate} scale(1 -1)\" font-size=\"{size}\" text-anchor=\"{anchor}\">{body}</text>";
    }

    /// <summary>一串线段写成一条 path（中心符号线一组几十段，一段一个 line 太占地方）；首尾相接的省掉 M。</summary>
    private static string Path(IReadOnlyList<SheetSegment> lines)
        => string.Join(" ", Polylines(lines).Select(points => "M" + string.Join("L", points.Select(p => Mm(p.X) + " " + Mm(p.Y)))));

    private static string Rect(SheetRect rect)
        => $" x=\"{Mm(rect.Left)}\" y=\"{Mm(rect.Bottom)}\" width=\"{Mm(rect.Width)}\" height=\"{Mm(rect.Height)}\"";

    /// <summary>外框写成「左,下,右,上」（图纸 mm），和 view.list / dim.list 的四个数一致。</summary>
    private static string Box(SheetRect rect) => $"{Mm(rect.Left)},{Mm(rect.Bottom)},{Mm(rect.Right)},{Mm(rect.Top)}";

    /// <summary>米 → mm，最多两位小数（0.01 mm 足够定位，再多只是噪声）。</summary>
    internal static string Mm(double meters)
    {
        var mm = Math.Round(meters * 1000, 2);
        return (mm == 0 ? 0 : mm).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string Escape(string text) => SecurityElement.Escape(text) ?? string.Empty;
}
