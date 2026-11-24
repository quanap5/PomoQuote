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

    private static IReadOnlyList<Quote> Load()
    {
        using var stream = typeof(BundledQuoteProvider).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource {ResourceName}.");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return QuoteCatalog.Parse(reader.ReadToEnd());
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

    public static IReadOnlyList<Quote> Parse(string json)
    {
        var file = JsonSerializer.Deserialize<QuoteFile>(json, JsonOptions)
            ?? throw new JsonException("The quote file is empty.");
        var quotes = new List<Quote>();
        foreach (var group in file.Groups ?? [])
        {
            var category = ParseCategory(group.Category);
            Require(group.Source, "source");
            Require(group.License, "license");
            foreach (var entry in group.Quotes ?? [])
            {
                Require(entry.English, "english");
                Require(entry.Vietnamese, "vietnamese");
                var author = string.IsNullOrWhiteSpace(entry.Author) ? group.Author : entry.Author;
                quotes.Add(new Quote(entry.English!.Trim(), entry.Vietnamese!.Trim(), category,
                    string.IsNullOrWhiteSpace(author) ? null : author.Trim(), group.Source!.Trim(), group.License!.Trim()));
            }
        }

        return quotes;
    }

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
    }
}
