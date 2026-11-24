using System.Globalization;

namespace HelloWorldBot.Weather;

/// <summary>What the weather UI currently knows: the city, the last report (if any) and whether the latest refresh failed.</summary>
internal sealed record WeatherSnapshot(string CityName, WeatherReport? Report, DateTime ReportTime, bool Failed)
{
    /// <summary>True when the values shown are old because the latest refresh failed.</summary>
    public bool IsStale => Failed && Report is not null;
}

/// <summary>
/// Turns a <see cref="WeatherSnapshot"/> into lines of icons and text. Every weather datum (place, temperature,
/// conditions, humidity, wind, wind direction, last update, unavailable/stale) is paired with its own icon. Pure, so the
/// rule "nothing weather-related is shown without an icon" is unit tested.
/// </summary>
internal static class WeatherLines
{
    private const float StaleAlpha = 0.55f;

    /// <summary>"[pin] Hanoi, Vietnam".</summary>
    public static IReadOnlyList<LinePart> City(WeatherSnapshot snapshot) =>
        string.IsNullOrEmpty(snapshot.CityName)
            ? []
            : [new LinePart.Icon(WeatherIconId.Location), new LinePart.Text(snapshot.CityName)];

    /// <summary>"[thermometer] 24.5 °C  [condition] Drizzle", or a status line with its own icon.</summary>
    public static IReadOnlyList<LinePart> Value(WeatherSnapshot snapshot)
    {
        if (snapshot.Report is not { } report)
        {
            return snapshot.Failed
                ? [new LinePart.Icon(WeatherIconId.Warning), new LinePart.Text("Weather unavailable")]
                : [new LinePart.Icon(WeatherIconId.Updated), new LinePart.Text("Loading weather…")];
        }

        var condition = WeatherCodes.Describe(report.WeatherCode, report.IsDay);
        return
        [
            new LinePart.Icon(WeatherIconId.Thermometer),
            new LinePart.Text(Temperature(report)),
            new LinePart.Gap(1.5f),
            new LinePart.Icon(condition.Icon, snapshot.IsStale ? StaleAlpha : 1f, Scale: 1.12f),
            new LinePart.Text(condition.Text),
        ];
    }

    /// <summary>"[drop] 87%  [wind] 7 km/h [arrow] NE", plus a warning with the time of the last update when stale.</summary>
    public static IReadOnlyList<LinePart> Detail(WeatherSnapshot snapshot)
    {
        if (snapshot.Report is not { } report)
        {
            return snapshot.Failed ? [new LinePart.Icon(WeatherIconId.Updated), new LinePart.Text("Will try again shortly")] : [];
        }

        var alpha = snapshot.IsStale ? StaleAlpha : 1f;
        var parts = new List<LinePart>
        {
            new LinePart.Icon(WeatherIconId.Humidity, alpha),
            new LinePart.Text($"{report.HumidityPercent}%"),
            new LinePart.Gap(1.5f),
            new LinePart.Icon(WeatherIconId.Wind, alpha),
            new LinePart.Text($"{report.WindSpeedKmh.ToString("0.#", CultureInfo.CurrentCulture)} km/h"),
        };

        if (report.WindDirectionDegrees is { } degrees)
        {
            parts.Add(new LinePart.Gap(0.6f));
            parts.Add(new LinePart.Icon(WeatherIconId.WindDirection, alpha, degrees));
            parts.Add(new LinePart.Text(WeatherCodes.CompassName(degrees)));
        }

        if (snapshot.IsStale)
        {
            parts.Add(new LinePart.Gap(1.5f));
            parts.Add(new LinePart.Icon(WeatherIconId.Warning));
            parts.Add(new LinePart.Text($"unavailable, last updated {snapshot.ReportTime:HH:mm}"));
        }

        return parts;
    }

    /// <summary>The widget's one-line summary: "[thermometer] 24.5 °C  [condition] Drizzle", "[warning] stale" when old.</summary>
    public static IReadOnlyList<LinePart> Summary(WeatherSnapshot snapshot)
    {
        if (snapshot.Report is not { } report) return Value(snapshot);

        var condition = WeatherCodes.Describe(report.WeatherCode, report.IsDay);
        var parts = new List<LinePart>
        {
            new LinePart.Icon(WeatherIconId.Thermometer),
            new LinePart.Text(Temperature(report)),
            new LinePart.Gap(1.2f),
            new LinePart.Icon(condition.Icon, snapshot.IsStale ? StaleAlpha : 1f, Scale: 1.12f),
            new LinePart.Text(condition.Text),
        };

        if (snapshot.IsStale)
        {
            parts.Add(new LinePart.Gap(1.2f));
            parts.Add(new LinePart.Icon(WeatherIconId.Warning));
            parts.Add(new LinePart.Text("stale"));
        }

        return parts;
    }

    private static string Temperature(WeatherReport report) =>
        $"{report.TemperatureCelsius.ToString("0.#", CultureInfo.CurrentCulture)} °C";
}
