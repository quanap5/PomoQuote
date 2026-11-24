namespace HelloWorldBot.Mascot;

internal enum MascotAnimation
{
    Walk,
    Shout,
    Sleep,
}

/// <summary>Where each animation lives in the sprite sheet and how fast it plays. The sheet has 16x16 cells.</summary>
internal static class MascotSprites
{
    public const int CellSize = 16;

    /// <summary>The row of the sheet an animation uses (frames run left to right).</summary>
    public static int Row(MascotAnimation animation) => (int)animation;

    public static int FrameCount(MascotAnimation animation) => animation == MascotAnimation.Walk ? 4 : 2;

    /// <summary>Frames per second: about 10 for the walk, slower for the calm poses.</summary>
    public static double FramesPerSecond(MascotAnimation animation, bool slow) => animation switch
    {
        MascotAnimation.Walk => slow ? 5 : 10,
        MascotAnimation.Shout => 6,
        _ => 1.5,
    };

    /// <summary>Which frame to show after the animation has been playing for <paramref name="seconds"/>.</summary>
    public static int Frame(MascotAnimation animation, double seconds, bool slow = false) =>
        (int)(Math.Max(0, seconds) * FramesPerSecond(animation, slow) + 1e-6) % FrameCount(animation);

    /// <summary>
    /// The size in pixels of one drawn frame: the sprite is enlarged by a whole number (so pixels stay
    /// crisp, nearest-neighbour) of about <paramref name="sizeScale"/> times the display scale.
    /// </summary>
    public static int PixelSize(int sizeScale, int dpi) => CellSize * Math.Max(1, (int)Math.Round(sizeScale * dpi / 96.0));
}
