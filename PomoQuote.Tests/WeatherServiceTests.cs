using System.Net;
using PomoQuote.Weather;

namespace PomoQuote.Tests;

public class WeatherServiceTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return respond(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body) };

    private static (WeatherService Service, StubHandler Handler) Create(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        var handler = new StubHandler(respond);
        return (new WeatherService(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task Geocode_escapes_the_city_and_parses_the_answer()
    {
        var (service, handler) = Create((_, _) => Task.FromResult(Json("""{"results":[{"name":"São Paulo","latitude":-23.5,"longitude":-46.6,"country":"Brazil"}]}""")));

        var city = await service.GeocodeAsync("São Paulo & co");

        Assert.Equal("São Paulo, Brazil", city!.DisplayName);
        var query = handler.Requests.Single().Query;
        Assert.Contains("name=S%C3%A3o%20Paulo%20%26%20co", query);
        Assert.Contains("count=1", query);
    }

    [Fact]
    public async Task Forecast_uses_invariant_coordinates_and_the_current_variables()
    {
        var (service, handler) = Create((_, _) => Task.FromResult(Json("""{"current":{"temperature_2m":20,"relative_humidity_2m":50,"weather_code":0,"wind_speed_10m":5}}""")));

        var report = await service.GetCurrentAsync(new CityLocation("Hanoi", "Vietnam", 21.0245, 105.84117));

        Assert.Equal(new WeatherReport(20, 50, 5, 0), report);
        var uri = handler.Requests.Single();
        Assert.Contains("latitude=21.0245", uri.Query);
        Assert.Contains("longitude=105.84117", uri.Query);
        Assert.Contains("current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m", uri.Query);
    }

    [Fact]
    public async Task Server_error_becomes_WeatherException()
    {
        var (service, _) = Create((_, _) => Task.FromResult(Json("oops", HttpStatusCode.InternalServerError)));

        await Assert.ThrowsAsync<WeatherException>(() => service.GetCurrentAsync(CityLocation.Default));
    }

    [Fact]
    public async Task Network_failure_becomes_WeatherException()
    {
        var (service, _) = Create((_, _) => throw new HttpRequestException("no route to host"));

        await Assert.ThrowsAsync<WeatherException>(() => service.GeocodeAsync("Hanoi"));
    }

    [Fact]
    public async Task Timeout_becomes_WeatherException()
    {
        var http = new HttpClient(new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Json("");
        }))
        { Timeout = TimeSpan.FromMilliseconds(100) };

        await Assert.ThrowsAsync<WeatherException>(() => new WeatherService(http).GetCurrentAsync(CityLocation.Default));
    }

    [Fact]
    public async Task Caller_cancellation_stays_a_cancellation()
    {
        var (service, _) = Create(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Json("");
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetCurrentAsync(CityLocation.Default, cts.Token));
    }

    [Fact]
    public async Task Garbage_body_becomes_WeatherException()
    {
        var (service, _) = Create((_, _) => Task.FromResult(Json("<html>captive portal</html>")));

        await Assert.ThrowsAsync<WeatherException>(() => service.GetCurrentAsync(CityLocation.Default));
    }
}
