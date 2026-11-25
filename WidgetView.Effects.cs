using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PomoQuote.Weather;
using PomoQuote.Widget;

namespace PomoQuote;

/// <summary>
/// The weather effect behind the widget's content. It has its own timer (about 15 to 18 frames a second) that exists only while
/// the effect is on, the widget is on screen and nothing hides it; every other state stops the timer, so an idle or hidden widget
/// costs nothing (see <see cref="WeatherEffectPolicy"/>). While an effect shows, the widget's text, icons and bars are drawn once
/// into a cached transparent layer that only changes when the content does, so a frame is the effect plus one blit.
/// </summary>
internal sealed partial class WidgetView
{
    private readonly WeatherEffectField _effects = new();
    private readonly System.Windows.Forms.Timer _effectTick = new();
    private WeatherEffectSettings _effectSettings = WeatherEffectSettings.Default;
    private WeatherEffect _effect = WeatherEffect.None;
    private EffectRunState _effectState = EffectRunState.Off;
    private WeatherEffect _stillOf = WeatherEffect.None;
    private WeatherEffectIntensity _stillIntensity;
    private long _lastFrame;
    private bool _frameOnly;
    private bool _contentDirty = true;
    private Bitmap? _content;
    private bool _sessionLocked;
    private bool _asleep;

    /// <summary>True while the Windows session is locked (the effect pauses); set by the session events, and by tests.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool SessionLocked
    {
        get => _sessionLocked;
        set { _sessionLocked = value; EvaluateEffects(); }
    }

    /// <summary>True while the computer is suspended (the effect pauses); set by the power events, and by tests.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Asleep
    {
        get => _asleep;
        set { _asleep = value; EvaluateEffects(); }
    }
    private bool _syncing;

    /// <summary>What the effect is doing: off, paused, a still picture or animating.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal EffectRunState EffectState => _effectState;

    /// <summary>The frames drawn by the effect timer since the widget was made (a test counts them to prove nothing ticks while paused).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int EffectTicks { get; private set; }

    /// <summary>True while the effect's own timer is running.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool EffectTimerRunning => _effectTick.Enabled;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int EffectTimerInterval => _effectTick.Interval;

    /// <summary>The effect the current weather asks for (None while there is no weather).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal WeatherEffect CurrentEffect => _effect;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal WeatherEffectField EffectField => _effects;

    /// <summary>The user's weather effect settings as applied.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal WeatherEffectSettings EffectSettings => _effectSettings;

    /// <summary>Test seam: false when the widget is not covered; the real check looks at what is on top of it.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<Form, bool>? OcclusionProbe { get; set; }

    /// <summary>Test seam: whether Windows has client-area animations on. The real value is read from the system.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<bool> SystemAnimationsProbe { get; set; } = WindowsMotion.ClientAreaAnimationEnabled;

    private void InitializeEffects()
    {
        _effectTick.Tick += (_, _) => OnEffectFrame();
        _weather.Updated += (_, _) => ApplyWeatherEffect();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        ApplyWeatherEffect();
    }

    private Form? _hostForm;

    /// <summary>The window is hidden, shown or minimised without this control hearing about it (its own Visible does not raise an event then), so the form is watched.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (FindForm() is not { } form || ReferenceEquals(form, _hostForm)) return;
        if (_hostForm is not null) UnwatchHostForm();
        _hostForm = form;
        form.VisibleChanged += OnHostFormChanged;
        form.Resize += OnHostFormChanged;
    }

    private void UnwatchHostForm()
    {
        if (_hostForm is null) return;
        _hostForm.VisibleChanged -= OnHostFormChanged;
        _hostForm.Resize -= OnHostFormChanged;
        _hostForm = null;
    }

    private void OnHostFormChanged(object? sender, EventArgs e)
    {
        _clockTick.Enabled = Visible; // no clock tick either while the window is hidden
        EvaluateEffects();
        if (Visible) Invalidate();
    }

    private void DisposeEffects()
    {
        UnwatchHostForm();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _effectTick.Stop();
        _effectTick.Dispose();
        _effects.Dispose();
        _content?.Dispose();
        _content = null;
    }

    /// <summary>Applies the user's settings: on or off, Low or Normal, still or animated.</summary>
    internal void ApplyEffectSettings(WeatherEffectSettings settings)
    {
        _effectSettings = settings;
        EvaluateEffects();
    }

    private void ApplyWeatherEffect()
    {
        var report = _weather.Snapshot.Report;
        _effect = WeatherEffectMapping.For(report?.WeatherCode, report?.IsDay ?? true);
        EvaluateEffects();
    }

    // The system events can arrive on another thread: only a flag is set there, the work is done on the UI thread.
    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        var locked = e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.ConsoleDisconnect;
        var unlocked = e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.RemoteConnect or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.SessionLogon;
        if (!locked && !unlocked) return;
        _sessionLocked = locked;
        PostEvaluate();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) _asleep = true;
        else if (e.Mode == PowerModes.Resume) _asleep = false;
        else return;
        PostEvaluate();
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Window or UserPreferenceCategory.VisualStyle)
        {
            PostEvaluate();
            PostSystemThemeChanged();
        }
    }

    private void PostSystemThemeChanged()
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(SystemThemeChanged); }
        catch (InvalidOperationException) { } // the handle went away between the check and the post
    }

    private void PostEvaluate()
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(EvaluateEffects); }
        catch (InvalidOperationException) { } // the handle went away between the check and the post
    }

    /// <summary>Decides again whether the effect runs: called whenever one of its inputs may have changed, and every clock tick.</summary>
    internal void EvaluateEffects()
    {
        if (IsDisposed || _syncing) return;
        _syncing = true;
        try
        {
            var form = FindForm();
            var visible = Visible && IsHandleCreated && form is { Visible: true };
            var minimized = form is { WindowState: FormWindowState.Minimized };
            var wantsFrames = _effectSettings.Enabled && (_effect.Kind != WeatherEffectKind.None || _effects.IsFading);
            // The covered check only runs while there is something to animate; it is a handful of window lookups.
            var occluded = wantsFrames && visible && !minimized && form is not null && IsOccluded(form);
            // A fade-out of an effect that just ended still needs its frames, so it counts as an effect for the policy.
            var kind = _effect.Kind != WeatherEffectKind.None ? _effect.Kind : _effects.IsFading ? WeatherEffectKind.Clouds : WeatherEffectKind.None;
            var state = WeatherEffectPolicy.Decide(_effectSettings, kind, visible, minimized, occluded, _sessionLocked, _asleep, SystemAnimationsProbe());
            ApplyState(state);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void ApplyState(EffectRunState state)
    {
        var changed = state != _effectState;
        _effectState = state;
        var intensity = _effectSettings.Intensity;
        switch (state)
        {
            case EffectRunState.Off:
                _effectTick.Stop();
                if (!_effectSettings.Enabled || !_effects.IsFading)
                {
                    if (_effects.Effect.Kind != WeatherEffectKind.None || _effects.HasVisibleEffect)
                    {
                        _effects.Set(WeatherEffect.None, intensity, instant: true);
                        InvalidateFrame();
                    }
                }

                break;
            case EffectRunState.Paused:
                _effectTick.Stop();
                _effects.Set(_effect, intensity, instant: true); // when it is shown again the picture is already there
                break;
            case EffectRunState.Still:
                _effectTick.Stop();
                if (_stillOf != _effect || _stillIntensity != intensity || changed)
                {
                    _effects.Set(_effect, intensity, instant: true);
                    _effects.Advance(2.5f); // a still picture of a developed effect, not of the first frame
                    (_stillOf, _stillIntensity) = (_effect, intensity);
                    InvalidateFrame();
                }

                break;
            case EffectRunState.Animating:
                _effects.Set(_effect, intensity);
                var interval = WeatherEffectPolicy.IntervalMilliseconds(intensity);
                if (_effectTick.Interval != interval) _effectTick.Interval = interval;
                if (!_effectTick.Enabled)
                {
                    _lastFrame = Stopwatch.GetTimestamp(); // no catch-up for the time it was paused
                    _effectTick.Start();
                }

                break;
        }

        if (changed) InvalidateFrame();
    }

    private void OnEffectFrame()
    {
        var now = Stopwatch.GetTimestamp();
        var dt = (float)((now - _lastFrame) / (double)Stopwatch.Frequency);
        _lastFrame = now;
        EffectTicks++;
        _effects.Update(dt);
        InvalidateFrame();
        if (_effect.Kind == WeatherEffectKind.None && !_effects.IsFading) EvaluateEffects(); // the fade-out is over: stop the timer
    }

    /// <summary>Repaints for a new effect frame only: the cached content layer stays as it is.</summary>
    private void InvalidateFrame()
    {
        _frameOnly = true;
        try { Invalidate(); }
        finally { _frameOnly = false; }
    }

    /// <summary>Any repaint that did not come from the effect means the content may have changed.</summary>
    protected override void OnInvalidated(InvalidateEventArgs e)
    {
        if (!_frameOnly) _contentDirty = true;
        base.OnInvalidated(e);
    }

    private void ResizeEffects()
    {
        _effects.SetSize(UsageGeometry.WidgetWidth, LogicalHeight);
        _content?.Dispose();
        _content = null;
        _contentDirty = true;
    }

    /// <summary>Draws the effect and then the content over it; false when there is no effect to show (the plain path draws the widget).</summary>
    private bool PaintWithEffects(Graphics graphics)
    {
        if (!_effects.HasVisibleEffect || ClientSize.Width < 2 || ClientSize.Height < 2) return false;

        graphics.Clear(_theme.Panel);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        _effects.Draw(graphics, Unit);

        if (_content is null || _content.Width != ClientSize.Width || _content.Height != ClientSize.Height)
        {
            _content?.Dispose();
            _content = new Bitmap(ClientSize.Width, ClientSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            _contentDirty = true;
        }

        if (_contentDirty)
        {
            _contentDirty = false;
            using var layer = Graphics.FromImage(_content);
            layer.Clear(Color.Transparent);
            PaintContent(layer);
        }

        graphics.DrawImageUnscaled(_content, 0, 0);
        return true;
    }

    // ---------- is the widget covered? ----------

    /// <summary>True when none of five sample points of the window shows this window (another window is over it, or it is on another virtual desktop).</summary>
    private bool IsOccluded(Form form)
    {
        if (OcclusionProbe is { } probe) return probe(form);
        if (!form.IsHandleCreated) return false;

        var bounds = form.Bounds;
        const int inset = 8;
        Point[] points =
        [
            new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2),
            new(bounds.Left + inset, bounds.Top + inset),
            new(bounds.Right - inset, bounds.Top + inset),
            new(bounds.Left + inset, bounds.Bottom - inset),
            new(bounds.Right - inset, bounds.Bottom - inset),
        ];
        foreach (var point in points)
        {
            var hit = NativeMethods.WindowFromPoint(point);
            if (hit == IntPtr.Zero) continue;
            var root = NativeMethods.GetAncestor(hit, NativeMethods.GA_ROOT);
            if (root == form.Handle || NativeMethods.GetWindow(root, NativeMethods.GW_OWNER) == form.Handle) return false;
        }

        return true;
    }

    private static class NativeMethods
    {
        public const uint GA_ROOT = 2;
        public const uint GW_OWNER = 4;

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(Point point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint command);
    }
}

/// <summary>The Windows animation preference.</summary>
internal static class WindowsMotion
{
    private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

    /// <summary>False when the user turned off "Show animations in Windows"; true when on or when it cannot be read.</summary>
    public static bool ClientAreaAnimationEnabled()
    {
        try
        {
            return !SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out var enabled, 0) || enabled;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return true;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, [MarshalAs(UnmanagedType.Bool)] out bool value, uint winIni);
}
