using PomoQuote.Mascot;

namespace PomoQuote.Tests;

public class MascotActorTests
{
    private static readonly MascotConfig Config = MascotConfig.Default;
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    // Scale 2 at 96 dpi is a 48 px character, so its feet-centre can move from Left+24 to Right-24.
    private static Walkway Walk(string id, int left, int width, int ground = 1000, int dpi = 96, int top = int.MinValue, int bottom = int.MaxValue) =>
        new(id, left, left + width, ground, dpi, top, bottom);

    private static MascotActor Make(MascotMood mood, int level, params Walkway[] route)
    {
        var actor = new MascotActor(Config, () => 0.5, randomActions: level) { Mood = mood };
        actor.SetRoute(route);
        return actor;
    }

    private static MascotActor MakeSeeded(MascotMood mood, int level, int seed, params Walkway[] route)
    {
        var random = new Random(seed);
        var actor = new MascotActor(Config, random.NextDouble, randomActions: level) { Mood = mood };
        actor.SetRoute(route);
        return actor;
    }

    private static void Run(MascotActor actor, double seconds, double step = 0.05)
    {
        for (var t = 0.0; t < seconds - 1e-9; t += step) actor.Update(TimeSpan.FromSeconds(step));
    }

    private static List<(MascotState From, MascotState To)> Track(MascotActor actor)
    {
        var transitions = new List<(MascotState, MascotState)>();
        actor.StateEntered += (from, to) => transitions.Add((from, to));
        return transitions;
    }

    private static void RunUntil(MascotActor actor, Func<bool> condition, double maxSeconds = 20, double step = 0.05)
    {
        for (var t = 0.0; t < maxSeconds && !condition(); t += step) actor.Update(TimeSpan.FromSeconds(step));
    }

    // -- Walking ---------------------------------------------------------------------------------------------------

    [Fact]
    public void The_character_starts_at_the_left_end_of_the_first_walkway_facing_right()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 1000));

        Assert.Equal(24, actor.Pose!.FeetX);
        Assert.Equal(1000, actor.Pose.FeetY);
        Assert.True(actor.Pose.FacingRight);
        Assert.Equal("A", actor.Pose.MonitorId);
        Assert.Equal(MascotState.Walk, actor.Pose.State);
    }

    [Fact]
    public void Working_walks_at_the_configured_speed()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 1000));

        actor.Update(Second);

        Assert.Equal(24 + Config.Movement.WalkDipsPerSecond, actor.Pose!.FeetX, 3);
    }

    [Fact]
    public void Idle_strolls_slowly()
    {
        var actor = Make(MascotMood.Idle, 0, Walk("A", 0, 1000));

        actor.Update(Second);

        Assert.Equal(24 + Config.Movement.WalkDipsPerSecond * Config.Movement.IdleWalkFactor, actor.Pose!.FeetX, 3);
    }

    [Fact]
    public void The_speed_setting_scales_the_walk()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 2000));
        actor.SpeedFactor = 2.5;

        actor.Update(Second);

        Assert.Equal(24 + Config.Movement.WalkDipsPerSecond * 2.5, actor.Pose!.FeetX, 3);
    }

    [Fact]
    public void A_higher_dpi_walks_more_pixels_per_second()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 3000, dpi: 192));
        var start = actor.Pose!.FeetX;

        actor.Update(Second);

        Assert.Equal(Config.Movement.WalkDipsPerSecond * 2, actor.Pose!.FeetX - start, 3);
    }

    [Fact]
    public void The_size_follows_scale_and_dpi_so_the_walk_limit_does_too()
    {
        var actor = new MascotActor(Config, () => 0.5, sizeScale: 4, randomActions: 0) { Mood = MascotMood.Working };
        actor.SetRoute([Walk("A", 0, 1000, dpi: 192)]);

        // 24 px * round(4 * 2) = 192 px, half is 96.
        Assert.Equal(96, actor.Pose!.FeetX);
    }

    // -- Step animation matches the speed --------------------------------------------------------------------------

    [Fact]
    public void The_walk_cycle_runs_at_the_frame_rate_the_speed_needs()
    {
        // 70 dips/s over a 40 dip stride with 6 frames = 10.5 frames a second.
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 10000));

        Run(actor, 1.0);

        Assert.Equal((int)(10.5) % 6, actor.Pose!.Frame);
    }

    [Fact]
    public void Doubling_the_speed_doubles_the_step_rate()
    {
        var normal = Make(MascotMood.Working, 0, Walk("A", 0, 10000));
        var fast = Make(MascotMood.Working, 0, Walk("A", 0, 10000));
        fast.SpeedFactor = 2;

        Run(normal, 0.4);
        Run(fast, 0.4);

        Assert.Equal((int)(10.5 * 0.4) % 6, normal.Pose!.Frame);
        Assert.Equal((int)(21 * 0.4) % 6, fast.Pose!.Frame);
    }

    [Fact]
    public void One_walk_cycle_covers_exactly_one_stride_so_the_feet_do_not_slide()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 10000));
        var stride = Config.Animation(MascotState.Walk).StrideDips;
        var cycleSeconds = 6 / (Config.Movement.WalkDipsPerSecond / stride * 6);
        var start = actor.Pose!.FeetX;

        Run(actor, cycleSeconds, step: 0.001);

        Assert.Equal(stride, actor.Pose!.FeetX - start, 1);
    }

    [Fact]
    public void A_slow_stroll_steps_slowly()
    {
        var actor = Make(MascotMood.Idle, 0, Walk("A", 0, 10000));

        Run(actor, 1.0);

        // 70 * 0.35 = 24.5 dips/s over a 40 dip stride = 3.675 frames a second.
        Assert.Equal((int)3.675 % 6, actor.Pose!.Frame);
    }

    [Fact]
    public void Running_is_faster_than_walking_with_its_own_stride()
    {
        // A random value of 0.9 makes a working character run.
        var actor = new MascotActor(Config, () => 0.9, randomActions: 2) { Mood = MascotMood.Working };
        actor.SetRoute([Walk("A", 0, 100000)]);
        Assert.Equal(MascotState.Run, actor.State);
        var start = actor.Pose!.FeetX;

        actor.Update(Second);

        Assert.Equal(Config.Movement.WalkDipsPerSecond * Config.Movement.RunMultiplier, actor.Pose!.FeetX - start, 3);
        Assert.Equal(MascotState.Run, actor.Pose.State);
    }

    // -- Turning round -----------------------------------------------------------------------------------------------

    [Fact]
    public void At_the_end_of_the_route_the_character_stops_for_a_beat_then_turns_and_walks_back()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 300)); // 252 px of travel
        var states = new List<MascotState>();
        var facing = new List<bool>();

        for (var i = 0; i < 100; i++)
        {
            actor.Update(TimeSpan.FromSeconds(0.05));
            states.Add(actor.Pose!.State);
            facing.Add(actor.Pose.FacingRight);
        }

        var firstIdle = states.IndexOf(MascotState.Idle);
        Assert.True(firstIdle > 0, "it paused at the end");
        Assert.True(facing[firstIdle], "still facing the old way while it pauses");
        var afterPause = states.FindIndex(firstIdle, s => s == MascotState.Walk);
        Assert.True(afterPause > firstIdle);
        Assert.False(facing[afterPause], "then it walks off the other way, facing left");
        Assert.InRange(actor.Pose!.FeetX, 24, 276);
    }

    [Fact]
    public void The_pause_lasts_the_configured_turn_time()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 300));
        RunUntil(actor, () => actor.State == MascotState.Idle, 10, 0.01);
        var start = 0.0;

        while (actor.State == MascotState.Idle && start < 2)
        {
            actor.Update(TimeSpan.FromSeconds(0.01));
            start += 0.01;
        }

        Assert.InRange(start, Config.Movement.TurnPauseSeconds - 0.03, Config.Movement.TurnPauseSeconds + 0.05);
    }

    // -- Breaks and sleep ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_short_break_means_sitting_and_after_a_while_sleeping_in_place()
    {
        var actor = Make(MascotMood.Break, 0, Walk("A", 0, 1000));
        var start = actor.Pose!.FeetX;

        Run(actor, 1.0);
        Assert.Equal(MascotState.Sit, actor.State);

        Run(actor, Config.Behavior.SleepAfterSitSeconds + 1);
        Assert.Equal(MascotState.Sleep, actor.State);
        Assert.Equal(start, actor.Pose!.FeetX);
    }

    [Fact]
    public void A_long_break_goes_to_sleep_through_idle_and_sit()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 100000));
        Run(actor, 1);
        var transitions = Track(actor);

        actor.Mood = MascotMood.LongBreak;
        Run(actor, 3);

        Assert.Equal([(MascotState.Walk, MascotState.Idle), (MascotState.Idle, MascotState.Sit), (MascotState.Sit, MascotState.Sleep)], transitions);
        Assert.Equal(MascotState.Sleep, actor.State);
    }

    [Fact]
    public void Waking_up_goes_through_sit_and_idle_before_walking()
    {
        var actor = Make(MascotMood.LongBreak, 0, Walk("A", 0, 100000));
        Run(actor, 3);
        Assert.Equal(MascotState.Sleep, actor.State);
        var transitions = Track(actor);

        actor.Mood = MascotMood.Working;
        Run(actor, 3);

        Assert.Equal([(MascotState.Sleep, MascotState.Sit), (MascotState.Sit, MascotState.Idle), (MascotState.Idle, MascotState.Walk)], transitions);
    }

    [Fact]
    public void Changing_the_mood_does_not_cut_a_one_off_animation_short()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 100000));
        actor.React(); // random 0.5 = wave, after a standing beat
        RunUntil(actor, () => actor.State == MascotState.Wave, 1);
        Assert.Equal(MascotState.Wave, actor.State);
        Run(actor, 0.5);

        actor.Mood = MascotMood.LongBreak;
        Assert.Equal(MascotState.Wave, actor.State);
        Run(actor, 1.2);

        Assert.NotEqual(MascotState.Wave, actor.State);
        Run(actor, 3);
        Assert.Equal(MascotState.Sleep, actor.State);
    }

    // -- Reactions, celebration, sadness ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(0.5, MascotState.Wave)]
    [InlineData(0.8, MascotState.Surprised)]
    public void A_click_makes_it_wave_or_look_surprised_then_it_carries_on(double random, MascotState expected)
    {
        var actor = new MascotActor(Config, () => random, randomActions: 0) { Mood = MascotMood.Working };
        actor.SetRoute([Walk("A", 0, 100000)]);
        Run(actor, 1);
        actor.React();

        // A wave starts after a standing beat (no popping from a walk into a wave); a startle may come straight away.
        Assert.True(actor.State == MascotState.Idle || actor.State == expected);
        RunUntil(actor, () => actor.State == expected, 1);
        Assert.Equal(expected, actor.State);
        var x = actor.Pose!.FeetX;
        Run(actor, 0.3);
        Assert.Equal(x, actor.Pose!.FeetX); // it stands still for the reaction
        RunUntil(actor, () => actor.State == MascotState.Walk, 6);
        Assert.Equal(MascotState.Walk, actor.State);
    }

    [Fact]
    public void A_wave_plays_its_frames_twice()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 100000));
        actor.React();
        RunUntil(actor, () => actor.State == MascotState.Wave, 1);
        var frames = new List<int>();

        while (actor.State == MascotState.Wave)
        {
            actor.Update(TimeSpan.FromSeconds(0.02));
            if (actor.State == MascotState.Wave) frames.Add(actor.Pose!.Frame);
        }

        var distinctRuns = frames.Zip(frames.Skip(1), (a, b) => b < a).Count(wrapped => wrapped);
        Assert.Equal(1, distinctRuns); // frames 0..5 twice means one wrap-round
        Assert.Equal(Enumerable.Range(0, 6), frames.Distinct());
    }

    [Fact]
    public void Celebrating_goes_celebrate_jump_land_idle_and_rises_off_the_ground_while_jumping()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 100000));
        Run(actor, 1);
        var x = actor.Pose!.FeetX;
        var transitions = Track(actor);

        actor.Celebrate();
        Assert.True(actor.IsCelebrating);
        var highest = 0.0;
        for (var i = 0; i < 100; i++)
        {
            actor.Update(TimeSpan.FromSeconds(0.05));
            highest = Math.Max(highest, 1000 - actor.Pose!.FeetY);
        }

        var order = transitions.Select(t => t.To).ToList();
        Assert.Contains(MascotState.Celebrate, order);
        Assert.True(order.IndexOf(MascotState.Celebrate) < order.IndexOf(MascotState.Jump));
        Assert.True(order.IndexOf(MascotState.Jump) < order.IndexOf(MascotState.Land));
        Assert.InRange(highest, 20, 40); // jump height 30 dips at 96 dpi
        Assert.Equal(1000, actor.Pose!.FeetY, 3);
        Assert.False(actor.IsCelebrating);
        Assert.True(actor.Pose.FeetX >= x);
    }

    [Fact]
    public void Celebrating_stays_in_place()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 100000));
        Run(actor, 1);
        actor.Celebrate();
        RunUntil(actor, () => actor.State == MascotState.Celebrate, 1);
        var x = actor.Pose!.FeetX;

        Run(actor, 1.5);

        Assert.Equal(x, actor.Pose!.FeetX);
    }

    [Fact]
    public void A_skipped_phase_makes_it_sad_for_a_moment()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 100000));
        Run(actor, 1);

        actor.Sadden();
        RunUntil(actor, () => actor.State == MascotState.Sad, 2);
        Assert.Equal(MascotState.Sad, actor.State);
        var x = actor.Pose!.FeetX;
        Run(actor, 1);
        Assert.Equal(x, actor.Pose!.FeetX);

        RunUntil(actor, () => actor.State == MascotState.Walk, 10);
        Assert.Equal(MascotState.Walk, actor.State);
    }

    // -- Crossing monitors -------------------------------------------------------------------------------------------------

    [Fact]
    public void Reaching_the_end_of_a_monitor_with_another_beyond_starts_a_jump_with_a_shout_event_once()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 300), Walk("B", 300, 300));
        var crossings = 0;

        for (var i = 0; i < 40; i++)
        {
            if (actor.Update(TimeSpan.FromSeconds(0.1)).HasFlag(MascotEvents.CrossingStarted)) crossings++;
        }

        Assert.Equal(1, crossings);
    }

    [Fact]
    public void While_crossing_it_arcs_through_the_air_shouting_and_lands_on_the_next_monitor()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 300), Walk("B", 300, 300));
        MascotEvents events;
        do { events = actor.Update(TimeSpan.FromSeconds(0.05)); } while (!events.HasFlag(MascotEvents.CrossingStarted));
        Assert.True(actor.IsCrossing);

        var lowest = double.MaxValue;
        var xs = new List<double>();
        while (actor.IsCrossing)
        {
            actor.Update(TimeSpan.FromSeconds(0.05));
            lowest = Math.Min(lowest, actor.Pose!.FeetY);
            xs.Add(actor.Pose.FeetX);
            if (actor.IsCrossing) Assert.Equal(MascotState.Shout, actor.Pose.State);
        }

        Assert.True(lowest < 1000 - 20, "it rose off the ground during the jump");
        Assert.True(xs.Zip(xs.Skip(1), (a, b) => b >= a).All(ok => ok), "it moved steadily towards the next monitor");
        Assert.Equal("B", actor.Pose!.MonitorId);
        Assert.Equal(324, actor.Pose.FeetX, 3);
        Assert.Equal(1000, actor.Pose.FeetY, 3);

        RunUntil(actor, () => actor.State == MascotState.Walk, 3);
        Assert.Equal(MascotState.Walk, actor.Pose!.State); // lands, then walks on across B
        Assert.Equal("B", actor.Pose.MonitorId);
    }

    [Fact]
    public void The_crossing_goes_walk_shout_land_idle_walk()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 300), Walk("B", 300, 300));
        var transitions = Track(actor);

        Run(actor, 8);

        var order = transitions.Select(t => t.To).Take(5).ToList();
        Assert.Equal([MascotState.Shout, MascotState.Land, MascotState.Idle, MascotState.Walk], order.Take(4));
    }

    [Fact]
    public void Walking_back_across_the_junction_lands_at_the_right_end_of_the_previous_monitor()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 300), Walk("B", 300, 300));
        var crossings = 0;

        for (var i = 0; i < 800 && crossings < 2; i++)
        {
            if (actor.Update(TimeSpan.FromSeconds(0.05)).HasFlag(MascotEvents.CrossingStarted)) crossings++;
        }

        Assert.Equal(2, crossings);
        RunUntil(actor, () => !actor.IsCrossing, 2);
        Assert.Equal("A", actor.Pose!.MonitorId);
        Assert.False(actor.Pose.FacingRight);
        Assert.InRange(actor.Pose.FeetX, 250, 276);
    }

    [Fact]
    public void Monitors_at_different_heights_are_joined_by_a_jump_that_ends_at_the_new_ground()
    {
        var actor = Make(MascotMood.Working, 0, Walk("Top", 0, 300, ground: 500), Walk("Bottom", 0, 300, ground: 1500));
        MascotEvents events;
        do { events = actor.Update(TimeSpan.FromSeconds(0.05)); } while (!events.HasFlag(MascotEvents.CrossingStarted));

        RunUntil(actor, () => !actor.IsCrossing, 3);

        Assert.Equal("Bottom", actor.Pose!.MonitorId);
        Assert.Equal(1500, actor.Pose.FeetY, 3);
    }

    [Fact]
    public void A_long_stall_does_not_carry_the_character_off_its_monitor()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 300), Walk("B", 300, 300));

        actor.Update(TimeSpan.FromSeconds(60));

        Assert.True(actor.IsCrossing);
        Assert.InRange(actor.Pose!.FeetX, 24, 276);
    }

    // -- Drag and drop ------------------------------------------------------------------------------------------------------------

    private static readonly Walkway[] TwoMonitors =
    [
        new("L", 0, 1920, 1032, 96, 0, 1080),
        new("R", 1920, 3840, 1032, 96, 0, 1080),
    ];

    [Fact]
    public void Picking_it_up_shows_the_drag_pose_at_the_pointer_and_stops_everything_else()
    {
        var actor = Make(MascotMood.Working, 0, TwoMonitors);
        Run(actor, 1);

        actor.BeginDrag(600, 400);
        Run(actor, 1);

        Assert.True(actor.IsDragging);
        Assert.Equal(MascotState.Drag, actor.Pose!.State);
        Assert.Equal((600, 400), (actor.Pose.FeetX, actor.Pose.FeetY));
        actor.DragTo(2500, 300);
        Assert.Equal((2500, 300), (actor.Pose.FeetX, actor.Pose.FeetY));
        Assert.Equal("R", actor.Pose.MonitorId);
    }

    [Fact]
    public void While_held_it_can_never_leave_the_visible_screens()
    {
        var actor = Make(MascotMood.Working, 0, TwoMonitors);
        actor.BeginDrag(600, 400);

        foreach (var (x, y) in new[] { (-500.0, -500.0), (9000.0, 9000.0), (-1.0, 5000.0), (3000.0, -20.0) })
        {
            actor.DragTo(x, y);

            var pose = actor.Pose!;
            Assert.InRange(pose.FeetX, 24, 3816);
            Assert.InRange(pose.FeetY, 48, 1080);
        }
    }

    [Fact]
    public void Letting_go_in_the_air_makes_it_fall_to_the_taskbar_strip_and_land()
    {
        var actor = Make(MascotMood.Working, 0, TwoMonitors);
        actor.BeginDrag(2500, 300);
        var transitions = Track(actor);

        actor.EndDrag();
        Assert.Equal(MascotState.Jump, actor.State);
        var previousY = actor.Pose!.FeetY;
        var rising = false;
        var seconds = 0.0;
        while (actor.State == MascotState.Jump && seconds < 3)
        {
            actor.Update(TimeSpan.FromSeconds(0.02));
            seconds += 0.02;
            if (actor.State == MascotState.Jump)
            {
                if (actor.Pose!.FeetY < previousY - 0.001) rising = true;
                previousY = actor.Pose.FeetY;
                Assert.Equal(5, actor.Pose.Frame); // the "falling" frame
            }
        }

        Assert.False(rising);
        Assert.Equal(MascotState.Land, actor.State);
        Assert.Equal(1032, actor.Pose!.FeetY, 3);
        Assert.Equal("R", actor.Pose.MonitorId);
        Assert.Equal(2500, actor.Pose.FeetX, 3);
        // 732 px under gravity 2600: sqrt(2 * 732 / 2600) = 0.75 s.
        Assert.InRange(seconds, 0.6, 0.9);
        Assert.Equal([(MascotState.Drag, MascotState.Jump), (MascotState.Jump, MascotState.Land)], transitions);

        RunUntil(actor, () => actor.State == MascotState.Walk, 5);
        Assert.Equal(MascotState.Walk, actor.State); // then it walks on from where it landed
    }

    [Fact]
    public void Letting_go_on_the_ground_lands_at_once_without_falling()
    {
        var actor = Make(MascotMood.Working, 0, TwoMonitors);
        actor.BeginDrag(700, 1032);

        actor.EndDrag();

        Assert.Equal(MascotState.Land, actor.State);
        Assert.Equal(1032, actor.Pose!.FeetY, 3);
    }

    [Fact]
    public void Dropping_it_below_the_taskbar_edge_puts_it_on_the_edge()
    {
        var actor = Make(MascotMood.Working, 0, TwoMonitors);
        actor.BeginDrag(700, 1075);

        actor.EndDrag();
        Run(actor, 0.1);

        Assert.Equal(1032, actor.Pose!.FeetY, 3);
    }

    [Fact]
    public void Dragging_while_crossing_cancels_the_jump_and_works()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 300, top: 0, bottom: 1100), Walk("B", 300, 300, top: 0, bottom: 1100));
        MascotEvents events;
        do { events = actor.Update(TimeSpan.FromSeconds(0.05)); } while (!events.HasFlag(MascotEvents.CrossingStarted));

        actor.BeginDrag(450, 500);

        Assert.False(actor.IsCrossing);
        Assert.Equal(MascotState.Drag, actor.State);
        actor.EndDrag();
        RunUntil(actor, () => actor.State == MascotState.Land, 3);
        Assert.Equal(1000, actor.Pose!.FeetY, 3);
    }

    [Fact]
    public void A_click_is_ignored_while_it_is_being_held_or_falling()
    {
        var actor = Make(MascotMood.Working, 0, TwoMonitors);
        actor.BeginDrag(700, 300);

        actor.React();
        Assert.Equal(MascotState.Drag, actor.State);

        actor.EndDrag();
        actor.React();
        Assert.Equal(MascotState.Jump, actor.State);
    }

    // -- Autonomy ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void With_random_actions_off_an_idle_character_only_ever_walks()
    {
        var actor = MakeSeeded(MascotMood.Idle, 0, 1, Walk("A", 0, 100000));
        var states = new HashSet<MascotState>();

        for (var i = 0; i < 2400; i++)
        {
            actor.Update(TimeSpan.FromSeconds(0.05));
            states.Add(actor.State);
        }

        Assert.Equal([MascotState.Walk], states);
    }

    [Fact]
    public void With_random_actions_on_an_idle_character_shows_its_personality()
    {
        var actor = MakeSeeded(MascotMood.Idle, 2, 5, Walk("A", 0, 5000));
        var seen = new HashSet<MascotState>();

        for (var i = 0; i < 12000; i++) // ten minutes
        {
            actor.Update(TimeSpan.FromSeconds(0.05));
            seen.Add(actor.State);
        }

        foreach (var state in new[] { MascotState.Walk, MascotState.Idle, MascotState.Look, MascotState.Sit, MascotState.Wave, MascotState.Jump, MascotState.Land })
        {
            Assert.Contains(state, seen);
        }
    }

    [Fact]
    public void Few_random_actions_means_fewer_playful_moments_than_normal()
    {
        int Count(int level)
        {
            var actor = MakeSeeded(MascotMood.Idle, level, 9, Walk("A", 0, 100000));
            var entered = 0;
            actor.StateEntered += (_, to) => { if (to is MascotState.Look or MascotState.Wave or MascotState.Jump or MascotState.Surprised) entered++; };
            for (var i = 0; i < 24000; i++) actor.Update(TimeSpan.FromSeconds(0.05));
            return entered;
        }

        Assert.True(Count(1) < Count(2) * 0.7);
    }

    [Fact]
    public void A_working_character_never_rests_or_plays()
    {
        var actor = MakeSeeded(MascotMood.Working, 2, 4, Walk("A", 0, 1000000));
        var seen = new HashSet<MascotState>();

        for (var i = 0; i < 6000; i++)
        {
            actor.Update(TimeSpan.FromSeconds(0.05));
            seen.Add(actor.State);
        }

        Assert.Subset(new HashSet<MascotState> { MascotState.Walk, MascotState.Run, MascotState.Idle }, seen); // Idle only as a turn-round beat
        Assert.Contains(MascotState.Run, seen);
    }

    [Fact]
    public void The_same_seed_gives_the_same_life()
    {
        List<MascotState> Life()
        {
            var actor = MakeSeeded(MascotMood.Idle, 2, 21, Walk("A", 0, 5000));
            var states = new List<MascotState>();
            for (var i = 0; i < 4000; i++) { actor.Update(TimeSpan.FromSeconds(0.05)); states.Add(actor.State); }
            return states;
        }

        Assert.Equal(Life(), Life());
    }

    // -- Natural transitions: a long chaotic life never pops between states ----------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void No_matter_what_happens_every_state_change_follows_the_transition_graph(int seed)
    {
        var random = new Random(seed);
        var actor = new MascotActor(Config, random.NextDouble, randomActions: 2);
        actor.SetRoute(TwoMonitors);
        var transitions = Track(actor);

        for (var i = 0; i < 20000; i++)
        {
            switch (random.Next(60))
            {
                case 0: actor.Mood = (MascotMood)random.Next(4); break;
                case 1: actor.React(); break;
                case 2: actor.Celebrate(); break;
                case 3: actor.Sadden(); break;
                case 4: actor.BeginDrag(random.Next(-200, 4000), random.Next(-100, 1200)); break;
                case 5 when actor.IsDragging: actor.DragTo(random.Next(-200, 4000), random.Next(-100, 1200)); break;
                case 6 when actor.IsDragging: actor.EndDrag(); break;
                case 7: actor.SetRoute(random.Next(3) == 0 ? [TwoMonitors[random.Next(2)]] : TwoMonitors); break;
            }

            actor.Update(TimeSpan.FromSeconds(0.05));
            var pose = actor.Pose!;
            var animation = Config.Animation(pose.State);
            Assert.Equal(animation.Row, pose.Row);
            Assert.InRange(pose.Frame, 0, animation.Frames - 1);
            Assert.InRange(pose.FeetX, -1, 3841);
            Assert.InRange(pose.FeetY, 0, 1100);
        }

        Assert.NotEmpty(transitions);
        foreach (var (from, to) in transitions)
        {
            Assert.True(from == to || MascotStateGraph.CanTransition(from, to), $"{from} -> {to} is not an allowed transition");
        }
    }

    // -- Layout changes -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Unplugging_the_current_monitor_puts_the_character_at_the_start_of_the_new_route()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 300), Walk("B", 300, 300));
        Run(actor, 6);

        actor.SetRoute([Walk("C", 1000, 500)]);

        Assert.False(actor.IsCrossing);
        Assert.Equal("C", actor.Pose!.MonitorId);
        Assert.Equal(1024, actor.Pose.FeetX);
    }

    [Fact]
    public void Plugging_in_a_monitor_keeps_the_character_where_it_is_and_extends_the_route()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 1000));
        Run(actor, 2);
        var x = actor.Pose!.FeetX;

        actor.SetRoute([Walk("A", 0, 1000), Walk("B", 1000, 1000)]);

        Assert.Equal("A", actor.Pose!.MonitorId);
        Assert.Equal(x, actor.Pose.FeetX, 3);
    }

    [Fact]
    public void A_shrunken_walkway_pulls_the_character_back_inside()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 2000));
        Run(actor, 10);

        actor.SetRoute([Walk("A", 0, 500)]);

        Assert.InRange(actor.Pose!.FeetX, 24, 476);
    }

    [Fact]
    public void Unplugging_the_monitor_under_a_held_or_falling_character_brings_it_back_on_screen()
    {
        var actor = Make(MascotMood.Working, 0, TwoMonitors);
        actor.BeginDrag(2500, 300);

        actor.SetRoute([TwoMonitors[0]]);

        Assert.InRange(actor.Pose!.FeetX, 24, 1896);
        actor.EndDrag();
        RunUntil(actor, () => actor.State == MascotState.Land, 5);
        Assert.Equal("L", actor.Pose!.MonitorId);
        Assert.Equal(1032, actor.Pose.FeetY, 3);
    }

    [Fact]
    public void An_empty_route_means_no_pose_and_updates_do_nothing()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 300));

        actor.SetRoute([]);
        var events = actor.Update(Second);

        Assert.Null(actor.Pose);
        Assert.Equal(MascotEvents.None, events);
        Assert.False(actor.HasRoute);
    }

    [Fact]
    public void A_walkway_narrower_than_the_character_does_not_break_it()
    {
        var actor = Make(MascotMood.Working, 0, Walk("Tiny", 100, 20));

        Run(actor, 3);

        Assert.InRange(actor.Pose!.FeetX, 100, 120);
    }

    [Fact]
    public void Zero_elapsed_time_changes_nothing()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 1000));
        var before = actor.Pose;

        actor.Update(TimeSpan.Zero);

        Assert.Equal(before!.FeetX, actor.Pose!.FeetX);
    }

    [Fact]
    public void A_reloaded_config_changes_the_behaviour_weights_too()
    {
        var actor = new MascotActor(Config, () => 0.5, randomActions: 2) { Mood = MascotMood.Idle };
        actor.SetRoute([Walk("A", 0, 100000)]);
        var warnings = new List<string>();

        actor.SetConfig(MascotConfig.Parse("""{ "behavior": { "moods": { "idle": { "sit": 100 } }, "idleSeconds": { "min": 0, "max": 0 } } }""", Config, warnings));
        actor.Mood = MascotMood.Working;
        actor.Mood = MascotMood.Idle; // makes the character choose again with the new weights
        Run(actor, 3);

        Assert.Empty(warnings);
        Assert.Contains(actor.State, new[] { MascotState.Sit, MascotState.Idle });
    }

    [Fact]
    public void A_reloaded_config_takes_effect_at_once()
    {
        var actor = Make(MascotMood.Working, 0, Walk("A", 0, 100000));
        actor.Update(Second);
        var slow = actor.Pose!.FeetX - 24;

        var warnings = new List<string>();
        actor.SetConfig(MascotConfig.Parse("""{ "movement": { "walkDipsPerSecond": 140 } }""", Config, warnings));
        var before = actor.Pose!.FeetX;
        actor.Update(Second);

        Assert.Equal(slow * 2, actor.Pose!.FeetX - before, 3);
    }
}
