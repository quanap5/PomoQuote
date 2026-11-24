using System.Drawing;

namespace HelloWorldBot.Mascot;

/// <summary>Things that happened during an update that the caller reacts to (e.g. by playing a sound).</summary>
[Flags]
internal enum MascotEvents
{
    None = 0,

    /// <summary>The character jumped from the end of one monitor onto the next: shout.</summary>
    CrossingStarted = 1,
}

/// <summary>
/// Where and how the character is drawn right now. <see cref="FeetX"/>/<see cref="FeetY"/> are screen pixels (bottom
/// centre of the sprite); <see cref="Row"/> and <see cref="Frame"/> pick the cell in the sprite sheet.
/// </summary>
internal sealed record MascotPose(
    double FeetX,
    double FeetY,
    bool FacingRight,
    MascotState State,
    int Row,
    int Frame,
    string MonitorId,
    int Dpi);

/// <summary>
/// The character as pure logic: a state machine with explicit transitions, autonomous behaviour from weighted dice,
/// movement along the monitors' walkways, jumping, falling, dragging and the Pomodoro-driven moods. No windows and no
/// clock of its own: the caller feeds it elapsed time, the monitor layout and a random source, so every behaviour is
/// testable. States never pop: a request for a far-away state (Sleep to Run) is walked through the allowed chain.
/// </summary>
internal sealed class MascotActor
{
    private const double JumpAnticipation = 0.22;
    private const double JumpAirEnd = 0.84;
    private const double IdlePassThroughSeconds = 0.3;
    private const double SleepWakeSeconds = 0.8;

    private readonly Func<double> _random;
    private MascotBrain _brain;
    private readonly Queue<MascotState> _queue = new();
    private MascotConfig _config;
    private IReadOnlyList<Walkway> _route = [];
    private int _index;
    private double _x;
    private int _direction = 1;
    private MascotState _state = MascotState.Idle;
    private AnimationDef _def;
    private double _stateTime;
    private double _holdRemaining;
    private double _phase;
    private double _heightPx;
    private double _fallSpeedPx;
    private bool _falling;
    private int _jumpFrame;
    private MascotState? _turnResume;
    private Crossing? _crossing;
    private double _dragX;
    private double _dragY;
    private bool _started;
    private MascotMood _mood = MascotMood.Idle;

    private sealed record Crossing(int From, int To, double StartX, double EndX, double Elapsed);

    public MascotActor(MascotConfig config, Func<double> random, int sizeScale = 2, double speedFactor = 1.0, int randomActions = 2)
    {
        _config = config;
        _random = random;
        _brain = new MascotBrain(config, random);
        _def = config.Animation(MascotState.Idle);
        SizeScale = sizeScale;
        SpeedFactor = speedFactor;
        RandomActions = randomActions;
    }

    public int SizeScale { get; set; }

    /// <summary>1.0 is normal speed.</summary>
    public double SpeedFactor { get; set; }

    /// <summary>0 = no random actions, 1 = few, 2 = normal.</summary>
    public int RandomActions { get; set; }

    public MascotConfig Config => _config;

    public MascotState State => _state;

    public bool HasRoute => _route.Count > 0;

    public bool IsCrossing => _crossing is not null;

    public bool IsDragging => _state == MascotState.Drag;

    /// <summary>True while the celebration (and the jump that follows it) is still to play.</summary>
    public bool IsCelebrating => _state == MascotState.Celebrate || _queue.Contains(MascotState.Celebrate);

    public MascotPose? Pose { get; private set; }

    /// <summary>The Pomodoro mood. Changing it steers the character towards what that mood does.</summary>
    public MascotMood Mood
    {
        get => _mood;
        set
        {
            if (_mood == value) return;
            _mood = value;
            if (_route.Count > 0 && !IsDragging && _crossing is null) Steer(interrupt: IsFree(_state));
        }
    }

    // -- Setup --------------------------------------------------------------------------------------------------

    /// <summary>Swaps in a reloaded configuration; the character keeps doing what it was doing.</summary>
    public void SetConfig(MascotConfig config)
    {
        _config = config;
        _brain = new MascotBrain(config, _random);
        _def = config.Animation(_state);
    }

    /// <summary>
    /// Replaces the monitor layout (monitors plugged in or out, settings changed). The character stays on its
    /// monitor if that is still part of the route, otherwise it reappears at the start of the route. A jump between
    /// monitors is cancelled and a dragged or falling character is pulled back onto a visible monitor.
    /// </summary>
    public void SetRoute(IReadOnlyList<Walkway> route)
    {
        var currentId = _route.Count > 0 && _index < _route.Count ? _route[_index].MonitorId : null;
        var wasCrossing = _crossing is not null;
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
            _heightPx = 0;
            _falling = false;
            if (IsAirborne(_state) || _state == MascotState.Drag) Enter(MascotState.Land);
        }

        if (_state == MascotState.Drag)
        {
            var clamped = MascotDrag.ClampFeet(new PointF((float)_dragX, (float)_dragY), SpriteSize(route[_index]), route);
            _dragX = clamped.X;
            _dragY = clamped.Y;
        }
        else if (wasCrossing && _state == MascotState.Shout)
        {
            Enter(MascotState.Land);
        }

        if (!_started)
        {
            _started = true;
            Steer(interrupt: true);
        }

        Pose = BuildPose();
    }

    // -- Commands ------------------------------------------------------------------------------------------------

    /// <summary>A work phase ended: celebrate, then jump for joy.</summary>
    public void Celebrate()
    {
        if (_route.Count == 0 || _crossing is not null || IsDragging) return;
        Go(MascotState.Celebrate, interrupt: IsFree(_state) || _state == MascotState.Land);
        if (_state == MascotState.Celebrate || _queue.Contains(MascotState.Celebrate)) _queue.Enqueue(MascotState.Jump);
    }

    /// <summary>A phase was skipped or the timer reset: a sad moment.</summary>
    public void Sadden()
    {
        if (_route.Count == 0 || _crossing is not null || IsDragging) return;
        Go(MascotState.Sad, interrupt: IsFree(_state));
    }

    /// <summary>The character was clicked: it waves or looks surprised, by the configured weights.</summary>
    public void React()
    {
        if (_route.Count == 0 || _crossing is not null || IsDragging || !IsFree(_state)) return;
        Go(_brain.PickReaction(), interrupt: true);
    }

    /// <summary>The character was picked up at <paramref name="feetX"/>, <paramref name="feetY"/> (screen pixels).</summary>
    public void BeginDrag(double feetX, double feetY)
    {
        if (_route.Count == 0) return;
        _crossing = null;
        _queue.Clear();
        _turnResume = null;
        _falling = false;
        _heightPx = 0;
        Enter(MascotState.Drag);
        MoveDrag(feetX, feetY);
        Pose = BuildPose();
    }

    /// <summary>Moves the held character; the position is kept inside a visible monitor.</summary>
    public void DragTo(double feetX, double feetY)
    {
        if (!IsDragging) return;
        MoveDrag(feetX, feetY);
        Pose = BuildPose();
    }

    /// <summary>Lets go: the character falls to the taskbar strip of the monitor it is over and lands.</summary>
    public void EndDrag()
    {
        if (!IsDragging || _route.Count == 0) return;

        var (index, x, fall) = MascotDrag.LandingTarget(new PointF((float)_dragX, (float)_dragY), SpriteSize(_route[Math.Min(_index, _route.Count - 1)]), _route);
        _index = index;
        _x = x;
        if (fall < 1)
        {
            _heightPx = 0;
            Enter(MascotState.Land);
        }
        else
        {
            _heightPx = fall;
            _fallSpeedPx = 0;
            _falling = true;
            Enter(MascotState.Jump);
        }

        Pose = BuildPose();
    }

    // -- Time ----------------------------------------------------------------------------------------------------

    /// <summary>Moves the character on by <paramref name="elapsed"/> and returns what happened.</summary>
    public MascotEvents Update(TimeSpan elapsed)
    {
        if (_route.Count == 0) return MascotEvents.None;

        var dt = Math.Max(0, elapsed.TotalSeconds);
        var events = MascotEvents.None;

        if (_state == MascotState.Drag)
        {
            _stateTime += dt;
        }
        else if (_crossing is not null)
        {
            _stateTime += dt;
            AdvanceCrossing(dt);
        }
        else
        {
            _stateTime += dt;
            events |= Tick(dt);
        }

        Pose = BuildPose();
        return events;
    }

    private MascotEvents Tick(double dt)
    {
        var events = MascotEvents.None;
        switch (_state)
        {
            case MascotState.Walk or MascotState.Run:
                events |= Move(dt);
                break;
            case MascotState.Jump:
                UpdateJump(dt);
                return events;
        }

        _holdRemaining -= dt;
        if (HasFinished()) Advance();
        return events;
    }

    /// <summary>True when the current state has run its course and the next one should start.</summary>
    private bool HasFinished()
    {
        switch (_state)
        {
            case MascotState.Look or MascotState.Land or MascotState.Wave or MascotState.Celebrate or MascotState.Surprised or MascotState.Sad:
                return _stateTime >= _def.DurationSeconds;
            case MascotState.Jump:
                return !_falling && _stateTime >= _config.Movement.JumpSeconds;
            case MascotState.Walk or MascotState.Run:
                return _queue.Count > 0 || _holdRemaining <= 0;
            case MascotState.Idle:
                return _queue.Count > 0 ? _stateTime >= IdlePassThroughSeconds : _holdRemaining <= 0;
            case MascotState.Sit:
                if (_queue.Count > 0) return _stateTime >= IntroSeconds(_def);
                if (_mood == MascotMood.Break) return _stateTime >= _config.Behavior.SleepAfterSitSeconds;
                return _mood is not MascotMood.LongBreak && _holdRemaining <= 0;
            case MascotState.Sleep:
                return _queue.Count > 0 && _stateTime >= SleepWakeSeconds;
            default:
                return false;
        }
    }

    /// <summary>The current state is done: take the next queued state, or decide what to do next.</summary>
    private void Advance()
    {
        if (_queue.Count > 0)
        {
            Enter(_queue.Dequeue());
            return;
        }

        if (_state == MascotState.Idle && _turnResume is { } resume)
        {
            _turnResume = null;
            _direction = -_direction;
            Go(resume, interrupt: true, hold: HoldFor(resume));
            return;
        }

        if (_state == MascotState.Sit && _mood == MascotMood.Break)
        {
            Go(MascotState.Sleep, interrupt: true);
            return;
        }

        // A one-off animation is over: a jump always ends in a landing, anything else in a short standing beat.
        if (_state == MascotState.Jump)
        {
            Go(MascotState.Land, interrupt: true);
            return;
        }

        if (_state is MascotState.Look or MascotState.Land or MascotState.Wave or MascotState.Celebrate or MascotState.Surprised or MascotState.Sad)
        {
            Go(MascotState.Idle, interrupt: true, hold: IdlePassThroughSeconds);
            return;
        }

        Steer(interrupt: true);
    }

    /// <summary>Chooses what the character does now for its mood, going through the allowed chain of states.</summary>
    private void Steer(bool interrupt)
    {
        switch (_mood)
        {
            case MascotMood.Break:
                Go(MascotState.Sit, interrupt);
                return;
            case MascotMood.LongBreak:
                Go(MascotState.Sleep, interrupt);
                return;
        }

        var action = _brain.Next(_mood, RandomActions);
        if (action.State is MascotState.Walk or MascotState.Run && RandomActions > 0 && _brain.Roll() < 0.3 && _state is MascotState.Walk or MascotState.Run)
        {
            // Turn round first: stop for a beat, then walk off the other way.
            _turnResume = action.State;
            Go(MascotState.Idle, interrupt, hold: _config.Movement.TurnPauseSeconds);
            return;
        }

        Go(action.State, interrupt, hold: action.Seconds);
    }

    /// <summary>
    /// Heads for <paramref name="target"/> through the allowed transitions. With <paramref name="interrupt"/> the character
    /// leaves its current state at once; without it, a running one-off animation finishes first.
    /// </summary>
    private void Go(MascotState target, bool interrupt, double hold = -1)
    {
        var path = MascotStateGraph.PathTo(_state, target);
        _queue.Clear();

        if (path.Count == 0)
        {
            // Already there: just refresh the hold time (another stretch of walking, another rest).
            _holdRemaining = hold >= 0 ? hold : HoldFor(target);
            if (_state == target && !IsLooping(target)) Enter(target);
            return;
        }

        foreach (var state in path) _queue.Enqueue(state);
        _pendingHold = hold >= 0 ? hold : HoldFor(target);
        if (interrupt) Enter(_queue.Dequeue());
    }

    private double _pendingHold = 1;

    private void Enter(MascotState state)
    {
        var previous = _state;
        _state = state;
        _def = _config.Animation(state);
        _stateTime = 0;
        _phase = 0;
        _jumpFrame = 0;
        _holdRemaining = _queue.Count == 0 ? _pendingHold : 0;
        if (state == MascotState.Idle && _queue.Count > 0) _holdRemaining = 0;
        if (state == MascotState.Jump && !_falling) _heightPx = 0;
        StateEntered?.Invoke(previous, state);
    }

    /// <summary>Raised each time the character switches state, with the old and the new state (also for a restart of the same state).</summary>
    public event Action<MascotState, MascotState>? StateEntered;

    private double HoldFor(MascotState state) => state switch
    {
        MascotState.Walk => _config.Behavior.WalkSeconds.At(0.5),
        MascotState.Run => _config.Behavior.RunSeconds.At(0.5),
        MascotState.Idle => IdlePassThroughSeconds,
        MascotState.Sit => _config.Behavior.SitSeconds.At(0.5),
        _ => double.PositiveInfinity,
    };

    // -- Movement --------------------------------------------------------------------------------------------------

    private MascotEvents Move(double dt)
    {
        var walkway = _route[_index];
        var dpiScale = walkway.Dpi / 96.0;
        var speedDips = SpeedDips();
        var speed = speedDips * dpiScale;

        // The step animation follows the speed, so the feet do not slide: one stride per cycle of frames.
        var stride = Math.Max(1, _def.StrideDips);
        var fps = Math.Clamp(speedDips / stride * _def.Frames, 1, 30);
        _phase += fps * dt;

        _x += _direction * speed * dt;
        var min = MinX(walkway);
        var max = MaxX(walkway);
        if (_x >= min && _x <= max) return MascotEvents.None;

        var next = _index + _direction;
        if (next < 0 || next >= _route.Count)
        {
            // End of the whole route: stop, look round and head back.
            _x = Math.Clamp(_x, min, max);
            _turnResume = _state;
            Go(MascotState.Idle, interrupt: true, hold: _config.Movement.TurnPauseSeconds);
            return MascotEvents.None;
        }

        var target = _route[next];
        var startX = Math.Clamp(_x, min, max);
        var endX = _direction > 0 ? MinX(target) : MaxX(target);
        _crossing = new Crossing(_index, next, startX, endX, 0);
        _x = startX;
        _queue.Clear();
        Enter(MascotState.Shout);
        return MascotEvents.CrossingStarted;
    }

    private double SpeedDips()
    {
        var movement = _config.Movement;
        var walk = movement.WalkDipsPerSecond * SpeedFactor;
        if (_state == MascotState.Run) return walk * movement.RunMultiplier;
        return _mood == MascotMood.Idle ? walk * movement.IdleWalkFactor : walk;
    }

    private void AdvanceCrossing(double dt)
    {
        var crossing = _crossing!;
        var elapsed = crossing.Elapsed + dt;
        if (elapsed >= _config.Movement.CrossingSeconds)
        {
            _index = crossing.To;
            _x = crossing.EndX;
            _crossing = null;
            Go(MascotState.Land, interrupt: true);
            return;
        }

        _crossing = crossing with { Elapsed = elapsed };
    }

    private void UpdateJump(double dt)
    {
        var dpiScale = _route[_index].Dpi / 96.0;
        if (_falling)
        {
            _jumpFrame = 5;
            _fallSpeedPx += _config.Movement.GravityDipsPerSecond2 * dpiScale * dt;
            _heightPx -= _fallSpeedPx * dt;
            if (_heightPx <= 0)
            {
                _heightPx = 0;
                _falling = false;
                Enter(MascotState.Land);
            }

            return;
        }

        var duration = _config.Movement.JumpSeconds;
        var t = _stateTime / duration;
        if (t < JumpAnticipation)
        {
            _heightPx = 0;
            _jumpFrame = t < JumpAnticipation / 2 ? 0 : 1;
        }
        else if (t < JumpAirEnd)
        {
            var a = (t - JumpAnticipation) / (JumpAirEnd - JumpAnticipation);
            _heightPx = _config.Movement.JumpHeightDips * dpiScale * 4 * a * (1 - a);
            _jumpFrame = a < 0.25 ? 2 : a < 0.5 ? 3 : a < 0.75 ? 4 : 5;
        }
        else
        {
            _heightPx = 0;
            _jumpFrame = 5;
        }

        _holdRemaining = double.PositiveInfinity;
        if (t >= 1) Advance();
    }

    // -- Drag ---------------------------------------------------------------------------------------------------------

    private void MoveDrag(double feetX, double feetY)
    {
        var clamped = MascotDrag.ClampFeet(new PointF((float)feetX, (float)feetY), SpriteSize(_route[Math.Min(_index, _route.Count - 1)]), _route);
        _dragX = clamped.X;
        _dragY = clamped.Y;
    }

    // -- Pose ------------------------------------------------------------------------------------------------------------

    private MascotPose BuildPose()
    {
        var walkway = _route[Math.Min(_index, _route.Count - 1)];
        double x = _x;
        double y = walkway.GroundY - _heightPx;
        var dpi = walkway.Dpi;
        var monitorId = walkway.MonitorId;

        if (_state == MascotState.Drag)
        {
            x = _dragX;
            y = _dragY;
            var held = _route[IndexOfWalkwayAt(_dragX, _dragY)];
            monitorId = held.MonitorId;
            dpi = held.Dpi;
        }
        else if (_crossing is { } crossing)
        {
            var from = _route[crossing.From];
            var to = _route[crossing.To];
            var t = Math.Clamp(crossing.Elapsed / _config.Movement.CrossingSeconds, 0, 1);
            x = crossing.StartX + (crossing.EndX - crossing.StartX) * t;
            y = from.GroundY + (to.GroundY - from.GroundY) * t - 4 * t * (1 - t) * _config.Movement.CrossingArcDips * Math.Max(from.Dpi, to.Dpi) / 96.0;
            monitorId = t < 0.5 ? from.MonitorId : to.MonitorId;
            dpi = t < 0.5 ? from.Dpi : to.Dpi;
        }

        var frame = _state switch
        {
            MascotState.Walk or MascotState.Run => (int)(_phase + 1e-6) % _def.Frames,
            MascotState.Jump => Math.Min(_jumpFrame, _def.Frames - 1),
            _ => _def.FrameAt(_stateTime),
        };

        return new MascotPose(x, y, _direction > 0, _state, _def.Row, frame, monitorId, dpi);
    }

    private int IndexOfWalkwayAt(double x, double y)
    {
        var (index, _, _) = MascotDrag.LandingTarget(new PointF((float)x, (float)y), 0, _route);
        return index;
    }

    // -- Helpers -------------------------------------------------------------------------------------------------------

    private static bool IsFree(MascotState state) => state is MascotState.Idle or MascotState.Walk or MascotState.Run or MascotState.Sit or MascotState.Sleep;

    private static bool IsAirborne(MascotState state) => state is MascotState.Jump or MascotState.Shout;

    private bool IsLooping(MascotState state) => _config.Animation(state).Loop;

    private static double IntroSeconds(AnimationDef def) => def.LoopFrom > 0 ? def.LoopFrom / def.Fps : 0;

    private double SpriteSize(Walkway walkway) => MascotSprites.PixelSize(_config.FrameSize, SizeScale, walkway.Dpi);

    private double HalfWidth(Walkway walkway) => SpriteSize(walkway) / 2.0;

    private double MinX(Walkway walkway) => walkway.Left + Math.Min(HalfWidth(walkway), (walkway.Right - walkway.Left) / 2.0);

    private double MaxX(Walkway walkway) => walkway.Right - Math.Min(HalfWidth(walkway), (walkway.Right - walkway.Left) / 2.0);

    private double ClampX(Walkway walkway, double x) => Math.Clamp(x, MinX(walkway), MaxX(walkway));
}
