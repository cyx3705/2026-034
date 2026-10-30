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
            var gap = 0.004 + tier * 0.004;
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

    /// <summary>出了图框的文字拉回框内要挪多少（1.25.0）：先贴边，再沿边滑几档；在框内的没有候选。</summary>
    public static IEnumerable<SheetPoint> Inward(IReadOnlyList<TextBox> texts, SheetRect frame)
    {
        var corners = texts.SelectMany(t => t.Corners()).ToList();
        if (corners.Count == 0 || Within(texts, frame)) yield break;
        var (left, right) = (corners.Min(p => p.X), corners.Max(p => p.X));
        var (bottom, top) = (corners.Min(p => p.Y), corners.Max(p => p.Y));
        var dx = left < frame.Left ? frame.Left - left : right > frame.Right ? frame.Right - right : 0;
        var dy = bottom < frame.Bottom ? frame.Bottom - bottom : top > frame.Top ? frame.Top - top : 0;
        for (var step = 0; step <= 12; step++)
            foreach (var sign in step == 0 ? new[] { 1 } : new[] { 1, -1 })
            {
                var slide = sign * step * 0.004;
                yield return dx != 0 && dy == 0 ? new SheetPoint(dx, slide) : dy != 0 && dx == 0 ? new SheetPoint(slide, dy) : new SheetPoint(dx, dy);
            }
    }

    public static bool Within(IReadOnlyList<TextBox> texts, SheetRect frame)
        => texts.SelectMany(t => t.Corners()).All(p => p.X >= frame.Left && p.X <= frame.Right && p.Y >= frame.Bottom && p.Y <= frame.Top);
}
