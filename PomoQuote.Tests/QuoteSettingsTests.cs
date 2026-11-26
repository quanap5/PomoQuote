using PomoQuote.Quotes;

namespace PomoQuote.Tests;

public sealed class QuoteSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PomoQuoteTests-" + Guid.NewGuid().ToString("N"));

    private string StorePath => Path.Combine(_directory, "nested", "quotes.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private void WriteRaw(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, text);
    }

    [Fact]
    public void A_missing_file_gives_every_category_translation_on_and_one_sentence_per_phase()
    {
        var settings = new QuoteSettingsStore(StorePath).Load();

        Assert.Equal(QuoteSettings.Default, settings);
        Assert.Equal(QuoteCategory.All, settings.Categories);
        Assert.True(settings.ShowTranslation);
        Assert.Equal(QuoteInterval.PerPhase, settings.Interval);
        Assert.Null(settings.LastEnglish);
    }

    [Fact]
    public void Saved_choices_survive_a_reload_and_no_temp_file_is_left()
    {
        var saved = new QuoteSettings(QuoteCategory.Idiom | QuoteCategory.Conversation, false, QuoteInterval.Minutes30, "Haste makes waste.");

        new QuoteSettingsStore(StorePath).Save(saved);

        Assert.Equal(saved, new QuoteSettingsStore(StorePath).Load());
        Assert.False(File.Exists(StorePath + ".tmp"));
    }

    [Fact]
    public void The_file_is_readable_text_with_names_not_numbers()
    {
        new QuoteSettingsStore(StorePath).Save(new QuoteSettings(QuoteCategory.Motivation, true, QuoteInterval.Off));

        var text = File.ReadAllText(StorePath);

        Assert.Contains("\"Motivation\"", text);
        Assert.Contains("\"Off\"", text);
    }

    [Fact]
    public void Vietnamese_text_in_the_last_sentence_survives_a_reload()
    {
        var saved = QuoteSettings.Default with { LastEnglish = "Fall seven times, stand up eight." };

        new QuoteSettingsStore(StorePath).Save(saved);

        Assert.Equal(saved, new QuoteSettingsStore(StorePath).Load());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{ "Categories": "Poetry", "ShowTranslation": true, "Interval": "Off" }""")]
    [InlineData("""{ "Categories": "None", "ShowTranslation": true, "Interval": "Off" }""")]
    [InlineData("""{ "Categories": 999, "ShowTranslation": true, "Interval": "Off" }""")] // a number with a bit no category has
    [InlineData("""{ "Categories": "All", "ShowTranslation": true, "Interval": "Hourly" }""")]
    [InlineData("""{ "Categories": "All", "ShowTranslation": true, "Interval": 42 }""")]
    [InlineData("""{ "Categories": "All", "ShowTranslation": "yes", "Interval": "Off" }""")]
    public void A_damaged_or_invalid_file_falls_back_to_the_defaults(string content)
    {
        WriteRaw(content);

        Assert.Equal(QuoteSettings.Default, new QuoteSettingsStore(StorePath).Load());
    }

    [Fact]
    public void An_unreadable_path_falls_back_to_the_defaults()
    {
        Directory.CreateDirectory(StorePath); // a folder where the file should be

        Assert.Equal(QuoteSettings.Default, new QuoteSettingsStore(StorePath).Load());
    }

    [Theory]
    [InlineData(QuoteInterval.Off, null, false)]
    [InlineData(QuoteInterval.PerPhase, null, true)]
    [InlineData(QuoteInterval.Minutes5, 5, true)]
    [InlineData(QuoteInterval.Minutes10, 10, true)]
    [InlineData(QuoteInterval.Minutes30, 30, true)]
    [InlineData(QuoteInterval.Minutes60, 60, true)]
    public void Each_interval_maps_to_its_timer_period_and_to_phase_changes(QuoteInterval interval, int? minutes, bool changesOnPhase)
    {
        var settings = QuoteSettings.Default with { Interval = interval };

        Assert.Equal(minutes is null ? null : TimeSpan.FromMinutes(minutes.Value), settings.Period);
        Assert.Equal(changesOnPhase, settings.ChangesOnPhase);
    }
}
