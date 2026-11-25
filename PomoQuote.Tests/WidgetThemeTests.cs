using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using PomoQuote.Mascot;
using PomoQuote.Pomodoro;
using PomoQuote.Quotes;
using PomoQuote.Weather;
using PomoQuote.Widget;

namespace PomoQuote.Tests;

/// <summary>Which theme is in force, the tokens, and the choice being remembered.</summary>
public sealed class WidgetThemeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PomoQuoteTheme-" + Guid.NewGuid().ToString("N"));

    public WidgetThemeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    // ---------- resolution ----------

    [Theory]
    [InlineData(ThemeMode.System, true, false)]
    [InlineData(ThemeMode.System, false, true)]
    [InlineData(ThemeMode.Light, true, false)]
    [InlineData(ThemeMode.Light, false, false)]
    [InlineData(ThemeMode.Dark, true, true)]
    [InlineData(ThemeMode.Dark, false, true)]
    public void Resolve_follows_windows_only_in_system_mode(ThemeMode mode, bool systemIsLight, bool expectDark)
    {
        Assert.Equal(expectDark, WidgetTheme.Resolve(mode, systemIsLight).IsDark);
    }

    [Fact]
    public void The_two_themes_are_two_distinct_token_sets_and_dark_is_the_original_look()
    {
        Assert.NotEqual(WidgetTheme.Dark.Panel, WidgetTheme.Light.Panel);
        Assert.Equal(Color.FromArgb(28, 32, 40), WidgetTheme.Dark.Panel);
        Assert.Equal(Color.FromArgb(84, 94, 110), WidgetTheme.Dark.Border);
        Assert.Equal(Color.FromArgb(244, 246, 248), WidgetTheme.Dark.PrimaryText);
        Assert.Equal(Color.FromArgb(138, 130, 244), WidgetTheme.Dark.PhaseWork);
        Assert.Equal(IconBackground(true), WidgetTheme.Dark.LogoBackground);
        Assert.Equal(IconBackground(false), WidgetTheme.Light.LogoBackground);
    }

    private static PomoQuote.AiUsage.IconBackground IconBackground(bool dark) => dark ? PomoQuote.AiUsage.IconBackground.Dark : PomoQuote.AiUsage.IconBackground.Light;

    [Fact]
    public void Every_token_is_set_in_both_themes()
    {
        foreach (var theme in new[] { WidgetTheme.Dark, WidgetTheme.Light })
        {
            foreach (var property in typeof(WidgetTheme).GetProperties().Where(p => p.PropertyType == typeof(Color)))
            {
                var colour = (Color)property.GetValue(theme)!;
                Assert.False(colour.IsEmpty, $"{property.Name} is not set");
            }
        }
    }

    // ---------- readability ----------

    private static double Luminance(Color c)
    {
        static double Channel(int v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        var (hi, lo) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>Blends a colour with some transparency over a background.</summary>
    private static Color Over(Color top, Color back) => Color.FromArgb(
        (top.R * top.A + back.R * (255 - top.A)) / 255, (top.G * top.A + back.G * (255 - top.A)) / 255, (top.B * top.A + back.B * (255 - top.A)) / 255);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Text_has_at_least_4_5_to_1_contrast_on_what_it_sits_on(bool dark)
    {
        var t = dark ? WidgetTheme.Dark : WidgetTheme.Light;

        Assert.True(Contrast(t.PrimaryText, t.Panel) >= 7, $"primary on panel {Contrast(t.PrimaryText, t.Panel):0.0}");
        Assert.True(Contrast(t.SecondaryText, t.Panel) >= 4.5, $"secondary on panel {Contrast(t.SecondaryText, t.Panel):0.0}");
        Assert.True(Contrast(t.QuoteEnglish, t.Card) >= 4.5, $"quote english on card {Contrast(t.QuoteEnglish, t.Card):0.0}");
        Assert.True(Contrast(t.QuoteVietnamese, t.Card) >= 4.5, $"quote vietnamese on card {Contrast(t.QuoteVietnamese, t.Card):0.0}");
        Assert.True(Contrast(t.TileText, t.TileFill) >= 4.5, $"tile letter {Contrast(t.TileText, t.TileFill):0.0}");
        Assert.True(Contrast(t.Warning, t.Panel) >= 3, $"warning text on panel {Contrast(t.Warning, t.Panel):0.0}"); // the note under a problem is drawn in it
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bars_icons_borders_and_marks_are_visible_against_the_panel(bool dark)
    {
        var t = dark ? WidgetTheme.Dark : WidgetTheme.Light;

        foreach (var (name, colour) in new[]
        {
            ("quota", t.Quota), ("warning", t.Warning), ("critical", t.Critical), ("stale", t.Stale), ("work", t.PhaseWork), ("short break", t.PhaseShortBreak),
            ("long break", t.PhaseLongBreak), ("focus ring", t.Focus), ("button glyph", t.Glyph), ("scroll thumb", t.Scroll),
        })
        {
            Assert.True(Contrast(colour, t.Panel) >= 3, $"{name} on panel {Contrast(colour, t.Panel):0.0}");
        }

        Assert.True(Contrast(t.Border, t.Panel) >= 1.5, "the border is visible");
        Assert.True(Contrast(t.Track, t.Panel) >= 1.15, "the empty part of a bar is visible");
        Assert.True(Contrast(t.Card, t.Panel) >= 1.1, "the quote card is visible");
        Assert.True(Contrast(Over(t.QuoteMark, t.Card), t.Card) >= 1.2, "the quotation marks show");
        Assert.True(Contrast(t.GlyphHot, t.ButtonHot) >= 4.5, "the chevron under the pointer");
        Assert.True(Contrast(t.Glyph, t.Panel) >= 4.5, "the chevron");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Timer_colours_are_never_the_warning_hues(bool dark)
    {
        var t = dark ? WidgetTheme.Dark : WidgetTheme.Light;

        foreach (var colour in new[] { t.PhaseWork, t.PhaseShortBreak, t.PhaseLongBreak })
        {
            var hue = colour.GetHue();
            Assert.False(hue < 70 || hue > 340, $"{colour} hue {hue}");
        }
    }

    // ---------- persistence ----------

    [Fact]
    public void The_default_is_system()
    {
        Assert.Equal(ThemeMode.System, WidgetSettings.Default.Theme);
        Assert.Equal(ThemeMode.System, new WidgetStore(Path.Combine(_dir, "none.json")).Load().Theme);
    }

    [Theory]
    [InlineData(ThemeMode.System)]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void The_choice_survives_a_save_and_a_load(ThemeMode mode)
    {
        var store = new WidgetStore(Path.Combine(_dir, "widget.json"));

        store.Save(WidgetSettings.Default with { Theme = mode });

        Assert.Equal(mode, store.Load().Theme);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("""{"Theme":99}""")]
    [InlineData("""{"Theme":-3}""")]
    [InlineData("""{"Theme":"purple"}""")]
    [InlineData("""{"Enabled":true,"OpacityPercent":80}""")]
    public void A_damaged_unknown_or_older_file_means_system(string content)
    {
        var path = Path.Combine(_dir, "widget.json");
        File.WriteAllText(path, content);

        Assert.Equal(ThemeMode.System, new WidgetStore(path).Load().Theme);
    }

    [Fact]
    public void An_older_file_keeps_its_other_choices()
    {
        var path = Path.Combine(_dir, "widget.json");
        File.WriteAllText(path, """{"Enabled":true,"OpacityPercent":80}""");

        var loaded = new WidgetStore(path).Load();

        Assert.True(loaded.Enabled);
        Assert.Equal(80, loaded.OpacityPercent);
    }

    // ---------- one set of tokens ----------

    [Fact]
    public void The_widget_code_holds_no_colour_literals_only_theme_tokens()
    {
        var root = AppContext.BaseDirectory.Split("PomoQuote.Tests")[0];
        foreach (var file in new[] { "WidgetView.cs", "WidgetView.Usage.cs", "UsageLogoSelector.cs", "UsageExpandToggle.cs", "WidgetGlyphs.cs", Path.Combine("Widget", "PomodoroIcon.cs") })
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            Assert.DoesNotContain("Color.FromArgb(", text);
            Assert.DoesNotContain("IconBackground.Dark", text.Replace("_theme.LogoBackground", ""));
        }
    }
}

/// <summary>The real widget: a choice applies at once, System follows Windows live, the choice is remembered, and both themes draw readably.</summary>
[Collection("process")]
public sealed class WidgetThemeWidgetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PomoQuoteThemeForm-" + Guid.NewGuid().ToString("N"));

    public WidgetThemeWidgetTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private sealed class NeverAnswers : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        }
    }

    private MainForm NewForm(bool systemIsLight = true, IReadOnlyList<PomoQuote.AiUsage.IUsageProvider>? providers = null)
    {
        var http = new HttpClient(new NeverAnswers()) { Timeout = TimeSpan.FromMinutes(10) };
        var form = new MainForm(
            new WeatherService(http), new SettingsStore(Path.Combine(_root, "settings.json")), new PomodoroStore(Path.Combine(_root, "pomodoro.json")),
            TimeProvider.System, new WidgetStore(Path.Combine(_root, "widget.json")), new MascotStore(Path.Combine(_root, "mascot.json")),
            new QuoteSettingsStore(Path.Combine(_root, "quotes.json")), new BundledQuoteProvider(), new Random(1),
            new PomoQuote.AiUsage.UsageSettingsStore(Path.Combine(_root, "usage.json")), new PomoQuote.AiUsage.UsageCacheStore(Path.Combine(_root, "usage-cache.json")), new PomoQuote.AiUsage.UsageAlertStore(Path.Combine(_root, "usage-alerts.json")),
            providers ?? [], null, new WeatherEffectSettingsStore(Path.Combine(_root, "weather-effects.json")))
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-3000, -3000),
            WorkAreaProvider = () => [new Rectangle(-6000, -6000, 4000, 4000)],
        };
        form.WidgetViewForTests.SystemIsLightProbe = () => systemIsLight;
        return form;
    }

    private static void OnStaThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(120)), "the form test did not finish");
        if (failure is not null) throw new InvalidOperationException("The form test failed: " + failure.Message, failure);
    }

    private static void Pump(TimeSpan time)
    {
        var until = DateTime.UtcNow + time;
        while (DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static Bitmap Draw(MainForm form)
    {
        var view = form.WidgetViewForTests;
        var bitmap = new Bitmap(view.ClientSize.Width, view.ClientSize.Height);
        view.DrawToBitmap(bitmap, new Rectangle(Point.Empty, view.ClientSize));
        return bitmap;
    }

    [Fact]
    public void The_default_follows_windows_and_a_choice_applies_at_once_without_a_restart()
    {
        OnStaThread(() =>
        {
            using var form = NewForm(systemIsLight: true);
            form.Show();
            form.SetWidgetMode(true);
            var view = form.WidgetViewForTests;
            Pump(TimeSpan.FromMilliseconds(200));
            Assert.Equal(ThemeMode.System, view.ThemeChoice);
            Assert.False(view.Theme.IsDark); // Windows is light

            form.SetTheme(ThemeMode.Dark);
            using (var dark = Draw(form)) Assert.Equal(WidgetTheme.Dark.Panel.ToArgb(), dark.GetPixel(dark.Width / 2, 3).ToArgb());
            Assert.True(view.Theme.IsDark);
            Assert.Equal(WidgetTheme.Dark.Panel, view.BackColor);

            form.SetTheme(ThemeMode.Light);
            using (var light = Draw(form)) Assert.Equal(WidgetTheme.Light.Panel.ToArgb(), light.GetPixel(light.Width / 2, 3).ToArgb());
            Assert.False(view.Theme.IsDark);
        });
    }

    [Fact]
    public void System_mode_follows_windows_live_and_a_fixed_choice_does_not()
    {
        OnStaThread(() =>
        {
            var systemLight = true;
            using var form = NewForm();
            form.WidgetViewForTests.SystemIsLightProbe = () => systemLight;
            form.Show();
            form.SetWidgetMode(true);
            var view = form.WidgetViewForTests;
            form.SetTheme(ThemeMode.System);
            Assert.False(view.Theme.IsDark);

            systemLight = false;
            view.SystemThemeChanged(); // what the Windows colour-change notice calls
            Assert.True(view.Theme.IsDark);
            using (var dark = Draw(form)) Assert.Equal(WidgetTheme.Dark.Panel.ToArgb(), dark.GetPixel(dark.Width / 2, 3).ToArgb());

            systemLight = true;
            view.SystemThemeChanged();
            Assert.False(view.Theme.IsDark);

            form.SetTheme(ThemeMode.Dark);
            systemLight = true;
            view.SystemThemeChanged();
            Assert.True(view.Theme.IsDark); // Dark stays dark whatever Windows does
        });
    }

    [Fact]
    public void The_choice_is_remembered_across_restarts_and_the_menus_show_it()
    {
        OnStaThread(() =>
        {
            using (var form = NewForm())
            {
                form.Show();
                Assert.All(form.ThemeMenuForTests.Where(i => i.Mode == ThemeMode.System), i => Assert.True(i.Checked));
                Assert.Equal(["System", "Light", "Dark"], form.ThemeMenuForTests.Take(3).Select(i => i.Text));
                Assert.Equal(9, form.ThemeMenuForTests.Count); // the main, the widget and the tray Settings menus each offer the three choices

                form.SetTheme(ThemeMode.Dark);
                Assert.All(form.ThemeMenuForTests.Where(i => i.Mode == ThemeMode.Dark), i => Assert.True(i.Checked));
                Assert.All(form.ThemeMenuForTests.Where(i => i.Mode != ThemeMode.Dark), i => Assert.False(i.Checked));
            }

            Assert.Equal(ThemeMode.Dark, new WidgetStore(Path.Combine(_root, "widget.json")).Load().Theme);
            using var next = NewForm(systemIsLight: true);
            next.Show();
            Assert.Equal(ThemeMode.Dark, next.WidgetViewForTests.ThemeChoice);
            Assert.True(next.WidgetViewForTests.Theme.IsDark);
            Assert.All(next.ThemeMenuForTests.Where(i => i.Mode == ThemeMode.Dark), i => Assert.True(i.Checked));
        });
    }

    private sealed class SampleProvider(string id, string name, double five, double week) : PomoQuote.AiUsage.IUsageProvider
    {
        public string Id => id;

        public string DisplayName => name;

        public PomoQuote.AiUsage.UsageDataSource DataSource => PomoQuote.AiUsage.UsageDataSource.CodexAppServer;

        public Task<PomoQuote.AiUsage.UsageSnapshot> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(
            new PomoQuote.AiUsage.UsageSnapshot(new PomoQuote.AiUsage.UsageSource(id, PomoQuote.AiUsage.UsageEnvironment.Windows, "x", "plus"),
                [new PomoQuote.AiUsage.QuotaWindow(PomoQuote.AiUsage.QuotaKind.FiveHour, 300, five, DateTimeOffset.UtcNow.AddHours(2), id, true), new PomoQuote.AiUsage.QuotaWindow(PomoQuote.AiUsage.QuotaKind.Weekly, 10080, week, DateTimeOffset.UtcNow.AddDays(3), id, true)],
                DateTimeOffset.UtcNow, PomoQuote.AiUsage.UsageDataSource.CodexAppServer, PomoQuote.AiUsage.UsageState.Ok));
    }

    /// <summary>Both themes draw with their own colours: no pixel of the other theme''s panel is left behind, the text is there, and Expanded with every logo, row and badge draws.</summary>
    [Theory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void Both_themes_draw_the_widget_with_only_their_own_colours_in_both_layouts(ThemeMode mode)
    {
        OnStaThread(() =>
        {
            var providers = new PomoQuote.AiUsage.IUsageProvider[] { new SampleProvider("codex", "Codex", 42, 31), new SampleProvider("claude", "Claude", 63, 96), new SampleProvider("antigravity", "Antigravity", 10, 20) };
            new PomoQuote.AiUsage.UsageSettingsStore(Path.Combine(_root, "usage.json")).Save(providers.Aggregate(PomoQuote.AiUsage.UsageSettings.Default with { AlertsEnabled = false, Expanded = true }, (s, p) => s.WithEnabled(p.Id, true)));
            using var form = NewForm(providers: providers);
            form.Show();
            form.SetWidgetMode(true);
            form.WidgetViewForTests.SetQuote("Look before you leap.", "Nghi ky truoc khi nhay.");
            for (var i = 0; i < 200 && form.UsageForTests.Views().Count(v => v.Rows.Count > 0) < providers.Length; i++) Pump(TimeSpan.FromMilliseconds(50));
            form.SetTheme(mode);
            var theme = mode == ThemeMode.Light ? WidgetTheme.Light : WidgetTheme.Dark;
            var other = mode == ThemeMode.Light ? WidgetTheme.Dark : WidgetTheme.Light;

            foreach (var expanded in new[] { true, false })
            {
                form.UsageForTests.SetLayout(expanded, "claude");
                Pump(TimeSpan.FromMilliseconds(150));
                using var bitmap = Draw(form);
                var panelPixels = 0;
                var otherPanelPixels = 0;
                var strongest = 0.0;
                for (var y = 2; y < bitmap.Height - 2; y += 2)
                {
                    for (var x = 2; x < bitmap.Width - 2; x += 2)
                    {
                        var c = bitmap.GetPixel(x, y);
                        if (c.ToArgb() == theme.Panel.ToArgb()) panelPixels++;
                        if (c.ToArgb() == other.Panel.ToArgb()) otherPanelPixels++;
                        strongest = Math.Max(strongest, Contrast(c, theme.Panel));
                    }
                }

                Assert.True(panelPixels > 1000, $"the panel colour is the background ({mode}, expanded {expanded})");
                Assert.Equal(0, otherPanelPixels); // nothing of the other theme is left (the quote card, the buttons and the controls follow too)
                Assert.True(strongest >= 7, $"text is drawn with strong contrast ({mode}, expanded {expanded}): {strongest:0.0}");
            }
        });
    }

    private static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double Ch(int v)
            {
                var s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }

        return (Math.Max(L(a), L(b)) + 0.05) / (Math.Min(L(a), L(b)) + 0.05);
    }

    [Fact]
    public void A_weather_effect_over_a_light_panel_is_drawn_dark_and_still_lets_the_text_through_and_switching_repeatedly_does_not_leak()
    {
        OnStaThread(() =>
        {
            using var form = NewForm();
            form.Show();
            form.SetWidgetMode(true);
            var view = form.WidgetViewForTests;
            form.ApplyWeatherEffectSettings(new WeatherEffectSettings(true, WeatherEffectIntensity.Normal, true));
            form.WeatherPanelForTests.ShowReport(new WeatherReport(12, 80, 14, 61, IsDay: true), DateTime.Now); // rain
            Pump(TimeSpan.FromMilliseconds(600));
            var process = Process.GetCurrentProcess();

            for (var i = 0; i < 12; i++)
            {
                form.SetTheme(i % 2 == 0 ? ThemeMode.Light : ThemeMode.Dark);
                Pump(TimeSpan.FromMilliseconds(30));
                using var warm = Draw(form);
            }

            process.Refresh();
            var handles = process.HandleCount;
            for (var i = 0; i < 60; i++)
            {
                form.SetTheme(i % 2 == 0 ? ThemeMode.Light : ThemeMode.Dark);
                using var bitmap = Draw(form);
            }

            Pump(TimeSpan.FromMilliseconds(300));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            process.Refresh();
            Assert.True(process.HandleCount - handles <= 40, $"handles {handles} -> {process.HandleCount}");

            form.SetTheme(ThemeMode.Light);
            using var light = Draw(form);
            var dark = 0;
            for (var y = 2; y < light.Height - 2; y += 2)
            {
                for (var x = 2; x < light.Width - 2; x += 2) if (Contrast(light.GetPixel(x, y), WidgetTheme.Light.Panel) >= 7) dark++;
            }

            Assert.True(dark > 50, "dark text is drawn over the effect on the light panel");
        });
    }
}