using PomoQuote.AiUsage;

namespace PomoQuote.Tests;

public sealed class UsageNoteFitTests
{
    // A fake measure: 6 px per character, so widths are easy to reason about.
    private static float Measure(string s) => s.Length * 6f;

    private static UsageProviderView View(string note, string? shortNote) =>
        new("Codex", "plus", [], note, true, shortNote);

    [Fact]
    public void A_note_that_fits_is_shown_as_it_is()
    {
        var view = View("not logged in; data from 15:11", "not logged in · 15:11");

        Assert.Equal(view.Note, UsageViewBuilder.FitNote(view, 200, Measure));
    }

    [Fact]
    public void A_note_that_is_too_wide_uses_the_short_form_which_keeps_the_time()
    {
        var view = View("not logged in; data from 15:11", "not logged in · 15:11");

        var text = UsageViewBuilder.FitNote(view, 21 * 6, Measure);

        Assert.Equal("not logged in · 15:11", text);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(16)]
    [InlineData(12)]
    [InlineData(9)]
    public void When_even_the_short_form_is_too_wide_the_start_is_cut_and_the_time_stays(int characters)
    {
        var view = View("unsupported version; data from 15:11", "unsupported · 15:11");

        var text = UsageViewBuilder.FitNote(view, characters * 6, Measure);

        Assert.EndsWith("15:11", text);
        Assert.True(Measure(text) <= characters * 6, $"'{text}' is wider than {characters} chars");
        if (characters < 19) Assert.Contains("…", text);
    }

    [Fact]
    public void With_almost_no_room_only_the_time_is_shown_and_it_is_never_empty()
    {
        var view = View("read failed; data from 15:11", "read failed · 15:11");

        Assert.Equal("15:11", UsageViewBuilder.FitNote(view, 5 * 6, Measure));
        Assert.NotEmpty(UsageViewBuilder.FitNote(view, 3 * 6, Measure));
    }

    [Fact]
    public void A_note_without_a_time_is_cut_with_an_ellipsis()
    {
        var view = View("not installed", null);

        var text = UsageViewBuilder.FitNote(view, 8 * 6, Measure);

        Assert.EndsWith("…", text);
        Assert.True(Measure(text) <= 8 * 6);
    }

    [Theory]
    [InlineData(UsageState.NotInstalled, "not installed · 06:00")]
    [InlineData(UsageState.NotLoggedIn, "not logged in · 06:00")]
    [InlineData(UsageState.Unsupported, "unsupported · 06:00")]
    [InlineData(UsageState.Error, "read failed · 06:00")]
    public void The_builder_gives_every_failed_read_with_old_data_a_short_note_that_ends_with_the_time(UsageState state, string expectedShort)
    {
        var good = UsageTestData.Snapshot(40, 50);
        var failed = UsageSnapshot.Empty(UsageTestData.Source(), UsageTestData.Now.AddMinutes(3), UsageDataSource.CodexAppServer, state, "x");
        var status = new UsageStatus("codex", "Codex", failed, good);

        var view = UsageViewBuilder.Build(status, UsageSettings.Default.WithEnabled("codex", true), UsageTestData.Now.AddMinutes(4), TimeZoneInfo.Utc);

        Assert.Equal(expectedShort, view.Short);
        Assert.Contains("data from 06:00", view.Note);
    }

    [Fact]
    public void The_stale_note_has_a_short_form_with_the_time_and_other_notes_use_the_same_text()
    {
        var stale = UsageViewBuilder.Build(new UsageStatus("codex", "Codex", UsageTestData.Snapshot(1, 2), UsageTestData.Snapshot(1, 2)),
            UsageSettings.Default.WithEnabled("codex", true), UsageTestData.Now.AddMinutes(10), TimeZoneInfo.Utc);
        var fresh = UsageViewBuilder.Build(new UsageStatus("codex", "Codex", UsageTestData.Snapshot(1, 2), UsageTestData.Snapshot(1, 2)),
            UsageSettings.Default.WithEnabled("codex", true), UsageTestData.Now.AddMinutes(1), TimeZoneInfo.Utc);

        Assert.Equal("stale · 06:00", stale.Short);
        Assert.Equal(fresh.Note, fresh.Short);
    }
}
