using System.Drawing;

namespace HelloWorldBot.Mascot;

/// <summary>One monitor as the mascot needs to know it. <see cref="Id"/> is the Windows device name.</summary>
internal sealed record MonitorInfo(string Id, Rectangle Bounds, Rectangle WorkArea, int Dpi = 96, bool Primary = false);

/// <summary>
/// The strip of one monitor the robot walks on: x from <see cref="Left"/> to <see cref="Right"/>, with its
/// feet on <see cref="GroundY"/> (the top edge of a bottom taskbar, or the bottom of the screen). <see cref="Top"/>
/// and <see cref="Bottom"/> are the monitor's own top and bottom, the limits for dragging.
/// </summary>
internal sealed record Walkway(string MonitorId, int Left, int Right, int GroundY, int Dpi, int Top = int.MinValue, int Bottom = int.MaxValue);

/// <summary>Turns the monitor layout into the walkways the mascot travels over, left to right.</summary>
internal static class MascotRoute
{
    /// <summary>
    /// Builds one walkway per monitor, ordered by position (left to right, then top to bottom, so monitors
    /// above or below each other follow one another). The ground is the bottom of the monitor's work area:
    /// with a bottom taskbar that is the taskbar's top edge; with the taskbar on another edge or hidden the
    /// work area ends at the bottom of the screen. If the filter matches no monitor all of them are used.
    /// </summary>
    public static IReadOnlyList<Walkway> Build(IEnumerable<MonitorInfo> monitors, IReadOnlyCollection<string>? enabledIds = null)
    {
        var all = monitors.ToList();
        var chosen = enabledIds is { Count: > 0 } && all.Any(m => enabledIds.Contains(m.Id))
            ? all.Where(m => enabledIds.Contains(m.Id)).ToList()
            : all;

        return chosen
            .OrderBy(m => m.Bounds.Left)
            .ThenBy(m => m.Bounds.Top)
            .Select(m => new Walkway(m.Id, m.WorkArea.Left, m.WorkArea.Right, m.WorkArea.Bottom, m.Dpi, m.Bounds.Top, m.Bounds.Bottom))
            .ToList();
    }
}
