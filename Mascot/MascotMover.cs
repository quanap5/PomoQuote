namespace HelloWorldBot.Mascot;

/// <summary>What the robot is "feeling", taken from the Pomodoro: it decides how it moves.</summary>
internal enum MascotMood
{
    /// <summary>No timer running (or paused): a slow stroll.</summary>
    Idle,

    /// <summary>A work phase is running: walking briskly.</summary>
    Working,

    /// <summary>A break is running: asleep in place.</summary>
    Break,
}

/// <summary>Things that happened during an update that the caller reacts to (e.g. by playing a sound).</summary>
[Flags]
internal enum MascotEvents
{
    None = 0,

    /// <summary>The robot jumped from the end of one monitor onto the next: shout.</summary>
    CrossingStarted = 1,
}

/// <summary>Where and how the robot is drawn right now. <see cref="FeetX"/>/<see cref="FeetY"/> are screen pixels.</summary>
internal sealed record MascotPose(
    double FeetX,
    double FeetY,
    bool FacingRight,
    MascotAnimation Animation,
    int Frame,
    string MonitorId,
    int Dpi);

/// <summary>
/// The robot's movement and animation as pure logic: no windows, no clock of its own. The caller feeds it
/// elapsed time and the monitor layout, so it is fully testable. It walks along each monitor's walkway,
/// turns round at the two ends of the whole route, and jumps (with a shout) from one monitor to the next.
/// </summary>
internal sealed class MascotMover
{
    /// <summary>Walking speed at "normal" speed, in device-independent pixels per second.</summary>
    public const double BaseSpeedDipsPerSecond = 80;

    public const double IdleSpeedFactor = 0.35;
    public static readonly TimeSpan CrossingDuration = TimeSpan.FromSeconds(0.8);
    public static readonly TimeSpan CelebrationDuration = TimeSpan.FromSeconds(2.4);
    private const double CrossingArcDips = 56;
    private const double HopDips = 22;

    private IReadOnlyList<Walkway> _route = [];
    private int _index;
    private double _x;
    private int _direction = 1;
    private Crossing? _crossing;
    private double _celebrationLeft;
    private double _celebrationElapsed;
    private double _animationSeconds;
    private MascotAnimation _animation = MascotAnimation.Walk;
    private bool _slow;

    private sealed record Crossing(int From, int To, double StartX, double EndX, double Elapsed);

    public MascotMover(int sizeScale = 3, double speedFactor = 1.0)
    {
        SizeScale = sizeScale;
        SpeedFactor = speedFactor;
    }

    public int SizeScale { get; set; }

    /// <summary>1.0 is normal speed.</summary>
    public double SpeedFactor { get; set; }

    public MascotMood Mood { get; set; } = MascotMood.Idle;

    public bool HasRoute => _route.Count > 0;

    public bool IsCrossing => _crossing is not null;

    public bool IsCelebrating => _celebrationLeft > 0;

    public MascotPose? Pose { get; private set; }

    /// <summary>
    /// Replaces the monitor layout (monitors plugged in or out, settings changed). The robot stays on its
    /// monitor if that is still part of the route, otherwise it reappears at the start of the route.
    /// A jump in progress is cancelled, since its monitors may be gone.
    /// </summary>
    public void SetRoute(IReadOnlyList<Walkway> route)
    {
        var currentId = _route.Count > 0 && _index < _route.Count ? _route[_index].MonitorId : null;
        _route = route;
        _crossing = null;

        if (route.Count == 0)
        {
            Pose = null;
            return;
        }

        var kept = currentId is null ? -1 : route.ToList().FindIndex(w => w.MonitorId == currentId);
        if (kept >= 0)
        {
            _index = kept;
            _x = ClampX(route[kept], _x);
        }
        else
        {
            _index = 0;
            _x = MinX(route[0]);
            _direction = 1;
        }

        Pose = BuildPose(0);
    }

    /// <summary>Makes the robot hop and shout in place for a couple of seconds (a work phase just ended).</summary>
    public void Celebrate()
    {
        if (_crossing is not null || _route.Count == 0) return;
        _celebrationLeft = CelebrationDuration.TotalSeconds;
        _celebrationElapsed = 0;
    }

    /// <summary>Moves the robot on by <paramref name="elapsed"/> and returns what happened.</summary>
    public MascotEvents Update(TimeSpan elapsed)
    {
        if (_route.Count == 0) return MascotEvents.None;

        var seconds = Math.Max(0, elapsed.TotalSeconds);
        var events = MascotEvents.None;
        var previousAnimation = _animation;
        var previousSlow = _slow;
        double hop = 0;

        if (_crossing is not null)
        {
            _animation = MascotAnimation.Shout;
            AdvanceCrossing(seconds);
        }
        else if (_celebrationLeft > 0)
        {
            _animation = MascotAnimation.Shout;
            _celebrationElapsed += seconds;
            _celebrationLeft = Math.Max(0, _celebrationLeft - seconds);
            // Two hops over the celebration.
            hop = Dpi(_route[_index]) / 96.0 * HopDips * Math.Abs(Math.Sin(Math.PI * 2 * _celebrationElapsed / CelebrationDuration.TotalSeconds));
        }
        else if (Mood == MascotMood.Break)
        {
            _animation = MascotAnimation.Sleep;
            _slow = false;
        }
        else
        {
            _animation = MascotAnimation.Walk;
            _slow = Mood == MascotMood.Idle;
            events |= Walk(seconds);
        }

        if (_animation != previousAnimation || _slow != previousSlow) _animationSeconds = 0;
        else _animationSeconds += seconds;

        Pose = BuildPose(hop);
        return events;
    }

    private MascotEvents Walk(double seconds)
    {
        var walkway = _route[_index];
        var speed = BaseSpeedDipsPerSecond * SpeedFactor * Dpi(walkway) / 96.0 * (Mood == MascotMood.Idle ? IdleSpeedFactor : 1.0);
        _x += _direction * speed * seconds;

        var min = MinX(walkway);
        var max = MaxX(walkway);
        if (_x >= min && _x <= max) return MascotEvents.None;

        var next = _index + _direction;
        if (next < 0 || next >= _route.Count)
        {
            // End of the whole route: turn round.
            _x = Math.Clamp(_x, min, max);
            _direction = -_direction;
            return MascotEvents.None;
        }

        var target = _route[next];
        var startX = Math.Clamp(_x, min, max);
        var endX = _direction > 0 ? MinX(target) : MaxX(target);
        _crossing = new Crossing(_index, next, startX, endX, 0);
        _x = startX;
        return MascotEvents.CrossingStarted;
    }

    private void AdvanceCrossing(double seconds)
    {
        var crossing = _crossing!;
        var elapsed = crossing.Elapsed + seconds;
        if (elapsed >= CrossingDuration.TotalSeconds)
        {
            _index = crossing.To;
            _x = crossing.EndX;
            _crossing = null;
            return;
        }

        _crossing = crossing with { Elapsed = elapsed };
    }

    private MascotPose BuildPose(double hop)
    {
        var walkway = _route[_index];
        var x = _x;
        var y = (double)walkway.GroundY;
        var dpi = walkway.Dpi;
        var monitorId = walkway.MonitorId;

        if (_crossing is { } crossing)
        {
            var from = _route[crossing.From];
            var to = _route[crossing.To];
            var t = Math.Clamp(crossing.Elapsed / CrossingDuration.TotalSeconds, 0, 1);
            x = crossing.StartX + (crossing.EndX - crossing.StartX) * t;
            y = from.GroundY + (to.GroundY - from.GroundY) * t - 4 * t * (1 - t) * CrossingArcDips * Math.Max(from.Dpi, to.Dpi) / 96.0;
            monitorId = t < 0.5 ? from.MonitorId : to.MonitorId;
            dpi = t < 0.5 ? from.Dpi : to.Dpi;
        }

        y -= hop;
        return new MascotPose(
            x,
            y,
            _direction > 0,
            _animation,
            MascotSprites.Frame(_animation, _animationSeconds, _slow),
            monitorId,
            dpi);
    }

    private double HalfWidth(Walkway walkway) => MascotSprites.PixelSize(SizeScale, walkway.Dpi) / 2.0;

    private double MinX(Walkway walkway) => walkway.Left + Math.Min(HalfWidth(walkway), (walkway.Right - walkway.Left) / 2.0);

    private double MaxX(Walkway walkway) => walkway.Right - Math.Min(HalfWidth(walkway), (walkway.Right - walkway.Left) / 2.0);

    private double ClampX(Walkway walkway, double x) => Math.Clamp(x, MinX(walkway), MaxX(walkway));

    private static int Dpi(Walkway walkway) => walkway.Dpi;
}
