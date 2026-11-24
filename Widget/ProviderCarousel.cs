namespace PomoQuote.Widget;

/// <summary>
/// Which provider Compact shows. Pure and wrap-around: the wheel or the arrow keys step through the enabled providers in the
/// user's order, a provider that was switched off falls back to the first one, and nothing ever changes by itself.
/// </summary>
internal static class ProviderCarousel
{
    /// <summary>The provider to show: <paramref name="selected"/> when it is still enabled, else the first one (null when there is none).</summary>
    public static string? Resolve(IReadOnlyList<string> ids, string? selected) =>
        ids.Count == 0 ? null : selected is not null && ids.Contains(selected) ? selected : ids[0];

    /// <summary>The next (+1) or previous (-1) provider, wrapping around; one provider stays itself.</summary>
    public static string? Step(IReadOnlyList<string> ids, string? selected, int direction)
    {
        var current = Resolve(ids, selected);
        if (current is null || ids.Count == 1) return current;
        var index = ids.ToList().IndexOf(current);
        return ids[((index + Math.Sign(direction)) % ids.Count + ids.Count) % ids.Count];
    }

    /// <summary>"Codex (1 of 2)".</summary>
    public static string Position(IReadOnlyList<string> names, int index) => names.Count <= 1 ? names.FirstOrDefault() ?? "" : $"{names[index]} ({index + 1} of {names.Count})";
}
