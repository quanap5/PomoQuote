using PomoQuote.AiUsage;

namespace PomoQuote.Tests;

public sealed class UsageViewTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly UsageSettings Settings = UsageSettings.Default.WithEnabled("codex", true);

    private static UsageStatus Status(UsageSnapshot? latest, UsageSnapshot? lastGood = null) =>
        new("codex", "Codex", latest, lastGood ?? (latest is { State: UsageState.Ok } ? latest : null));

    private static UsageProviderView Build(UsageStatus status, DateTimeOffset? now = null, UsageSettings? settings = null) =>
        UsageViewBuilder.Build(status, settings ?? Settings, now ?? UsageTestData.Now.AddMinutes(1), Utc);

    [Fact]
    public void A_fresh_read_shows_both_windows_with_percent_bar_fraction_and_countdown()
    {
        var view = Build(Status(UsageTestData.Snapshot(21, 27)));

        Assert.Equal("Codex", view.Name);
        Assert.Equal("plus", view.Plan);
        Assert.False(view.IsProblem);
        Assert.Equal("updated 06:00", view.Note);
        Assert.Equal(["5h", "Week"], view.Rows.Select(r => r.Label).ToArray());
        Assert.Equal("21%", view.Rows[0].Percent);
        Assert.Equal(0.21, view.Rows[0].Fraction, 3);
        Assert.Equal("2h 59m", view.Rows[0].Reset);
        Assert.Equal("2d 23h", view.Rows[1].Reset);
        Assert.Equal(UsageLevel.Normal, view.Rows[0].Level);
    }

    [Fact]
    public void Data_older_than_twice_the_interval_is_marked_stale_and_still_shows_its_numbers()
    {
        var view = Build(Status(UsageTestData.Snapshot(21, 27)), UsageTestData.Now.AddMinutes(10));

        Assert.True(view.IsProblem);
        Assert.Equal("stale, from 06:00", view.Note);
        Assert.Equal(2, view.Rows.Count);
    }

    [Theory]
    [InlineData(UsageState.NotInstalled, "not installed")]
    [InlineData(UsageState.NotLoggedIn, "not logged in")]
    [InlineData(UsageState.Unsupported, "unsupported version")]
    [InlineData(UsageState.Error, "read failed")]
    public void A_failed_read_without_earlier_numbers_shows_only_the_reason(UsageState state, string expected)
    {
        var view = Build(Status(UsageSnapshot.Empty(UsageTestData.Source(), UsageTestData.Now, UsageDataSource.CodexAppServer, state, "x")));

        Assert.Equal(expected, view.Note);
        Assert.True(view.IsProblem);
        Assert.Empty(view.Rows);
    }

    [Fact]
    public void A_failed_read_after_a_good_one_keeps_the_numbers_and_says_when_they_are_from()
    {
        var good = UsageTestData.Snapshot(40, 50);
        var failed = UsageSnapshot.Empty(UsageTestData.Source(), UsageTestData.Now.AddMinutes(3), UsageDataSource.CodexAppServer, UsageState.Error, "x");

        var view = Build(Status(failed, good), UsageTestData.Now.AddMinutes(4));

        Assert.Equal("read failed; data from 06:00", view.Note);
        Assert.Equal(2, view.Rows.Count);
        Assert.True(view.IsProblem);
    }

    [Fact]
    public void Before_the_first_read_it_says_reading_and_a_cached_reading_shows_while_waiting()
    {
        Assert.Equal("Reading...", Build(Status(null)).Note);

        var cached = UsageTestData.Snapshot(30, 30);
        var view = Build(new UsageStatus("codex", "Codex", null, cached));
        Assert.Equal(2, view.Rows.Count);
    }

    [Fact]
    public void A_missing_window_shows_only_the_other_one()
    {
        var snapshot = new UsageSnapshot(UsageTestData.Source(), [UsageTestData.Window(QuotaKind.Weekly, 60, UsageTestData.Now.AddDays(1))],
            UsageTestData.Now, UsageDataSource.CodexAppServer, UsageState.Ok);

        var view = Build(Status(snapshot));

        Assert.Equal("Week", Assert.Single(view.Rows).Label);
    }

    [Theory]
    [InlineData(true, 90, "")]
    [InlineData(false, 0, "")]
    public void A_placeholder_reset_is_not_shown(bool _, int __, string expected)
    {
        var window = new QuotaWindow(QuotaKind.Weekly, 10080, 0, UsageTestData.Now.AddDays(7), "", IsResetReal: false);

        Assert.Equal(expected, UsageViewBuilder.ResetText(window, UsageTestData.Now));
    }

    [Fact]
    public void Reset_text_covers_unknown_due_and_short_times()
    {
        string Text(DateTimeOffset? at) => UsageViewBuilder.ResetText(new QuotaWindow(QuotaKind.FiveHour, 300, 10, at, "", true), UsageTestData.Now);

        Assert.Equal("?", Text(null));
        Assert.Equal("due", Text(UsageTestData.Now));
        Assert.Equal("due", Text(UsageTestData.Now.AddMinutes(-5)));
        Assert.Equal("<1m", Text(UsageTestData.Now.AddSeconds(30)));
        Assert.Equal("45m", Text(UsageTestData.Now.AddMinutes(45)));
        Assert.Equal("1h 05m", Text(UsageTestData.Now.AddMinutes(65)));
        Assert.Equal("4d 2h", Text(UsageTestData.Now.AddHours(98)));
    }

    [Theory]
    [InlineData(10, UsageLevel.Normal)]
    [InlineData(79.9, UsageLevel.Normal)]
    [InlineData(80, UsageLevel.Warning)]
    [InlineData(94, UsageLevel.Warning)]
    [InlineData(95, UsageLevel.Critical)]
    [InlineData(100, UsageLevel.Critical)]
    public void Levels_follow_the_thresholds(double percent, UsageLevel expected) =>
        Assert.Equal(expected, UsageViewBuilder.LevelFor(percent, [80, 95]));

    [Fact]
    public void One_threshold_makes_everything_above_it_critical()
    {
        Assert.Equal(UsageLevel.Normal, UsageViewBuilder.LevelFor(40, [60]));
        Assert.Equal(UsageLevel.Critical, UsageViewBuilder.LevelFor(60, [60]));
    }

    [Fact]
    public void Line_counts_match_the_display_mode()
    {
        var withRows = Build(Status(UsageTestData.Snapshot(1, 2)));
        var without = Build(Status(null));

        Assert.Equal(3, UsageViewBuilder.LineCount([withRows], UsageDisplayMode.Bars));
        Assert.Equal(2, UsageViewBuilder.LineCount([without], UsageDisplayMode.Bars));
        Assert.Equal(1, UsageViewBuilder.LineCount([withRows], UsageDisplayMode.Percent));
        Assert.Equal(4, UsageViewBuilder.LineCount([withRows, without], UsageDisplayMode.Percent) + 2);
        Assert.Equal(0, UsageViewBuilder.LineCount([], UsageDisplayMode.Bars));
    }

    [Fact]
    public void Percentages_are_rounded_for_display_and_the_bar_is_clamped()
    {
        var snapshot = new UsageSnapshot(UsageTestData.Source(), [UsageTestData.Window(QuotaKind.FiveHour, 37.6, UsageTestData.Now.AddHours(1))],
            UsageTestData.Now, UsageDataSource.CodexAppServer, UsageState.Ok);

        var row = Assert.Single(Build(Status(snapshot)).Rows);

        Assert.Equal("38%", row.Percent);
        Assert.InRange(row.Fraction, 0, 1);
    }
}
