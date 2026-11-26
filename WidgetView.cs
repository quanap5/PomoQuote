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


    // The colours in force: every colour the widget draws comes from this one theme (see WidgetTheme).
    private WidgetTheme _theme = WidgetTheme.Dark;
    private ThemeMode _themeMode = ThemeMode.System;

    private readonly PomodoroController _pomodoro;
    private readonly WeatherPanel _weather;
    private readonly System.Windows.Forms.Timer _clockTick = new() { Interval = 500 };
    private string _shownTime = "";
    private readonly DateLineCache _dateLines = new();
    private string _solarLine = "";
    private string _lunarLine = "";
    private string _pomodoroName = "";
    private string _quoteEnglish = "";
    private string? _quoteVietnamese;

    public WidgetView(PomodoroController pomodoro, WeatherPanel weather)
    {
        _pomodoro = pomodoro;
        _weather = weather;
        DoubleBuffered = true;
        ResizeRedraw = true;
        _theme = WidgetTheme.Resolve(_themeMode, SystemIsLight());
        _effects.LightPanel = !_theme.IsDark; // the weather effect draws for this panel from its first frame
        BackColor = _theme.Panel;
        Cursor = Cursors.SizeAll;
        AccessibleName = "PomoQuote widget";
        AccessibleRole = AccessibleRole.Pane;
        InitializeUsage();

        _pomodoro.ViewChanged += (_, _) => Invalidate();
        _weather.Updated += (_, _) => Invalidate();
        InitializeEffects();
        _clockTick.Tick += (_, _) =>
        {
            EvaluateEffects(); // picks up a minimised, covered or restored window within half a second
            var now = TimeText();
            if (now == _shownTime) return;
            _shownTime = now;
            RefreshDate(); // worked out again only when the local date changes (midnight, or a wake-up on another day)
            Invalidate();
        };
        RefreshDate();
        Relayout();
    }

    /// <summary>The clock tick only runs while the widget is on screen.</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        _clockTick.Enabled = Visible;
        EvaluateEffects();
        if (Visible) Invalidate();
    }

    // ---------- theme ----------

    /// <summary>Test seam: whether Windows uses its light theme for apps. The real value is read from the registry.</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<bool> SystemIsLightProbe { get; set; } = SystemTheme.IsLight;

    private bool SystemIsLight() => SystemIsLightProbe();

    /// <summary>Whether the weather effect draws for a light panel (dark particles); for the tests.</summary>
    internal bool EffectsLightPanel => _effects.LightPanel;

    /// <summary>The colours in force now.</summary>
    internal WidgetTheme Theme => _theme;

    /// <summary>The user's choice (System, Light or Dark).</summary>
    internal ThemeMode ThemeChoice => _themeMode;

    /// <summary>Applies the user's choice at once: no restart, nothing is read again, the widget just repaints with the new tokens.</summary>
    internal void ApplyTheme(ThemeMode mode)
    {
        _themeMode = mode;
        ResolveTheme();
    }

    /// <summary>Windows changed its colours: only System mode follows it.</summary>
    internal void SystemThemeChanged()
    {
        if (_themeMode == ThemeMode.System) ResolveTheme();
    }

    private void ResolveTheme()
    {
        var next = WidgetTheme.Resolve(_themeMode, SystemIsLight());
        _effects.LightPanel = !next.IsDark; // also when the theme itself did not change: the effect always matches the panel
        if (ReferenceEquals(next, _theme)) return;
        _theme = next;
        BackColor = next.Panel;
        _selector.BackColor = next.Panel;
        _selector.Theme = next;
        _toggle.BackColor = next.Panel;
        _toggle.Theme = next;
        _effects.LightPanel = !next.IsDark;
        _contentDirty = true; // the cached layer over a weather effect was drawn with the old colours
        Invalidate(true);
    }

    private static string TimeText() => DateTime.Now.ToString("HH:mm:ss");

    /// <summary>The two date lines after the clock; a cache keeps them for the whole day, so the clock tick allocates nothing for them.</summary>
    private void RefreshDate()
    {
        if (_dateLines.Update(DateOnly.FromDateTime(DateTime.Now), out _solarLine, out _lunarLine)) UpdateAccessibleSummary();
    }

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
        ResizeEffects();
        PositionUsageControls();
    }

    // ---------- the quote card ----------

    private const int QuoteTop = 134;
    private const int QuoteInsetLeft = 14;
    private const int QuoteInsetRight = 12;
    private const int QuoteTail = 6;

    private QuoteGeometry.Plan? _quotePlan;
    private string? _quoteAttribution;

    /// <summary>The tooltip on the sentence (credit, and for an expression its meaning and example), or null; for the tests.</summary>
    internal string? QuoteAttribution => _quoteAttribution;

    private const int BadgeHeight = 13;
    private string? _quoteBadge;

    /// <summary>The note drawn at the foot of the card (a machine translation that nobody reviewed), or null; for the tests.</summary>
    internal string? QuoteBadge => _quoteBadge;

    /// <summary>The card's height at 100% scale: the sentence, plus the note when there is one.</summary>
    private float CardHeight(QuoteGeometry.Plan plan) => plan.CardHeight + (_quoteBadge is null ? 0 : BadgeHeight);
    /// <summary>Sets the sentence and its translation; returns true when the widget height changed (the card grows with the text).</summary>
    public bool SetQuote(string english, string? vietnamese, string? attribution = null, string? badge = null)
    {
        _quoteAttribution = attribution;
        _quoteBadge = badge;
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
        var card = new RectangleF(UsageGeometry.Margin * u, QuoteTop * u, (UsageGeometry.WidgetWidth - 2 * UsageGeometry.Margin) * u, CardHeight(plan) * u);
        if (_quoteAttribution is not null) _hotSpots.Add((card, "quote", _quoteAttribution)); // the credit a Tatoeba sentence needs, as a tooltip
        using (var path = PillBar.Rounded(card, 11 * u))
        using (var fill = new SolidBrush(_theme.Card))
        {
            graphics.FillPath(fill, path);
        }

        // The small tail of the bubble, at the lower left.
        using (var tail = new SolidBrush(_theme.Card))
        {
            var x = card.X + 18 * u;
            graphics.FillPolygon(tail, [new PointF(x, card.Bottom - 1 * u), new PointF(x + 9 * u, card.Bottom - 1 * u), new PointF(x - 1 * u, card.Bottom + QuoteTail * u)]);
        }

        // The big quotation marks are decoration: drawn, never text, so a screen reader does not read them out. They sit in the
        // corners the text leaves free.
        using (var marks = new Font("Georgia", 34 * u, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var markBrush = new SolidBrush(_theme.QuoteMark))
        using (var typographic = new StringFormat(StringFormat.GenericTypographic))
        {
            graphics.DrawString("“", marks, markBrush, card.X + 6 * u, card.Y - 5 * u, typographic);
            var closing = graphics.MeasureString("”", marks, int.MaxValue, typographic);
            graphics.DrawString("”", marks, markBrush, card.Right - closing.Width - 6 * u, card.Bottom - (QuoteGeometry.ClosingMarkVisibleHeight + 4) * u, typographic);
        }

        using var typo = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces };
        using var english = new Font(QuoteLayout.EnglishFamily, plan.EnglishPixels * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var englishBrush = new SolidBrush(_theme.QuoteEnglish);
        var x0 = card.X + QuoteInsetLeft * u;
        var y = card.Y + QuoteGeometry.PaddingTop * u;
        foreach (var line in plan.English)
        {
            graphics.DrawString(line, english, englishBrush, x0, y, typo);
            y += plan.EnglishPixels * QuoteGeometry.LineHeightFactor * u;
        }

        if (_quoteBadge is not null)
        {
            using var note = new Font("Segoe UI", 9.5f * u, FontStyle.Italic, GraphicsUnit.Pixel);
            using var noteBrush = new SolidBrush(_theme.SecondaryText);
            using var noteFormat = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
            graphics.DrawString(_quoteBadge, note, noteBrush, new RectangleF(card.X + QuoteInsetLeft * u, card.Bottom - (BadgeHeight + 1) * u, card.Width - (QuoteInsetLeft + QuoteInsetRight + 16) * u, BadgeHeight * u), noteFormat);
        }

        if (plan.Vietnamese is null) return;
        y += QuoteGeometry.GapBetweenTexts * u;
        using var vietnamese = new Font(QuoteLayout.VietnameseFamily, plan.VietnamesePixels * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var vietnameseBrush = new SolidBrush(_theme.QuoteVietnamese);
        foreach (var line in plan.Vietnamese)
        {
            graphics.DrawString(line, vietnamese, vietnameseBrush, x0, y, typo);
            y += plan.VietnamesePixels * QuoteGeometry.LineHeightFactor * u;
        }
    }

    private static readonly float[] DateSizes = [12f, 11.5f, 11f, 10.5f, 10f];
    private readonly float[] _datePixels = [11.5f, 11.5f];
    private string? _measuredSolar;
    private string? _measuredLunar;
    private float _measuredRoom;
    private float _measuredUnit;

    /// <summary>
    /// The solar date over the lunar date, left aligned after the clock. Each line keeps 11.5 px while it fits the room that is
    /// left at 280 px, then goes down to 10 px, and only a line that still does not fit ends with an ellipsis. The sizes are
    /// worked out when the text, the room or the display scale changes, not at every tick.
    /// </summary>
    private void DrawDate(Graphics graphics, float u, float widestClockLogical, StringFormat typographic)
    {
        var room = DateBlockLayout.Room(widestClockLogical) * u;
        if (!ReferenceEquals(_measuredSolar, _solarLine) || !ReferenceEquals(_measuredLunar, _lunarLine) || _measuredRoom != room || _measuredUnit != u)
        {
            for (var i = 0; i < 2; i++)
            {
                var line = i == 0 ? _solarLine : _lunarLine;
                _datePixels[i] = DateSizes[^1];
                foreach (var size in DateSizes)
                {
                    using var candidate = new Font("Segoe UI", size * u, FontStyle.Regular, GraphicsUnit.Pixel);
                    if (graphics.MeasureString(line, candidate, int.MaxValue, typographic).Width > room) continue;
                    _datePixels[i] = size;
                    break;
                }
            }

            (_measuredSolar, _measuredLunar, _measuredRoom, _measuredUnit) = (_solarLine, _lunarLine, room, u);
        }

        using var brush = new SolidBrush(_theme.SecondaryText);
        // Right-aligned: every line ends flush at the right content margin, whatever size it was shrunk to.
        using var ellipsis = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, Alignment = StringAlignment.Far };
        for (var i = 0; i < 2; i++)
        {
            using var font = new Font("Segoe UI", _datePixels[i] * u, FontStyle.Regular, GraphicsUnit.Pixel);
            var line = DateBlockLayout.Line(i, widestClockLogical);
            graphics.DrawString(i == 0 ? _solarLine : _lunarLine, font, brush, new RectangleF(line.X * u, line.Y * u, line.Width * u, line.Height * u), ellipsis);
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
    private int _separatorY;

    /// <summary>Where the separator line between the quote and the usage area is, in logical px (the button sits centred on it).</summary>
    public int SeparatorY => _separatorY;

    /// <summary>Where the usage area starts, in logical px (the room below it decides how tall Expanded may be).</summary>
    public int UsageTop => _usageTop;

    private void Relayout()
    {
        _quotePlan = PlanQuote();
        var y = QuoteTop;
        if (_quotePlan is { } plan) y += (int)CardHeight(plan) + QuoteTail;
        var hasUsage = _views.Count > 0;
        // A separator line under the quote bubble, with the Expand/Collapse button centred on it; the usage area starts below it.
        _separatorY = y + UsageGeometry.SeparatorGap;
        _usageTop = hasUsage ? _separatorY + UsageGeometry.AreaTopGap : y + 6;
        var area = UsageGeometry.AreaHeight(_views, _expanded, _maxArea);
        LogicalHeight = Math.Max(150, hasUsage ? _usageTop + area + 12 : y + 8);
        PositionUsageControls();
        UpdateAccessibleSummary();
        _effects.SetSize(UsageGeometry.WidgetWidth, LogicalHeight);
    }

    // ---------- painting ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        if (PaintWithEffects(e.Graphics)) return;
        e.Graphics.Clear(_theme.Panel);
        PaintContent(e.Graphics);
    }

    /// <summary>Everything the widget shows except the panel colour: drawn straight on the panel, or into the cached layer that sits over a weather effect.</summary>
    private void PaintContent(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var u = Unit;
        _hotSpots.Clear();
        var left = UsageGeometry.Margin * u;
        var width = ClientSize.Width - 2 * UsageGeometry.Margin * u;
        using var near = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
        using var farFormat = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Far };

        using (var border = new Pen(_theme.Border, Math.Max(1f, u)))
        {
            graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        // Time, and after it the solar date and the Vietnamese lunar date on two lines
        using (var timeFont = new Font("Segoe UI Light", 30 * u, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var primary = new SolidBrush(_theme.PrimaryText))
        using (var typographic = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces })
        {
            var time = TimeText();
            graphics.DrawString(time, timeFont, primary, new RectangleF(left, 6 * u, width, 40 * u), near);
            var widestClock = graphics.MeasureString("88:88:88", timeFont, int.MaxValue, typographic).Width / u; // the widest the clock gets: the date never moves or overlaps
            DrawDate(graphics, u, widestClock, typographic);
        }

        // Weather: city on the left, temperature and conditions below
        using (var small = new Font("Segoe UI", 12f * u, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            // Every weather value comes with its own icon, drawn crisp at exactly this size (see IconTextLine).
            var snapshot = _weather.Snapshot;
            var iconSize = (int)Math.Round(14 * u);
            IconTextLine.Draw(graphics, _weather.Icons, WeatherLines.City(snapshot), small, iconSize, 5 * u, left, 48 * u, _theme.SecondaryText, shadow: false);
            IconTextLine.Draw(graphics, _weather.Icons, WeatherLines.Summary(snapshot), small, iconSize, 5 * u, left, 65 * u, _theme.PrimaryText, shadow: false);
        }

        // Pomodoro: phase and remaining time, today's count, pill progress bar
        var timer = _pomodoro.Timer;
        var accent = _theme.Phase(timer.Phase);
        var seconds = _pomodoro.WholeSecondsLeft();
        var phase = PomodoroController.PhaseName(timer.Phase) + (timer.State == PomodoroRunState.Paused ? " (paused)" : "");
        using (var bold = new Font("Segoe UI Semibold", 14 * u, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var small = new Font("Segoe UI", 12f * u, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var primary = new SolidBrush(_theme.PrimaryText))
        using (var secondary = new SolidBrush(_theme.SecondaryText))
        {
            // The state icon has the weather icons' left edge, size (14 px) and gap (5 px), and is centred on its row.
            var iconSize = (int)Math.Round(14 * u);
            var iconState = PomodoroIcon.StateOf(timer.Phase, timer.State);
            var iconBox = new RectangleF((int)Math.Round(left), (int)Math.Round(87 * u + (bold.Height - iconSize) / 2f), iconSize, iconSize);
            PomodoroIcon.Draw(graphics, iconBox, iconState, iconState == PomodoroIconState.Idle ? _theme.SecondaryText : accent, timer.State == PomodoroRunState.Paused, u, _theme.Panel, _theme.PrimaryText);
            graphics.DrawString($"{phase}  {seconds / 60:00}:{seconds % 60:00}", bold, primary, new RectangleF(left + iconSize + 5 * u, 87 * u, width - iconSize - 5 * u, 20 * u), near);
            graphics.DrawString($"Today {_pomodoro.CompletedToday}", small, secondary, new RectangleF(left, 89 * u, width, 17 * u), farFormat);
        }

        PillBar.Draw(graphics, new RectangleF(left, 116 * u, width, 7 * u), timer.Progress, _theme.Track, accent);
        _hotSpots.Add((new RectangleF(left, 85 * u, width, 42 * u), "timer", TimerTooltip.Text(timer.Phase, timer.State, timer.PhaseDuration, timer.Remaining, DateTime.Now)));
        var pomodoroName = PomodoroIcon.Name(timer.Phase, timer.State);
        if (pomodoroName != _pomodoroName)
        {
            _pomodoroName = pomodoroName;
            UpdateAccessibleSummary();
        }

        DrawQuote(graphics, u);
        if (_views.Count > 0)
        {
            using var separator = new Pen(_theme.Border, Math.Max(1f, u));
            graphics.DrawLine(separator, left, _separatorY * u, left + width, _separatorY * u);
        }

        DrawUsage(graphics, u);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clockTick.Stop();
            _clockTick.Dispose();
            DisposeEffects();
            DisposeUsage();
        }

        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
}
