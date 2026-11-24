using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PomoQuote.AiUsage;

/// <summary>Which background the icon is drawn on: the widget is dark, the settings dialog follows the system (light).</summary>
public enum IconBackground
{
    Dark,
    Light,
}

/// <summary>
/// The provider icons (Assets/Providers, embedded in the program: nothing is fetched at run time). Each file is loaded once,
/// scaled once per pixel size with high quality (two steps for a big reduction, so small icons stay crisp) and kept until
/// the owner is disposed. A missing or damaged icon gives null, and the caller then shows the name alone.
/// </summary>
internal sealed class ProviderIcons : IDisposable
{
    private readonly Func<string, Stream?> _open;
    private readonly Dictionary<string, Bitmap?> _originals = [];
    private readonly Dictionary<(string Key, int Size), Bitmap?> _scaled = [];
    private readonly object _gate = new();
    private bool _disposed;

    /// <param name="open">Opens the PNG of a file key such as "codex-on-dark"; null when there is none. The default reads the embedded resources.</param>
    public ProviderIcons(Func<string, Stream?>? open = null) => _open = open ?? OpenEmbedded;

    private static Stream? OpenEmbedded(string key) => typeof(ProviderIcons).Assembly.GetManifestResourceStream($"PomoQuote.providers.{key}.png");

    /// <summary>The file key for a provider and background, e.g. "codex-on-dark".</summary>
    public static string Key(string providerId, IconBackground background) => $"{providerId.ToLowerInvariant()}-on-{(background == IconBackground.Dark ? "dark" : "light")}";

    /// <summary>The icon scaled to <paramref name="pixels"/> by <paramref name="pixels"/> (fit inside, centred), or null when there is none.</summary>
    public Bitmap? Get(string providerId, IconBackground background, int pixels)
    {
        if (providerId.Length == 0 || pixels < 4) return null;
        var key = Key(providerId, background);
        lock (_gate)
        {
            if (_disposed) return null;
            if (_scaled.TryGetValue((key, pixels), out var cached)) return cached;
            var original = Original(key);
            var scaled = original is null ? null : Scale(original, pixels);
            _scaled[(key, pixels)] = scaled;
            return scaled;
        }
    }

    private Bitmap? Original(string key)
    {
        if (_originals.TryGetValue(key, out var cached)) return cached;
        Bitmap? bitmap = null;
        try
        {
            using var stream = _open(key);
            if (stream is not null)
            {
                using var decoded = new Bitmap(stream);
                if (decoded.Width >= 4 && decoded.Height >= 4) bitmap = CropToVisible(decoded); // detach from the stream and drop the transparent padding
            }
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or IOException or OutOfMemoryException or InvalidOperationException)
        {
            bitmap = null; // a damaged file: name only
        }

        _originals[key] = bitmap;
        return bitmap;
    }

    /// <summary>
    /// A copy of <paramref name="source"/> cut to the visible part (the pixels that are not almost transparent). The official files
    /// keep wide empty margins, which made the mark small in its box; the files themselves are never changed, only this copy in memory.
    /// </summary>
    internal static Bitmap CropToVisible(Bitmap source)
    {
        var area = new Rectangle(0, 0, source.Width, source.Height);
        var data = source.LockBits(area, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        int left = source.Width, top = source.Height, right = -1, bottom = -1;
        try
        {
            var row = new byte[data.Stride];
            for (var y = 0; y < source.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
                for (var x = 0; x < source.Width; x++)
                {
                    if (row[x * 4 + 3] < 16) continue; // alpha
                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < top) top = y;
                    if (y > bottom) bottom = y;
                }
            }
        }
        finally
        {
            source.UnlockBits(data);
        }

        if (right < left || bottom < top) return new Bitmap(source); // nothing visible: keep as it is
        var visible = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        return source.Clone(visible, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    }

    private static Bitmap Scale(Bitmap source, int pixels)
    {
        var current = source;
        var owned = false;
        // Halve until close to the target: one big step from 700 px to 14 px aliases.
        while (Math.Max(current.Width, current.Height) > pixels * 2)
        {
            var next = Draw(current, Math.Max(pixels, Math.Max(current.Width, current.Height) / 2));
            if (owned) current.Dispose();
            current = next;
            owned = true;
        }

        var result = Draw(current, pixels);
        if (owned) current.Dispose();
        return result;
    }

    private static Bitmap Draw(Bitmap source, int box)
    {
        var scale = Math.Min((double)box / source.Width, (double)box / source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var bitmap = new Bitmap(box, box, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        using var attributes = new System.Drawing.Imaging.ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY); // no halo at the edge
        graphics.DrawImage(source, new Rectangle((box - width) / 2, (box - height) / 2, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return bitmap;
    }

    /// <summary>How many scaled images are alive (for the tests that check nothing piles up).</summary>
    internal int CachedCount
    {
        get
        {
            lock (_gate) return _scaled.Count;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var bitmap in _scaled.Values) bitmap?.Dispose();
            foreach (var bitmap in _originals.Values) bitmap?.Dispose();
            _scaled.Clear();
            _originals.Clear();
        }
    }
}
