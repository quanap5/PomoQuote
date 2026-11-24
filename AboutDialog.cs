using System.Reflection;

namespace PomoQuote;

/// <summary>Modal dialog showing the app name and its version. Enter/OK and Esc both close it.</summary>
internal sealed class AboutDialog : Form
{
    public const string AppName = "PomoQuote";

    public AboutDialog()
    {
        Text = $"About {AppName}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            Anchor = AnchorStyles.None,
        };
        AcceptButton = okButton;
        CancelButton = okButton;

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            MinimumSize = new Size(260, 0),
        };
        layout.Controls.Add(new Label { Text = AppName, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) });
        layout.Controls.Add(new Label { Text = $"Version {GetAppVersion()}", AutoSize = true, Margin = new Padding(0, 0, 0, 16) });
        layout.Controls.Add(okButton);
        Controls.Add(layout);
    }

    /// <summary>
    /// The version comes from &lt;Version&gt; in PomoQuote.csproj, via the assembly's informational
    /// version. The SDK may append "+&lt;commit&gt;" build metadata, which is dropped for display.
    /// </summary>
    internal static string GetAppVersion()
    {
        var assembly = typeof(AboutDialog).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational ?? assembly.GetName().Version?.ToString() ?? "unknown";
        var metadataStart = version.IndexOf('+');
        return metadataStart >= 0 ? version[..metadataStart] : version;
    }
}
