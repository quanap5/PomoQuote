using System.Drawing;
using System.Drawing.Text;
using PomoQuote.Widget;

namespace PomoQuote.Tests;

/// <summary>The two date lines sit on the time row, centred on the clock digits, and end flush at the right content margin without touching the clock.</summary>
public sealed class DateBlockLayoutTests
{
    [Theory]
    [InlineData(90f)]
    [InlineData(118f)]
    [InlineData(130f)]
    public void Both_lines_end_exactly_at_the_right_content_margin(float clockWidth)
    {
        for (var line = 0; line < 2; line++)
        {
            var rect = DateBlockLayout.Line(line, clockWidth);

            Assert.Equal(UsageGeometry.WidgetWidth - UsageGeometry.Margin, rect.Right);
            Assert.Equal(DateBlockLayout.RightEdge, rect.Right);
            Assert.Equal(DateBlockLayout.Room(clockWidth), rect.Width);
        }
    }

    [Fact]
    public void The_right_edge_is_the_same_edge_as_the_quota_columns_and_the_timer_row()
    {
        var quotaRight = UsageGeometry.Columns(UsageGeometry.Margin + UsageGeometry.LogoColumn, UsageGeometry.CompactRowsWidth).Right;

        Assert.Equal(quotaRight, DateBlockLayout.RightEdge);
    }

    [Fact]
    public void The_block_is_centred_on_the_clock_digits_and_stays_inside_the_clock_row()
    {
        var first = DateBlockLayout.Line(0, 120);
        var second = DateBlockLayout.Line(1, 120);

        Assert.Equal(DateBlockLayout.DigitsCentre, (first.Top + second.Bottom) / 2f);
        Assert.Equal(first.Bottom, second.Top); // two lines, no gap, no overlap
        Assert.True(first.Top >= DateBlockLayout.ClockTop);
        Assert.True(second.Bottom <= DateBlockLayout.ClockTop + DateBlockLayout.ClockHeight);
    }

    private static (float Left, float Right, float Top, float Bottom) Ink(Bitmap bitmap, Color background)
    {
        int left = bitmap.Width, right = -1, top = bitmap.Height, bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (Math.Abs(p.R - background.R) + Math.Abs(p.G - background.G) + Math.Abs(p.B - background.B) < 90) continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }

        return (left, right + 1, top, bottom + 1);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void On_screen_the_widest_clock_ends_before_the_date_starts_and_the_digits_centre_matches_the_block(float unit)
    {
        var background = Color.FromArgb(28, 32, 40);
        using var probe = new Font("Segoe UI Light", 30f, FontStyle.Regular, GraphicsUnit.Pixel);
        if (probe.Name != "Segoe UI Light") return; // the design font is not installed on this machine
        using var bitmap = new Bitmap((int)(UsageGeometry.WidgetWidth * unit), (int)(60 * unit));
        float widest;
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Segoe UI Light", 30 * unit, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces })
        {
            graphics.Clear(background);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            widest = graphics.MeasureString("88:88:88", font, int.MaxValue, format).Width / unit;
            using var brush = new SolidBrush(Color.White);
            graphics.DrawString("88:88:88", font, brush, new RectangleF(UsageGeometry.Margin * unit, DateBlockLayout.ClockTop * unit, UsageGeometry.WidgetWidth * unit, DateBlockLayout.ClockHeight * unit), new StringFormat(StringFormatFlags.NoWrap));
        }

        var ink = Ink(bitmap, background);
        var date = DateBlockLayout.Line(0, widest);

        // The widest clock text ends before the date's room starts, at every scale.
        Assert.True(ink.Right / unit < date.Left, $"the clock ink ends at {ink.Right / unit:0.0}, the date room starts at {date.Left:0.0}");
        // The digits' centre is where the block is centred (within 2 logical px).
        var centre = (ink.Top + ink.Bottom) / 2f / unit;
        Assert.InRange(centre, DateBlockLayout.DigitsCentre - 2, DateBlockLayout.DigitsCentre + 2);
    }

    [Theory]
    [InlineData(1f, 12f, "Thứ Ba, 24/11/2026")]
    [InlineData(1.25f, 11.5f, "Âm lịch: 16/10 Bính Ngọ")]
    [InlineData(1.5f, 10.5f, "Âm lịch: 1/6 nhuận Ất Tỵ")]
    [InlineData(2f, 10f, "Chủ Nhật, 28/12/2026")]
    public void Right_aligned_text_ends_flush_at_the_margin_at_any_size_and_scale(float unit, float pixels, string text)
    {
        var background = Color.FromArgb(28, 32, 40);
        using var probe = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        if (probe.Name != "Segoe UI") return;
        using var bitmap = new Bitmap((int)(UsageGeometry.WidgetWidth * unit), (int)(60 * unit));
        var rect = DateBlockLayout.Line(0, 118);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Segoe UI", pixels * unit, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(Color.White))
        using (var format = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, Alignment = StringAlignment.Far })
        {
            graphics.Clear(background);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.DrawString(text, font, brush, new RectangleF(rect.X * unit, rect.Y * unit, rect.Width * unit, rect.Height * unit), format);
        }

        var ink = Ink(bitmap, background);

        Assert.InRange(ink.Right / unit, DateBlockLayout.RightEdge - 3, DateBlockLayout.RightEdge + 0.5f); // flush with the right margin
        Assert.True(ink.Left / unit >= rect.Left, "the text stays inside its room");
        Assert.True(ink.Top / unit >= rect.Top - 1 && ink.Bottom / unit <= rect.Bottom + 1, "the text stays inside its row");
    }

    [Fact]
    public void The_drawing_code_uses_the_layout_and_right_alignment()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "PomoQuote.csproj"))) root = Path.GetDirectoryName(root);
        var source = File.ReadAllText(Path.Combine(root!, "WidgetView.cs"));

        Assert.Contains("DateBlockLayout.Line(i, widestClockLogical)", source);
        Assert.Contains("Alignment = StringAlignment.Far", source);
        Assert.Contains("DateBlockLayout.Room(widestClockLogical)", source);
    }
}
