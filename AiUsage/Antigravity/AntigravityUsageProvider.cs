using System.ComponentModel;

namespace PomoQuote.AiUsage;

/// <summary>
/// Reads the Antigravity quota from the vendor's own command line: `agy -p /usage --output-format json`, the command the vendor
/// documents for seeing the quota (https://antigravity.google/docs/cli/commands/usage; its headless JSON mode is documented at /docs/cli/headless.md, the shape of the usage answer is only observed). The CLI owns the login; PomoQuote passes and reads no login of any kind. Guards: the answer must come from the usage command
/// and report no conversation turn and no token use, otherwise the provider says "unsupported version" and stops asking for the rest of
/// the run (a read that might spend quota is never repeated). A read that takes too long is abandoned and the process is killed.
/// The shape of the answer is not an official contract: the parser tolerates missing and extra fields.
/// </summary>
internal sealed class AntigravityUsageProvider : IUsageProvider
{
    public const string ProviderId = "antigravity";

    /// <summary>How often agy is asked (each read starts the CLI for about 6 seconds, and its numbers move slowly).</summary>
    public static readonly TimeSpan Poll = TimeSpan.FromMinutes(5);

    private readonly IAgyRunner _runner;
    private readonly TimeProvider _clock;
    private volatile bool _halted;

    public AntigravityUsageProvider(IAgyRunner runner, TimeProvider clock)
    {
        _runner = runner;
        _clock = clock;
    }

    public string Id => ProviderId;

    public string DisplayName => "Antigravity";

    public UsageDataSource DataSource => UsageDataSource.AntigravityCli;

    public TimeSpan? PollInterval => Poll;

    /// <summary>True after a guard failed: nothing more is run until the app restarts.</summary>
    internal bool Halted => _halted;

    public async Task<UsageSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        if (_halted) return Fail(UsageState.Unsupported, "The Antigravity CLI answered in a way PomoQuote does not understand (unsupported version). Reading is stopped until PomoQuote restarts.");

        AgyRunResult result;
        try
        {
            result = await _runner.RunUsageAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or Win32Exception or DirectoryNotFoundException)
        {
            return Fail(UsageState.NotInstalled, "The Antigravity CLI (agy) was not found on this PC.");
        }
        catch (TimeoutException)
        {
            return Fail(UsageState.Error, "The Antigravity CLI did not answer in time.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return Fail(UsageState.Error, "Could not run the Antigravity CLI.");
        }

        try
        {
            var windows = AgyUsageParser.Parse(result.Output);
            return new UsageSnapshot(new UsageSource(ProviderId, UsageEnvironment.Windows, "", ""), windows, _clock.GetUtcNow(), UsageDataSource.AntigravityCli, UsageState.Ok);
        }
        catch (AgyReportedErrorException reported)
        {
            return reported.SignedOut
                ? Fail(UsageState.NotLoggedIn, "The Antigravity CLI is not signed in. Run agy once in a terminal.")
                : Fail(UsageState.Error, "The Antigravity CLI could not read the quota.");
        }
        catch (AgyGuardException)
        {
            _halted = true;
            return Fail(UsageState.Unsupported, "The Antigravity CLI answered in a way PomoQuote does not understand (unsupported version). Reading is stopped until PomoQuote restarts.");
        }
        catch (UsageParseException)
        {
            // Not the report, a non-zero exit with a sign-in message, or a shape this version does not know.
            if (result.ExitCode != 0 && AgyUsageParser.LooksSignedOut(result.Output))
            {
                return Fail(UsageState.NotLoggedIn, "The Antigravity CLI is not signed in. Run agy once in a terminal.");
            }

            return Fail(UsageState.Unsupported, "Unexpected answer from the Antigravity CLI (unsupported version?).");
        }
    }

    private UsageSnapshot Fail(UsageState state, string text) =>
        UsageSnapshot.Empty(new UsageSource(ProviderId, UsageEnvironment.Windows, "", ""), _clock.GetUtcNow(), UsageDataSource.AntigravityCli, state, text);
}