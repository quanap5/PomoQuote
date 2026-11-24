using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace PomoQuote.Weather;

/// <summary>One piece of a line: an icon or some text.</summary>
internal abstract record LinePart
{
    /// <summary>An icon. <paramref name="Alpha"/> below 1 dims it (a stale value); <paramref name="WindDegrees"/> only for the wind arrow.</summary>
    public sealed record Icon(WeatherIconId Id, float Alpha = 1f, double WindDegrees = 0, float Scale = 1f) : LinePart;

    public sealed record Text(string Value) : LinePart;

    /// <summary>Extra space, as a multiple of the line's normal gap.</summary>
    public sealed record Gap(float Gaps = 1f) : LinePart;
}

/// <summary>
/// Lays out and draws a single line made of icons and text, the way the weather panel and the widget show their data:
/// "[pin] Hanoi, Vietnam" or "[drop] 87%  [wind] 7 km/h". Icons come from a <see cref="WeatherIconCache"/> at exactly the
/// pixel size needed, so they are never resampled.
/// </summary>
internal static class IconTextLine
{
    private static readonly StringFormat Typographic = new(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces };

    /// <summary>The icon height for a font: a little under the line height, so icons sit with the text without crowding it.</summary>
    public static int IconSizeFor(Font font, float factor = 0.95f) => Math.Max(10, (int)Math.Round(font.Height * factor));

    public static float Measure(Graphics graphics, IReadOnlyList<LinePart> parts, Font font, int iconSize, float gap)
    {
        var width = 0f;
        foreach (var part in parts) width += PartWidth(graphics, part, font, iconSize, gap);
        return width;
    }

    /// <summary>
    /// Draws the parts left to right starting at <paramref name="x"/>, with the line's top at <paramref name="y"/> and the
    /// icons centred on the text height. Returns the x after the last part.
    /// </summary>
    public static float Draw(
        Graphics graphics,
        WeatherIconCache icons,
        IReadOnlyList<LinePart> parts,
        Font font,
        int iconSize,
        float gap,
        float x,
        float y,
        Color textColor,
        bool shadow)
    {
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var brush = new SolidBrush(textColor);
        using var shadowBrush = new SolidBrush(Color.FromArgb(170, 0, 0, 0));

        foreach (var part in parts)
        {
            switch (part)
            {
                case LinePart.Text text:
                    if (shadow) graphics.DrawString(text.Value, font, shadowBrush, x + 1, y + 1, Typographic);
                    graphics.DrawString(text.Value, font, brush, x, y, Typographic);
                    break;
                case LinePart.Icon icon:
                    DrawIcon(graphics, icons, icon, iconSize, (int)Math.Round(x), (int)Math.Round(y + (font.Height - iconSize * icon.Scale) / 2f));
                    break;
            }

            x += PartWidth(graphics, part, font, iconSize, gap);
        }

        return x;
    }

    /// <summary>Draws one icon with its top-left corner on whole pixels (the bitmap is exactly <paramref name="iconSize"/> big: 1:1, sharp).</summary>
    public static void DrawIcon(Graphics graphics, WeatherIconCache icons, LinePart.Icon icon, int iconSize, int x, int y)
    {
        var size = (int)Math.Round(iconSize * icon.Scale);
        var bitmap = icons.Get(icon.Id, size, icon.WindDegrees);
        var mode = graphics.InterpolationMode;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        var offset = graphics.PixelOffsetMode;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        if (icon.Alpha >= 0.999f)
        {
            graphics.DrawImage(bitmap, new Rectangle(x, y, size, size));
        }
        else
        {
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = Math.Clamp(icon.Alpha, 0f, 1f) });
            graphics.DrawImage(bitmap, new Rectangle(x, y, size, size), 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel, attributes);
        }

        graphics.InterpolationMode = mode;
        graphics.PixelOffsetMode = offset;
    }

    private static float PartWidth(Graphics graphics, LinePart part, Font font, int iconSize, float gap) => part switch
    {
        LinePart.Text text => graphics.MeasureString(text.Value, font, PointF.Empty, Typographic).Width + gap * 0.5f,
        LinePart.Icon icon => (float)Math.Round(iconSize * icon.Scale) + gap * 0.5f,
        LinePart.Gap space => gap * space.Gaps,
        _ => 0,
    };
}
