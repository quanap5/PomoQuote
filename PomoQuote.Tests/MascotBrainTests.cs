using System.Drawing;
using PomoQuote.Mascot;

namespace PomoQuote.Tests;

public class MascotBrainTests
{
    private static MascotBrain Brain(params double[] randoms)
    {
        var index = 0;
        return new MascotBrain(MascotConfig.Default, () => randoms[Math.Min(index++, randoms.Length - 1)]);
    }

    // -- Weighted selection ------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0.00, "a")]
    [InlineData(0.19, "a")]
    [InlineData(0.20, "b")]
    [InlineData(0.69, "b")]
    [InlineData(0.70, "c")]
    [InlineData(0.9999, "c")]
    [InlineData(1.0, "c")]
    public void Weighted_pick_follows_the_cumulative_weights(double random, string expected)
    {
        var picked = MascotBrain.WeightedPick<Letter>([(Letter.A, 20), (Letter.B, 50), (Letter.C, 30)], random);

        Assert.Equal(expected, picked.ToString()!.ToLowerInvariant());
    }

    private enum Letter { A, B, C }

    [Fact]
    public void Weighted_pick_never_chooses_a_zero_weight_item()
    {
        for (var r = 0.0; r <= 1.0; r += 0.01)
        {
            Assert.Equal(Letter.B, MascotBrain.WeightedPick<Letter>([(Letter.A, 0), (Letter.B, 10), (Letter.C, 0)], r));
        }
    }

    [Fact]
    public void Weighted_pick_with_no_positive_weight_picks_nothing()
    {
        Assert.Null(MascotBrain.WeightedPick<Letter>([(Letter.A, 0), (Letter.B, -3)], 0.5));
        Assert.Null(MascotBrain.WeightedPick<Letter>([], 0.5));
    }

    [Fact]
    public void Weighted_pick_is_proportional_over_many_rolls()
    {
        var random = new Random(7);
        var counts = new int[3];

        for (var i = 0; i < 30000; i++) counts[(int)MascotBrain.WeightedPick<Letter>([(Letter.A, 1), (Letter.B, 2), (Letter.C, 7)], random.NextDouble())!]++;

        Assert.InRange(counts[0] / 30000.0, 0.08, 0.12);
        Assert.InRange(counts[1] / 30000.0, 0.17, 0.23);
        Assert.InRange(counts[2] / 30000.0, 0.67, 0.73);
    }

    // -- Reactions ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0.0, MascotState.Wave)]
    [InlineData(0.59, MascotState.Wave)]
    [InlineData(0.61, MascotState.Surprised)]
    [InlineData(0.99, MascotState.Surprised)]
    public void A_click_is_a_wave_or_a_surprise_by_weight_60_40(double random, MascotState expected)
    {
        Assert.Equal(expected, Brain(random).PickReaction());
    }

    // -- Choosing the next action --------------------------------------------------------------------------------

    [Theory]
    [InlineData(0.00, MascotState.Walk)]
    [InlineData(0.39, MascotState.Walk)]
    [InlineData(0.41, MascotState.Idle)] // 40..60
    [InlineData(0.61, MascotState.Look)] // 60..75
    [InlineData(0.76, MascotState.Sit)] // 75..85
    [InlineData(0.86, MascotState.Wave)] // 85..92
    [InlineData(0.93, MascotState.Jump)] // 92..98
    [InlineData(0.99, MascotState.Surprised)]
    public void An_idle_character_chooses_by_the_default_weights(double random, MascotState expected)
    {
        Assert.Equal(expected, Brain(random, 0.5).Next(MascotMood.Idle, level: 2).State);
    }

    [Theory]
    [InlineData(0.0, MascotState.Walk)]
    [InlineData(0.39, MascotState.Walk)]
    [InlineData(0.41, MascotState.Run)]
    [InlineData(0.99, MascotState.Run)]
    public void A_working_character_walks_or_runs_40_60(double random, MascotState expected)
    {
        Assert.Equal(expected, Brain(random, 0.5).Next(MascotMood.Working, level: 2).State);
    }

    [Fact]
    public void Breaks_always_mean_sitting_and_long_breaks_sleeping()
    {
        Assert.Equal(MascotState.Sit, Brain(0.3, 0.5).Next(MascotMood.Break, 2).State);
        Assert.Equal(MascotState.Sleep, Brain(0.3, 0.5).Next(MascotMood.LongBreak, 2).State);
    }

    [Fact]
    public void Durations_come_from_the_configured_ranges()
    {
        var behavior = MascotConfig.Default.Behavior;

        var walk = Brain(0.1, 0.0).Next(MascotMood.Idle, 2);
        var longWalk = Brain(0.1, 1.0).Next(MascotMood.Idle, 2);
        var run = Brain(0.9, 0.5).Next(MascotMood.Working, 2);

        Assert.Equal((MascotState.Walk, behavior.WalkSeconds.Min), (walk.State, walk.Seconds));
        Assert.Equal(behavior.WalkSeconds.Max, longWalk.Seconds);
        Assert.Equal(MascotState.Run, run.State);
        Assert.Equal(behavior.RunSeconds.At(0.5), run.Seconds);
    }

    [Theory]
    [InlineData(MascotMood.Idle, MascotState.Walk)]
    [InlineData(MascotMood.Working, MascotState.Walk)]
    [InlineData(MascotMood.Break, MascotState.Sit)]
    [InlineData(MascotMood.LongBreak, MascotState.Sleep)]
    public void With_random_actions_off_the_dice_are_not_rolled(MascotMood mood, MascotState expected)
    {
        foreach (var random in new[] { 0.0, 0.3, 0.7, 0.99 })
        {
            Assert.Equal(expected, Brain(random, random).Next(mood, level: 0).State);
        }
    }

    [Fact]
    public void With_few_random_actions_playful_choices_become_rarer_and_rests_longer()
    {
        var normal = new Random(3);
        var few = new Random(3);
        var playful = new[] { MascotState.Look, MascotState.Wave, MascotState.Jump, MascotState.Surprised };
        var normalBrain = new MascotBrain(MascotConfig.Default, normal.NextDouble);
        var fewBrain = new MascotBrain(MascotConfig.Default, few.NextDouble);
        int normalPlayful = 0, fewPlayful = 0;
        double normalRest = 0, fewRest = 0;
        int normalRests = 0, fewRests = 0;

        for (var i = 0; i < 5000; i++)
        {
            var n = normalBrain.Next(MascotMood.Idle, 2);
            var f = fewBrain.Next(MascotMood.Idle, 1);
            if (playful.Contains(n.State)) normalPlayful++;
            if (playful.Contains(f.State)) fewPlayful++;
            if (n.State == MascotState.Idle) { normalRest += n.Seconds; normalRests++; }
            if (f.State == MascotState.Idle) { fewRest += f.Seconds; fewRests++; }
        }

        Assert.True(fewPlayful < normalPlayful * 0.6, $"few={fewPlayful} normal={normalPlayful}");
        Assert.True(fewRest / fewRests > normalRest / normalRests * 1.5);
    }

    [Fact]
    public void The_same_random_numbers_always_give_the_same_decisions()
    {
        var first = new Random(11);
        var second = new Random(11);
        var a = new MascotBrain(MascotConfig.Default, first.NextDouble);
        var b = new MascotBrain(MascotConfig.Default, second.NextDouble);

        for (var i = 0; i < 200; i++) Assert.Equal(a.Next(MascotMood.Idle, 2), b.Next(MascotMood.Idle, 2));
    }
}

public class MascotDragTests
{
    // Two monitors side by side, 1920x1080 each, work area 1032 high (taskbar at the bottom).
    private static readonly Walkway Left = new("L", 0, 1920, 1032, 96, 0, 1080);
    private static readonly Walkway Right = new("R", 1920, 3840, 1032, 96, 0, 1080);
    private static readonly IReadOnlyList<Walkway> Both = [Left, Right];

    [Fact]
    public void A_position_inside_a_monitor_is_kept()
    {
        var point = MascotDrag.ClampFeet(new PointF(500, 600), 48, Both);

        Assert.Equal(new PointF(500, 600), point);
    }

    [Theory]
    [InlineData(-300, 600, 24, 600)] // off the left edge
    [InlineData(5000, 600, 3816, 600)] // off the right edge of the second monitor
    [InlineData(500, -400, 500, 48)] // above the top: the whole sprite stays visible
    [InlineData(500, 2000, 500, 1080)] // below the bottom of the screen
    public void Dragging_off_the_screen_is_clamped_so_the_sprite_stays_fully_visible(float x, float y, float expectedX, float expectedY)
    {
        var point = MascotDrag.ClampFeet(new PointF(x, y), 48, Both);

        Assert.Equal(new PointF(expectedX, expectedY), point);
    }

    [Fact]
    public void A_position_over_the_second_monitor_stays_on_it()
    {
        var point = MascotDrag.ClampFeet(new PointF(2500, 300), 48, Both);

        Assert.Equal(new PointF(2500, 300), point);
    }

    [Fact]
    public void Between_monitors_with_a_gap_the_nearest_one_wins()
    {
        IReadOnlyList<Walkway> gap = [Left, Right with { Left = 2100, Right = 4020 }];

        var point = MascotDrag.ClampFeet(new PointF(1990, 300), 48, gap);

        Assert.Equal(new PointF(1896, 300), point); // closer to the left monitor's edge than to the right one
    }

    [Fact]
    public void Stacked_monitors_are_handled_by_vertical_distance()
    {
        IReadOnlyList<Walkway> stacked = [new("Top", 0, 1920, 1032, 96, 0, 1080), new("Bottom", 0, 1920, 2112, 96, 1080, 2160)];

        var point = MascotDrag.ClampFeet(new PointF(500, 1500), 48, stacked);
        var (index, x, fall) = MascotDrag.LandingTarget(new PointF(500, 1500), 48, stacked);

        Assert.Equal(new PointF(500, 1500), point);
        Assert.Equal(1, index);
        Assert.Equal(500, x);
        Assert.Equal(2112 - 1500, fall);
    }

    [Fact]
    public void The_landing_is_on_the_taskbar_strip_of_the_monitor_below_the_character()
    {
        var (index, x, fall) = MascotDrag.LandingTarget(new PointF(2500, 300), 48, Both);

        Assert.Equal(1, index);
        Assert.Equal(2500, x);
        Assert.Equal(1032 - 300, fall);
    }

    [Fact]
    public void Dropping_at_or_below_the_ground_means_no_fall()
    {
        Assert.Equal(0, MascotDrag.LandingTarget(new PointF(500, 1032), 48, Both).FallPixels);
        Assert.Equal(0, MascotDrag.LandingTarget(new PointF(500, 1070), 48, Both).FallPixels);
    }

    [Fact]
    public void The_landing_x_is_kept_inside_the_strip()
    {
        var (_, x, _) = MascotDrag.LandingTarget(new PointF(-500, 200), 48, Both);

        Assert.Equal(24, x);
    }

    [Fact]
    public void Without_monitors_nothing_is_changed()
    {
        Assert.Equal(new PointF(10, 20), MascotDrag.ClampFeet(new PointF(10, 20), 48, []));
    }

    [Fact]
    public void A_sprite_wider_than_the_strip_is_centred_on_it()
    {
        IReadOnlyList<Walkway> narrow = [new("N", 100, 140, 500, 96, 0, 600)];

        var point = MascotDrag.ClampFeet(new PointF(900, 300), 96, narrow);

        Assert.Equal(120, point.X);
    }
}
