using System.Drawing;

namespace HelloWorldBot.Widget;

/// <summary>Keeps the widget on a screen you can actually see. Pure maths on rectangles, so it is unit tested.</summary>
internal static class WidgetPlacement
{
    /// <summary>The gap to the screen edge for the first-run position.</summary>
    public const int DefaultMargin = 24;

    /// <summary>
    /// The top-left corner to use for a widget of <paramref name="size"/>. The widget always ends up fully
    /// inside one of <paramref name="workAreas"/> (the first one is the primary screen): the area it overlaps
    /// most, or the nearest one when it is on no screen (e.g. its monitor was unplugged). With no saved
    /// position it goes to the top-right of the primary area.
    /// </summary>
    public static Point Clamp(Point? desired, Size size, IReadOnlyList<Rectangle> workAreas)
    {
        if (workAreas.Count == 0) return desired ?? Point.Empty;

        var primary = workAreas[0];
        var wanted = desired ?? new Point(primary.Right - size.Width - DefaultMargin, primary.Top + DefaultMargin);
        var rectangle = new Rectangle(wanted, size);

        var area = PickArea(rectangle, workAreas);
        var x = Math.Clamp(rectangle.X, area.Left, Math.Max(area.Left, area.Right - size.Width));
        var y = Math.Clamp(rectangle.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height));
        return new Point(x, y);
    }

    private static Rectangle PickArea(Rectangle widget, IReadOnlyList<Rectangle> workAreas)
    {
        var best = workAreas[0];
        long bestOverlap = -1;
        foreach (var area in workAreas)
        {
            var overlap = Rectangle.Intersect(widget, area);
            long size = overlap.IsEmpty ? 0 : (long)overlap.Width * overlap.Height;
            if (size > bestOverlap)
            {
                best = area;
                bestOverlap = size;
            }
        }

        if (bestOverlap > 0) return best;

        // On no screen at all: take the area whose nearest point is closest to the widget's centre.
        var centre = new Point(widget.X + widget.Width / 2, widget.Y + widget.Height / 2);
        return workAreas.OrderBy(area => DistanceSquared(area, centre)).First();
    }

    private static long DistanceSquared(Rectangle area, Point point)
    {
        long dx = Math.Max(Math.Max(area.Left - point.X, 0), point.X - area.Right);
        long dy = Math.Max(Math.Max(area.Top - point.Y, 0), point.Y - area.Bottom);
        return dx * dx + dy * dy;
    }
}
