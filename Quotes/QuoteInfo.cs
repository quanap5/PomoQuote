namespace PomoQuote.Quotes;

/// <summary>Where a Vietnamese translation comes from and how far it has been checked.</summary>
internal enum TranslationStatus
{
    /// <summary>Written by a person in the source the sentence comes from.</summary>
    Source,

    /// <summary>Drafted by a machine and not reviewed by a person: the app says so.</summary>
    MachineUnreviewed,

    /// <summary>Machine drafted; passed a sample review.</summary>
    SampledOk,

    /// <summary>Checked by a person.</summary>
    HumanReviewed,
}

/// <summary>
/// What an expression record says beyond the two lines the widget shows: its meaning and example, the state of its translation, and the source,
/// licence and attribution that have to travel with it. Null for the sentences of quotes.json.
/// </summary>
/// <param name="Id">The record's id, unique across all data files.</param>
/// <param name="Meaning">The English meaning (idioms, expressions, phrasal verbs, proverbs); empty for quotes and maxims.</param>
/// <param name="ExampleEnglish">A short English example, or empty.</param>
/// <param name="ExampleVietnamese">Its translation, or empty.</param>
/// <param name="Status">The state of the Vietnamese translation.</param>
/// <param name="SourceUrl">The page or file the record was taken from.</param>
/// <param name="AttributionText">The credit the licence asks for.</param>
/// <param name="Work">For quotes and maxims: the author's work the words were found in.</param>
internal sealed record QuoteInfo(
    string Id, string Meaning, string ExampleEnglish, string ExampleVietnamese, TranslationStatus Status, string SourceUrl, string AttributionText, string Work)
{
    /// <summary>The visible note under a machine translation that no person has reviewed; null when there is nothing to warn about.</summary>
    public string? Badge => Status == TranslationStatus.MachineUnreviewed ? "machine translation, not reviewed" : null;
}
