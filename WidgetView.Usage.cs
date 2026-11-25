using System.Drawing.Drawing2D;
using System.Drawing.Text;
using PomoQuote.AiUsage;
using PomoQuote.Widget;

namespace PomoQuote;

/// <summary>What the user chose in the usage area: the layout and the provider Compact shows.</summary>
internal sealed class UsageUiEventArgs(bool expanded, string? selectedProvider) : EventArgs
{
    public bool Expanded { get; } = expanded;

    public string? SelectedProvider { get; } = selectedProvider;
}

/// <summary>
/// The AI usage area of the widget. Compact: one provider, its logo on the far left (a focusable selector: the wheel over the logo
/// steps through the providers, a click opens the list) and its two quota rows on the right; no header, no footer, the details
/// are in the logo's tooltip. Expanded: a card per enabled provider with the logo on the far left, full name, plan, every window,
/// source and update time; only this area scrolls. The Expand/Collapse button sits in the right margin, so Compact needs no row of its own.
/// </summary>
internal sealed partial class WidgetView
{

    private readonly ProviderIcons _icons = new();
    private readonly ToolTip _tip = new() { InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 9000, ShowAlways = true };
    private readonly List<(RectangleF Bounds, string Key, string Text)> _hotSpots = [];
    private UsageLogoSelector _selector = null!;
    private UsageExpandToggle _toggle = null!;
    private IReadOnlyList<UsageProviderView> _views = [];
    private bool _expanded;
    private string? _selected;
    private int _scroll;
    private int _maxArea = UsageGeometry.MaxExpandedHeight;
    private string? _tipKey;
    private int _focusedCard = -1;
    private bool _listFocused;
    private bool _keyboardUsed;

    /// <summary>The card that has the keyboard focus in Expanded (-1 when none); for the tests.</summary>
    internal int FocusedCard => _focusedCard;

    /// <summary>How far the Expanded area is scrolled, in logical px; for the tests.</summary>
    internal int ScrollOffset => _scroll;

    /// <summary>Raised when the user switches the layout or the provider; the owner remembers it.</summary>
    public event EventHandler<UsageUiEventArgs>? UsageUiChanged;

    private void InitializeUsage()
    {
        _selector = new UsageLogoSelector { BackColor = _theme.Panel, Theme = _theme, Visible = false };
        _selector.Step += (_, direction) => StepProvider(direction);
        _selector.OpenList += (_, _) => ShowProviderList();
        _toggle = new UsageExpandToggle { BackColor = _theme.Panel, Theme = _theme, Visible = false };
        _toggle.Toggled += (_, _) => SetExpanded(!_expanded);
        _toggle.ScrollRequested += (_, direction) => ScrollBy(direction * UsageGeometry.RowHeight);
        SetStyle(ControlStyles.Selectable, true);
        TabStop = false; // a tab stop only in Expanded (see PositionUsageControls)
        Controls.Add(_selector);
        Controls.Add(_toggle);
        _tip.SetToolTip(_toggle, _toggle.TipText);
    }

    protected override void OnContextMenuStripChanged(EventArgs e)
    {
        base.OnContextMenuStripChanged(e);
        if (_selector is null) return;
        _selector.ContextMenuStrip = ContextMenuStrip;
        _toggle.ContextMenuStrip = ContextMenuStrip;
    }

    /// <summary>
    /// Gives the usage area its content and layout; returns true when the widget height changed (the window is then resized).
    /// <paramref name="selectedProvider"/> is the provider Compact shows (a missing one falls back to the first).
    /// </summary>
    public bool SetUsage(Func<IReadOnlyList<UsageProviderView>> views, bool expanded, string? selectedProvider, int maxExpandedHeight = UsageGeometry.MaxExpandedHeight)
    {
        _maxArea = Math.Clamp(maxExpandedHeight, UsageGeometry.CompactHeight, UsageGeometry.MaxExpandedHeight);
        _views = views();
        _expanded = expanded;
        _selected = ProviderCarousel.Resolve(_views.Select(v => v.Id).ToList(), selectedProvider);
        _scroll = Math.Clamp(_scroll, 0, UsageGeometry.MaxScroll(_views, _maxArea));
        var before = LogicalHeight;
        Relayout();
        Invalidate();
        return before != LogicalHeight;
    }

    private UsageProviderView? Selected => _views.FirstOrDefault(v => v.Id == _selected) ?? _views.FirstOrDefault();

    // ---------- interaction ----------

    private void SetExpanded(bool expanded)
    {
        if (expanded == _expanded) return;
        _expanded = expanded;
        _scroll = 0;
        Relayout();
        Invalidate();
        UsageUiChanged?.Invoke(this, new UsageUiEventArgs(_expanded, _selected));
    }

    private void StepProvider(int direction)
    {
        var next = ProviderCarousel.Step(_views.Select(v => v.Id).ToList(), _selected, direction);
        SelectProvider(next);
    }

    private void SelectProvider(string? id)
    {
        if (id is null || id == _selected) return;
        _selected = id;
        PositionUsageControls();
        Invalidate();
        UsageUiChanged?.Invoke(this, new UsageUiEventArgs(_expanded, _selected));
    }

    private void ShowProviderList()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = true };
        foreach (var view in _views)
        {
            var title = view.Plan.Length > 0 ? $"{view.Name}  ({view.Plan})" : view.Name;
            var icon = _icons.Get(view.Id, IconBackground.Light, (int)Math.Round(16 * Unit));
            var item = new ToolStripMenuItem(title, icon) { Checked = view.Id == _selected, Tag = view.Id, ToolTipText = view.Tip };
            var id = view.Id;
            item.Click += (_, _) => SelectProvider(id);
            menu.Items.Add(item);
        }

        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_selector, new Point(0, _selector.Height));
    }

    private void ScrollBy(int logicalDelta)
    {
        var max = UsageGeometry.MaxScroll(_views, _maxArea);
        var next = Math.Clamp(_scroll + logicalDelta, 0, max);
        if (next == _scroll) return;
        _scroll = next;
        Invalidate();
    }

    // ---------- Expanded as a keyboard-operable list ----------

    protected override bool IsInputKey(Keys keyData) =>
        (_expanded && _views.Count > 0 && keyData is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown) || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!_expanded || _views.Count == 0) return;
        var last = _views.Count - 1;
        var current = Math.Clamp(_focusedCard, 0, last);
        var next = e.KeyCode switch
        {
            Keys.Down or Keys.PageDown => Math.Min(last, current + 1),
            Keys.Up or Keys.PageUp => Math.Max(0, current - 1),
            Keys.Home => 0,
            Keys.End => last,
            _ => -1,
        };
        if (next < 0) return;
        e.Handled = true;
        _keyboardUsed = true; // the focus ring shows once the keyboard has been used, as Windows does
        FocusCard(next);
    }

    private void FocusCard(int index)
    {
        _focusedCard = index;
        EnsureCardVisible(index);
        Invalidate();
        AccessibilityNotifyClients(AccessibleEvents.Focus, index + (_selector is null ? 0 : Controls.Count));
    }

    /// <summary>Scrolls the Expanded area just enough to show the whole card.</summary>
    private void EnsureCardVisible(int index)
    {
        var top = _views.Take(index).Sum(v => UsageGeometry.CardHeight(v) + UsageGeometry.CardGap);
        var bottom = top + UsageGeometry.CardHeight(_views[index]);
        var visible = UsageGeometry.AreaHeight(_views, true, _maxArea);
        var wanted = _scroll;
        if (top < wanted) wanted = top;
        else if (bottom > wanted + visible) wanted = bottom - visible;
        _scroll = Math.Clamp(wanted, 0, UsageGeometry.MaxScroll(_views, _maxArea));
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _listFocused = true;
        if (_expanded && _views.Count > 0 && _focusedCard < 0) _focusedCard = 0;
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _listFocused = false;
        Invalidate();
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new WidgetAccessibleObject(this);

    /// <summary>The widget's accessible tree: its two controls and, in Expanded, one list item per provider card with a name and a description.</summary>
    private sealed class WidgetAccessibleObject(WidgetView owner) : ControlAccessibleObject(owner)
    {
        private int CardCount => owner._expanded ? owner._views.Count : 0;

        // WinForms answers -1 ("use the window's own children") for a control, so the children are listed here: the visible
        // controls (the logo selector or the arrow button) first, then, in Expanded, one list item per provider card.
        private List<Control> VisibleControls => owner.Controls.Cast<Control>().Where(c => c.Visible).ToList();

        public override int GetChildCount() => VisibleControls.Count + CardCount;

        public override AccessibleObject? GetChild(int index)
        {
            var controls = VisibleControls;
            return index < controls.Count ? controls[index].AccessibilityObject : index - controls.Count < CardCount ? new CardAccessibleObject(this, owner, index - controls.Count) : null;
        }
    }
    private sealed class CardAccessibleObject(WidgetAccessibleObject parent, WidgetView owner, int index) : AccessibleObject
    {
        private UsageProviderView View => owner._views[index];

        public override AccessibleObject Parent => parent;

        public override string Name => View.Plan.Length > 0 ? $"{View.Name}, {View.Plan}" : View.Name;

        public override string Description =>
            (View.HasData ? string.Join("; ", View.Rows.Select(r => $"{r.Label} {r.Percent} used, resets in {(r.Reset.Length > 0 ? r.Reset : "unknown")}")) : "N/A")
            + $". {(View.Source.Length > 0 ? View.Source + ", " : "")}{View.Note}.";

        public override AccessibleRole Role => AccessibleRole.ListItem;

        public override AccessibleStates State =>
            AccessibleStates.Focusable | AccessibleStates.Selectable | (owner._listFocused && owner._focusedCard == index ? AccessibleStates.Focused | AccessibleStates.Selected : AccessibleStates.None);

        public override Rectangle Bounds
        {
            get
            {
                var u = owner.Unit;
                var top = owner._views.Take(index).Sum(v => UsageGeometry.CardHeight(v) + UsageGeometry.CardGap);
                var rect = new Rectangle((int)Math.Round(UsageGeometry.Margin * u), (int)Math.Round((owner._usageTop + top - owner._scroll) * u),
                    (int)Math.Round((UsageGeometry.WidgetWidth - 2 * UsageGeometry.Margin - UsageGeometry.ScrollGutter) * u), (int)Math.Round(UsageGeometry.CardHeight(View) * u));
                return owner.RectangleToScreen(rect);
            }
        }

        public override void DoDefaultAction() => owner.FocusCard(index);

        public override string DefaultAction => "Focus";
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        // Only the usage area scrolls, and only in Expanded; the clock and the timer stay where they are. Compact is changed by the logo only.
        if (!_expanded || _views.Count == 0) return;
        var u = Unit;
        var area = new RectangleF(0, _usageTop * u, ClientSize.Width, UsageGeometry.AreaHeight(_views, true, _maxArea) * u);
        if (area.Contains(e.Location)) ScrollBy(e.Delta > 0 ? -UsageGeometry.RowHeight : UsageGeometry.RowHeight);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        string? key = null;
        string? text = null;
        foreach (var (bounds, spotKey, spotText) in _hotSpots)
        {
            if (bounds.Contains(e.Location))
            {
                key = spotKey;
                text = spotText;
                break;
            }
        }

        // The timer's text changes every second: it is shown once per visit, not re-shown at each tick.
        if (key == _tipKey) return;
        _tipKey = key;
        if (text is null) _tip.Hide(this);
        else _tip.Show(text, this, e.X + 12, e.Y + 18, 7000);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _tipKey = null;
        _tip.Hide(this);
    }

    // ---------- children and accessibility ----------

    private void PositionUsageControls()
    {
        if (_selector is null) return;
        var u = Unit;
        var show = _views.Count > 0;
        _selector.PixelsPerUnit = u;
        _toggle.PixelsPerUnit = u;
        _toggle.Expanded = _expanded;
        _toggle.Visible = show;
        TabStop = show && _expanded;
        if (!_expanded) _focusedCard = -1;
        else if (show) _focusedCard = Math.Min(_focusedCard, _views.Count - 1);
        _selector.Visible = show && !_expanded;

        var areaHeight = UsageGeometry.AreaHeight(_views, _expanded, _maxArea);
        _selector.Bounds = new Rectangle((int)Math.Round(UsageGeometry.Margin * u), (int)Math.Round(_usageTop * u), (int)Math.Round(UsageGeometry.LogoColumn * u), (int)Math.Round(UsageGeometry.CompactHeight * u));
        // The button sits on the separator line, centred horizontally: no extra row, and the same place in Compact and Expanded.
        var toggleWidth = (int)Math.Round(UsageGeometry.ToggleWidth * u);
        var toggleHeight = (int)Math.Round(UsageGeometry.ToggleHeight * u);
        _toggle.Bounds = new Rectangle((ClientSize.Width - toggleWidth) / 2, (int)Math.Round(_separatorY * u) - toggleHeight / 2, toggleWidth, toggleHeight);
        _tip.SetToolTip(_toggle, _toggle.TipText);

        var view = Selected;
        if (view is null || _expanded)
        {
            _selector.SetState(null, "", ProviderSeverity.None, false, "AI usage provider");
            return;
        }

        var others = _views.Where(v => v.Id != view.Id).ToList();
        var otherWarning = others.Any(v => v.Severity != ProviderSeverity.None);
        var index = _views.ToList().FindIndex(v => v.Id == view.Id);
        var names = _views.Select(v => v.Name).ToList();
        var position = ProviderCarousel.Position(names, Math.Max(0, index));
        var icon = _icons.Get(view.Id, _theme.LogoBackground, (int)Math.Round(UsageGeometry.LogoSize * u));
        var tip = view.Tip + (otherWarning ? "\n" + string.Join("; ", others.Where(v => v.Severity != ProviderSeverity.None).Select(v => $"{v.Name}: {v.Note}")) : "")
            + (_views.Count > 1 ? "\nScroll over the logo to switch provider; click to choose." : "");
        _selector.SetState(icon, view.Name, view.Severity, otherWarning, $"AI usage provider: {position}. {view.Name} {(view.HasData ? string.Join(", ", view.Rows.Select(r => $"{r.Label} {r.Percent} used")) : "no data")}{(view.Refreshing ? ", updating" : "")}. Use the arrow keys to switch, Enter to choose from a list.", view.Refreshing);
        _tip.SetToolTip(_selector, tip);
        _ = areaHeight;
    }

    private void UpdateAccessibleSummary()
    {
        // A text summary for screen readers: the quote without the decorative marks, and the usage numbers.
        var parts = new List<string>();
        if (_pomodoroName.Length > 0) parts.Add(_pomodoroName + ".");
        if (_solarLine.Length > 0) parts.Add($"{_solarLine}. {_lunarLine}.");
        if (_quoteEnglish.Length > 0) parts.Add(_quoteEnglish + (_quoteVietnamese is null ? "" : " " + _quoteVietnamese));
        foreach (var view in _views)
        {
            parts.Add($"{view.Name}: " + (view.HasData ? string.Join(", ", view.Rows.Select(r => $"{r.Label} {r.Percent} used, resets in {(r.Reset.Length > 0 ? r.Reset : "unknown")}")) : "N/A") + $". {view.Note}.");
        }

        AccessibleDescription = string.Join(" ", parts);
    }

    // ---------- painting ----------

    private void DrawUsage(Graphics graphics, float u)
    {
        if (_views.Count == 0) return;

        var area = new RectangleF(UsageGeometry.Margin * u, _usageTop * u, (UsageGeometry.WidgetWidth - 2 * UsageGeometry.Margin) * u, UsageGeometry.AreaHeight(_views, _expanded, _maxArea) * u);
        if (_expanded) DrawExpanded(graphics, u, area);
        else if (Selected is { } view) DrawCompact(graphics, u, area, view);
    }

    private void DrawCompact(Graphics graphics, float u, RectangleF area, UsageProviderView view)
    {
        var rowsLeft = area.X + UsageGeometry.LogoColumn * u;
        var columns = ScaledColumns(rowsLeft, UsageGeometry.CompactRowsWidth, u);
        using var text = new Font("Segoe UI", 12f * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var semibold = new Font("Segoe UI Semibold", 12f * u, FontStyle.Regular, GraphicsUnit.Pixel);
        var rowHeight = UsageGeometry.RowHeight * u;
        var y = area.Y + 5 * u;

        if (!view.HasData)
        {
            // No quota read: say so plainly (N/A, never 0%) and why.
            using var primary = new SolidBrush(_theme.PrimaryText);
            using var near = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
            graphics.DrawString("N/A", semibold, primary, new RectangleF(rowsLeft, y, columns.LabelWidth + columns.BarWidth, rowHeight), near);
            var room = columns.Right - rowsLeft;
            var note = UsageViewBuilder.FitNote(view, room, s => graphics.MeasureString(s, text, int.MaxValue, near).Width);
            using var noteBrush = new SolidBrush(view.IsProblem ? _theme.Warning : _theme.SecondaryText);
            graphics.DrawString(note, text, noteBrush, new RectangleF(rowsLeft, y + rowHeight, room, rowHeight), near);
            _hotSpots.Add((new RectangleF(rowsLeft, area.Y, room, area.Height), "na:" + view.Id, view.Tip));
            return;
        }

        foreach (var row in view.Rows.Take(2))
        {
            DrawRow(graphics, u, columns, y, row, text, semibold);
            y += rowHeight;
        }
    }

    private void DrawRow(Graphics graphics, float u, UsageGeometry.RowColumns c, float y, UsageRow row, Font text, Font semibold)
    {
        var rowHeight = UsageGeometry.RowHeight * u;
        using var near = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
        using var far = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
        using var secondary = new SolidBrush(_theme.SecondaryText);
        using var primary = new SolidBrush(_theme.PrimaryText);

        graphics.DrawString(row.Label, text, secondary, new RectangleF(c.LabelX, y, c.LabelWidth, rowHeight), near);
        var barHeight = 7 * u;
        PillBar.Draw(graphics, new RectangleF(c.BarX, y + (rowHeight - barHeight) / 2f, c.BarWidth, barHeight), row.Fraction, _theme.Track, LevelColor(row.Level));
        graphics.DrawString(row.Percent, semibold, primary, new RectangleF(c.PercentX, y, c.PercentWidth, rowHeight), far);
        if (row.Stale) DrawClock(graphics, new RectangleF(c.PercentX - 1 * u, y + (rowHeight - 9 * u) / 2f, 9 * u, 9 * u), u);
        graphics.DrawString(row.Reset.Length > 0 ? row.Reset : "–", text, secondary, new RectangleF(c.ResetX, y, c.ResetWidth, rowHeight), far);
        _hotSpots.Add((new RectangleF(c.LabelX, y, c.Right - c.LabelX, rowHeight), "row:" + row.Label + ":" + y, row.Tip));
    }

    /// <summary>The row columns (designed in logical px) for this scale: every position and width multiplied by the display unit, from the device left edge.</summary>
    private static UsageGeometry.RowColumns ScaledColumns(float left, float logicalWidth, float u)
    {
        var c = UsageGeometry.Columns(0, logicalWidth);
        return new UsageGeometry.RowColumns(left + c.LabelX * u, c.LabelWidth * u, left + c.BarX * u, c.BarWidth * u, left + c.PercentX * u, c.PercentWidth * u, left + c.ResetX * u, c.ResetWidth * u);
    }

    private void DrawClock(Graphics graphics, RectangleF box, float u)
    {
        using var pen = new Pen(_theme.Stale, Math.Max(1f, 1.1f * u));
        graphics.DrawEllipse(pen, box);
        var cx = box.X + box.Width / 2;
        var cy = box.Y + box.Height / 2;
        graphics.DrawLine(pen, cx, cy, cx, cy - box.Height * 0.3f);
        graphics.DrawLine(pen, cx, cy, cx + box.Width * 0.24f, cy);
    }

    private void DrawExpanded(Graphics graphics, float u, RectangleF area)
    {
        var state = graphics.Save();
        graphics.SetClip(area);
        var blockWidth = area.Width - UsageGeometry.ScrollGutter * u;
        var y = area.Y - _scroll * u;
        using var name = new Font("Segoe UI Semibold", 12f * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var text = new Font("Segoe UI", 12f * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var footer = new Font("Segoe UI", 11f * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var primary = new SolidBrush(_theme.PrimaryText);
        using var near = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter };
        using var centred = new StringFormat(near) { LineAlignment = StringAlignment.Center };

        var index = -1;
        foreach (var view in _views)
        {
            index++;
            // A provider block is plain: no background, border or rounded container, only space between the blocks.
            var block = new RectangleF(area.X, y, blockWidth, UsageGeometry.CardHeight(view) * u);
            y = block.Bottom + UsageGeometry.CardGap * u;
            if (block.Bottom < area.Y || block.Y > area.Bottom) continue;

            if (Focused && (ShowFocusCues || _keyboardUsed) && index == _focusedCard)
            {
                using var ring = new Pen(_theme.Focus, Math.Max(1f, u)) { DashStyle = DashStyle.Dot };
                graphics.DrawRectangle(ring, block.X - 2 * u, block.Y - 1 * u, block.Width + 4 * u, block.Height + 2 * u);
            }

            // The logo: far left, centred on the block.
            var logo = UsageGeometry.LogoSize * u;
            var logoBox = new RectangleF(block.X + (UsageGeometry.LogoColumn - UsageGeometry.LogoSize) * u / 2f, block.Y + (block.Height - logo) / 2f, logo, logo);
            DrawLogo(graphics, view, logoBox, u);
            _hotSpots.Add((new RectangleF(block.X, block.Y, UsageGeometry.LogoColumn * u, block.Height), "logo:" + view.Id, view.Tip));

            var left = block.X + UsageGeometry.LogoColumn * u;
            var right = block.Right;
            var rowTop = block.Y + UsageGeometry.HeaderHeight * u;
            var header = view.Plan.Length > 0 ? $"{view.Name}  \u00B7  {view.Plan}" : view.Name;
            graphics.DrawString(header, name, primary, new RectangleF(left, block.Y, right - left - (view.Refreshing ? 14 * u : 0), UsageGeometry.HeaderHeight * u), near);
            if (view.Refreshing) WidgetGlyphs.DrawUpdating(graphics, new RectangleF(right - 11 * u, block.Y + 3 * u, 10 * u, 10 * u), u, _theme.SecondaryText);
            _hotSpots.Add((new RectangleF(left, block.Y, right - left, UsageGeometry.HeaderHeight * u), "head:" + view.Id, view.Tip));

            var columns = ScaledColumns(left, (right - left) / u, u);
            if (view.HasData)
            {
                foreach (var row in view.Rows)
                {
                    DrawRow(graphics, u, columns, rowTop, row, text, name);
                    rowTop += UsageGeometry.RowHeight * u;
                }
            }
            else
            {
                graphics.DrawString("N/A", name, primary, new RectangleF(left, rowTop, right - left, UsageGeometry.RowHeight * u), centred);
                rowTop += UsageGeometry.RowHeight * u;
            }

            // Footer: where the numbers come from and when (or what is wrong), in readable secondary text.
            var line = view.Source.Length > 0 ? $"{view.Source}  \u00B7  {view.Note}" : view.Note;
            var room = right - left;
            var shown = UsageViewBuilder.FitNote(view with { Note = line, ShortNote = view.Source.Length > 0 ? $"{view.Source}  \u00B7  {view.Short}" : view.Short }, room, s => graphics.MeasureString(s, footer, int.MaxValue, near).Width);
            using var footerBrush = new SolidBrush(view.IsProblem ? _theme.Warning : _theme.SecondaryText);
            graphics.DrawString(shown, footer, footerBrush, new RectangleF(left, rowTop, room, UsageGeometry.FooterHeight * u), near);
        }

        graphics.Restore(state);
        DrawScrollbar(graphics, u, area);
    }
    private void DrawScrollbar(Graphics graphics, float u, RectangleF area)
    {
        var max = UsageGeometry.MaxScroll(_views, _maxArea);
        if (max <= 0) return;
        var content = UsageGeometry.ExpandedContentHeight(_views);
        var track = new RectangleF(area.Right - 4 * u, area.Y + 2 * u, 3 * u, area.Height - 4 * u);
        var thumbHeight = Math.Max(14 * u, track.Height * _maxArea / content);
        var thumbTop = track.Y + (track.Height - thumbHeight) * _scroll / max;
        PillBar.Draw(graphics, track, 0, _theme.Track, _theme.Track);
        using var thumb = PillBar.Pill(new RectangleF(track.X, thumbTop, track.Width, thumbHeight));
        using var brush = new SolidBrush(_theme.Scroll);
        graphics.FillPath(brush, thumb);
    }

    /// <summary>The provider logo, or a neutral letter tile when there is no icon (the name stays in the header and the tooltip).</summary>
    private void DrawLogo(Graphics graphics, UsageProviderView view, RectangleF box, float u)
    {
        var icon = _icons.Get(view.Id, _theme.LogoBackground, (int)Math.Round(box.Width));
        if (icon is not null)
        {
            graphics.DrawImageUnscaled(icon, (int)Math.Round(box.X), (int)Math.Round(box.Y));
            return;
        }

        using var tile = PillBar.Rounded(box, 5 * u);
        using var fill = new SolidBrush(_theme.TileFill);
        graphics.FillPath(fill, tile);
        using var font = new Font("Segoe UI Semibold", 11 * u, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(_theme.TileText);
        using var centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(view.Name.Length > 0 ? char.ToUpperInvariant(view.Name[0]).ToString() : "?", font, brush, box, centre);
    }

    private Color LevelColor(UsageLevel level) => level switch
    {
        UsageLevel.Critical => _theme.Critical,
        UsageLevel.Warning => _theme.Warning,
        _ => _theme.Quota,
    };

    private void DisposeUsage()
    {
        _tip.Dispose();
        _icons.Dispose();
    }
}
