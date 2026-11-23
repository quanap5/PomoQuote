using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace HelloWorldBot;

/// <summary>Shows "Hello World" centered in the window using a bundled digital font.</summary>
internal sealed class MainForm : Form
{
    private const string DisplayText = "Hello World";

    // DSEG's space glyph is very narrow; its "!" glyph is a blank, full-width digit cell.
    private const string DigitalFontDisplayText = "Hello!World";

    private const float FontSizePoints = 36f;
    private const string FallbackFontFamily = "Consolas";

    // The collection must outlive any Font created from it.
    private readonly PrivateFontCollection _fonts = new();
    private readonly Font _displayFont;

    public MainForm()
    {
        var digitalFont = LoadDigitalFont();
        _displayFont = digitalFont ?? new Font(FallbackFontFamily, FontSizePoints, FontStyle.Regular, GraphicsUnit.Point);

        Text = DisplayText;
        ClientSize = new Size(800, 300);
        StartPosition = FormStartPosition.CenterScreen;

        Controls.Add(new Label
        {
            Text = digitalFont is null ? DisplayText : DigitalFontDisplayText,
            AccessibleName = DisplayText,
            Font = _displayFont,
            ForeColor = Color.Green,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            // GDI text rendering cannot draw fonts from a PrivateFontCollection; GDI+ can.
            UseCompatibleTextRendering = true,
        });
    }

    private Font? LoadDigitalFont()
    {
        try
        {
            _fonts.AddFontFile(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "DSEG14Classic-Regular.ttf"));
            return _fonts.Families.Length > 0
                ? new Font(_fonts.Families[0], FontSizePoints, FontStyle.Regular, GraphicsUnit.Point)
                : null;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException)
        {
            return null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _displayFont.Dispose();
            _fonts.Dispose();
        }

        base.Dispose(disposing);
    }
}
