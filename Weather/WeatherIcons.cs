using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PomoQuote.Weather;

/// <summary>Every icon the weather UI can show: one per kind of weather (with day and night variants) and one per datum.</summary>
public enum WeatherIconId
{
    // Conditions
    ClearDay,
    ClearNight,
    MainlyClearDay,
    MainlyClearNight,
    PartlyCloudyDay,
    PartlyCloudyNight,
    Overcast,
    Fog,
    Drizzle,
    FreezingDrizzle,
    Rain,
    FreezingRain,
    Snow,
    SnowGrains,
    RainShowersDay,
    RainShowersNight,
    SnowShowersDay,
    SnowShowersNight,
    Thunderstorm,
    ThunderstormHail,
    Unknown,

    // Data
    Thermometer,
    Humidity,
    Wind,
    WindDirection,
    Location,
    Updated,
    Warning,
}

/// <summary>
/// The weather icon set: original vector drawings, painted in code with GDI+ (no image files, no third-party art), so
/// they are sharp at any size and display scale. One flat style throughout: a light-coloured shape with a dark outline,
/// which stays readable on the plain background, on every wallpaper and on the dark widget panel.
/// </summary>
internal static class WeatherIconPainter
{
    private static readonly Color Outline = Color.FromArgb(34, 42, 58);
    private static readonly Color CloudWhite = Color.FromArgb(240, 244, 249);
    private static readonly Color CloudGray = Color.FromArgb(176, 186, 200);
    private static readonly Color SunYellow = Color.FromArgb(255, 196, 46);
    private static readonly Color MoonCream = Color.FromArgb(246, 230, 150);
    private static readonly Color RainBlue = Color.FromArgb(72, 156, 240);
    private static readonly Color IceBlue = Color.FromArgb(150, 214, 255);
    private static readonly Color BoltYellow = Color.FromArgb(255, 210, 40);
    private static readonly Color FogGray = Color.FromArgb(196, 205, 217);
    private static readonly Color WindBlue = Color.FromArgb(128, 170, 214);
    private static readonly Color Red = Color.FromArgb(232, 76, 68);

    private static readonly (float X, float Y)[] Eight =
        [(-1, 0), (1, 0), (0, -1), (0, 1), (-0.7f, -0.7f), (0.7f, -0.7f), (-0.7f, 0.7f), (0.7f, 0.7f)];

    /// <summary>Draws <paramref name="icon"/> into the unit square (0,0)-(1,1) of the graphics' current transform.</summary>
    /// <param name="outlineWidth">The outline thickness in unit coordinates; callers raise it for tiny icons.</param>
    public static void Draw(Graphics g, WeatherIconId icon, float outlineWidth, float windDegrees = 0)
    {
        var ow = outlineWidth;
        switch (icon)
        {
            case WeatherIconId.ClearDay:
                Sun(g, 0.5f, 0.5f, 0.22f, 0.34f, 0.42f, ow);
                break;
            case WeatherIconId.ClearNight:
                Moon(g, 0.46f, 0.54f, 0.32f, ow);
                Sparkle(g, 0.78f, 0.2f, 0.09f, ow);
                Sparkle(g, 0.84f, 0.46f, 0.055f, ow);
                break;
            case WeatherIconId.MainlyClearDay:
                Sun(g, 0.42f, 0.42f, 0.2f, 0.27f, 0.33f, ow);
                Cloud(g, 0.42f, 0.5f, 0.5f, CloudWhite, ow);
                break;
            case WeatherIconId.MainlyClearNight:
                Moon(g, 0.4f, 0.42f, 0.27f, ow);
                Cloud(g, 0.42f, 0.5f, 0.5f, CloudWhite, ow);
                break;
            case WeatherIconId.PartlyCloudyDay:
                Sun(g, 0.36f, 0.36f, 0.16f, 0.21f, 0.26f, ow);
                Cloud(g, 0.12f, 0.3f, 0.82f, CloudWhite, ow);
                break;
            case WeatherIconId.PartlyCloudyNight:
                Moon(g, 0.34f, 0.34f, 0.23f, ow);
                Cloud(g, 0.12f, 0.3f, 0.82f, CloudWhite, ow);
                break;
            case WeatherIconId.Overcast:
                Cloud(g, 0.02f, 0.14f, 0.62f, CloudGray, ow);
                Cloud(g, 0.22f, 0.3f, 0.76f, CloudWhite, ow);
                break;
            case WeatherIconId.Fog:
                Cloud(g, 0.1f, 0.08f, 0.8f, CloudWhite, ow);
                Line(g, 0.14f, 0.68f, 0.74f, 0.68f, FogGray, 0.07f, ow);
                Line(g, 0.26f, 0.79f, 0.86f, 0.79f, FogGray, 0.07f, ow);
                Line(g, 0.14f, 0.9f, 0.6f, 0.9f, FogGray, 0.07f, ow);
                break;
            case WeatherIconId.Drizzle:
                Cloud(g, 0.1f, 0.08f, 0.8f, CloudWhite, ow);
                Drops(g, [(0.3f, 0.7f), (0.5f, 0.76f), (0.7f, 0.7f)], 0.1f, ow);
                break;
            case WeatherIconId.FreezingDrizzle:
                Cloud(g, 0.1f, 0.08f, 0.8f, CloudWhite, ow);
                Drops(g, [(0.28f, 0.7f), (0.72f, 0.7f)], 0.1f, ow);
                Flake(g, 0.5f, 0.82f, 0.1f, ow);
                break;
            case WeatherIconId.Rain:
                Cloud(g, 0.1f, 0.06f, 0.8f, CloudWhite, ow);
                Drops(g, [(0.28f, 0.66f), (0.5f, 0.72f), (0.72f, 0.66f)], 0.19f, ow);
                break;
            case WeatherIconId.FreezingRain:
                Cloud(g, 0.1f, 0.06f, 0.8f, CloudWhite, ow);
                Drops(g, [(0.27f, 0.66f), (0.73f, 0.66f)], 0.19f, ow);
                Flake(g, 0.5f, 0.82f, 0.11f, ow);
                break;
            case WeatherIconId.Snow:
                Cloud(g, 0.1f, 0.06f, 0.8f, CloudWhite, ow);
                Flake(g, 0.28f, 0.76f, 0.1f, ow);
                Flake(g, 0.52f, 0.85f, 0.09f, ow);
                Flake(g, 0.74f, 0.74f, 0.1f, ow);
                break;
            case WeatherIconId.SnowGrains:
                Cloud(g, 0.1f, 0.06f, 0.8f, CloudWhite, ow);
                foreach (var (x, y) in new[] { (0.28f, 0.74f), (0.5f, 0.82f), (0.72f, 0.74f), (0.4f, 0.89f), (0.62f, 0.9f) }) Grain(g, x, y, 0.05f, ow);
                break;
            case WeatherIconId.RainShowersDay:
                Sun(g, 0.32f, 0.32f, 0.13f, 0.18f, 0.23f, ow);
                Cloud(g, 0.22f, 0.2f, 0.72f, CloudWhite, ow);
                Drops(g, [(0.34f, 0.7f), (0.55f, 0.76f), (0.76f, 0.68f)], 0.17f, ow);
                break;
            case WeatherIconId.RainShowersNight:
                Moon(g, 0.28f, 0.28f, 0.2f, ow);
                Cloud(g, 0.22f, 0.2f, 0.72f, CloudWhite, ow);
                Drops(g, [(0.34f, 0.7f), (0.55f, 0.76f), (0.76f, 0.68f)], 0.17f, ow);
                break;
            case WeatherIconId.SnowShowersDay:
                Sun(g, 0.32f, 0.32f, 0.13f, 0.18f, 0.23f, ow);
                Cloud(g, 0.22f, 0.2f, 0.72f, CloudWhite, ow);
                Flake(g, 0.36f, 0.78f, 0.09f, ow);
                Flake(g, 0.6f, 0.85f, 0.09f, ow);
                Flake(g, 0.8f, 0.74f, 0.09f, ow);
                break;
            case WeatherIconId.SnowShowersNight:
                Moon(g, 0.28f, 0.28f, 0.2f, ow);
                Cloud(g, 0.22f, 0.2f, 0.72f, CloudWhite, ow);
                Flake(g, 0.36f, 0.78f, 0.09f, ow);
                Flake(g, 0.6f, 0.85f, 0.09f, ow);
                Flake(g, 0.8f, 0.74f, 0.09f, ow);
                break;
            case WeatherIconId.Thunderstorm:
                Cloud(g, 0.1f, 0.04f, 0.8f, CloudGray, ow);
                Bolt(g, ow);
                break;
            case WeatherIconId.ThunderstormHail:
                Cloud(g, 0.1f, 0.04f, 0.8f, CloudGray, ow);
                Bolt(g, ow);
                Grain(g, 0.22f, 0.84f, 0.06f, ow);
                Grain(g, 0.8f, 0.88f, 0.06f, ow);
                break;
            case WeatherIconId.Unknown:
                Question(g, ow);
                break;
            case WeatherIconId.Thermometer:
                Thermometer(g, ow);
                break;
            case WeatherIconId.Humidity:
                Droplet(g, ow);
                break;
            case WeatherIconId.Wind:
                WindSwooshes(g, ow);
                break;
            case WeatherIconId.WindDirection:
                Arrow(g, windDegrees, ow);
                break;
            case WeatherIconId.Location:
                Pin(g, ow);
                break;
            case WeatherIconId.Updated:
                Clock(g, ow);
                break;
            case WeatherIconId.Warning:
                Triangle(g, ow);
                break;
        }
    }

    // -- Building blocks ------------------------------------------------------------------------------------------

    /// <summary>
    /// Fills a shape with an outline by painting it dark at eight small offsets first, then in its own colour on top.
    /// Overlapping shapes therefore merge into one outlined silhouette with no seams.
    /// </summary>
    private static void Outlined(Graphics g, Action<Graphics, Brush> shape, Color fill, float ow)
    {
        using var dark = new SolidBrush(Outline);
        foreach (var (dx, dy) in Eight)
        {
            g.TranslateTransform(dx * ow, dy * ow, MatrixOrder.Prepend);
            shape(g, dark);
            g.TranslateTransform(-dx * ow, -dy * ow, MatrixOrder.Prepend);
        }

        using var brush = new SolidBrush(fill);
        shape(g, brush);
    }

    private static void Line(Graphics g, float x1, float y1, float x2, float y2, Color color, float width, float ow)
    {
        using var dark = new Pen(Outline, width + 2 * ow) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(dark, x1, y1, x2, y2);
        g.DrawLine(pen, x1, y1, x2, y2);
    }

    /// <summary>A cloud whose top-left corner of its 1 x 0.62 box is (<paramref name="ox"/>, <paramref name="oy"/>), scaled by <paramref name="s"/>.</summary>
    private static void Cloud(Graphics g, float ox, float oy, float s, Color fill, float ow)
    {
        Outlined(g, (graphics, brush) =>
        {
            using var basePath = RoundedRectangle(ox + 0.08f * s, oy + 0.34f * s, 0.84f * s, 0.3f * s, 0.15f * s);
            graphics.FillPath(brush, basePath);
            Circle(graphics, brush, ox + 0.30f * s, oy + 0.38f * s, 0.17f * s);
            Circle(graphics, brush, ox + 0.55f * s, oy + 0.30f * s, 0.24f * s);
            Circle(graphics, brush, ox + 0.76f * s, oy + 0.41f * s, 0.15f * s);
        }, fill, ow);
    }

    private static void Sun(Graphics g, float cx, float cy, float radius, float rayFrom, float rayTo, float ow)
    {
        for (var i = 0; i < 8; i++)
        {
            var angle = i * Math.PI / 4;
            var cos = (float)Math.Cos(angle);
            var sin = (float)Math.Sin(angle);
            Line(g, cx + cos * rayFrom, cy + sin * rayFrom, cx + cos * rayTo, cy + sin * rayTo, SunYellow, 0.065f, ow);
        }

        Outlined(g, (graphics, brush) => Circle(graphics, brush, cx, cy, radius), SunYellow, ow);
    }

    private static void Moon(Graphics g, float cx, float cy, float radius, float ow)
    {
        Outlined(g, (graphics, brush) =>
        {
            using var outer = new GraphicsPath();
            outer.AddEllipse(cx - radius, cy - radius, 2 * radius, 2 * radius);
            using var region = new Region(outer);
            using var cut = new GraphicsPath();
            var r2 = radius * 0.82f;
            cut.AddEllipse(cx + radius * 0.5f - r2, cy - radius * 0.32f - r2, 2 * r2, 2 * r2);
            region.Exclude(cut);
            graphics.FillRegion(brush, region);
        }, MoonCream, ow);
    }

    private static void Sparkle(Graphics g, float cx, float cy, float r, float ow)
    {
        Line(g, cx - r, cy, cx + r, cy, MoonCream, 0.045f, ow * 0.7f);
        Line(g, cx, cy - r, cx, cy + r, MoonCream, 0.045f, ow * 0.7f);
    }

    private static void Drops(Graphics g, (float X, float Y)[] starts, float length, float ow)
    {
        foreach (var (x, y) in starts) Line(g, x, y, x - length * 0.35f, y + length, RainBlue, 0.07f, ow);
    }

    private static void Flake(Graphics g, float cx, float cy, float r, float ow)
    {
        for (var i = 0; i < 3; i++)
        {
            var angle = i * Math.PI / 3 + Math.PI / 6;
            var dx = (float)Math.Cos(angle) * r;
            var dy = (float)Math.Sin(angle) * r;
            Line(g, cx - dx, cy - dy, cx + dx, cy + dy, IceBlue, 0.05f, ow * 0.8f);
        }
    }

    private static void Grain(Graphics g, float cx, float cy, float r, float ow) =>
        Outlined(g, (graphics, brush) => Circle(graphics, brush, cx, cy, r), Color.White, ow * 0.8f);

    private static void Bolt(Graphics g, float ow) =>
        Outlined(g, (graphics, brush) => graphics.FillPolygon(brush, new PointF[]
        {
            new(0.56f, 0.5f), new(0.36f, 0.78f), new(0.5f, 0.78f), new(0.42f, 0.94f), new(0.68f, 0.66f), new(0.53f, 0.66f), new(0.64f, 0.5f),
        }), BoltYellow, ow);

    private static void Question(Graphics g, float ow)
    {
        Outlined(g, (graphics, brush) => Circle(graphics, brush, 0.5f, 0.5f, 0.38f), CloudGray, ow);
        using var path = new GraphicsPath();
        using var family = new FontFamily("Segoe UI");
        path.AddString("?", family, (int)FontStyle.Bold, 0.62f, new PointF(0.5f, 0.5f), new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
        using var brush = new SolidBrush(Outline);
        g.FillPath(brush, path);
    }

    private static void Thermometer(Graphics g, float ow)
    {
        Outlined(g, (graphics, brush) =>
        {
            using var stem = RoundedRectangle(0.4f, 0.08f, 0.2f, 0.6f, 0.1f);
            graphics.FillPath(brush, stem);
            Circle(graphics, brush, 0.5f, 0.76f, 0.2f);
        }, CloudWhite, ow);
        using var red = new SolidBrush(Red);
        g.FillEllipse(red, 0.34f, 0.6f, 0.32f, 0.32f);
        using var mercury = RoundedRectangle(0.45f, 0.26f, 0.1f, 0.5f, 0.05f);
        g.FillPath(red, mercury);
    }

    private static void Droplet(Graphics g, float ow) =>
        Outlined(g, (graphics, brush) =>
        {
            Circle(graphics, brush, 0.5f, 0.64f, 0.3f);
            graphics.FillPolygon(brush, new PointF[] { new(0.5f, 0.08f), new(0.2f, 0.52f), new(0.8f, 0.52f) });
        }, RainBlue, ow);

    private static void WindSwooshes(Graphics g, float ow)
    {
        Swoosh(g, 0.12f, 0.4f, 0.62f, -1, 0.12f, ow);
        Swoosh(g, 0.12f, 0.56f, 0.74f, -1, 0.1f, ow);
        Swoosh(g, 0.22f, 0.7f, 0.56f, 1, 0.1f, ow);
    }

    private static void Swoosh(Graphics g, float x1, float y, float x2, int curl, float r, float ow)
    {
        using var path = new GraphicsPath();
        path.AddLine(x1, y, x2, y);
        path.AddArc(x2 - r, y + (curl < 0 ? -2 * r : 0), 2 * r, 2 * r, curl < 0 ? 90 : 270, curl < 0 ? -270 : 270);
        using var dark = new Pen(Outline, 0.075f + 2 * ow) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var pen = new Pen(WindBlue, 0.075f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawPath(dark, path);
        g.DrawPath(pen, path);
    }

    /// <summary>An arrow pointing the way the wind blows: wind from the north (0 degrees) blows south, so the arrow points down.</summary>
    private static void Arrow(Graphics g, float windFromDegrees, float ow)
    {
        var state = g.Save();
        g.TranslateTransform(0.5f, 0.5f, MatrixOrder.Prepend);
        g.RotateTransform(windFromDegrees + 180f, MatrixOrder.Prepend);
        g.TranslateTransform(-0.5f, -0.5f, MatrixOrder.Prepend);
        Outlined(g, (graphics, brush) => graphics.FillPolygon(brush, new PointF[]
        {
            new(0.5f, 0.1f), new(0.78f, 0.88f), new(0.5f, 0.72f), new(0.22f, 0.88f),
        }), RainBlue, ow);
        g.Restore(state);
    }

    private static void Pin(Graphics g, float ow)
    {
        Outlined(g, (graphics, brush) =>
        {
            Circle(graphics, brush, 0.5f, 0.36f, 0.28f);
            graphics.FillPolygon(brush, new PointF[] { new(0.5f, 0.93f), new(0.25f, 0.5f), new(0.75f, 0.5f) });
        }, Red, ow);
        using var hole = new SolidBrush(CloudWhite);
        g.FillEllipse(hole, 0.39f, 0.25f, 0.22f, 0.22f);
    }

    private static void Clock(Graphics g, float ow)
    {
        Outlined(g, (graphics, brush) => Circle(graphics, brush, 0.5f, 0.5f, 0.38f), CloudWhite, ow);
        using var pen = new Pen(Outline, 0.08f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, 0.5f, 0.5f, 0.5f, 0.26f);
        g.DrawLine(pen, 0.5f, 0.5f, 0.68f, 0.6f);
    }

    private static void Triangle(Graphics g, float ow)
    {
        Outlined(g, (graphics, brush) => graphics.FillPolygon(brush, new PointF[] { new(0.5f, 0.1f), new(0.92f, 0.86f), new(0.08f, 0.86f) }), SunYellow, ow);
        using var pen = new Pen(Outline, 0.09f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, 0.5f, 0.38f, 0.5f, 0.58f);
        using var dot = new SolidBrush(Outline);
        g.FillEllipse(dot, 0.455f, 0.67f, 0.09f, 0.09f);
    }

    private static void Circle(Graphics g, Brush brush, float cx, float cy, float r) => g.FillEllipse(brush, cx - r, cy - r, 2 * r, 2 * r);

    private static GraphicsPath RoundedRectangle(float x, float y, float width, float height, float radius)
    {
        var d = Math.Min(radius * 2, Math.Min(width, height));
        var path = new GraphicsPath();
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + width - d, y, d, d, 270, 90);
        path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
        path.AddArc(x, y + height - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// Keeps each icon, drawn once at the exact pixel size it is needed in (so it is never scaled and never blurry), and
/// hands the same bitmap out again. One cache serves the whole weather UI; disposing it releases every bitmap.
/// </summary>
internal sealed class WeatherIconCache : IDisposable
{
    private readonly Dictionary<(WeatherIconId Icon, int Size, int Degrees), Bitmap> _bitmaps = [];

    /// <summary>How many icons are cached right now.</summary>
    public int Count => _bitmaps.Count;

    /// <summary>
    /// The icon as a <paramref name="size"/> x <paramref name="size"/> premultiplied-alpha bitmap. The bitmap belongs to the
    /// cache: do not dispose it. <paramref name="windDegrees"/> only matters for <see cref="WeatherIconId.WindDirection"/>
    /// and is rounded to 15-degree steps so the cache stays small.
    /// </summary>
    public Bitmap Get(WeatherIconId icon, int size, double windDegrees = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        size = Math.Clamp(size, 8, 512);
        var degrees = icon == WeatherIconId.WindDirection ? (int)(Math.Round(((windDegrees % 360) + 360) % 360 / 15) * 15) % 360 : 0;
        var key = (icon, size, degrees);
        if (_bitmaps.TryGetValue(key, out var cached)) return cached;

        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);

            // A little inset so outlines are never cut off at the edge; thicker outlines keep tiny icons readable.
            const float inset = 0.04f;
            graphics.TranslateTransform(size * inset, size * inset);
            graphics.ScaleTransform(size * (1 - 2 * inset), size * (1 - 2 * inset));
            var outline = Math.Max(0.032f, 0.85f / size);
            WeatherIconPainter.Draw(graphics, icon, outline, degrees);
        }

        _bitmaps[key] = bitmap;
        return bitmap;
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var bitmap in _bitmaps.Values) bitmap.Dispose();
        _bitmaps.Clear();
    }
}
