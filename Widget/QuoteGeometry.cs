namespace PomoQuote.Widget;

/// <summary>
/// The measurements and the font-size choice of the quote card, in logical pixels. The card grows with the text, so the text keeps
/// its size (12 px or more) and nothing is cut off; only when the card would pass <see cref="MaxCardHeight"/> does the text go
/// below 12 px, and only past the last step is the last line ended with an ellipsis. Pure: the wrapping is passed in.
/// </summary>
internal static class QuoteGeometry
{
    public const float PaddingTop = 16;

    /// <summary>Room under the last text line: the closing quotation mark sits in it, never under the text.</summary>
    public const float PaddingBottom = 24;

    public const float LineHeightFactor = 1.36f;
    public const float GapBetweenTexts = 5;

    /// <summary>The most the card may grow to (the widget grows with it).</summary>
    public const float MaxCardHeight = 210;

    /// <summary>The smallest size of the original text and of the translation before the card is allowed to go below 12 px.</summary>
    public const float MinimumPreferredPixels = 12;

    /// <summary>From the bottom of the card up to where the closing mark's visible shape starts; always inside <see cref="PaddingBottom"/>.</summary>
    public const float ClosingMarkVisibleHeight = 18;

    /// <summary>The (original, translation) font sizes to try, largest first. The first three keep both at 12 px or more.</summary>
    public static readonly (float English, float Vietnamese)[] Levels =
    [
        (13f, 12f), (12.5f, 12f), (12f, 12f),
        (11.5f, 11.5f), (11f, 11f), (10.5f, 10.5f),
    ];

    /// <summary>What was chosen: the lines and sizes, the card height, and whether the text had to go below 12 px or be cut.</summary>
    internal sealed record Plan(IReadOnlyList<string> English, IReadOnlyList<string>? Vietnamese, float EnglishPixels, float VietnamesePixels, float CardHeight, bool BelowMinimum, bool Ellipsized);

    public static float Height(int englishLines, float englishPixels, int vietnameseLines, float vietnamesePixels) =>
        (float)Math.Ceiling(PaddingTop + englishLines * englishPixels * LineHeightFactor
            + (vietnameseLines > 0 ? GapBetweenTexts + vietnameseLines * vietnamesePixels * LineHeightFactor : 0)
            + PaddingBottom);

    /// <summary>Where the last text line ends, from the top of the card.</summary>
    public static float LastLineBottom(float cardHeight) => cardHeight - PaddingBottom;

    /// <summary>Where the visible part of the closing mark starts, from the top of the card.</summary>
    public static float ClosingMarkTop(float cardHeight) => cardHeight - ClosingMarkVisibleHeight;

    /// <param name="wrapEnglish">The original text broken into lines at a font size (and ended with an ellipsis after <c>maxLines</c> when given).</param>
    /// <param name="wrapVietnamese">The translation broken into lines at a font size, or null when there is none.</param>
    public static Plan Choose(Func<float, int, IReadOnlyList<string>> wrapEnglish, Func<float, int, IReadOnlyList<string>>? wrapVietnamese)
    {
        for (var level = 0; level < Levels.Length; level++)
        {
            var (english, vietnamese) = Levels[level];
            var englishLines = wrapEnglish(english, int.MaxValue);
            var vietnameseLines = wrapVietnamese?.Invoke(vietnamese, int.MaxValue);
            var height = Height(englishLines.Count, english, vietnameseLines?.Count ?? 0, vietnamese);
            if (height <= MaxCardHeight) return new Plan(englishLines, vietnameseLines, english, vietnamese, height, level >= 3, false);
        }

        // Even the smallest size does not fit: keep it and end the last line with an ellipsis, so the card stays at the clamp.
        var (smallEnglish, smallVietnamese) = Levels[^1];
        var lineBudget = (int)Math.Floor((MaxCardHeight - PaddingTop - PaddingBottom - (wrapVietnamese is null ? 0 : GapBetweenTexts)) / (smallEnglish * LineHeightFactor));
        var englishShare = wrapVietnamese is null ? lineBudget : Math.Max(1, lineBudget * 55 / 100);
        var vietnameseShare = Math.Max(1, lineBudget - englishShare);
        var cutEnglish = wrapEnglish(smallEnglish, englishShare);
        var cutVietnamese = wrapVietnamese?.Invoke(smallVietnamese, vietnameseShare);
        return new Plan(cutEnglish, cutVietnamese, smallEnglish, smallVietnamese, Math.Min(MaxCardHeight, Height(cutEnglish.Count, smallEnglish, cutVietnamese?.Count ?? 0, smallVietnamese)), true, true);
    }
}
