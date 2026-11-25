using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace PomoQuote.AiUsage;

/// <summary>Finds the Antigravity CLI (agy) on this PC. Nothing here opens a file of the CLI's own folder: only the program is looked up.</summary>
internal static class AgyLocator
{
    /// <summary>The agy program: first on PATH (exe, then cmd), then the folder its installer uses. Null when not found.</summary>
    public static string? FindExecutable(Func<string, bool>? fileExists = null, IEnumerable<string>? pathDirectories = null, string? localAppData = null)
    {
        fileExists ??= File.Exists;
        var directories = pathDirectories ?? (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var name in new[] { "agy.exe", "agy.cmd" })
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
        var installed = Path.Combine(local, "agy", "bin", "agy.exe");
        return fileExists(installed) ? installed : null;
    }
}

/// <summary>What one run of the usage command returned: the exit code and what it printed.</summary>
internal sealed record AgyRunResult(int ExitCode, string Output);

/// <summary>Runs the usage command once; replaceable in tests.</summary>
internal interface IAgyRunner
{
    /// <summary>Runs `agy -p /usage --output-format json`. Throws <see cref="FileNotFoundException"/> when agy is not there and <see cref="TimeoutException"/> when it took too long (the process is killed).</summary>
    Task<AgyRunResult> RunUsageAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Starts the real `agy -p /usage --output-format json`: no model prompt is passed (the usage command is handled by the CLI itself),
/// slash commands are never disabled, the working folder is a private empty temporary folder, the CLI's auto-update is switched off
/// for this child, the input is closed at once, the output is capped, and a run that takes longer than the time limit is killed with
/// its whole process tree. PomoQuote passes and reads no login of any kind: agy uses its own.
/// </summary>
internal sealed class ProcessAgyRunner : IAgyRunner
{
    /// <summary>The only arguments the usage read ever uses.</summary>
    public static readonly string[] UsageArguments = ["-p", "/usage", "--output-format", "json", "--print-timeout", "10s"];

    private const int OutputCap = 1024 * 1024;

    private readonly Func<string?> _locate;
    private readonly TimeSpan _timeout;
    private readonly IReadOnlyList<string> _arguments;

    public ProcessAgyRunner(Func<string?>? locate = null, TimeSpan? timeout = null, IReadOnlyList<string>? arguments = null)
    {
        _locate = locate ?? (() => AgyLocator.FindExecutable());
        _timeout = timeout ?? TimeSpan.FromSeconds(12);
        _arguments = arguments ?? UsageArguments;
    }

    public async Task<AgyRunResult> RunUsageAsync(CancellationToken cancellationToken)
    {
        var executable = _locate() ?? throw new FileNotFoundException("agy was not found");
        var (file, arguments) = CodexLocator.CommandFor(executable, [.. _arguments]);
        var workFolder = Path.Combine(Path.GetTempPath(), "pomoquote-agy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workFolder);
        try
        {
            var info = new ProcessStartInfo(file)
            {
                WorkingDirectory = workFolder,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            info.Environment["AGY_CLI_DISABLE_AUTO_UPDATE"] = "true";

            using var process = Process.Start(info) ?? throw new Win32Exception("The process did not start.");
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(_timeout);
            try
            {
                process.StandardInput.Close();
                var output = ReadCappedAsync(process.StandardOutput, limit.Token);
                var error = ReadCappedAsync(process.StandardError, limit.Token);
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
                return new AgyRunResult(process.ExitCode, await output.ConfigureAwait(false) + "\n" + await error.ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                if (cancellationToken.IsCancellationRequested) throw;
                throw new TimeoutException($"agy did not answer within {_timeout.TotalSeconds:0} s.");
            }
        }
        finally
        {
            try { Directory.Delete(workFolder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* a temporary folder left behind is harmless */ }
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Gone already.
        }
    }

    /// <summary>Reads a stream to its end, keeping at most <see cref="OutputCap"/> characters (the rest is read and dropped so the child never blocks on a full pipe).</summary>
    private static async Task<string> ReadCappedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (builder.Length < OutputCap) builder.Append(buffer, 0, Math.Min(read, OutputCap - builder.Length));
        }

        return builder.ToString();
    }
}

/// <summary>What the Settings dialog shows about agy: found and its version. Read only: `agy --version`.</summary>
internal sealed record AgyInstallInfo(bool Installed, string Version, string Detail);

internal sealed class AgyDetector
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(8);

    private readonly ICommandRunner _runner;
    private readonly Func<string?> _locate;

    public AgyDetector(ICommandRunner? runner = null, Func<string?>? locate = null)
    {
        _runner = runner ?? new ProcessCommandRunner();
        _locate = locate ?? (() => AgyLocator.FindExecutable());
    }

    public async Task<AgyInstallInfo> DetectAsync(CancellationToken cancellationToken)
    {
        var executable = _locate();
        if (executable is null) return new AgyInstallInfo(false, "", "The Antigravity CLI (agy) was not found. Install it first.");

        try
        {
            var (file, arguments) = CodexLocator.CommandFor(executable, "--version");
            var (_, text) = await _runner.RunAsync(file, arguments, CommandTimeout, cancellationToken).ConfigureAwait(false);
            var version = CodexDetector.ParseVersion(text);
            return new AgyInstallInfo(true, version, "The Antigravity CLI (agy) is installed. If it asks you to sign in, run agy once in a terminal (PomoQuote never asks for a token).");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return new AgyInstallInfo(true, "", "agy is installed but did not answer.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AgyInstallInfo(true, "", "agy is installed but did not answer in time.");
        }
    }
}