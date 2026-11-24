using PomoQuote.AiUsage;

namespace PomoQuote.Tests;

/// <summary>A read must never run, not even its first synchronous part, on the thread that asks for it (the UI thread in the app).</summary>
public sealed class UsageOffUiThreadTests
{
    private sealed class BlockingFactory : IJsonLineSessionFactory
    {
        public ManualResetEventSlim Entered { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public int ThreadId { get; private set; } = -1;

        public IJsonLineSession Start()
        {
            ThreadId = Environment.CurrentManagedThreadId;
            Entered.Set();
            Release.Wait(TimeSpan.FromSeconds(20)); // a slow PATH probe or process start
            throw new FileNotFoundException("not installed");
        }
    }

    [Fact]
    public async Task The_session_factory_runs_on_another_thread_so_a_slow_start_cannot_block_the_caller()
    {
        var factory = new BlockingFactory();
        var provider = new CodexUsageProvider(factory, TimeProvider.System, TimeSpan.FromSeconds(30));
        using var collector = new UsageCollector([provider], TimeProvider.System);
        var callerThread = Environment.CurrentManagedThreadId;

        var read = collector.RefreshAsync("codex"); // like the UI timer's tick: must come back at once

        Assert.False(read.IsCompleted); // the call returned while the factory is still blocked
        Assert.True(factory.Entered.Wait(TimeSpan.FromSeconds(10)));
        Assert.NotEqual(callerThread, factory.ThreadId);
        Assert.False(read.IsCompleted);

        factory.Release.Set();
        await read;
        Assert.Equal(UsageState.NotInstalled, collector.StatusOf("codex")!.Latest!.State);
    }

    [Fact]
    public async Task A_caller_with_a_synchronization_context_is_not_blocked_either()
    {
        var factory = new BlockingFactory();
        var provider = new CodexUsageProvider(factory, TimeProvider.System, TimeSpan.FromSeconds(30));
        using var collector = new UsageCollector([provider], TimeProvider.System);

        Task read = Task.CompletedTask;
        var uiThread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            read = collector.RefreshAsync("codex");
            Assert.False(read.IsCompleted);
        });
        uiThread.Start();
        Assert.True(uiThread.Join(TimeSpan.FromSeconds(5)), "the caller was blocked by the read");

        Assert.True(factory.Entered.Wait(TimeSpan.FromSeconds(10)));
        factory.Release.Set();
        await read;
    }

    [Fact]
    public async Task A_failed_read_takes_its_data_source_from_the_provider()
    {
        var provider = new FakeProvider("codex", _ => throw new InvalidOperationException());
        using var collector = new UsageCollector([provider], TimeProvider.System);

        await collector.RefreshAsync("codex");

        Assert.Equal(provider.DataSource, collector.StatusOf("codex")!.Latest!.DataSource);
    }
}
