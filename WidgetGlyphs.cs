using System.Drawing.Drawing2D;

namespace PomoQuote;

/// <summary>Small drawn symbols of the widget.</summary>
internal static class WidgetGlyphs
{
    /// <summary>The "updating" mark of one provider: a circular arrow in the secondary text colour (static, the numbers on screen are the last good ones).</summary>
    public static void DrawUpdating(Graphics graphics, RectangleF box, float u, Color color)
    {
        using var pen = new Pen(color, Math.Max(1f, 1.3f * u)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawArc(pen, box, 40, 280);
        var tip = new PointF(box.Right - box.Width * 0.12f, box.Y + box.Height * 0.2f);
        graphics.DrawLine(pen, tip, new PointF(tip.X - 2.6f * u, tip.Y - 0.2f * u));
        graphics.DrawLine(pen, tip, new PointF(tip.X + 0.2f * u, tip.Y + 2.6f * u));
    }
}