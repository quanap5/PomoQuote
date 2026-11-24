using PomoQuote.Weather;

namespace PomoQuote.Tests;

public class WeatherLinesTests
{
    private static readonly DateTime Updated = new(2026, 11, 24, 10, 42, 0);

    private static WeatherSnapshot Fresh(WeatherReport? report = null) =>
        new("Hanoi, Vietnam", report ?? new WeatherReport(24.5, 86, 6.6, 51, true, 40), Updated, false);

    private static IEnumerable<WeatherIconId> Icons(IReadOnlyList<LinePart> line) => line.OfType<LinePart.Icon>().Select(i => i.Id);

    private static IEnumerable<string> Texts(IReadOnlyList<LinePart> line) => line.OfType<LinePart.Text>().Select(t => t.Value);

    /// <summary>True when every piece of text has an icon right before it (ignoring spacing).</summary>
    private static bool EveryTextHasItsIcon(IReadOnlyList<LinePart> line)
    {
        var parts = line.Where(p => p is not LinePart.Gap).ToList();
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i] is LinePart.Text && (i == 0 || parts[i - 1] is not LinePart.Icon)) return false;
        }

        return true;
    }

    [Fact]
    public void The_place_comes_with_a_pin()
    {
        var line = WeatherLines.City(Fresh());

        Assert.Equal([WeatherIconId.Location], Icons(line));
        Assert.Equal(["Hanoi, Vietnam"], Texts(line));
    }

    [Fact]
    public void No_city_means_no_line_at_all()
    {
        Assert.Empty(WeatherLines.City(new WeatherSnapshot("", null, default, false)));
    }

    [Fact]
    public void Temperature_and_conditions_each_have_an_icon()
    {
        var line = WeatherLines.Value(Fresh());

        Assert.Equal([WeatherIconId.Thermometer, WeatherIconId.Drizzle], Icons(line));
        Assert.Contains("24.5 °C", Texts(line));
        Assert.Contains("Drizzle", Texts(line));
    }

    [Fact]
    public void The_condition_icon_follows_the_time_of_day()
    {
        var night = WeatherLines.Value(Fresh(new WeatherReport(18, 70, 3, 0, IsDay: false)));
        var day = WeatherLines.Value(Fresh(new WeatherReport(18, 70, 3, 0, IsDay: true)));

        Assert.Contains(WeatherIconId.ClearNight, Icons(night));
        Assert.Contains(WeatherIconId.ClearDay, Icons(day));
    }

    [Fact]
    public void Humidity_wind_and_wind_direction_each_have_an_icon()
    {
        var line = WeatherLines.Detail(Fresh());

        Assert.Equal([WeatherIconId.Humidity, WeatherIconId.Wind, WeatherIconId.WindDirection], Icons(line));
        Assert.Equal(["86%", "6.6 km/h", "NE"], Texts(line));
        Assert.Equal(40, line.OfType<LinePart.Icon>().Single(i => i.Id == WeatherIconId.WindDirection).WindDegrees);
    }

    [Fact]
    public void Without_a_wind_direction_there_is_no_arrow_and_nothing_is_missing_an_icon()
    {
        var line = WeatherLines.Detail(Fresh(new WeatherReport(24.5, 86, 6.6, 51)));

        Assert.Equal([WeatherIconId.Humidity, WeatherIconId.Wind], Icons(line));
        Assert.True(EveryTextHasItsIcon(line));
    }

    [Fact]
    public void Stale_values_are_dimmed_and_a_warning_with_the_update_time_is_added()
    {
        var stale = Fresh() with { Failed = true };

        var value = WeatherLines.Value(stale);
        var detail = WeatherLines.Detail(stale);

        Assert.True(stale.IsStale);
        Assert.All(value.OfType<LinePart.Icon>().Where(i => i.Id == WeatherIconId.Drizzle), icon => Assert.True(icon.Alpha < 1));
        Assert.Contains(WeatherIconId.Warning, Icons(detail));
        Assert.Contains(Texts(detail), text => text.Contains("10:42"));
        Assert.True(EveryTextHasItsIcon(detail));
    }

    [Fact]
    public void A_failed_first_load_shows_a_warning_not_a_blank()
    {
        var snapshot = new WeatherSnapshot("Hanoi, Vietnam", null, default, true);

        Assert.Equal([WeatherIconId.Warning], Icons(WeatherLines.Value(snapshot)));
        Assert.Equal(["Weather unavailable"], Texts(WeatherLines.Value(snapshot)));
        Assert.Equal([WeatherIconId.Updated], Icons(WeatherLines.Detail(snapshot)));
        Assert.Equal([WeatherIconId.Warning], Icons(WeatherLines.Summary(snapshot)));
    }

    [Fact]
    public void Loading_shows_a_clock()
    {
        var snapshot = new WeatherSnapshot("Hanoi, Vietnam", null, default, false);

        Assert.Equal([WeatherIconId.Updated], Icons(WeatherLines.Value(snapshot)));
        Assert.Empty(WeatherLines.Detail(snapshot));
    }

    [Fact]
    public void The_widget_summary_has_the_temperature_and_condition_icons_and_a_stale_warning()
    {
        var summary = WeatherLines.Summary(Fresh());
        var stale = WeatherLines.Summary(Fresh() with { Failed = true });

        Assert.Equal([WeatherIconId.Thermometer, WeatherIconId.Drizzle], Icons(summary));
        Assert.Equal([WeatherIconId.Thermometer, WeatherIconId.Drizzle, WeatherIconId.Warning], Icons(stale));
        Assert.True(EveryTextHasItsIcon(summary));
        Assert.True(EveryTextHasItsIcon(stale));
    }

    [Fact]
    public void Nothing_weather_related_is_ever_shown_without_an_icon()
    {
        var codes = new[] { 0, 1, 2, 3, 45, 48, 51, 53, 55, 56, 57, 61, 63, 65, 66, 67, 71, 73, 75, 77, 80, 81, 82, 85, 86, 95, 96, 99, 4, 1234, -7 };
        var checkedLines = 0;

        foreach (var code in codes)
        foreach (var isDay in new[] { true, false })
        foreach (var direction in new double?[] { null, 0, 123.4, 360 })
        foreach (var failed in new[] { false, true })
        {
            var snapshot = new WeatherSnapshot("Hanoi, Vietnam", new WeatherReport(-3.2, 100, 0, code, isDay, direction), Updated, failed);
            foreach (var line in new[] { WeatherLines.City(snapshot), WeatherLines.Value(snapshot), WeatherLines.Detail(snapshot), WeatherLines.Summary(snapshot) })
            {
                Assert.True(EveryTextHasItsIcon(line), $"code {code} day={isDay} dir={direction} failed={failed}: a value has no icon");
                checkedLines++;
            }
        }

        foreach (var failed in new[] { false, true })
        {
            var snapshot = new WeatherSnapshot("Hanoi, Vietnam", null, default, failed);
            foreach (var line in new[] { WeatherLines.City(snapshot), WeatherLines.Value(snapshot), WeatherLines.Detail(snapshot), WeatherLines.Summary(snapshot) })
            {
                Assert.True(EveryTextHasItsIcon(line));
            }
        }

        Assert.True(checkedLines > 400);
    }

    [Fact]
    public void Every_icon_a_line_uses_is_a_real_icon_never_a_missing_one()
    {
        var all = new HashSet<WeatherIconId>(Enum.GetValues<WeatherIconId>());

        foreach (var code in new[] { 0, 3, 61, 95, 1234 })
        {
            var snapshot = Fresh(new WeatherReport(10, 50, 5, code, true, 90)) with { Failed = true };
            foreach (var line in new[] { WeatherLines.City(snapshot), WeatherLines.Value(snapshot), WeatherLines.Detail(snapshot), WeatherLines.Summary(snapshot) })
            {
                Assert.All(Icons(line), id => Assert.Contains(id, all));
            }
        }
    }
}

public class WeatherParserIconFieldTests
{
    [Fact]
    public void Is_day_and_wind_direction_are_read_when_present()
    {
        const string json = """{"current":{"temperature_2m":19.1,"relative_humidity_2m":81,"weather_code":3,"wind_speed_10m":14.0,"is_day":1,"wind_direction_10m":24}}""";

        var report = OpenMeteoParser.ParseForecast(json);

        Assert.Equal(new WeatherReport(19.1, 81, 14.0, 3, true, 24), report);
    }

    [Fact]
    public void Is_day_zero_means_night()
    {
        const string json = """{"current":{"temperature_2m":10,"relative_humidity_2m":90,"weather_code":0,"wind_speed_10m":2,"is_day":0}}""";

        Assert.False(OpenMeteoParser.ParseForecast(json).IsDay);
    }

    [Fact]
    public void A_missing_is_day_is_day_and_a_missing_direction_is_null_so_older_answers_still_parse()
    {
        const string json = """{"current":{"temperature_2m":10,"relative_humidity_2m":90,"weather_code":0,"wind_speed_10m":2}}""";

        var report = OpenMeteoParser.ParseForecast(json);

        Assert.True(report.IsDay);
        Assert.Null(report.WindDirectionDegrees);
    }

    [Theory]
    [InlineData("\"is_day\":\"yes\"")]
    [InlineData("\"is_day\":null")]
    [InlineData("\"is_day\":[]")]
    public void A_garbled_is_day_is_ignored_not_fatal(string field)
    {
        var json = "{\"current\":{\"temperature_2m\":10,\"relative_humidity_2m\":90,\"weather_code\":0,\"wind_speed_10m\":2," + field + "}}";

        Assert.True(OpenMeteoParser.ParseForecast(json).IsDay);
    }

    [Theory]
    [InlineData("\"wind_direction_10m\":\"north\"")]
    [InlineData("\"wind_direction_10m\":null")]
    [InlineData("\"wind_direction_10m\":-5")]
    [InlineData("\"wind_direction_10m\":720")]
    public void A_garbled_or_out_of_range_wind_direction_is_ignored_not_fatal(string field)
    {
        var json = "{\"current\":{\"temperature_2m\":10,\"relative_humidity_2m\":90,\"weather_code\":0,\"wind_speed_10m\":2," + field + "}}";

        Assert.Null(OpenMeteoParser.ParseForecast(json).WindDirectionDegrees);
    }

    [Fact]
    public async Task The_forecast_request_asks_for_the_day_flag_and_the_wind_direction()
    {
        Uri? requested = null;
        var handler = new RecordingHandler(uri => requested = uri);
        var service = new WeatherService(new HttpClient(handler));

        await service.GetCurrentAsync(CityLocation.Default);

        Assert.Contains("is_day", requested!.Query);
        Assert.Contains("wind_direction_10m", requested.Query);
        Assert.Contains("weather_code", requested.Query);
    }

    private sealed class RecordingHandler(Action<Uri> record) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            record(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"current":{"temperature_2m":1,"relative_humidity_2m":2,"weather_code":3,"wind_speed_10m":4,"is_day":0,"wind_direction_10m":90}}"""),
            });
        }
    }
}
