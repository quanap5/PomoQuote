using System.Drawing.Drawing2D;

namespace HelloWorldBot;

/// <summary>
/// A round analog clock showing the computer's local time. It fits the largest circle
/// that the control's bounds allow, centered, and repaints when the second changes.
/// </summary>
internal sealed class AnalogClockControl : Control
{
    private const int HourMarkCount = 12;
    private const int PollIntervalMilliseconds = 200;

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = PollIntervalMilliseconds };
    private DateTime _shownTime = DateTime.Now;

    public AnalogClockControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;

        // Polling several times a second keeps the hands within 1 second of the system clock
        // without drifting like a single 1000 ms timer would.
        _timer.Tick += (_, _) => RefreshIfSecondChanged();
        _timer.Start();
    }

    /// <summary>Clockwise angle in degrees from 12 o'clock for each hand.</summary>
    internal static (double Hour, double Minute, double Second) HandAngles(DateTime time)
    {
        var second = time.Second + time.Millisecond / 1000.0;
        var minute = time.Minute + second / 60.0;
        var hour = time.Hour % 12 + minute / 60.0;
        return (hour * 30.0, minute * 6.0, time.Second * 6.0);
    }

    private void RefreshIfSecondChanged()
    {
        var now = DateTime.Now;
        if (now.Second == _shownTime.Second && now.Minute == _shownTime.Minute) return;
        _shownTime = now;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var diameter = Math.Min(ClientSize.Width, ClientSize.Height) - 4f;
        if (diameter < 20f) return;

        var center = new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f);
        var radius = diameter / 2f;

        using (var rim = new Pen(Color.DimGray, Math.Max(2f, radius / 30f)))
        {
            graphics.DrawEllipse(rim, center.X - radius, center.Y - radius, diameter, diameter);
        }

        using (var markPen = CreateRoundPen(Color.DimGray, Math.Max(2f, radius / 25f)))
        {
            for (var i = 0; i < HourMarkCount; i++)
            {
                var angle = i * 360.0 / HourMarkCount;
                graphics.DrawLine(markPen, PointAt(center, angle, radius * 0.84f), PointAt(center, angle, radius * 0.94f));
            }
        }

        var (hour, minute, second) = HandAngles(_shownTime);
        DrawHand(graphics, center, hour, radius * 0.50f, Math.Max(3f, radius / 12f), Color.Black);
        DrawHand(graphics, center, minute, radius * 0.75f, Math.Max(2f, radius / 22f), Color.DarkSlateGray);
        DrawHand(graphics, center, second, radius * 0.85f, Math.Max(1f, radius / 50f), Color.Red);

        var hubRadius = Math.Max(3f, radius / 20f);
        using var hub = new SolidBrush(Color.Red);
        graphics.FillEllipse(hub, center.X - hubRadius, center.Y - hubRadius, hubRadius * 2, hubRadius * 2);
    }

    private static Pen CreateRoundPen(Color color, float width) =>
        new(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };

    private static void DrawHand(Graphics graphics, PointF center, double angle, float length, float width, Color color)
    {
        using var pen = CreateRoundPen(color, width);
        graphics.DrawLine(pen, center, PointAt(center, angle, length));
    }

    private static PointF PointAt(PointF center, double angleDegrees, float distance)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new PointF(
            center.X + (float)(Math.Sin(radians) * distance),
            center.Y - (float)(Math.Cos(radians) * distance));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}
