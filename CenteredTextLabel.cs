namespace HelloWorldBot;

/// <summary>
/// A label that draws one unwrapped line at its center. The drawn string can differ from
/// <see cref="Control.Text"/> so a font's substitute glyphs never change the exposed text.
/// </summary>
internal sealed class CenteredTextLabel : Label
{
    private readonly string _drawnText;

    public CenteredTextLabel(string drawnText)
    {
        _drawnText = drawnText;
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    public Size MeasureDrawnText()
    {
        using var graphics = CreateGraphics();
        using var format = CreateFormat();
        return Size.Ceiling(graphics.MeasureString(_drawnText, Font, PointF.Empty, format));
    }

    // GDI+ drawing is required because GDI cannot use fonts from a PrivateFontCollection.
    protected override void OnPaint(PaintEventArgs e)
    {
        using var brush = new SolidBrush(ForeColor);
        using var format = CreateFormat();
        var center = new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f);
        e.Graphics.DrawString(_drawnText, Font, brush, center, format);
    }

    private static StringFormat CreateFormat() => new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        FormatFlags = StringFormatFlags.NoWrap,
        Trimming = StringTrimming.None,
    };
}
