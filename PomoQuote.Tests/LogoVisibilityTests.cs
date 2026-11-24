using System.Drawing;
using PomoQuote.AiUsage;

namespace PomoQuote.Tests;

/// <summary>The logos must be easy to recognise on the dark widget: the visible mark fills its 18 to 20 px box and is bright, whatever padding the official file has.</summary>
public sealed class LogoVisibilityTests
{
    private static (int Width, int Height, int Peak, double Coverage) Measure(Bitmap icon)
    {
        int left = icon.Width, top = icon.Height, right = -1, bottom = -1, peak = 0;
        var coverage = 0.0;
        for (var y = 0; y < icon.Height; y++)
        {
            for (var x = 0; x < icon.Width; x++)
            {
                var alpha = icon.GetPixel(x, y).A;
                peak = Math.Max(peak, alpha);
                coverage += alpha / 255.0;
                if (alpha <= 40) continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }

        return right < left ? (0, 0, peak, coverage) : (right - left + 1, bottom - top + 1, peak, coverage);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    [InlineData("antigravity")]
    public void Each_logo_fills_at_least_18_px_of_its_box_and_is_bright_on_dark(string id)
    {
        using var icons = new ProviderIcons();

        foreach (var size in new[] { 20, 25, 30, 40 })
        {
            var icon = icons.Get(id, IconBackground.Dark, size)!;
            var (width, height, peak, coverage) = Measure(icon);
            var minimum = (int)Math.Round(size * 0.9);

            Assert.True(Math.Max(width, height) >= minimum, $"{id} at {size}px: visible box {width}x{height} (want a side of at least {minimum})");
            // The long side fills the box and the short side is at least half of it (the marks are square or only a little wider than tall).
            Assert.True(Math.Min(width, height) >= size * 0.5, $"{id} at {size}px: visible box {width}x{height} is too narrow");
            Assert.True(peak >= 230, $"{id} at {size}px: peak opacity {peak} (the alpha peak, not the colour brightness; want 230 or more)");
            Assert.True(coverage > size * 1.2, $"{id} at {size}px: only {coverage:0.0} px of ink");
        }
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    [InlineData("antigravity")]
    public void The_light_versions_for_the_settings_dialog_are_normalised_the_same_way(string id)
    {
        using var icons = new ProviderIcons();

        var icon = icons.Get(id, IconBackground.Light, 16)!;
        var (width, height, peak, _) = Measure(icon);

        Assert.True(Math.Max(width, height) >= 14, $"{id}: visible box {width}x{height}");
        Assert.True(peak >= 230, $"{id}: peak {peak}");
    }

    [Fact]
    public void Cropping_works_on_the_decoded_copy_and_leaves_the_packaged_file_untouched()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "PomoQuote.csproj"))) root = Path.GetDirectoryName(root);
        var path = Path.Combine(root!, "Assets", "Providers", "codex-on-dark.png");
        var before = File.ReadAllBytes(path);
        using var original = new Bitmap(path);

        using var cropped = ProviderIcons.CropToVisible(original);

        Assert.True(cropped.Width < original.Width && cropped.Height < original.Height, "the padding was cut");
        Assert.Equal(before, File.ReadAllBytes(path)); // the official file is byte for byte the same
        using var empty = new Bitmap(8, 8);
        using var kept = ProviderIcons.CropToVisible(empty);
        Assert.Equal(8, kept.Width); // nothing visible: left as it is
    }

    /// <summary>The relative luminance of an sRGB colour (WCAG 2.x).</summary>
    private static double Luminance(Color c)
    {
        static double Channel(int v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>The contrast ratio of a logo's own colour against the dark widget panel (28, 32, 40).</summary>
    private static double ContrastOnPanel(Bitmap icon)
    {
        var panel = Luminance(Color.FromArgb(28, 32, 40));
        double sum = 0;
        var solid = 0;
        for (var y = 0; y < icon.Height; y++)
        {
            for (var x = 0; x < icon.Width; x++)
            {
                var pixel = icon.GetPixel(x, y);
                if (pixel.A < 240) continue; // a solid pixel of the mark, not an anti-aliased edge
                sum += Luminance(pixel);
                solid++;
            }
        }

        var mean = solid == 0 ? 0 : sum / solid; // the typical solid colour of the mark, so one bright overshoot pixel cannot hide a dark logo
        return (Math.Max(mean, panel) + 0.05) / (Math.Min(mean, panel) + 0.05);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    [InlineData("antigravity")]
    public void Each_logo_has_a_contrast_of_at_least_4_5_to_1_against_the_dark_panel(string id)
    {
        using var icons = new ProviderIcons();

        foreach (var size in new[] { 20, 25, 30, 40 })
        {
            var contrast = ContrastOnPanel(icons.Get(id, IconBackground.Dark, size)!);

            // Measured: the Claude Spark's Clay colour (217, 119, 87) is about 5.2:1 on (28, 32, 40); the white marks are about 15:1.
            Assert.True(contrast >= 4.5, $"{id} at {size}px: contrast {contrast:0.00}:1 against the panel (want 4.5:1 or more)");
        }
    }
}