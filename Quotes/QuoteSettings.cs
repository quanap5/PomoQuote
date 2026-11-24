using System.Text.Json;
using System.Text.Json.Serialization;

namespace PomoQuote.Quotes;

/// <summary>How often the sentence changes by itself, besides the one shown at start and the "next sentence" command.</summary>
public enum QuoteInterval
{
    /// <summary>Only at start and on request.</summary>
    Off,
    Minutes5,
    Minutes10,
    Minutes30,
    Minutes60,

    /// <summary>At the start of each Pomodoro phase.</summary>
    PerPhase,
}

/// <summary>The user's choices for the sentences, saved in %AppData%\PomoQuote\quotes.json.</summary>
/// <param name="Categories">Which kinds of sentence are shown (at least one).</param>
/// <param name="ShowTranslation">Whether the Vietnamese translation is shown under the English sentence.</param>
/// <param name="Interval">When the sentence changes by itself.</param>
/// <param name="LastEnglish">The sentence shown when the app last ran, so a restart does not begin with it again.</param>
internal sealed record QuoteSettings(QuoteCategory Categories, bool ShowTranslation, QuoteInterval Interval, string? LastEnglish = null)
{
    public static QuoteSettings Default { get; } = new(QuoteCategory.All, true, QuoteInterval.PerPhase);

    /// <summary>The timer period for a minutes setting; null for the settings that do not use a timer.</summary>
    public TimeSpan? Period => Interval switch
    {
        QuoteInterval.Minutes5 => TimeSpan.FromMinutes(5),
        QuoteInterval.Minutes10 => TimeSpan.FromMinutes(10),
        QuoteInterval.Minutes30 => TimeSpan.FromMinutes(30),
        QuoteInterval.Minutes60 => TimeSpan.FromMinutes(60),
        _ => null,
    };

    /// <summary>Whether a new Pomodoro phase brings a new sentence (every setting except Off).</summary>
    public bool ChangesOnPhase => Interval != QuoteInterval.Off;

    internal bool IsValid =>
        Categories != QuoteCategory.None &&
        (Categories & ~QuoteCategory.All) == 0 &&
        Enum.IsDefined(Interval);
}

/// <summary>
/// Reads and writes <see cref="QuoteSettings"/> as JSON. A missing, unreadable or invalid file yields the
/// default settings, never an exception; saving goes through a temp file so a crash cannot leave half a file.
/// </summary>
internal sealed class QuoteSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public QuoteSettingsStore(string path) => _path = path;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PomoQuote", "quotes.json");

    public QuoteSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<QuoteSettings>(File.ReadAllText(_path), JsonOptions);
            return settings is { IsValid: true } ? settings : QuoteSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return QuoteSettings.Default;
        }
    }

    public void Save(QuoteSettings settings)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, _path, overwrite: true);
    }
}
