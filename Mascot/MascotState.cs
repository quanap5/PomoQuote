namespace PomoQuote.Mascot;

/// <summary>
/// Everything the character can be doing. <see cref="Look"/>, <see cref="Land"/>, <see cref="Drag"/> and
/// <see cref="Shout"/> are the "helper" states between the main ones.
/// </summary>
public enum MascotState
{
    Idle,
    Look,
    Walk,
    Run,
    Jump,
    Land,
    Sit,
    Sleep,
    Wave,
    Celebrate,
    Surprised,
    Sad,
    Drag,
    Shout,
}

/// <summary>
/// Which state may follow which. The graph is what keeps the character natural: it can never pop from
/// sleeping to running; it sits up, stands, then runs. <see cref="PathTo"/> finds that chain.
/// </summary>
internal static class MascotStateGraph
{
    private static readonly IReadOnlyDictionary<MascotState, MascotState[]> Allowed = new Dictionary<MascotState, MascotState[]>
    {
        [MascotState.Idle] = [MascotState.Look, MascotState.Walk, MascotState.Run, MascotState.Jump, MascotState.Sit, MascotState.Wave, MascotState.Celebrate, MascotState.Surprised, MascotState.Sad, MascotState.Drag, MascotState.Shout],
        [MascotState.Look] = [MascotState.Idle, MascotState.Surprised, MascotState.Drag],
        [MascotState.Walk] = [MascotState.Idle, MascotState.Run, MascotState.Jump, MascotState.Surprised, MascotState.Drag, MascotState.Shout],
        [MascotState.Run] = [MascotState.Walk, MascotState.Jump, MascotState.Surprised, MascotState.Drag, MascotState.Shout],
        [MascotState.Jump] = [MascotState.Land, MascotState.Drag],
        [MascotState.Land] = [MascotState.Idle, MascotState.Drag],
        [MascotState.Sit] = [MascotState.Sleep, MascotState.Idle, MascotState.Wave, MascotState.Surprised, MascotState.Drag],
        [MascotState.Sleep] = [MascotState.Sit, MascotState.Drag],
        [MascotState.Wave] = [MascotState.Idle, MascotState.Surprised, MascotState.Drag],
        [MascotState.Celebrate] = [MascotState.Idle, MascotState.Jump, MascotState.Drag],
        [MascotState.Surprised] = [MascotState.Idle, MascotState.Drag],
        [MascotState.Sad] = [MascotState.Idle, MascotState.Drag],
        [MascotState.Drag] = [MascotState.Jump, MascotState.Land],
        [MascotState.Shout] = [MascotState.Land, MascotState.Drag],
    };

    public static IReadOnlyList<MascotState> NextStates(MascotState from) => Allowed[from];

    public static bool CanTransition(MascotState from, MascotState to) => Allowed[from].Contains(to);

    /// <summary>
    /// The shortest chain of allowed transitions from <paramref name="from"/> to <paramref name="to"/>, not
    /// including <paramref name="from"/> (empty when they are the same state). Sleep to Run is Sit, Idle, Run.
    /// </summary>
    public static IReadOnlyList<MascotState> PathTo(MascotState from, MascotState to)
    {
        if (from == to) return [];

        var previous = new Dictionary<MascotState, MascotState>();
        var queue = new Queue<MascotState>();
        queue.Enqueue(from);
        previous[from] = from;

        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            foreach (var next in Allowed[state])
            {
                if (previous.ContainsKey(next)) continue;
                previous[next] = state;
                if (next == to)
                {
                    var path = new List<MascotState>();
                    for (var step = to; step != from; step = previous[step]) path.Add(step);
                    path.Reverse();
                    return path;
                }

                queue.Enqueue(next);
            }
        }

        return [];
    }
}
