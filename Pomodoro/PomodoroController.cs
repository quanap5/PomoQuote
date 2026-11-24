using System.Media;

namespace PomoQuote.Pomodoro;

/// <summary>
/// Connects the <see cref="PomodoroTimer"/> to the desktop: a UI timer that ticks it, the alert
/// sounds, the balloon notification (through the shared <see cref="TrayIconHost"/>), the window-title text and the saved settings and daily count.
/// Everything it owns is released by <see cref="Dispose"/>.
/// </summary>
internal sealed class PomodoroController : IDisposable
{
    private const int PollIntervalMilliseconds = 250;

    private readonly TimeProvider _clock;
    private readonly PomodoroStore _store;
    private readonly DailyCounter _counter;
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = PollIntervalMilliseconds };
    private readonly TrayIconHost _tray;
    private readonly SoundPlayer? _workEndSound;
    private readonly SoundPlayer? _breakEndSound;
    private int _shownSeconds = -1;

    public PomodoroController(TimeProvider clock, PomodoroStore store, string soundsDirectory, TrayIconHost tray)
    {
        _clock = clock;
        _store = store;
        _tray = tray;

        var data = store.Load();
        _counter = new DailyCounter(data.Day, data.CompletedToday);
        Timer = new PomodoroTimer(clock, data.Settings);
        Timer.PhaseCompleted += OnPhaseCompleted;
        Timer.Changed += (_, _) => RaiseViewChanged(force: true);

        _workEndSound = TryCreateSound(Path.Combine(soundsDirectory, "work-end.wav"));
        _breakEndSound = TryCreateSound(Path.Combine(soundsDirectory, "break-end.wav"));

        _poll.Tick += (_, _) => PollTimer();
    }

    public PomodoroTimer Timer { get; }

    /// <summary>Work sessions finished today (resets by itself on a new day).</summary>
    public int CompletedToday => _counter.CountFor(Today);

    /// <summary>Raised when the displayed seconds, phase or state change: redraw the panel.</summary>
    public event EventHandler? ViewChanged;

    /// <summary>Raised with the text for the window title while a phase is active, or null to show the normal title.</summary>
    public event EventHandler<string?>? TitleChanged;

    public void Start() => _poll.Start();

    public void ApplySettings(PomodoroSettings settings)
    {
        Timer.ApplySettings(settings);
        Save();
    }

    /// <summary>"mm:ss Work" style text, or null while the timer is idle.</summary>
    public string? BuildTitle()
    {
        if (Timer.State == PomodoroRunState.Idle) return null;
        var seconds = WholeSecondsLeft();
        var paused = Timer.State == PomodoroRunState.Paused ? " (paused)" : "";
        return $"{seconds / 60:00}:{seconds % 60:00} {PhaseName(Timer.Phase)}{paused}";
    }

    public static string PhaseName(PomodoroPhase phase) => phase switch
    {
        PomodoroPhase.Work => "Work",
        PomodoroPhase.ShortBreak => "Break",
        _ => "Long break",
    };

    /// <summary>Whole seconds left, rounded up so the display reads 00:00 only when the phase is over.</summary>
    public int WholeSecondsLeft() => (int)Math.Ceiling(Timer.Remaining.TotalSeconds);

    private DateOnly Today => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

    private void PollTimer()
    {
        Timer.Tick();
        RaiseViewChanged(force: false);
    }

    private void RaiseViewChanged(bool force)
    {
        var seconds = WholeSecondsLeft();
        if (!force && seconds == _shownSeconds) return;
        _shownSeconds = seconds;
        ViewChanged?.Invoke(this, EventArgs.Empty);
        TitleChanged?.Invoke(this, BuildTitle());
    }

    private void OnPhaseCompleted(object? sender, PhaseCompletedEventArgs e)
    {
        if (e.WorkSessionCompleted)
        {
            _counter.Increment(Today);
            Save();
        }

        if (Timer.Settings.SoundEnabled)
        {
            // Play() returns at once and plays on another thread, so the window never waits for it.
            (e.WorkSessionCompleted ? _workEndSound : _breakEndSound)?.Play();
        }

        var settings = Timer.Settings;
        var message = e.WorkSessionCompleted
            ? $"Work session complete. Time for a {(e.Next == PomodoroPhase.LongBreak ? $"long break ({settings.LongBreakMinutes} min)" : $"break ({settings.ShortBreakMinutes} min)")}."
            : $"Break over. Back to work ({settings.WorkMinutes} min).";
        ShowBalloon(message);
    }

    private void ShowBalloon(string message) => _tray.ShowBalloon("Pomodoro", message);

    private void Save()
    {
        try
        {
            _store.Save(new PomodoroData(Timer.Settings, _counter.Day, _counter.Count));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The timer keeps working; the options or count are just not remembered next time.
        }
    }

    private static SoundPlayer? TryCreateSound(string path)
    {
        if (!File.Exists(path)) return null;
        var player = new SoundPlayer(path);
        try
        {
            player.LoadAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            player.Dispose();
            return null;
        }

        return player;
    }

    public void Dispose()
    {
        _poll.Stop();
        _poll.Dispose();
        _workEndSound?.Dispose();
        _breakEndSound?.Dispose();
    }
}
