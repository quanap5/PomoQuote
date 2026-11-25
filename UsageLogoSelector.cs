using System.Drawing.Drawing2D;
using System.Drawing.Text;
using PomoQuote.AiUsage;
using PomoQuote.Widget;

namespace PomoQuote;

/// <summary>
/// The provider logo on the far left of Compact: it stays where it is while the wheel (over the logo only), the arrow keys or the
/// list change the provider. Click or Enter opens the list. It is a real focusable control with an accessible name, and it shows
/// a small badge on the logo for an error, stale data or a window near its limit, plus a ring when another provider needs attention.
/// The logo is never recoloured to say an error: the state is the badge, the row text and the tooltip.
/// </summary>
internal sealed class UsageLogoSelector : Control
{
    private Bitmap? _icon;
    private string _letter = "?";
    private ProviderSeverity _severity;
    private bool _otherWarning;
    private bool _hot;
    private bool _refreshing;
    private WidgetTheme _theme = WidgetTheme.Dark;

    public UsageLogoSelector()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.ComboBox;
        AccessibleDefaultActionDescription = "Open the list of providers";
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

    /// <summary>The device pixels of one logical pixel (the badge and focus ring scale with it).</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float PixelsPerUnit { get; set; } = 1f;

    /// <summary>Raised with +1 (next) or -1 (previous) by the wheel over the logo and by the arrow keys.</summary>
    public event EventHandler<int>? Step;

    /// <summary>Raised by a click, Enter, Space or F4: the owner shows the list of providers.</summary>
    public event EventHandler? OpenList;

    /// <summary>What to draw now. <paramref name="icon"/> is owned by the icon cache; null draws a neutral letter tile.</summary>
    public void SetState(Bitmap? icon, string name, ProviderSeverity severity, bool otherWarning, string accessibleName, bool refreshing = false)
    {
        _refreshing = refreshing;
        _icon = icon;
        _letter = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?";
        _severity = severity;
        _otherWarning = otherWarning;
        AccessibleName = accessibleName;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Up or Keys.Left or Keys.PageUp:
                Step?.Invoke(this, -1);
                e.Handled = true;
                break;
            case Keys.Down or Keys.Right or Keys.PageDown:
                Step?.Invoke(this, 1);
                e.Handled = true;
                break;
            case Keys.Enter or Keys.Space or Keys.F4:
                OpenList?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Delta != 0) Step?.Invoke(this, e.Delta > 0 ? -1 : 1);
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left)
        {
            Focus();
            OpenList?.Invoke(this, EventArgs.Empty);
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
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var u = Math.Max(0.5f, PixelsPerUnit);

        var size = Widget.UsageGeometry.LogoSize * u;
        var box = new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size);
        if (_hot)
        {
            using var hot = new SolidBrush(_theme.SelectorHot);
            g.FillEllipse(hot, RectangleF.Inflate(box, 4 * u, 4 * u));
        }

        if (_icon is not null)
        {
            g.DrawImageUnscaled(_icon, (int)Math.Round(box.X), (int)Math.Round(box.Y));
        }
        else
        {
            // No icon allowed or found: a neutral tile with the first letter; the name is in the tooltip and the accessible name.
            using var tile = Widget.PillBar.Rounded(box, 5 * u);
            using var fill = new SolidBrush(_theme.TileFill);
            g.FillPath(fill, tile);
            using var font = new Font("Segoe UI Semibold", 11 * u, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(_theme.TileText);
            using var centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(_letter, font, brush, box, centre);
        }

        DrawBadge(g, box, u);
        if (_refreshing) WidgetGlyphs.DrawUpdating(g, new RectangleF(box.X - 7 * u, box.Y - 3 * u, 9 * u, 9 * u), u, _theme.SecondaryText);
        if (_otherWarning)
        {
            // Another provider needs attention: a small ring at the lower right of the logo.
            var ring = new RectangleF(box.Right - 3 * u, box.Bottom - 5 * u, 7 * u, 7 * u);
            using var pen = new Pen(_theme.Warning, Math.Max(1.5f, 1.6f * u));
            g.DrawEllipse(pen, ring);
        }

        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(_theme.Focus, Math.Max(1f, u)) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
        }
    }

    /// <summary>The badge of the logo: a red circle with ! for an error, an amber clock for stale data, an amber triangle near the limit.</summary>
    private void DrawBadge(Graphics g, RectangleF box, float u)
    {
        if (_severity == ProviderSeverity.None) return;
        var d = 10 * u;
        var badge = new RectangleF(box.Right - d * 0.7f, box.Y - d * 0.3f, d, d);
        using var ring = new SolidBrush(BackColor);
        g.FillEllipse(ring, RectangleF.Inflate(badge, 1.2f * u, 1.2f * u));
        switch (_severity)
        {
            case ProviderSeverity.Error:
                using (var red = new SolidBrush(_theme.Critical)) g.FillEllipse(red, badge);
                using (var font = new Font("Segoe UI", 8.5f * u, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var white = new SolidBrush(Color.White))
                using (var centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString("!", font, white, badge, centre);
                }

                break;
            case ProviderSeverity.Stale:
                using (var amber = new SolidBrush(_theme.Stale)) g.FillEllipse(amber, badge);
                using (var hands = new Pen(_theme.OnAmber, Math.Max(1f, 1.2f * u)))
                {
                    var c = new PointF(badge.X + d / 2, badge.Y + d / 2);
                    g.DrawLine(hands, c.X, c.Y, c.X, c.Y - d * 0.28f);
                    g.DrawLine(hands, c.X, c.Y, c.X + d * 0.22f, c.Y);
                }

                break;
            default:
                using (var amber = new SolidBrush(_theme.Warning))
                {
                    g.FillPolygon(amber, [new PointF(badge.X + d / 2, badge.Y), new PointF(badge.Right, badge.Bottom), new PointF(badge.X, badge.Bottom)]);
                }

                break;
        }
    }
}
