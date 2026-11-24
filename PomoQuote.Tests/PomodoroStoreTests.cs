using PomoQuote.Pomodoro;

namespace PomoQuote.Tests;

public sealed class PomodoroStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PomoQuoteTests-" + Guid.NewGuid().ToString("N"));

    private string StorePath => Path.Combine(_directory, "nested", "pomodoro.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Missing_file_gives_the_defaults()
    {
        var data = new PomodoroStore(StorePath).Load();

        Assert.Equal(PomodoroData.Default, data);
        Assert.Equal(PomodoroSettings.Default, data.Settings);
        Assert.Equal(0, data.CompletedToday);
    }

    [Fact]
    public void Saved_data_survives_a_reload_and_creates_the_folder()
    {
        var store = new PomodoroStore(StorePath);
        var data = new PomodoroData(new PomodoroSettings(50, 10, 30, 3, false, false), "2026-11-23", 7);

        store.Save(data);

        Assert.Equal(data, new PomodoroStore(StorePath).Load());
        Assert.False(File.Exists(StorePath + ".tmp"));
    }

    [Fact]
    public void Saving_again_replaces_the_previous_data()
    {
        var store = new PomodoroStore(StorePath);
        store.Save(new PomodoroData(PomodoroSettings.Default, "2026-11-22", 1));
        var newer = new PomodoroData(new PomodoroSettings(30), "2026-11-23", 2);

        store.Save(newer);

        Assert.Equal(newer, store.Load());
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"Settings":null}""")]
    [InlineData("""{"Settings":{"WorkMinutes":0}}""")]
    [InlineData("""{"Settings":{"WorkMinutes":1000}}""")]
    [InlineData("""{"Settings":{"SessionsBeforeLongBreak":99}}""")]
    [InlineData("""{"Settings":{"WorkMinutes":"lots"}}""")]
    [InlineData("""{"Settings":{},"CompletedToday":-4}""")]
    public void Corrupt_or_invalid_file_falls_back_to_the_defaults(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, content);

        Assert.Equal(PomodoroData.Default, new PomodoroStore(StorePath).Load());
    }

    [Fact]
    public void Missing_fields_use_the_default_values()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, """{"Settings":{"WorkMinutes":40}}""");

        var data = new PomodoroStore(StorePath).Load();

        Assert.Equal(new PomodoroSettings(WorkMinutes: 40), data.Settings);
    }

    [Fact]
    public void Unreadable_path_falls_back_to_the_defaults()
    {
        Directory.CreateDirectory(StorePath); // a folder where the file should be

        Assert.Equal(PomodoroData.Default, new PomodoroStore(StorePath).Load());
    }
}

public class DailyCounterTests
{
    private static readonly DateOnly Monday = new(2026, 11, 23);

    [Fact]
    public void New_counter_is_zero()
    {
        Assert.Equal(0, new DailyCounter().CountFor(Monday));
    }

    [Fact]
    public void Counts_up_within_a_day()
    {
        var counter = new DailyCounter();

        counter.Increment(Monday);
        counter.Increment(Monday);

        Assert.Equal(2, counter.CountFor(Monday));
        Assert.Equal("2026-11-23", counter.Day);
    }

    [Fact]
    public void A_new_day_starts_again_from_zero()
    {
        var counter = new DailyCounter("2026-11-23", 5);

        Assert.Equal(0, counter.CountFor(Monday.AddDays(1)));
        counter.Increment(Monday.AddDays(1));

        Assert.Equal(1, counter.CountFor(Monday.AddDays(1)));
        Assert.Equal("2026-11-24", counter.Day);
    }

    [Fact]
    public void A_stored_count_from_an_earlier_day_reads_as_zero_today()
    {
        Assert.Equal(0, new DailyCounter("2026-11-22", 9).CountFor(Monday));
    }

    [Fact]
    public void Negative_stored_counts_are_treated_as_zero()
    {
        Assert.Equal(0, new DailyCounter("2026-11-23", -3).CountFor(Monday));
    }
}
