using System.Globalization;
using System.Text.Json;

namespace PomoQuote.Mascot;

/// <summary>What the Pomodoro says about the day: it decides how the character spends its time.</summary>
public enum MascotMood
{
    /// <summary>No timer running (or paused): a slow stroll, now and then something playful.</summary>
    Idle,

    /// <summary>A work phase is running: walking or running briskly.</summary>
    Working,

    /// <summary>A short break: sits down, then dozes off.</summary>
    Break,

    /// <summary>A long break: straight to sleep.</summary>
    LongBreak,
}

/// <summary>One animation: where its frames are in the sprite sheet and how it plays.</summary>
/// <param name="Row">The row of the sheet (frames run left to right from column 0).</param>
/// <param name="Frames">How many frames the animation has.</param>
/// <param name="Fps">Frames per second. For walk and run this is only the nominal rate: the real one follows the speed.</param>
/// <param name="Loop">True to repeat until the state ends; false to play <paramref name="Loops"/> times and finish.</param>
/// <param name="LoopFrom">When looping, the frame to repeat from after the first pass (an intro of frames before it).</param>
/// <param name="Loops">How many times a non-looping animation plays.</param>
/// <param name="StrideDips">Walk and run: how far the character travels during one full cycle, so feet do not slide.</param>
internal sealed record AnimationDef(MascotState State, int Row, int Frames, double Fps, bool Loop, int LoopFrom = 0, int Loops = 1, double StrideDips = 0)
{
    /// <summary>How long a non-looping animation takes.</summary>
    public double DurationSeconds => Frames / Fps * Loops;

    /// <summary>The frame to show <paramref name="seconds"/> into the animation.</summary>
    public int FrameAt(double seconds)
    {
        var index = (int)(Math.Max(0, seconds) * Fps + 1e-6);
        if (!Loop) return index >= Frames * Loops ? Frames - 1 : index % Frames;
        if (index < Frames) return index;
        var loopStart = Math.Clamp(LoopFrom, 0, Frames - 1);
        return loopStart + (index - Frames) % (Frames - loopStart);
    }
}

/// <summary>A range of seconds to pick a random duration from.</summary>
internal sealed record SecondsRange(double Min, double Max)
{
    /// <summary>A value in the range for a random number between 0 and 1.</summary>
    public double At(double random) => Min + (Max - Min) * Math.Clamp(random, 0, 1);
}

/// <summary>Speeds, jump and fall numbers. Lengths are in device-independent pixels (96 dpi).</summary>
internal sealed record MascotMovement(
    double WalkDipsPerSecond = 70,
    double RunMultiplier = 2.2,
    double IdleWalkFactor = 0.35,
    double JumpSeconds = 0.9,
    double JumpHeightDips = 30,
    double GravityDipsPerSecond2 = 2600,
    double CrossingSeconds = 0.8,
    double CrossingArcDips = 56,
    double TurnPauseSeconds = 0.25);

/// <summary>How the character chooses what to do: how long it rests between actions and how likely each action is.</summary>
internal sealed record MascotBehavior(
    SecondsRange IdleSeconds,
    SecondsRange WalkSeconds,
    SecondsRange RunSeconds,
    SecondsRange SitSeconds,
    double SleepAfterSitSeconds,
    IReadOnlyDictionary<MascotMood, IReadOnlyDictionary<MascotState, double>> MoodWeights,
    IReadOnlyDictionary<MascotState, double> ReactionWeights);

/// <summary>
/// All the numbers that make the character feel alive, as one value: animation frame ranges and speeds, movement,
/// and the probability weights of its random actions. <see cref="Default"/> is built in; a JSON file shipped with the
/// app and an optional user file in %AppData% override it field by field. Anything invalid is ignored with a warning
/// and the default for that field is kept.
/// </summary>
internal sealed class MascotConfig
{
    /// <summary>The sprite sheet has this many columns, so an animation cannot have more frames.</summary>
    public const int SheetColumns = 8;

    public int FrameSize { get; init; } = 24;

    public MascotMovement Movement { get; init; } = new();

    public MascotBehavior Behavior { get; init; } = DefaultBehavior();

    public IReadOnlyDictionary<MascotState, AnimationDef> Animations { get; init; } = DefaultAnimations();

    public static MascotConfig Default { get; } = new();

    public AnimationDef Animation(MascotState state) => Animations[state];

    private static IReadOnlyDictionary<MascotState, AnimationDef> DefaultAnimations() => new Dictionary<MascotState, AnimationDef>
    {
        [MascotState.Idle] = new(MascotState.Idle, 0, 4, 3, true),
        [MascotState.Look] = new(MascotState.Look, 1, 4, 5, false),
        [MascotState.Walk] = new(MascotState.Walk, 2, 6, 10, true, StrideDips: 40),
        [MascotState.Run] = new(MascotState.Run, 3, 6, 12, true, StrideDips: 72),
        [MascotState.Jump] = new(MascotState.Jump, 4, 6, 8, false),
        [MascotState.Land] = new(MascotState.Land, 5, 3, 10, false),
        [MascotState.Sit] = new(MascotState.Sit, 6, 4, 4, true, LoopFrom: 2),
        [MascotState.Sleep] = new(MascotState.Sleep, 7, 4, 2.5, true),
        [MascotState.Wave] = new(MascotState.Wave, 8, 6, 8, false, Loops: 2),
        [MascotState.Celebrate] = new(MascotState.Celebrate, 9, 6, 8, false, Loops: 3),
        [MascotState.Surprised] = new(MascotState.Surprised, 10, 4, 6, false),
        [MascotState.Sad] = new(MascotState.Sad, 11, 4, 3, false, Loops: 2),
        [MascotState.Drag] = new(MascotState.Drag, 12, 4, 8, true),
        [MascotState.Shout] = new(MascotState.Shout, 13, 2, 6, true),
    };

    private static MascotBehavior DefaultBehavior() => new(
        IdleSeconds: new SecondsRange(2.5, 7),
        WalkSeconds: new SecondsRange(4, 12),
        RunSeconds: new SecondsRange(2, 6),
        SitSeconds: new SecondsRange(5, 12),
        SleepAfterSitSeconds: 6,
        MoodWeights: new Dictionary<MascotMood, IReadOnlyDictionary<MascotState, double>>
        {
            [MascotMood.Idle] = new Dictionary<MascotState, double>
            {
                [MascotState.Walk] = 40, [MascotState.Idle] = 20, [MascotState.Look] = 15, [MascotState.Sit] = 10,
                [MascotState.Wave] = 7, [MascotState.Jump] = 6, [MascotState.Surprised] = 2,
            },
            [MascotMood.Working] = new Dictionary<MascotState, double> { [MascotState.Walk] = 40, [MascotState.Run] = 60 },
            [MascotMood.Break] = new Dictionary<MascotState, double> { [MascotState.Sit] = 100 },
            [MascotMood.LongBreak] = new Dictionary<MascotState, double> { [MascotState.Sleep] = 100 },
        },
        ReactionWeights: new Dictionary<MascotState, double> { [MascotState.Wave] = 60, [MascotState.Surprised] = 40 });

    // -- Parsing -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Applies the JSON in <paramref name="json"/> on top of <paramref name="baseline"/>. Anything missing keeps the
    /// baseline value; anything invalid keeps it too and adds a line to <paramref name="warnings"/>. Never throws.
    /// </summary>
    public static MascotConfig Parse(string? json, MascotConfig baseline, ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(json)) return baseline;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            warnings.Add($"The mascot config is not valid JSON ({ex.Message}); using the defaults.");
            return baseline;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("The mascot config must be a JSON object; using the defaults.");
                return baseline;
            }

            var frameSize = baseline.FrameSize;
            if (root.TryGetProperty("frameSize", out var frameElement))
            {
                if (TryInt(frameElement, 8, 128, out var size)) frameSize = size;
                else warnings.Add("frameSize must be a whole number from 8 to 128.");
            }

            return new MascotConfig
            {
                FrameSize = frameSize,
                Movement = root.TryGetProperty("movement", out var movement) ? ParseMovement(movement, baseline.Movement, warnings) : baseline.Movement,
                Animations = root.TryGetProperty("animations", out var animations) ? ParseAnimations(animations, baseline.Animations, warnings) : baseline.Animations,
                Behavior = root.TryGetProperty("behavior", out var behavior) ? ParseBehavior(behavior, baseline.Behavior, warnings) : baseline.Behavior,
            };
        }
    }

    /// <summary>
    /// The built-in defaults, then the shipped file, then the user's file on top. A file that is missing or unreadable
    /// is skipped silently; invalid content adds warnings.
    /// </summary>
    public static MascotConfig Load(string? shippedPath, string? userPath, ICollection<string> warnings)
    {
        var config = Default;
        foreach (var path in new[] { shippedPath, userPath })
        {
            if (string.IsNullOrEmpty(path)) continue;
            try
            {
                if (File.Exists(path)) config = Parse(File.ReadAllText(path), config, warnings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not read {path}: {ex.Message}");
            }
        }

        return config;
    }

    private static MascotMovement ParseMovement(JsonElement element, MascotMovement baseline, ICollection<string> warnings)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            warnings.Add("movement must be an object.");
            return baseline;
        }

        return baseline with
        {
            WalkDipsPerSecond = Number(element, "walkDipsPerSecond", 5, 2000, baseline.WalkDipsPerSecond, warnings),
            RunMultiplier = Number(element, "runMultiplier", 1, 10, baseline.RunMultiplier, warnings),
            IdleWalkFactor = Number(element, "idleWalkFactor", 0.05, 1, baseline.IdleWalkFactor, warnings),
            JumpSeconds = Number(element, "jumpSeconds", 0.3, 5, baseline.JumpSeconds, warnings),
            JumpHeightDips = Number(element, "jumpHeightDips", 4, 400, baseline.JumpHeightDips, warnings),
            GravityDipsPerSecond2 = Number(element, "gravityDipsPerSecond2", 100, 20000, baseline.GravityDipsPerSecond2, warnings),
            CrossingSeconds = Number(element, "crossingSeconds", 0.2, 5, baseline.CrossingSeconds, warnings),
            CrossingArcDips = Number(element, "crossingArcDips", 0, 400, baseline.CrossingArcDips, warnings),
            TurnPauseSeconds = Number(element, "turnPauseSeconds", 0, 3, baseline.TurnPauseSeconds, warnings),
        };
    }

    private static IReadOnlyDictionary<MascotState, AnimationDef> ParseAnimations(JsonElement element, IReadOnlyDictionary<MascotState, AnimationDef> baseline, ICollection<string> warnings)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            warnings.Add("animations must be an object.");
            return baseline;
        }

        var result = new Dictionary<MascotState, AnimationDef>(baseline);
        foreach (var property in element.EnumerateObject())
        {
            if (!Enum.TryParse<MascotState>(property.Name, ignoreCase: true, out var state))
            {
                warnings.Add($"Unknown animation \"{property.Name}\" ignored.");
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                warnings.Add($"Animation \"{property.Name}\" must be an object.");
                continue;
            }

            var current = result[state];
            var value = property.Value;
            var frames = (int)Number(value, "frames", 1, SheetColumns, current.Frames, warnings, property.Name);
            var loopFrom = (int)Number(value, "loopFrom", 0, SheetColumns - 1, current.LoopFrom, warnings, property.Name);
            result[state] = current with
            {
                Row = (int)Number(value, "row", 0, 63, current.Row, warnings, property.Name),
                Frames = frames,
                Fps = Number(value, "fps", 0.25, 60, current.Fps, warnings, property.Name),
                Loop = Bool(value, "loop", current.Loop, warnings, property.Name),
                LoopFrom = Math.Min(loopFrom, frames - 1),
                Loops = (int)Number(value, "loops", 1, 20, current.Loops, warnings, property.Name),
                StrideDips = Number(value, "strideDips", 0, 1000, current.StrideDips, warnings, property.Name),
            };
        }

        return result;
    }

    private static MascotBehavior ParseBehavior(JsonElement element, MascotBehavior baseline, ICollection<string> warnings)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            warnings.Add("behavior must be an object.");
            return baseline;
        }

        var moods = new Dictionary<MascotMood, IReadOnlyDictionary<MascotState, double>>(baseline.MoodWeights);
        if (element.TryGetProperty("moods", out var moodsElement) && moodsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in moodsElement.EnumerateObject())
            {
                if (!Enum.TryParse<MascotMood>(property.Name, ignoreCase: true, out var mood))
                {
                    warnings.Add($"Unknown mood \"{property.Name}\" ignored.");
                    continue;
                }

                var weights = Weights(property.Value, $"moods.{property.Name}", warnings);
                if (weights is not null) moods[mood] = weights;
            }
        }
        else if (element.TryGetProperty("moods", out _))
        {
            warnings.Add("moods must be an object.");
        }

        var reactions = baseline.ReactionWeights;
        if (element.TryGetProperty("reactions", out var reactionsElement))
        {
            reactions = Weights(reactionsElement, "reactions", warnings) ?? reactions;
        }

        return new MascotBehavior(
            Range(element, "idleSeconds", baseline.IdleSeconds, warnings),
            Range(element, "walkSeconds", baseline.WalkSeconds, warnings),
            Range(element, "runSeconds", baseline.RunSeconds, warnings),
            Range(element, "sitSeconds", baseline.SitSeconds, warnings),
            Number(element, "sleepAfterSitSeconds", 0, 3600, baseline.SleepAfterSitSeconds, warnings),
            moods,
            reactions);
    }

    private static IReadOnlyDictionary<MascotState, double>? Weights(JsonElement element, string name, ICollection<string> warnings)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            warnings.Add($"{name} must be an object of action weights.");
            return null;
        }

        var weights = new Dictionary<MascotState, double>();
        foreach (var property in element.EnumerateObject())
        {
            if (!Enum.TryParse<MascotState>(property.Name, ignoreCase: true, out var state))
            {
                warnings.Add($"{name}: unknown action \"{property.Name}\" ignored.");
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var weight) && weight >= 0 && weight <= 1_000_000)
            {
                weights[state] = weight;
            }
            else
            {
                warnings.Add($"{name}.{property.Name} must be a number from 0 up.");
            }
        }

        if (weights.Values.All(w => w <= 0))
        {
            warnings.Add($"{name} has no action with a weight above 0; keeping the defaults.");
            return null;
        }

        return weights;
    }

    private static SecondsRange Range(JsonElement parent, string name, SecondsRange baseline, ICollection<string> warnings)
    {
        if (!parent.TryGetProperty(name, out var element)) return baseline;
        if (element.ValueKind == JsonValueKind.Object)
        {
            var min = Number(element, "min", 0, 3600, baseline.Min, warnings, name);
            var max = Number(element, "max", 0, 3600, baseline.Max, warnings, name);
            if (min <= max) return new SecondsRange(min, max);
        }

        warnings.Add($"{name} must be {{\"min\": seconds, \"max\": seconds}} with min <= max.");
        return baseline;
    }

    private static double Number(JsonElement parent, string name, double min, double max, double fallback, ICollection<string> warnings, string? scope = null)
    {
        if (!parent.TryGetProperty(name, out var element)) return fallback;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var value) && double.IsFinite(value) && value >= min && value <= max) return value;

        warnings.Add($"{(scope is null ? "" : scope + ".")}{name} must be a number from {min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}.");
        return fallback;
    }

    private static bool Bool(JsonElement parent, string name, bool fallback, ICollection<string> warnings, string scope)
    {
        if (!parent.TryGetProperty(name, out var element)) return fallback;
        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False) return element.GetBoolean();

        warnings.Add($"{scope}.{name} must be true or false.");
        return fallback;
    }

    private static bool TryInt(JsonElement element, int min, int max, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value) && value >= min && value <= max;
    }
}
