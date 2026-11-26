using System.Drawing;
using System.Windows.Forms;
using PomoQuote.Mascot;
using PomoQuote.Pomodoro;
using PomoQuote.Quotes;
using PomoQuote.Weather;
using PomoQuote.Widget;

namespace PomoQuote.Tests;

/// <summary>The new categories in the settings, and the "not reviewed" note and tooltip of a machine translation in both windows.</summary>
public sealed class ExpressionUiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PomoQuoteTests-" + Guid.NewGuid().ToString("N"));

    public ExpressionUiTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static Quote Record(TranslationStatus status) => new(
        "break the ice", "phá vỡ sự ngại ngùng", QuoteCategory.Idiom, null, "English Wiktionary", "CC-BY-SA-4.0",
        Info: new QuoteInfo("idiom-1", "To start a conversation.", "", "", status, "https://en.wiktionary.org/wiki/break_the_ice", "From English Wiktionary", ""));

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

    private MainForm NewForm(Quote quote, bool translation = true)
    {
        var store = new QuoteSettingsStore(Path.Combine(_root, "quotes.json"));
        store.Save(QuoteSettings.Default with { ShowTranslation = translation });
        var http = new HttpClient(new NeverAnswers()) { Timeout = TimeSpan.FromMinutes(10) };
        return new MainForm(
            new WeatherService(http), new SettingsStore(Path.Combine(_root, "settings.json")), new PomodoroStore(Path.Combine(_root, "pomodoro.json")),
            TimeProvider.System, new WidgetStore(Path.Combine(_root, "widget.json")), new MascotStore(Path.Combine(_root, "mascot.json")),
            store, new OneQuote(quote), new Random(1),
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

    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>().SelectMany(child => Descendants(child).Prepend(child));

    [Fact]
    public void A_machine_translation_is_marked_in_both_windows_and_its_tooltip_gives_meaning_and_credit()
    {
        OnStaThread(() =>
        {
            var quote = Record(TranslationStatus.MachineUnreviewed);
            using var form = NewForm(quote);
            form.Show();
            form.ShowNextQuote();

            Assert.Equal("machine translation, not reviewed", form.WidgetViewForTests.QuoteBadge);
            Assert.Equal(quote.Details, form.WidgetViewForTests.QuoteAttribution);
            Assert.Contains("From English Wiktionary (CC-BY-SA-4.0).", form.WidgetViewForTests.QuoteAttribution);
            var label = Assert.Single(Descendants(form).OfType<QuoteLabel>());
            Assert.Equal("machine translation, not reviewed", label.Badge);
            Assert.EndsWith("(machine translation, not reviewed)", label.Text); // read out by assistive technology too
        });
    }

    [Theory]
    [InlineData("MachineUnreviewed", false)]
    [InlineData("Source", true)]
    [InlineData("SampledOk", true)]
    [InlineData("HumanReviewed", true)]
    public void There_is_no_note_without_a_translation_on_screen_or_when_a_person_checked_it(string status, bool translationShown)
    {
        OnStaThread(() =>
        {
            using var form = NewForm(Record(Enum.Parse<TranslationStatus>(status)), translationShown);
            form.Show();
            form.ShowNextQuote();

            Assert.Null(form.WidgetViewForTests.QuoteBadge);
            Assert.Null(Assert.Single(Descendants(form).OfType<QuoteLabel>()).Badge);
        });
    }

    [Fact]
    public void The_widget_card_grows_for_the_note_and_shrinks_again()
    {
        OnStaThread(() =>
        {
            using var form = NewForm(Record(TranslationStatus.Source));
            form.Show();
            form.SetWidgetMode(true);
            var view = form.WidgetViewForTests;
            view.SetQuote("break the ice", "phá vỡ sự ngại ngùng");
            var plain = view.LogicalHeight;

            view.SetQuote("break the ice", "phá vỡ sự ngại ngùng", "credit", "machine translation, not reviewed");
            var marked = view.LogicalHeight;
            using var bitmap = new Bitmap(view.Width, view.Height);
            view.DrawToBitmap(bitmap, new Rectangle(Point.Empty, view.Size)); // the note draws without an error
            view.SetQuote("break the ice", "phá vỡ sự ngại ngùng");

            Assert.Equal(plain + 13, marked);
            Assert.Equal(plain, view.LogicalHeight);
        });
    }

    [Fact]
    public void The_settings_dialog_offers_every_category_once()
    {
        var categories = Enum.GetValues<QuoteCategory>().Where(c => c is not (QuoteCategory.None or QuoteCategory.All)).ToList();

        Assert.Equal(categories.Order(), QuoteSettingsDialog.CategoryChoices.Select(c => c.Value).Order());
        Assert.Equal(QuoteCategory.All, categories.Aggregate((all, one) => all | one));
        Assert.Equal(QuoteSettingsDialog.CategoryChoices.Length, QuoteSettingsDialog.CategoryChoices.Select(c => c.Caption).Distinct().Count());
    }

    [Theory]
    [InlineData(QuoteCategory.All)]
    [InlineData(QuoteCategory.Proverb)]
    [InlineData(QuoteCategory.Motivation | QuoteCategory.PhrasalVerb | QuoteCategory.Maxim)]
    public void The_settings_dialog_ticks_and_returns_the_chosen_categories(QuoteCategory chosen)
    {
        OnStaThread(() =>
        {
            var current = QuoteSettings.Default with { Categories = chosen };
            using var dialog = new QuoteSettingsDialog(current);
            var boxes = Descendants(dialog).OfType<CheckBox>().ToList();
            Assert.All(QuoteSettingsDialog.CategoryChoices, choice => Assert.Equal((chosen & choice.Value) != 0, boxes.Single(b => b.Text == choice.Caption).Checked));
            Assert.Equal(chosen == QuoteCategory.All, boxes.Single(b => b.Text == "All categories").Checked);

            Assert.Equal(chosen, dialog.ValueFrom(current).Categories);
        });
    }

    [Theory]
    [InlineData("\"All\"", QuoteCategory.All)]
    [InlineData("\"Motivation, Idiom, Conversation\"", QuoteCategory.Motivation | QuoteCategory.Idiom | QuoteCategory.Conversation)]
    [InlineData("\"Conversation\"", QuoteCategory.Conversation)]
    [InlineData("\"Proverb, PhrasalVerb\"", QuoteCategory.Proverb | QuoteCategory.PhrasalVerb)]
    public void A_saved_All_includes_the_new_categories_and_a_saved_list_stays_as_it_was(string saved, QuoteCategory expected)
    {
        var path = Path.Combine(_root, "saved.json");
        File.WriteAllText(path, $$"""{ "Categories": {{saved}}, "ShowTranslation": true, "Interval": "Off" }""");

        Assert.Equal(expected, new QuoteSettingsStore(path).Load().Categories);
    }
}
