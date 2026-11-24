namespace PomoQuote.Weather;

/// <summary>What the weather looks like: words and the icon that goes with them.</summary>
internal sealed record WeatherCondition(string Text, WeatherIconId Icon);

/// <summary>Text and icon for a WMO weather interpretation code (as used by Open-Meteo), with day and night variants.</summary>
internal static class WeatherCodes
{
    /// <summary>
    /// Describes <paramref name="code"/>. <paramref name="isDay"/> picks the sun or the moon where the icon has both
    /// (clear, mainly clear, partly cloudy, showers). An unknown code gives a neutral "?" icon, never nothing.
    /// </summary>
    public static WeatherCondition Describe(int code, bool isDay = true) => code switch
    {
        0 => new("Clear sky", isDay ? WeatherIconId.ClearDay : WeatherIconId.ClearNight),
        1 => new("Mainly clear", isDay ? WeatherIconId.MainlyClearDay : WeatherIconId.MainlyClearNight),
        2 => new("Partly cloudy", isDay ? WeatherIconId.PartlyCloudyDay : WeatherIconId.PartlyCloudyNight),
        3 => new("Overcast", WeatherIconId.Overcast),
        45 or 48 => new("Fog", WeatherIconId.Fog),
        51 or 53 or 55 => new("Drizzle", WeatherIconId.Drizzle),
        56 or 57 => new("Freezing drizzle", WeatherIconId.FreezingDrizzle),
        61 or 63 or 65 => new("Rain", WeatherIconId.Rain),
        66 or 67 => new("Freezing rain", WeatherIconId.FreezingRain),
        71 or 73 or 75 => new("Snow", WeatherIconId.Snow),
        77 => new("Snow grains", WeatherIconId.SnowGrains),
        80 or 81 or 82 => new("Rain showers", isDay ? WeatherIconId.RainShowersDay : WeatherIconId.RainShowersNight),
        85 or 86 => new("Snow showers", isDay ? WeatherIconId.SnowShowersDay : WeatherIconId.SnowShowersNight),
        95 => new("Thunderstorm", WeatherIconId.Thunderstorm),
        96 or 99 => new("Thunderstorm with hail", WeatherIconId.ThunderstormHail),
        _ => new("Unknown conditions", WeatherIconId.Unknown),
    };

    /// <summary>Eight-point compass name for the direction the wind comes from (0 = north).</summary>
    public static string CompassName(double degrees)
    {
        string[] names = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
        var index = (int)Math.Round(((degrees % 360) + 360) % 360 / 45) % 8;
        return names[index];
    }
}
