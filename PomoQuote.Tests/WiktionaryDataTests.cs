using System.Text;
using System.Text.Json;
using PomoQuote.Quotes;

namespace PomoQuote.Tests;

/// <summary>The bundled Wiktionary entries: complete, one licence, credited, marked when no person reviewed the translation.</summary>
public sealed class WiktionaryDataTests
{
    private static IReadOnlyList<Quote> All { get; } = new BundledQuoteProvider().GetAll();

    private static IReadOnlyList<Quote> Entries => All.Where(q => q.Info?.Id.StartsWith("enwikt:", StringComparison.Ordinal) == true).ToList();

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PomoQuote.csproj"))) directory = directory.Parent;
        return directory!.FullName;
    }

    private static string Text(params string[] path) => File.ReadAllText(Path.Combine([RepoRoot(), .. path]));

    [Fact]
    public void Every_data_file_of_the_repository_is_embedded_unchanged_and_is_strict_utf8()
    {
        var assembly = typeof(BundledQuoteProvider).Assembly;
        var files = Directory.GetFiles(Path.Combine(RepoRoot(), "Assets", "Quotes", "data"), "*.jsonl");
        var resources = assembly.GetManifestResourceNames().Where(n => n.StartsWith(BundledQuoteProvider.ExpressionPrefix, StringComparison.Ordinal)).ToList();

        Assert.Equal(files.Length, resources.Count);
        foreach (var file in files)
        {
            using var stream = assembly.GetManifestResourceStream(BundledQuoteProvider.ExpressionPrefix + Path.GetFileName(file))!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = File.ReadAllBytes(file);
            Assert.Equal(bytes, memory.ToArray());
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, file);
            _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
    }

    [Theory]
    [InlineData("wiktionary-idioms.jsonl", QuoteCategory.Idiom, 1441)]
    [InlineData("wiktionary-phrasal-verbs.jsonl", QuoteCategory.PhrasalVerb, 778)]
    [InlineData("wiktionary-expressions.jsonl", QuoteCategory.Expression, 775)]
    [InlineData("wiktionary-proverbs.jsonl", QuoteCategory.Proverb, 248)]
    public void Each_file_holds_its_type_under_CC_BY_SA_with_its_page_and_credit(string file, QuoteCategory category, int count)
    {
        var quotes = ExpressionCatalog.Parse(Text("Assets", "Quotes", "data", file), file);

        Assert.Equal(count, quotes.Count);
        Assert.All(quotes, quote =>
        {
            Assert.Equal(category, quote.Category);
            Assert.Equal("CC-BY-SA-4.0", quote.License);
            Assert.StartsWith("https://en.wiktionary.org/wiki/", quote.Info!.SourceUrl);
            Assert.Contains("Wiktionary contributors", quote.Info.AttributionText);
            Assert.NotEmpty(quote.Info.Meaning);
            Assert.True(quote.Info.Status is TranslationStatus.MachineUnreviewed or TranslationStatus.Source, quote.English);
            Assert.Equal(quote.Info.Status == TranslationStatus.MachineUnreviewed, quote.Info.Badge is not null);
        });
    }

    /// <summary>A translated example is covered by the note on the entry, so an entry whose translation is the dictionary's own carries none.</summary>
    [Fact]
    public void Machine_translated_text_appears_only_in_entries_marked_as_machine_translated()
    {
        var source = Entries.Where(q => q.Info!.Status == TranslationStatus.Source).ToList();

        Assert.NotEmpty(source);
        Assert.All(source, q => Assert.Empty(q.Info!.ExampleVietnamese));
    }

    [Fact]
    public void Translations_are_one_plain_rendering_each_and_fit_the_widget()
    {
        Assert.All(Entries, quote =>
        {
            Assert.InRange(quote.Vietnamese.Length, 2, 110);
            Assert.DoesNotContain(quote.Vietnamese, c => "()[]{}<>\"/\\|_*#@=~^;".Contains(c));
            Assert.True(quote.English.Length <= 60, quote.English);
            Assert.True(quote.Info!.Meaning.Length <= 200, quote.English);
            Assert.True(quote.Info.ExampleEnglish.Length <= 100 && quote.Info.ExampleVietnamese.Length <= 200, quote.English);
            Assert.True(quote.Info.ExampleVietnamese.IsNormalized(NormalizationForm.FormC), quote.English);
        });
    }

    /// <summary>A guard on the result, not the filter itself (tools/expression-content-filter.mjs has the full lists).</summary>
    [Fact]
    public void No_entry_carries_vulgar_or_adult_words()
    {
        var blocked = new System.Text.RegularExpressions.Regex(
            @"\b(fuck\w*|shit\w*|bitch\w*|cunt|whore|slut|porn\w*|sexual\w*|sex|penis|vagina|orgasm|masturbat\w*|prostitut\w*|nigger|faggot|bastard|asshole)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        Assert.All(Entries, q => Assert.DoesNotMatch(blocked, $"{q.English} | {q.Info!.Meaning} | {q.Info.ExampleEnglish}"));
    }

    [Fact]
    public void The_sentences_that_were_there_before_are_all_still_there()
    {
        Assert.Equal(14034, All.Count(q => q.Info is null));
        Assert.Equal(3242, Entries.Count);
    }

    [Fact]
    public void The_licence_travels_with_the_data_and_the_credits_and_the_about_dialog_name_it()
    {
        foreach (var text in new[] { Text("Assets", "Quotes", "data", "LICENSE.md"), Text("Assets", "Quotes", "CREDITS.md"), Text("THIRD_PARTY_ASSETS.md"), AboutDialog.ExpressionsNotice })
        {
            Assert.Contains("Wiktionary", text);
            Assert.Contains("CC BY-SA 4.0", text.Replace("Creative Commons Attribution-ShareAlike 4.0 International", "CC BY-SA 4.0"));
        }

        Assert.Contains(@"Assets\Quotes\data\LICENSE.md", Text("PomoQuote.csproj"));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Quotes", "data", "LICENSE.md")));
    }

    /// <summary>The owner's decision: a translation the back-translation check doubted does not ship, and a new import run does not bring it back.</summary>
    [Fact]
    public void No_entry_the_check_doubted_ships_and_the_build_keeps_them_out()
    {
        var dropped = JsonDocument.Parse(Text("tools", "wiktionary-dropped.json")).RootElement.GetProperty("ids").EnumerateArray().Select(id => id.GetString()!).ToHashSet();
        Assert.Equal(114, dropped.Count);
        Assert.DoesNotContain(Entries, q => dropped.Contains(q.Info!.Id));

        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot(), "Assets", "Quotes", "data"), "wiktionary-*.jsonl"))
        {
            Assert.All(File.ReadLines(file), line => Assert.False(JsonDocument.Parse(line).RootElement.TryGetProperty("flags", out _), line));
        }

        var build = Text("tools", "wiktionary-build.mjs");
        Assert.Contains("wiktionary-dropped.json", build);
        Assert.Contains("r.check.verdict === 'ok' && !DROPPED.has(r.id)", build);
    }
}
