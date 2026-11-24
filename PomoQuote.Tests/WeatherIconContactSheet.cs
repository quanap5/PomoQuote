using System.Drawing;
using System.Drawing.Drawing2D;
using PomoQuote.Weather;

namespace PomoQuote.Tests;

/// <summary>
/// Draws the contact sheet of every weather icon. It is not a check: it is the reproducible way to make
/// <c>Assets/Weather/contact-sheet.png</c>. Run it with the environment variable
/// <c>POMOQUOTE_ICON_SHEET</c> set to the output path:
/// <code>$env:POMOQUOTE_ICON_SHEET = "Assets/Weather/contact-sheet.png"; dotnet test PomoQuote.Tests --filter ContactSheet</code>
/// Without the variable it does nothing.
/// </summary>
public class WeatherIconContactSheet
{
    [Fact]
    public void Draw_the_contact_sheet_when_asked_to()
    {
        var path = Environment.GetEnvironmentVariable("POMOQUOTE_ICON_SHEET");
        if (string.IsNullOrEmpty(path)) return;

        using var cache = new WeatherIconCache();
        var icons = Enum.GetValues<WeatherIconId>();
        const int columns = 7;
        const int big = 64;
        const int cell = 96;
        var rows = (icons.Length + columns - 1) / columns;
        var smallBand = 100;
        var width = columns * cell + 20;
        var height = rows * (cell + 24) + smallBand * 2 + 60;
        using var sheet = new Bitmap(width, height);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(58, 66, 84));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var font = new Font("Segoe UI", 10, FontStyle.Regular, GraphicsUnit.Pixel);
        using var title = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);

        g.DrawString("Weather icons at 64 px (day and night variants where they exist)", title, Brushes.White, 10, 6);
        for (var i = 0; i < icons.Length; i++)
        {
            var column = i % columns;
            var row = i / columns;
            var x = 10 + column * cell;
            var y = 28 + row * (cell + 24);
            var background = (column + row) % 2 == 0 ? Color.FromArgb(214, 222, 232) : Color.FromArgb(40, 44, 54);
            using (var brush = new SolidBrush(background)) g.FillRectangle(brush, x + 4, y, big + 24, big + 8);
            g.DrawImage(cache.Get(icons[i], big, icons[i] == WeatherIconId.WindDirection ? 45 : 0), x + 16, y + 4);
            g.DrawString(icons[i].ToString(), font, Brushes.LightGray, x + 2, y + big + 12);
        }

        // The same icons at the sizes the app really uses (100%-200% display scale), on light and dark backgrounds.
        var bandTop = 28 + rows * (cell + 24) + 10;
        foreach (var (label, background, offset) in new[] { ("light background: 16, 20, 24, 32 px", Color.FromArgb(240, 240, 240), 0), ("dark background: 16, 20, 24, 32 px", Color.FromArgb(28, 32, 40), smallBand) })
        {
            using (var brush = new SolidBrush(background)) g.FillRectangle(brush, 10, bandTop + offset, width - 20, smallBand - 10);
            using var textBrush = new SolidBrush(background.GetBrightness() > 0.5 ? Color.FromArgb(40, 40, 40) : Color.White);
            g.DrawString(label, font, textBrush, 14, bandTop + offset + 2);
            for (var i = 0; i < icons.Length; i++)
            {
                var sizes = new[] { 16, 20, 24, 32 };
                var size = sizes[i % 4];
                var y = bandTop + offset + 18 + (i / 14) * 36;
                var xx = 14 + (i % 14) * 54;
                g.DrawImage(cache.Get(icons[i], size, icons[i] == WeatherIconId.WindDirection ? 45 : 0), xx, y);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        sheet.Save(path);
    }
}
