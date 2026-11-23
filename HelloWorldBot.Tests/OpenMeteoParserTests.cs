using HelloWorldBot.Weather;

namespace HelloWorldBot.Tests;

public class OpenMeteoParserTests
{
    private const string HanoiResponse = """
        {"results":[{"id":1581130,"name":"Hanoi","latitude":21.0245,"longitude":105.84117,"country":"Vietnam","admin1":"Hanoi"},
                    {"id":1,"name":"Other","latitude":1,"longitude":2,"country":"Elsewhere"}]}
        """;

    [Fact]
    public void Geocoding_uses_the_top_result()
    {
        var city = OpenMeteoParser.ParseGeocoding(HanoiResponse);

        Assert.Equal(new CityLocation("Hanoi", "Vietnam", 21.0245, 105.84117), city);
        Assert.Equal("Hanoi, Vietnam", city!.DisplayName);
    }

    [Theory]
    [InlineData("""{"generationtime_ms":0.05}""")]
    [InlineData("""{"results":[]}""")]
    [InlineData("[]")]
    public void Geocoding_without_matches_returns_null(string json)
    {
        Assert.Null(OpenMeteoParser.ParseGeocoding(json));
    }

    [Fact]
    public void Geocoding_without_a_country_shows_only_the_name()
    {
        var city = OpenMeteoParser.ParseGeocoding("""{"results":[{"name":"Atlantis","latitude":0,"longitude":0}]}""");

        Assert.Equal("Atlantis", city!.DisplayName);
    }

    [Theory]
    [InlineData("""{"results":[{"name":"NoCoords"}]}""")]
    [InlineData("""{"results":[{"latitude":1,"longitude":2}]}""")]
    [InlineData("not json")]
    [InlineData("")]
    public void Geocoding_with_bad_data_throws_WeatherException(string json)
    {
        Assert.Throws<WeatherException>(() => OpenMeteoParser.ParseGeocoding(json));
    }

    [Fact]
    public void Forecast_reads_the_current_conditions()
    {
        const string json = """
            {"latitude":21.05,"current_units":{"temperature_2m":"°C"},
             "current":{"time":"2026-11-23T18:30","temperature_2m":24.5,"relative_humidity_2m":86,"weather_code":51,"wind_speed_10m":6.6}}
            """;

        var report = OpenMeteoParser.ParseForecast(json);

        Assert.Equal(new WeatherReport(24.5, 86, 6.6, 51), report);
    }

    [Fact]
    public void Forecast_accepts_whole_numbers_and_negative_temperatures()
    {
        const string json = """{"current":{"temperature_2m":-3,"relative_humidity_2m":100,"weather_code":71,"wind_speed_10m":0}}""";

        var report = OpenMeteoParser.ParseForecast(json);

        Assert.Equal(new WeatherReport(-3, 100, 0, 71), report);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"current":{}}""")]
    [InlineData("""{"current":{"temperature_2m":1,"relative_humidity_2m":1,"weather_code":1}}""")]
    [InlineData("""{"current":{"temperature_2m":"warm","relative_humidity_2m":1,"weather_code":1,"wind_speed_10m":1}}""")]
    [InlineData("""{"current":[]}""")]
    [InlineData("""{"error":true,"reason":"Latitude must be in range of -90 to 90"}""")]
    [InlineData("<html>Bad gateway</html>")]
    public void Forecast_with_missing_or_wrong_data_throws_WeatherException(string json)
    {
        Assert.Throws<WeatherException>(() => OpenMeteoParser.ParseForecast(json));
    }

    [Theory]
    [InlineData(0, "Clear sky")]
    [InlineData(3, "Overcast")]
    [InlineData(45, "Fog")]
    [InlineData(61, "Rain")]
    [InlineData(75, "Snow")]
    [InlineData(95, "Thunderstorm")]
    [InlineData(1234, "Unknown conditions")]
    public void Weather_codes_have_text(int code, string expected)
    {
        Assert.Equal(expected, WeatherCodes.Describe(code).Text);
    }
}
