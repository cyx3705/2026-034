namespace HistoryStrenua;

/// <summary>REQ-DQ-02：标注后的整页避障，先沿尺寸线滑，再往视图外侧换层。单位为图纸米。</summary>
internal static class AnnotationPlacementPlanner
{
    public static IEnumerable<SheetPoint> Offsets(IReadOnlyList<TextBox> texts, SheetRect view, bool callout)
    {
        if (texts.Count == 0) yield break;
        var corners = texts.SelectMany(t => t.Corners()).ToList();
        var box = new SheetRect(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Max(p => p.X), corners.Max(p => p.Y));
        var horizontal = Math.Abs(Math.Cos(texts[0].Angle)) > 0.9;
        var candidates = new List<SheetPoint>();
        for (var step = 1; step <= 60; step++)
            foreach (var sign in new[] { 1, -1 })
                candidates.Add(horizontal ? new SheetPoint(sign * step * 0.0005, 0) : new SheetPoint(0, sign * step * 0.0005));
        for (var tier = 0; tier < 8; tier++)
        {
            var gap = 0.006 + tier * 0.006;
            for (var along = -6; along <= 6; along++)
            {
                if (callout || horizontal)
                {
                    candidates.Add(new SheetPoint(along * 0.006, view.Top + gap - box.Bottom));
                    candidates.Add(new SheetPoint(along * 0.006, view.Bottom - gap - box.Top));
                }
                if (callout || !horizontal)
                {
                    candidates.Add(new SheetPoint(view.Left - gap - box.Right, along * 0.006));
                    candidates.Add(new SheetPoint(view.Right + gap - box.Left, along * 0.006));
                }
            }
        }
        foreach (var point in candidates.Distinct().OrderBy(p => p.X * p.X + p.Y * p.Y)) yield return point;
    }

    public static bool Within(IReadOnlyList<TextBox> texts, SheetRect frame)
        => texts.SelectMany(t => t.Corners()).All(p => p.X >= frame.Left && p.X <= frame.Right && p.Y >= frame.Bottom && p.Y <= frame.Top);
}
