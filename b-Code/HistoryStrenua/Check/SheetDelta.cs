namespace HistoryStrenua;

/// <summary>两次读图之间的差异：哪些视图要出现在反馈里、哪些要读几何、一行摘要。</summary>
/// <param name="Views">差异里的视图（注解只留改了的；几何此时还是空的，读完再由 <see cref="SheetDelta.Fill"/> 填）。</param>
/// <param name="Geometry">要带几何的视图（新建、换比例）。</param>
/// <param name="Scan">要重新扫的视图：带几何的，加上新加 / 改了尺寸的（尺寸量的两头要扫几何才知道）。</param>
/// <param name="Summary">一行摘要，如「Drawing View1 +3 ~1 -2；Drawing View2 挪了 (+10, 0)」。</param>
internal sealed record SheetDiff(IReadOnlyList<SvgView> Views, IReadOnlySet<string> Geometry, IReadOnlySet<string> Scan, string Summary)
{
    public bool Empty => Views.Count == 0;
}

/// <summary>
/// 每一步回执什么（1.21.0，用户定：只回本轮改的；我定怎么算「改了」）。纯数据，离线可测。
/// </summary>
/// <remarks>
/// <para>站在调指令的 AI 这边想：零件几何是静的，视图建好（或换比例）给一次就够，之后按编号引用；每一步真正要知道的是
/// 加了 / 删了 / 改了哪些注解、哪个视图挪了。所以差异按注解名比：新的 added、没了 removed、字 / 值变了或字挪了 0.05 mm 以上 changed；
/// 视图大小或比例变了 rescaled（连同几何与全部注解重给），只是中心挪了 moved（给原中心；跟着视图一起挪的注解不算改）。</para>
/// <para>换了一张图（一键出图从零件建了新图）前面就当什么都没有，整页都是 added——多半超预算，降成截图，这是对的。</para>
/// </remarks>
internal static class SheetDelta
{
    /// <summary>算挪动的容差（0.05 mm）。</summary>
    public const double Tolerance = 5e-5;

    public static SheetDiff Diff(SvgSheet? before, SvgSheet after)
    {
        var old = before is not null && before.Title == after.Title
            ? before.Views.ToDictionary(view => view.Name, StringComparer.Ordinal)
            : new Dictionary<string, SvgView>(StringComparer.Ordinal);
        var views = new List<SvgView>();
        var geometry = new HashSet<string>(StringComparer.Ordinal);
        var scan = new HashSet<string>(StringComparer.Ordinal);
        var summary = new List<string>();
        foreach (var view in after.Views)
        {
            if (!old.TryGetValue(view.Name, out var was))
            {
                views.Add(view with { Change = "added", Annotations = view.Annotations.Select(a => a with { Change = "added" }).ToList() });
                if (view.Outline is not null)
                {
                    geometry.Add(view.Name);
                    scan.Add(view.Name);
                }

                summary.Add($"{view.Name} 新建（{view.Annotations.Count} 个注解）");
                continue;
            }

            var rescaled = view.Scale != was.Scale || !SameSize(view.Outline, was.Outline);
            if (rescaled)
            {
                views.Add(view with { Change = "rescaled" });
                geometry.Add(view.Name);
                scan.Add(view.Name);
                summary.Add($"{view.Name} 换了比例 / 大小（{was.Scale} → {view.Scale}）");
                continue;
            }

            var (dx, dy) = Shift(was.Outline, view.Outline);
            var moved = Math.Abs(dx) > Tolerance || Math.Abs(dy) > Tolerance;
            var changes = Annotations(was.Annotations, view.Annotations, dx, dy);
            if (!moved && changes.Count == 0)
                continue;
            views.Add(view with
            {
                Change = moved ? "moved" : string.Empty,
                From = moved ? was.Outline?.Center : null,
                Annotations = changes,
            });
            // 只挪了的不用重扫（不写量的两头），省几秒。
            if (changes.Any(a => a.Change is "added" or "changed" && (a.Kind.EndsWith("尺寸", StringComparison.Ordinal) || a.Kind == "孔标注")))
                scan.Add(view.Name);
            var parts = new List<string>();
            if (moved)
                parts.Add($"挪了 ({Signed(dx)}, {Signed(dy)})");
            foreach (var (change, sign) in new[] { ("added", "+"), ("changed", "~"), ("moved", "→"), ("removed", "-") })
                if (changes.Count(a => a.Change == change) is > 0 and var n)
                    parts.Add(sign + n);
            summary.Add(view.Name + " " + string.Join(" ", parts));
        }

        foreach (var (name, was) in old)
        {
            if (after.Views.Any(view => view.Name == name))
                continue;
            views.Add(was with { Change = "removed", Annotations = [], Lines = [], Curves = [], Holes = [] });
            summary.Add($"{name} 删了");
        }

        return new SheetDiff(views, geometry, scan, string.Join("；", summary));
    }

    /// <summary>
    /// 把重新扫过的视图（几何 + 尺寸量的两头）填进差异：带几何的视图换成扫过的几何，差异里的注解按名字换成扫过的那份（保留变更标记）。
    /// </summary>
    public static SvgSheet Fill(SvgSheet after, SheetDiff diff, IReadOnlyDictionary<string, SvgView> scanned)
    {
        var views = diff.Views.Select(view =>
        {
            if (!scanned.TryGetValue(view.Name, out var full))
                return view;
            var byName = full.Annotations.ToDictionary(a => a.Name, StringComparer.Ordinal);
            var annotations = view.Change == "rescaled"
                ? full.Annotations
                : view.Annotations.Select(a => byName.TryGetValue(a.Name, out var f) ? f with { Change = a.Change } : a).ToList();
            return diff.Geometry.Contains(view.Name)
                ? view with { Lines = full.Lines, Curves = full.Curves, Holes = full.Holes, Note = full.Note, Annotations = annotations }
                : view with { Annotations = annotations };
        }).ToList();
        return after with { Frame = null, Blocks = [], Views = views, Delta = true };
    }

    /// <summary>同名注解逐个比：新的 added、没了 removed、字 / 值变了或（扣掉视图挪动后）字挪了的 changed。</summary>
    private static List<SvgAnnotation> Annotations(IReadOnlyList<SvgAnnotation> before, IReadOnlyList<SvgAnnotation> after, double dx, double dy)
    {
        var old = before.GroupBy(a => a.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var result = new List<SvgAnnotation>();
        foreach (var annotation in after)
        {
            if (!old.TryGetValue(annotation.Name, out var was))
                result.Add(annotation with { Change = "added" });
            else if (was.Text != annotation.Text || was.Value != annotation.Value)
                result.Add(annotation with { Change = "changed" });
            // 1.22.0 真机：整理标注挪了技术要求，差异里又带了一遍 9 条全文——字没变的只是 moved，只给新方框。
            else if (Moved(was.TextBox ?? was.Box, annotation.TextBox ?? annotation.Box, dx, dy))
                result.Add(annotation with { Change = "moved" });
        }

        var names = after.Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
        result.AddRange(before.Where(a => !names.Contains(a.Name)).Select(a => a with { Change = "removed" }));
        return result;
    }

    private static bool Moved(SheetRect? before, SheetRect? after, double dx, double dy)
        => before is { } b && after is { } a
            ? Math.Abs(a.Left - b.Left - dx) > Tolerance || Math.Abs(a.Bottom - b.Bottom - dy) > Tolerance
              || Math.Abs(a.Width - b.Width) > Tolerance || Math.Abs(a.Height - b.Height) > Tolerance
            : before.HasValue != after.HasValue;

    private static bool SameSize(SheetRect? a, SheetRect? b)
        => a is { } x && b is { } y
            ? Math.Abs(x.Width - y.Width) <= 2 * Tolerance && Math.Abs(x.Height - y.Height) <= 2 * Tolerance
            : a.HasValue == b.HasValue;

    private static (double Dx, double Dy) Shift(SheetRect? before, SheetRect? after)
        => before is { } b && after is { } a ? (a.Center.X - b.Center.X, a.Center.Y - b.Center.Y) : (0, 0);

    private static string Signed(double meters)
    {
        var mm = DrawingSvgWriter.Mm(meters);
        return mm == "0" || mm.StartsWith('-') ? mm : "+" + mm;
    }
}
