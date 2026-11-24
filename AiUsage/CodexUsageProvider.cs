using System.ComponentModel;
using System.Text.Json;

namespace PomoQuote.AiUsage;

/// <summary>
/// Reads the Codex quota the way the spike verified: start a short-lived `codex app-server`, send `initialize`, the
/// `initialized` notification and `account/rateLimits/read`, stay connected until the reply, then let the process
/// exit. The Codex CLI owns login and token refresh; PomoQuote passes and reads no credential. A read that takes
/// longer than the timeout is abandoned and the process is killed.
/// </summary>
internal sealed class CodexUsageProvider : IUsageProvider
{
    public const string ProviderId = "codex";

    private const int InitializeId = 1;
    private const int RateLimitsId = 2;

    private readonly IJsonLineSessionFactory _sessions;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _timeout;

    public CodexUsageProvider(IJsonLineSessionFactory sessions, TimeProvider clock, TimeSpan? timeout = null)
    {
        _sessions = sessions;
        _clock = clock;
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
    }

    public string Id => ProviderId;

    public string DisplayName => "Codex";

    public UsageDataSource DataSource => UsageDataSource.CodexAppServer;

    public async Task<UsageSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var unknown = new UsageSource(ProviderId, UsageEnvironment.Windows, "", "");
        IJsonLineSession session;
        try
        {
            session = _sessions.Start();
        }
        catch (Exception ex) when (ex is FileNotFoundException or Win32Exception or DirectoryNotFoundException)
        {
            return Fail(unknown, UsageState.NotInstalled, "Codex was not found on this PC.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            await using (session.ConfigureAwait(false))
            {
                await session.SendAsync(Request(InitializeId, "initialize", """{"clientInfo":{"name":"pomoquote","version":"1"}}"""), timeout.Token).ConfigureAwait(false);
                var init = await WaitForReplyAsync(session, InitializeId, timeout.Token).ConfigureAwait(false);
                if (init is null) return Fail(unknown, UsageState.Error, "Codex stopped unexpectedly.");
                if (init.Value.TryGetProperty("error", out _)) return Fail(unknown, UsageState.Unsupported, "Codex refused the app-server handshake (unsupported version?).");

                await session.SendAsync("""{"method":"initialized"}""", timeout.Token).ConfigureAwait(false);
                await session.SendAsync(Request(RateLimitsId, "account/rateLimits/read", null), timeout.Token).ConfigureAwait(false);
                var reply = await WaitForReplyAsync(session, RateLimitsId, timeout.Token).ConfigureAwait(false);
                if (reply is null) return Fail(unknown, UsageState.Error, "Codex stopped unexpectedly.");
                return Interpret(reply.Value);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own timeout: disposing the session (above) has killed the process.
            return Fail(unknown, UsageState.Error, $"Codex did not answer within {_timeout.TotalSeconds:0} s.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return Fail(unknown, UsageState.Error, "Could not talk to Codex.");
        }
    }

    private UsageSnapshot Interpret(JsonElement reply)
    {
        var unknown = new UsageSource(ProviderId, UsageEnvironment.Windows, "", "");
        if (reply.TryGetProperty("error", out var error))
        {
            // Only the code is kept; the message may name the account.
            var text = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                ? message.GetString() ?? ""
                : "";
            return LooksLikeSignedOut(text)
                ? Fail(unknown, UsageState.NotLoggedIn, "Codex is not logged in. Run \"codex login\" in a terminal.")
                : Fail(unknown, UsageState.Error, "Codex could not read the quota.");
        }

        if (!reply.TryGetProperty("result", out var result)) return Fail(unknown, UsageState.Unsupported, "Unexpected answer from Codex (unsupported version?).");
        try
        {
            var limits = CodexRateLimitsParser.Parse(result);
            var source = new UsageSource(ProviderId, UsageEnvironment.Windows, limits.AccountKey, limits.PlanTier);
            return new UsageSnapshot(source, limits.Windows, _clock.GetUtcNow(), UsageDataSource.CodexAppServer, UsageState.Ok);
        }
        catch (UsageParseException)
        {
            return Fail(unknown, UsageState.Unsupported, "Unexpected answer from Codex (unsupported version?).");
        }
    }

    private static bool LooksLikeSignedOut(string message) =>
        message.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("log in", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("logged in", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("login", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("sign in", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("signed in", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads lines until the reply with the given id arrives; notifications, other ids and non-JSON lines are skipped. Null means the stream closed.</summary>
    private static async Task<JsonElement?> WaitForReplyAsync(IJsonLineSession session, int id, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await session.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) return null;
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonElement root;
            try
            {
                using var document = JsonDocument.Parse(line);
                root = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue; // a log line or a damaged line
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out var replyId) && replyId.ValueKind == JsonValueKind.Number && replyId.TryGetInt32(out var number) && number == id)
            {
                return root;
            }
        }
    }

    private static string Request(int id, string method, string? paramsJson) =>
        paramsJson is null ? $$"""{"id":{{id}},"method":"{{method}}"}""" : $$"""{"id":{{id}},"method":"{{method}}","params":{{paramsJson}}}""";

    private UsageSnapshot Fail(UsageSource source, UsageState state, string text) =>
        UsageSnapshot.Empty(source, _clock.GetUtcNow(), UsageDataSource.CodexAppServer, state, text);
}
