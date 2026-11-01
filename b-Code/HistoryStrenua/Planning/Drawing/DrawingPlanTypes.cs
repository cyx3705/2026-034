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

/// <summary>一个视图在图纸上占多大：外框（含 SolidWorks 视图留白），以及左边、上边要留给尺寸的宽度。</summary>
internal sealed record ViewBox(double Width, double Height, double MarginLeft, double MarginTop,
    double MarginRight = 0, double MarginBottom = 0, IReadOnlyList<SheetRect>? AnnotationBounds = null);

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
