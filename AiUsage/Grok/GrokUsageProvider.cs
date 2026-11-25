using System.ComponentModel;
using System.Text.Json;

namespace PomoQuote.AiUsage;

/// <summary>
/// Starts `grok agent stdio` (the CLI's own agent server for editors) as a real child process. The CLI does its normal start-up work each time
/// it starts; the environment only switches off its worktree auto-clean and its update check for this child. Nothing here opens a login file.
/// </summary>
internal sealed class GrokAcpSessionFactory : IJsonLineSessionFactory
{
    /// <summary>The only arguments the usage read ever uses.</summary>
    public static readonly string[] Arguments = ["agent", "stdio"];

    public static readonly IReadOnlyDictionary<string, string> Environment_ = new Dictionary<string, string>
    {
        ["GROK_WORKTREE_AUTO_GC"] = "0",
        ["GROK_DISABLE_AUTOUPDATER"] = "1",
    };

    private readonly Func<string?> _locate;

    public GrokAcpSessionFactory(Func<string?>? locate = null) => _locate = locate ?? (() => GrokLocator.FindExecutable());

    public IJsonLineSession Start()
    {
        var executable = _locate() ?? throw new FileNotFoundException("grok was not found");
        var (file, arguments) = CodexLocator.CommandFor(executable, Arguments);
        return ProcessJsonLineSession.Start(file, arguments, environment: Environment_);
    }
}

/// <summary>
/// Reads the Grok allowance (the weekly credit pool of a SuperGrok plan) from the Grok Build CLI's own agent server: `grok agent stdio`, then
/// `initialize` and the billing request, and nothing else. By construction no prompt and no session is ever sent, so no model can be called and
/// no credit can be spent from here; the CLI owns the login and PomoQuote passes and reads no login of any kind. The billing request is an
/// extension of the CLI's agent protocol found in its open-source code, not a documented API, so a shape that is not understood, or a method the
/// CLI refuses, ends in "unsupported version" and the provider stops asking until PomoQuote restarts. A read that takes too long is abandoned and
/// the process is killed with its children.
/// </summary>
internal sealed class GrokUsageProvider : IUsageProvider
{
    public const string ProviderId = "grok";

    /// <summary>How often Grok is read: the weekly pool moves slowly, and each read starts the CLI.</summary>
    public static readonly TimeSpan Poll = TimeSpan.FromMinutes(5);

    /// <summary>The only requests this provider can ever build.</summary>
    internal static readonly string[] AllowedMethods = ["initialize", "_x.ai/billing", "x.ai/billing"];

    private const int InitializeId = 1;
    private const int MethodNotFound = -32601;

    private readonly IJsonLineSessionFactory _sessions;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _timeout;
    private volatile bool _halted;

    public GrokUsageProvider(IJsonLineSessionFactory sessions, TimeProvider clock, TimeSpan? timeout = null)
    {
        _sessions = sessions;
        _clock = clock;
        _timeout = timeout ?? TimeSpan.FromSeconds(12);
    }

    public string Id => ProviderId;

    public string DisplayName => "Grok";

    public UsageDataSource DataSource => UsageDataSource.GrokAcp;

    public TimeSpan? PollInterval => Poll;

    /// <summary>True after the CLI answered in a way this version does not understand: nothing more is run until the app restarts.</summary>
    internal bool Halted => _halted;

    public async Task<UsageSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        if (_halted) return Unsupported();

        IJsonLineSession session;
        try
        {
            session = _sessions.Start();
        }
        catch (Exception ex) when (ex is FileNotFoundException or Win32Exception or DirectoryNotFoundException)
        {
            return Fail(UsageState.NotInstalled, "The Grok CLI (grok) was not found on this PC.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            await using (session.ConfigureAwait(false))
            {
                await session.SendAsync(Request(InitializeId, "initialize", """{"protocolVersion":1,"clientCapabilities":{}}"""), timeout.Token).ConfigureAwait(false);
                var init = await WaitForReplyAsync(session, InitializeId, timeout.Token).ConfigureAwait(false);
                if (init is null) return Fail(UsageState.Error, "The Grok CLI stopped unexpectedly.");
                if (init.Value.TryGetProperty("error", out _)) return Halt();

                // The method goes over the wire with the protocol's extension prefix; the plain name is tried only if the CLI says it does not know the first.
                var id = InitializeId;
                JsonElement? reply = null;
                foreach (var method in new[] { "_x.ai/billing", "x.ai/billing" })
                {
                    id++;
                    await session.SendAsync(Request(id, method, "{}"), timeout.Token).ConfigureAwait(false);
                    reply = await WaitForReplyAsync(session, id, timeout.Token).ConfigureAwait(false);
                    if (reply is null) return Fail(UsageState.Error, "The Grok CLI stopped unexpectedly.");
                    if (!(reply.Value.TryGetProperty("error", out var error) && ErrorCode(error) == MethodNotFound)) break;
                }

                return Interpret(reply!.Value);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own limit: disposing the session (above) has killed the process.
            return Fail(UsageState.Error, $"The Grok CLI did not answer within {_timeout.TotalSeconds:0} s.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return Fail(UsageState.Error, "Could not talk to the Grok CLI.");
        }
    }

    private UsageSnapshot Interpret(JsonElement reply)
    {
        if (reply.TryGetProperty("error", out var error))
        {
            if (ErrorCode(error) == MethodNotFound) return Halt();
            // Only the kind is kept: the text may name the account.
            return LooksSignedOut(ErrorMessage(error))
                ? Fail(UsageState.NotLoggedIn, "The Grok CLI is not signed in. Run \"grok login\" in a terminal.")
                : Fail(UsageState.Error, "The Grok CLI could not read the allowance.");
        }

        if (!reply.TryGetProperty("result", out var result)) return Halt();
        try
        {
            var billing = GrokBillingParser.Parse(result);
            return new UsageSnapshot(new UsageSource(ProviderId, UsageEnvironment.Windows, "", billing.PlanTier), billing.Windows, _clock.GetUtcNow(), UsageDataSource.GrokAcp, UsageState.Ok);
        }
        catch (UsageParseException)
        {
            return Halt();
        }
    }

    private UsageSnapshot Halt()
    {
        _halted = true;
        return Unsupported();
    }

    private UsageSnapshot Unsupported() =>
        Fail(UsageState.Unsupported, "The Grok CLI answered in a way PomoQuote does not understand (unsupported version). Reading is stopped until PomoQuote restarts.");

    private static int? ErrorCode(JsonElement error) =>
        error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var number) ? number : null;

    private static string ErrorMessage(JsonElement error) =>
        error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString() ?? "" : "";

    private static bool LooksSignedOut(string text) =>
        text.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("grok login", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("log in", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("sign in", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("signed in", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>The one place a request is built: only the allowed methods can be sent (a prompt or a session can never be).</summary>
    internal static string Request(int id, string method, string paramsJson) =>
        AllowedMethods.Contains(method) ? $$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}","params":{{paramsJson}}}""" : throw new InvalidOperationException("This request is not allowed.");

    private UsageSnapshot Fail(UsageState state, string text) =>
        UsageSnapshot.Empty(new UsageSource(ProviderId, UsageEnvironment.Windows, "", ""), _clock.GetUtcNow(), UsageDataSource.GrokAcp, state, text);
}

/// <summary>What the Settings dialog shows about the Grok CLI: found and its version. Read only: `grok version`, a documented command.</summary>
internal sealed record GrokInstallInfo(bool Installed, string Version, string Detail);

internal sealed class GrokDetector
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(8);

    private readonly ICommandRunner _runner;
    private readonly Func<string?> _locate;

    public GrokDetector(ICommandRunner? runner = null, Func<string?>? locate = null)
    {
        _runner = runner ?? new ProcessCommandRunner();
        _locate = locate ?? (() => GrokLocator.FindExecutable());
    }

    public async Task<GrokInstallInfo> DetectAsync(CancellationToken cancellationToken)
    {
        var executable = _locate();
        if (executable is null) return new GrokInstallInfo(false, "", "The Grok CLI (grok) was not found. Install Grok Build first.");

        try
        {
            var (file, arguments) = CodexLocator.CommandFor(executable, "version");
            var (_, text) = await _runner.RunAsync(file, arguments, CommandTimeout, cancellationToken).ConfigureAwait(false);
            return new GrokInstallInfo(true, CodexDetector.ParseVersion(text), "The Grok CLI (grok) is installed. If it asks you to sign in, run \"grok login\" in a terminal (PomoQuote never asks for a token).");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return new GrokInstallInfo(true, "", "grok is installed but did not answer.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new GrokInstallInfo(true, "", "grok is installed but did not answer in time.");
        }
    }
}