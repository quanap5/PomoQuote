using System.Text.Json;

namespace HelloWorldBot.Mascot;

/// <summary>
/// Options for the taskbar mascot. <see cref="EnabledMonitors"/> lists the monitors (by device name) the
/// robot may walk on; null means all of them. <see cref="RandomActions"/> is 0 (off), 1 (few) or 2 (normal).
/// </summary>
internal sealed record MascotSettings(
    bool Enabled = true,
    bool SoundEnabled = true,
    int VolumePercent = 70,
    int SpeedPercent = 100,
    int SizeScale = 2,
    string[]? EnabledMonitors = null,
    int RandomActions = 2)
{
    public const int MinVolume = 0;
    public const int MaxVolume = 100;
    public const int MinSpeed = 25;
    public const int MaxSpeed = 300;
    public const int DefaultSizeScale = 2;
    public const int MaxRandomActions = 2;
    public static readonly IReadOnlyList<int> SizeChoices = [2, 3, 4];

    public static MascotSettings Default { get; } = new();

    /// <summary>The same settings with every number pulled into its allowed range.</summary>
    public MascotSettings Normalized() => this with
    {
        VolumePercent = Math.Clamp(VolumePercent, MinVolume, MaxVolume),
        SpeedPercent = Math.Clamp(SpeedPercent, MinSpeed, MaxSpeed),
        SizeScale = SizeChoices.Contains(SizeScale) ? SizeScale : DefaultSizeScale,
        EnabledMonitors = EnabledMonitors is { Length: > 0 } ? EnabledMonitors : null,
        RandomActions = Math.Clamp(RandomActions, 0, MaxRandomActions),
    };

    /// <summary>The walking speed as a factor of the normal speed.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double SpeedFactor => SpeedPercent / 100.0;

    /// <summary>The volume as a 0-1 gain.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double VolumeGain => VolumePercent / 100.0;
}

/// <summary>
/// Reads and writes <see cref="MascotSettings"/> as JSON, by default in %AppData%\HelloWorldBot\mascot.json.
/// A missing or unreadable file yields the defaults; out-of-range numbers are clamped.
/// </summary>
internal sealed class MascotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public MascotStore(string path) => _path = path;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HelloWorldBot", "mascot.json");

    /// <summary>The optional user file that overrides the animation and behaviour defaults.</summary>
    public static string UserConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HelloWorldBot", "mascot-config.json");

    public MascotSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<MascotSettings>(File.ReadAllText(_path))?.Normalized() ?? MascotSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return MascotSettings.Default;
        }
    }

    /// <summary>Writes via a temp file so a crash never leaves a half-written file.</summary>
    public void Save(MascotSettings settings)
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
}
