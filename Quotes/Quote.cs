namespace PomoQuote.Quotes;

/// <summary>The kinds of sentence. A selection is a set of them, so the flags combine.</summary>
[Flags]
public enum QuoteCategory
{
    None = 0,
    Motivation = 1,
    Idiom = 2,
    Conversation = 4,
    Proverb = 8,
    Quote = 16,
    Maxim = 32,
    Expression = 64,
    PhrasalVerb = 128,

    /// <summary>Every category. A settings file saved as "All" keeps meaning all, including categories added later.</summary>
    All = Motivation | Idiom | Conversation | Proverb | Quote | Maxim | Expression | PhrasalVerb,
}

/// <summary>One English sentence with its Vietnamese translation and where it comes from.</summary>
/// <param name="English">The sentence as shown.</param>
/// <param name="Vietnamese">Its translation, with full diacritics.</param>
/// <param name="Category">The kind of sentence.</param>
/// <param name="Author">Who said it, or null for proverbs and sentences written for the app.</param>
/// <param name="Source">Where the English text comes from (a work, "traditional proverb", "original").</param>
/// <param name="License">The terms the English text and the translation can be reused under.</param>
/// <param name="TatoebaId">For a sentence taken from Tatoeba: the id of the English sentence (<see cref="Author"/> is its author); null for every other sentence.</param>
/// <param name="TranslationId">For a Tatoeba sentence: the id of the Vietnamese translation.</param>
/// <param name="TranslationAuthor">For a Tatoeba sentence: the author of the Vietnamese translation.</param>
/// <param name="Info">For an expression record (idiom, proverb, quote, ...): meaning, example, translation state and source; null otherwise.</param>
internal sealed record Quote(string English, string Vietnamese, QuoteCategory Category, string? Author, string Source, string License, long? TatoebaId = null, long? TranslationId = null, string? TranslationAuthor = null, QuoteInfo? Info = null)
{
    /// <summary>
    /// The attribution a Tatoeba sentence needs (its licence, CC BY 2.0 FR, requires the authors' names): shown as a tooltip on the sentence. Null for
    /// a sentence that is not from Tatoeba.
    /// </summary>
    public string? Attribution => TatoebaId is { } id
        ? $"Tatoeba (tatoeba.org), CC BY 2.0 FR. English: sentence #{id} by {Author}. Vietnamese: sentence #{TranslationId} by {TranslationAuthor}."
        : Info is { } info ? $"{info.AttributionText} ({License})." : null;

    /// <summary>
    /// The tooltip on the sentence: for an expression its meaning, example and translation state, then the credit; for a Tatoeba sentence the credit;
    /// null when there is nothing to add.
    /// </summary>
    public string? Details
    {
        get
        {
            if (Info is not { } info) return Attribution;
            var lines = new List<string>();
            if (info.Meaning.Length > 0) lines.Add("Meaning: " + info.Meaning);
            if (info.ExampleEnglish.Length > 0) lines.Add("Example: " + info.ExampleEnglish + (info.ExampleVietnamese.Length > 0 ? "\n" + info.ExampleVietnamese : ""));
            if (Author is { Length: > 0 }) lines.Add("— " + Author + (info.Work.Length > 0 ? ", " + info.Work : ""));
            if (info.Badge is { } badge) lines.Add("Vietnamese: " + badge + ".");
            lines.Add(Attribution!);
            return string.Join("\n", lines);
        }
    }
}
