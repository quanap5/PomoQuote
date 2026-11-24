using PomoQuote.AiUsage;

namespace PomoQuote.Tests;

/// <summary>While one provider is being read, only that provider shows an updating mark and its last numbers stay.</summary>
public sealed class RefreshingMarkTests
{
    private static UsageSnapshot Good(string provider, double five = 10) => UsageTestData.Snapshot(five, 20, source: UsageTestData.Source(provider));

    [Fact]
    public async Task A_slow_read_marks_only_its_own_provider_and_keeps_the_last_numbers_until_it_ends()
    {
        var release = new TaskCompletionSource<UsageSnapshot>();
        var slow = new FakeProvider("slow", _ => release.Task);
        var other = new FakeProvider("other", _ => Task.FromResult(Good("other")));
        using var collector = new UsageCollector([slow, other], TimeProvider.System) { RefreshIndicatorDelay = TimeSpan.FromMilliseconds(30) };

        var read = collector.RefreshAsync("slow");
        await collector.RefreshAsync("other");
        for (var i = 0; i < 500 && !(collector.StatusOf("slow")?.Refreshing ?? false); i++) await Task.Delay(20);

        Assert.True(collector.StatusOf("slow")!.Refreshing);
        Assert.False(collector.StatusOf("other")!.Refreshing);
        Assert.NotNull(collector.StatusOf("other")!.Shown); // the other provider is untouched

        release.SetResult(Good("slow", 55));
        await read;

        var done = collector.StatusOf("slow")!;
        Assert.False(done.Refreshing);
        Assert.Equal(55, done.Shown!.Windows[0].PercentUsed);
    }

    [Fact]
    public async Task A_fast_read_never_flashes_the_mark()
    {
        var provider = new FakeProvider("fast", _ => Task.FromResult(Good("fast")));
        using var collector = new UsageCollector([provider], TimeProvider.System) { RefreshIndicatorDelay = TimeSpan.FromMilliseconds(300) };
        var marks = 0;
        collector.StatusChanged += (_, s) => { if (s.Refreshing) marks++; };

        await collector.RefreshAsync("fast");
        await Task.Delay(400);

        Assert.Equal(0, marks);
        Assert.False(collector.StatusOf("fast")!.Refreshing);
    }

    [Fact]
    public async Task A_read_that_runs_into_its_time_limit_does_not_leave_the_mark_behind()
    {
        var provider = new FakeProvider("hang", async token => { await Task.Delay(Timeout.Infinite, token); return Good("hang"); });
        using var collector = new UsageCollector([provider], TimeProvider.System, readLimit: TimeSpan.FromMilliseconds(150)) { RefreshIndicatorDelay = TimeSpan.FromMilliseconds(30) };

        await collector.RefreshAsync("hang");

        Assert.False(collector.StatusOf("hang")!.Refreshing);
    }

    [Fact]
    public void The_view_says_updating_in_the_tooltip_and_keeps_the_rows()
    {
        var status = new UsageStatus("codex", "Codex", Good("codex"), Good("codex"), Refreshing: true);

        var view = UsageViewBuilder.Build(status, UsageSettings.Default.WithEnabled("codex", true), UsageTestData.Now.AddMinutes(1), TimeZoneInfo.Utc);

        Assert.True(view.Refreshing);
        Assert.Contains("Updating", view.Tip);
        Assert.Equal(2, view.Rows.Count);
    }
}
