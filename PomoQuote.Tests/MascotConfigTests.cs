using System.Drawing;
using PomoQuote.Mascot;

namespace PomoQuote.Tests;

public sealed class MascotConfigTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PomoQuoteTests-" + Guid.NewGuid().ToString("N"));
    private static readonly string AssetsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Mascot");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static MascotConfig Parse(string json, out List<string> warnings)
    {
        warnings = [];
        return MascotConfig.Parse(json, MascotConfig.Default, warnings);
    }

    private string WriteFile(string name, string content)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    // -- Defaults and the shipped file ---------------------------------------------------------------------------

    [Fact]
    public void Every_state_has_a_default_animation()
    {
        foreach (var state in Enum.GetValues<MascotState>())
        {
            var animation = MascotConfig.Default.Animation(state);
            Assert.Equal(state, animation.State);
            Assert.InRange(animation.Frames, 1, MascotConfig.SheetColumns);
            Assert.True(animation.Fps > 0);
        }
    }

    [Fact]
    public void The_shipped_json_parses_without_warnings_and_equals_the_built_in_defaults()
    {
        var warnings = new List<string>();

        var config = MascotConfig.Load(Path.Combine(AssetsDirectory, "mascot-config.json"), null, warnings);

        Assert.Empty(warnings);
        Assert.Equal(MascotConfig.Default.FrameSize, config.FrameSize);
        Assert.Equal(MascotConfig.Default.Movement, config.Movement);
        Assert.Equal(MascotConfig.Default.Animations.OrderBy(p => p.Key).Select(p => p.Value), config.Animations.OrderBy(p => p.Key).Select(p => p.Value));
        var defaults = MascotConfig.Default.Behavior;
        Assert.Equal(defaults.IdleSeconds, config.Behavior.IdleSeconds);
        Assert.Equal(defaults.WalkSeconds, config.Behavior.WalkSeconds);
        Assert.Equal(defaults.RunSeconds, config.Behavior.RunSeconds);
        Assert.Equal(defaults.SitSeconds, config.Behavior.SitSeconds);
        Assert.Equal(defaults.SleepAfterSitSeconds, config.Behavior.SleepAfterSitSeconds);
        Assert.Equal(defaults.ReactionWeights.OrderBy(p => p.Key), config.Behavior.ReactionWeights.OrderBy(p => p.Key));
        foreach (var mood in Enum.GetValues<MascotMood>())
        {
            Assert.Equal(defaults.MoodWeights[mood].OrderBy(p => p.Key), config.Behavior.MoodWeights[mood].OrderBy(p => p.Key));
        }
    }

    [Fact]
    public void The_sprite_sheet_matches_the_config_and_every_frame_is_drawn()
    {
        using var sheet = new Bitmap(Path.Combine(AssetsDirectory, "robot-sprites.png"));
        var config = MascotConfig.Default;

        Assert.Equal(MascotConfig.SheetColumns * config.FrameSize, sheet.Width);
        Assert.Equal(config.Animations.Count * config.FrameSize, sheet.Height);
        Assert.Equal(config.Animations.Count, config.Animations.Values.Select(a => a.Row).Distinct().Count());

        foreach (var animation in config.Animations.Values)
        {
            Assert.InRange(animation.Row, 0, sheet.Height / config.FrameSize - 1);
            for (var frame = 0; frame < animation.Frames; frame++)
            {
                var opaque = 0;
                for (var x = 0; x < config.FrameSize; x++)
                {
                    for (var y = 0; y < config.FrameSize; y++)
                    {
                        if (sheet.GetPixel(frame * config.FrameSize + x, animation.Row * config.FrameSize + y).A > 0) opaque++;
                    }
                }

                Assert.True(opaque > 60, $"{animation.State} frame {frame} looks empty ({opaque} opaque pixels)");
            }
        }
    }

    [Fact]
    public void The_sprite_sheet_uses_one_palette_and_only_full_or_no_transparency()
    {
        using var sheet = new Bitmap(Path.Combine(AssetsDirectory, "robot-sprites.png"));
        var colours = new HashSet<int>();
        for (var x = 0; x < sheet.Width; x++)
        {
            for (var y = 0; y < sheet.Height; y++)
            {
                var pixel = sheet.GetPixel(x, y);
                Assert.True(pixel.A is 0 or 255, $"pixel {x},{y} is semi-transparent");
                if (pixel.A == 255) colours.Add(pixel.ToArgb());
            }
        }

        Assert.InRange(colours.Count, 4, 12);
    }

    // -- Parsing ------------------------------------------------------------------------------------------------

    [Fact]
    public void Missing_values_keep_the_defaults()
    {
        var config = Parse("""{ "movement": { "walkDipsPerSecond": 120 } }""", out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(120, config.Movement.WalkDipsPerSecond);
        Assert.Equal(MascotConfig.Default.Movement.RunMultiplier, config.Movement.RunMultiplier);
        Assert.Equal(MascotConfig.Default.Animation(MascotState.Walk), config.Animation(MascotState.Walk));
    }

    [Fact]
    public void An_animation_can_be_changed_field_by_field()
    {
        var config = Parse("""{ "animations": { "walk": { "fps": 14, "strideDips": 55 }, "Wave": { "loops": 4 } } }""", out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(MascotConfig.Default.Animation(MascotState.Walk) with { Fps = 14, StrideDips = 55 }, config.Animation(MascotState.Walk));
        Assert.Equal(4, config.Animation(MascotState.Wave).Loops);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_allowed()
    {
        var config = Parse("""
            {
              // a comment
              "movement": { "jumpSeconds": 1.2, },
            }
            """, out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(1.2, config.Movement.JumpSeconds);
    }

    [Theory]
    [InlineData("""{ "movement": { "walkDipsPerSecond": -5 } }""")]
    [InlineData("""{ "movement": { "walkDipsPerSecond": "fast" } }""")]
    [InlineData("""{ "movement": { "runMultiplier": 99 } }""")]
    [InlineData("""{ "movement": { "jumpSeconds": 0 } }""")]
    [InlineData("""{ "movement": 5 }""")]
    public void Invalid_movement_values_fall_back_with_a_warning(string json)
    {
        var config = Parse(json, out var warnings);

        Assert.NotEmpty(warnings);
        Assert.Equal(MascotConfig.Default.Movement, config.Movement);
    }

    [Theory]
    [InlineData("""{ "animations": { "walk": { "frames": 0 } } }""")]
    [InlineData("""{ "animations": { "walk": { "frames": 9 } } }""")]
    [InlineData("""{ "animations": { "walk": { "fps": 0 } } }""")]
    [InlineData("""{ "animations": { "walk": { "fps": 500 } } }""")]
    [InlineData("""{ "animations": { "walk": { "row": -1 } } }""")]
    [InlineData("""{ "animations": { "walk": { "loop": "yes" } } }""")]
    [InlineData("""{ "animations": { "walk": 3 } }""")]
    public void Invalid_animation_values_keep_the_default_animation(string json)
    {
        var config = Parse(json, out var warnings);

        Assert.NotEmpty(warnings);
        Assert.Equal(MascotConfig.Default.Animation(MascotState.Walk), config.Animation(MascotState.Walk));
    }

    [Fact]
    public void An_unknown_animation_or_mood_is_ignored_with_a_warning()
    {
        var config = Parse("""{ "animations": { "moonwalk": { "row": 3 } }, "behavior": { "moods": { "grumpy": { "walk": 1 } } } }""", out var warnings);

        Assert.Equal(2, warnings.Count);
        Assert.Equal(MascotConfig.Default.Animations.Count, config.Animations.Count);
    }

    [Fact]
    public void Weights_can_be_changed_per_mood_and_for_clicks()
    {
        var config = Parse("""{ "behavior": { "moods": { "idle": { "sit": 5, "wave": 95 } }, "reactions": { "surprised": 1 } } }""", out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(new Dictionary<MascotState, double> { [MascotState.Sit] = 5, [MascotState.Wave] = 95 }, config.Behavior.MoodWeights[MascotMood.Idle]);
        Assert.Equal(new Dictionary<MascotState, double> { [MascotState.Surprised] = 1 }, config.Behavior.ReactionWeights);
        Assert.Equal(MascotConfig.Default.Behavior.MoodWeights[MascotMood.Working].Count, config.Behavior.MoodWeights[MascotMood.Working].Count);
    }

    [Theory]
    [InlineData("""{ "behavior": { "moods": { "idle": { "walk": 0, "sit": 0 } } } }""")]
    [InlineData("""{ "behavior": { "moods": { "idle": {} } } }""")]
    [InlineData("""{ "behavior": { "moods": { "idle": { "walk": -4 } } } }""")]
    [InlineData("""{ "behavior": { "moods": { "idle": [1, 2] } } }""")]
    public void A_mood_without_any_usable_weight_keeps_its_default_table(string json)
    {
        var config = Parse(json, out var warnings);

        Assert.NotEmpty(warnings);
        Assert.Equal(MascotConfig.Default.Behavior.MoodWeights[MascotMood.Idle].OrderBy(p => p.Key), config.Behavior.MoodWeights[MascotMood.Idle].OrderBy(p => p.Key));
    }

    [Fact]
    public void Seconds_ranges_need_min_not_above_max()
    {
        var good = Parse("""{ "behavior": { "idleSeconds": { "min": 1, "max": 2 } } }""", out var goodWarnings);
        var bad = Parse("""{ "behavior": { "idleSeconds": { "min": 9, "max": 2 } } }""", out var badWarnings);

        Assert.Empty(goodWarnings);
        Assert.Equal(new SecondsRange(1, 2), good.Behavior.IdleSeconds);
        Assert.NotEmpty(badWarnings);
        Assert.Equal(MascotConfig.Default.Behavior.IdleSeconds, bad.Behavior.IdleSeconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_text_means_no_changes_and_no_warning(string json)
    {
        var config = Parse(json, out var warnings);

        Assert.Empty(warnings);
        Assert.Same(MascotConfig.Default, config);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("null")]
    public void Broken_json_gives_the_defaults_and_one_warning(string json)
    {
        var config = Parse(json, out var warnings);

        Assert.Single(warnings);
        Assert.Same(MascotConfig.Default, config);
    }

    [Fact]
    public void A_bad_frame_size_is_rejected()
    {
        Parse("""{ "frameSize": 3 }""", out var warnings);

        Assert.Single(warnings);
    }

    [Fact]
    public void Looping_animations_with_an_intro_repeat_from_the_loop_frame()
    {
        var sit = MascotConfig.Default.Animation(MascotState.Sit); // 4 frames at 4 fps, loops from frame 2

        var frames = Enumerable.Range(0, 12).Select(i => sit.FrameAt(i / 4.0 + 0.01)).ToArray();

        Assert.Equal([0, 1, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3], frames);
    }

    [Fact]
    public void One_off_animations_play_their_loops_and_then_hold_the_last_frame()
    {
        var wave = MascotConfig.Default.Animation(MascotState.Wave); // 6 frames at 8 fps, 2 loops

        Assert.Equal(1.5, wave.DurationSeconds);
        Assert.Equal(0, wave.FrameAt(0));
        Assert.Equal(5, wave.FrameAt(0.7));
        Assert.Equal(0, wave.FrameAt(0.8)); // the second loop starts again
        Assert.Equal(5, wave.FrameAt(1.45));
        Assert.Equal(5, wave.FrameAt(10)); // after the last loop the final frame is held
    }

    // -- Loading files ------------------------------------------------------------------------------------------

    [Fact]
    public void The_user_file_overrides_the_shipped_file_which_overrides_the_defaults()
    {
        var shipped = WriteFile("shipped.json", """{ "movement": { "walkDipsPerSecond": 100, "runMultiplier": 3 } }""");
        var user = WriteFile("user.json", """{ "movement": { "walkDipsPerSecond": 55 } }""");
        var warnings = new List<string>();

        var config = MascotConfig.Load(shipped, user, warnings);

        Assert.Empty(warnings);
        Assert.Equal(55, config.Movement.WalkDipsPerSecond);
        Assert.Equal(3, config.Movement.RunMultiplier);
    }

    [Fact]
    public void Missing_files_are_skipped_silently()
    {
        var warnings = new List<string>();

        var config = MascotConfig.Load(Path.Combine(_directory, "nope.json"), Path.Combine(_directory, "nope-either.json"), warnings);

        Assert.Empty(warnings);
        Assert.Same(MascotConfig.Default, config);
    }

    [Fact]
    public void A_broken_user_file_leaves_the_shipped_settings_in_force()
    {
        var shipped = WriteFile("shipped.json", """{ "movement": { "walkDipsPerSecond": 100 } }""");
        var user = WriteFile("user.json", "{ oops");
        var warnings = new List<string>();

        var config = MascotConfig.Load(shipped, user, warnings);

        Assert.Single(warnings);
        Assert.Equal(100, config.Movement.WalkDipsPerSecond);
    }

    [Fact]
    public void An_unreadable_file_path_is_reported_not_thrown()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "folder.json"));
        var warnings = new List<string>();

        var config = MascotConfig.Load(Path.Combine(_directory, "folder.json"), null, warnings);

        Assert.Same(MascotConfig.Default, config);
    }
}
