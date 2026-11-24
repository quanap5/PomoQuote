using System.Drawing.Drawing2D;

namespace PomoQuote.Widget;

/// <summary>The progress bar of the timer and of the quotas: a pill, rounded track and fill, honest at 0%, tiny values and 100%.</summary>
internal static class PillBar
{
    /// <summary>
    /// The width of the fill: nothing at 0% (no stray dot), at least a full round end (the height) for any value above 0, the
    /// whole track at 100%.
    /// </summary>
    public static float FillWidth(float trackWidth, float height, double fraction)
    {
        if (double.IsNaN(fraction) || fraction <= 0) return 0;
        if (fraction >= 1) return trackWidth;
        return Math.Clamp((float)(trackWidth * fraction), Math.Min(height, trackWidth), trackWidth);
    }

    public static void Draw(Graphics graphics, RectangleF track, double fraction, Color trackColor, Color fillColor)
    {
        using var trackBrush = new SolidBrush(trackColor);
        using (var path = Pill(track)) graphics.FillPath(trackBrush, path);

        var width = FillWidth(track.Width, track.Height, fraction);
        if (width <= 0) return;
        using var fillBrush = new SolidBrush(fillColor);
        using var fill = Pill(new RectangleF(track.X, track.Y, width, track.Height));
        graphics.FillPath(fillBrush, fill);
    }

    /// <summary>A rectangle with fully rounded ends.</summary>
    public static GraphicsPath Pill(RectangleF bounds)
    {
        var path = new GraphicsPath();
        var radius = Math.Min(bounds.Height, bounds.Width) / 2f;
        if (radius <= 0.5f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 90, 180);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 180);
        path.CloseFigure();
        return path;
    }

    /// <summary>A rounded rectangle (the quote card, the provider cards).</summary>
    public static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
