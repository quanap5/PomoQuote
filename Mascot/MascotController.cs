using System.Runtime.InteropServices;
using PomoQuote.Pomodoro;
using Microsoft.Win32;

namespace PomoQuote.Mascot;

/// <summary>
/// Runs the taskbar character while the main window is hidden in the tray: a timer feeds the <see cref="MascotActor"/>,
/// each frame is drawn into the click-through <see cref="MascotWindow"/> (only when something changed), and the character
/// reacts to the Pomodoro (mood, celebration, sadness), to being clicked and dragged, to monitors coming and going, to the
/// screen being locked or the PC sleeping (animation stops), and to full-screen apps (it hides). Everything it creates is
/// released again by <see cref="Stop"/>, so minimize / restore cycles do not leak.
/// </summary>
internal sealed class MascotController : IDisposable
{
    private const int TickMilliseconds = 50; // 20 updates a second: smooth movement, animation frames follow their own fps
    private const double MaxStepSeconds = 0.25;
    private const int HousekeepingEvery = 20; // about once a second

    private readonly PomodoroController _pomodoro;
    private readonly TimeProvider _clock;
    private readonly string _assetsDirectory;
    private readonly string? _userConfigPath;
    private readonly Func<double> _random;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = TickMilliseconds };
    private MascotActor? _actor;
    private MascotConfig _config = MascotConfig.Default;
    private MascotRenderer? _renderer;
    private MascotSound? _shout;
    private MascotWindow? _window;
    private MascotSettings _settings;
    private string _loadedCharacter = "";
    private string _requestedCharacter = "";
    private SynchronizationContext? _uiContext;
    private long _lastTimestamp;
    private int _ticks;
    private bool _running;
    private bool _paused;
    private bool _fullScreenHidden;
    private PointF _grabOffset;
    private (int X, int Y, int Row, int Frame, bool Right, int Size) _lastDrawn;
    private bool _hasDrawn;

    public MascotController(PomodoroController pomodoro, MascotSettings settings, string assetsDirectory, TimeProvider clock, string? userConfigPath = null, Func<double>? random = null)
    {
        _pomodoro = pomodoro;
        _settings = settings;
        _assetsDirectory = assetsDirectory;
        _clock = clock;
        _userConfigPath = userConfigPath;
        _random = random ?? Random.Shared.NextDouble;
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>The usable area of every monitor. Replaceable so tests can fake a monitor layout.</summary>
    public Func<IReadOnlyList<MonitorInfo>> MonitorProvider { get; set; } = () => [];

    /// <summary>True while a full-screen app, game or presentation is in front. Replaceable for tests.</summary>
    public Func<bool> FullScreenAppDetector { get; set; } = DetectFullScreenApp;

    /// <summary>True from <see cref="Start"/> to <see cref="Stop"/>.</summary>
    public bool IsRunning => _running;

    public bool IsAnimating => _running && _timer.Enabled;

    public MascotPose? Pose => _actor?.Pose;

    public MascotActor? Actor => _actor;

    public MascotSettings Settings => _settings;

    /// <summary>The id of the character whose sheet is loaded now (empty before anything was loaded); for the tests.</summary>
    internal string LoadedCharacter => _loadedCharacter;

    public MascotConfig Config => _config;

    /// <summary>Problems found in the animation config files the last time they were loaded (empty when all is well).</summary>
    public IReadOnlyList<string> ConfigWarnings { get; private set; } = [];

    /// <summary>Raised when the character is double-clicked: the window should come back.</summary>
    public event EventHandler? RestoreRequested;

    /// <summary>Raised each time the character jumps onto another monitor (when its shout is played).</summary>
    public event EventHandler? Crossed;

    /// <summary>Puts the character on the taskbar. Does nothing if the mascot is disabled or its art is missing.</summary>
    public bool Start()
    {
        if (_running) return true;
        if (!_settings.Enabled || !LoadAssets()) return false;

        _running = true;
        _paused = false;
        _fullScreenHidden = false;
        _hasDrawn = false;

        // SystemEvents are raised on their own thread; the UI thread's context lets the handlers hop back.
        _uiContext = SynchronizationContext.Current;

        _actor = new MascotActor(_config, _random);
        ApplyToActor();
        RefreshRoute();
        UpdateMood();

        _pomodoro.Timer.Changed += OnPomodoroChanged;
        _pomodoro.Timer.PhaseCompleted += OnPhaseCompleted;
        _pomodoro.Timer.PhaseSkipped += OnPhaseSkipped;
        _pomodoro.Timer.ResetPerformed += OnPhaseSkipped;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _window = new MascotWindow();
        _window.Clicked += OnClicked;
        _window.DoubleClicked += OnDoubleClicked;
        _window.DragStarted += OnDragStarted;
        _window.DragMoved += OnDragMoved;
        _window.DragEnded += OnDragEnded;
        _lastTimestamp = _clock.GetTimestamp();
        _timer.Start();
        Tick();
        return true;
    }

    /// <summary>Takes the character off the taskbar and releases the window, the timer subscriptions and the system hooks.</summary>
    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _timer.Stop();

        _pomodoro.Timer.Changed -= OnPomodoroChanged;
        _pomodoro.Timer.PhaseCompleted -= OnPhaseCompleted;
        _pomodoro.Timer.PhaseSkipped -= OnPhaseSkipped;
        _pomodoro.Timer.ResetPerformed -= OnPhaseSkipped;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        if (_window is not null)
        {
            _window.Clicked -= OnClicked;
            _window.DoubleClicked -= OnDoubleClicked;
            _window.DragStarted -= OnDragStarted;
            _window.DragMoved -= OnDragMoved;
            _window.DragEnded -= OnDragEnded;
            _window.Close();
            _window.Dispose();
            _window = null;
        }

        _actor = null;
    }

    /// <summary>Applies new options immediately. Disabling the mascot while it is running stops it.</summary>
    public void Apply(MascotSettings settings)
    {
        _settings = settings.Normalized();
        // Compared with the character asked for, not the one loaded: a sheet that is missing (the robot stands in) is not tried again on every Apply.
        if (_renderer is not null && _requestedCharacter != MascotCharacters.Find(_settings.Character).Id)
        {
            // Another character: its sheet replaces the old one at once (a running character keeps walking where it is).
            _renderer.Dispose();
            _renderer = null;
            if (_running && !LoadAssets()) Stop();
        }

        if (!_running) return;
        if (!_settings.Enabled)
        {
            Stop();
            return;
        }

        ApplyToActor();
        RefreshRoute();
        _hasDrawn = false;
    }

    /// <summary>
    /// Reads the animation and behaviour config files again (the shipped one, then the user's in %AppData%) and applies
    /// them at once. Returns the warnings about anything invalid that was ignored.
    /// </summary>
    public IReadOnlyList<string> ReloadConfig()
    {
        var previousFrameSize = _config.FrameSize;
        var warnings = LoadConfig();
        if (_running && _actor is not null)
        {
            _actor.SetConfig(_config);
            if (_renderer is not null && previousFrameSize != _config.FrameSize)
            {
                _renderer.Dispose();
                _renderer = null;
                LoadAssets();
            }

            _hasDrawn = false;
        }

        return warnings;
    }

    private IReadOnlyList<string> LoadConfig()
    {
        var warnings = new List<string>();
        _config = MascotConfig.Load(Path.Combine(_assetsDirectory, "mascot-config.json"), _userConfigPath, warnings);
        ConfigWarnings = warnings;
        return warnings;
    }

    private bool LoadAssets()
    {
        if (_renderer is null)
        {
            LoadConfig();
            var character = MascotCharacters.Find(_settings.Character);
            _requestedCharacter = character.Id;
            try
            {
                _renderer = new MascotRenderer(Path.Combine(_assetsDirectory, character.SheetFile), _config.FrameSize);
                _loadedCharacter = character.Id;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException or OutOfMemoryException)
            {
                // A sheet that is missing or damaged: the robot instead, and no character at all only when its art is missing too.
                try
                {
                    _renderer = new MascotRenderer(Path.Combine(_assetsDirectory, MascotCharacters.Default.SheetFile), _config.FrameSize);
                    _loadedCharacter = MascotCharacters.DefaultId;
                }
                catch (Exception inner) when (inner is IOException or ArgumentException or ExternalException or OutOfMemoryException)
                {
                    return false;
                }
            }
        }

        if (_shout is null)
        {
            try
            {
                _shout = new MascotSound(Path.Combine(_assetsDirectory, "shout.wav"));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // The character still walks; it just cannot shout.
            }
        }

        return true;
    }

    private void ApplyToActor()
    {
        if (_actor is null) return;
        _actor.SizeScale = _settings.SizeScale;
        _actor.SpeedFactor = _settings.SpeedFactor;
        _actor.RandomActions = _settings.RandomActions;
    }

    private void RefreshRoute() => _actor?.SetRoute(MascotRoute.Build(MonitorProvider(), _settings.EnabledMonitors));

    private void UpdateMood()
    {
        if (_actor is null) return;
        var timer = _pomodoro.Timer;
        _actor.Mood = (timer.State, timer.Phase) switch
        {
            (PomodoroRunState.Running, PomodoroPhase.Work) => MascotMood.Working,
            (PomodoroRunState.Running, PomodoroPhase.ShortBreak) => MascotMood.Break,
            (PomodoroRunState.Running, PomodoroPhase.LongBreak) => MascotMood.LongBreak,
            _ => MascotMood.Idle,
        };
    }

    private void Tick()
    {
        if (!_running || _window is null || _renderer is null || _actor is null) return;

        var now = _clock.GetTimestamp();
        var seconds = Math.Min(MaxStepSeconds, Math.Max(0, _clock.GetElapsedTime(_lastTimestamp, now).TotalSeconds));
        _lastTimestamp = now;

        if (++_ticks % HousekeepingEvery == 0) Housekeeping();
        if (_fullScreenHidden) return;

        try
        {
            var events = _actor.Update(TimeSpan.FromSeconds(seconds));
            if (events.HasFlag(MascotEvents.CrossingStarted))
            {
                PlayShout();
                Crossed?.Invoke(this, EventArgs.Empty);
            }

            Draw();
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            // A drawing or window failure costs this frame only: a tray-only app must not crash over its mascot.
        }
    }

    /// <summary>Draws the current pose, unless it looks exactly like the last one drawn (sleeping costs nothing).</summary>
    private void Draw()
    {
        if (_window is null || _renderer is null || _actor?.Pose is not { } pose) return;

        var frame = _renderer.Render(pose, _settings.SizeScale, pose.Dpi);
        var topLeft = new Point((int)Math.Round(pose.FeetX - frame.Width / 2.0), (int)Math.Round(pose.FeetY - frame.Height));
        var signature = (topLeft.X, topLeft.Y, pose.Row, pose.Frame, pose.FacingRight, frame.Width);
        if (_hasDrawn && signature == _lastDrawn && _window.Visible) return;

        _window.SetFrame(frame, topLeft);
        _lastDrawn = signature;
        _hasDrawn = true;
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
            else
            {
                _hasDrawn = false;
                Draw();
            }
        }

        if (!fullScreen) _window?.KeepOnTop();
    }

    private void PlayShout()
    {
        if (_settings.SoundEnabled) _shout?.Play(_settings.VolumeGain);
    }

    // -- Pomodoro ---------------------------------------------------------------------------------------------------

    private void OnPomodoroChanged(object? sender, EventArgs e) => UpdateMood();

    private void OnPhaseCompleted(object? sender, PhaseCompletedEventArgs e)
    {
        if (!e.WorkSessionCompleted) return;
        _actor?.Celebrate();

        // The Pomodoro already plays its own phase-end sound; add the shout only when that one is off.
        if (!_pomodoro.Timer.Settings.SoundEnabled) PlayShout();
    }

    private void OnPhaseSkipped(object? sender, EventArgs e) => _actor?.Sadden();

    // -- Mouse -------------------------------------------------------------------------------------------------------

    private void OnClicked(object? sender, EventArgs e)
    {
        _actor?.React();
        _hasDrawn = false;
    }

    private void OnDoubleClicked(object? sender, EventArgs e) => RestoreRequested?.Invoke(this, EventArgs.Empty);

    private void OnDragStarted(Point cursor)
    {
        if (_actor?.Pose is not { } pose) return;
        _grabOffset = new PointF((float)(pose.FeetX - cursor.X), (float)(pose.FeetY - cursor.Y));
        _actor.BeginDrag(cursor.X + _grabOffset.X, cursor.Y + _grabOffset.Y);
        Draw();
    }

    private void OnDragMoved(Point cursor)
    {
        if (_actor is null) return;
        _actor.DragTo(cursor.X + _grabOffset.X, cursor.Y + _grabOffset.Y);
        Draw();
    }

    private void OnDragEnded(Point cursor)
    {
        if (_actor is null) return;
        _actor.DragTo(cursor.X + _grabOffset.X, cursor.Y + _grabOffset.Y);
        _actor.EndDrag();
        Draw();
    }

    // -- System events (arrive on a background thread) -------------------------------------------------------

    // The three SystemEvents below arrive on a background thread. Everything they touch (the WinForms timer,
    // the actor, the window) belongs to the UI thread, so each handler only posts the work back to it.
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => RunOnUiThread(RefreshRoute);

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                RunOnUiThread(Pause);
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                RunOnUiThread(Resume);
                break;
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) RunOnUiThread(Pause);
        else if (e.Mode == PowerModes.Resume) RunOnUiThread(Resume);
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread, later. It is skipped if the mascot was stopped in the
    /// meantime, so a late system event can never touch a window or timer that is already gone.
    /// </summary>
    private void RunOnUiThread(Action action)
    {
        var context = _uiContext;
        if (context is null)
        {
            if (_running) action();
            return;
        }

        context.Post(_ =>
        {
            if (_running) action();
        }, null);
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

    /// <summary>One animation step, for tests and for driving the character without waiting for the timer.</summary>
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
