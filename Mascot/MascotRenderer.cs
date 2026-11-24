using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PomoQuote.Mascot;

/// <summary>
/// Cuts frames out of the sprite sheet and enlarges them by a whole factor with nearest-neighbour scaling (no blur at
/// any size). Every enlarged frame, for each facing, is made once and kept: drawing a frame is a lookup.
/// </summary>
internal sealed class MascotRenderer : IDisposable
{
    private readonly Bitmap _sheet;
    private readonly int _frameSize;
    private readonly Dictionary<(int Row, int Frame, bool Right), Bitmap> _cache = [];
    private int _cachedSize;

    /// <summary>Loads the sprite sheet; throws if the file is missing or not an image.</summary>
    public MascotRenderer(string sheetPath, int frameSize)
    {
        using var file = Image.FromFile(sheetPath);
        _sheet = new Bitmap(file); // a copy, so the file is never left locked
        _frameSize = frameSize;
    }

    /// <summary>How many enlarged frames are cached right now.</summary>
    public int CachedFrames => _cache.Count;

    /// <summary>
    /// The frame for <paramref name="pose"/> as a premultiplied-alpha bitmap (what a layered window needs), mirrored when
    /// the character faces left. The bitmap belongs to the renderer: do not dispose it.
    /// </summary>
    public Bitmap Render(MascotPose pose, int sizeScale, int dpi)
    {
        var size = MascotSprites.PixelSize(_frameSize, sizeScale, dpi);
        if (size != _cachedSize) ClearCache(size);

        var key = (pose.Row, pose.Frame, pose.FacingRight);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.CompositingMode = CompositingMode.SourceCopy;

            if (!pose.FacingRight)
            {
                graphics.TranslateTransform(size, 0);
                graphics.ScaleTransform(-1, 1);
            }

            var source = new Rectangle(pose.Frame * _frameSize, pose.Row * _frameSize, _frameSize, _frameSize);
            graphics.DrawImage(_sheet, new Rectangle(0, 0, size, size), source, GraphicsUnit.Pixel);
        }

        _cache[key] = bitmap;
        return bitmap;
    }

    private void ClearCache(int newSize)
    {
        foreach (var bitmap in _cache.Values) bitmap.Dispose();
        _cache.Clear();
        _cachedSize = newSize;
    }

    public void Dispose()
    {
        ClearCache(0);
        _sheet.Dispose();
    }
}
