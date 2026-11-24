using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using PomoQuote.Weather;

namespace PomoQuote.Tests;

/// <summary>The weather effect: the code mapping, the particle pool, the pause rules, the settings file and the contrast budget.</summary>
public sealed class WeatherEffectTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PomoQuoteFx-" + Guid.NewGuid().ToString("N"));

    public WeatherEffectTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // ---------- the mapping ----------

    private static readonly int[] UsedCodes = [0, 1, 2, 3, 45, 48, 51, 53, 55, 56, 57, 61, 63, 65, 66, 67, 71, 73, 75, 77, 80, 81, 82, 85, 86, 95, 96, 99];

    [Fact]
    public void The_table_covers_exactly_the_codes_the_app_describes()
    {
        Assert.Equal(UsedCodes.OrderBy(c => c), WeatherEffectMapping.Table.Keys.OrderBy(c => c));
        for (var code = -2; code < 120; code++)
        {
            var known = WeatherCodes.Describe(code).Icon != WeatherIconId.Unknown;
            Assert.Equal(known, WeatherEffectMapping.Table.ContainsKey(code));
            Assert.Equal(known, WeatherEffectMapping.For(code, true).Kind != WeatherEffectKind.None);
        }
    }

    [Theory]
    [InlineData(0, 1, 1, 0, 0)] // kind (WeatherEffectKind), level, icy, lightning
    [InlineData(1, 1, 1, 0, 0)]
    [InlineData(2, 3, 1, 0, 0)]
    [InlineData(3, 3, 3, 0, 0)]
    [InlineData(45, 4, 1, 0, 0)]
    [InlineData(48, 4, 2, 0, 0)]
    [InlineData(51, 5, 1, 0, 0)]
    [InlineData(53, 5, 1, 0, 0)]
    [InlineData(55, 5, 2, 0, 0)]
    [InlineData(56, 5, 1, 1, 0)]
    [InlineData(57, 5, 2, 1, 0)]
    [InlineData(61, 5, 1, 0, 0)]
    [InlineData(63, 5, 2, 0, 0)]
    [InlineData(65, 5, 3, 0, 0)]
    [InlineData(66, 5, 2, 1, 0)]
    [InlineData(67, 5, 3, 1, 0)]
    [InlineData(71, 6, 1, 0, 0)]
    [InlineData(73, 6, 2, 0, 0)]
    [InlineData(75, 6, 3, 0, 0)]
    [InlineData(77, 6, 1, 0, 0)]
    [InlineData(80, 5, 1, 0, 0)]
    [InlineData(81, 5, 2, 0, 0)]
    [InlineData(82, 5, 3, 0, 0)]
    [InlineData(85, 6, 1, 0, 0)]
    [InlineData(86, 6, 3, 0, 0)]
    [InlineData(95, 5, 3, 0, 1)]
    [InlineData(96, 5, 3, 0, 1)]
    [InlineData(99, 5, 3, 0, 1)]
    public void Every_code_has_its_effect_and_level(int code, int kind, int level, int icy, int lightning)
    {
        var effect = WeatherEffectMapping.For(code, isDay: true);

        // Clear sky (0, 1) is glints by day (kind 1) and stars by night (kind 2); the kind numbers are the enum values.
        Assert.Equal(kind == 1 ? WeatherEffectKind.SunGlints : (WeatherEffectKind)kind, effect.Kind);
        Assert.Equal((EffectLevel)level, effect.Level);
        Assert.Equal(icy == 1, effect.Icy);
        Assert.Equal(lightning == 1, effect.Lightning);
        Assert.True(effect.IsDay);
    }

    [Fact]
    public void Clear_sky_is_glints_by_day_and_stars_by_night_and_everything_else_keeps_its_kind()
    {
        Assert.Equal(WeatherEffectKind.SunGlints, WeatherEffectMapping.For(0, true).Kind);
        Assert.Equal(WeatherEffectKind.Stars, WeatherEffectMapping.For(0, false).Kind);
        Assert.Equal(WeatherEffectKind.Stars, WeatherEffectMapping.For(1, false).Kind);

        foreach (var code in UsedCodes.Where(c => c > 1))
        {
            var day = WeatherEffectMapping.For(code, true);
            var night = WeatherEffectMapping.For(code, false);
            Assert.Equal(day.Kind, night.Kind);
            Assert.Equal(day.Level, night.Level);
            Assert.True(day.IsDay);
            Assert.False(night.IsDay);
        }
    }

    [Fact]
    public void The_map_matches_what_the_icons_say()
    {
        foreach (var code in UsedCodes)
        {
            var effect = WeatherEffectMapping.For(code, true);
            var icon = WeatherCodes.Describe(code, true).Icon;
            var expected = icon switch
            {
                WeatherIconId.ClearDay or WeatherIconId.MainlyClearDay => WeatherEffectKind.SunGlints,
                WeatherIconId.PartlyCloudyDay or WeatherIconId.Overcast => WeatherEffectKind.Clouds,
                WeatherIconId.Fog => WeatherEffectKind.Haze,
                WeatherIconId.Drizzle or WeatherIconId.FreezingDrizzle or WeatherIconId.Rain or WeatherIconId.FreezingRain
                    or WeatherIconId.RainShowersDay or WeatherIconId.Thunderstorm or WeatherIconId.ThunderstormHail => WeatherEffectKind.Rain,
                WeatherIconId.Snow or WeatherIconId.SnowGrains or WeatherIconId.SnowShowersDay => WeatherEffectKind.Snow,
                _ => throw new InvalidOperationException($"code {code}: icon {icon} has no expected effect"),
            };
            Assert.Equal(expected, effect.Kind);
        }
    }

    [Fact]
    public void A_stronger_report_never_gets_a_weaker_effect()
    {
        int[][] ladders = [[61, 63, 65], [80, 81, 82], [71, 73, 75], [51, 53, 55], [2, 3], [45, 48]];
        foreach (var ladder in ladders)
        {
            var levels = ladder.Select(code => (int)WeatherEffectMapping.For(code, true).Level).ToList();
            Assert.Equal(levels.OrderBy(l => l), levels);
        }
    }

    [Fact]
    public void No_weather_or_an_unknown_code_means_no_effect()
    {
        Assert.Equal(WeatherEffect.None, WeatherEffectMapping.For(null, true));
        Assert.Equal(WeatherEffectKind.None, WeatherEffectMapping.For(1234, false).Kind);
        Assert.Equal(WeatherEffectKind.None, WeatherEffectMapping.For(-1, true).Kind);
    }

    // ---------- the particle pool ----------

    private static IEnumerable<WeatherEffect> AllEffects()
    {
        foreach (var isDay in new[] { true, false })
        {
            foreach (var code in UsedCodes) yield return WeatherEffectMapping.For(code, isDay);
        }
    }

    [Fact]
    public void The_particle_count_is_bounded_and_grows_with_the_level()
    {
        using var field = new WeatherEffectField();
        foreach (var effect in AllEffects())
        {
            foreach (var intensity in Enum.GetValues<WeatherEffectIntensity>())
            {
                field.Set(effect, intensity, instant: true);
                Assert.InRange(field.ParticleCount, 1, WeatherEffectField.MaxParticles);
                Assert.True(field.TotalParticleCount <= 2 * WeatherEffectField.MaxParticles);
            }
        }

        foreach (var kind in new[] { WeatherEffectKind.Rain, WeatherEffectKind.Snow, WeatherEffectKind.Clouds })
        {
            var counts = new List<int>();
            foreach (var level in new[] { EffectLevel.Light, EffectLevel.Moderate, EffectLevel.Heavy })
            {
                field.Set(new WeatherEffect(kind, level, true), WeatherEffectIntensity.Normal, instant: true);
                counts.Add(field.ParticleCount);
            }

            Assert.True(counts[0] < counts[1] && counts[1] < counts[2], $"{kind}: {string.Join(", ", counts)}");
        }

        field.Set(WeatherEffect.None, WeatherEffectIntensity.Normal, instant: true);
        Assert.Equal(0, field.ParticleCount);
        Assert.False(field.HasVisibleEffect);
    }

    [Fact]
    public void Low_has_fewer_particles_than_Normal()
    {
        using var field = new WeatherEffectField();
        var heavyRain = WeatherEffectMapping.For(65, true);

        field.Set(heavyRain, WeatherEffectIntensity.Normal, instant: true);
        var normal = field.ParticleCount;
        field.Set(heavyRain, WeatherEffectIntensity.Low, instant: true);

        Assert.True(field.ParticleCount < normal);
        Assert.InRange(WeatherEffectPolicy.IntervalMilliseconds(WeatherEffectIntensity.Low), 50, 70); // about 15 to 20 frames a second
    }

    [Fact]
    public void Particles_stay_inside_the_widget_for_a_long_run_and_the_count_never_changes()
    {
        using var field = new WeatherEffectField();
        foreach (var effect in AllEffects())
        {
            field.SetSize(280, 360);
            field.Set(effect, WeatherEffectIntensity.Normal, instant: true);
            var count = field.ParticleCount;
            for (var i = 0; i < 1800; i++) field.Update(0.0625f); // 112 seconds at 16 frames a second

            Assert.Equal(count, field.ParticleCount);
            Assert.True(field.ParticlesWithin(30), $"{effect}: a particle left the widget");
        }
    }

    [Fact]
    public void A_frame_allocates_nothing()
    {
        using var field = new WeatherEffectField();
        using var bitmap = new Bitmap(280, 360);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        field.SetSize(280, 360);

        foreach (var effect in AllEffects().Where(e => e.IsDay).Concat([WeatherEffectMapping.For(0, false)]).DistinctBy(e => (e.Kind, e.Level, e.Icy, e.Lightning)))
        {
            field.Set(effect, WeatherEffectIntensity.Normal, instant: true);
            for (var i = 0; i < 30; i++) { field.Update(0.06f); field.Draw(graphics, 1f); } // warm-up: JIT, GDI+ state

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 400; i++)
            {
                field.Update(0.06f);
                field.Draw(graphics, 1f);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(allocated <= 2048, $"{effect.Kind} {effect.Level}: {allocated} bytes allocated over 400 frames");
        }
    }

    [Fact]
    public void The_same_seed_gives_the_same_effect()
    {
        using var a = new WeatherEffectField(7);
        using var b = new WeatherEffectField(7);
        var effect = WeatherEffectMapping.For(63, true);
        a.Set(effect, WeatherEffectIntensity.Normal, instant: true);
        b.Set(effect, WeatherEffectIntensity.Normal, instant: true);
        using var one = new Bitmap(280, 210);
        using var two = new Bitmap(280, 210);
        using (var g = Graphics.FromImage(one)) { g.Clear(Color.Black); a.Advance(3); a.Draw(g, 1f); }
        using (var g = Graphics.FromImage(two)) { g.Clear(Color.Black); b.Advance(3); b.Draw(g, 1f); }

        Assert.Equal(Pixels(one), Pixels(two));
    }

    private static byte[] Pixels(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    // ---------- cross-fade and lightning ----------

    [Fact]
    public void A_change_of_weather_cross_fades_smoothly_and_the_old_layer_goes()
    {
        using var field = new WeatherEffectField();
        var rain = WeatherEffectMapping.For(63, true);
        var snow = WeatherEffectMapping.For(73, true);
        field.Set(rain, WeatherEffectIntensity.Normal);
        Assert.Equal(0, field.Opacity);
        field.Advance(WeatherEffectField.FadeSeconds + 0.3f);
        Assert.Equal(1, field.Opacity);
        Assert.False(field.IsFading);

        field.Set(snow, WeatherEffectIntensity.Normal);
        Assert.Equal(0, field.Opacity); // the new one starts invisible, the old one is still full: no jump
        Assert.True(field.IsFading);
        var last = 0f;
        for (var i = 0; i < 12; i++)
        {
            field.Update(0.1f);
            Assert.True(field.Opacity >= last && field.Opacity - last <= 0.1f / WeatherEffectField.FadeSeconds + 1e-4f, "the fade is gradual");
            last = field.Opacity;
        }

        field.Advance(0.5f);
        Assert.Equal(1, field.Opacity);
        Assert.False(field.IsFading);
        Assert.Equal(snow, field.Effect);
        Assert.Equal(field.ParticleCount, field.TotalParticleCount); // the rain layer is gone
    }

    [Fact]
    public void A_stall_never_makes_a_jump()
    {
        using var field = new WeatherEffectField();
        field.Set(WeatherEffectMapping.For(65, true), WeatherEffectIntensity.Normal, instant: true);

        field.Update(30f); // a 30 second stall counts as one step of at most 0.1 s

        Assert.True(field.ParticlesWithin(30));
        Assert.Equal(1, field.Opacity);
    }

    [Fact]
    public void The_thunderstorm_flash_is_rare_soft_and_never_a_strobe()
    {
        using var field = new WeatherEffectField();
        field.Set(WeatherEffectMapping.For(95, true), WeatherEffectIntensity.Normal, instant: true);
        var previous = 0f;
        var flashes = new List<float>();
        var inFlash = false;
        var maxStep = 0f;
        var peak = 0f;
        for (var t = 0f; t < 600; t += 0.0625f)
        {
            field.Update(0.0625f);
            var level = field.FlashLevel;
            maxStep = Math.Max(maxStep, Math.Abs(level - previous));
            peak = Math.Max(peak, level);
            if (level > 0.02f && !inFlash) flashes.Add(t);
            inFlash = level > 0.02f;
            previous = level;
        }

        Assert.InRange(flashes.Count, 10, 40); // one every 15 to 40 seconds
        for (var i = 1; i < flashes.Count; i++) Assert.True(flashes[i] - flashes[i - 1] >= 14.9f, "at most one flash every 15 seconds (far under the 3 flashes a second limit)");
        Assert.True(maxStep <= 0.35f, $"the flash changes by at most {maxStep:0.00} between frames");
        Assert.InRange(peak, 0.9f, 1.0001f);
        Assert.True(WeatherEffectField.FlashAlpha <= 12, "the flash is a faint wash, not a white-out");
    }

    [Fact]
    public void Only_a_thunderstorm_flashes()
    {
        using var field = new WeatherEffectField();
        field.Set(WeatherEffectMapping.For(65, true), WeatherEffectIntensity.Normal, instant: true);
        for (var i = 0; i < 4000; i++)
        {
            field.Update(0.0625f);
            Assert.Equal(0, field.FlashLevel);
        }
    }

    // ---------- the pause rules ----------

    [Theory]
    [InlineData(true, false, false, false, false, false, true, 3)]
    [InlineData(false, false, false, false, false, false, true, 1)] // hidden (the tray)
    [InlineData(true, true, false, false, false, false, true, 1)] // minimised
    [InlineData(true, false, true, false, false, false, true, 1)] // covered
    [InlineData(true, false, false, true, false, false, true, 1)] // session locked
    [InlineData(true, false, false, false, true, false, true, 1)] // asleep
    [InlineData(true, false, false, false, false, true, true, 2)] // reduce motion
    [InlineData(true, false, false, false, false, false, false, 2)] // Windows animations off
    public void Only_a_visible_uncovered_awake_widget_animates(bool visible, bool minimized, bool occluded, bool locked, bool asleep, bool reduceMotion, bool systemAnimations, int expectedState)
    {
        var settings = new WeatherEffectSettings(true, WeatherEffectIntensity.Normal, reduceMotion);

        Assert.Equal((EffectRunState)expectedState, WeatherEffectPolicy.Decide(settings, WeatherEffectKind.Rain, visible, minimized, occluded, locked, asleep, systemAnimations));
    }

    [Fact]
    public void Off_or_no_weather_is_off_whatever_else_is_true()
    {
        var on = WeatherEffectSettings.Default;
        Assert.Equal(EffectRunState.Off, WeatherEffectPolicy.Decide(on with { Enabled = false }, WeatherEffectKind.Rain, true, false, false, false, false, true));
        Assert.Equal(EffectRunState.Off, WeatherEffectPolicy.Decide(on, WeatherEffectKind.None, true, false, false, false, false, true));
        Assert.Equal(EffectRunState.Off, WeatherEffectPolicy.Decide(on with { Enabled = false }, WeatherEffectKind.Rain, false, true, true, true, true, false));
    }

    // ---------- the settings ----------

    [Fact]
    public void The_defaults_are_on_Low_and_animated()
    {
        var settings = new WeatherEffectSettingsStore(Path.Combine(_root, "none.json")).Load();

        Assert.True(settings.Enabled);
        Assert.Equal(WeatherEffectIntensity.Low, settings.Intensity);
        Assert.False(settings.ReduceMotion);
    }

    [Fact]
    public void Settings_are_saved_atomically_and_read_back()
    {
        var path = Path.Combine(_root, "sub", "weather-effects.json");
        var store = new WeatherEffectSettingsStore(path);
        var wanted = new WeatherEffectSettings(false, WeatherEffectIntensity.Normal, true);

        store.Save(wanted);

        Assert.Equal(wanted, new WeatherEffectSettingsStore(path).Load());
        Assert.False(File.Exists(path + ".tmp")); // the temporary file was renamed away, never left behind
        var text = File.ReadAllText(path);
        Assert.Contains("\"Normal\"", text); // readable names, not numbers
        store.Save(WeatherEffectSettings.Default);
        Assert.Equal(WeatherEffectSettings.Default, store.Load());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json {")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"Enabled\": \"maybe\"}")]
    [InlineData("{\"Intensity\": \"Extreme\"}")]
    [InlineData("{\"Intensity\": 9}")]
    public void A_damaged_file_gives_the_defaults(string content)
    {
        var path = Path.Combine(_root, "weather-effects.json");
        File.WriteAllText(path, content);

        Assert.Equal(WeatherEffectSettings.Default, new WeatherEffectSettingsStore(path).Load());
    }

    [Fact]
    public void A_file_with_only_some_fields_keeps_the_defaults_for_the_rest()
    {
        var path = Path.Combine(_root, "weather-effects.json");
        File.WriteAllText(path, "{\"Enabled\": false}");

        var settings = new WeatherEffectSettingsStore(path).Load();

        Assert.False(settings.Enabled);
        Assert.Equal(WeatherEffectIntensity.Low, settings.Intensity);
        _ = JsonSerializer.Serialize(settings); // serialisable
    }

    // ---------- the contrast budget ----------

    /// <summary>The relative luminance of an sRGB colour (WCAG 2.x).</summary>
    internal static double Luminance(int r, int g, int b)
    {
        static double Channel(int v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    internal static double Contrast(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);

    /// <summary>The brightest pixel (by luminance) the effect ever puts over the widget's panel colour, over many frames of every effect.</summary>
    internal static (double Luminance, Color Pixel, string Where) WorstPixel(float unit = 1f, int frames = 40)
    {
        var worst = (Luminance: 0.0, Pixel: Color.Black, Where: "");
        using var bitmap = new Bitmap((int)(280 * unit), (int)(360 * unit), PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var field = new WeatherEffectField();
        field.SetSize(280, 360);
        foreach (var effect in AllEffects().Distinct())
        {
            foreach (var intensity in Enum.GetValues<WeatherEffectIntensity>())
            {
                field.Set(effect, intensity, instant: true);
                for (var frame = 0; frame < frames; frame++)
                {
                    field.Advance(3.1f); // 124 seconds per effect, so a thunderstorm flash lands in some frames
                    graphics.Clear(Color.FromArgb(28, 32, 40));
                    field.Draw(graphics, unit);
                    var pixels = Pixels(bitmap);
                    for (var i = 0; i < pixels.Length; i += 4)
                    {
                        if (pixels[i] == 40 && pixels[i + 1] == 32 && pixels[i + 2] == 28) continue; // the untouched panel
                        var l = Luminance(pixels[i + 2], pixels[i + 1], pixels[i]);
                        if (l <= worst.Luminance) continue;
                        worst = (l, Color.FromArgb(pixels[i + 2], pixels[i + 1], pixels[i]), $"{effect.Kind} {effect.Level} day={effect.IsDay} icy={effect.Icy} {intensity}");
                    }
                }
            }
        }

        return worst;
    }

    private static (double Luminance, Color Colour) Composite((int R, int G, int B) rgb, int alpha, int stack, bool withFlash)
    {
        // Source-over compositing of `stack` layers of one colour and opacity on the panel, then the thunderstorm flash on top.
        double r = 28, g = 32, b = 40;
        for (var i = 0; i < stack; i++)
        {
            var k = alpha / 255.0;
            (r, g, b) = (r * (1 - k) + rgb.R * k, g * (1 - k) + rgb.G * k, b * (1 - k) + rgb.B * k);
        }

        if (withFlash)
        {
            var k = WeatherEffectField.FlashAlpha / 255.0;
            var flash = WeatherEffectField.FlashColor;
            (r, g, b) = (r * (1 - k) + flash.R * k, g * (1 - k) + flash.G * k, b * (1 - k) + flash.B * k);
        }

        var colour = Color.FromArgb((int)Math.Round(r), (int)Math.Round(g), (int)Math.Round(b));
        return (Luminance(colour.R, colour.G, colour.B), colour);
    }

    [Fact]
    public void Up_to_four_overlapping_particles_and_the_flash_keep_the_text_readable_analytically()
    {
        // Not sampled: the very colours and opacities the renderer draws with (GuaranteedLayers), stacked to the overlap it
        // guarantees (4 streaks, flakes, cloud puffs or haze patches; a thunderstorm's flash on top), over the panel (28, 32, 40).
        Assert.Equal(4, WeatherEffectField.GuaranteedOverlap);
        var layers = WeatherEffectField.GuaranteedLayers().ToList();
        Assert.True(layers.Count >= 11);
        foreach (var (name, rgb, alpha, stack, withFlash) in layers)
        {
            var (luminance, colour) = Composite(rgb, alpha, stack, withFlash);
            var secondary = Contrast(Luminance(190, 200, 212), luminance);
            var primary = Contrast(Luminance(244, 246, 248), luminance);
            Assert.True(secondary >= 4.5, $"{name}: {stack} stacked (flash {withFlash}) give {colour}: secondary text {secondary:0.00}:1 (want 4.5:1)");
            Assert.True(primary >= 7.0, $"{name}: {stack} stacked (flash {withFlash}) give {colour}: primary text {primary:0.00}:1 (want 7:1)");
        }

        // One more particle than guaranteed breaks the rule for the rain: the guarantee is tight, not slack.
        var rain = layers.First(l => l.Name == "icy rain");
        var five = Composite(rain.Rgb, rain.Alpha, 5, true);
        Assert.True(Contrast(Luminance(190, 200, 212), five.Luminance) < 4.5 || Contrast(Luminance(244, 246, 248), five.Luminance) < 7.0);
    }

    [Fact]
    public void The_streak_and_flake_opacity_cannot_be_raised_without_failing_the_guarantee()
    {
        // The opacity at which four stacked icy streaks plus the flash stop passing is above the one in use and below 33 (the value an earlier version used).
        var rain = WeatherEffectField.GuaranteedLayers().First(l => l.Name == "icy rain");
        foreach (var alpha in new[] { 33, 28, 24, 21, 20 })
        {
            var (luminance, _) = Composite(rain.Rgb, alpha, rain.Stack, rain.WithFlash);
            Assert.True(Contrast(Luminance(190, 200, 212), luminance) < 4.5, $"opacity {alpha} must not pass");
        }
    }

    [Fact]
    public void The_text_stays_readable_over_the_brightest_pixel_any_effect_ever_draws()
    {
        // A second, sampled guard: the real renderer, every code at both intensities, at 100% and at 200%.
        foreach (var (unit, frames) in new[] { (1f, 40), (2f, 16) })
        {
            var (luminance, pixel, where) = WorstPixel(unit, frames);
            var secondary = Contrast(Luminance(190, 200, 212), luminance);
            var primary = Contrast(Luminance(244, 246, 248), luminance);
            Assert.True(secondary >= 4.5, $"unit {unit}: secondary text {secondary:0.00}:1 over {pixel} ({where})");
            Assert.True(primary >= 7.0, $"unit {unit}: primary text {primary:0.00}:1 over {pixel} ({where})");
        }
    }

    [Fact]
    public void The_cloud_and_haze_bands_follow_the_widget_height()
    {
        using var field = new WeatherEffectField();
        foreach (var code in new[] { 3, 45 })
        {
            field.SetSize(280, 200);
            field.Set(WeatherEffectMapping.For(code, true), WeatherEffectIntensity.Normal, instant: true);
            Assert.True(field.MaxParticleY < 200);

            field.SetSize(280, 520); // Expanded: the widget grows
            Assert.True(field.MaxParticleY > 400, $"code {code}: the bands stayed in the top part ({field.MaxParticleY:0})");
            Assert.True(field.ParticlesWithin(30));

            field.SetSize(280, 180); // and shrinks back
            Assert.True(field.MaxParticleY < 180);
        }
    }
    [Fact]
    public void The_effect_is_visible_on_the_dark_panel()
    {
        // Faint but there: the brightest pixel of the effect is clearly lighter than the panel, so it is not invisible.
        var (luminance, _, _) = WorstPixel();

        Assert.True(Contrast(luminance, Luminance(28, 32, 40)) >= 1.4);
    }
}
