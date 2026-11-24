using System.ComponentModel;
using System.Text.Json;

namespace PomoQuote.AiUsage.Claude;

/// <summary>How a manual refresh ended.</summary>
public enum RefreshOutcome
{
    Done,

    /// <summary>The last refresh was less than a minute ago.</summary>
    TooSoon,

    /// <summary>A refresh is already running.</summary>
    Busy,

    NotInstalled,
    TimedOut,

    /// <summary>Claude answered but reported no plan limits (an API-key login).</summary>
    NoLimits,

    /// <summary>Claude could not answer (not logged in, offline, ...); no text of its answer is kept.</summary>
    Failed,
}

/// <summary>The result of a manual refresh: the outcome, a short message for the user, and the new snapshot on success.</summary>
internal sealed record RefreshResult(RefreshOutcome Outcome, string Message, ClaudeSnapshot? Snapshot = null);

/// <summary>Starts the Claude CLI; replaceable in tests (a fake `claude`).</summary>
internal interface IClaudeSessionFactory
{
    /// <summary>Starts `claude` with these arguments in this folder; throws <see cref="FileNotFoundException"/> or <see cref="Win32Exception"/> when it cannot start.</summary>
    IJsonLineSession Start(IReadOnlyList<string> arguments, string workingDirectory);
}

internal sealed class ClaudeProcessFactory : IClaudeSessionFactory
{
    public IJsonLineSession Start(IReadOnlyList<string> arguments, string workingDirectory)
    {
        var exe = ClaudeLocator.FindExecutable() ?? throw new FileNotFoundException("claude was not found");
        return ProcessJsonLineSession.Start(exe, arguments, workingDirectory, closeInput: true);
    }
}

/// <summary>Finds the Claude Code CLI. Nothing here opens a credential or settings file.</summary>
internal static class ClaudeLocator
{
    public static string? FindExecutable(Func<string, bool>? fileExists = null, IEnumerable<string>? pathDirectories = null, string? userProfile = null)
    {
        fileExists ??= File.Exists;
        var directories = pathDirectories ?? (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var directory in directories)
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), "claude.exe");
                if (fileExists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry.
            }
        }

        var home = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Path.Combine(home, ".local", "bin", "claude.exe");
        return fileExists(local) ? local : null;
    }
}

/// <summary>
/// The manual "Refresh Claude now": runs `claude -p` once with the smallest model and a one-word prompt, reads the
/// `rate_limit_event` from `--output-format stream-json`, converts the 0..1 utilization to percent used and saves it as a
/// snapshot. It costs a very small amount of quota, so it is never automatic: one at a time, at most one a minute, 30 s limit
/// (the process is killed on timeout and when the app exits), no credential read, and nothing of the prompt or the answer is kept.
/// </summary>
internal sealed class ClaudeRefresher
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MinimumGap = TimeSpan.FromSeconds(60);

    private readonly IClaudeSessionFactory _factory;
    private readonly ClaudeSnapshotStore _store;
    private readonly TimeProvider _clock;
    private readonly Func<string> _workDirectory;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset? _lastAttempt;

    public ClaudeRefresher(IClaudeSessionFactory factory, ClaudeSnapshotStore store, TimeProvider clock, Func<string>? workDirectory = null, TimeSpan? timeout = null)
    {
        _factory = factory;
        _store = store;
        _clock = clock;
        _workDirectory = workDirectory ?? Path.GetTempPath;
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>The arguments of the one request (public so the note in Settings and the tests can show exactly what runs).</summary>
    public static IReadOnlyList<string> Arguments { get; } =
    [
        "-p", "Reply with the single word ok.", "--model", "haiku", "--output-format", "stream-json", "--verbose",
        "--setting-sources", "project", "--no-session-persistence", "--max-budget-usd", "0.05",
    ];

    /// <summary>Seconds until another refresh is allowed (0 when one is).</summary>
    public int SecondsUntilAllowed()
    {
        if (_lastAttempt is not { } last) return 0;
        var left = MinimumGap - (_clock.GetUtcNow() - last);
        return left > TimeSpan.Zero ? (int)Math.Ceiling(left.TotalSeconds) : 0;
    }

    public async Task<RefreshResult> RefreshAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, CancellationToken.None).ConfigureAwait(false)) return new RefreshResult(RefreshOutcome.Busy, "A refresh is already running.");
        try
        {
            var wait = SecondsUntilAllowed();
            if (wait > 0) return new RefreshResult(RefreshOutcome.TooSoon, $"Refresh again in {wait} s (at most one a minute).");
            _lastAttempt = _clock.GetUtcNow();

            // Run off the UI thread: starting the process is synchronous.
            return await Task.Run(() => RunAsync(cancellationToken), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<RefreshResult> RunAsync(CancellationToken cancellationToken)
    {
        IJsonLineSession session;
        try
        {
            session = _factory.Start(Arguments, _workDirectory());
        }
        catch (Exception ex) when (ex is FileNotFoundException or Win32Exception or DirectoryNotFoundException)
        {
            return new RefreshResult(RefreshOutcome.NotInstalled, "Claude Code was not found on this PC.");
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(_timeout);
        try
        {
            await using (session.ConfigureAwait(false))
            {
                var windows = new List<ClaudeSnapshotWindow>();
                var answered = false;
                var failed = false;
                while (await session.ReceiveAsync(limit.Token).ConfigureAwait(false) is { } line)
                {
                    if (!TryReadEvent(line, windows, out var isResult, out var isError)) continue;
                    if (isResult)
                    {
                        answered = true;
                        failed = isError;
                        break;
                    }
                }

                if (windows.Count == 0)
                {
                    return failed || !answered
                        ? new RefreshResult(RefreshOutcome.Failed, "Claude Code did not answer (is it logged in? is there a connection?).")
                        : new RefreshResult(RefreshOutcome.NoLimits, "Claude answered, but this login has no 5-hour or weekly plan limits.");
                }

                var snapshot = new ClaudeSnapshot(_clock.GetUtcNow(), ClaudeSnapshotSource.ManualRefresh, "", "manual", true, true, windows.OrderBy(w => w.Kind).ToList());
                try
                {
                    _store.Write(snapshot);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return new RefreshResult(RefreshOutcome.Failed, "The numbers were read but could not be saved.");
                }

                return new RefreshResult(RefreshOutcome.Done, "Updated.", snapshot);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new RefreshResult(RefreshOutcome.TimedOut, $"Claude Code did not answer within {_timeout.TotalSeconds:0} s.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return new RefreshResult(RefreshOutcome.Failed, "Could not talk to Claude Code.");
        }
    }

    /// <summary>Reads one stream-json line. Only the quota fields of a `rate_limit_event` and the end of the answer are looked at; everything else is ignored and never kept.</summary>
    private static bool TryReadEvent(string line, List<ClaudeSnapshotWindow> windows, out bool isResult, out bool isError)
    {
        isResult = false;
        isError = false;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return false;

            switch (type.GetString())
            {
                case "result":
                    isResult = true;
                    isError = root.TryGetProperty("is_error", out var error) && error.ValueKind == JsonValueKind.True;
                    return true;

                case "rate_limit_event":
                    if (root.TryGetProperty("rate_limit_info", out var info) && info.TryGetProperty("unifiedWindows", out var unified) && unified.ValueKind == JsonValueKind.Object)
                    {
                        windows.Clear(); // the last event wins
                        Add(windows, unified, "five_hour", QuotaKind.FiveHour);
                        Add(windows, unified, "seven_day", QuotaKind.Weekly);
                    }

                    return true;

                default:
                    return false;
            }
        }
        catch (JsonException)
        {
            return false; // a log line or a damaged line
        }
    }

    private static void Add(List<ClaudeSnapshotWindow> windows, JsonElement unified, string name, QuotaKind kind)
    {
        if (!unified.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object) return;
        if (!window.TryGetProperty("utilization", out var utilization) || utilization.ValueKind != JsonValueKind.Number) return;

        DateTimeOffset? resetsAt = null;
        if (window.TryGetProperty("resetsAt", out var reset) && reset.ValueKind == JsonValueKind.Number && reset.TryGetInt64(out var epoch) && epoch > 0)
        {
            try
            {
                resetsAt = DateTimeOffset.FromUnixTimeSeconds(epoch);
            }
            catch (ArgumentOutOfRangeException)
            {
                resetsAt = null;
            }
        }

        // utilization is a 0..1 fraction; percent used is what every provider here shows.
        windows.Add(new ClaudeSnapshotWindow(kind, Math.Clamp(utilization.GetDouble() * 100, 0, 100), resetsAt));
    }
}
