using System.Drawing;
using PomoQuote.Quotes;

namespace PomoQuote.Tests;

/// <summary>The sentence text must always fit its band: at most three lines, never outside it, translation below the sentence.</summary>
public sealed class QuoteLayoutTests : IDisposable
{
    private readonly Bitmap _bitmap = new(16, 16);
    private readonly Graphics _graphics;

    public QuoteLayoutTests() => _graphics = Graphics.FromImage(_bitmap);

    public void Dispose()
    {
        _graphics.Dispose();
        _bitmap.Dispose();
    }

    // Band areas in pixels: the minimum window, the default window, a large window; then 150% and 200% scale.
    public static TheoryData<int, int, float> Areas => new()
    {
        { 380, 130, 1f },
        { 750, 150, 1f },
        { 1700, 330, 1f },
        { 570, 195, 1.5f },
        { 1500, 300, 2f },
    };

    [Theory]
    [MemberData(nameof(Areas))]
    public void Every_sentence_fits_in_three_lines_inside_the_band_without_overlapping_its_translation(int width, int height, float scale)
    {
        var area = new SizeF(width, height);

        foreach (var quote in new BundledQuoteProvider().GetAll())
        {
            var fit = QuoteLayout.Fit(_graphics, quote.English, quote.Vietnamese, area, scale);
            var detail = $"{quote.English} @ {width}x{height}";

            Assert.False(fit.Truncated, detail);
            Assert.InRange(fit.EnglishLines, 1, QuoteLayout.MaxLines);
            Assert.True(fit.EnglishPixels >= 13 * scale - 0.01f, detail);
            Assert.True(fit.VietnamesePixels >= 11 * scale - 0.01f, detail);
            Assert.True(fit.English.Top >= 0 && fit.English.Bottom <= area.Height + 0.5f, detail);
            Assert.True(fit.Vietnamese.Bottom <= area.Height + 0.5f, detail);
            Assert.True(fit.English.Bottom <= fit.Vietnamese.Top, detail);
            Assert.True(fit.English.Width <= area.Width && fit.Vietnamese.Width <= area.Width, detail);

            // What is drawn into those rectangles shows the whole text: no line is cut off or replaced by an ellipsis.
            Assert.Equal(quote.English.Length, CharactersShown(quote.English, QuoteLayout.EnglishFamily, fit.EnglishPixels, fit.English, out var lines));
            Assert.Equal(fit.EnglishLines, lines);
            Assert.Equal(quote.Vietnamese.Length, CharactersShown(quote.Vietnamese, QuoteLayout.VietnameseFamily, fit.VietnamesePixels, fit.Vietnamese, out _));
        }
    }

    private int CharactersShown(string text, string family, float pixels, RectangleF rect, out int lines)
    {
        _graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var font = new Font(family, pixels, FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = QuoteLayout.CreateFormat();
        _graphics.MeasureString(text, font, rect.Size, format, out var characters, out lines);
        return characters;
    }

    [Fact]
    public void A_short_sentence_is_shown_larger_than_a_long_one()
    {
        var area = new SizeF(750, 150);

        var shortFit = QuoteLayout.Fit(_graphics, "Time is money.", "Thời gian là vàng bạc.", area, 1f);
        var longFit = QuoteLayout.Fit(_graphics,
            "Permanence, perseverance and persistence in spite of all obstacles: it is this that distinguishes the strong soul from the weak.",
            "Sự bền bỉ, kiên trì và không bỏ cuộc bất chấp mọi trở ngại: đó là điều phân biệt tâm hồn mạnh mẽ với tâm hồn yếu đuối.",
            area, 1f);

        Assert.True(shortFit.EnglishPixels > longFit.EnglishPixels);
        Assert.True(shortFit.EnglishLines <= longFit.EnglishLines);
    }

    [Fact]
    public void Without_a_translation_the_sentence_gets_the_whole_band()
    {
        var area = new SizeF(750, 150);

        var fit = QuoteLayout.Fit(_graphics, "Haste makes waste.", null, area, 1f);

        Assert.Equal(0f, fit.VietnamesePixels);
        Assert.Equal(0f, fit.Vietnamese.Height);
        Assert.False(fit.Truncated);
    }

    [Fact]
    public void Text_that_cannot_fit_even_at_the_smallest_size_is_cut_inside_the_band_instead_of_overflowing()
    {
        var area = new SizeF(120, 40);
        var text = string.Join(' ', Enumerable.Repeat("overflowing", 40));

        var fit = QuoteLayout.Fit(_graphics, text, text, area, 1f);

        Assert.True(fit.Truncated);
        Assert.True(fit.English.Bottom <= area.Height + 0.5f);
        Assert.True(fit.Vietnamese.Bottom <= area.Height + 0.5f);
        Assert.True(fit.English.Bottom <= fit.Vietnamese.Top + 0.01f || fit.Vietnamese.Height == 0f);
    }

    [Fact]
    public void Fitting_does_not_change_the_text_rendering_state_of_the_graphics()
    {
        var before = _graphics.TextRenderingHint;

        QuoteLayout.Fit(_graphics, "Hello", "Xin chào", new SizeF(400, 120), 1f);

        Assert.Equal(before, _graphics.TextRenderingHint);
    }
}
