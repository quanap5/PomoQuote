using System.Globalization;

namespace HelloWorldBot.Weather;

/// <summary>
/// Calls the free Open-Meteo geocoding and forecast APIs (no API key). Every failure, including
/// a timeout, surfaces as <see cref="WeatherException"/>; only the caller's own cancellation
/// surfaces as <see cref="OperationCanceledException"/>.
/// </summary>
internal sealed class WeatherService
{
    private const string GeocodingUrl = "https://geocoding-api.open-meteo.com/v1/search";
    private const string ForecastUrl = "https://api.open-meteo.com/v1/forecast";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;

    /// <summary>The app-wide service. It owns the one HttpClient, which is meant to be reused.</summary>
    public static WeatherService Shared { get; } = new(new HttpClient { Timeout = RequestTimeout });

    public WeatherService(HttpClient http) => _http = http;

    /// <summary>Resolves a city name to coordinates (top match), or null when nothing matches.</summary>
    public async Task<CityLocation?> GeocodeAsync(string cityName, CancellationToken cancellationToken = default)
    {
        var url = $"{GeocodingUrl}?name={Uri.EscapeDataString(cityName)}&count=1&language=en&format=json";
        return OpenMeteoParser.ParseGeocoding(await GetStringAsync(url, cancellationToken));
    }

    public async Task<WeatherReport> GetCurrentAsync(CityLocation city, CancellationToken cancellationToken = default)
    {
        var latitude = city.Latitude.ToString("0.#####", CultureInfo.InvariantCulture);
        var longitude = city.Longitude.ToString("0.#####", CultureInfo.InvariantCulture);
        var url = $"{ForecastUrl}?latitude={latitude}&longitude={longitude}" +
                  "&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m&timezone=auto";
        return OpenMeteoParser.ParseForecast(await GetStringAsync(url, cancellationToken));
    }

    private async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new WeatherException($"The weather service answered {(int)response.StatusCode}.");
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WeatherException("The weather service did not answer in time.");
        }
        catch (HttpRequestException ex)
        {
            throw new WeatherException("Could not reach the weather service.", ex);
        }
    }
}
