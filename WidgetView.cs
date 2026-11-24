using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using PomoQuote.AiUsage;
using PomoQuote.Pomodoro;
using PomoQuote.Quotes;
using PomoQuote.Weather;
using PomoQuote.Widget;

namespace PomoQuote;

/// <summary>
/// The content of widget mode (280 logical px wide): the time, the weather, the Pomodoro phase with its pill progress bar, the
/// sentence in a message-bubble card and the AI usage area (Compact or Expanded, see WidgetView.Usage.cs), on a dark panel that
/// stays legible over any desktop. Everything is laid out in logical pixels and scaled by the display (width / 280), so it is
/// the same at 100, 125, 150 and 200%. Dragging it anywhere moves the window. It shares the app's controller and weather panel;
/// it has no timer of its own except a clock tick while it is visible.
/// </summary>
internal sealed partial class WidgetView : Control
{
    /// <summary>The widget's width at 100% display scale; the layout is designed on this grid and does not support less.</summary>
    public static readonly Size LogicalSize = new(UsageGeometry.WidgetWidth, 210);

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;

    private static readonly Color PanelColor = Color.FromArgb(28, 32, 40);
    private static readonly Color BorderColor = Color.FromArgb(84, 94, 110);
    private static readonly Color PrimaryText = Color.FromArgb(244, 246, 248);
    private static readonly Color SecondaryText = Color.FromArgb(190, 200, 212);
    private static readonly Color TrackColor = Color.FromArgb(62, 70, 84);
    private static readonly Color CardColor = Color.FromArgb(40, 50, 66);

    private readonly PomodoroController _pomodoro;
    private readonly WeatherPanel _weather;
    private readonly System.Windows.Forms.Timer _clockTick = new() { Interval = 500 };
    private string _shownTime = "";
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
        AccessibleName = "PomoQuote widget";
        AccessibleRole = AccessibleRole.Pane;
        InitializeUsage();

        _pomodoro.ViewChanged += (_, _) => Invalidate();
        _weather.Updated += (_, _) => Invalidate();
        _clockTick.Tick += (_, _) =>
        {
            var now = TimeText();
            if (now == _shownTime) return;
            _shownTime = now;
            Invalidate();
        };
        Relayout();
    }

    /// <summary>The clock tick only runs while the widget is on screen.</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        _clockTick.Enabled = Visible;
        if (Visible) Invalidate();
    }

    private static string TimeText() => DateTime.Now.ToString("HH:mm:ss");

    /// <summary>The device pixels of one logical pixel.</summary>
    private float Unit => Math.Max(0.5f, ClientSize.Width / (float)UsageGeometry.WidgetWidth);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_expanded && TabStop && e.Button == MouseButtons.Left) Focus(); // a click in Expanded gives the list the keyboard focus
        if (e.Button != MouseButtons.Left || FindForm() is not { } form) return;

        // Hands the drag to Windows as if the title bar had been pressed: the whole widget is a handle.
        ReleaseCapture();
        SendMessage(form.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PositionUsageControls();
    }

    // ---------- the quote card ----------

    private const int QuoteTop = 134;
    private const int QuoteInsetLeft = 14;
    private const int QuoteInsetRight = 12;
    private const int QuoteTail = 6;

    private QuoteGeometry.Plan? _quotePlan;
    /// <summary>Sets the sentence and its translation; returns true when the widget height changed (the card grows with the text).</summary>
    public bool SetQuote(string english, string? vietnamese)
    {
        _quoteEnglish = english;
        _quoteVietnamese = string.IsNullOrWhiteSpace(vietnamese) ? null : vietnamese;
        var before = LogicalHeight;
        Relayout();
        Invalidate();
        return before != LogicalHeight;
    }

    /// <summary>
    /// Breaks the text into lines for the card's width. The text keeps 12 px or more while the card can grow (up to a clamp); only
    /// past the clamp does it go smaller, and only past the smallest size is the last line ended with an ellipsis (see QuoteGeometry).
    /// </summary>
    private QuoteGeometry.Plan? PlanQuote()
    {
        if (_quoteEnglish.Length == 0) return null;
        var inner = UsageGeometry.WidgetWidth - 2 * UsageGeometry.Margin - QuoteInsetLeft - QuoteInsetRight - 4;
        using var graphics = Graphics.FromHwnd(IntPtr.Zero);
        using var typographic = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces };

        IReadOnlyList<string> Wrap(string text, string family, float pixels, int maxLines)
        {
            using var font = new Font(family, pixels, FontStyle.Regular, GraphicsUnit.Pixel);
            return TextWrap.Lines(text, inner, s => graphics.MeasureString(s, font, int.MaxValue, typographic).Width, maxLines);
        }

        var vietnamese = _quoteVietnamese;
        return QuoteGeometry.Choose(
            (pixels, maxLines) => Wrap(_quoteEnglish, QuoteLayout.EnglishFamily, pixels, maxLines),
            vietnamese is null ? null : (pixels, maxLines) => Wrap(vietnamese, QuoteLayout.VietnameseFamily, pixels, maxLines));
    }
    private void DrawQuote(Graphics graphics, float u)
    {
        if (_quotePlan is not { } plan) return;
        var card = new RectangleF(UsageGeometry.Margin * u, QuoteTop * u, (UsageGeometry.WidgetWidth - 2 * UsageGeometry.Margin) * u, plan.CardHeight * u);
        using (var path = PillBar.Rounded(card, 11 * u))
        using (var fill = new SolidBrush(CardColor))
        {
            graphics.FillPath(fill, path);
        }

        // The small tail of the bubble, at the lower left.
        using (var tail = new SolidBrush(CardColor))
        {
            var x = card.X + 18 * u;
            graphics.FillPolygon(tail, [new PointF(x, card.Bottom - 1 * u), new PointF(x + 9 * u, card.Bottom - 1 * u), new PointF(x - 1 * u, card.Bottom + QuoteTail * u)]);
        }

        // The big quotation marks are decoration: drawn, never text, so a screen reader does not read them out. They sit in the
        // corners the text leaves free.
        using (var marks = new Font("Georgia", 34 * u, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var markBrush = new SolidBrush(Color.FromArgb(70, 160, 180, 210)))
        using (var typographic = new StringFormat(StringFormat.GenericTypographic))
        {
            graphics.DrawString("“", marks, markBrush, card.X + 6 * u, card.Y - 5 * u, typographic);
            var closing = graphics.MeasureString("”", marks, int.MaxValue, typographic);
            graphics.DrawString("”", marks, markBrush, card.Right - closing.Width - 6 * u, card.Bottom - (QuoteGeometry.ClosingMarkVisibleHeight + 4) * u, typographic);
        }

        using var typo = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces };
        using var english = new Font(QuoteLayout.EnglishFamily, plan.EnglishPixels * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var englishBrush = new SolidBrush(Color.FromArgb(150, 236, 160));
        var x0 = card.X + QuoteInsetLeft * u;
        var y = card.Y + QuoteGeometry.PaddingTop * u;
        foreach (var line in plan.English)
        {
            graphics.DrawString(line, english, englishBrush, x0, y, typo);
            y += plan.EnglishPixels * QuoteGeometry.LineHeightFactor * u;
        }

        if (plan.Vietnamese is null) return;
        y += QuoteGeometry.GapBetweenTexts * u;
        using var vietnamese = new Font(QuoteLayout.VietnameseFamily, plan.VietnamesePixels * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var vietnameseBrush = new SolidBrush(Color.FromArgb(206, 216, 228));
        foreach (var line in plan.Vietnamese)
        {
            graphics.DrawString(line, vietnamese, vietnameseBrush, x0, y, typo);
            y += plan.VietnamesePixels * QuoteGeometry.LineHeightFactor * u;
        }
    }

    // ---------- height ----------

    /// <summary>Height of the widget without the quote card and the usage area, in logical px.</summary>
    public const int BaseLogicalHeight = QuoteTop;

    /// <summary>The widget's current height at 100% scale: the timer block, the quote card and the usage area.</summary>
    public int LogicalHeight { get; private set; } = 210;

    /// <summary>The widget's size at 100% scale with what it currently shows.</summary>
    public Size LogicalClientSize => new(UsageGeometry.WidgetWidth, LogicalHeight);

    private int _usageTop;

    private void Relayout()
    {
        _quotePlan = PlanQuote();
        var y = QuoteTop;
        if (_quotePlan is { } plan) y += (int)plan.CardHeight + QuoteTail + 6;
        _usageTop = y;
        var area = UsageGeometry.AreaHeight(_views, _expanded);
        LogicalHeight = Math.Max(150, y + area + (area > 0 ? 12 : 8));
        PositionUsageControls();
        UpdateAccessibleSummary();
    }

    // ---------- painting ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(PanelColor);

        var u = Unit;
        _hotSpots.Clear();
        var left = UsageGeometry.Margin * u;
        var width = ClientSize.Width - 2 * UsageGeometry.Margin * u;
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
        using (var small = new Font("Segoe UI", 12f * u, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            // Every weather value comes with its own icon, drawn crisp at exactly this size (see IconTextLine).
            var snapshot = _weather.Snapshot;
            var iconSize = (int)Math.Round(14 * u);
            IconTextLine.Draw(graphics, _weather.Icons, WeatherLines.City(snapshot), small, iconSize, 5 * u, left, 48 * u, SecondaryText, shadow: false);
            IconTextLine.Draw(graphics, _weather.Icons, WeatherLines.Summary(snapshot), small, iconSize, 5 * u, left, 65 * u, PrimaryText, shadow: false);
        }

        // Pomodoro: phase and remaining time, today's count, pill progress bar
        var timer = _pomodoro.Timer;
        var accent = PhaseColor(timer.Phase);
        var seconds = _pomodoro.WholeSecondsLeft();
        var phase = PomodoroController.PhaseName(timer.Phase) + (timer.State == PomodoroRunState.Paused ? " (paused)" : "");
        using (var bold = new Font("Segoe UI Semibold", 14 * u, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var small = new Font("Segoe UI", 12f * u, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var primary = new SolidBrush(PrimaryText))
        using (var secondary = new SolidBrush(SecondaryText))
        using (var dot = new SolidBrush(accent))
        {
            graphics.FillEllipse(dot, left, 92 * u, 9 * u, 9 * u);
            graphics.DrawString($"{phase}  {seconds / 60:00}:{seconds % 60:00}", bold, primary, new RectangleF(left + 15 * u, 87 * u, width - 15 * u, 20 * u), near);
            graphics.DrawString($"Today {_pomodoro.CompletedToday}", small, secondary, new RectangleF(left, 89 * u, width, 17 * u), farFormat);
        }

        PillBar.Draw(graphics, new RectangleF(left, 116 * u, width, 7 * u), timer.Progress, TrackColor, accent);
        _hotSpots.Add((new RectangleF(left, 85 * u, width, 42 * u), "timer", TimerTooltip.Text(timer.Phase, timer.State, timer.PhaseDuration, timer.Remaining, DateTime.Now)));

        DrawQuote(graphics, u);
        DrawUsage(graphics, u);
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
            DisposeUsage();
        }

        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
}
