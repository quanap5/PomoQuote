using PomoQuote.Weather;

namespace PomoQuote;

/// <summary>Modal dialog for the weather effects: on or off, Low or Normal, and Reduce motion. Enter = OK, Esc = Cancel.</summary>
internal sealed class WeatherEffectsDialog : Form
{
    private readonly CheckBox _enabled;
    private readonly RadioButton _low;
    private readonly RadioButton _normal;
    private readonly CheckBox _reduceMotion;

    public WeatherEffectsDialog(WeatherEffectSettings current)
    {
        Text = "Settings - Weather effects";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        _enabled = new CheckBox { Text = "Show the weather behind the widget (clouds, rain, snow, stars...)", Checked = current.Enabled, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        _low = new RadioButton { Text = "Low: about half the particles (default)", Checked = current.Intensity == WeatherEffectIntensity.Low, AutoSize = true, Margin = new Padding(16, 0, 0, 2) };
        _normal = new RadioButton { Text = "Normal: the full effect", Checked = current.Intensity == WeatherEffectIntensity.Normal, AutoSize = true, Margin = new Padding(16, 0, 0, 8) };
        _reduceMotion = new CheckBox { Text = "Reduce motion: show one still picture instead of an animation", Checked = current.ReduceMotion, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
        var note = new Label
        {
            Text = "The effect is also still whenever Windows animations are turned off, and it stops while the\r\nwidget is minimised, hidden, covered, the session is locked or the computer sleeps.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 12),
        };

        void UpdateEnabledState()
        {
            _low.Enabled = _enabled.Checked;
            _normal.Enabled = _enabled.Checked;
            _reduceMotion.Enabled = _enabled.Checked;
        }

        _enabled.CheckedChanged += (_, _) => UpdateEnabledState();
        UpdateEnabledState();

        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, Margin = new Padding(0) };
        AcceptButton = okButton;
        CancelButton = cancelButton;
        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);

        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(_enabled);
        layout.Controls.Add(_low);
        layout.Controls.Add(_normal);
        layout.Controls.Add(_reduceMotion);
        layout.Controls.Add(note);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
    }

    /// <summary>The settings as chosen.</summary>
    public WeatherEffectSettings Value => new(_enabled.Checked, _normal.Checked ? WeatherEffectIntensity.Normal : WeatherEffectIntensity.Low, _reduceMotion.Checked);
}
