using System.Globalization;
using System.Text.Json;

namespace HelloWorldBot.Pomodoro;

/// <summary>Counts finished work sessions for one calendar day; the count starts again on a new day.</summary>
internal sealed class DailyCounter
{
    private const string DayFormat = "yyyy-MM-dd";

    public DailyCounter(string? day = null, int count = 0)
    {
        Day = day;
        Count = Math.Max(0, count);
    }

    /// <summary>The day the stored count belongs to, as yyyy-MM-dd.</summary>
    public string? Day { get; private set; }

    public int Count { get; private set; }

    /// <summary>The number of sessions finished on <paramref name="today"/>: 0 if the stored day is older.</summary>
    public int CountFor(DateOnly today) => Day == Format(today) ? Count : 0;

    public void Increment(DateOnly today)
    {
        var key = Format(today);
        if (Day != key)
        {
            Day = key;
            Count = 0;
        }

        Count++;
    }

    private static string Format(DateOnly day) => day.ToString(DayFormat, CultureInfo.InvariantCulture);
}

/// <summary>What is saved between runs: the options and today's finished-session count.</summary>
internal sealed record PomodoroData(PomodoroSettings Settings, string? Day = null, int CompletedToday = 0)
{
    public static PomodoroData Default { get; } = new(PomodoroSettings.Default);
}

/// <summary>
/// Reads and writes <see cref="PomodoroData"/> as JSON, by default in %AppData%\HelloWorldBot\pomodoro.json.
/// A missing, unreadable or invalid file yields the defaults, never an exception.
/// </summary>
internal sealed class PomodoroStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public PomodoroStore(string path) => _path = path;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HelloWorldBot", "pomodoro.json");

    public PomodoroData Load()
    {
        try
        {
            var data = JsonSerializer.Deserialize<PomodoroData>(File.ReadAllText(_path));
            return data is { Settings: { IsValid: true }, CompletedToday: >= 0 } ? data : PomodoroData.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return PomodoroData.Default;
        }
    }

    /// <summary>Writes via a temp file so a crash never leaves a half-written file.</summary>
    public void Save(PomodoroData data)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(data, JsonOptions));
        File.Move(temporary, _path, overwrite: true);
    }
}
