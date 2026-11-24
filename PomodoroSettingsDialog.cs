using PomoQuote.Pomodoro;

namespace PomoQuote;

/// <summary>Modal dialog for the Pomodoro options. Enter = OK, Esc = Cancel; numbers are limited to their valid ranges.</summary>
internal sealed class PomodoroSettingsDialog : Form
{
    private readonly NumericUpDown _work;
    private readonly NumericUpDown _shortBreak;
    private readonly NumericUpDown _longBreak;
    private readonly NumericUpDown _sessions;
    private readonly CheckBox _sound;
    private readonly CheckBox _autoStart;

    public PomodoroSettingsDialog(PomodoroSettings current)
    {
        Text = "Settings - Pomodoro";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        _work = Number(current.WorkMinutes, PomodoroSettings.MinMinutes, PomodoroSettings.MaxMinutes);
        _shortBreak = Number(current.ShortBreakMinutes, PomodoroSettings.MinMinutes, PomodoroSettings.MaxMinutes);
        _longBreak = Number(current.LongBreakMinutes, PomodoroSettings.MinMinutes, PomodoroSettings.MaxMinutes);
        _sessions = Number(current.SessionsBeforeLongBreak, PomodoroSettings.MinSessions, PomodoroSettings.MaxSessions);
        _sound = new CheckBox { Text = "Play a sound at the end of each phase", Checked = current.SoundEnabled, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        _autoStart = new CheckBox { Text = "Start the next phase automatically", Checked = current.AutoStartNext, AutoSize = true, Margin = new Padding(0, 4, 0, 12) };

        var grid = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Dock = DockStyle.Fill };
        AddRow(grid, $"Work (minutes, {PomodoroSettings.MinMinutes}-{PomodoroSettings.MaxMinutes}):", _work);
        AddRow(grid, $"Short break (minutes):", _shortBreak);
        AddRow(grid, $"Long break (minutes):", _longBreak);
        AddRow(grid, $"Work sessions before a long break ({PomodoroSettings.MinSessions}-{PomodoroSettings.MaxSessions}):", _sessions);

        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, Margin = new Padding(0) };
        AcceptButton = okButton;
        CancelButton = cancelButton;
        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);

        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(grid);
        layout.Controls.Add(_sound);
        layout.Controls.Add(_autoStart);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
    }

    /// <summary>The options as entered; every number is inside its valid range by construction.</summary>
    public PomodoroSettings Value => new(
        (int)_work.Value,
        (int)_shortBreak.Value,
        (int)_longBreak.Value,
        (int)_sessions.Value,
        _sound.Checked,
        _autoStart.Checked);

    private static NumericUpDown Number(int value, int min, int max) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Width = 70,
        Margin = new Padding(12, 3, 0, 3),
    };

    private static void AddRow(TableLayoutPanel grid, string label, NumericUpDown box)
    {
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 6) });
        grid.Controls.Add(box);
    }
}
