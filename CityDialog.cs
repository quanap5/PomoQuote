namespace PomoQuote;

/// <summary>Small modal dialog asking for a city name. Enter = OK, Esc = Cancel.</summary>
internal sealed class CityDialog : Form
{
    private const int MaxCityLength = 100;

    private readonly TextBox _cityBox;
    private readonly Button _okButton;

    public CityDialog(string currentCity)
    {
        Text = "Settings - City";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        _cityBox = new TextBox { Text = currentCity, MaxLength = MaxCityLength, Width = 260, Margin = new Padding(0, 0, 0, 12) };
        _cityBox.TextChanged += (_, _) => _okButton!.Enabled = Value.Length > 0;
        _cityBox.SelectAll();

        _okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, Margin = new Padding(0) };
        AcceptButton = _okButton;
        CancelButton = cancelButton;
        _okButton.Enabled = Value.Length > 0;

        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
        buttons.Controls.Add(_okButton);
        buttons.Controls.Add(cancelButton);

        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(new Label { Text = "City (for the weather):", AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
        layout.Controls.Add(_cityBox);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
    }

    /// <summary>The trimmed text in the box.</summary>
    public string Value => _cityBox.Text.Trim();
}
