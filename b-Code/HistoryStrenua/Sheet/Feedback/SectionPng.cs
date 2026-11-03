using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace HistoryStrenua;

/// <summary>
/// 接触面截面画成 PNG（1.39.0，用户定：截面放进回执，文字版比图贵时给图）。版式、颜色与 <see cref="ContactPlanner.Svg"/> 相同，重叠区照实填色。
/// </summary>
internal static class SectionPng
{
    /// <summary>存到模块数据目录 snapshots（和图纸截图同一处，只留最近 30 张），返回路径。</summary>
    public static string Save(QuickCommandContext context, string title, IReadOnlyList<SectionPanel> panels)
    {
        var directory = Path.Combine(context.Options.DataDirectory ?? Path.GetTempPath(), DrawingSnapshot.Folder);
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, $"{DrawingSnapshot.SafeName(title)}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
        using (var bitmap = Draw(panels))
            bitmap.Save(file, ImageFormat.Png);
        DrawingSnapshot.Prune(directory);
        return file;
    }

    public static Bitmap Draw(IReadOnlyList<SectionPanel> panels)
    {
        var (width, height) = ContactPlanner.SheetSize(panels.Count);
        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(ColorTranslator.FromHtml("#FBFAF7"));
        using var bold = new Font("Microsoft YaHei", 8.5f, FontStyle.Bold);
        using var small = new Font("Microsoft YaHei", 8f);
        using var ink = new SolidBrush(ColorTranslator.FromHtml(ContactPlanner.Ink));
        using var grey = new SolidBrush(ColorTranslator.FromHtml("#6B675E"));
        using var frame = new Pen(ColorTranslator.FromHtml("#D6D2C8"));
        for (var i = 0; i < panels.Count; i++)
        {
            var panel = panels[i];
            var x0 = i % ContactPlanner.PanelColumns * ContactPlanner.PanelWidth;
            var y0 = i / ContactPlanner.PanelColumns * ContactPlanner.PanelHeight;
            g.DrawRectangle(frame, x0 + 2, y0 + 2, ContactPlanner.PanelWidth - 4, ContactPlanner.PanelHeight - 4);
            g.DrawString($"{panel.Id} {panel.Title}", bold, ink, x0 + 6, y0 + 4);
            g.DrawString($"横 {panel.UName} → 竖 {panel.VName} ↑（mm）", small, grey, x0 + 6, y0 + 18);

            // 取景框等比缩放、居中；截面 v 朝上，像素 y 朝下。
            var box = new RectangleF(x0 + 6, y0 + ContactPlanner.PanelHeader + 2, ContactPlanner.PanelWidth - 12, ContactPlanner.PanelHeight - ContactPlanner.PanelHeader - 8);
            var (minU, minV, maxU, maxV) = panel.View;
            var scale = (float)Math.Min(box.Width / (maxU - minU), box.Height / (maxV - minV));
            var ox = box.X + (box.Width - (float)(maxU - minU) * scale) / 2;
            var oy = box.Y + (box.Height + (float)(maxV - minV) * scale) / 2;
            PointF Map((double U, double V) p) => new(ox + (float)(p.U - minU) * scale, oy - (float)(p.V - minV) * scale);

            var state = g.Save();
            g.SetClip(box);
            foreach (var shape in panel.Shapes.Where(shape => shape.Fill is not null))
            {
                using var fill = new SolidBrush(Color.FromArgb(140, ColorTranslator.FromHtml(shape.Fill!)));
                foreach (var loop in shape.Loops.Where(loop => loop.Count >= 3))
                    g.FillPolygon(fill, loop.Select(Map).ToArray());
            }

            foreach (var shape in panel.Shapes.Where(shape => shape.Fill is null))
            {
                using var pen = new Pen(ColorTranslator.FromHtml(shape.Stroke!), (float)shape.Width);
                foreach (var loop in shape.Loops.Where(loop => loop.Count >= 2))
                    g.DrawPolygon(pen, loop.Select(Map).ToArray());
            }

            g.Restore(state);
        }

        return bitmap;
    }
}
