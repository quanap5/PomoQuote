using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using HelloWorldBot.Pomodoro;

namespace HelloWorldBot;

/// <summary>
/// The compact content of widget mode (about 260x140 logical px): the time, the weather on one line, and
/// the Pomodoro phase, remaining time and progress bar, on a solid dark panel so it stays legible over any
/// desktop. Everything is laid out in proportion to the control's height, so it scales with the display.
/// Dragging it anywhere moves the window. It shares the app's controller and weather panel; it has no
/// timer of its own except a one-second clock tick while it is visible.
/// </summary>
internal sealed class WidgetView : Control
{
    /// <summary>The widget's size at 100% display scale; the layout is designed on this grid.</summary>
    public static readonly Size LogicalSize = new(260, 140);

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;

    private static readonly Color PanelColor = Color.FromArgb(28, 32, 40);
    private static readonly Color BorderColor = Color.FromArgb(84, 94, 110);
    private static readonly Color PrimaryText = Color.FromArgb(244, 246, 248);
    private static readonly Color SecondaryText = Color.FromArgb(178, 188, 200);

    private readonly PomodoroController _pomodoro;
    private readonly WeatherPanel _weather;
    private readonly System.Windows.Forms.Timer _clockTick = new() { Interval = 500 };
    private string _shownTime = "";

    public WidgetView(PomodoroController pomodoro, WeatherPanel weather)
    {
        _pomodoro = pomodoro;
        _weather = weather;
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = PanelColor;
        Cursor = Cursors.SizeAll;

        _pomodoro.ViewChanged += (_, _) => Invalidate();
        _weather.Updated += (_, _) => Invalidate();
        _clockTick.Tick += (_, _) =>
        {
            var now = TimeText();
            if (now == _shownTime) return;
            _shownTime = now;
            Invalidate();
        };
    }

    /// <summary>The clock tick only runs while the widget is on screen.</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        _clockTick.Enabled = Visible;
        if (Visible) Invalidate();
    }

    private static string TimeText() => DateTime.Now.ToString("HH:mm:ss");

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || FindForm() is not { } form) return;

        // Hands the drag to Windows as if the title bar had been pressed: the whole widget is a handle.
        ReleaseCapture();
        SendMessage(form.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(PanelColor);

        // One unit = 1/140 of the height: 1 px at 100% scale, 2 px at 200%.
        var u = Math.Max(0.5f, ClientSize.Height / (float)LogicalSize.Height);
        var left = 14 * u;
        var width = ClientSize.Width - 28 * u;
        using var near = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
        using var farFormat = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Far };

        using (var border = new Pen(BorderColor, Math.Max(1f, u)))
        {
            graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        // Time
        using (var timeFont = new Font("Segoe UI Light", 30 * u, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var primary = new SolidBrush(PrimaryText))
        {
            graphics.DrawString(TimeText(), timeFont, primary, new RectangleF(left, 6 * u, width, 40 * u), near);
        }

        // Weather: city on the left, temperature and conditions below
        using (var small = new Font("Segoe UI", 11.5f * u, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var secondary = new SolidBrush(SecondaryText))
        using (var primary = new SolidBrush(PrimaryText))
        {
            graphics.DrawString(_weather.CityName, small, secondary, new RectangleF(left, 48 * u, width, 17 * u), near);
            graphics.DrawString(_weather.CompactSummary, small, primary, new RectangleF(left, 65 * u, width, 17 * u), near);
        }

        // Pomodoro: phase and remaining time, today's count, progress bar
        var timer = _pomodoro.Timer;
        var accent = PhaseColor(timer.Phase);
        var seconds = _pomodoro.WholeSecondsLeft();
        var phase = PomodoroController.PhaseName(timer.Phase) + (timer.State == PomodoroRunState.Paused ? " (paused)" : "");
        using (var bold = new Font("Segoe UI Semibold", 14 * u, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var small = new Font("Segoe UI", 11.5f * u, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var primary = new SolidBrush(PrimaryText))
        using (var secondary = new SolidBrush(SecondaryText))
        using (var dot = new SolidBrush(accent))
        {
            graphics.FillEllipse(dot, left, 92 * u, 9 * u, 9 * u);
            graphics.DrawString($"{phase}  {seconds / 60:00}:{seconds % 60:00}", bold, primary, new RectangleF(left + 15 * u, 87 * u, width - 15 * u, 20 * u), near);
            graphics.DrawString($"Today {_pomodoro.CompletedToday}", small, secondary, new RectangleF(left, 89 * u, width, 17 * u), farFormat);
        }

        var bar = new RectangleF(left, 116 * u, width, 8 * u);
        using (var track = new SolidBrush(Color.FromArgb(70, 78, 92)))
        using (var fill = new SolidBrush(accent))
        {
            graphics.FillRectangle(track, bar);
            graphics.FillRectangle(fill, bar.X, bar.Y, (float)(bar.Width * timer.Progress), bar.Height);
        }
    }

    private static Color PhaseColor(PomodoroPhase phase) => phase switch
    {
        PomodoroPhase.Work => Color.FromArgb(240, 98, 82),
        PomodoroPhase.ShortBreak => Color.FromArgb(70, 190, 120),
        _ => Color.FromArgb(86, 150, 240),
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clockTick.Stop();
            _clockTick.Dispose();
        }

        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
}
