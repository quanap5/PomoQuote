using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace PomoQuote.AiUsage;

/// <summary>One conversation with a child process that talks JSON, one message per line.</summary>
internal interface IJsonLineSession : IAsyncDisposable
{
    Task SendAsync(string line, CancellationToken cancellationToken);

    /// <summary>The next line, or null when the other side has closed its output.</summary>
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);
}

/// <summary>Starts sessions. Replaceable so tests can use a fake server.</summary>
internal interface IJsonLineSessionFactory
{
    /// <summary>Starts the process; throws <see cref="FileNotFoundException"/> or <see cref="Win32Exception"/> when it cannot start.</summary>
    IJsonLineSession Start();
}

/// <summary>
/// A real child process with redirected pipes, no window and UTF-8 without a byte order mark. The process is
/// killed (with its children) if it does not exit by itself shortly after its input is closed, and every live one
/// is tracked so <see cref="KillAll"/> can clean up when the app exits.
/// </summary>
internal sealed class ProcessJsonLineSession : IJsonLineSession
{
    private static readonly ConcurrentDictionary<int, Process> Live = new();

    private readonly Process _process;

    private ProcessJsonLineSession(Process process) => _process = process;

    public static ProcessJsonLineSession Start(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, bool closeInput = false)
    {
        var info = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        if (workingDirectory is not null) info.WorkingDirectory = workingDirectory;
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        var process = Process.Start(info) ?? throw new Win32Exception("The process did not start.");
        Live[process.Id] = process;

        // stderr is never shown or kept; draining it stops the child from blocking on a full pipe.
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        if (closeInput) process.StandardInput.Close(); // for programs that take their request from the command line and must not wait for input
        return new ProcessJsonLineSession(process);
    }

    public async Task SendAsync(string line, CancellationToken cancellationToken)
    {
        var bytes = new UTF8Encoding(false).GetBytes(line + "\n");
        var stream = _process.StandardInput.BaseStream; // written as bytes so no byte order mark is ever sent
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken) =>
        await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        var id = _process.Id;
        try
        {
            try
            {
                _process.StandardInput.Close();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                // Already closed.
            }

            using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            try
            {
                await _process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Kill(_process);
            }
        }
        finally
        {
            Live.TryRemove(id, out _);
            _process.Dispose();
        }
    }

    /// <summary>Kills every process still alive (called on app exit and on a timeout).</summary>
    public static void KillAll()
    {
        foreach (var process in Live.Values) Kill(process);
        Live.Clear();
    }

    /// <summary>How many child processes are alive right now (for tests and the leak check).</summary>
    public static int LiveCount => Live.Count;

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Gone already.
        }
    }
}
