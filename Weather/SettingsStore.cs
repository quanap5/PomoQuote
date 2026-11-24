using System.Text.Json;

namespace PomoQuote.Weather;

/// <summary>The user's saved settings: for now just the city.</summary>
internal sealed record AppSettings(CityLocation City)
{
    public static AppSettings Default { get; } = new(CityLocation.Default);
}

/// <summary>
/// Reads and writes <see cref="AppSettings"/> as JSON, by default in %AppData%\PomoQuote\settings.json.
/// A missing, unreadable or invalid file yields the default settings, never an exception.
/// </summary>
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public SettingsStore(string path) => _path = path;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PomoQuote", "settings.json");

    public AppSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path));
            return settings is not null && IsValid(settings.City) ? settings : AppSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return AppSettings.Default;
        }
    }

    /// <summary>Writes via a temp file so a crash never leaves a half-written settings file.</summary>
    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, _path, overwrite: true);
    }

    private static bool IsValid(CityLocation? city) =>
        city is not null &&
        !string.IsNullOrWhiteSpace(city.Name) &&
        city.Latitude is >= -90 and <= 90 &&
        city.Longitude is >= -180 and <= 180;
}
