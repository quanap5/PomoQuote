using System.Drawing;
using System.Windows.Forms;
using PomoQuote.AiUsage;
using PomoQuote.Mascot;
using PomoQuote.Pomodoro;
using PomoQuote.Quotes;
using PomoQuote.Weather;
using PomoQuote.Widget;

namespace PomoQuote.Tests;

/// <summary>
/// The effect timer inside the real widget: it ticks only while the effect is on and the widget can be seen, and it makes no tick
/// in any paused state. Runs on throwaway settings in an off-screen window; nothing touches the real files or the running app.
/// </summary>
[Collection("process")]
public sealed class WidgetEffectTimerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PomoQuoteFxForm-" + Guid.NewGuid().ToString("N"));

    public WidgetEffectTimerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class NeverAnswers : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        }
    }

    private MainForm NewForm()
    {
        var http = new HttpClient(new NeverAnswers()) { Timeout = TimeSpan.FromMinutes(10) };
        return new MainForm(
            new WeatherService(http), new SettingsStore(Path.Combine(_root, "settings.json")), new PomodoroStore(Path.Combine(_root, "pomodoro.json")),
            TimeProvider.System, new WidgetStore(Path.Combine(_root, "widget.json")), new MascotStore(Path.Combine(_root, "mascot.json")),
            new QuoteSettingsStore(Path.Combine(_root, "quotes.json")), new BundledQuoteProvider(), new Random(1),
            new UsageSettingsStore(Path.Combine(_root, "usage.json")), new UsageCacheStore(Path.Combine(_root, "usage-cache.json")), new UsageAlertStore(Path.Combine(_root, "usage-alerts.json")),
            [], null, new WeatherEffectSettingsStore(Path.Combine(_root, "weather-effects.json")));
    }

    private static void OnStaThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(90)), "the form test did not finish");
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

    /// <summary>An off-screen widget showing rain at night, with the effect on.</summary>
    private MainForm ShownWidget(WeatherEffectSettings? settings = null, int code = 63)
    {
        var form = NewForm();
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-3000, -3000);
        form.WorkAreaProvider = () => [new Rectangle(-6000, -6000, 4000, 4000)]; // widget mode keeps the window off screen
        form.Show();
        form.WidgetViewForTests.OcclusionProbe = _ => false;
        form.WidgetViewForTests.SystemAnimationsProbe = () => true;
        form.WeatherPanelForTests.ShowReport(new WeatherReport(22, 70, 8, code, IsDay: false), DateTime.Now);
        form.ApplyWeatherEffectSettings(settings ?? new WeatherEffectSettings(true, WeatherEffectIntensity.Normal, false));
        form.SetWidgetMode(true);
        Pump(TimeSpan.FromMilliseconds(100));
        return form;
    }

    [Fact]
    public void It_ticks_while_on_and_visible_at_the_chosen_rate()
    {
        OnStaThread(() =>
        {
            using var form = ShownWidget();
            var view = form.WidgetViewForTests;

            Assert.Equal(EffectRunState.Animating, view.EffectState);
            Assert.True(view.EffectTimerRunning);
            Assert.Equal(62, view.EffectTimerInterval);
            var before = view.EffectTicks;
            Pump(TimeSpan.FromSeconds(1));
            var frames = view.EffectTicks - before;
            Assert.InRange(frames, 12, 20); // about 16 a second, give or take the timer's resolution

            form.ApplyWeatherEffectSettings(new WeatherEffectSettings(true, WeatherEffectIntensity.Low, false));
            Assert.Equal(62, view.EffectTimerInterval);
        });
    }

    [Fact]
    public void It_makes_zero_ticks_when_off_or_when_there_is_no_weather()
    {
        OnStaThread(() =>
        {
            using var form = ShownWidget(new WeatherEffectSettings(false, WeatherEffectIntensity.Normal, false));
            var view = form.WidgetViewForTests;
            Assert.Equal(EffectRunState.Off, view.EffectState);
            Assert.False(view.EffectTimerRunning);
            Pump(TimeSpan.FromSeconds(1));
            Assert.Equal(0, view.EffectTicks);

            form.ApplyWeatherEffectSettings(WeatherEffectSettings.Default);
            Assert.True(view.EffectTimerRunning);
            form.WeatherPanelForTests.ShowReport(new WeatherReport(22, 70, 8, 1234, IsDay: true), DateTime.Now); // an unknown code
            Pump(TimeSpan.FromSeconds(2)); // the old effect fades out, then the timer stops
            Assert.Equal(EffectRunState.Off, view.EffectState);
            Assert.False(view.EffectTimerRunning);
            var ticks = view.EffectTicks;
            Pump(TimeSpan.FromSeconds(1));
            Assert.Equal(ticks, view.EffectTicks);
        });
    }

    [Fact]
    public void It_makes_zero_ticks_in_every_paused_state()
    {
        OnStaThread(() =>
        {
            using var form = ShownWidget();
            var view = form.WidgetViewForTests;

            void AssertPaused(string why)
            {
                view.EvaluateEffects();
                Assert.Equal(EffectRunState.Paused, view.EffectState);
                Assert.False(view.EffectTimerRunning);
                var ticks = view.EffectTicks;
                Pump(TimeSpan.FromMilliseconds(600));
                Assert.True(view.EffectTicks == ticks, $"{why}: the effect ticked while paused");
            }

            void AssertRunning(string why)
            {
                view.EvaluateEffects();
                Assert.True(view.EffectTimerRunning, $"{why}: the effect did not resume (state {view.EffectState}, view visible {view.Visible}, form visible {form.Visible}, window {form.WindowState})");
                var ticks = view.EffectTicks;
                Pump(TimeSpan.FromMilliseconds(400));
                Assert.True(view.EffectTicks > ticks, $"{why}: no frames after resuming");
            }

            view.OcclusionProbe = _ => true;
            AssertPaused("covered");
            view.OcclusionProbe = _ => false;
            AssertRunning("uncovered");

            view.SessionLocked = true;
            AssertPaused("session locked");
            view.SessionLocked = false;
            AssertRunning("session unlocked");

            view.Asleep = true;
            AssertPaused("asleep");
            view.Asleep = false;
            AssertRunning("woken");

            form.WindowState = FormWindowState.Minimized;
            Pump(TimeSpan.FromMilliseconds(50));
            AssertPaused("minimised");
            form.RestoreFromTray(); // minimising goes to the tray when the mascot starts
            for (var i = 0; i < 50 && form.WindowState != FormWindowState.Normal; i++)
            {
                form.WindowState = FormWindowState.Normal;
                Pump(TimeSpan.FromMilliseconds(40));
            }

            if (!form.Visible) form.Show();
            Pump(TimeSpan.FromMilliseconds(50));
            AssertRunning("restored");

            view.Visible = false;
            AssertPaused("hidden");
            view.Visible = true;
            AssertRunning("shown");

            form.Hide(); // the tray
            AssertPaused("in the tray");
            form.Show();
            AssertRunning("out of the tray");
        });
    }

    [Fact]
    public void Reduce_motion_and_Windows_animations_off_give_one_still_picture_and_no_timer()
    {
        OnStaThread(() =>
        {
            using var form = ShownWidget(new WeatherEffectSettings(true, WeatherEffectIntensity.Normal, ReduceMotion: true));
            var view = form.WidgetViewForTests;

            Assert.Equal(EffectRunState.Still, view.EffectState);
            Assert.False(view.EffectTimerRunning);
            Assert.True(view.EffectField.HasVisibleEffect); // the picture is there
            Assert.Equal(1, view.EffectField.Opacity);
            Pump(TimeSpan.FromSeconds(1));
            Assert.Equal(0, view.EffectTicks);

            form.ApplyWeatherEffectSettings(new WeatherEffectSettings(true, WeatherEffectIntensity.Normal, false));
            Assert.Equal(EffectRunState.Animating, view.EffectState);
            view.SystemAnimationsProbe = () => false; // "Show animations in Windows" turned off
            view.EvaluateEffects();
            Assert.Equal(EffectRunState.Still, view.EffectState);
            Assert.False(view.EffectTimerRunning);
        });
    }

    [Fact]
    public void The_chosen_settings_are_saved_and_read_by_the_next_start()
    {
        OnStaThread(() =>
        {
            using (var form = NewForm())
            {
                form.ApplyWeatherEffectSettings(new WeatherEffectSettings(false, WeatherEffectIntensity.Normal, true));
            }

            using var next = NewForm();
            Assert.Equal(new WeatherEffectSettings(false, WeatherEffectIntensity.Normal, true), next.WeatherEffectSettings);
        });
    }

    [Fact]
    public void The_size_and_layout_of_the_widget_do_not_depend_on_the_effect()
    {
        OnStaThread(() =>
        {
            using var form = ShownWidget(new WeatherEffectSettings(false, WeatherEffectIntensity.Normal, false));
            var view = form.WidgetViewForTests;
            var off = (view.LogicalClientSize, form.ClientSize, view.LogicalHeight);

            form.ApplyWeatherEffectSettings(new WeatherEffectSettings(true, WeatherEffectIntensity.Normal, false));
            Pump(TimeSpan.FromMilliseconds(300));

            Assert.Equal(off, (view.LogicalClientSize, form.ClientSize, view.LogicalHeight));
        });
    }

    [Fact]
    public void The_widget_paints_the_same_content_over_the_effect_as_without_one()
    {
        OnStaThread(() =>
        {
            using var form = ShownWidget(new WeatherEffectSettings(false, WeatherEffectIntensity.Normal, false));
            var view = form.WidgetViewForTests;
            using var plain = new Bitmap(view.ClientSize.Width, view.ClientSize.Height);
            view.DrawToBitmap(plain, new Rectangle(Point.Empty, view.ClientSize));

            form.ApplyWeatherEffectSettings(new WeatherEffectSettings(true, WeatherEffectIntensity.Normal, ReduceMotion: true));
            using var withEffect = new Bitmap(view.ClientSize.Width, view.ClientSize.Height);
            view.DrawToBitmap(withEffect, new Rectangle(Point.Empty, view.ClientSize));

            // Text pixels that are bright (the clock, the weather lines) are in the same places with and without the effect.
            var brightPlain = 0;
            var brightBoth = 0;
            for (var y = 0; y < plain.Height; y += 2)
            {
                for (var x = 0; x < plain.Width; x += 2)
                {
                    if (plain.GetPixel(x, y).GetBrightness() < 0.55f) continue;
                    brightPlain++;
                    if (withEffect.GetPixel(x, y).GetBrightness() >= 0.45f) brightBoth++;
                }
            }
            Assert.True(brightPlain > 200, "the widget has bright text to compare");
            Assert.True(brightBoth >= brightPlain * 0.9, $"{brightBoth} of {brightPlain} bright pixels survive the effect");
        });
    }
}
