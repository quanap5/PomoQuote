using System.Text.Json;

namespace HelloWorldBot.Weather;

/// <summary>Turns Open-Meteo geocoding and forecast JSON into models. Pure, so it is unit tested.</summary>
internal static class OpenMeteoParser
{
    /// <summary>The top geocoding match, or null when the search found nothing.</summary>
    public static CityLocation? ParseGeocoding(string json)
    {
        using var document = Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            return null;
        }

        var top = results[0];
        var name = GetString(top, "name");
        if (string.IsNullOrWhiteSpace(name) ||
            !TryGetNumber(top, "latitude", out var latitude) ||
            !TryGetNumber(top, "longitude", out var longitude))
        {
            throw new WeatherException("The city search returned incomplete data.");
        }

        return new CityLocation(name, GetString(top, "country") ?? "", latitude, longitude);
    }

    /// <summary>The current conditions from a forecast response (<c>current=</c> variables).</summary>
    public static WeatherReport ParseForecast(string json)
    {
        using var document = Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("current", out var current) ||
            current.ValueKind != JsonValueKind.Object ||
            !TryGetNumber(current, "temperature_2m", out var temperature) ||
            !TryGetNumber(current, "relative_humidity_2m", out var humidity) ||
            !TryGetNumber(current, "wind_speed_10m", out var wind) ||
            !TryGetNumber(current, "weather_code", out var code))
        {
            throw new WeatherException("The weather service returned incomplete data.");
        }

        // is_day and wind_direction_10m are optional: an older or partial answer still parses (day, no direction).
        var isDay = !TryGetNumber(current, "is_day", out var day) || day >= 0.5;
        double? direction = TryGetNumber(current, "wind_direction_10m", out var degrees) && degrees is >= 0 and <= 360 ? degrees : null;
        return new WeatherReport(temperature, (int)Math.Round(humidity), wind, (int)code, isDay, direction);
    }

    private static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new WeatherException("The weather service returned unreadable data.", ex);
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryGetNumber(JsonElement element, string name, out double number)
    {
        number = 0;
        return element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetDouble(out number);
    }
}
