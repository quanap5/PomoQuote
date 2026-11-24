using System.Diagnostics;

namespace PomoQuote;

/// <summary>
/// One-time move of the settings from the folder of the app's former name (%AppData%\HelloWorldBot) to
/// %AppData%\PomoQuote. Files are copied, never moved: the old folder stays untouched, and a file that
/// already exists in the new folder is never overwritten. A failure is logged and the app starts with
/// whatever was copied (or the defaults); it never throws.
/// </summary>
internal static class AppDataMigration
{
    internal const string DoneMarker = ".migrated-from-helloworldbot";

    private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public static string LegacyFolder { get; } = Path.Combine(AppData, "HelloWorldBot");

    public static string CurrentFolder { get; } = Path.Combine(AppData, "PomoQuote");

    /// <summary>Copies the old settings into the new folder once. Returns how many files were copied.</summary>
    public static int Run() => Run(LegacyFolder, CurrentFolder, message => Trace.WriteLine($"PomoQuote migration: {message}"));

    internal static int Run(string legacyFolder, string currentFolder, Action<string> log)
    {
        try
        {
            if (File.Exists(Path.Combine(currentFolder, DoneMarker))) return 0;
            if (!Directory.Exists(legacyFolder)) return 0;

            Directory.CreateDirectory(currentFolder);
            var copied = 0;
            var allCopiedOrKept = true;
            foreach (var source in Directory.EnumerateFiles(legacyFolder))
            {
                var name = Path.GetFileName(source);
                if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;

                var target = Path.Combine(currentFolder, name);
                try
                {
                    if (File.Exists(target)) continue; // never overwrite

                    // CopyTo without overwrite throws if the file appeared meanwhile, which keeps the rule above.
                    File.Copy(source, target, overwrite: false);
                    copied++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    allCopiedOrKept = false;
                    log($"could not copy {name}: {ex.Message}");
                }
            }

            // Only a complete pass is final; after a failure the next start tries the missing files again.
            if (allCopiedOrKept) File.WriteAllText(Path.Combine(currentFolder, DoneMarker), "");
            return copied;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            log($"skipped: {ex.Message}");
            return 0;
        }
    }
}
