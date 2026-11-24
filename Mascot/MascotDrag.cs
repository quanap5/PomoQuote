using System.Drawing;

namespace HelloWorldBot.Mascot;

/// <summary>
/// Where the character may be picked up to and where it lands. Pure geometry on the walkways (one per monitor), so
/// it is unit tested: the character can never be dropped outside every monitor, and always lands on a taskbar strip.
/// </summary>
internal static class MascotDrag
{
    /// <summary>
    /// Pulls a feet position (bottom centre of the sprite) so the whole sprite of <paramref name="spriteSize"/> pixels
    /// stays inside the monitor nearest to it. With no walkways the point is returned unchanged.
    /// </summary>
    public static PointF ClampFeet(PointF feet, double spriteSize, IReadOnlyList<Walkway> route)
    {
        if (route.Count == 0) return feet;

        var walkway = Nearest(feet, route);
        var half = Math.Min(spriteSize / 2, (walkway.Right - walkway.Left) / 2.0);
        var x = Math.Clamp(feet.X, walkway.Left + half, walkway.Right - half);
        var top = walkway.Top == int.MinValue ? double.NegativeInfinity : walkway.Top + spriteSize;
        var bottom = walkway.Bottom == int.MaxValue ? double.PositiveInfinity : walkway.Bottom;
        var y = Math.Clamp(feet.Y, top, Math.Max(top, bottom));
        return new PointF((float)x, (float)y);
    }

    /// <summary>
    /// Where a character let go at <paramref name="feet"/> ends up: the walkway of the nearest monitor, the x it lands
    /// at (kept inside the strip) and how many pixels it has to fall (0 if it is already on or below the ground).
    /// </summary>
    public static (int Index, double X, double FallPixels) LandingTarget(PointF feet, double spriteSize, IReadOnlyList<Walkway> route)
    {
        var clamped = ClampFeet(feet, spriteSize, route);
        var index = IndexOfNearest(clamped, route);
        var walkway = route[index];
        var half = Math.Min(spriteSize / 2, (walkway.Right - walkway.Left) / 2.0);
        var x = Math.Clamp(clamped.X, walkway.Left + half, walkway.Right - half);
        return (index, x, Math.Max(0, walkway.GroundY - clamped.Y));
    }

    private static Walkway Nearest(PointF point, IReadOnlyList<Walkway> route) => route[IndexOfNearest(point, route)];

    private static int IndexOfNearest(PointF point, IReadOnlyList<Walkway> route)
    {
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < route.Count; i++)
        {
            var walkway = route[i];
            var top = walkway.Top == int.MinValue ? double.NegativeInfinity : walkway.Top;
            var bottom = walkway.Bottom == int.MaxValue ? double.PositiveInfinity : walkway.Bottom;
            var dx = Math.Max(Math.Max(walkway.Left - point.X, 0), point.X - walkway.Right);
            var dy = Math.Max(Math.Max(top - point.Y, 0), point.Y - bottom);
            var distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }
}
