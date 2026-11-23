using System.Drawing.Drawing2D;

namespace HelloWorldBot;

/// <summary>A bundled wallpaper: the menu caption and the file name inside Assets/Wallpapers.</summary>
internal sealed record Wallpaper(string Name, string FileName);

/// <summary>
/// Owns the current wallpaper image and paints it to cover a rectangle (aspect ratio kept, extra
/// cropped, never stretched) under a dark overlay that keeps the text and clock legible.
/// </summary>
internal sealed class WallpaperBackground : IDisposable
{
    public static readonly IReadOnlyList<Wallpaper> Available =
    [
        new("Night Sky", "night-sky.jpg"),
        new("Foggy Forest", "foggy-forest.jpg"),
        new("Mountain Lake", "mountain-lake.jpg"),
    ];

    private const int OverlayAlpha = 110;

    private Image? _image;

    public bool HasImage => _image is not null;

    /// <summary>
    /// Loads a wallpaper and releases the previous one. The file is copied into memory so it is
    /// never left locked. On failure nothing changes and the exception is thrown to the caller.
    /// </summary>
    public void Load(Wallpaper wallpaper)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Wallpapers", wallpaper.FileName);
        using var file = Image.FromFile(path);
        var copy = new Bitmap(file);
        Clear();
        _image = copy;
    }

    public void Clear()
    {
        _image?.Dispose();
        _image = null;
    }

    /// <summary>The part of an image (in image pixels) that covers a target of the given size.</summary>
    internal static RectangleF CoverSourceRectangle(SizeF image, SizeF target)
    {
        var scale = Math.Max(target.Width / image.Width, target.Height / image.Height);
        var width = target.Width / scale;
        var height = target.Height / scale;
        return new RectangleF((image.Width - width) / 2f, (image.Height - height) / 2f, width, height);
    }

    public void Paint(Graphics graphics, Rectangle target)
    {
        if (_image is null || target.Width <= 0 || target.Height <= 0) return;

        var source = CoverSourceRectangle(_image.Size, target.Size);
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        graphics.DrawImage(_image, target, source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel);

        using var overlay = new SolidBrush(Color.FromArgb(OverlayAlpha, 0, 0, 0));
        graphics.FillRectangle(overlay, target);
    }

    public void Dispose() => Clear();
}
