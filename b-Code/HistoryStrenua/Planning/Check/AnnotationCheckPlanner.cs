namespace HistoryStrenua;

/// <summary>检查时读到的一个视图注解：在哪个视图、叫什么、什么类、显示出来的线与文字（图纸坐标）。</summary>
/// <param name="Index">回指调用方手里那个注解。</param>
/// <param name="View">所在视图名。</param>
/// <param name="Name">注解名（SolidWorks 里的名字，如「D1@工程图视图1」）。</param>
/// <param name="Kind">类名（尺寸、孔标注、注释……），回执用。</param>
/// <param name="Lines">显示数据里的直线（尺寸线、尺寸界线、引线、下划线……）。</param>
/// <param name="Texts">显示数据里的文字框。</param>
internal sealed record CheckedAnnotation(int Index, string View, string Name, string Kind, IReadOnlyList<SheetSegment> Lines, IReadOnlyList<TextBox> Texts);

/// <summary>一处重叠：哪个注解的文字压到了什么。<paramref name="Other"/> 是压到的另一个注解（压视图线、出图框时为 null）。</summary>
internal sealed record OverlapIssue(int Index, int? Other, string Reason);

/// <summary>
/// 检查类「注解重叠」（1.12.0）的纯几何部分：视图注解的文字有没有压到别的注解的文字或线、视图的轮廓线与孔、图纸上的注释与图框线，
/// 有没有出图框。不碰 SolidWorks，能离线测。
/// </summary>
/// <remarks>
/// <para>「压到」与避障同一个判法（<see cref="ClearancePlanner.Hits(TextBox, SheetSegment)"/>：线穿过放宽 0.3 mm 的文字框、两个文字框相交），
/// 所以避障开着加的东西按理查不出来，查出来的是手工摆的、避障关着加的、或者避障也没找到空处的。</para>
/// <para>自己的线不算（尺寸数字本来就站在自己的尺寸线上、孔标注本来就压着自己的下划线）。两个注解文字互相压只报一次。
/// 一个注解压了好几样，按「文字重叠 → 压别的注解的线 → 压视图线 → 压图纸注释 / 图框线 → 出图框」各报一条。</para>
/// </remarks>
internal static class AnnotationCheckPlanner
{
    /// <param name="annotations">视图里的注解。</param>
    /// <param name="viewLines">视图的轮廓线、曲线近似段、孔圆近似段（全部视图合在一起：注解压到相邻视图的线也是问题）。</param>
    /// <param name="sheetLines">图框线、标题栏等的边。</param>
    /// <param name="sheetBoxes">图纸上（不在视图里）的注解占的地方：技术要求、标题栏里的字……，带名字。</param>
    /// <param name="frame">图框；null 不查出框。</param>
    public static List<OverlapIssue> Overlaps(IReadOnlyList<CheckedAnnotation> annotations, IReadOnlyList<SheetSegment> viewLines,
        IReadOnlyList<SheetSegment> sheetLines, IReadOnlyList<(string Name, SheetRect Rect)> sheetBoxes, SheetRect? frame)
    {
        var issues = new List<OverlapIssue>();
        for (var i = 0; i < annotations.Count; i++)
        {
            var self = annotations[i];
            if (self.Texts.Count == 0)
                continue;

            // 文字压文字：只和后面的比，一对只报一次。
            for (var j = i + 1; j < annotations.Count; j++)
            {
                var other = annotations[j];
                if (self.Texts.Any(text => other.Texts.Any(o => ClearancePlanner.Hits(text, o))))
                    issues.Add(new OverlapIssue(self.Index, other.Index, $"文字与「{Label(other)}」的文字重叠"));
            }

            foreach (var other in annotations)
            {
                if (other.Index != self.Index && other.Lines.Any(line => self.Texts.Any(text => ClearancePlanner.Hits(text, line)))
                    && ClearancePlanner.Seat(self.Texts, self.Lines, new Obstacles(other.Lines, [])) is not 0.0)
                    issues.Add(new OverlapIssue(self.Index, other.Index, $"文字压在「{Label(other)}」的线上"));
            }

            var crossed = viewLines.Count(line => self.Texts.Any(text => ClearancePlanner.Hits(text, line)));
            if (crossed > 0)
                issues.Add(new OverlapIssue(self.Index, null, $"文字压在视图轮廓线上（{crossed} 段）"));

            foreach (var (name, rect) in sheetBoxes)
            {
                var box = new TextBox(rect.Left, rect.Bottom, rect.Width, rect.Height);
                if (self.Texts.Any(text => ClearancePlanner.Hits(text, box)))
                    issues.Add(new OverlapIssue(self.Index, null, $"文字压在图纸注释「{name}」上"));
            }

            if (sheetLines.Any(line => self.Texts.Any(text => ClearancePlanner.Hits(text, line))))
                issues.Add(new OverlapIssue(self.Index, null, "文字压在图框 / 标题栏线上"));

            if (frame is { } f && self.Texts.SelectMany(text => text.Corners()).Any(p => p.X < f.Left || p.X > f.Right || p.Y < f.Bottom || p.Y > f.Top))
                issues.Add(new OverlapIssue(self.Index, null, "文字出了图框"));

            if (self.Kind == CalloutKind)
                LeaderIssues(annotations, i, issues);
        }

        return issues;
    }

    /// <summary>孔标注的类名（<see cref="AnnotationRows.Kind"/> 从尺寸里分出来的）。</summary>
    public const string CalloutKind = "孔标注";

    /// <summary>
    /// 孔标注的引线（1.24.0）：过长、和别的孔标注引线交叉、横穿别的标注的线（尺寸线、尺寸界线）、穿过别的标注的字。
    /// 中心符号线、中心线不算——孔本来就在它们上面，引线必然跨过。两个孔标注引线交叉只报一次。
    /// </summary>
    private static void LeaderIssues(IReadOnlyList<CheckedAnnotation> annotations, int i, List<OverlapIssue> issues)
    {
        var self = annotations[i];
        var leaders = LeaderPlanner.Leaders(self.Lines);
        if (leaders.Count == 0)
            return;
        var length = LeaderPlanner.Length(leaders);
        if (length > LeaderPlanner.LongLeader)
            issues.Add(new OverlapIssue(self.Index, null, $"引线过长（{DrawingSvgWriter.Mm(length)} mm，孔标注宜放在孔旁）"));
        for (var j = 0; j < annotations.Count; j++)
        {
            var other = annotations[j];
            if (j == i || IsMark(other))
                continue;
            if (other.Kind == CalloutKind)
            {
                if (j > i && LeaderPlanner.Crossings(leaders, LeaderPlanner.Leaders(other.Lines)) > 0)
                    issues.Add(new OverlapIssue(self.Index, other.Index, $"引线与「{Label(other)}」的引线交叉"));
            }
            else if (LeaderPlanner.Crossings(leaders, other.Lines) > 0)
                issues.Add(new OverlapIssue(self.Index, other.Index, $"引线横穿「{Label(other)}」的线"));

            if (other.Texts.Any(text => leaders.Any(leader => ClearancePlanner.Hits(text, leader))))
                issues.Add(new OverlapIssue(self.Index, other.Index, $"引线穿过「{Label(other)}」的文字"));
        }
    }

    private static bool IsMark(CheckedAnnotation annotation) => annotation.Kind is "中心符号线" or "中心线";

    /// <summary>回执里称呼一个注解：「孔标注 D3@工程图视图1」。</summary>
    public static string Label(CheckedAnnotation annotation)
        => annotation.Name.Length > 0 ? $"{annotation.Kind} {annotation.Name}" : annotation.Kind;

    /// <summary>注解类名（<c>swAnnotationType_e</c>，回执用）；孔标注是尺寸的一种，由调用方另判。</summary>
    public static string KindName(int type) => type switch
    {
        1 => "装饰螺纹线",
        2 => "基准特征",
        3 => "基准目标",
        4 => "尺寸",
        5 => "形位公差",
        6 => "注释",
        7 => "表面粗糙度",
        8 => "焊接符号",
        9 => "自定义符号",
        10 => "销钉符号",
        11 => "引线",
        12 => "块",
        13 => "中心符号线",
        14 => "表格",
        15 => "中心线",
        16 => "基准原点",
        17 => "焊缝",
        18 => "修订云",
        _ => $"注解（类型 {type}）",
    };
}
