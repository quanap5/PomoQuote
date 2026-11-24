using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using PomoQuote.AiUsage;
using PomoQuote.Pomodoro;
using PomoQuote.Quotes;
using PomoQuote.Weather;

namespace PomoQuote;

/// <summary>
/// The compact content of widget mode (about 260x210 logical px): the time, the weather on one line,
/// the Pomodoro phase, remaining time and progress bar, on a solid dark panel so it stays legible over any
/// desktop. Everything is laid out in proportion to the control's height, so it scales with the display.
/// Dragging it anywhere moves the window. It shares the app's controller and weather panel; it has no
/// timer of its own except a one-second clock tick while it is visible.
/// </summary>
internal sealed class WidgetView : Control
{
    /// <summary>The widget's size at 100% display scale; the layout is designed on this grid.</summary>
    public static readonly Size LogicalSize = new(260, 210);

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
    private Func<IReadOnlyList<UsageProviderView>> _usageViews = () => [];
    private UsageDisplayMode _usageMode;
    private string _quoteEnglish = "";
    private string? _quoteVietnamese;

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

        // One unit = 1/210 of the height: 1 px at 100% scale, 2 px at 200%.
        var u = Math.Max(0.5f, ClientSize.Height / (float)LogicalHeight);
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
        {
            // Every weather value comes with its own icon, drawn crisp at exactly this size (see IconTextLine).
            var snapshot = _weather.Snapshot;
            var iconSize = (int)Math.Round(14 * u);
            IconTextLine.Draw(graphics, _weather.Icons, WeatherLines.City(snapshot), small, iconSize, 5 * u, left, 48 * u, SecondaryText, shadow: false);
            IconTextLine.Draw(graphics, _weather.Icons, WeatherLines.Summary(snapshot), small, iconSize, 5 * u, left, 65 * u, PrimaryText, shadow: false);
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

        DrawQuote(graphics, u, left, width);
        DrawUsage(graphics, u, left, width);
    }

    /// <summary>
    /// The sentence and its translation in the bottom strip. Each is limited to the lines that fit and ends
    /// with an ellipsis when cut, so a long sentence never spills over the Pomodoro rows above it.
    /// </summary>
    private void DrawQuote(Graphics graphics, float u, float left, float width)
    {
        if (_quoteEnglish.Length == 0) return;

        using (var divider = new Pen(BorderColor, Math.Max(1f, u)))
        {
            graphics.DrawLine(divider, left, 134 * u, left + width, 134 * u);
        }

        using var format = new StringFormat { Trimming = StringTrimming.EllipsisWord, FormatFlags = StringFormatFlags.LineLimit };
        var englishPixels = 11.5f * u;
        var vietnamesePixels = 10.5f * u;
        using var english = new Font(QuoteLayout.EnglishFamily, englishPixels, FontStyle.Regular, GraphicsUnit.Pixel);
        using var vietnamese = new Font(QuoteLayout.VietnameseFamily, vietnamesePixels, FontStyle.Regular, GraphicsUnit.Pixel);
        using var primary = new SolidBrush(Color.FromArgb(120, 230, 120));
        using var secondary = new SolidBrush(SecondaryText);

        // Two lines of English; the translation gets what is left, which is two lines when it is shown.
        var top = 139 * u;
        var bottom = 205 * u;
        var englishHeight = englishPixels * 1.35f * 2;
        graphics.DrawString(_quoteEnglish, english, primary, new RectangleF(left, top, width, englishHeight), format);
        if (_quoteVietnamese is not null)
        {
            var translationTop = top + englishHeight + 2 * u;
            graphics.DrawString(_quoteVietnamese, vietnamese, secondary, new RectangleF(left, translationTop, width, bottom - translationTop), format);
        }
    }

    /// <summary>Height of the base layout in logical px (time, weather, Pomodoro, sentence).</summary>
    public const int BaseLogicalHeight = 210;

    private const int UsageLineHeight = 15;
    private const int UsageStripPadding = 10;

    /// <summary>The widget's current height at 100% scale: the base layout plus one line per usage line.</summary>
    public int LogicalHeight { get; private set; } = BaseLogicalHeight;

    /// <summary>The widget's size at 100% scale with the usage strip it currently shows.</summary>
    public Size LogicalClientSize => new(LogicalSize.Width, LogicalHeight);

    /// <summary>Sets where the usage strip gets its content and how it is drawn; returns true when the widget height changed.</summary>
    public bool SetUsage(Func<IReadOnlyList<UsageProviderView>> views, UsageDisplayMode mode)
    {
        _usageViews = views;
        _usageMode = mode;
        var lines = UsageViewBuilder.LineCount(views(), mode);
        var height = lines > 0 ? BaseLogicalHeight + UsageStripPadding + lines * UsageLineHeight : BaseLogicalHeight;
        var changed = height != LogicalHeight;
        LogicalHeight = height;
        Invalidate();
        return changed;
    }

    /// <summary>
    /// The AI usage strip under the sentence: per provider a header (name, plan, note) and one bar row per quota
    /// window, or a single line in percent-only mode. Colours follow the warning thresholds; a note in amber means
    /// stale data or a problem.
    /// </summary>
    private void DrawUsage(Graphics graphics, float u, float left, float width)
    {
        var views = _usageViews();
        if (views.Count == 0) return;

        using (var divider = new Pen(BorderColor, Math.Max(1f, u)))
        {
            graphics.DrawLine(divider, left, (BaseLogicalHeight - 1) * u, left + width, (BaseLogicalHeight - 1) * u);
        }

        using var name = new Font("Segoe UI Semibold", 11f * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var small = new Font("Segoe UI", 10.5f * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var primary = new SolidBrush(PrimaryText);
        using var secondary = new SolidBrush(SecondaryText);
        using var trackBrush = new SolidBrush(Color.FromArgb(70, 78, 92));
        using var near = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
        using var far = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Far };

        var y = (BaseLogicalHeight + 4) * u;
        var line = UsageLineHeight * u;
        foreach (var view in views)
        {
            var noteColor = view.IsProblem ? Color.FromArgb(240, 180, 80) : SecondaryText;
            if (_usageMode == UsageDisplayMode.Percent)
            {
                var text = view.Rows.Count == 0 ? view.Note : string.Join("  ", view.Rows.Select(r => $"{r.Label} {r.Percent}"));
                using var noteBrush = new SolidBrush(view.Rows.Count == 0 || view.IsProblem ? noteColor : PrimaryText);
                graphics.DrawString(view.Name, name, primary, new RectangleF(left, y, 52 * u, line), near);
                graphics.DrawString(text, small, noteBrush, new RectangleF(left + 54 * u, y, width - 54 * u, line), near);
                y += line;
                continue;
            }

            var header = view.Plan.Length > 0 ? $"{view.Name}  {view.Plan}" : view.Name;
            using (var noteBrush = new SolidBrush(noteColor))
            {
                graphics.DrawString(header, name, primary, new RectangleF(left, y, width * 0.45f, line), near);
                graphics.DrawString(view.Note, small, noteBrush, new RectangleF(left + width * 0.4f, y + 0.5f * u, width * 0.6f, line), far);
            }

            y += line;
            if (view.Rows.Count == 0)
            {
                y += line; // keeps the height the layout reserved for a provider without numbers
                continue;
            }

            foreach (var row in view.Rows)
            {
                var barLeft = left + 34 * u;
                var barWidth = 96 * u;
                graphics.DrawString(row.Label, small, secondary, new RectangleF(left, y, 34 * u, line), near);
                graphics.FillRectangle(trackBrush, barLeft, y + 4 * u, barWidth, 7 * u);
                using (var fill = new SolidBrush(LevelColor(row.Level)))
                {
                    graphics.FillRectangle(fill, barLeft, y + 4 * u, (float)(barWidth * row.Fraction), 7 * u);
                }

                graphics.DrawString(row.Percent, small, primary, new RectangleF(barLeft + barWidth + 4 * u, y, 32 * u, line), far);
                if (row.Reset.Length > 0)
                {
                    graphics.DrawString(row.Reset, small, secondary, new RectangleF(barLeft + barWidth + 40 * u, y, width - (34 + 96 + 40) * u, line), far);
                }

                y += line;
            }
        }
    }

    private static Color LevelColor(UsageLevel level) => level switch
    {
        UsageLevel.Critical => Color.FromArgb(240, 98, 82),
        UsageLevel.Warning => Color.FromArgb(240, 180, 80),
        _ => Color.FromArgb(70, 190, 120),
    };
    /// <summary>Sets the sentence shown in the widget (empty text hides the strip's content).</summary>
    public void SetQuote(string english, string? vietnamese)
    {
        _quoteEnglish = english;
        _quoteVietnamese = string.IsNullOrWhiteSpace(vietnamese) ? null : vietnamese;
        Invalidate();
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
