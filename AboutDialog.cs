using System.Reflection;

namespace PomoQuote;

/// <summary>Modal dialog showing the app name and its version. Enter/OK and Esc both close it.</summary>
internal sealed class AboutDialog : Form
{
    public const string AppName = "PomoQuote";

    /// <summary>The notice about the marks shown in the widget (the details and sources are in THIRD_PARTY_ASSETS.md).</summary>
    public const string MarksNotice =
        "Third-party marks: OpenAI and the OpenAI Blossom logo belong to OpenAI; Claude and Anthropic belong to Anthropic; " +
        "Antigravity and Google belong to Google. PomoQuote is not affiliated with or endorsed by any of them. " +
        "Sources and terms: THIRD_PARTY_ASSETS.md in the PomoQuote repository."; 

    private readonly Bitmap _logo;

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
        Icon = AppIcon.Load();

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
        // The logo, drawn from the icon image closest to 64 px (scaled with the display).
        _logo = AppIcon.LoadBitmap(LogicalToDeviceUnits(64));
        layout.Controls.Add(new PictureBox { Image = _logo, SizeMode = PictureBoxSizeMode.CenterImage, Size = _logo.Size, Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 0, 8) });
        layout.Controls.Add(new Label { Text = AppName, Anchor = AnchorStyles.None, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) });
        layout.Controls.Add(new Label { Text = $"Version {GetAppVersion()}", Anchor = AnchorStyles.None, AutoSize = true, Margin = new Padding(0, 0, 0, 12) });
        layout.Controls.Add(new Label { Text = MarksNotice, AutoSize = true, MaximumSize = new Size(300, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 14), AccessibleName = "Third-party marks" });
        layout.Controls.Add(okButton);
        Controls.Add(layout);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _logo.Dispose();
        base.Dispose(disposing);
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
