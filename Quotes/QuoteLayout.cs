using System.Drawing.Text;

namespace PomoQuote.Quotes;

/// <summary>Where and how large the two texts are drawn, as chosen by <see cref="QuoteLayout.Fit"/>.</summary>
/// <param name="EnglishPixels">Font height in pixels for the English sentence.</param>
/// <param name="VietnamesePixels">Font height in pixels for the translation (0 when it is not shown).</param>
/// <param name="English">Rectangle for the English text, inside the area.</param>
/// <param name="Vietnamese">Rectangle for the translation (empty when it is not shown).</param>
/// <param name="EnglishLines">Lines the English text takes (never more than <see cref="QuoteLayout.MaxLines"/>).</param>
/// <param name="Truncated">True when even the smallest size did not fit and the text ends with an ellipsis.</param>
internal sealed record QuoteFit(float EnglishPixels, float VietnamesePixels, RectangleF English, RectangleF Vietnamese, int EnglishLines, bool Truncated);

/// <summary>
/// Chooses the largest font size at which the English sentence (at most three lines) and its translation
/// (at most three lines) both fit in the area, centred as one block. At the smallest size it truncates with
/// an ellipsis instead of overflowing, so the text can never reach the controls around it.
/// </summary>
internal static class QuoteLayout
{
    public const int MaxLines = 3;
    public const string EnglishFamily = "Segoe UI Semibold";
    public const string VietnameseFamily = "Segoe UI";

    private const float MaxEnglishPixelsAtScale1 = 80f;
    private const float MinEnglishPixelsAtScale1 = 13f;
    private const float MinVietnamesePixelsAtScale1 = 11f;
    private const float VietnameseToEnglish = 0.62f;
    private const float Step = 1f;

    /// <summary>The format used both to measure and to draw, so the two always agree.</summary>
    public static StringFormat CreateFormat() => new(StringFormat.GenericTypographic)
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Near,
        Trimming = StringTrimming.EllipsisWord,
        FormatFlags = StringFormat.GenericTypographic.FormatFlags | StringFormatFlags.LineLimit,
    };

    /// <param name="scale">Display scale (1 at 100%); sizes are designed at scale 1.</param>
    public static QuoteFit Fit(Graphics graphics, string english, string? vietnamese, SizeF area, float scale)
    {
        var minEnglish = MinEnglishPixelsAtScale1 * scale;
        var maxEnglish = Math.Max(minEnglish, Math.Min(MaxEnglishPixelsAtScale1 * scale, Math.Min(area.Height * 0.3f, area.Width * 0.06f)));
        var hasTranslation = !string.IsNullOrWhiteSpace(vietnamese);

        using var format = CreateFormat();
        var previousHint = graphics.TextRenderingHint;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        try
        {
            QuoteFit? smallest = null;
            for (var px = maxEnglish; px >= minEnglish - 0.01f; px -= Step)
            {
                var vietnamesePx = hasTranslation ? Math.Max(px * VietnameseToEnglish, MinVietnamesePixelsAtScale1 * scale) : 0f;
                var fit = Measure(graphics, format, english, vietnamese, area, px, vietnamesePx, scale);
                smallest = fit;
                if (fit is not null && !fit.Truncated) return fit;
            }

            return smallest!;
        }
        finally
        {
            graphics.TextRenderingHint = previousHint;
        }
    }

    // Returns the layout at the given sizes; Truncated says it did not fit in lines or height.
    private static QuoteFit Measure(Graphics graphics, StringFormat format, string english, string? vietnamese, SizeF area, float englishPx, float vietnamesePx, float scale)
    {
        using var englishFont = new Font(EnglishFamily, englishPx, FontStyle.Regular, GraphicsUnit.Pixel);
        var englishSize = graphics.MeasureString(english, englishFont, new SizeF(area.Width, 10000f), format);
        var englishLineHeight = englishFont.GetHeight(graphics);
        var englishLines = Math.Max(1, (int)Math.Round(englishSize.Height / englishLineHeight));

        var hasTranslation = vietnamesePx > 0 && !string.IsNullOrWhiteSpace(vietnamese);
        var vietnameseHeight = 0f;
        var vietnameseLines = 0;
        var vietnameseLineHeight = 0f;
        if (hasTranslation)
        {
            using var vietnameseFont = new Font(VietnameseFamily, vietnamesePx, FontStyle.Regular, GraphicsUnit.Pixel);
            var size = graphics.MeasureString(vietnamese!, vietnameseFont, new SizeF(area.Width, 10000f), format);
            vietnameseLineHeight = vietnameseFont.GetHeight(graphics);
            vietnameseLines = Math.Max(1, (int)Math.Round(size.Height / vietnameseLineHeight));
            vietnameseHeight = size.Height;
        }

        var gap = hasTranslation ? englishPx * 0.45f : 0f;
        var fits = englishLines <= MaxLines && vietnameseLines <= MaxLines &&
                   englishSize.Height + gap + vietnameseHeight <= area.Height;

        // At the smallest size, cap each part to its allowed lines and shrink the rest into the area.
        var englishHeight = Math.Min(englishSize.Height, MaxLines * englishLineHeight);
        var translationHeight = hasTranslation ? Math.Min(vietnameseHeight, MaxLines * vietnameseLineHeight) : 0f;
        if (!fits)
        {
            var available = Math.Max(0f, area.Height - gap);
            var total = englishHeight + translationHeight;
            if (total > available && total > 0)
            {
                var englishShare = englishHeight / total * available;
                englishHeight = Math.Max(englishLineHeight, (float)Math.Floor(englishShare / englishLineHeight) * englishLineHeight);
                englishHeight = Math.Min(englishHeight, available);
                translationHeight = hasTranslation
                    ? Math.Max(0f, (float)Math.Floor((available - englishHeight) / vietnameseLineHeight) * vietnameseLineHeight)
                    : 0f;
            }
        }

        var blockHeight = englishHeight + (translationHeight > 0 ? gap + translationHeight : 0f);
        var top = area.Height > blockHeight ? (area.Height - blockHeight) / 2f : 0f;

        // GDI+ drops a last line that is not completely inside the rectangle, and a measured height can fall a
        // hair short of the lines it holds, so each rectangle gets a little slack (less than a line, inside the band).
        var englishRoom = translationHeight > 0 ? gap * 0.9f : area.Height - top - englishHeight;
        var englishRect = new RectangleF(0, top, area.Width, englishHeight + Math.Clamp(englishRoom, 0f, englishLineHeight * 0.4f));
        var vietnameseTop = top + englishHeight + gap;
        var vietnameseRect = translationHeight > 0
            ? new RectangleF(0, vietnameseTop, area.Width, translationHeight + Math.Clamp(area.Height - vietnameseTop - translationHeight, 0f, vietnameseLineHeight * 0.4f))
            : RectangleF.Empty;
        return new QuoteFit(englishPx, hasTranslation ? vietnamesePx : 0f, englishRect, vietnameseRect, Math.Min(englishLines, MaxLines), !fits);
    }
}
