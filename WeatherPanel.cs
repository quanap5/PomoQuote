using System.Drawing.Drawing2D;
using System.Drawing.Text;
using PomoQuote.Weather;

namespace PomoQuote;

/// <summary>
/// Shows a city's current weather as three centered lines of icons and text on a transparent background: the place, the
/// temperature and conditions, and humidity and wind. Over a wallpaper the text turns white with a shadow; on the plain
/// background it is dark gray. The icons carry their own outline, so they read on either.
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

    /// <summary>The one icon cache of the weather UI (the widget draws from it too). Disposed with the panel.</summary>
    internal WeatherIconCache Icons { get; } = new();

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

    /// <summary>The city shown, as "City, Country".</summary>
    public string CityName => _cityName;

    /// <summary>The city, the last report and whether the latest refresh failed.</summary>
    internal WeatherSnapshot Snapshot => new(_cityName, _report, _reportTime, _failed);

    /// <summary>Raised whenever the city or the weather text changes.</summary>
    public event EventHandler? Updated;

    /// <summary>Height that fits the three lines at the current display scale.</summary>
    public int MeasureHeight() => _cityFont.Height + _valueFont.Height + _detailFont.Height + Padding.Vertical + 8;

    /// <summary>Switches to another city; the previous city's numbers are dropped.</summary>
    public void SetCity(string cityName)
    {
        _cityName = cityName;
        _report = null;
        _failed = false;
        Invalidate();
        Updated?.Invoke(this, EventArgs.Empty);
    }

    public void ShowReport(WeatherReport report, DateTime time)
    {
        _report = report;
        _reportTime = time;
        _failed = false;
        Invalidate();
        Updated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The refresh failed: "Weather unavailable", plus the last known values marked stale.</summary>
    public void ShowUnavailable()
    {
        _failed = true;
        Invalidate();
        Updated?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var snapshot = Snapshot;
        var y = Padding.Top + 4f;
        DrawLine(graphics, WeatherLines.City(snapshot), _cityFont, ref y);
        DrawLine(graphics, WeatherLines.Value(snapshot), _valueFont, ref y);
        DrawLine(graphics, WeatherLines.Detail(snapshot), _detailFont, ref y);
    }

    /// <summary>Draws one line centered. A line too wide for the panel is shifted left to the padding rather than cut on both sides.</summary>
    private void DrawLine(Graphics graphics, IReadOnlyList<LinePart> parts, Font font, ref float y)
    {
        if (parts.Count > 0)
        {
            var iconSize = IconTextLine.IconSizeFor(font);
            var gap = font.Height * 0.3f;
            var width = IconTextLine.Measure(graphics, parts, font, iconSize, gap);
            var x = Math.Max(Padding.Left, (ClientSize.Width - width) / 2f);
            IconTextLine.Draw(graphics, Icons, parts, font, iconSize, gap, x, y, _onWallpaper ? Color.White : PlainTextColor, _onWallpaper);
        }

        y += font.Height;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cityFont.Dispose();
            _valueFont.Dispose();
            _detailFont.Dispose();
            Icons.Dispose();
        }

        base.Dispose(disposing);
    }
}
