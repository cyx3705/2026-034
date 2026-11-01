namespace HistoryStrenua;

/// <summary>图纸上的一个点（米）。</summary>
internal readonly record struct SheetPoint(double X, double Y);

/// <summary>图纸上的一条线段（米）。</summary>
internal readonly record struct SheetSegment(double X1, double Y1, double X2, double Y2);

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
    /// <summary>A2 的长边（米）：从这么宽起模板有装订边。</summary>
    private const double LargeSheetWidth = 0.590;

    public static SheetSpace Standard(double width, double height)
    {
        // 真机模板：A3、A4 四边 5 mm；A2 起左边装订 25 mm、其余 10 mm（1.25.0：一律按 5 mm 算时夹爪的尺寸落到 A2 图框线外，还报没冲突）。
        var (bind, edge) = width >= LargeSheetWidth ? (0.025, 0.010) : (0.005, 0.005);
        var frame = new SheetRect(bind, edge, width - edge, height - edge);
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
