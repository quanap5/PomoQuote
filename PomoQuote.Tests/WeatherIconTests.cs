using System.Drawing;
using System.Drawing.Imaging;
using PomoQuote.Weather;

namespace PomoQuote.Tests;

public class WeatherIconMappingTests
{
    // Every WMO code the app knows, with the icons expected by day and by night.
    public static TheoryData<int, string, WeatherIconId, WeatherIconId> Codes => new()
    {
        { 0, "Clear sky", WeatherIconId.ClearDay, WeatherIconId.ClearNight },
        { 1, "Mainly clear", WeatherIconId.MainlyClearDay, WeatherIconId.MainlyClearNight },
        { 2, "Partly cloudy", WeatherIconId.PartlyCloudyDay, WeatherIconId.PartlyCloudyNight },
        { 3, "Overcast", WeatherIconId.Overcast, WeatherIconId.Overcast },
        { 45, "Fog", WeatherIconId.Fog, WeatherIconId.Fog },
        { 48, "Fog", WeatherIconId.Fog, WeatherIconId.Fog },
        { 51, "Drizzle", WeatherIconId.Drizzle, WeatherIconId.Drizzle },
        { 53, "Drizzle", WeatherIconId.Drizzle, WeatherIconId.Drizzle },
        { 55, "Drizzle", WeatherIconId.Drizzle, WeatherIconId.Drizzle },
        { 56, "Freezing drizzle", WeatherIconId.FreezingDrizzle, WeatherIconId.FreezingDrizzle },
        { 57, "Freezing drizzle", WeatherIconId.FreezingDrizzle, WeatherIconId.FreezingDrizzle },
        { 61, "Rain", WeatherIconId.Rain, WeatherIconId.Rain },
        { 63, "Rain", WeatherIconId.Rain, WeatherIconId.Rain },
        { 65, "Rain", WeatherIconId.Rain, WeatherIconId.Rain },
        { 66, "Freezing rain", WeatherIconId.FreezingRain, WeatherIconId.FreezingRain },
        { 67, "Freezing rain", WeatherIconId.FreezingRain, WeatherIconId.FreezingRain },
        { 71, "Snow", WeatherIconId.Snow, WeatherIconId.Snow },
        { 73, "Snow", WeatherIconId.Snow, WeatherIconId.Snow },
        { 75, "Snow", WeatherIconId.Snow, WeatherIconId.Snow },
        { 77, "Snow grains", WeatherIconId.SnowGrains, WeatherIconId.SnowGrains },
        { 80, "Rain showers", WeatherIconId.RainShowersDay, WeatherIconId.RainShowersNight },
        { 81, "Rain showers", WeatherIconId.RainShowersDay, WeatherIconId.RainShowersNight },
        { 82, "Rain showers", WeatherIconId.RainShowersDay, WeatherIconId.RainShowersNight },
        { 85, "Snow showers", WeatherIconId.SnowShowersDay, WeatherIconId.SnowShowersNight },
        { 86, "Snow showers", WeatherIconId.SnowShowersDay, WeatherIconId.SnowShowersNight },
        { 95, "Thunderstorm", WeatherIconId.Thunderstorm, WeatherIconId.Thunderstorm },
        { 96, "Thunderstorm with hail", WeatherIconId.ThunderstormHail, WeatherIconId.ThunderstormHail },
        { 99, "Thunderstorm with hail", WeatherIconId.ThunderstormHail, WeatherIconId.ThunderstormHail },
    };

    [Theory]
    [MemberData(nameof(Codes))]
    public void Every_known_code_has_its_text_and_its_day_and_night_icon(int code, string text, WeatherIconId day, WeatherIconId night)
    {
        var byDay = WeatherCodes.Describe(code, isDay: true);
        var byNight = WeatherCodes.Describe(code, isDay: false);

        Assert.Equal((text, day), (byDay.Text, byDay.Icon));
        Assert.Equal((text, night), (byNight.Text, byNight.Icon));
    }

    [Fact]
    public void Day_is_the_default()
    {
        Assert.Equal(WeatherIconId.ClearDay, WeatherCodes.Describe(0).Icon);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(44)]
    [InlineData(100)]
    [InlineData(1234)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void An_unknown_code_gets_the_neutral_icon_by_day_and_night(int code)
    {
        foreach (var isDay in new[] { true, false })
        {
            var condition = WeatherCodes.Describe(code, isDay);

            Assert.Equal(WeatherIconId.Unknown, condition.Icon);
            Assert.False(string.IsNullOrWhiteSpace(condition.Text));
        }
    }

    [Fact]
    public void The_fifteen_kinds_of_weather_each_have_a_distinct_icon()
    {
        var kinds = new[] { 0, 1, 2, 3, 45, 51, 56, 61, 66, 71, 77, 80, 85, 95, 96 };

        var icons = kinds.Select(code => WeatherCodes.Describe(code).Icon).ToList();

        Assert.Equal(kinds.Length, icons.Distinct().Count());
        Assert.DoesNotContain(WeatherIconId.Unknown, icons);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(80)]
    [InlineData(85)]
    public void Clear_cloudy_and_shower_weather_looks_different_by_night(int code)
    {
        Assert.NotEqual(WeatherCodes.Describe(code, true).Icon, WeatherCodes.Describe(code, false).Icon);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(45)]
    [InlineData(61)]
    [InlineData(71)]
    [InlineData(95)]
    public void Weather_without_a_sun_or_moon_looks_the_same_day_and_night(int code)
    {
        Assert.Equal(WeatherCodes.Describe(code, true).Icon, WeatherCodes.Describe(code, false).Icon);
    }

    [Theory]
    [InlineData(0, "N")]
    [InlineData(22, "N")]
    [InlineData(23, "NE")]
    [InlineData(90, "E")]
    [InlineData(180, "S")]
    [InlineData(270, "W")]
    [InlineData(315, "NW")]
    [InlineData(359, "N")]
    [InlineData(360, "N")]
    [InlineData(-45, "NW")]
    public void Wind_direction_names_the_eight_compass_points(double degrees, string expected)
    {
        Assert.Equal(expected, WeatherCodes.CompassName(degrees));
    }
}

public class WeatherIconRenderingTests
{
    private static readonly WeatherIconId[] All = Enum.GetValues<WeatherIconId>();

    private static int OpaquePixels(Bitmap bitmap)
    {
        var count = 0;
        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetPixel(x, y).A > 40) count++;
            }
        }

        return count;
    }

    [Fact]
    public void There_are_28_icons_21_conditions_and_7_data_icons()
    {
        Assert.Equal(28, All.Length);
        Assert.Equal(21, All.Count(id => id <= WeatherIconId.Unknown));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(64)]
    public void Every_icon_draws_something_visible_at_every_size_the_app_uses(int size)
    {
        using var cache = new WeatherIconCache();

        foreach (var id in All)
        {
            var bitmap = cache.Get(id, size);

            Assert.Equal((size, size), (bitmap.Width, bitmap.Height));
            Assert.Equal(PixelFormat.Format32bppPArgb, bitmap.PixelFormat);
            var opaque = OpaquePixels(bitmap);
            Assert.True(opaque > size * size * 0.1, $"{id} at {size}px has only {opaque} visible pixels");
            Assert.True(opaque < size * size * 0.95, $"{id} at {size}px fills its whole square");
        }
    }

    [Fact]
    public void Icons_keep_a_transparent_margin_so_the_outline_is_never_cut_off()
    {
        using var cache = new WeatherIconCache();

        foreach (var id in All)
        {
            var bitmap = cache.Get(id, 48);
            var edge = 0;
            for (var i = 0; i < 48; i++)
            {
                edge = Math.Max(edge, Math.Max(Math.Max(bitmap.GetPixel(i, 0).A, bitmap.GetPixel(0, i).A), Math.Max(bitmap.GetPixel(47, i).A, bitmap.GetPixel(i, 47).A)));
            }

            Assert.True(edge == 0, $"{id} touches the edge of its square (alpha {edge})");
        }
    }

    [Fact]
    public void All_icons_look_different_from_each_other()
    {
        using var cache = new WeatherIconCache();
        var seen = new Dictionary<string, WeatherIconId>();

        foreach (var id in All)
        {
            var bitmap = cache.Get(id, 48);
            var pixels = new System.Text.StringBuilder();
            for (var y = 0; y < 48; y++)
            {
                for (var x = 0; x < 48; x++) pixels.Append(bitmap.GetPixel(x, y).ToArgb().ToString("X8"));
            }

            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(pixels.ToString())));
            Assert.True(seen.TryAdd(hash, id), $"{id} is drawn exactly like {(seen.TryGetValue(hash, out var other) ? other : default)}");
        }
    }

    [Fact]
    public void Icons_are_vector_drawings_sharp_at_every_size_not_enlargements()
    {
        using var cache = new WeatherIconCache();

        // A 96 px icon has detail a scaled-up 24 px icon could not: many more distinct edge pixels.
        var small = Enumerable.Range(0, 24).SelectMany(x => Enumerable.Range(0, 24).Select(y => cache.Get(WeatherIconId.Rain, 24).GetPixel(x, y).ToArgb())).Distinct().Count();
        var large = Enumerable.Range(0, 96).SelectMany(x => Enumerable.Range(0, 96).Select(y => cache.Get(WeatherIconId.Rain, 96).GetPixel(x, y).ToArgb())).Distinct().Count();

        Assert.True(large > small);
    }

    [Fact]
    public void The_wind_arrow_points_where_the_wind_blows()
    {
        using var cache = new WeatherIconCache();

        // Wind from the north (0) blows south: the arrow head is at the bottom. Wind from the south (180): head at the top.
        int WeightedRow(Bitmap bitmap)
        {
            long sum = 0, count = 0;
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).A > 40) { sum += y; count++; }
                }
            }

            return (int)(sum / count);
        }

        var north = cache.Get(WeatherIconId.WindDirection, 64, 0);
        var south = cache.Get(WeatherIconId.WindDirection, 64, 180);
        Assert.NotSame(north, south);
        // The arrow's wide tail carries most of its pixels: it is at the top for a north wind (head pointing down).
        Assert.True(WeightedRow(north) < WeightedRow(south));
    }

    // -- The cache ----------------------------------------------------------------------------------------------

    [Fact]
    public void The_cache_hands_out_the_same_bitmap_for_the_same_icon_and_size()
    {
        using var cache = new WeatherIconCache();

        Assert.Same(cache.Get(WeatherIconId.Rain, 24), cache.Get(WeatherIconId.Rain, 24));
        Assert.NotSame(cache.Get(WeatherIconId.Rain, 24), cache.Get(WeatherIconId.Rain, 32));
        Assert.NotSame(cache.Get(WeatherIconId.Rain, 24), cache.Get(WeatherIconId.Snow, 24));
        Assert.Equal(3, cache.Count);
    }

    [Fact]
    public void Wind_directions_are_rounded_to_15_degree_steps_to_keep_the_cache_small()
    {
        using var cache = new WeatherIconCache();

        Assert.Same(cache.Get(WeatherIconId.WindDirection, 20, 91), cache.Get(WeatherIconId.WindDirection, 20, 96));
        Assert.Same(cache.Get(WeatherIconId.WindDirection, 20, 0), cache.Get(WeatherIconId.WindDirection, 20, 359));
        Assert.Equal(2, cache.Count);
        Assert.Same(cache.Get(WeatherIconId.Rain, 20, 10), cache.Get(WeatherIconId.Rain, 20, 200)); // the angle only matters for the arrow
    }

    [Fact]
    public void Many_requests_never_grow_the_cache_beyond_the_distinct_icons_and_sizes()
    {
        using var cache = new WeatherIconCache();
        var random = new Random(5);

        for (var i = 0; i < 20000; i++)
        {
            cache.Get(All[random.Next(All.Length)], new[] { 14, 16, 20, 24, 32 }[random.Next(5)], random.Next(360));
        }

        Assert.True(cache.Count <= All.Length * 5 + 24 * 5, $"cache grew to {cache.Count}");
    }

    [Fact]
    public void Disposing_the_cache_releases_every_bitmap_and_refuses_further_use()
    {
        var cache = new WeatherIconCache();
        var bitmap = cache.Get(WeatherIconId.Fog, 24);

        cache.Dispose();

        Assert.Throws<ArgumentException>(() => bitmap.GetPixel(0, 0)); // a disposed Bitmap
        Assert.Throws<ObjectDisposedException>(() => cache.Get(WeatherIconId.Fog, 24));
        cache.Dispose(); // twice is fine
    }

    [Fact]
    public void Extreme_sizes_are_pulled_into_a_sane_range()
    {
        using var cache = new WeatherIconCache();

        Assert.Equal(8, cache.Get(WeatherIconId.Rain, 1).Width);
        Assert.Equal(512, cache.Get(WeatherIconId.Rain, 100000).Width);
    }
}
