using System.Text.Json;
using PomoQuote.Quotes;

namespace PomoQuote.Tests;

/// <summary>The bundled quotes and life maxims: an author long dead, a named work, a public-domain licence, and the note on the translation.</summary>
public sealed class PublicDomainDataTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PomoQuote.csproj"))) directory = directory.Parent;
        return directory!.FullName;
    }

    private static string Text(params string[] path) => File.ReadAllText(Path.Combine([RepoRoot(), .. path]));

    private static IEnumerable<JsonElement> Records() => new[] { "public-domain-quotes.jsonl", "public-domain-maxims.jsonl" }
        .SelectMany(file => File.ReadLines(Path.Combine(RepoRoot(), "Assets", "Quotes", "data", file)))
        .Select(line => JsonDocument.Parse(line).RootElement);

    [Theory]
    [InlineData("public-domain-quotes.jsonl", QuoteCategory.Quote, 583)]
    [InlineData("public-domain-maxims.jsonl", QuoteCategory.Maxim, 600)]
    public void Each_file_holds_its_type_with_author_work_and_a_marked_translation(string file, QuoteCategory category, int count)
    {
        var quotes = ExpressionCatalog.Parse(Text("Assets", "Quotes", "data", file), file);

        Assert.Equal(count, quotes.Count);
        Assert.All(quotes, quote =>
        {
            Assert.Equal(category, quote.Category);
            Assert.Equal("Public-Domain", quote.License);
            Assert.False(string.IsNullOrWhiteSpace(quote.Author), quote.English);
            Assert.NotEmpty(quote.Info!.Work);
            Assert.Equal("machine translation, not reviewed", quote.Info.Badge);
            Assert.Contains(quote.Author!, quote.Details);
            Assert.Contains(quote.Info.Work, quote.Details);
            Assert.InRange(quote.English.Length, 35, 130);
            Assert.InRange(quote.Vietnamese.Length, 8, 180);
        });
    }

    [Fact]
    public void Every_author_and_translator_died_in_1955_or_before_and_the_translation_is_CC0()
    {
        Assert.All(Records(), record =>
        {
            var author = record.GetProperty("author");
            Assert.True(author.GetProperty("died").GetInt32() <= 1955);
            if (author.TryGetProperty("translator", out _)) Assert.True(author.GetProperty("translator_died").GetInt32() <= 1955);
            Assert.Equal("CC0-1.0", record.GetProperty("translations")[0].GetProperty("license").GetString());
            Assert.StartsWith("https://www.wikidata.org/wiki/Q", record.GetProperty("source").GetProperty("url").GetString());
        });
    }

    /// <summary>The archive the book files came from asks that its name does not travel with extracted public-domain text.</summary>
    [Fact]
    public void The_shipped_data_does_not_carry_the_name_of_the_text_archive()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot(), "Assets", "Quotes", "data"), "public-domain-*.jsonl"))
        {
            Assert.DoesNotContain("gutenberg", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Every_shipped_work_is_one_of_the_listed_books()
    {
        var books = Text("tools", "public-domain-works.mjs");

        Assert.All(Records(), record =>
        {
            var work = record.GetProperty("attribution").GetProperty("work").GetString()!;
            Assert.Contains(work, books);
        });
    }

    [Fact]
    public void The_licence_file_the_third_party_list_and_the_about_dialog_say_what_the_files_are()
    {
        foreach (var text in new[] { Text("Assets", "Quotes", "data", "LICENSE.md"), Text("THIRD_PARTY_ASSETS.md"), AboutDialog.QuotesNotice })
        {
            Assert.Contains("public domain", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CC0 1.0", text);
        }
    }

    [Fact]
    public void With_all_three_parts_every_category_has_sentences()
    {
        var all = new BundledQuoteProvider().GetAll();

        Assert.All(QuoteSettingsDialog.CategoryChoices, choice => Assert.Contains(all, q => q.Category == choice.Value));
        Assert.Equal(14034 + 3356 + 1183, all.Count);
    }
}
