namespace PomoQuote.AiUsage;

/// <summary>Finds the Grok CLI on this PC: only the program is looked up, no file of the CLI's own folder is opened.</summary>
internal static class GrokLocator
{
    /// <summary>The grok program: first on PATH (exe, then cmd), then the folder its installer uses. Null when not found.</summary>
    public static string? FindExecutable(Func<string, bool>? fileExists = null, IEnumerable<string>? pathDirectories = null, string? userProfile = null)
    {
        fileExists ??= File.Exists;
        var directories = pathDirectories ?? (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var name in new[] { "grok.exe", "grok.cmd" })
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

        var profile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var installed = Path.Combine(profile, ".grok", "bin", "grok.exe");
        return fileExists(installed) ? installed : null;
    }
}
