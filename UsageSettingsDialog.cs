using PomoQuote.AiUsage;
using PomoQuote.AiUsage.Claude;

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
    private readonly AgyDetector _agyDetector;
    private readonly CheckBox _agy;
    private readonly Label _agyStatus;
    private readonly Label _agyResult;
    private readonly Button _agyTest;
    private readonly GrokDetector _grokDetector;
    private readonly CheckBox _grok;
    private readonly Label _grokStatus;
    private readonly Label _grokResult;
    private readonly Button _grokTest;
    private readonly Func<string, CancellationToken, Task<UsageStatus?>> _test;
    private readonly IReadOnlyList<(string Id, string Name)> _providers;
    private readonly CancellationTokenSource _closing = new();
    private readonly ProviderIcons _icons = new();
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
    private readonly ClaudeActions _claude;
    private readonly string _claudeSettingsPath;
    private readonly Func<Form, BridgePreview, bool> _confirmBridge;
    private readonly CheckBox _claudeEnabled;
    private readonly Label _claudeStatus;
    private readonly Label _claudeResult;
    private readonly Button _setup;
    private readonly Button _remove;
    private readonly Button _upgrade;
    private readonly Button _checkBridge;
    private readonly Button _checkProject;
    private readonly Button _refreshClaude;

    public UsageSettingsDialog(
        UsageSettings current, IReadOnlyList<(string Id, string Name)> providers, CodexDetector detector,
        Func<string, CancellationToken, Task<UsageStatus?>> test, ClaudeActions claude, string claudeSettingsPath, Func<Form, BridgePreview, bool> confirmBridge, AgyDetector? antigravityDetector = null, GrokDetector? grokDetector = null)
    {
        _claude = claude;
        _claudeSettingsPath = claudeSettingsPath;
        _confirmBridge = confirmBridge;
        _providers = providers;
        _detector = detector;
        _agyDetector = antigravityDetector ?? new AgyDetector();
        _grokDetector = grokDetector ?? new GrokDetector();
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
            Text = "Shows how much of your Codex, Claude Code, Antigravity and Grok quota is used, read from the official CLIs on this PC. PomoQuote never asks for a password, key or token and never stores one: log in with the CLI itself.",
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

        // ---------- Claude Code ----------
        _claudeEnabled = new CheckBox { Text = "Show Claude Code usage on the widget", AutoSize = true, Checked = current.IsEnabled(ClaudeSnapshotProvider.ProviderId), Margin = new Padding(0, 12, 0, 0) };
        _claudeStatus = new Label { Text = "Checking your Claude settings...", AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(20, 2, 0, 2) };
        _setup = new Button { Text = "Set up status line bridge...", AutoSize = true, Enabled = false };
        _remove = new Button { Text = "Remove bridge", AutoSize = true, Enabled = false };
        _upgrade = new Button { Text = "Upgrade bridge...", AutoSize = true, Visible = false };
        _checkBridge = new Button { Text = "Check", AutoSize = true };
        _checkProject = new Button { Text = "Check a project folder...", AutoSize = true };
        _refreshClaude = new Button { Text = "Refresh Claude now", AutoSize = true };
        _claudeResult = new Label { Text = "", AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(20, 2, 0, 2) };
        _setup.Click += (_, _) => SetUpBridge();
        _remove.Click += (_, _) => RemoveBridge();
        _upgrade.Click += (_, _) => UpgradeBridge();
        _checkBridge.Click += async (_, _) => await ShowClaudeStateAsync(check: true);
        _checkProject.Click += (_, _) => CheckProject();
        _refreshClaude.Click += async (_, _) => await RefreshClaudeAsync();
        var claudeButtons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, MaximumSize = new Size(460, 0), Margin = new Padding(20, 2, 0, 2) };
        claudeButtons.Controls.AddRange([_setup, _upgrade, _remove, _checkBridge, _checkProject, _refreshClaude]);

        // ---------- Antigravity ----------
        _agy = new CheckBox { Text = "Show Antigravity usage on the widget", AutoSize = true, Checked = current.IsEnabled(AntigravityUsageProvider.ProviderId), Margin = new Padding(0, 12, 0, 0) };
        _agyStatus = new Label { Text = "Checking for the Antigravity CLI (agy)...", AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(20, 2, 0, 2) };
        _agyTest = new Button { Text = "Test connection", AutoSize = true, Margin = new Padding(20, 2, 8, 2) };
        _agyResult = new Label { Text = "", AutoSize = true, MaximumSize = new Size(320, 0), Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 2) };
        _agyTest.Click += async (_, _) => await RunAgyTestAsync();
        var agyRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
        agyRow.Controls.Add(_agyTest);
        agyRow.Controls.Add(_agyResult);
        var agyNote = new Label { Text = "Antigravity is read every 5 minutes with agy's own usage command (it starts agy for a few seconds and uses no model).", AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(20, 2, 0, 2) };
        // ---------- Grok ----------
        _grok = new CheckBox { Text = "Show Grok usage on the widget", AutoSize = true, Checked = current.IsEnabled(GrokUsageProvider.ProviderId), Margin = new Padding(0, 12, 0, 0) };
        _grokStatus = new Label { Text = "Checking for the Grok CLI (grok)...", AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(20, 2, 0, 2) };
        _grokTest = new Button { Text = "Test connection", AutoSize = true, Margin = new Padding(20, 2, 8, 2) };
        _grokResult = new Label { Text = "", AutoSize = true, MaximumSize = new Size(320, 0), Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 0, 2) };
        _grokTest.Click += async (_, _) => await RunGrokTestAsync();
        var grokRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
        grokRow.Controls.Add(_grokTest);
        grokRow.Controls.Add(_grokResult);
        var grokNote = new Label { Text = "Grok is read every 5 minutes through the Grok CLI's own agent (it starts grok for a few seconds, sends no prompt and spends no credits).", AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(20, 2, 0, 2) };
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

        _mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
        _mode.Items.AddRange(["Compact: one provider at a time (scroll over its logo to switch)", "Expanded: every provider as a block"]);
        _mode.SelectedIndex = current.Expanded ? 1 : 0;

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
        layout.Controls.Add(ProviderRow(CodexUsageProvider.ProviderId, _codex));
        layout.Controls.Add(_codexStatus);
        layout.Controls.Add(testRow);
        layout.Controls.Add(ProviderRow(ClaudeSnapshotProvider.ProviderId, _claudeEnabled));
        layout.Controls.Add(_claudeStatus);
        layout.Controls.Add(claudeButtons);
        layout.Controls.Add(_claudeResult);
        layout.Controls.Add(ProviderRow(AntigravityUsageProvider.ProviderId, _agy));
        layout.Controls.Add(_agyStatus);
        layout.Controls.Add(agyRow);
        layout.Controls.Add(agyNote);
        layout.Controls.Add(ProviderRow(GrokUsageProvider.ProviderId, _grok));
        layout.Controls.Add(_grokStatus);
        layout.Controls.Add(grokRow);
        layout.Controls.Add(grokNote);
        layout.Controls.Add(new Label { Text = "Display order:", AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
        layout.Controls.Add(orderRow);
        layout.Controls.Add(new Label { Text = "Layout of the usage area (also the arrow button on the widget):", AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
        layout.Controls.Add(_mode);
        layout.Controls.Add(new Label { Text = "Read the quota:", AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
        layout.Controls.Add(_interval);
        layout.Controls.Add(_alerts);
        layout.Controls.Add(levelRow);
        layout.Controls.Add(note);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        Shown += async (_, _) =>
        {
            await DetectAsync();
            await DetectAgyAsync();
            await DetectGrokAsync();
            await ShowClaudeStateAsync(check: false);
        };
        FormClosing += (_, _) => _closing.Cancel();
        RefreshState();
    }

    /// <summary>The settings as entered, on top of the ones the dialog started from.</summary>
    public UsageSettings ValueFrom(UsageSettings current)
    {
        var order = _order.Items.Cast<string>().Select(name => _providers.First(p => p.Name == name).Id).ToList();
        var thresholds = _warn.Value == _critical.Value ? [(int)_warn.Value] : new List<int> { (int)_warn.Value, (int)_critical.Value };
        return current.WithEnabled(CodexUsageProvider.ProviderId, _codex.Checked).WithEnabled(ClaudeSnapshotProvider.ProviderId, _claudeEnabled.Checked).WithEnabled(AntigravityUsageProvider.ProviderId, _agy.Checked).WithEnabled(GrokUsageProvider.ProviderId, _grok.Checked) with
        {
            Order = order,
            Expanded = _mode.SelectedIndex == 1,
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

    private async Task ShowClaudeStateAsync(bool check)
    {
        try
        {
            var status = await Task.Run(() => _claude.Check());
            if (IsDisposed) return;
            _claudeStatus.Text = status.State switch
            {
                StatusLineState.None => "You have no status line. The bridge would add one (a short usage line).",
                StatusLineState.Custom => "You have your own status line. The bridge would wrap it: yours keeps running exactly as before.",
                StatusLineState.Bridged when status.OldStyle => "The status line bridge is set up in the old style, without a fail-safe: if PomoQuote.exe is moved or deleted your status line disappears. Choose Upgrade bridge (your original command is kept).",
                StatusLineState.Bridged => "The status line bridge is set up. Numbers appear after the first reply in a Claude Code session.",
                _ => $"Your Claude settings cannot be used: {status.Reason}.",
            };
            _setup.Enabled = status.State is StatusLineState.None or StatusLineState.Custom;
            _remove.Enabled = status.State == StatusLineState.Bridged;
            _upgrade.Visible = status.State == StatusLineState.Bridged && status.OldStyle;
            if (check)
            {
                _claudeResult.Text = status.Problems.Count == 0 ? "Check: no problem found." : "Check: " + string.Join(" ", status.Problems);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!IsDisposed) _claudeStatus.Text = "Your Claude settings cannot be read right now.";
        }
    }

    private async void SetUpBridge()
    {
        var preview = _claude.Preview();
        if (preview is null)
        {
            _claudeResult.Text = _claude.CannotInstallReason() ?? "Nothing to set up (already set up, or the settings file cannot be used).";
            return;
        }

        // Nothing is written before the user has seen the exact change and clicked "Set up bridge".
        if (!_confirmBridge(this, preview)) return;

        var result = _claude.Install();
        _claudeResult.Text = result switch
        {
            BridgeResult.Done => $"Done. A backup of the file is in {preview.BackupDirectory}.",
            BridgeResult.AlreadyBridged => "Already set up.",
            BridgeResult.ChangedMeanwhile => "The settings file changed while it was being backed up (Claude Code may be using it). Nothing was changed; try again.",
            BridgeResult.WriteFailed => "The settings file could not be written (it may be locked). Nothing was changed; try again.",
            BridgeResult.WrongProgram => _claude.CannotInstallReason() ?? "The bridge program could not be copied to its folder. Your Claude settings were not changed.",
            _ => "The settings file cannot be used. Nothing was changed.",
        };
        await ShowClaudeStateAsync(check: false);
    }

    /// <summary>Rewrites an old-style bridge to the new form: same confirmation dialog, nothing is written before "Upgrade bridge" is clicked.</summary>
    private async void UpgradeBridge()
    {
        var preview = _claude.PreviewUpgrade();
        if (preview is null)
        {
            _claudeResult.Text = _claude.CannotInstallReason() ?? "Nothing to upgrade (the bridge is already the new form, or its saved command is damaged: use Remove bridge).";
            return;
        }

        if (!_confirmBridge(this, preview)) return;

        var result = _claude.Upgrade();
        _claudeResult.Text = result switch
        {
            BridgeResult.Done => $"Upgraded. A backup of the file is in {preview.BackupDirectory}. Your original status line command is unchanged inside it.",
            BridgeResult.AlreadyBridged => "The bridge is already the new form.",
            BridgeResult.ChangedMeanwhile => "The settings file changed while it was being backed up (Claude Code may be using it). Nothing was changed; try again.",
            BridgeResult.WriteFailed => "The settings file could not be written (it may be locked). Nothing was changed; try again.",
            BridgeResult.WrongProgram => _claude.CannotInstallReason() ?? "The bridge program could not be copied to its folder. Your Claude settings were not changed.",
            _ => "The settings file cannot be used. Nothing was changed.",
        };
        await ShowClaudeStateAsync(check: false);
    }

    private async void RemoveBridge()
    {
        var answer = MessageBox.Show(this, "Put your original Claude status line back?", "Remove bridge", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        var result = _claude.Uninstall();
        _claudeResult.Text = result switch
        {
            BridgeResult.Done => "Removed. Your original status line is back.",
            BridgeResult.NotOurs => "The status line is no longer the bridge, so nothing was changed.",
            _ => "The settings file cannot be used right now. Nothing was changed.",
        };
        await ShowClaudeStateAsync(check: false);
    }

    private void CheckProject()
    {
        using var picker = new FolderBrowserDialog { Description = "Choose a project folder where you use Claude Code. A project that has its own status line replaces the bridge there.", UseDescriptionForTitle = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        _claudeResult.Text = ClaudeBridgeInstaller.ProjectOverrides(picker.SelectedPath)
            ? $"\"{Path.GetFileName(picker.SelectedPath.TrimEnd('\\', '/'))}\" has its own status line: it replaces the bridge in that project, so its usage is not read from there."
            : "That project does not override the status line.";
    }

    private async Task RefreshClaudeAsync()
    {
        if (!_claude.RefreshNoteSeen)
        {
            var go = MessageBox.Show(this, ClaudeActions.RefreshNote + "\r\n\r\nRefresh now?", "Refresh Claude now", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2);
            if (go != DialogResult.Yes) return;
            _claude.MarkRefreshNoteSeen();
        }

        _refreshClaude.Enabled = false;
        _claudeResult.Text = "Asking Claude...";
        try
        {
            var result = await _claude.RefreshAsync(_closing.Token);
            if (!IsDisposed) _claudeResult.Text = result.Message;
        }
        catch (OperationCanceledException)
        {
            // The dialog was closed.
        }
        finally
        {
            if (!IsDisposed) _refreshClaude.Enabled = true;
        }
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


    private async Task DetectAgyAsync()
    {
        try
        {
            var info = await _agyDetector.DetectAsync(_closing.Token);
            if (IsDisposed) return;
            _agyStatus.Text = info.Installed && info.Version.Length > 0 ? $"{info.Detail} (version {info.Version})" : info.Detail;
            _agyTest.Enabled = info.Installed;
        }
        catch (OperationCanceledException)
        {
            // The dialog was closed.
        }
    }

    /// <summary>The first read: nothing is run until the user presses this (or turns Antigravity on and presses OK).</summary>
    private async Task RunAgyTestAsync()
    {
        _agyTest.Enabled = false;
        _agyResult.Text = "Reading...";
        try
        {
            var status = await _test(AntigravityUsageProvider.ProviderId, _closing.Token);
            if (IsDisposed) return;
            var latest = status?.Latest;
            _agyResult.Text = latest switch
            {
                null => "No answer.",
                { State: UsageState.Ok } ok => "OK: " + (UsageViewBuilder.GroupDetail(ok) is { Length: > 0 } groups ? groups : string.Join(", ", ok.Windows.Select(w => $"{UsageViewBuilder.Label(w)} {Math.Round(w.PercentUsed):0}%"))) + " used",
                _ => latest.ErrorText.Length > 0 ? latest.ErrorText : latest.State.ToString(),
            };
        }
        catch (OperationCanceledException)
        {
            // The dialog was closed.
        }
        finally
        {
            if (!IsDisposed) _agyTest.Enabled = true;
        }
    }
    private async Task DetectGrokAsync()
    {
        try
        {
            var info = await _grokDetector.DetectAsync(_closing.Token);
            if (IsDisposed) return;
            _grokStatus.Text = info.Installed && info.Version.Length > 0 ? $"{info.Detail} (version {info.Version})" : info.Detail;
            _grokTest.Enabled = info.Installed;
        }
        catch (OperationCanceledException)
        {
            // The dialog was closed.
        }
    }

    /// <summary>The first read: nothing is run until the user presses this (or turns Grok on and presses OK).</summary>
    private async Task RunGrokTestAsync()
    {
        _grokTest.Enabled = false;
        _grokResult.Text = "Reading...";
        try
        {
            var status = await _test(GrokUsageProvider.ProviderId, _closing.Token);
            if (IsDisposed) return;
            var latest = status?.Latest;
            _grokResult.Text = latest switch
            {
                null => "No answer.",
                { State: UsageState.Ok } ok => "OK: " + string.Join(", ", ok.Windows.Select(w => $"{UsageViewBuilder.Label(w)} {Math.Round(w.PercentUsed):0}%")) + " used" + (ok.Source.PlanTier.Length > 0 ? $" ({ok.Source.PlanTier})" : ""),
                _ => latest.ErrorText.Length > 0 ? latest.ErrorText : latest.State.ToString(),
            };
        }
        catch (OperationCanceledException)
        {
            // The dialog was closed.
        }
        finally
        {
            if (!IsDisposed) _grokTest.Enabled = true;
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

    /// <summary>A provider's checkbox with its logo in front (the logo is decoration: the checkbox text names the provider).</summary>
    private FlowLayoutPanel ProviderRow(string providerId, CheckBox box)
    {
        var margin = box.Margin;
        box.Margin = new Padding(0, 0, 0, 0);
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, margin.Top, 0, 0) };
        row.Controls.Add(new ProviderIconBox(_icons, providerId));
        row.Controls.Add(box);
        return row;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closing.Dispose();
            _icons.Dispose();
        }

        base.Dispose(disposing);
    }
}
