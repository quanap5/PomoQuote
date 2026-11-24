using System.Drawing.Drawing2D;
using PomoQuote.Pomodoro;

namespace PomoQuote.Widget;

/// <summary>The three states the icon beside the timer shows.</summary>
internal enum PomodoroIconState
{
    /// <summary>Not started: the phase waits for Start.</summary>
    Idle,

    /// <summary>A work phase is running or paused.</summary>
    Work,

    /// <summary>A short or long break is running or paused.</summary>
    Rest,
}

/// <summary>
/// The Pomodoro state icon, drawn for this project (no outside artwork): a stopwatch outline for Idle, a filled stopwatch for Work,
/// a cup for Rest, with a small pair of bars on top of the phase icon while the timer is paused. It sits in the same column, size
/// and gap as the weather icons, centred on its row.
/// </summary>
internal static class PomodoroIcon
{
    /// <summary>Idle only when the timer has not been started; a paused timer keeps its phase icon and adds the pause mark.</summary>
    public static PomodoroIconState StateOf(PomodoroPhase phase, PomodoroRunState state) =>
        state == PomodoroRunState.Idle ? PomodoroIconState.Idle : phase == PomodoroPhase.Work ? PomodoroIconState.Work : PomodoroIconState.Rest;

    /// <summary>The name a screen reader and the tooltip use: "Pomodoro idle", "Pomodoro work, paused", "Pomodoro rest".</summary>
    public static string Name(PomodoroPhase phase, PomodoroRunState state)
    {
        var name = StateOf(phase, state) switch
        {
            PomodoroIconState.Idle => "Pomodoro idle, not running",
            PomodoroIconState.Work => "Pomodoro work",
            _ => "Pomodoro rest",
        };
        return state == PomodoroRunState.Paused ? name + ", paused" : name;
    }

    public static void Draw(Graphics graphics, RectangleF box, PomodoroIconState state, Color colour, bool paused, float unit)
    {
        var previous = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var line = Math.Max(1.2f, 1.5f * unit);
        using var pen = new Pen(colour, line) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(colour);
        var cx = box.X + box.Width / 2;

        switch (state)
        {
            case PomodoroIconState.Idle:
            {
                // A stopwatch at rest: outline only, no hand.
                var face = new RectangleF(box.X + box.Width * 0.14f, box.Y + box.Height * 0.2f, box.Width * 0.72f, box.Height * 0.72f);
                graphics.DrawEllipse(pen, face);
                graphics.DrawLine(pen, cx, box.Y + box.Height * 0.06f, cx, box.Y + box.Height * 0.2f);
                graphics.DrawLine(pen, cx - box.Width * 0.14f, box.Y + box.Height * 0.06f, cx + box.Width * 0.14f, box.Y + box.Height * 0.06f);
                break;
            }

            case PomodoroIconState.Work:
            {
                // A running stopwatch: a filled face with its hand cut out.
                var face = new RectangleF(box.X + box.Width * 0.1f, box.Y + box.Height * 0.18f, box.Width * 0.8f, box.Height * 0.8f);
                graphics.FillEllipse(brush, face);
                graphics.DrawLine(pen, cx, box.Y + box.Height * 0.04f, cx, box.Y + box.Height * 0.18f);
                graphics.DrawLine(pen, cx - box.Width * 0.14f, box.Y + box.Height * 0.04f, cx + box.Width * 0.14f, box.Y + box.Height * 0.04f);
                using var hand = new Pen(Color.FromArgb(28, 32, 40), line) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                var centre = new PointF(face.X + face.Width / 2, face.Y + face.Height / 2);
                graphics.DrawLine(hand, centre.X, centre.Y, centre.X, face.Y + face.Height * 0.2f);
                graphics.DrawLine(hand, centre.X, centre.Y, centre.X + face.Width * 0.24f, centre.Y + face.Height * 0.12f);
                break;
            }

            default:
            {
                // A cup with steam.
                var cup = new RectangleF(box.X + box.Width * 0.12f, box.Y + box.Height * 0.42f, box.Width * 0.58f, box.Height * 0.5f);
                using (var body = PillBar.Rounded(cup, 2.2f * unit)) graphics.FillPath(brush, body);
                var handle = new RectangleF(cup.Right - 1.2f * unit, cup.Y + cup.Height * 0.16f, box.Width * 0.24f, cup.Height * 0.52f);
                graphics.DrawArc(pen, handle, -90, 180);
                graphics.DrawBezier(pen, box.X + box.Width * 0.28f, box.Y + box.Height * 0.34f, box.X + box.Width * 0.2f, box.Y + box.Height * 0.22f, box.X + box.Width * 0.36f, box.Y + box.Height * 0.16f, box.X + box.Width * 0.28f, box.Y + box.Height * 0.04f);
                graphics.DrawBezier(pen, box.X + box.Width * 0.5f, box.Y + box.Height * 0.34f, box.X + box.Width * 0.42f, box.Y + box.Height * 0.22f, box.X + box.Width * 0.58f, box.Y + box.Height * 0.16f, box.X + box.Width * 0.5f, box.Y + box.Height * 0.04f);
                break;
            }
        }

        if (paused)
        {
            // Two small bars on a panel-coloured dot at the lower right: the phase is paused, not over.
            var dot = new RectangleF(box.Right - box.Width * 0.5f, box.Bottom - box.Height * 0.5f, box.Width * 0.56f, box.Height * 0.56f);
            using var backdrop = new SolidBrush(Color.FromArgb(28, 32, 40));
            graphics.FillEllipse(backdrop, RectangleF.Inflate(dot, 0.8f * unit, 0.8f * unit));
            using var white = new SolidBrush(Color.FromArgb(244, 246, 248));
            var barWidth = Math.Max(1.2f, dot.Width * 0.2f);
            graphics.FillRectangle(white, dot.X + dot.Width * 0.26f, dot.Y + dot.Height * 0.22f, barWidth, dot.Height * 0.56f);
            graphics.FillRectangle(white, dot.X + dot.Width * 0.56f, dot.Y + dot.Height * 0.22f, barWidth, dot.Height * 0.56f);
        }

        graphics.SmoothingMode = previous;
    }
}
