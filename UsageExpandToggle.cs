using System.Drawing.Drawing2D;
using PomoQuote.Widget;

namespace PomoQuote;

/// <summary>
/// The small Expand/Collapse button of the AI usage area: a chevron in the right margin (so Compact needs no extra row). It is
/// focusable, has an accessible name that says what it will do, and in Expanded the Up and Down keys scroll the area.
/// </summary>
internal sealed class UsageExpandToggle : Control
{
    private bool _expanded;
    private bool _hot;
    private WidgetTheme _theme = WidgetTheme.Dark;

    public UsageExpandToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
        Expanded = false;
    }

    /// <summary>The colours in force (set by the widget; a change repaints).</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public WidgetTheme Theme
    {
        get => _theme;
        set
        {
            if (ReferenceEquals(_theme, value)) return;
            _theme = value;
            Invalidate();
        }
    }

    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float PixelsPerUnit { get; set; } = 1f;

    /// <summary>Raised when the user asks for the other layout (click, Enter or Space).</summary>
    public event EventHandler? Toggled;

    /// <summary>Raised in Expanded by Up/Down: -1 or +1 step of scrolling.</summary>
    public event EventHandler<int>? ScrollRequested;

    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Expanded
    {
        get => _expanded;
        set
        {
            _expanded = value;
            AccessibleName = value ? "Collapse AI usage" : "Expand AI usage";
            AccessibleDefaultActionDescription = value ? "Show one provider" : "Show every provider";
            Invalidate();
        }
    }

    public string TipText => _expanded ? "Collapse: show one provider" : "Expand: show every provider";

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            Toggled?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (_expanded && e.KeyCode is Keys.Up or Keys.Down)
        {
            ScrollRequested?.Invoke(this, e.KeyCode == Keys.Up ? -1 : 1);
            e.Handled = true;
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left)
        {
            Focus();
            Toggled?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hot = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = false;
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var u = Math.Max(0.5f, PixelsPerUnit);
        // A small pill button sitting on the separator line: its own panel-coloured face hides the line behind it.
        using (var face = new SolidBrush(_hot ? _theme.ButtonHot : BackColor))
        using (var outline = new Pen(_theme.ButtonOutline, Math.Max(1f, u)))
        using (var path = Widget.PillBar.Pill(new RectangleF(0.5f * u, 0.5f * u, Width - 1 * u, Height - 1 * u)))
        {
            g.FillPath(face, path);
            g.DrawPath(outline, path);
        }
        using var pen = new Pen(_hot ? _theme.GlyphHot : _theme.Glyph, Math.Max(1.5f, 1.7f * u)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var cx = Width / 2f;
        var cy = Height / 2f;
        var half = 3.4f * u;
        var tilt = _expanded ? -1 : 1; // down chevron to expand, up chevron to collapse
        g.DrawLines(pen, [new PointF(cx - half, cy - tilt * half * 0.5f), new PointF(cx, cy + tilt * half * 0.5f), new PointF(cx + half, cy - tilt * half * 0.5f)]);

        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(_theme.Focus, Math.Max(1f, u)) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(focus, 0, 0, Width - 1, Height - 1);
        }
    }
}
