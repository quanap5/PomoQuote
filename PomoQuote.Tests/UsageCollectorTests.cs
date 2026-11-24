using System.Diagnostics;
using PomoQuote.AiUsage;

namespace PomoQuote.Tests;

internal sealed class FakeProvider : IUsageProvider
{
    private readonly Func<CancellationToken, Task<UsageSnapshot>> _read;

    public FakeProvider(string id, Func<CancellationToken, Task<UsageSnapshot>> read)
    {
        Id = id;
        _read = read;
    }

    public string Id { get; }

    public string DisplayName => Id.ToUpperInvariant();

    public int Reads { get; private set; }

    public Task<UsageSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        Reads++;
        return _read(cancellationToken);
    }
}

public sealed class UsageCollectorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PomoQuoteTests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static UsageSnapshot Good(string provider, double five = 10) =>
        UsageTestData.Snapshot(five, 20, source: UsageTestData.Source(provider));

    [Fact]
    public async Task A_good_read_becomes_the_latest_and_last_good_and_raises_one_event()
    {
        using var collector = new UsageCollector([new FakeProvider("codex", _ => Task.FromResult(Good("codex", 33)))], TimeProvider.System);
        var events = new List<UsageStatus>();
        collector.StatusChanged += (_, status) => events.Add(status);

        await collector.RefreshAsync("codex");

        var status = collector.StatusOf("codex")!;
        Assert.Equal(33, status.Latest!.Windows[0].PercentUsed);
        Assert.Same(status.Latest, status.LastGood);
        Assert.Same(status.Latest, status.Shown);
        Assert.Single(events);
    }

    [Fact]
    public async Task A_provider_that_throws_gives_an_error_snapshot_and_the_other_provider_still_works()
    {
        var bad = new FakeProvider("bad", _ => throw new InvalidOperationException("secret detail someone@example.com"));
        var good = new FakeProvider("good", _ => Task.FromResult(Good("good", 55)));
        using var collector = new UsageCollector([bad, good], TimeProvider.System);

        await collector.RefreshAsync(["bad", "good"]);

        var badStatus = collector.StatusOf("bad")!;
        Assert.Equal(UsageState.Error, badStatus.Latest!.State);
        Assert.DoesNotContain("someone", badStatus.Latest.ErrorText);
        Assert.DoesNotContain("secret", badStatus.Latest.ErrorText);
        Assert.Null(badStatus.LastGood);
        Assert.Equal(55, collector.StatusOf("good")!.Shown!.Windows[0].PercentUsed);
    }

    [Fact]
    public async Task A_provider_that_hangs_is_cut_off_by_the_collector_and_does_not_hold_up_the_others()
    {
        var hang = new FakeProvider("hang", async ct => { await Task.Delay(Timeout.Infinite, ct); return Good("hang"); });
        var good = new FakeProvider("good", _ => Task.FromResult(Good("good")));
        using var collector = new UsageCollector([hang, good], TimeProvider.System, readLimit: TimeSpan.FromMilliseconds(300));
        var clock = Stopwatch.StartNew();

        await collector.RefreshAsync(["hang", "good"]);

        Assert.True(clock.ElapsedMilliseconds < 5000);
        Assert.Equal(UsageState.Error, collector.StatusOf("hang")!.Latest!.State);
        Assert.Equal(UsageState.Ok, collector.StatusOf("good")!.Latest!.State);
    }

    [Fact]
    public async Task A_failed_read_keeps_the_last_good_numbers_to_show()
    {
        var results = new Queue<Func<UsageSnapshot>>([() => Good("codex", 40), () => throw new IOException("offline")]);
        using var collector = new UsageCollector([new FakeProvider("codex", _ => Task.FromResult(results.Dequeue()()))], TimeProvider.System);

        await collector.RefreshAsync("codex");
        await collector.RefreshAsync("codex");

        var status = collector.StatusOf("codex")!;
        Assert.Equal(UsageState.Error, status.Latest!.State);
        Assert.Equal(40, status.LastGood!.Windows[0].PercentUsed);
        Assert.Equal(40, status.Shown!.Windows[0].PercentUsed);
    }

    [Fact]
    public async Task Recovery_after_an_error_shows_the_new_numbers_again()
    {
        var results = new Queue<Func<UsageSnapshot>>([() => throw new IOException(), () => Good("codex", 61)]);
        using var collector = new UsageCollector([new FakeProvider("codex", _ => Task.FromResult(results.Dequeue()()))], TimeProvider.System);

        await collector.RefreshAsync("codex");
        await collector.RefreshAsync("codex");

        Assert.Equal(UsageState.Ok, collector.StatusOf("codex")!.Latest!.State);
        Assert.Equal(61, collector.StatusOf("codex")!.Shown!.Windows[0].PercentUsed);
    }

    [Fact]
    public async Task Reads_of_the_same_provider_never_overlap()
    {
        var release = new TaskCompletionSource();
        var provider = new FakeProvider("codex", async _ => { await release.Task; return Good("codex"); });
        using var collector = new UsageCollector([provider], TimeProvider.System);

        var first = collector.RefreshAsync("codex");
        await Task.Delay(100);
        await collector.RefreshAsync("codex"); // returns at once: a read is already running
        release.SetResult();
        await first;

        Assert.Equal(1, provider.Reads);
    }

    [Fact]
    public async Task Unknown_providers_and_calls_after_dispose_do_nothing()
    {
        var provider = new FakeProvider("codex", _ => Task.FromResult(Good("codex")));
        var collector = new UsageCollector([provider], TimeProvider.System);

        await collector.RefreshAsync("nope");
        collector.Dispose();
        await collector.RefreshAsync("codex");

        Assert.Equal(0, provider.Reads);
    }

    [Fact]
    public async Task Disposing_while_a_read_runs_cancels_it_without_an_event()
    {
        var provider = new FakeProvider("codex", async ct => { await Task.Delay(Timeout.Infinite, ct); return Good("codex"); });
        var collector = new UsageCollector([provider], TimeProvider.System);
        var events = 0;
        collector.StatusChanged += (_, _) => events++;

        var read = collector.RefreshAsync("codex");
        await Task.Delay(100);
        collector.Dispose();
        await read;

        Assert.Equal(0, events);
    }

    [Fact]
    public async Task The_last_good_reading_is_cached_and_seeds_a_new_collector()
    {
        var path = Path.Combine(_directory, "usage-cache.json");
        using (var collector = new UsageCollector([new FakeProvider("codex", _ => Task.FromResult(Good("codex", 72)))], TimeProvider.System, new UsageCacheStore(path)))
        {
            await collector.RefreshAsync("codex");
        }

        using var next = new UsageCollector([new FakeProvider("codex", _ => throw new IOException())], TimeProvider.System, new UsageCacheStore(path));
        Assert.Null(next.StatusOf("codex")!.Shown);
        next.LoadCache();

        Assert.Equal(72, next.StatusOf("codex")!.Shown!.Windows[0].PercentUsed);
    }

    [Fact]
    public async Task A_cache_that_cannot_be_written_does_not_break_the_read()
    {
        Directory.CreateDirectory(_directory);
        var blocked = Path.Combine(_directory, "usage-cache.json");
        Directory.CreateDirectory(blocked); // a folder where the file should be
        using var collector = new UsageCollector([new FakeProvider("codex", _ => Task.FromResult(Good("codex", 5)))], TimeProvider.System, new UsageCacheStore(blocked));

        await collector.RefreshAsync("codex");

        Assert.Equal(UsageState.Ok, collector.StatusOf("codex")!.Latest!.State);
    }
}
