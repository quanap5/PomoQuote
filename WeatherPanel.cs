using System.Drawing.Drawing2D;
using System.Drawing.Text;
using HelloWorldBot.Weather;

namespace HelloWorldBot;

/// <summary>
/// Shows a city's current weather as three centered lines on a transparent background. Over a
/// wallpaper the text turns white with a shadow; on the plain background it is dark gray.
/// </summary>
internal sealed class WeatherPanel : Control
{
    private static readonly Color PlainTextColor = Color.FromArgb(45, 45, 45);

    private readonly Font _cityFont;
    private readonly Font _valueFont;
    private readonly Font _detailFont;

    private string _cityName = "";
    private WeatherReport? _report;
    private DateTime _reportTime;
    private bool _failed;
    private bool _onWallpaper;

    public WeatherPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        TabStop = false;

        _cityFont = new Font("Segoe UI Semibold", 13f, FontStyle.Regular, GraphicsUnit.Point);
        _valueFont = new Font("Segoe UI", 18f, FontStyle.Regular, GraphicsUnit.Point);
        _detailFont = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
    }

    /// <summary>True while a wallpaper is behind the panel, so the text needs the light style.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool OnWallpaper
    {
        get => _onWallpaper;
        set
        {
            if (_onWallpaper == value) return;
            _onWallpaper = value;
            Invalidate();
        }
    }

    /// <summary>Height that fits the three lines at the current display scale.</summary>
    public int MeasureHeight() => _cityFont.Height + _valueFont.Height + _detailFont.Height + Padding.Vertical + 8;

    /// <summary>Switches to another city; the previous city's numbers are dropped.</summary>
    public void SetCity(string cityName)
    {
        _cityName = cityName;
        _report = null;
        _failed = false;
        Invalidate();
    }

    public void ShowReport(WeatherReport report, DateTime time)
    {
        _report = report;
        _reportTime = time;
        _failed = false;
        Invalidate();
    }

    /// <summary>The refresh failed: "Weather unavailable", plus the last known values marked stale.</summary>
    public void ShowUnavailable()
    {
        _failed = true;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var format = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
        };

        var (valueLine, detailLine) = BuildLines();
        var y = Padding.Top + 4f;
        DrawLine(graphics, _cityName, _cityFont, ref y, format);
        DrawLine(graphics, valueLine, _valueFont, ref y, format);
        DrawLine(graphics, detailLine, _detailFont, ref y, format);
    }

    private (string Value, string Detail) BuildLines()
    {
        if (_report is null)
        {
            return _failed ? ("Weather unavailable", "Will try again shortly") : ("Loading weather…", "");
        }

        var (condition, symbol) = WeatherCodes.Describe(_report.WeatherCode);
        var value = $"{_report.TemperatureCelsius:0.#} °C   {symbol} {condition}";
        var detail = $"Humidity {_report.HumidityPercent}%   Wind {_report.WindSpeedKmh:0.#} km/h";
        if (_failed)
        {
            detail += $"   · Weather unavailable, last updated {_reportTime:HH:mm}";
        }

        return (value, detail);
    }

    private void DrawLine(Graphics graphics, string text, Font font, ref float y, StringFormat format)
    {
        var area = new RectangleF(Padding.Left, y, Math.Max(1, ClientSize.Width - Padding.Horizontal), font.Height);
        if (_onWallpaper)
        {
            using var shadow = new SolidBrush(Color.FromArgb(170, 0, 0, 0));
            graphics.DrawString(text, font, shadow, new RectangleF(area.X + 1, area.Y + 1, area.Width, area.Height), format);
        }

        using var brush = new SolidBrush(_onWallpaper ? Color.White : PlainTextColor);
        graphics.DrawString(text, font, brush, area, format);
        y += font.Height;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cityFont.Dispose();
            _valueFont.Dispose();
            _detailFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
