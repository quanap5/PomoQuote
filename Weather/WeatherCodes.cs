namespace HelloWorldBot.Weather;

/// <summary>Text and a simple symbol for a WMO weather interpretation code (as used by Open-Meteo).</summary>
internal static class WeatherCodes
{
    public static (string Text, string Symbol) Describe(int code) => code switch
    {
        0 => ("Clear sky", "☀"),
        1 => ("Mainly clear", "☀"),
        2 => ("Partly cloudy", "☁"),
        3 => ("Overcast", "☁"),
        45 or 48 => ("Fog", "≈"),
        51 or 53 or 55 => ("Drizzle", "☂"),
        56 or 57 => ("Freezing drizzle", "☂"),
        61 or 63 or 65 => ("Rain", "☂"),
        66 or 67 => ("Freezing rain", "☂"),
        71 or 73 or 75 => ("Snow", "❄"),
        77 => ("Snow grains", "❄"),
        80 or 81 or 82 => ("Rain showers", "☂"),
        85 or 86 => ("Snow showers", "❄"),
        95 => ("Thunderstorm", "⚡"),
        96 or 99 => ("Thunderstorm with hail", "⚡"),
        _ => ("Unknown conditions", "?"),
    };
}
