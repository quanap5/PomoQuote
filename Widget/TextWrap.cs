namespace PomoQuote.Widget;

/// <summary>Word wrapping with a measuring function, so the quote card can draw every line itself and never cut the text off.</summary>
internal static class TextWrap
{
    /// <summary>
    /// Breaks <paramref name="text"/> into lines no wider than <paramref name="maxWidth"/> (a single word wider than that is
    /// broken by characters). With <paramref name="maxLines"/> the last line ends with an ellipsis when text is left over.
    /// </summary>
    public static IReadOnlyList<string> Lines(string text, float maxWidth, Func<string, float> measure, int maxLines = int.MaxValue)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var word in text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (measure(candidate) <= maxWidth)
            {
                current = candidate;
                continue;
            }

            if (current.Length > 0)
            {
                lines.Add(current);
                current = "";
            }

            var rest = word;
            while (measure(rest) > maxWidth && rest.Length > 1)
            {
                var cut = rest.Length - 1;
                while (cut > 1 && measure(rest[..cut]) > maxWidth) cut--;
                lines.Add(rest[..cut]);
                rest = rest[cut..];
            }

            current = rest;
        }

        if (current.Length > 0) lines.Add(current);
        if (lines.Count <= maxLines) return lines;

        var kept = lines.Take(maxLines).ToList();
        var last = kept[^1];
        while (last.Length > 1 && measure(last + "…") > maxWidth) last = last[..^1];
        kept[^1] = last + "…";
        return kept;
    }
}
