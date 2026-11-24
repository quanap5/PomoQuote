namespace PomoQuote.Widget;

/// <summary>
/// Where the two date lines go, in logical pixels at 100%: on the same row as the clock digits, centred on their height, with every
/// line ending flush at the widget's right content margin (the same right edge as the other right-aligned content), and starting
/// after the widest clock text so the date never overlaps the clock. Pure numbers, so the layout is tested.
/// </summary>
internal static class DateBlockLayout
{
    /// <summary>The clock text box (top and height) the time is drawn in.</summary>
    public const float ClockTop = 6;

    public const float ClockHeight = 40;

    /// <summary>The vertical centre of the clock's digits, measured on the real font (Segoe UI Light 30 px): the date block is centred on it.</summary>
    public const float DigitsCentre = 27;

    public const float LineHeight = 15;

    /// <summary>The gap kept between the widest clock text and the date.</summary>
    public const float ClockGap = 4;

    /// <summary>The right edge of the content: the widget width minus its margin.</summary>
    public static float RightEdge => UsageGeometry.WidgetWidth - UsageGeometry.Margin;

    /// <summary>The top of the two-line block: its centre is the centre of the digits.</summary>
    public static float Top => DigitsCentre - LineHeight;

    /// <summary>The room for a line: from after the widest clock text to the right margin.</summary>
    public static float Room(float widestClockWidth) => RightEdge - (UsageGeometry.Margin + widestClockWidth + ClockGap);

    /// <summary>The rectangle of line 0 (solar) or 1 (lunar); the text is right-aligned inside it, so it ends at <see cref="RightEdge"/>.</summary>
    public static RectangleF Line(int index, float widestClockWidth)
    {
        var left = UsageGeometry.Margin + widestClockWidth + ClockGap;
        return new RectangleF(left, Top + index * LineHeight, RightEdge - left, LineHeight);
    }
}
