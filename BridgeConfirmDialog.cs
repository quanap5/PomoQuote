using PomoQuote.AiUsage.Claude;

namespace PomoQuote;

/// <summary>
/// Shows the exact change the Claude status line bridge would make and asks for an explicit yes. Nothing is written
/// before this dialog is confirmed, and the default button is "Cancel". The text box is read-only: there is nothing to type.
/// </summary>
internal sealed class BridgeConfirmDialog : Form
{
    public BridgeConfirmDialog(string settingsPath, BridgePreview preview)
    {
        Text = "Set up the Claude status line bridge";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var explanation = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Margin = new Padding(0, 0, 0, 8),
            Text = preview.State == StatusLineState.Custom
                ? "PomoQuote will change ONE line in your Claude settings file: the command of your status line. Your own command is kept inside the new one and runs exactly as before, with the same input, and its output and exit code are passed through. PomoQuote additionally saves the numbers it needs (percentages and reset times only)."
                : "You have no status line yet. PomoQuote will add one to your Claude settings file. It shows a short usage line and saves the numbers it needs (percentages and reset times only).",
        };

        var detail = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Width = 560,
            Height = 170,
            Text =
                $"File:    {settingsPath}\r\n\r\n" +
                $"Now:     statusLine.command = {(preview.CurrentCommand is null ? "(none)" : preview.CurrentCommand)}\r\n\r\n" +
                $"After:   statusLine.command = {preview.NewCommand}\r\n\r\n" +
                $"Backup:  a copy of the whole file is saved first in {preview.BackupDirectory}\r\n" +
                "Undo:    Settings > AI Usage > Remove bridge puts the original back.",
        };

        var set = new Button { Text = "Set up bridge", DialogResult = DialogResult.OK, AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, Margin = new Padding(0) };
        AcceptButton = cancel; // Enter does NOT confirm: only a click on "Set up bridge" does
        CancelButton = cancel;
        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Right, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.Add(set);
        buttons.Controls.Add(cancel);

        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(explanation);
        layout.Controls.Add(detail);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        Shown += (_, _) => cancel.Focus();
    }
}
