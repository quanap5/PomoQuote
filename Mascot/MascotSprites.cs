namespace PomoQuote.Mascot;

/// <summary>Sizing of the drawn character. The sprite sheet is a grid of square cells (24 px by default).</summary>
internal static class MascotSprites
{
    /// <summary>
    /// The size in pixels of one drawn frame: the sprite is enlarged by a whole number (so pixels stay
    /// crisp, nearest-neighbour) of about <paramref name="sizeScale"/> times the display scale.
    /// </summary>
    public static int PixelSize(int frameSize, int sizeScale, int dpi) => frameSize * Math.Max(1, (int)Math.Round(sizeScale * dpi / 96.0));
}
