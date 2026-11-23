using HelloWorldBot.Mascot;

namespace HelloWorldBot.Tests;

public class MascotMoverTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    // Scale 3 at 96 dpi is a 48 px robot, so its feet-centre can move from Left+24 to Right-24.
    private static Walkway Walk(string id, int left, int width, int ground = 1000, int dpi = 96) => new(id, left, left + width, ground, dpi);

    private static MascotMover Mover(MascotMood mood, params Walkway[] route)
    {
        var mover = new MascotMover { Mood = mood };
        mover.SetRoute(route);
        return mover;
    }

    private static void Run(MascotMover mover, double seconds, double step = 0.05)
    {
        for (var t = 0.0; t < seconds; t += step) mover.Update(TimeSpan.FromSeconds(step));
    }

    [Fact]
    public void The_robot_starts_at_the_left_end_of_the_first_walkway_facing_right()
    {
        var mover = Mover(MascotMood.Working, Walk("A", 0, 1000));

        Assert.Equal(24, mover.Pose!.FeetX);
        Assert.Equal(1000, mover.Pose.FeetY);
        Assert.True(mover.Pose.FacingRight);
        Assert.Equal("A", mover.Pose.MonitorId);
    }

    [Fact]
    public void Working_walks_at_the_base_speed()
    {
        var mover = Mover(MascotMood.Working, Walk("A", 0, 1000));

        mover.Update(Second);

        Assert.Equal(24 + MascotMover.BaseSpeedDipsPerSecond, mover.Pose!.FeetX, 3);
        Assert.Equal(MascotAnimation.Walk, mover.Pose.Animation);
    }

    [Fact]
    public void Idle_strolls_slowly()
    {
        var mover = Mover(MascotMood.Idle, Walk("A", 0, 1000));

        mover.Update(Second);

        Assert.Equal(24 + MascotMover.BaseSpeedDipsPerSecond * MascotMover.IdleSpeedFactor, mover.Pose!.FeetX, 3);
    }

    [Fact]
    public void The_speed_setting_scales_the_walk()
    {
        var mover = Mover(MascotMood.Working, Walk("A", 0, 2000));
        mover.SpeedFactor = 2.5;

        mover.Update(Second);

        Assert.Equal(24 + MascotMover.BaseSpeedDipsPerSecond * 2.5, mover.Pose!.FeetX, 3);
    }

    [Fact]
    public void A_higher_dpi_walks_more_pixels_per_second()
    {
        var mover = Mover(MascotMood.Working, Walk("A", 0, 3000, dpi: 192));
        var start = mover.Pose!.FeetX;

        mover.Update(Second);

        Assert.Equal(MascotMover.BaseSpeedDipsPerSecond * 2, mover.Pose!.FeetX - start, 3);
    }

    [Fact]
    public void The_robot_size_follows_scale_and_dpi_so_its_walk_limit_does_too()
    {
        var mover = new MascotMover(sizeScale: 4) { Mood = MascotMood.Working };
        mover.SetRoute([Walk("A", 0, 1000, dpi: 192)]);

        // 16 px * round(4 * 2) = 128 px, half is 64.
        Assert.Equal(64, mover.Pose!.FeetX);
    }

    [Fact]
    public void Break_makes_the_robot_sleep_in_place()
    {
        var mover = Mover(MascotMood.Break, Walk("A", 0, 1000));
        var start = mover.Pose!.FeetX;

        Run(mover, 5);

        Assert.Equal(start, mover.Pose!.FeetX);
        Assert.Equal(MascotAnimation.Sleep, mover.Pose.Animation);
    }

    [Fact]
    public void Changing_the_mood_changes_the_animation_and_restarts_it()
    {
        var mover = Mover(MascotMood.Working, Walk("A", 0, 3000));
        Run(mover, 0.35);
        Assert.NotEqual(0, mover.Pose!.Frame);

        mover.Mood = MascotMood.Break;
        mover.Update(TimeSpan.Zero);

        Assert.Equal(MascotAnimation.Sleep, mover.Pose!.Animation);
        Assert.Equal(0, mover.Pose.Frame);
    }

    [Fact]
    public void On_a_single_monitor_the_robot_turns_round_at_each_end_without_jumping()
    {
        var mover = Mover(MascotMood.Working, Walk("A", 0, 300)); // 252 px of travel: 3.15 s each way
        var events = MascotEvents.None;

        for (var i = 0; i < 100; i++) events |= mover.Update(TimeSpan.FromSeconds(0.1));

        Assert.Equal(MascotEvents.None, events);
        Assert.False(mover.IsCrossing);
        Assert.InRange(mover.Pose!.FeetX, 24, 276);
    }

    [Fact]
    public void The_robot_turns_to_face_left_after_the_end_of_the_route()
    {
        var mover = Mover(MascotMood.Working, Walk("A", 0, 300));

        Run(mover, 3.5);

        Assert.False(mover.Pose!.FacingRight);
    }

    [Fact]
    public void Reaching_the_end_of_a_monitor_with_another_beyond_starts_a_jump_and_raises_the_event_once()
    {
        var mover = Mover(MascotMood.Working, Walk("A", 0, 300), Walk("B", 300, 300));
        var crossings = 0;

        for (var i = 0; i < 40; i++)
        {
            if (mover.Update(TimeSpan.FromSeconds(0.1)).HasFlag(MascotEvents.CrossingStarted)) crossings++;
        }

        Assert.Equal(1, crossings);
    }

    [Fact]
    public void While_crossing_the_robot_arcs_through_the_air_shouting_and_lands_on_the_next_monitor()
    {
        var mover = Mover(MascotMood.Working, Walk("A", 0, 300), Walk("B", 300, 300));
        MascotEvents events;
        do { events = mover.Update(TimeSpan.FromSeconds(0.05)); } while (!events.HasFlag(MascotEvents.CrossingStarted));

        Assert.True(mover.IsCrossing);
        var lowest = double.MaxValue;
        var xs = new List<double>();
        while (mover.IsCrossing)
        {
            mover.Update(TimeSpan.FromSeconds(0.05));
            lowest = Math.Min(lowest, mover.Pose!.FeetY);
            xs.Add(mover.Pose.FeetX);
            if (mover.IsCrossing) Assert.Equal(MascotAnimation.Shout, mover.Pose.Animation);
        }

        Assert.True(lowest < 1000 - 20, "the robot rose off the ground during the jump");
        Assert.True(xs.Zip(xs.Skip(1), (a, b) => b >= a).All(ok => ok), "it moved steadily towards the next monitor");
        Assert.Equal("B", mover.Pose!.MonitorId);
        Assert.Equal(324, mover.Pose.FeetX, 3); // start of B plus half the robot
        Assert.Equal(1000, mover.Pose.FeetY, 3);

        mover.Update(TimeSpan.FromSeconds(0.05));
        Assert.Equal(MascotAnimation.Walk, mover.Pose!.Animation); // walking again on B
    }

    [Fact]
    public void Walking_back_across_the_junction_lands_at_the_right_end_of_the_previous_monitor()
    {
        var mover = Mover(MascotMood.Working, Walk("A", 0, 300), Walk("B", 300, 300));
        var crossings = 0;

        for (var i = 0; i < 400 && crossings < 2; i++)
        {
            if (mover.Update(TimeSpan.FromSeconds(0.05)).HasFlag(MascotEvents.CrossingStarted)) crossings++;
        }

        Assert.Equal(2, crossings);
        Run(mover, MascotMover.CrossingDuration.TotalSeconds + 0.1);
        Assert.Equal("A", mover.Pose!.MonitorId);
        Assert.False(mover.Pose.FacingRight);
        Assert.InRange(mover.Pose.FeetX, 260, 276); // just in from the right end of A, walking left
    }

    [Fact]
    public void Monitors_at_different_heights_are_joined_by_a_jump_that_ends_at_the_new_ground()
    {
        var mover = Mover(MascotMood.Working, Walk("Top", 0, 300, ground: 500), Walk("Bottom", 0, 300, ground: 1500));
        MascotEvents events;
        do { events = mover.Update(TimeSpan.FromSeconds(0.05)); } while (!events.HasFlag(MascotEvents.CrossingStarted));

        while (mover.IsCrossing) mover.Update(TimeSpan.FromSeconds(0.05));

        Assert.Equal("Bottom", mover.Pose!.MonitorId);
        Assert.Equal(1500, mover.Pose.FeetY, 3);
    }

    [Fact]
    public void A_long_stall_does_not_carry_the_robot_off_its_monitor()
    {
        var mover = Mover(MascotMood.Working, Walk("A", 0, 300), Walk("B", 300, 300));

        mover.Update(TimeSpan.FromSeconds(60));

        Assert.True(mover.IsCrossing);
        Assert.InRange(mover.Pose!.FeetX, 24, 276);
    }

    [Fact]
    public void Celebrating_hops_and_shouts_in_place_then_goes_back_to_walking()
    {
        var mover = Mover(MascotMood.Working, Walk("A", 0, 3000));
        Run(mover, 1);
        var x = mover.Pose!.FeetX;

        mover.Celebrate();
        var highest = 0.0;
        for (var t = 0.0; t < 2.3; t += 0.05)
        {
            mover.Update(TimeSpan.FromSeconds(0.05));
            highest = Math.Max(highest, 1000 - mover.Pose!.FeetY);
            Assert.Equal(MascotAnimation.Shout, mover.Pose.Animation);
            Assert.Equal(x, mover.Pose.FeetX, 3);
        }

        Assert.True(highest > 10, "it jumped");
        Run(mover, 0.3);
        Assert.False(mover.IsCelebrating);
        Assert.Equal(MascotAnimation.Walk, mover.Pose!.Animation);
        Assert.True(mover.Pose.FeetX > x);
        Assert.Equal(1000, mover.Pose.FeetY, 3);
    }

    [Fact]
    public void Celebrating_is_ignored_while_jumping_between_monitors()
    {
        var mover = Mover(MascotMood.Working, Walk("A", 0, 300), Walk("B", 300, 300));
        MascotEvents events;
        do { events = mover.Update(TimeSpan.FromSeconds(0.05)); } while (!events.HasFlag(MascotEvents.CrossingStarted));

        mover.Celebrate();

        Assert.False(mover.IsCelebrating);
    }

    [Fact]
    public void Unplugging_the_current_monitor_puts_the_robot_at_the_start_of_the_new_route()
    {
        var mover = Mover(MascotMood.Working, Walk("A", 0, 300), Walk("B", 300, 300));
        Run(mover, 6); // somewhere on B or in the air

        mover.SetRoute([Walk("C", 1000, 500)]);

        Assert.False(mover.IsCrossing);
        Assert.Equal("C", mover.Pose!.MonitorId);
        Assert.Equal(1024, mover.Pose.FeetX);
    }

    [Fact]
    public void Plugging_in_a_monitor_keeps_the_robot_where_it_is_and_extends_the_route()
    {
        var mover = Mover(MascotMood.Working, Walk("A", 0, 1000));
        Run(mover, 2);
        var x = mover.Pose!.FeetX;

        mover.SetRoute([Walk("A", 0, 1000), Walk("B", 1000, 1000)]);

        Assert.Equal("A", mover.Pose!.MonitorId);
        Assert.Equal(x, mover.Pose.FeetX, 3);
    }

    [Fact]
    public void A_shrunken_walkway_pulls_the_robot_back_inside()
    {
        var mover = Mover(MascotMood.Working, Walk("A", 0, 2000));
        Run(mover, 10); // x is about 824

        mover.SetRoute([Walk("A", 0, 500)]);

        Assert.InRange(mover.Pose!.FeetX, 24, 476);
    }

    [Fact]
    public void An_empty_route_means_no_pose_and_updates_do_nothing()
    {
        var mover = Mover(MascotMood.Working, Walk("A", 0, 300));

        mover.SetRoute([]);
        var events = mover.Update(Second);

        Assert.Null(mover.Pose);
        Assert.Equal(MascotEvents.None, events);
        Assert.False(mover.HasRoute);
    }

    [Fact]
    public void A_walkway_narrower_than_the_robot_does_not_break_it()
    {
        var mover = Mover(MascotMood.Working, Walk("Tiny", 100, 20));

        Run(mover, 3);

        Assert.InRange(mover.Pose!.FeetX, 100, 120);
    }

    [Fact]
    public void Zero_elapsed_time_changes_nothing()
    {
        var mover = Mover(MascotMood.Working, Walk("A", 0, 1000));
        var before = mover.Pose;

        mover.Update(TimeSpan.Zero);

        Assert.Equal(before!.FeetX, mover.Pose!.FeetX);
    }

    [Fact]
    public void The_walk_cycles_through_four_frames_at_about_ten_per_second()
    {
        var mover = Mover(MascotMood.Working, Walk("A", 0, 10000));
        var frames = new List<int>();

        for (var i = 0; i < 8; i++)
        {
            mover.Update(TimeSpan.FromSeconds(0.1));
            frames.Add(mover.Pose!.Frame);
        }

        Assert.Equal([1, 2, 3, 0, 1, 2, 3, 0], frames);
    }
}

public class MascotSpritesTests
{
    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.05, 0)]
    [InlineData(0.1, 1)]
    [InlineData(0.25, 2)]
    [InlineData(0.39, 3)]
    [InlineData(0.4, 0)]
    public void Walk_frames_advance_every_tenth_of_a_second_and_wrap(double seconds, int expected)
    {
        Assert.Equal(expected, MascotSprites.Frame(MascotAnimation.Walk, seconds));
    }

    [Fact]
    public void The_slow_walk_runs_at_half_the_frame_rate()
    {
        Assert.Equal(0, MascotSprites.Frame(MascotAnimation.Walk, 0.15, slow: true));
        Assert.Equal(1, MascotSprites.Frame(MascotAnimation.Walk, 0.25, slow: true));
    }

    [Fact]
    public void Each_animation_has_its_own_row_and_frame_count()
    {
        Assert.Equal([0, 1, 2], new[] { MascotAnimation.Walk, MascotAnimation.Shout, MascotAnimation.Sleep }.Select(MascotSprites.Row));
        Assert.Equal([4, 2, 2], new[] { MascotAnimation.Walk, MascotAnimation.Shout, MascotAnimation.Sleep }.Select(MascotSprites.FrameCount));
    }

    [Theory]
    [InlineData(3, 96, 48)]
    [InlineData(2, 96, 32)]
    [InlineData(4, 96, 64)]
    [InlineData(3, 192, 96)]
    [InlineData(3, 120, 64)]
    [InlineData(2, 24, 16)]
    public void Pixel_size_is_a_whole_multiple_of_the_cell_size_scaled_by_dpi(int scale, int dpi, int expected)
    {
        var size = MascotSprites.PixelSize(scale, dpi);

        Assert.Equal(expected, size);
        Assert.Equal(0, size % MascotSprites.CellSize);
    }
}
