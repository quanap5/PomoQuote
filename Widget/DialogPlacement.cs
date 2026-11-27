using System.Drawing;

namespace PomoQuote.Widget;

/// <summary>Where a dialog goes: beside the widget, or over the window that opened it, always inside one screen's usable area. Pure maths.</summary>
internal static class DialogPlacement
{
    /// <summary>The space left between the widget and a dialog beside it, in logical pixels.</summary>
    public const int Gap = 8;

    /// <summary>
    /// The top-left corner for a dialog of <paramref name="size"/> next to <paramref name="widget"/> inside <paramref name="area"/>, without
    /// covering the widget: left or right of it (the side with more room first), level with its top; else under or above it. When the screen has
    /// no free spot of that size (a very large dialog) it goes to the edge on the side with more room, so it covers as little as it has to.
    /// A dialog larger than the area starts at the area's top-left corner.
    /// </summary>
    public static Point Beside(Rectangle widget, Size size, Rectangle area, int gap)
    {
        var level = Within(widget.Top, size.Height, area.Top, area.Bottom);
        var roomLeft = widget.Left - area.Left;
        var roomRight = area.Right - widget.Right;
        var left = widget.Left - gap - size.Width;
        var right = widget.Right + gap;
        var fitsLeft = left >= area.Left && left + size.Width <= area.Right;
        var fitsRight = right >= area.Left && right + size.Width <= area.Right;
        if (fitsLeft && (!fitsRight || roomLeft >= roomRight)) return new Point(left, level);
        if (fitsRight) return new Point(right, level);

        var aligned = Within(widget.Left, size.Width, area.Left, area.Right);
        var under = widget.Bottom + gap;
        var above = widget.Top - gap - size.Height;
        if (under >= area.Top && under + size.Height <= area.Bottom) return new Point(aligned, under);
        if (above >= area.Top && above + size.Height <= area.Bottom) return new Point(aligned, above);

        var edge = roomLeft >= roomRight ? area.Left : area.Right - size.Width;
        return new Point(Within(edge, size.Width, area.Left, area.Right), level);
    }

    /// <summary>
    /// Centred over <paramref name="parent"/> (the window that opened it) and inside <paramref name="area"/>. If that would cover
    /// <paramref name="widget"/>, the dialog goes beside the widget instead.
    /// </summary>
    public static Point Over(Rectangle parent, Size size, Rectangle area, Rectangle? widget, int gap)
    {
        var centred = Clamp(new Point(parent.Left + (parent.Width - size.Width) / 2, parent.Top + (parent.Height - size.Height) / 2), size, area);
        return widget is { } avoid && avoid.IntersectsWith(new Rectangle(centred, size)) ? Beside(avoid, size, area, gap) : centred;
    }

    /// <summary>The nearest position at which the whole dialog is inside the area (its top-left corner stays visible if it is larger).</summary>
    public static Point Clamp(Point desired, Size size, Rectangle area) =>
        new(Within(desired.X, size.Width, area.Left, area.Right), Within(desired.Y, size.Height, area.Top, area.Bottom));

    private static int Within(int start, int length, int from, int to) => Math.Clamp(start, from, Math.Max(from, to - length));
}
