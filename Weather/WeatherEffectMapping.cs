namespace PomoQuote.Weather;

/// <summary>The kind of animation drawn behind the widget's content.</summary>
internal enum WeatherEffectKind
{
    /// <summary>No animation (unknown or missing weather).</summary>
    None,

    /// <summary>A few soft sun glints that twinkle (clear, day).</summary>
    SunGlints,

    /// <summary>Faint twinkling stars (clear, night).</summary>
    Stars,

    /// <summary>Slowly drifting clouds; the level is how dense they are.</summary>
    Clouds,

    /// <summary>A light haze that drifts (fog).</summary>
    Haze,

    /// <summary>Falling streaks; the level is the rate.</summary>
    Rain,

    /// <summary>Slowly falling flakes; the level is the rate.</summary>
    Snow,
}

/// <summary>How strong an effect is: how many clouds, streaks or flakes.</summary>
internal enum EffectLevel
{
    Light = 1,
    Moderate = 2,
    Heavy = 3,
}

/// <summary>One effect for one weather: what to draw, how strongly, and its variants.</summary>
/// <param name="Kind">What is drawn.</param>
/// <param name="Level">How strong (ignored for <see cref="WeatherEffectKind.None"/>).</param>
/// <param name="IsDay">True for the day colours, false for the night colours.</param>
/// <param name="Icy">Rain with an icy tint (freezing drizzle and freezing rain).</param>
/// <param name="Lightning">A rare, soft, brief flash (thunderstorm).</param>
internal readonly record struct WeatherEffect(WeatherEffectKind Kind, EffectLevel Level, bool IsDay, bool Icy = false, bool Lightning = false)
{
    public static WeatherEffect None { get; } = new(WeatherEffectKind.None, EffectLevel.Light, true);
}

/// <summary>
/// Which animation each Open-Meteo (WMO) weather code gets. The one table is <see cref="Table"/>: a code that is not in it
/// (unknown, or no weather yet) gets no effect. Clear sky is the only code whose effect depends on day or night (glints or
/// stars); every other effect keeps its kind and takes day or night colours.
/// </summary>
internal static class WeatherEffectMapping
{
    /// <summary>Every code the app understands (the same set as <see cref="WeatherCodes.Describe"/>) with its effect.</summary>
    public static IReadOnlyDictionary<int, (WeatherEffectKind Kind, EffectLevel Level, bool Icy, bool Lightning)> Table { get; } =
        new Dictionary<int, (WeatherEffectKind, EffectLevel, bool, bool)>
        {
            [0] = (WeatherEffectKind.SunGlints, EffectLevel.Light, false, false), // clear sky: glints by day, stars by night
            [1] = (WeatherEffectKind.SunGlints, EffectLevel.Light, false, false), // mainly clear
            [2] = (WeatherEffectKind.Clouds, EffectLevel.Light, false, false), // partly cloudy
            [3] = (WeatherEffectKind.Clouds, EffectLevel.Heavy, false, false), // overcast
            [45] = (WeatherEffectKind.Haze, EffectLevel.Light, false, false), // fog
            [48] = (WeatherEffectKind.Haze, EffectLevel.Moderate, false, false), // depositing rime fog
            [51] = (WeatherEffectKind.Rain, EffectLevel.Light, false, false), // drizzle: light
            [53] = (WeatherEffectKind.Rain, EffectLevel.Light, false, false), // drizzle: moderate
            [55] = (WeatherEffectKind.Rain, EffectLevel.Moderate, false, false), // drizzle: dense
            [56] = (WeatherEffectKind.Rain, EffectLevel.Light, true, false), // freezing drizzle: light
            [57] = (WeatherEffectKind.Rain, EffectLevel.Moderate, true, false), // freezing drizzle: dense
            [61] = (WeatherEffectKind.Rain, EffectLevel.Light, false, false), // rain: slight
            [63] = (WeatherEffectKind.Rain, EffectLevel.Moderate, false, false), // rain: moderate
            [65] = (WeatherEffectKind.Rain, EffectLevel.Heavy, false, false), // rain: heavy
            [66] = (WeatherEffectKind.Rain, EffectLevel.Moderate, true, false), // freezing rain: light
            [67] = (WeatherEffectKind.Rain, EffectLevel.Heavy, true, false), // freezing rain: heavy
            [71] = (WeatherEffectKind.Snow, EffectLevel.Light, false, false), // snow fall: slight
            [73] = (WeatherEffectKind.Snow, EffectLevel.Moderate, false, false), // snow fall: moderate
            [75] = (WeatherEffectKind.Snow, EffectLevel.Heavy, false, false), // snow fall: heavy
            [77] = (WeatherEffectKind.Snow, EffectLevel.Light, false, false), // snow grains
            [80] = (WeatherEffectKind.Rain, EffectLevel.Light, false, false), // rain showers: slight
            [81] = (WeatherEffectKind.Rain, EffectLevel.Moderate, false, false), // rain showers: moderate
            [82] = (WeatherEffectKind.Rain, EffectLevel.Heavy, false, false), // rain showers: violent
            [85] = (WeatherEffectKind.Snow, EffectLevel.Light, false, false), // snow showers: slight
            [86] = (WeatherEffectKind.Snow, EffectLevel.Heavy, false, false), // snow showers: heavy
            [95] = (WeatherEffectKind.Rain, EffectLevel.Heavy, false, true), // thunderstorm
            [96] = (WeatherEffectKind.Rain, EffectLevel.Heavy, false, true), // thunderstorm with slight hail
            [99] = (WeatherEffectKind.Rain, EffectLevel.Heavy, false, true), // thunderstorm with heavy hail
        };

    /// <summary>The effect for a weather code at day or night; <see cref="WeatherEffect.None"/> when there is no code or it is unknown.</summary>
    public static WeatherEffect For(int? code, bool isDay)
    {
        if (code is not { } value || !Table.TryGetValue(value, out var entry)) return WeatherEffect.None;
        var kind = entry.Kind == WeatherEffectKind.SunGlints && !isDay ? WeatherEffectKind.Stars : entry.Kind;
        return new WeatherEffect(kind, entry.Level, isDay, entry.Icy, entry.Lightning);
    }
}
