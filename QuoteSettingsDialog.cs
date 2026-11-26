using PomoQuote.Quotes;

namespace PomoQuote;

/// <summary>
/// Modal dialog for the sentences: which categories, translation on/off and how often the sentence changes.
/// Enter = OK, Esc = Cancel. OK stays disabled while no category is ticked.
/// </summary>
internal sealed class QuoteSettingsDialog : Form
{
    private static readonly (QuoteInterval Value, string Caption)[] IntervalChoices =
    [
        (QuoteInterval.PerPhase, "Each Pomodoro phase"),
        (QuoteInterval.Minutes5, "Every 5 minutes (and each phase)"),
        (QuoteInterval.Minutes10, "Every 10 minutes (and each phase)"),
        (QuoteInterval.Minutes30, "Every 30 minutes (and each phase)"),
        (QuoteInterval.Minutes60, "Every 60 minutes (and each phase)"),
        (QuoteInterval.Off, "Off (only at start and on request)"),
    ];

    /// <summary>The categories offered, in the order of the dialog.</summary>
    internal static readonly (QuoteCategory Value, string Caption)[] CategoryChoices =
    [
        (QuoteCategory.Motivation, "Motivation"),
        (QuoteCategory.Idiom, "Idioms (and the classic sayings)"),
        (QuoteCategory.Proverb, "Proverbs"),
        (QuoteCategory.Quote, "Quotes"),
        (QuoteCategory.Maxim, "Life maxims"),
        (QuoteCategory.Expression, "Common expressions"),
        (QuoteCategory.PhrasalVerb, "Phrasal verbs"),
        (QuoteCategory.Conversation, "Conversation"),
    ];

    private readonly CheckBox _all;
    private readonly CheckBox[] _categories;
    private readonly CheckBox _translation;
    private readonly ComboBox _interval;
    private readonly Button _ok;
    private bool _updating;

    public QuoteSettingsDialog(QuoteSettings current)
    {
        Text = "Settings - Quotes";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        _all = new CheckBox { Text = "All categories", AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
        _categories = CategoryChoices.Select(choice => Category(choice.Caption, current.Categories, choice.Value)).ToArray();
        _translation = new CheckBox { Text = "Show the Vietnamese translation", Checked = current.ShowTranslation, AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        _interval = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250, Margin = new Padding(0, 4, 0, 12) };
        foreach (var (_, caption) in IntervalChoices) _interval.Items.Add(caption);
        _interval.SelectedIndex = Math.Max(0, Array.FindIndex(IntervalChoices, choice => choice.Value == current.Interval));

        _ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, Margin = new Padding(0) };
        AcceptButton = _ok;
        CancelButton = cancel;
        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
        buttons.Controls.Add(_ok);
        buttons.Controls.Add(cancel);

        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(new Label { Text = "Sentences from:", AutoSize = true });
        foreach (var box in _categories) layout.Controls.Add(box);
        layout.Controls.Add(_all);
        layout.Controls.Add(_translation);
        layout.Controls.Add(new Label { Text = "Change the sentence:", AutoSize = true, Margin = new Padding(0, 12, 0, 0) });
        layout.Controls.Add(_interval);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        _all.CheckedChanged += (_, _) =>
        {
            if (_updating) return;
            _updating = true;
            foreach (var box in _categories) box.Checked = _all.Checked;
            _updating = false;
            RefreshState();
        };
        foreach (var box in _categories)
        {
            box.CheckedChanged += (_, _) =>
            {
                if (!_updating) RefreshState();
            };
        }

        RefreshState();
    }

    /// <summary>The settings as entered. The sentence last shown is carried over from <paramref name="current"/>.</summary>
    public QuoteSettings ValueFrom(QuoteSettings current) => current with
    {
        Categories = Selected,
        ShowTranslation = _translation.Checked,
        Interval = IntervalChoices[Math.Max(0, _interval.SelectedIndex)].Value,
    };

    private QuoteCategory Selected =>
        CategoryChoices.Select((choice, i) => _categories[i].Checked ? choice.Value : 0).Aggregate(QuoteCategory.None, (all, one) => all | one);

    private CheckBox Category(string caption, QuoteCategory current, QuoteCategory flag) =>
        new() { Text = caption, Checked = (current & flag) != 0, AutoSize = true, Margin = new Padding(0, 4, 0, 0) };

    private void RefreshState()
    {
        _updating = true;
        _all.Checked = Selected == QuoteCategory.All;
        _updating = false;
        _ok.Enabled = Selected != QuoteCategory.None;
    }
}
