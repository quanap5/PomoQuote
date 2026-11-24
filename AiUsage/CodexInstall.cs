using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace PomoQuote.AiUsage;

/// <summary>Finds the Codex CLI on this PC and starts it as an app-server. Nothing here opens a credential file.</summary>
internal static class CodexLocator
{
    /// <summary>The Codex executable: first on PATH (exe, then cmd), then the desktop app's folder. Null when not found.</summary>
    public static string? FindExecutable(Func<string, bool>? fileExists = null, IEnumerable<string>? pathDirectories = null, string? localAppData = null)
    {
        fileExists ??= File.Exists;
        var directories = pathDirectories ?? (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var name in new[] { "codex.exe", "codex.cmd" })
        {
            foreach (var directory in directories)
            {
                try
                {
                    var candidate = Path.Combine(directory.Trim('"'), name);
                    if (fileExists(candidate)) return candidate;
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry.
                }
            }
        }

        var local = localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var desktopApp = Path.Combine(local, "Programs", "OpenAI", "Codex", "bin", "codex.exe");
        return fileExists(desktopApp) ? desktopApp : null;
    }

    /// <summary>The file and arguments that run the CLI (a .cmd shim must go through cmd.exe).</summary>
    public static (string FileName, string[] Arguments) CommandFor(string executable, params string[] arguments) =>
        executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            ? ("cmd.exe", ["/c", executable, .. arguments])
            : (executable, arguments);
}

/// <summary>Starts `codex app-server` as a real child process.</summary>
internal sealed class CodexAppServerSessionFactory : IJsonLineSessionFactory
{
    public IJsonLineSession Start()
    {
        var executable = CodexLocator.FindExecutable() ?? throw new FileNotFoundException("codex was not found");
        var (file, arguments) = CodexLocator.CommandFor(executable, "app-server");
        return ProcessJsonLineSession.Start(file, arguments);
    }
}

/// <summary>What the Settings dialog shows about Codex: found, version and whether it is logged in. Read only.</summary>
internal sealed record CodexInstallInfo(bool Installed, string Version, bool? LoggedIn, string Detail);

/// <summary>Runs a command and returns its exit code and combined output; replaceable in tests.</summary>
internal interface ICommandRunner
{
    Task<(int ExitCode, string Output)> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);
}

internal sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<(int ExitCode, string Output)> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new Win32Exception("The process did not start.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(limit.Token);
            var error = process.StandardError.ReadToEndAsync(limit.Token);
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            return (process.ExitCode, (await output.ConfigureAwait(false)) + "\n" + (await error.ConfigureAwait(false)));
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                // Gone already.
            }

            throw;
        }
    }
}

/// <summary>Asks the CLI itself, read only: `codex --version` and `codex login status`. Never logs in or out.</summary>
internal sealed class CodexDetector
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(8);

    private readonly ICommandRunner _runner;
    private readonly Func<string?> _locate;

    public CodexDetector(ICommandRunner? runner = null, Func<string?>? locate = null)
    {
        _runner = runner ?? new ProcessCommandRunner();
        _locate = locate ?? (() => CodexLocator.FindExecutable());
    }

    public async Task<CodexInstallInfo> DetectAsync(CancellationToken cancellationToken)
    {
        var executable = _locate();
        if (executable is null) return new CodexInstallInfo(false, "", null, "Codex was not found. Install the Codex CLI or desktop app first.");

        try
        {
            var (versionFile, versionArgs) = CodexLocator.CommandFor(executable, "--version");
            var (_, versionText) = await _runner.RunAsync(versionFile, versionArgs, CommandTimeout, cancellationToken).ConfigureAwait(false);
            var version = ParseVersion(versionText);

            var (statusFile, statusArgs) = CodexLocator.CommandFor(executable, "login", "status");
            var (_, statusText) = await _runner.RunAsync(statusFile, statusArgs, CommandTimeout, cancellationToken).ConfigureAwait(false);
            var loggedIn = ParseLoggedIn(statusText);
            var detail = loggedIn switch
            {
                true => "Codex is installed and logged in.",
                false => "Codex is installed but not logged in. Run \"codex login\" in a terminal (PomoQuote never asks for a token).",
                null => "Codex is installed; its login state could not be read.",
            };
            return new CodexInstallInfo(true, version, loggedIn, detail);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return new CodexInstallInfo(true, "", null, "Codex is installed but did not answer.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CodexInstallInfo(true, "", null, "Codex is installed but did not answer in time.");
        }
    }

    internal static string ParseVersion(string text)
    {
        foreach (var token in text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length > 0 && char.IsDigit(token[0]) && token.Contains('.')) return token.TrimEnd('.', ',');
        }

        return "";
    }

    /// <summary>True for "Logged in ...", false for "Not logged in", null when the text says neither.</summary>
    internal static bool? ParseLoggedIn(string text)
    {
        if (text.Contains("not logged in", StringComparison.OrdinalIgnoreCase)) return false;
        if (text.Contains("logged in", StringComparison.OrdinalIgnoreCase)) return true;
        return null;
    }
}
