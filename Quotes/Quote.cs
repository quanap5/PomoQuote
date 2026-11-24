namespace PomoQuote.Quotes;

/// <summary>The kinds of sentence. A selection is a set of them, so the flags combine.</summary>
[Flags]
public enum QuoteCategory
{
    None = 0,
    Motivation = 1,
    Idiom = 2,
    Conversation = 4,
    All = Motivation | Idiom | Conversation,
}

/// <summary>One English sentence with its Vietnamese translation and where it comes from.</summary>
/// <param name="English">The sentence as shown.</param>
/// <param name="Vietnamese">Its translation, with full diacritics.</param>
/// <param name="Category">The kind of sentence.</param>
/// <param name="Author">Who said it, or null for proverbs and sentences written for the app.</param>
/// <param name="Source">Where the English text comes from (a work, "traditional proverb", "original").</param>
/// <param name="License">The terms the English text and the translation can be reused under.</param>
internal sealed record Quote(string English, string Vietnamese, QuoteCategory Category, string? Author, string Source, string License);
