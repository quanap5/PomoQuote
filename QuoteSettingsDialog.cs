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

    private readonly CheckBox _all;
    private readonly CheckBox _motivation;
    private readonly CheckBox _idiom;
    private readonly CheckBox _conversation;
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
        _motivation = Category("Motivation", current.Categories, QuoteCategory.Motivation);
        _idiom = Category("Idioms and proverbs", current.Categories, QuoteCategory.Idiom);
        _conversation = Category("Conversation", current.Categories, QuoteCategory.Conversation);
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
        layout.Controls.Add(_motivation);
        layout.Controls.Add(_idiom);
        layout.Controls.Add(_conversation);
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
            _motivation.Checked = _idiom.Checked = _conversation.Checked = _all.Checked;
            _updating = false;
            RefreshState();
        };
        foreach (var box in new[] { _motivation, _idiom, _conversation })
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
        (_motivation.Checked ? QuoteCategory.Motivation : 0) |
        (_idiom.Checked ? QuoteCategory.Idiom : 0) |
        (_conversation.Checked ? QuoteCategory.Conversation : 0);

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
