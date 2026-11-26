using System.Text.Json;

namespace PomoQuote.Quotes;

/// <summary>
/// Reads the expression data: one JSON Lines file per source and licence, one record per line (idioms, proverbs, quotes, life maxims, common
/// expressions and phrasal verbs). The rules are strict, because every record is licensed material the app ships: an incomplete record, two
/// licences in one record or in one file, an unknown type or status, a quote without a verified public-domain author, or a duplicate id is an
/// error, never something to skip silently.
/// </summary>
internal static class ExpressionCatalog
{
    /// <summary>A quote or maxim author must have died by this year (70 full years before 2026), so the words are in the public domain.</summary>
    internal const int LatestDeathYear = 1955;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>The licences a source may have, and the licences its translation may then carry. A record never mixes two licences.</summary>
    private static readonly Dictionary<string, string[]> Licenses = new(StringComparer.Ordinal)
    {
        ["CC-BY-SA-4.0"] = ["CC-BY-SA-4.0"],
        ["CC-BY-2.0-FR"] = ["CC-BY-2.0-FR"],
        // Words that are in the public domain: their translation is either also public domain or dedicated to it (CC0).
        ["Public-Domain"] = ["Public-Domain", "CC0-1.0"],
    };

    private static readonly Dictionary<string, QuoteCategory> Types = new(StringComparer.Ordinal)
    {
        ["idiom"] = QuoteCategory.Idiom,
        ["proverb"] = QuoteCategory.Proverb,
        ["quote"] = QuoteCategory.Quote,
        ["maxim"] = QuoteCategory.Maxim,
        ["expression"] = QuoteCategory.Expression,
        ["phrasal_verb"] = QuoteCategory.PhrasalVerb,
    };

    private static readonly Dictionary<string, TranslationStatus> Statuses = new(StringComparer.Ordinal)
    {
        ["source"] = TranslationStatus.Source,
        ["machine_unreviewed"] = TranslationStatus.MachineUnreviewed,
        ["sampled_ok"] = TranslationStatus.SampledOk,
        ["human_reviewed"] = TranslationStatus.HumanReviewed,
    };

    public static IReadOnlyList<Quote> Parse(string jsonLines, string fileName = "data.jsonl") =>
        Parse(new StringReader(jsonLines), fileName, new HashSet<string>(StringComparer.Ordinal));

    /// <summary>Parses one file; <paramref name="ids"/> carries the ids seen in the files read before, so an id is unique across all of them.</summary>
    public static IReadOnlyList<Quote> Parse(TextReader reader, string fileName, ISet<string> ids)
    {
        var quotes = new List<Quote>();
        string? fileLicense = null;
        var shared = new Dictionary<string, string>(StringComparer.Ordinal); // the same source and credit text on many records: one string each
        var number = 0;
        while (reader.ReadLine() is { } line)
        {
            number++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<Record>(line, Options) ?? throw new JsonException("empty record");
                var quote = Convert(record, shared);
                if (!ids.Add(quote.Info!.Id)) throw new JsonException($"duplicate id \"{quote.Info.Id}\"");
                fileLicense ??= quote.License;
                if (!string.Equals(fileLicense, quote.License, StringComparison.Ordinal))
                {
                    throw new JsonException($"a file holds one licence: found \"{quote.License}\" after \"{fileLicense}\"");
                }

                quotes.Add(quote);
            }
            catch (JsonException ex)
            {
                throw new JsonException($"{fileName} line {number}: {ex.Message}", ex);
            }
        }

        return quotes;
    }

    private static Quote Convert(Record r, Dictionary<string, string> shared)
    {
        var id = Require(r.Id, "id");
        var type = Require(r.Type, "type");
        if (!Types.TryGetValue(type, out var category)) throw new JsonException($"unknown type \"{type}\"");
        if (!string.Equals(Require(r.Lang, "lang"), "en", StringComparison.Ordinal)) throw new JsonException("only English headwords are supported");
        var text = Require(r.Text, "text");

        var source = r.Source ?? throw new JsonException("a record needs its \"source\"");
        var dataset = Intern(shared, Require(source.Dataset, "source.dataset"));
        var url = Require(source.Url, "source.url");
        Require(source.Retrieved, "source.retrieved");
        var license = Intern(shared, Require(source.License, "source.license"));
        if (!Licenses.TryGetValue(license, out var allowed)) throw new JsonException($"unknown licence \"{license}\"");
        var attributionText = Intern(shared, Require(source.AttributionText, "source.attribution_text"));

        var translation = r.Translations?.FirstOrDefault(t => string.Equals(t.Lang, "vi", StringComparison.Ordinal))
            ?? throw new JsonException("a record needs a Vietnamese translation");
        var vietnamese = Require(translation.Text, "translations.text");
        var statusText = Require(translation.Status, "translations.status");
        if (!Statuses.TryGetValue(statusText, out var status)) throw new JsonException($"unknown translation status \"{statusText}\"");
        var translationLicense = Require(translation.License, "translations.license");
        if (!allowed.Contains(translationLicense)) throw new JsonException($"a \"{license}\" record cannot carry a \"{translationLicense}\" translation");
        if (status != TranslationStatus.Source) Require(translation.By, "translations.by (who drafted it)");

        var meaning = r.Meanings?.FirstOrDefault(m => string.Equals(m.Lang, "en", StringComparison.Ordinal))?.Text?.Trim() ?? "";
        var example = r.Examples?.FirstOrDefault();
        var author = r.Author?.Name?.Trim();
        var work = r.Attribution?.Work?.Trim() ?? "";
        if (category is QuoteCategory.Quote or QuoteCategory.Maxim)
        {
            // Words given a name must be proven to be that person's, and long enough ago.
            Require(author, "author.name");
            if (r.Author!.Died is not { } died || died > LatestDeathYear) throw new JsonException($"the author of a quote must have died in {LatestDeathYear} or before");
            if (!string.Equals(r.Attribution?.Status, "verified", StringComparison.Ordinal)) throw new JsonException("a quote needs a verified attribution");
            Require(work, "attribution.work");
        }
        else if (meaning.Length == 0)
        {
            throw new JsonException($"a {type} needs its English meaning");
        }

        var info = new QuoteInfo(id, meaning, example?.En?.Trim() ?? "", example?.Vi?.Trim() ?? "", status, url, attributionText, work);
        return new Quote(text, vietnamese, category, string.IsNullOrWhiteSpace(author) ? null : Intern(shared, author), dataset, license, Info: info);
    }

    private static string Require(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new JsonException($"empty \"{field}\"") : value.Trim();

    private static string Intern(Dictionary<string, string> shared, string value) => shared.TryGetValue(value, out var known) ? known : shared[value] = value;

    private sealed class Record
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? Lang { get; set; }
        public string? Text { get; set; }
        public List<Meaning>? Meanings { get; set; }
        public List<Translation>? Translations { get; set; }
        public List<Example>? Examples { get; set; }
        public AuthorEntry? Author { get; set; }
        public AttributionEntry? Attribution { get; set; }
        public SourceEntry? Source { get; set; }
    }

    private sealed class Meaning
    {
        public string? Lang { get; set; }
        public string? Text { get; set; }
    }

    private sealed class Translation
    {
        public string? Lang { get; set; }
        public string? Text { get; set; }
        public string? Status { get; set; }
        public string? By { get; set; }
        public string? License { get; set; }
    }

    private sealed class Example
    {
        public string? En { get; set; }
        public string? Vi { get; set; }
    }

    private sealed class AuthorEntry
    {
        public string? Name { get; set; }
        public int? Died { get; set; }
    }

    private sealed class AttributionEntry
    {
        public string? Status { get; set; }
        public string? Work { get; set; }
    }

    private sealed class SourceEntry
    {
        public string? Dataset { get; set; }
        public string? Url { get; set; }
        public string? Retrieved { get; set; }
        public string? License { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("attribution_text")]
        public string? AttributionText { get; set; }
    }
}
