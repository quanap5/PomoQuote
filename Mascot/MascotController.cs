using System.Runtime.InteropServices;
using HelloWorldBot.Pomodoro;
using Microsoft.Win32;

namespace HelloWorldBot.Mascot;

/// <summary>
/// Runs the taskbar mascot while the main window is hidden in the tray: a ~12 fps timer feeds the
/// <see cref="MascotMover"/>, each frame is drawn into the click-through <see cref="MascotWindow"/>, and the
/// robot reacts to the Pomodoro (mood, celebration), to monitors coming and going, to the screen being
/// locked or the PC sleeping (animation stops), and to full-screen apps (robot hides). Everything it creates
/// is released again by <see cref="Stop"/>, so minimize / restore cycles do not leak.
/// </summary>
internal sealed class MascotController : IDisposable
{
    private const int TickMilliseconds = 80;
    private const double MaxStepSeconds = 0.25;
    private const int HousekeepingEvery = 12; // about once a second

    private readonly PomodoroController _pomodoro;
    private readonly TimeProvider _clock;
    private readonly string _assetsDirectory;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = TickMilliseconds };
    private readonly MascotMover _mover = new();
    private MascotRenderer? _renderer;
    private MascotSound? _shout;
    private MascotWindow? _window;
    private MascotSettings _settings;
    private long _lastTimestamp;
    private int _ticks;
    private bool _running;
    private bool _paused;
    private bool _fullScreenHidden;

    public MascotController(PomodoroController pomodoro, MascotSettings settings, string assetsDirectory, TimeProvider clock)
    {
        _pomodoro = pomodoro;
        _settings = settings;
        _assetsDirectory = assetsDirectory;
        _clock = clock;
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>The usable area of every monitor. Replaceable so tests can fake a monitor layout.</summary>
    public Func<IReadOnlyList<MonitorInfo>> MonitorProvider { get; set; } = () => [];

    /// <summary>True while a full-screen app, game or presentation is in front. Replaceable for tests.</summary>
    public Func<bool> FullScreenAppDetector { get; set; } = DetectFullScreenApp;

    /// <summary>True from <see cref="Start"/> to <see cref="Stop"/>.</summary>
    public bool IsRunning => _running;

    public bool IsAnimating => _running && _timer.Enabled;

    public MascotPose? Pose => _mover.Pose;

    public MascotSettings Settings => _settings;

    /// <summary>Raised when the robot is clicked.</summary>
    public event EventHandler? RobotClicked;

    /// <summary>Raised each time the robot jumps onto another monitor (when its shout is played).</summary>
    public event EventHandler? Crossed;

    /// <summary>Puts the robot on the taskbar and starts walking. Does nothing if the mascot is disabled or its art is missing.</summary>
    public bool Start()
    {
        if (_running) return true;
        if (!_settings.Enabled || !LoadAssets()) return false;

        _running = true;
        _paused = false;
        _fullScreenHidden = false;
        ApplyToMover();
        RefreshRoute();
        UpdateMood();

        _pomodoro.Timer.Changed += OnPomodoroChanged;
        _pomodoro.Timer.PhaseCompleted += OnPhaseCompleted;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _window = new MascotWindow();
        _window.RobotClicked += OnRobotClicked;
        _lastTimestamp = _clock.GetTimestamp();
        _timer.Start();
        Tick();
        return true;
    }

    /// <summary>Takes the robot off the taskbar and releases the window, the timer subscriptions and the system hooks.</summary>
    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _timer.Stop();

        _pomodoro.Timer.Changed -= OnPomodoroChanged;
        _pomodoro.Timer.PhaseCompleted -= OnPhaseCompleted;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        if (_window is not null)
        {
            _window.RobotClicked -= OnRobotClicked;
            _window.Close();
            _window.Dispose();
            _window = null;
        }
    }

    /// <summary>Applies new options immediately. Disabling the mascot while it is running stops it.</summary>
    public void Apply(MascotSettings settings)
    {
        _settings = settings.Normalized();
        if (!_running) return;
        if (!_settings.Enabled)
        {
            Stop();
            return;
        }

        ApplyToMover();
        RefreshRoute();
    }

    private bool LoadAssets()
    {
        try
        {
            _renderer ??= new MascotRenderer(Path.Combine(_assetsDirectory, "robot-sprites.png"));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException or OutOfMemoryException)
        {
            return false; // no art, no robot
        }

        if (_shout is null)
        {
            try
            {
                _shout = new MascotSound(Path.Combine(_assetsDirectory, "shout.wav"));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // The robot still walks; it just cannot shout.
            }
        }

        return true;
    }

    private void ApplyToMover()
    {
        _mover.SizeScale = _settings.SizeScale;
        _mover.SpeedFactor = _settings.SpeedFactor;
    }

    private void RefreshRoute()
    {
        var monitors = MonitorProvider();
        var enabled = _settings.EnabledMonitors;
        _mover.SetRoute(MascotRoute.Build(monitors, enabled));
    }

    private void UpdateMood()
    {
        var timer = _pomodoro.Timer;
        _mover.Mood = (timer.State, timer.Phase) switch
        {
            (PomodoroRunState.Running, PomodoroPhase.Work) => MascotMood.Working,
            (PomodoroRunState.Running, _) => MascotMood.Break,
            _ => MascotMood.Idle,
        };
    }

    private void Tick()
    {
        if (!_running || _window is null || _renderer is null) return;

        var now = _clock.GetTimestamp();
        var seconds = Math.Min(MaxStepSeconds, Math.Max(0, _clock.GetElapsedTime(_lastTimestamp, now).TotalSeconds));
        _lastTimestamp = now;

        if (++_ticks % HousekeepingEvery == 0) Housekeeping();
        if (_fullScreenHidden) return;

        var events = _mover.Update(TimeSpan.FromSeconds(seconds));
        if (events.HasFlag(MascotEvents.CrossingStarted))
        {
            PlayShout();
            Crossed?.Invoke(this, EventArgs.Empty);
        }

        Draw();
    }

    private void Draw()
    {
        if (_window is null || _renderer is null || _mover.Pose is not { } pose) return;

        using var frame = _renderer.Render(pose, _settings.SizeScale, pose.Dpi);
        var topLeft = new Point((int)Math.Round(pose.FeetX - frame.Width / 2.0), (int)Math.Round(pose.FeetY - frame.Height));
        _window.SetFrame(frame, topLeft);
        if (!_window.Visible) _window.Show();
    }

    /// <summary>Once a second: hide for full-screen apps and keep the window on top.</summary>
    private void Housekeeping()
    {
        var fullScreen = FullScreenAppDetector();
        if (fullScreen != _fullScreenHidden)
        {
            _fullScreenHidden = fullScreen;
            if (fullScreen) _window?.Hide();
            else Draw();
        }

        if (!fullScreen) _window?.KeepOnTop();
    }

    private void PlayShout()
    {
        if (_settings.SoundEnabled) _shout?.Play(_settings.VolumeGain);
    }

    private void OnPomodoroChanged(object? sender, EventArgs e) => UpdateMood();

    private void OnPhaseCompleted(object? sender, PhaseCompletedEventArgs e)
    {
        if (!e.WorkSessionCompleted) return;
        _mover.Celebrate();
        PlayShout();
    }

    private void OnRobotClicked(object? sender, EventArgs e) => RobotClicked?.Invoke(this, EventArgs.Empty);

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_running) RefreshRoute();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                Pause();
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                Resume();
                break;
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) Pause();
        else if (e.Mode == PowerModes.Resume) Resume();
    }

    /// <summary>Stops the animation (the screen is locked or the PC is going to sleep).</summary>
    internal void Pause()
    {
        if (!_running || _paused) return;
        _paused = true;
        _timer.Stop();
    }

    /// <summary>Starts the animation again; the time spent paused is not replayed.</summary>
    internal void Resume()
    {
        if (!_running || !_paused) return;
        _paused = false;
        _lastTimestamp = _clock.GetTimestamp();
        RefreshRoute();
        _timer.Start();
    }

    /// <summary>One animation step, for tests and for driving the robot without waiting for the timer.</summary>
    internal void StepForTest() => Tick();

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
        _renderer?.Dispose();
        _shout?.Dispose();
    }

    // QUERY_USER_NOTIFICATION_STATE: 2 = busy (a full-screen app), 3 = Direct3D full screen, 4 = presentation mode.
    private static bool DetectFullScreenApp() =>
        SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4;

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}
