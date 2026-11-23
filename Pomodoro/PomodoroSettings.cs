namespace HelloWorldBot.Pomodoro;

internal enum PomodoroPhase
{
    Work,
    ShortBreak,
    LongBreak,
}

/// <summary>
/// User-adjustable Pomodoro options. Defaults are the classic 25 / 5 / 15 minutes with a long break
/// after 4 work sessions; sound and auto-start are on.
/// </summary>
internal sealed record PomodoroSettings(
    int WorkMinutes = 25,
    int ShortBreakMinutes = 5,
    int LongBreakMinutes = 15,
    int SessionsBeforeLongBreak = 4,
    bool SoundEnabled = true,
    bool AutoStartNext = true)
{
    public const int MinMinutes = 1;
    public const int MaxMinutes = 180;
    public const int MinSessions = 1;
    public const int MaxSessions = 12;

    public static PomodoroSettings Default { get; } = new();

    /// <summary>True when every number is inside its allowed range.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid =>
        InRange(WorkMinutes, MinMinutes, MaxMinutes) &&
        InRange(ShortBreakMinutes, MinMinutes, MaxMinutes) &&
        InRange(LongBreakMinutes, MinMinutes, MaxMinutes) &&
        InRange(SessionsBeforeLongBreak, MinSessions, MaxSessions);

    public TimeSpan DurationOf(PomodoroPhase phase) => TimeSpan.FromMinutes(phase switch
    {
        PomodoroPhase.Work => WorkMinutes,
        PomodoroPhase.ShortBreak => ShortBreakMinutes,
        _ => LongBreakMinutes,
    });

    private static bool InRange(int value, int min, int max) => value >= min && value <= max;
}
