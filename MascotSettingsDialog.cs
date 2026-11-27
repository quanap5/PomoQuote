using PomoQuote.Mascot;

namespace PomoQuote;

/// <summary>Modal dialog for the mascot options. Enter = OK, Esc = Cancel; at least one monitor must stay ticked.</summary>
internal sealed class MascotSettingsDialog : Form
{
    private readonly string _character;
    private readonly CheckBox _enabled;
    private readonly CheckBox _sound;
    private readonly TrackBar _volume;
    private readonly TrackBar _speed;
    private readonly ComboBox _size;
    private readonly ComboBox _randomActions;
    private readonly CheckedListBox _monitors;
    private readonly IReadOnlyList<MonitorInfo> _monitorInfos;
    private readonly Button _okButton;
    private readonly Label _volumeLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label _speedLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left };

    /// <param name="reloadConfig">Reads the animation config files again and returns the warnings; null hides the button.</param>
    public MascotSettingsDialog(MascotSettings current, IReadOnlyList<MonitorInfo> monitors, Func<IReadOnlyList<string>>? reloadConfig = null)
    {
        Text = "Settings - Mascot";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        _monitorInfos = monitors;
        _character = current.Character; // chosen in the Mascot menu; the dialog keeps it

        _enabled = new CheckBox { Text = "Show the mascot on the taskbar (in widget mode and when the window is minimized)", Checked = current.Enabled, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        _sound = new CheckBox { Text = "Play the mascot's shout sounds", Checked = current.SoundEnabled, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        _volume = new TrackBar { Minimum = MascotSettings.MinVolume, Maximum = MascotSettings.MaxVolume, TickFrequency = 25, Value = current.VolumePercent, Width = 200, AutoSize = false, Height = 30 };
        _speed = new TrackBar { Minimum = MascotSettings.MinSpeed, Maximum = MascotSettings.MaxSpeed, TickFrequency = 25, Value = current.SpeedPercent, Width = 200, AutoSize = false, Height = 30 };
        _size = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        foreach (var scale in MascotSettings.SizeChoices)
        {
            _size.Items.Add($"{(scale == 2 ? "Small" : scale == 3 ? "Medium" : "Large")} ({MascotConfig.Default.FrameSize * scale} px)");
        }

        _size.SelectedIndex = Math.Max(0, MascotSettings.SizeChoices.ToList().IndexOf(current.SizeScale));

        _randomActions = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        _randomActions.Items.AddRange(["Off (just walks)", "Few", "Normal"]);
        _randomActions.SelectedIndex = Math.Clamp(current.RandomActions, 0, MascotSettings.MaxRandomActions);

        _monitors = new CheckedListBox { CheckOnClick = true, Width = 320, Height = 80, IntegralHeight = false };
        for (var i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            var ticked = current.EnabledMonitors is null || current.EnabledMonitors.Contains(monitor.Id);
            _monitors.Items.Add($"{i + 1}: {monitor.Bounds.Width}x{monitor.Bounds.Height}{(monitor.Primary ? " (primary)" : "")}", ticked);
        }

        _monitors.ItemCheck += (_, e) => BeginInvoke(UpdateOkEnabled);

        _volume.ValueChanged += (_, _) => _volumeLabel.Text = $"{_volume.Value}%";
        _speed.ValueChanged += (_, _) => _speedLabel.Text = $"{_speed.Value}%";
        _volumeLabel.Text = $"{_volume.Value}%";
        _speedLabel.Text = $"{_speed.Value}%";

        var grid = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
        AddRow(grid, "Volume:", _volume, _volumeLabel);
        AddRow(grid, "Walking speed:", _speed, _speedLabel);
        AddRow(grid, "Robot size:", _size, null);
        AddRow(grid, "Random actions:", _randomActions, null);

        _okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, Margin = new Padding(0) };
        AcceptButton = _okButton;
        CancelButton = cancelButton;
        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Right, Margin = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(_okButton);
        buttons.Controls.Add(cancelButton);

        var reloadButton = new Button { Text = "Reload animations", AutoSize = true, Margin = new Padding(0, 8, 0, 0), Visible = reloadConfig is not null };
        reloadButton.Click += (_, _) =>
        {
            var warnings = reloadConfig!();
            DialogHost.Message(this, warnings.Count == 0
                ? "The animation settings were reloaded."
                : "The animation settings were reloaded, but some values were ignored:\n\n" + string.Join("\n", warnings.Take(8)),
                Text, MessageBoxButtons.OK, warnings.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        };

        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(_enabled);
        layout.Controls.Add(_sound);
        layout.Controls.Add(grid);
        layout.Controls.Add(new Label { Text = "Walk on these monitors:", AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
        layout.Controls.Add(_monitors);
        layout.Controls.Add(reloadButton);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        UpdateOkEnabled();
    }

    /// <summary>The options as entered. If every monitor is ticked the list is null (meaning "all", including future monitors).</summary>
    public MascotSettings Value
    {
        get
        {
            var ticked = _monitorInfos.Where((_, i) => _monitors.GetItemChecked(i)).Select(m => m.Id).ToArray();
            return new MascotSettings(
                _enabled.Checked,
                _sound.Checked,
                _volume.Value,
                _speed.Value,
                MascotSettings.SizeChoices[Math.Max(0, _size.SelectedIndex)],
                ticked.Length == _monitorInfos.Count ? null : ticked,
                _randomActions.SelectedIndex,
                _character).Normalized();
        }
    }

    private void UpdateOkEnabled() => _okButton.Enabled = _monitorInfos.Count == 0 || _monitors.CheckedItems.Count > 0;

    private static void AddRow(TableLayoutPanel grid, string caption, Control control, Control? valueLabel)
    {
        grid.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) });
        grid.Controls.Add(control);
        grid.Controls.Add(valueLabel ?? new Label { AutoSize = true });
    }
}
