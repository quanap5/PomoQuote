using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PomoQuote.AiUsage.Claude;

/// <summary>Runs the user's original status line command with the same input; replaceable in tests.</summary>
internal interface IChainRunner
{
    /// <summary>Runs <paramref name="command"/> in the shell Claude Code uses, feeds it <paramref name="input"/>, and returns its output and exit code. Null when it could not be run or did not finish in time.</summary>
    (byte[] Output, int ExitCode)? Run(string command, byte[] input, TimeSpan timeout);
}

/// <summary>
/// What Claude Code runs as the status line when the bridge is installed. It reads the status line JSON, saves a
/// redacted snapshot for PomoQuote, then either runs the user's original command with the same input and passes its
/// output and exit code through, or (when the user had none) prints a short usage line. Whatever goes wrong inside the
/// bridge is swallowed: it must never break the status line, never be slow, and always exit with the original's code (0 otherwise).
/// </summary>
internal static class ClaudeBridge
{
    public static readonly TimeSpan ChainTimeout = TimeSpan.FromSeconds(4);

    private const string ChainArgument = "--chain-b64";

    /// <summary>The entry point for `PomoQuote.exe --bridge claude [--chain-b64 &lt;base64&gt;]`.</summary>
    public static int RunFromProcess(string[] args)
    {
        try
        {
            using var stdin = Console.OpenStandardInput();
            using var stdout = Console.OpenStandardOutput();
            return Run(args, stdin, stdout, new ShellChainRunner(), new ClaudeSnapshotStore(ClaudeSnapshotStore.DefaultDirectory), TimeProvider.System);
        }
        catch (Exception)
        {
            return 0; // never break the user's status line
        }
    }

    public static int Run(string[] args, Stream input, Stream output, IChainRunner chain, ClaudeSnapshotStore store, TimeProvider clock)
    {
        byte[] bytes;
        try
        {
            using var memory = new MemoryStream();
            input.CopyTo(memory);
            bytes = memory.ToArray();
        }
        catch (Exception)
        {
            return 0;
        }

        var summary = "";
        try
        {
            var snapshot = Extract(bytes, clock.GetUtcNow());
            if (snapshot is not null)
            {
                store.Write(snapshot);
                store.DeleteOld(clock.GetUtcNow());
                summary = Summary(snapshot);
            }
        }
        catch (Exception)
        {
            // A snapshot that cannot be written is only a missed update.
        }

        var original = ChainFrom(args);
        if (original is not null)
        {
            try
            {
                var result = chain.Run(original, bytes, ChainTimeout);
                if (result is { } done)
                {
                    output.Write(done.Output);
                    output.Flush();
                    return done.ExitCode;
                }
            }
            catch (Exception)
            {
                // Fall through: print the summary instead of nothing.
            }
        }

        if (summary.Length > 0)
        {
            var text = Encoding.UTF8.GetBytes(summary);
            output.Write(text);
            output.Flush();
        }

        return 0;
    }

    /// <summary>The original command from `--chain-b64 &lt;base64&gt;`, or null (no chain, or damaged).</summary>
    internal static string? ChainFrom(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] != ChainArgument) continue;
            return DecodeChain(args[i + 1]);
        }

        return null;
    }

    internal static string? DecodeChain(string base64)
    {
        try
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            return text.Length > 0 ? text : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    internal static string EncodeChain(string command) => Convert.ToBase64String(Encoding.UTF8.GetBytes(command));

    /// <summary>Turns the status line JSON into a redacted snapshot. Null when the input is not JSON.</summary>
    internal static ClaudeSnapshot? Extract(byte[] json, DateTimeOffset now)
    {
        JsonDocument document;
        try
        {
            var start = json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF ? 3 : 0; // tolerate a UTF-8 BOM
            document = JsonDocument.Parse(json.AsMemory(start));
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var version = root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String ? Truncate(v.GetString() ?? "", 32) : "";
            var session = root.TryGetProperty("session_id", out var s) && s.ValueKind == JsonValueKind.String ? Hash(s.GetString()) : "unknown";
            var hadResponse = root.TryGetProperty("context_window", out var cw) && cw.ValueKind == JsonValueKind.Object
                              && cw.TryGetProperty("current_usage", out var usage) && usage.ValueKind != JsonValueKind.Null;

            var windows = new List<ClaudeSnapshotWindow>();
            var present = root.TryGetProperty("rate_limits", out var limits) && limits.ValueKind == JsonValueKind.Object;
            if (present)
            {
                AddWindow(windows, limits, "five_hour", QuotaKind.FiveHour);
                AddWindow(windows, limits, "seven_day", QuotaKind.Weekly);
            }

            return new ClaudeSnapshot(now, ClaudeSnapshotSource.StatusLine, version, session, hadResponse, present, windows);
        }
    }

    private static void AddWindow(List<ClaudeSnapshotWindow> windows, JsonElement limits, string name, QuotaKind kind)
    {
        if (!limits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object) return;
        if (!window.TryGetProperty("used_percentage", out var percent) || percent.ValueKind != JsonValueKind.Number) return;

        DateTimeOffset? resetsAt = null;
        if (window.TryGetProperty("resets_at", out var reset) && reset.ValueKind == JsonValueKind.Number && reset.TryGetInt64(out var epoch) && epoch > 0)
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

        windows.Add(new ClaudeSnapshotWindow(kind, Math.Clamp(percent.GetDouble(), 0, 100), resetsAt));
    }

    private static string Hash(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "unknown";
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("pomoquote-claude-session|" + id));
        return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];

    private static string Summary(ClaudeSnapshot snapshot)
    {
        if (snapshot.Windows.Count == 0) return "";
        return string.Join(" · ", snapshot.Windows.Select(w => $"{(w.Kind == QuotaKind.FiveHour ? "5h" : "week")} {Math.Round(w.PercentUsed):0}%"));
    }
}

/// <summary>Runs the original status line command in the same shell Claude Code uses on Windows: Git Bash when it is installed, otherwise PowerShell.</summary>
internal sealed class ShellChainRunner : IChainRunner
{
    public (byte[] Output, int ExitCode)? Run(string command, byte[] input, TimeSpan timeout)
    {
        var (file, args) = ClaudeShell.CommandFor(command);
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in args) info.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new Win32Exception();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return null;
        }

        using (process)
        {
            // stderr is discarded; reading it keeps the child from blocking.
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            var output = new MemoryStream();
            var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
            try
            {
                process.StandardInput.BaseStream.Write(input, 0, input.Length);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The command did not read its input; that is allowed.
            }

            if (!process.WaitForExit(timeout))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // Gone already.
                }

                return null;
            }

            copy.Wait(TimeSpan.FromSeconds(1));
            return (output.ToArray(), process.ExitCode);
        }
    }
}

/// <summary>The shell Claude Code runs status line commands in on Windows, and how to start a command in it.</summary>
internal static class ClaudeShell
{
    /// <summary>The Git Bash executable (CLAUDE_CODE_GIT_BASH_PATH, then the usual Git for Windows folders), or null when there is none (Claude then uses PowerShell).</summary>
    public static string? FindGitBash(Func<string, bool>? fileExists = null, Func<string, string?>? env = null)
    {
        fileExists ??= File.Exists;
        env ??= Environment.GetEnvironmentVariable;
        var explicitPath = env("CLAUDE_CODE_GIT_BASH_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath) && fileExists(explicitPath)) return explicitPath;

        foreach (var root in new[] { env("ProgramFiles"), env("ProgramFiles(x86)"), env("LocalAppData") is { } local ? Path.Combine(local, "Programs") : null })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var candidate = Path.Combine(root, "Git", "bin", "bash.exe");
            if (fileExists(candidate)) return candidate;
        }

        return null;
    }

    public static (string File, string[] Arguments) CommandFor(string command)
    {
        var bash = FindGitBash();
        return bash is not null
            ? (bash, ["-c", command])
            : ("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", command]);
    }
}
