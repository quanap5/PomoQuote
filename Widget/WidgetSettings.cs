using System.Drawing;
using System.Text.Json;

namespace PomoQuote.Widget;

/// <summary>
/// What is remembered about widget mode: whether it is on, where the widget sits (top-left corner in
/// screen pixels, null until it was first placed) and its opacity.
/// </summary>
internal sealed record WidgetSettings(bool Enabled = false, int? X = null, int? Y = null, int OpacityPercent = 100)
{
    public static readonly IReadOnlyList<int> OpacityChoices = [100, 80, 60];

    public static WidgetSettings Default { get; } = new();

    /// <summary>The saved position, or null when it was never set (or only half of it was saved).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Point? Position => X is { } x && Y is { } y ? new Point(x, y) : null;

    /// <summary>The opacity as a 0-1 factor for <c>Form.Opacity</c>.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double Opacity => OpacityPercent / 100.0;

    /// <summary>The same settings with an unknown opacity replaced by 100% and a half-saved position dropped.</summary>
    public WidgetSettings Normalized() => this with
    {
        OpacityPercent = OpacityChoices.Contains(OpacityPercent) ? OpacityPercent : 100,
        X = Position is null ? null : X,
        Y = Position is null ? null : Y,
    };

    public WidgetSettings WithPosition(Point position) => this with { X = position.X, Y = position.Y };
}

/// <summary>
/// Reads and writes <see cref="WidgetSettings"/> as JSON, by default in %AppData%\PomoQuote\widget.json.
/// A missing or unreadable file yields the defaults; an unknown opacity is reset to 100%.
/// </summary>
internal sealed class WidgetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public WidgetStore(string path) => _path = path;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PomoQuote", "widget.json");

    public WidgetSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(_path))?.Normalized() ?? WidgetSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return WidgetSettings.Default;
        }
    }

    /// <summary>Writes via a temp file so a crash never leaves a half-written file.</summary>
    public void Save(WidgetSettings settings)
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
