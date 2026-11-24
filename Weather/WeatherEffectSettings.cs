using PomoQuote.AiUsage;

namespace PomoQuote.Weather;

/// <summary>How many clouds, streaks and flakes are drawn, and how often the picture updates.</summary>
public enum WeatherEffectIntensity
{
    /// <summary>About half the particles: the default, the lightest on the CPU.</summary>
    Low,

    /// <summary>The full number of particles.</summary>
    Normal,
}

/// <summary>The user's choices for the weather effects, saved in %AppData%\PomoQuote\weather-effects.json. Nothing in it is secret.</summary>
/// <param name="Enabled">Whether the effects are drawn at all (on by default: they are light, and stop by themselves when nobody sees them).</param>
/// <param name="Intensity">Low (the default) or Normal.</param>
/// <param name="ReduceMotion">When true the effect is one still picture, not an animation. The effect is also still whenever Windows has client-area animations turned off.</param>
internal sealed record WeatherEffectSettings(
    bool Enabled = true,
    WeatherEffectIntensity Intensity = WeatherEffectIntensity.Low,
    bool ReduceMotion = false)
{
    public static WeatherEffectSettings Default { get; } = new();

    internal bool IsValid => Enum.IsDefined(Intensity);
}

/// <summary>Stores <see cref="WeatherEffectSettings"/> in weather-effects.json: atomic save, and a missing or damaged file gives the defaults.</summary>
internal sealed class WeatherEffectSettingsStore
{
    private readonly string _path;

    public WeatherEffectSettingsStore(string path) => _path = path;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PomoQuote", "weather-effects.json");

    public WeatherEffectSettings Load() => AtomicJson.Load(_path, WeatherEffectSettings.Default, settings => settings.IsValid);

    public void Save(WeatherEffectSettings settings) => AtomicJson.Save(_path, settings);
}

/// <summary>What the effect is doing right now.</summary>
internal enum EffectRunState
{
    /// <summary>Nothing is drawn: the effects are off or the weather has none.</summary>
    Off,

    /// <summary>Visible but not drawn right now: minimised, hidden, covered, session locked or asleep. No timer runs.</summary>
    Paused,

    /// <summary>One still picture, no timer (Reduce motion, or Windows animations are off).</summary>
    Still,

    /// <summary>Animating: the effect's own timer runs.</summary>
    Animating,
}

/// <summary>Decides, from the plain facts, whether the effect timer may run. Pure, so every pause condition is tested.</summary>
internal static class WeatherEffectPolicy
{
    public static EffectRunState Decide(
        WeatherEffectSettings settings, WeatherEffectKind kind,
        bool visible, bool minimized, bool occluded, bool sessionLocked, bool asleep, bool systemAnimations)
    {
        if (!settings.Enabled || kind == WeatherEffectKind.None) return EffectRunState.Off;
        if (!visible || minimized || occluded || sessionLocked || asleep) return EffectRunState.Paused;
        return settings.ReduceMotion || !systemAnimations ? EffectRunState.Still : EffectRunState.Animating;
    }

    /// <summary>The effect timer's interval: 62 ms, which the Windows timer (steps of about 15.6 ms) runs at about 16 frames a second, for either intensity (Low just has fewer particles to draw).</summary>
    public static int IntervalMilliseconds(WeatherEffectIntensity intensity) => 62;
}
