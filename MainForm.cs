using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace HelloWorldBot;

/// <summary>Shows "Hello World" in a bundled digital font with a real-time analog clock below it.</summary>
internal sealed class MainForm : Form
{
    private const string DisplayText = "Hello World";

    // DSEG's space glyph is very narrow; its "!" glyph is a blank, full-width digit cell.
    private const string DigitalFontDrawnText = "Hello!World";

    private const float FontSizePoints = 36f;
    private const string FallbackFontFamily = "Consolas";

    private static readonly Size LogicalClientSize = new(800, 300);
    private static readonly Size LogicalTextPadding = new(48, 48);
    private const int LogicalMinimumClockSize = 120;

    // The collection must outlive any Font created from it.
    private readonly PrivateFontCollection _fonts = new();
    private readonly Font _displayFont;
    private readonly CenteredTextLabel _label;
    private readonly AnalogClockControl _clock;

    public MainForm()
    {
        var digitalFont = LoadDigitalFont();
        _displayFont = digitalFont ?? new Font(FallbackFontFamily, FontSizePoints, FontStyle.Regular, GraphicsUnit.Point);

        Text = DisplayText;
        StartPosition = FormStartPosition.CenterScreen;

        _label = new CenteredTextLabel(digitalFont is null ? DisplayText : DigitalFontDrawnText)
        {
            Text = DisplayText,
            Font = _displayFont,
            ForeColor = Color.Green,
            Dock = DockStyle.Top,
        };
        _clock = new AnalogClockControl { Dock = DockStyle.Fill };

        // The clock fills what remains below the text band, so the two never overlap.
        Controls.Add(_clock);
        Controls.Add(_label);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Sizes depend on the display scale, so they are applied once the window has a DPI.
        var textBand = _label.MeasureDrawnText() + LogicalToDeviceUnits(LogicalTextPadding);
        _label.Height = textBand.Height;
        var minimumClientSize = new Size(textBand.Width, textBand.Height + LogicalToDeviceUnits(LogicalMinimumClockSize));
        MinimumSize = SizeFromClientSize(minimumClientSize);
        ClientSize = LogicalToDeviceUnits(LogicalClientSize);
        CenterToScreen();
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
        base.Dispose(disposing);

        if (disposing)
        {
            _displayFont.Dispose();
            _fonts.Dispose();
        }
    }
}
