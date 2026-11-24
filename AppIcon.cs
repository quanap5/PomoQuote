using System.Reflection;

namespace PomoQuote;

/// <summary>
/// The PomoQuote logo as a Windows icon: the multi-size .ico (16 to 256 px) embedded in the assembly, the same
/// file the executable carries. Callers own, and must dispose, what they get.
/// </summary>
internal static class AppIcon
{
    internal const string ResourceName = "PomoQuote.pomoquote.ico";

    /// <summary>The icon with all its sizes; Windows picks the right one for the title bar, taskbar and Alt+Tab.</summary>
    public static Icon Load()
    {
        using var stream = OpenStream();
        return new Icon(stream);
    }

    /// <summary>The image of the icon that best fits <paramref name="size"/> (for the tray and small UI).</summary>
    public static Icon Load(Size size)
    {
        using var stream = OpenStream();
        return new Icon(stream, size);
    }

    /// <summary>A bitmap of the logo at the given pixel size, taken from the closest icon image.</summary>
    public static Bitmap LoadBitmap(int pixels)
    {
        using var icon = Load(new Size(pixels, pixels));
        return icon.ToBitmap();
    }

    private static Stream OpenStream() =>
        typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {ResourceName}.");
}