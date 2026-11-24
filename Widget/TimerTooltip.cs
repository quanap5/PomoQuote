using PomoQuote.Pomodoro;

namespace PomoQuote.Widget;

/// <summary>The tooltip of the timer's progress bar: the phase, what has passed, what is left and when it ends.</summary>
internal static class TimerTooltip
{
    public static string Text(PomodoroPhase phase, PomodoroRunState state, TimeSpan duration, TimeSpan remaining, DateTime now)
    {
        var name = PomodoroController.PhaseName(phase);
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        if (remaining > duration) remaining = duration;
        var elapsed = duration - remaining;

        if (state == PomodoroRunState.Idle) return $"{name}: not started\nLength {Clock(duration)}";

        var lines = new List<string>
        {
            state == PomodoroRunState.Paused ? $"{name} (paused)" : name,
            $"Elapsed {Clock(elapsed)} of {Clock(duration)}",
            $"Remaining {Clock(remaining)}",
        };
        lines.Add(state == PomodoroRunState.Paused
            ? "It ends when you resume"
            : $"Ends at {now + remaining:HH:mm:ss}");
        return string.Join("\n", lines);
    }

    private static string Clock(TimeSpan span)
    {
        var seconds = (int)Math.Ceiling(span.TotalSeconds);
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }
}
