using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using PomoQuote.Mascot;
using PomoQuote.Pomodoro;
using PomoQuote.Quotes;
using PomoQuote.Weather;
using PomoQuote.Widget;

namespace PomoQuote.Tests;

/// <summary>The sentences from Tatoeba: their licence (CC BY 2.0 FR) asks for the author of each sentence, so every entry carries it.</summary>
public sealed class TatoebaSentenceTests
{
    private static IReadOnlyList<Quote> All { get; } = new BundledQuoteProvider().GetAll();

    private static IReadOnlyList<Quote> Tatoeba => All.Where(q => q.TatoebaId is not null).ToList();

    private static IReadOnlyList<Quote> Original => All.Where(q => q.TatoebaId is null && q.Info is null).ToList(); // the sentences of quotes.json that are not from Tatoeba

    private static string RepoRoot() => AppContext.BaseDirectory.Split("PomoQuote.Tests")[0];

    [Fact]
    public void The_original_357_sentences_and_their_groups_are_unchanged_and_the_Tatoeba_ones_are_added_to_them()
    {
        Assert.Equal(357, Original.Count);
        Assert.Equal(113, Original.Count(q => q.Category == QuoteCategory.Motivation));
        Assert.Equal(131, Original.Count(q => q.Category == QuoteCategory.Idiom));
        Assert.Equal(113, Original.Count(q => q.Category == QuoteCategory.Conversation));
        Assert.True(Tatoeba.Count >= 10000, $"only {Tatoeba.Count} Tatoeba sentences");
        Assert.Equal(All.Count, Original.Count + Tatoeba.Count + All.Count(q => q.Info is not null));
        // the first sentence of the file is still the first sentence of the first group
        Assert.Equal("A journey of a thousand miles begins with a single step.", All[0].English);
        Assert.All(Tatoeba, q => Assert.Equal(QuoteCategory.Conversation, q.Category)); // the existing categories only
    }

    [Fact]
    public void Every_Tatoeba_sentence_has_the_id_and_the_author_of_the_English_sentence_and_of_its_translation()
    {
        foreach (var quote in Tatoeba)
        {
            Assert.True(quote.TatoebaId > 0, quote.English);
            Assert.True(quote.TranslationId > 0, quote.English);
            Assert.False(string.IsNullOrWhiteSpace(quote.Author), quote.English);
            Assert.False(string.IsNullOrWhiteSpace(quote.TranslationAuthor), quote.English);
            Assert.Contains("CC BY 2.0 FR", quote.License);
            Assert.Contains("tatoeba.org", quote.Source);
            Assert.NotEqual(quote.TatoebaId, quote.TranslationId);
        }

        Assert.Equal(Tatoeba.Count, Tatoeba.Select(q => q.TatoebaId).Distinct().Count()); // one entry per English sentence
        Assert.Equal(Tatoeba.Count, Tatoeba.Select(q => q.TranslationId).Distinct().Count()); // and per Vietnamese sentence
    }

    [Fact]
    public void The_attribution_text_names_the_licence_the_ids_and_both_authors_and_only_Tatoeba_sentences_have_one()
    {
        var sample = Tatoeba[0];

        Assert.Contains("CC BY 2.0 FR", sample.Attribution);
        Assert.Contains($"#{sample.TatoebaId} by {sample.Author}", sample.Attribution);
        Assert.Contains($"#{sample.TranslationId} by {sample.TranslationAuthor}", sample.Attribution);
        Assert.All(Original, q => Assert.Null(q.Attribution));
    }

    [Fact]
    public void Tatoeba_sentences_are_short_enough_for_the_widget_and_have_a_real_translation()
    {
        foreach (var quote in Tatoeba)
        {
            Assert.InRange(quote.English.Length, 12, 100);
            Assert.InRange(quote.Vietnamese.Length, 6, 130);
            Assert.InRange(System.Text.RegularExpressions.Regex.Matches(quote.English.ToLowerInvariant(), @"[a-z]+(?:'[a-z]+)?|\d+").Count, 3, 20); // counted the way the import tool counts words
            Assert.NotEqual(quote.English, quote.Vietnamese);
        }
    }

    [Fact]
    public void Nothing_from_the_content_filters_word_lists_is_left_in_the_English_text()
    {
        var banned = new[] { "sex", "fuck", "shit", "kill", "murder", "suicide", "god", "jesus", "allah", "muslim", "president", "election", "terrorist", "hoang sa" };
        foreach (var quote in Tatoeba)
        {
            var words = quote.English.ToLowerInvariant();
            foreach (var word in banned) Assert.DoesNotMatch($@"\b{word}\b", words);
        }
    }

    [Fact]
    public void The_rotation_over_all_sentences_never_repeats_one_straight_away_and_shows_each_before_any_comes_again()
    {
        var rotator = new QuoteRotator(All.Where(q => q.Category == QuoteCategory.Conversation).ToList(), QuoteCategory.All, new Random(11)); // within one category: a pass through all of it
        var seen = new HashSet<string>();
        string? last = null;

        for (var i = 0; i < All.Count(q => q.Category == QuoteCategory.Conversation); i++)
        {
            var quote = rotator.Next()!;
            Assert.True(seen.Add(quote.English), $"shown twice in one pass: {quote.English}");
            Assert.NotEqual(last, quote.English);
            last = quote.English;
        }

        for (var i = 0; i < 2000; i++)
        {
            var quote = rotator.Next()!;
            Assert.NotEqual(last, quote.English); // also across the end of the pass
            last = quote.English;
        }
    }

    [Fact]
    public void The_loader_refuses_a_CC_BY_entry_without_its_ids_and_authors()
    {
        const string header = """{ "groups": [ { "category": "conversation", "source": "Tatoeba", "license": "CC BY 2.0 FR", "quotes": [ """;
        const string footer = " ] } ] }";

        Assert.ThrowsAny<JsonException>(() => QuoteCatalog.Parse(header + """{ "english": "Hi there.", "vietnamese": "Xin chào." }""" + footer));
        Assert.ThrowsAny<JsonException>(() => QuoteCatalog.Parse(header + """{ "id": 1, "author": "a", "english": "Hi there.", "vietnamese": "Xin chào.", "vid": 2 }""" + footer));
        Assert.ThrowsAny<JsonException>(() => QuoteCatalog.Parse(header + """{ "id": 1, "author": "a", "english": "Hi there.", "vietnamese": "Xin chào.", "vauthor": "b" }""" + footer));
        Assert.ThrowsAny<JsonException>(() => QuoteCatalog.Parse(header + """{ "id": 0, "author": "a", "english": "Hi there.", "vietnamese": "Xin chào.", "vid": 2, "vauthor": "b" }""" + footer));

        var quote = Assert.Single(QuoteCatalog.Parse(header + """{ "id": 1, "author": "a", "english": "Hi there.", "vietnamese": "Xin chào.", "vid": 2, "vauthor": "b" }""" + footer));
        Assert.Equal((1L, 2L, "a", "b"), (quote.TatoebaId, quote.TranslationId, quote.Author, quote.TranslationAuthor));
    }

    [Fact]
    public void An_entry_that_is_not_CC_BY_keeps_working_without_ids()
    {
        var quote = Assert.Single(QuoteCatalog.Parse("""{ "groups": [ { "category": "idiom", "source": "s", "license": "Public domain", "quotes": [ { "english": "A", "vietnamese": "a" } ] } ] }"""));

        Assert.Null(quote.TatoebaId);
        Assert.Null(quote.Attribution);
    }

    [Fact]
    public void The_credits_the_third_party_list_and_the_about_dialog_record_the_licence_and_the_attribution()
    {
        var credits = File.ReadAllText(Path.Combine(RepoRoot(), "Assets", "Quotes", "CREDITS.md"));
        var third = File.ReadAllText(Path.Combine(RepoRoot(), "THIRD_PARTY_ASSETS.md"));
        var report = File.ReadAllText(Path.Combine(RepoRoot(), "Assets", "Quotes", "tatoeba-import-report.md"));

        Assert.Contains("Tatoeba", credits);
        Assert.Contains("CC BY 2.0 FR", credits);
        Assert.Contains("https://creativecommons.org/licenses/by/2.0/fr/", credits);
        Assert.Contains("https://tatoeba.org/en/terms_of_use", credits);
        Assert.Contains($"{Tatoeba.Count}", credits);
        Assert.Contains("Tatoeba", third);
        Assert.Contains("CC BY 2.0 FR", third);
        Assert.Contains($"**{Tatoeba.Count}** pairs", report);
        Assert.Contains("Tatoeba", AboutDialog.SentencesNotice);
        Assert.Contains("CC BY 2.0 FR", AboutDialog.SentencesNotice);
    }

    [Fact]
    public void The_import_tool_is_in_the_tools_folder_and_the_app_never_reads_from_the_network_for_sentences()
    {
        var tool = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "import-tatoeba.mjs"));
        Assert.Contains("development tool", tool);

        var catalog = File.ReadAllText(Path.Combine(RepoRoot(), "Quotes", "QuoteCatalog.cs"));
        Assert.DoesNotContain("HttpClient", catalog);
        Assert.DoesNotContain("WebClient", catalog);
    }
}
/// <summary>The real window: a Tatoeba sentence carries its authors to the widget's tooltip and the normal window's tooltip.</summary>
[Collection("process")]
public sealed class TatoebaAttributionShownTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PomoQuoteTatoeba-" + Guid.NewGuid().ToString("N"));

    public TatoebaAttributionShownTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private sealed class OneQuote(Quote quote) : IQuoteProvider
    {
        public IReadOnlyList<Quote> GetAll() => [quote];
    }

    private sealed class NeverAnswers : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        }
    }

    private MainForm NewForm(Quote quote)
    {
        var http = new HttpClient(new NeverAnswers()) { Timeout = TimeSpan.FromMinutes(10) };
        return new MainForm(
            new WeatherService(http), new SettingsStore(Path.Combine(_root, "settings.json")), new PomodoroStore(Path.Combine(_root, "pomodoro.json")),
            TimeProvider.System, new WidgetStore(Path.Combine(_root, "widget.json")), new MascotStore(Path.Combine(_root, "mascot.json")),
            new QuoteSettingsStore(Path.Combine(_root, "quotes.json")), new OneQuote(quote), new Random(1),
            new PomoQuote.AiUsage.UsageSettingsStore(Path.Combine(_root, "usage.json")), new PomoQuote.AiUsage.UsageCacheStore(Path.Combine(_root, "usage-cache.json")), new PomoQuote.AiUsage.UsageAlertStore(Path.Combine(_root, "usage-alerts.json")),
            [], null, new WeatherEffectSettingsStore(Path.Combine(_root, "weather-effects.json")))
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-3000, -3000),
            WorkAreaProvider = () => [new Rectangle(-6000, -6000, 4000, 4000)],
        };
    }

    private static void OnStaThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(120)), "the form test did not finish");
        if (failure is not null) throw new InvalidOperationException("The form test failed: " + failure.Message, failure);
    }

    [Fact]
    public void A_Tatoeba_sentence_shows_its_authors_in_the_widget_and_a_sentence_of_the_app_shows_none()
    {
        OnStaThread(() =>
        {
            var tatoeba = new Quote("I have to go to sleep.", "Tôi phải đi ngủ.", QuoteCategory.Conversation, "vinhan", "Tatoeba (https://tatoeba.org)", "CC BY 2.0 FR", 1277, 5662, "evaZ");
            using (var form = NewForm(tatoeba))
            {
                form.Show();
                form.ShowNextQuote();

                Assert.Equal(tatoeba.Attribution, form.WidgetViewForTests.QuoteAttribution);
                Assert.Contains("vinhan", form.WidgetViewForTests.QuoteAttribution);
                Assert.Contains("evaZ", form.WidgetViewForTests.QuoteAttribution);
            }

            var own = new Quote("Fortune favors the bold.", "Vận may đứng về phía người dám làm.", QuoteCategory.Motivation, "Virgil", "s", "Public domain");
            using var other = NewForm(own);
            other.Show();
            other.ShowNextQuote();
            Assert.Null(other.WidgetViewForTests.QuoteAttribution);
        });
    }
}