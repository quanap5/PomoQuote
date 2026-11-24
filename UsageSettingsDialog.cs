using PomoQuote.AiUsage;

namespace PomoQuote;

/// <summary>
/// Settings > AI Usage: which providers to show, what the CLI on this PC reports (found, version, logged in; read
/// only), a test of the connection, the order, display mode, refresh interval and the warning levels. There is no
/// place to type a password, key or token: the official CLI owns the login, and the dialog says how to log in there.
/// </summary>
internal sealed class UsageSettingsDialog : Form
{
    private static readonly int[] IntervalChoices = [1, 2, 3, 5, 10, 15];

    private readonly CodexDetector _detector;
    private readonly Func<string, CancellationToken, Task<UsageStatus?>> _test;
    private readonly IReadOnlyList<(string Id, string Name)> _providers;
    private readonly CancellationTokenSource _closing = new();
    private readonly CheckBox _codex;
    private readonly Label _codexStatus;
    private readonly Label _testResult;
    private readonly Button _testButton;
    private readonly ListBox _order;
    private readonly ComboBox _mode;
    private readonly ComboBox _interval;
    private readonly CheckBox _alerts;
    private readonly NumericUpDown _warn;
    private readonly NumericUpDown _critical;
    private readonly Button _ok;

    public UsageSettingsDialog(
        UsageSettings current, IReadOnlyList<(string Id, string Name)> providers, CodexDetector detector,
        Func<string, CancellationToken, Task<UsageStatus?>> test)
    {
        _providers = providers;
        _detector = detector;
        _test = test;
        Text = "Settings - AI Usage";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var intro = new Label
        {
            Text = "Shows how much of your Codex quota is used, read from the official Codex CLI on this PC. PomoQuote never asks for a password, key or token and never stores one: log in with the CLI itself.",
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Margin = new Padding(0, 0, 0, 10),
        };

        _codex = new CheckBox { Text = "Show Codex usage on the widget", AutoSize = true, Checked = current.IsEnabled(CodexUsageProvider.ProviderId) };
        _codexStatus = new Label { Text = "Checking for Codex...", AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(20, 2, 0, 2) };
        _testButton = new Button { Text = "Test connection", AutoSize = true, Margin = new Padding(20, 2, 8, 2) };
        _testResult = new Label { Text = "", AutoSize = true, MaximumSize = new Size(320, 0), Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 2) };
        _testButton.Click += async (_, _) => await RunTestAsync();
        var testRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
        testRow.Controls.Add(_testButton);
        testRow.Controls.Add(_testResult);

        _order = new ListBox { Height = 40, Width = 200, IntegralHeight = false };
        foreach (var id in current.Order.Where(id => providers.Any(p => p.Id == id)).Concat(providers.Select(p => p.Id)).Distinct())
        {
            _order.Items.Add(providers.First(p => p.Id == id).Name);
        }

        var up = new Button { Text = "Up", AutoSize = true };
        var down = new Button { Text = "Down", AutoSize = true };
        up.Click += (_, _) => MoveSelected(-1);
        down.Click += (_, _) => MoveSelected(1);
        var orderRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0) };
        orderRow.Controls.Add(_order);
        orderRow.Controls.Add(up);
        orderRow.Controls.Add(down);
        if (_order.Items.Count > 0) _order.SelectedIndex = 0;

        _mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        _mode.Items.AddRange(["Bars: percent, bar and reset time per window", "Percent only: one short line per provider"]);
        _mode.SelectedIndex = current.DisplayMode == UsageDisplayMode.Percent ? 1 : 0;

        _interval = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        foreach (var minutes in IntervalChoices) _interval.Items.Add(minutes == 1 ? "every minute" : $"every {minutes} minutes");
        var index = Array.IndexOf(IntervalChoices, current.IntervalMinutes);
        _interval.SelectedIndex = index >= 0 ? index : Array.IndexOf(IntervalChoices, UsageSettings.DefaultIntervalMinutes);

        _alerts = new CheckBox { Text = "Show a warning when a window passes", AutoSize = true, Checked = current.AlertsEnabled };
        var levels = current.Thresholds.OrderBy(t => t).ToList();
        _warn = Level(levels[0]);
        _critical = Level(levels.Count > 1 ? levels[^1] : Math.Min(100, levels[0] + 15));
        var levelRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(20, 0, 0, 0) };
        levelRow.Controls.Add(new Label { Text = "Warning at", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        levelRow.Controls.Add(_warn);
        levelRow.Controls.Add(new Label { Text = "% and again at", AutoSize = true, Margin = new Padding(4, 6, 4, 0) });
        levelRow.Controls.Add(_critical);
        levelRow.Controls.Add(new Label { Text = "% used", AutoSize = true, Margin = new Padding(4, 6, 0, 0) });
        _alerts.CheckedChanged += (_, _) => RefreshState();
        _warn.ValueChanged += (_, _) => RefreshState();
        _critical.ValueChanged += (_, _) => RefreshState();

        var note = new Label { Text = "Windows only for now: WSL is not supported yet.", AutoSize = true, Margin = new Padding(0, 10, 0, 10), ForeColor = SystemColors.GrayText };

        _ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, Margin = new Padding(0) };
        AcceptButton = _ok;
        CancelButton = cancel;
        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
        buttons.Controls.Add(_ok);
        buttons.Controls.Add(cancel);

        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(intro);
        layout.Controls.Add(_codex);
        layout.Controls.Add(_codexStatus);
        layout.Controls.Add(testRow);
        layout.Controls.Add(new Label { Text = "Display order:", AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
        layout.Controls.Add(orderRow);
        layout.Controls.Add(new Label { Text = "Display mode:", AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
        layout.Controls.Add(_mode);
        layout.Controls.Add(new Label { Text = "Read the quota:", AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
        layout.Controls.Add(_interval);
        layout.Controls.Add(_alerts);
        layout.Controls.Add(levelRow);
        layout.Controls.Add(note);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        Shown += async (_, _) => await DetectAsync();
        FormClosing += (_, _) => _closing.Cancel();
        RefreshState();
    }

    /// <summary>The settings as entered, on top of the ones the dialog started from.</summary>
    public UsageSettings ValueFrom(UsageSettings current)
    {
        var order = _order.Items.Cast<string>().Select(name => _providers.First(p => p.Name == name).Id).ToList();
        var thresholds = _warn.Value == _critical.Value ? [(int)_warn.Value] : new List<int> { (int)_warn.Value, (int)_critical.Value };
        return current.WithEnabled(CodexUsageProvider.ProviderId, _codex.Checked) with
        {
            Order = order,
            DisplayMode = _mode.SelectedIndex == 1 ? UsageDisplayMode.Percent : UsageDisplayMode.Bars,
            IntervalMinutes = IntervalChoices[Math.Max(0, _interval.SelectedIndex)],
            AlertsEnabled = _alerts.Checked,
            Thresholds = thresholds.OrderBy(t => t).ToList(),
        };
    }

    private static NumericUpDown Level(int value) => new() { Minimum = 1, Maximum = 100, Value = Math.Clamp(value, 1, 100), Width = 56 };

    private void MoveSelected(int delta)
    {
        var i = _order.SelectedIndex;
        var j = i + delta;
        if (i < 0 || j < 0 || j >= _order.Items.Count) return;
        (_order.Items[i], _order.Items[j]) = (_order.Items[j], _order.Items[i]);
        _order.SelectedIndex = j;
    }

    private void RefreshState()
    {
        _warn.Enabled = _critical.Enabled = _alerts.Checked;
        // Two different levels are expected; equal ones are saved as one.
        _ok.Enabled = true;
    }

    private async Task DetectAsync()
    {
        try
        {
            var info = await _detector.DetectAsync(_closing.Token);
            if (IsDisposed) return;
            _codexStatus.Text = info.Installed && info.Version.Length > 0 ? $"{info.Detail} (version {info.Version})" : info.Detail;
            _testButton.Enabled = info.Installed;
        }
        catch (OperationCanceledException)
        {
            // The dialog was closed.
        }
    }

    private async Task RunTestAsync()
    {
        _testButton.Enabled = false;
        _testResult.Text = "Reading...";
        try
        {
            var status = await _test(CodexUsageProvider.ProviderId, _closing.Token);
            if (IsDisposed) return;
            var latest = status?.Latest;
            _testResult.Text = latest switch
            {
                null => "No answer.",
                { State: UsageState.Ok } ok => "OK: " + string.Join(", ", ok.Windows.Select(w => $"{UsageViewBuilder.Label(w)} {Math.Round(w.PercentUsed):0}%")) + (ok.Source.PlanTier.Length > 0 ? $" ({ok.Source.PlanTier})" : ""),
                _ => latest.ErrorText.Length > 0 ? latest.ErrorText : latest.State.ToString(),
            };
        }
        catch (OperationCanceledException)
        {
            // The dialog was closed.
        }
        finally
        {
            if (!IsDisposed) _testButton.Enabled = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _closing.Dispose();
        base.Dispose(disposing);
    }
}
