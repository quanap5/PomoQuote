using System.Drawing;

namespace PomoQuote.Tests;

public sealed class AppIconTests
{
    private static readonly string IcoFile = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "Assets", "Logo", "pomoquote.ico"));

    private static readonly int[] RequiredSizes = [16, 20, 24, 32, 40, 48, 64, 256];

    private static List<(int Size, int Bits)> ReadDirectory(byte[] ico)
    {
        Assert.Equal(0, BitConverter.ToUInt16(ico, 0));
        Assert.Equal(1, BitConverter.ToUInt16(ico, 2));
        var count = BitConverter.ToUInt16(ico, 4);
        var list = new List<(int, int)>();
        for (var i = 0; i < count; i++)
        {
            var e = 6 + i * 16;
            var width = ico[e] == 0 ? 256 : ico[e];
            var height = ico[e + 1] == 0 ? 256 : ico[e + 1];
            Assert.Equal(width, height);
            list.Add((width, BitConverter.ToUInt16(ico, e + 6)));
        }

        return list;
    }

    [Fact]
    public void The_ico_file_holds_every_required_size_with_32_bit_colour()
    {
        var directory = ReadDirectory(File.ReadAllBytes(IcoFile));

        Assert.Equal(RequiredSizes, directory.Select(entry => entry.Size).OrderBy(size => size).ToArray());
        Assert.All(directory, entry => Assert.Equal(32, entry.Bits));
    }

    [Fact]
    public void The_embedded_icon_is_the_file_in_the_repository()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(AppIcon.ResourceName)!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        Assert.Equal(File.ReadAllBytes(IcoFile), memory.ToArray());
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(64)]
    public void Every_size_loads_at_its_own_size_and_is_not_blank(int size)
    {
        using var icon = AppIcon.Load(new Size(size, size));
        using var bitmap = icon.ToBitmap();

        Assert.Equal(size, icon.Width);
        Assert.Equal(size, bitmap.Width);
        var opaque = 0;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++) if (bitmap.GetPixel(x, y).A > 200) opaque++;
        }

        // The logo fills most of the square at every size (readable, not a speck).
        Assert.True(opaque > size * size * 0.5, $"only {opaque} opaque pixels at {size}px");
    }

    [Fact]
    public void The_small_logo_still_shows_its_warm_and_cool_halves_and_a_light_centre()
    {
        using var bitmap = AppIcon.LoadBitmap(16);

        var left = bitmap.GetPixel(3, 8);
        var right = bitmap.GetPixel(12, 8);
        var centre = bitmap.GetPixel(8, 4);

        Assert.True(left.R > left.B + 40, "left half should be warm (focus)");
        Assert.True(right.G > right.R + 20, "right half should be cool (rest)");
        Assert.True(centre.R > 200 && centre.G > 200 && centre.B > 200, "the centre disc is light");
    }

    [Fact]
    public void The_full_icon_and_a_bitmap_can_be_loaded_and_disposed_repeatedly_without_error()
    {
        for (var i = 0; i < 200; i++)
        {
            using var icon = AppIcon.Load();
            using var bitmap = AppIcon.LoadBitmap(64);
            Assert.True(icon.Width > 0 && bitmap.Width == 64);
        }
    }
}
