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
            // The Claude Code mark is a wide shape (24 by 15): its long side fills the box, its short side is at least half.
            Assert.True(Math.Min(width, height) >= size * 0.5, $"{id} at {size}px: visible box {width}x{height} is too narrow");
            Assert.True(peak >= 230, $"{id} at {size}px: peak brightness {peak} (want 230 or more)");
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
}
