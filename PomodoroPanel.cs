using System.Drawing.Drawing2D;
using System.Drawing.Text;
using HelloWorldBot.Pomodoro;

namespace HelloWorldBot;

/// <summary>
/// The Pomodoro panel: phase name, a progress ring around the remaining mm:ss, today's session count,
/// and Start/Pause/Resume, Skip and Reset buttons. Transparent so a wallpaper shows behind it.
/// </summary>
internal sealed class PomodoroPanel : Control
{
    private static readonly Color PlainTextColor = Color.FromArgb(45, 45, 45);

    private readonly PomodoroController _controller;
    private readonly Button _startPauseButton = new() { AutoSize = false };
    private readonly Button _skipButton = new() { Text = "Skip", AutoSize = false };
    private readonly Button _resetButton = new() { Text = "Reset", AutoSize = false };
    private bool _onWallpaper;

    public PomodoroPanel(PomodoroController controller)
    {
        _controller = controller;
        DoubleBuffered = true;
        ResizeRedraw = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Padding = new Padding(8);

        _startPauseButton.Click += (_, _) => OnStartPause();
        _skipButton.Click += (_, _) => _controller.Timer.Skip();
        _resetButton.Click += (_, _) => _controller.Timer.Reset();
        Controls.AddRange([_startPauseButton, _skipButton, _resetButton]);

        _controller.ViewChanged += (_, _) => RefreshView();
        RefreshView();
    }

    /// <summary>True while a wallpaper is behind the panel, so the text needs the light style.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool OnWallpaper
    {
        get => _onWallpaper;
        set
        {
            if (_onWallpaper == value) return;
            _onWallpaper = value;
            Invalidate();
        }
    }

    /// <summary>Height below which the ring, text and buttons no longer fit.</summary>
    public int MeasureMinimumHeight() => ButtonHeight + Font.Height * 2 + LogicalRingMinimum + Padding.Vertical;

    private int ButtonHeight => Math.Max(26, (int)(Font.Height * 1.9));

    private int LogicalRingMinimum => (int)(Font.Height * 6.5);

    private void OnStartPause()
    {
        var timer = _controller.Timer;
        if (timer.State == PomodoroRunState.Running) timer.Pause();
        else timer.Start();
    }

    private void RefreshView()
    {
        _startPauseButton.Text = _controller.Timer.State switch
        {
            PomodoroRunState.Running => "Pause",
            PomodoroRunState.Paused => "Resume",
            _ => "Start",
        };
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        var gap = 6;
        var width = Math.Max(1, (ClientSize.Width - Padding.Horizontal - gap * 2) / 3);
        var top = ClientSize.Height - Padding.Bottom - ButtonHeight;
        _startPauseButton.SetBounds(Padding.Left, top, width, ButtonHeight);
        _skipButton.SetBounds(Padding.Left + width + gap, top, width, ButtonHeight);
        _resetButton.SetBounds(Padding.Left + (width + gap) * 2, top, width, ButtonHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var timer = _controller.Timer;

        using var textFormat = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
        };

        var phaseText = PomodoroController.PhaseName(timer.Phase) + (timer.State == PomodoroRunState.Paused ? " (paused)" : "");
        using var titleFont = new Font("Segoe UI Semibold", 12f, FontStyle.Regular, GraphicsUnit.Point);
        var titleArea = new RectangleF(Padding.Left, Padding.Top, ClientSize.Width - Padding.Horizontal, titleFont.Height);
        DrawText(graphics, phaseText, titleFont, titleArea, textFormat);

        var countText = $"Today: {_controller.CompletedToday} {(_controller.CompletedToday == 1 ? "session" : "sessions")}";
        using var smallFont = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        var buttonsTop = ClientSize.Height - Padding.Bottom - ButtonHeight;
        var countArea = new RectangleF(Padding.Left, buttonsTop - smallFont.Height - 2, ClientSize.Width - Padding.Horizontal, smallFont.Height);
        DrawText(graphics, countText, smallFont, countArea, textFormat);

        var ringTop = titleArea.Bottom + 4;
        var ringBottom = countArea.Top - 4;
        var diameter = Math.Min(ClientSize.Width - Padding.Horizontal, ringBottom - ringTop);
        if (diameter < 40) return;

        var stroke = Math.Max(6f, diameter / 14f);
        var ring = new RectangleF(
            (ClientSize.Width - diameter) / 2f + stroke / 2f,
            ringTop + (ringBottom - ringTop - diameter) / 2f + stroke / 2f,
            diameter - stroke,
            diameter - stroke);

        using (var track = new Pen(Color.FromArgb(_onWallpaper ? 110 : 90, 140, 140, 140), stroke))
        {
            graphics.DrawEllipse(track, ring);
        }

        var sweep = (float)(360 * timer.Progress);
        if (sweep > 0.5f)
        {
            using var arc = new Pen(PhaseColor(timer.Phase), stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawArc(arc, ring, -90f, sweep);
        }

        var seconds = _controller.WholeSecondsLeft();
        using var timeFont = new Font("Segoe UI", Math.Max(10f, diameter * 0.17f), FontStyle.Regular, GraphicsUnit.Pixel);
        DrawText(graphics, $"{seconds / 60:00}:{seconds % 60:00}", timeFont, ring, textFormat);
    }

    private static Color PhaseColor(PomodoroPhase phase) => phase switch
    {
        PomodoroPhase.Work => Color.FromArgb(226, 76, 60),
        PomodoroPhase.ShortBreak => Color.FromArgb(46, 160, 90),
        _ => Color.FromArgb(52, 120, 220),
    };

    private void DrawText(Graphics graphics, string text, Font font, RectangleF area, StringFormat format)
    {
        if (_onWallpaper)
        {
            using var shadow = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
            graphics.DrawString(text, font, shadow, new RectangleF(area.X + 1, area.Y + 1, area.Width, area.Height), format);
        }

        using var brush = new SolidBrush(_onWallpaper ? Color.White : PlainTextColor);
        graphics.DrawString(text, font, brush, area, format);
    }
}
