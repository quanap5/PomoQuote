using System.Collections.Concurrent;

namespace PomoQuote.AiUsage;

/// <summary>What the widget knows about one provider: the outcome of the latest read and the last good numbers.</summary>
/// <param name="ProviderId">Provider id.</param>
/// <param name="DisplayName">Name to show.</param>
/// <param name="Latest">The latest read (any state), or null before the first one.</param>
/// <param name="LastGood">The last read that had numbers, or null. Shown (marked stale) while the latest read failed.</param>
internal sealed record UsageStatus(string ProviderId, string DisplayName, UsageSnapshot? Latest, UsageSnapshot? LastGood)
{
    /// <summary>The snapshot whose numbers should be drawn: the latest if it is good, otherwise the last good one.</summary>
    public UsageSnapshot? Shown => Latest is { State: UsageState.Ok } ? Latest : LastGood;
}

/// <summary>
/// Reads the providers and keeps the results. Each provider is read on its own, with its own time limit and its own
/// catch, so one that fails, hangs or crashes never blocks or breaks another, and reads of the same provider never
/// overlap. Results are immutable and are announced through <see cref="StatusChanged"/> (possibly on a worker thread:
/// the owner marshals to the UI). Only the last good, non-secret reading is persisted, through <see cref="UsageCacheStore"/>.
/// </summary>
internal sealed class UsageCollector : IDisposable
{
    private readonly IReadOnlyList<IUsageProvider> _providers;
    private readonly TimeProvider _clock;
    private readonly UsageCacheStore? _cache;
    private readonly TimeSpan _readLimit;
    private readonly ConcurrentDictionary<string, UsageStatus> _status = new();
    private readonly Dictionary<string, SemaphoreSlim> _gates;
    private readonly CancellationTokenSource _disposed = new();

    public UsageCollector(IReadOnlyList<IUsageProvider> providers, TimeProvider clock, UsageCacheStore? cache = null, TimeSpan? readLimit = null)
    {
        _providers = providers;
        _clock = clock;
        _cache = cache;
        _readLimit = readLimit ?? TimeSpan.FromSeconds(15);
        _gates = providers.ToDictionary(p => p.Id, _ => new SemaphoreSlim(1, 1));
        foreach (var provider in providers) _status[provider.Id] = new UsageStatus(provider.Id, provider.DisplayName, null, null);
    }

    /// <summary>Raised after every read with the provider's new status.</summary>
    public event EventHandler<UsageStatus>? StatusChanged;

    public IReadOnlyList<IUsageProvider> Providers => _providers;

    public UsageStatus? StatusOf(string providerId) => _status.TryGetValue(providerId, out var status) ? status : null;

    /// <summary>Seeds the statuses with the cached last-good readings, so the widget has numbers (marked stale by age) right after a start.</summary>
    public void LoadCache()
    {
        if (_cache is null) return;
        foreach (var snapshot in _cache.Load())
        {
            if (!_status.TryGetValue(snapshot.Source.ProviderId, out var current) || current.LastGood is not null) continue;
            _status[snapshot.Source.ProviderId] = current with { Latest = snapshot, LastGood = snapshot };
        }
    }

    /// <summary>Reads one provider. If a read of it is already running this returns at once.</summary>
    public async Task RefreshAsync(string providerId, CancellationToken cancellationToken = default)
    {
        var provider = _providers.FirstOrDefault(p => p.Id == providerId);
        if (provider is null || _disposed.IsCancellationRequested) return;

        var gate = _gates[providerId];
        if (!await gate.WaitAsync(0, CancellationToken.None).ConfigureAwait(false)) return;
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposed.Token);
            limit.CancelAfter(_readLimit);
            UsageSnapshot snapshot;
            try
            {
                snapshot = await provider.ReadAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _disposed.IsCancellationRequested)
            {
                return; // asked to stop: nothing to report
            }
            catch (OperationCanceledException)
            {
                snapshot = Failure(provider, UsageState.Error, $"The read took longer than {_readLimit.TotalSeconds:0} s.");
            }
            catch (Exception)
            {
                // Whatever went wrong inside a provider stays inside it: no message text is kept (it could name an account).
                snapshot = Failure(provider, UsageState.Error, "The read failed unexpectedly.");
            }

            var previous = _status[providerId];
            var updated = previous with
            {
                Latest = snapshot,
                LastGood = snapshot.State == UsageState.Ok ? snapshot : previous.LastGood,
            };
            _status[providerId] = updated;
            if (snapshot.State == UsageState.Ok) SaveCache();
            StatusChanged?.Invoke(this, updated);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Reads the given providers at the same time; each is isolated from the others.</summary>
    public Task RefreshAsync(IEnumerable<string> providerIds, CancellationToken cancellationToken = default) =>
        Task.WhenAll(providerIds.Select(id => RefreshAsync(id, cancellationToken)));

    private UsageSnapshot Failure(IUsageProvider provider, UsageState state, string text) =>
        UsageSnapshot.Empty(new UsageSource(provider.Id, UsageEnvironment.Windows, "", ""), _clock.GetUtcNow(), UsageDataSource.CodexAppServer, state, text);

    private void SaveCache()
    {
        if (_cache is null) return;
        try
        {
            _cache.Save(_status.Values.Where(s => s.LastGood is not null).Select(s => s.LastGood!));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The numbers are just not remembered for the next start.
        }
    }

    public void Dispose()
    {
        _disposed.Cancel();
        _disposed.Dispose();
    }
}
