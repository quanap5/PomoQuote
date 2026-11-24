namespace HelloWorldBot.Mascot;

/// <summary>What the character decided to do next, and for how long (movement and rests; finite animations ignore it).</summary>
internal sealed record MascotAction(MascotState State, double Seconds);

/// <summary>
/// The character's personality: picks the next thing to do with weighted dice. The random numbers come from an
/// injected function (0 up to but not including 1), so every decision is reproducible in tests.
/// </summary>
internal sealed class MascotBrain
{
    private readonly MascotConfig _config;
    private readonly Func<double> _random;

    public MascotBrain(MascotConfig config, Func<double> random)
    {
        _config = config;
        _random = random;
    }

    /// <summary>A random number from 0 up to 1, for the caller's own coin flips (e.g. which way to walk).</summary>
    public double Roll() => _random();

    /// <summary>How the character reacts when clicked: wave or surprised, by the configured weights.</summary>
    public MascotState PickReaction() =>
        WeightedPick(_config.Behavior.ReactionWeights.Select(pair => (pair.Key, pair.Value)).ToList(), _random()) ?? MascotState.Wave;

    /// <summary>
    /// The next action for <paramref name="mood"/>. <paramref name="level"/> is the "random actions" setting: 0 turns
    /// the dice off (the character only does what its mood needs: walk, sit, sleep), 1 makes playful actions rare and
    /// the rests long, 2 is the configured behaviour.
    /// </summary>
    public MascotAction Next(MascotMood mood, int level)
    {
        var behavior = _config.Behavior;

        if (level <= 0)
        {
            return mood switch
            {
                MascotMood.Break => new MascotAction(MascotState.Sit, behavior.SitSeconds.At(0.5)),
                MascotMood.LongBreak => new MascotAction(MascotState.Sleep, behavior.SitSeconds.At(0.5)),
                _ => new MascotAction(MascotState.Walk, behavior.WalkSeconds.At(0.5)),
            };
        }

        var weights = behavior.MoodWeights[mood]
            .Select(pair => (State: pair.Key, Weight: pair.Value * (level == 1 && IsPlayful(pair.Key) ? 0.35 : 1.0)))
            .ToList();
        var state = WeightedPick(weights, _random()) ?? MascotState.Idle;

        var rest = level == 1 ? 1.8 : 1.0;
        var seconds = state switch
        {
            MascotState.Walk => behavior.WalkSeconds.At(_random()),
            MascotState.Run => behavior.RunSeconds.At(_random()),
            MascotState.Idle => behavior.IdleSeconds.At(_random()) * rest,
            MascotState.Sit or MascotState.Sleep => behavior.SitSeconds.At(_random()),
            _ => 0,
        };
        return new MascotAction(state, seconds);
    }

    /// <summary>Picks one item with probability proportional to its weight; null when every weight is zero.</summary>
    internal static T? WeightedPick<T>(IReadOnlyList<(T Item, double Weight)> items, double random)
        where T : struct
    {
        var total = items.Sum(item => Math.Max(0, item.Weight));
        if (total <= 0) return default;

        var target = Math.Clamp(random, 0, 0.999999999) * total;
        var running = 0.0;
        foreach (var (item, weight) in items)
        {
            if (weight <= 0) continue;
            running += weight;
            if (target < running) return item;
        }

        return items.Last(item => item.Weight > 0).Item;
    }

    private static bool IsPlayful(MascotState state) =>
        state is MascotState.Look or MascotState.Wave or MascotState.Jump or MascotState.Surprised or MascotState.Celebrate;
}
