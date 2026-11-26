using System.Reflection;
using System.Text.Json;

namespace PomoQuote.Quotes;

/// <summary>Where the sentences come from. Only the bundled offline provider exists; an online one could implement this later.</summary>
internal interface IQuoteProvider
{
    IReadOnlyList<Quote> GetAll();
}

/// <summary>The sentences bundled with the app (Quotes/quotes.json, embedded in the assembly).</summary>
internal sealed class BundledQuoteProvider : IQuoteProvider
{
    internal const string ResourceName = "PomoQuote.quotes.json";

    private readonly Lazy<IReadOnlyList<Quote>> _quotes = new(Load);

    public IReadOnlyList<Quote> GetAll() => _quotes.Value;

    /// <summary>The prefix of the embedded expression data files (one JSON Lines file per source and licence).</summary>
    internal const string ExpressionPrefix = "PomoQuote.expressions.";

    private static IReadOnlyList<Quote> Load()
    {
        var assembly = typeof(BundledQuoteProvider).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {ResourceName}.");
        var quotes = new List<Quote>(QuoteCatalog.Parse(stream)); // read from the stream: the 2.7 MB text is never held as one string

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ExpressionPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var data = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(data, System.Text.Encoding.UTF8);
            quotes.AddRange(ExpressionCatalog.Parse(reader, name[ExpressionPrefix.Length..], ids));
        }

        return quotes;
    }
}

/// <summary>
/// Reads the quote data file: a list of groups, each with one category, source and license shared by its
/// quotes. A quote may add its own author. Anything incomplete or invalid is an error, because the file ships
/// with the app and a bad entry is a bug to fix, not something to skip silently.
/// </summary>
internal static class QuoteCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<Quote> Parse(string json) =>
        Build(JsonSerializer.Deserialize<QuoteFile>(json, JsonOptions) ?? throw new JsonException("The quote file is empty."));

    /// <summary>The same as <see cref="Parse(string)"/> for UTF-8 text in a stream (BOM allowed): the text is deserialized in pieces, not read into one big string first.</summary>
    public static IReadOnlyList<Quote> Parse(Stream utf8Json) =>
        Build(JsonSerializer.Deserialize<QuoteFile>(utf8Json, JsonOptions) ?? throw new JsonException("The quote file is empty."));

    private static IReadOnlyList<Quote> Build(QuoteFile file)
    {
        var quotes = new List<Quote>();
        var names = new Dictionary<string, string>(StringComparer.Ordinal); // the same author appears on many sentences: one string for each
        foreach (var group in file.Groups ?? [])
        {
            var category = ParseCategory(group.Category);
            Require(group.Source, "source");
            Require(group.License, "license");
            var source = group.Source!.Trim(); // one string for the whole group
            var license = group.License!.Trim();
            // A licence that asks for attribution (Tatoeba's CC BY) needs the id and the author of the English sentence and of its translation on every entry.
            var needsAttribution = group.License!.Contains("CC BY", StringComparison.OrdinalIgnoreCase);
            foreach (var entry in group.Quotes ?? [])
            {
                Require(entry.English, "english");
                Require(entry.Vietnamese, "vietnamese");
                if (needsAttribution)
                {
                    Require(entry.Author, "author");
                    Require(entry.Vauthor, "vauthor");
                    if (entry.Id is not > 0 || entry.Vid is not > 0) throw new JsonException($"A CC BY entry needs its sentence ids (id and vid): {entry.English}");
                }

                var author = string.IsNullOrWhiteSpace(entry.Author) ? group.Author : entry.Author;
                quotes.Add(new Quote(entry.English!.Trim(), entry.Vietnamese!.Trim(), category,
                    string.IsNullOrWhiteSpace(author) ? null : Intern(names, author.Trim()), source, license,
                    needsAttribution ? entry.Id : null, needsAttribution ? entry.Vid : null, needsAttribution ? Intern(names, entry.Vauthor!.Trim()) : null));
            }
        }

        return quotes;
    }

    private static string Intern(Dictionary<string, string> names, string value) => names.TryGetValue(value, out var known) ? known : names[value] = value;

    private static QuoteCategory ParseCategory(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "motivation" => QuoteCategory.Motivation,
        "idiom" => QuoteCategory.Idiom,
        "conversation" => QuoteCategory.Conversation,
        _ => throw new JsonException($"Unknown category \"{text}\"."),
    };

    private static void Require(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new JsonException($"A quote or group has an empty \"{field}\".");
    }

    private sealed class QuoteFile
    {
        public List<GroupEntry>? Groups { get; set; }
    }

    private sealed class GroupEntry
    {
        public string? Category { get; set; }
        public string? Source { get; set; }
        public string? License { get; set; }
        public string? Author { get; set; }
        public List<QuoteEntry>? Quotes { get; set; }
    }

    private sealed class QuoteEntry
    {
        public string? English { get; set; }
        public string? Vietnamese { get; set; }
        public string? Author { get; set; }

        /// <summary>The Tatoeba id of the English sentence, the id of its Vietnamese translation and that translation's author (Tatoeba entries only).</summary>
        public long? Id { get; set; }
        public long? Vid { get; set; }
        public string? Vauthor { get; set; }
    }
}
