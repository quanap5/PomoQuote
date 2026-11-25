using System.Text;
using PomoQuote.Quotes;

namespace PomoQuote.Tests;

/// <summary>Checks the bundled sentence data (Assets/Quotes/quotes.json): the file itself and what the app loads from it.</summary>
public sealed class QuoteDataTests
{
    private static readonly string RepoFile = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "Assets", "Quotes", "quotes.json"));

    private static IReadOnlyList<Quote> Load() => new BundledQuoteProvider().GetAll();

    [Fact]
    public void The_data_file_is_strict_utf8_without_a_byte_order_mark()
    {
        var bytes = File.ReadAllBytes(RepoFile);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        Assert.DoesNotContain('�', text);
    }

    [Fact]
    public void The_embedded_copy_is_the_file_in_the_repository()
    {
        using var stream = typeof(BundledQuoteProvider).Assembly.GetManifestResourceStream(BundledQuoteProvider.ResourceName)!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        Assert.Equal(File.ReadAllBytes(RepoFile), memory.ToArray());
    }

    [Fact]
    public void There_are_at_least_three_hundred_sentences_with_a_good_share_in_each_category()
    {
        var quotes = Load();

        Assert.True(quotes.Count >= 300, $"only {quotes.Count} sentences");
        foreach (var category in new[] { QuoteCategory.Motivation, QuoteCategory.Idiom, QuoteCategory.Conversation })
        {
            Assert.True(quotes.Count(quote => quote.Category == category) >= 60, $"too few {category} sentences");
        }
    }

    [Fact]
    public void Every_sentence_has_text_a_translation_a_known_category_a_source_and_a_license()
    {
        foreach (var quote in Load())
        {
            Assert.False(string.IsNullOrWhiteSpace(quote.English));
            Assert.False(string.IsNullOrWhiteSpace(quote.Vietnamese), quote.English);
            Assert.False(string.IsNullOrWhiteSpace(quote.Source), quote.English);
            Assert.False(string.IsNullOrWhiteSpace(quote.License), quote.English);
            Assert.True(quote.Category is QuoteCategory.Motivation or QuoteCategory.Idiom or QuoteCategory.Conversation, quote.English);
            Assert.True(quote.English.Length <= 140, $"too long to show: {quote.English}");
            Assert.True(quote.Vietnamese.Length <= 200, $"translation too long to show: {quote.English}");
        }
    }

    [Fact]
    public void Sentences_and_translations_are_unique_and_the_translation_is_not_a_copy()
    {
        var quotes = Load();

        Assert.Equal(quotes.Count, quotes.Select(quote => quote.English).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(quotes.Count, quotes.Select(quote => quote.Vietnamese).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(quotes, quote => Assert.NotEqual(quote.English, quote.Vietnamese));
    }

    [Fact]
    public void Vietnamese_text_is_normalized_and_has_the_diacritics_of_the_alphabet()
    {
        var quotes = Load();

        Assert.All(quotes, quote => Assert.True(quote.Vietnamese.IsNormalized(NormalizationForm.FormC), quote.English));

        // The letters with their own shape and all five tone marks occur somewhere in the data.
        var all = string.Concat(quotes.Select(quote => quote.Vietnamese)).ToLowerInvariant();
        foreach (var letter in "ăâêôơưđ") Assert.Contains(letter, all);

        var decomposed = all.Normalize(NormalizationForm.FormD);
        foreach (var toneMark in new[] { '̀', '́', '̉', '̃', '̣' })
        {
            Assert.Contains(toneMark, decomposed);
        }
    }

    [Fact]
    public void English_sentences_use_plain_characters_and_translations_have_no_stray_markup()
    {
        foreach (var quote in Load())
        {
            Assert.All(quote.English, ch => Assert.True(ch < 0x80, $"non-ASCII character in: {quote.English}"));
            Assert.DoesNotContain("  ", quote.English);
            Assert.DoesNotContain("  ", quote.Vietnamese);
            Assert.Equal(quote.English.Trim(), quote.English);
            Assert.Equal(quote.Vietnamese.Trim(), quote.Vietnamese);
        }
    }

    [Fact]
    public void Every_sentence_names_a_public_domain_or_project_license_or_Tatoeba_s_CC_BY_with_its_attribution()
    {
        foreach (var quote in Load())
        {
            var open = quote.License.Contains("public domain", StringComparison.OrdinalIgnoreCase) || quote.License.Contains("CC0", StringComparison.Ordinal);
            var tatoeba = quote.License.Contains("CC BY 2.0 FR", StringComparison.Ordinal) && quote.Attribution is not null;
            Assert.True(open || tatoeba, quote.English);
        }
    }

    [Fact]
    public void An_incomplete_entry_is_reported_instead_of_skipped()
    {
        const string noTranslation = """{ "groups": [ { "category": "idiom", "source": "s", "license": "l", "quotes": [ { "english": "Hi" } ] } ] }""";
        const string badCategory = """{ "groups": [ { "category": "poetry", "source": "s", "license": "l", "quotes": [] } ] }""";
        const string noLicense = """{ "groups": [ { "category": "idiom", "source": "s", "quotes": [ { "english": "Hi", "vietnamese": "Chào" } ] } ] }""";

        Assert.ThrowsAny<System.Text.Json.JsonException>(() => QuoteCatalog.Parse(noTranslation));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => QuoteCatalog.Parse(badCategory));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => QuoteCatalog.Parse(noLicense));
    }

    [Fact]
    public void A_group_author_is_inherited_and_an_entry_author_wins()
    {
        const string json = """
            { "groups": [ { "category": "motivation", "source": "s", "license": "l", "author": "Group",
              "quotes": [ { "english": "A", "vietnamese": "a" }, { "english": "B", "vietnamese": "b", "author": "Own" } ] } ] }
            """;

        var quotes = QuoteCatalog.Parse(json);

        Assert.Equal("Group", quotes[0].Author);
        Assert.Equal("Own", quotes[1].Author);
    }
}
