namespace HelloWorldBot.Weather;

/// <summary>A city resolved to coordinates. <see cref="Country"/> may be empty when the API gives none.</summary>
internal sealed record CityLocation(string Name, string Country, double Latitude, double Longitude)
{
    /// <summary>"City, Country", or just the city when there is no country.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Country) ? Name : $"{Name}, {Country}";

    /// <summary>The city used on first run, and whenever the settings file is missing or unusable.</summary>
    public static CityLocation Default { get; } = new("Hanoi", "Vietnam", 21.0245, 105.84117);
}

/// <summary>Current conditions at a city, as reported by Open-Meteo.</summary>
internal sealed record WeatherReport(
    double TemperatureCelsius,
    int HumidityPercent,
    double WindSpeedKmh,
    int WeatherCode,
    bool IsDay = true,
    double? WindDirectionDegrees = null);

/// <summary>Any failure to look up a city or its weather: network, timeout, HTTP status or unexpected data.</summary>
internal sealed class WeatherException : Exception
{
    public WeatherException(string message) : base(message) { }

    public WeatherException(string message, Exception inner) : base(message, inner) { }
}
