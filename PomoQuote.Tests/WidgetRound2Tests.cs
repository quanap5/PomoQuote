using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using PomoQuote.AiUsage;
using PomoQuote.AiUsage.Claude;
using PomoQuote.Mascot;
using PomoQuote.Pomodoro;
using PomoQuote.Quotes;
using PomoQuote.Weather;
using PomoQuote.Widget;

namespace PomoQuote.Tests;

/// <summary>Round 2: timer colours, marking old numbers, and the clock and timer keeping their place on screen when the layout changes.</summary>
public sealed class WidgetRound2Tests
{
    // ---------- timer colours are never warning colours ----------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Timer_phase_colours_are_not_the_quota_warning_colours(int phaseNumber)
    {
        var colour = (Color)typeof(WidgetView).GetMethod("PhaseColor", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [(PomodoroPhase)phaseNumber])!;

        // Warning is amber (240,180,80), critical is red-orange (240,98,82): nothing red, orange or yellow for a timer.
        Assert.True(colour.R < 200 || (colour.G > 190 && colour.B < 130 && colour.R < 200), $"phase {phaseNumber} {colour} is too warm");
        Assert.NotEqual(Color.FromArgb(240, 98, 82), colour);
        Assert.NotEqual(Color.FromArgb(240, 180, 80), colour);
        var hue = colour.GetHue();
        Assert.False(hue < 70 || hue > 340, $"phase {phaseNumber} hue {hue} is in the red-orange-yellow range reserved for warnings");
    }

    // ---------- old numbers are marked, not silent ----------

    private static UsageProviderView Build(UsageSnapshot latest, UsageSnapshot good, DateTimeOffset? now = null) =>
        UsageViewBuilder.Build(new UsageStatus("claude", "Claude", latest, good), UsageSettings.Default.WithEnabled("claude", true), now ?? UsageTestData.Now.AddMinutes(1), TimeZoneInfo.Utc);

    private static UsageSnapshot Empty(UsageState state) => UsageSnapshot.Empty(UsageTestData.Source("claude"), UsageTestData.Now.AddMinutes(1), UsageDataSource.CodexAppServer, state, "x");

    [Theory]
    [InlineData(UsageState.NoPlanLimits, "no plan limits")]
    [InlineData(UsageState.Waiting, "no reply")]
    [InlineData(UsageState.NotSetUp, "not set up")]
    public void Kept_numbers_are_marked_old_when_the_latest_read_has_no_plan_limits_or_no_reply(UsageState state, string text)
    {
        var good = UsageTestData.Snapshot(21, 27, source: UsageTestData.Source("claude"));

        var view = Build(Empty(state), good);

        Assert.True(view.HasData); // the numbers stay
        Assert.Equal(2, view.Rows.Count);
        Assert.All(view.Rows, row => Assert.True(row.Stale)); // icon
        Assert.Equal(ProviderSeverity.Stale, view.Severity); // badge
        Assert.True(view.IsProblem); // amber note
        Assert.Contains(text, view.Note);
        Assert.Contains("data from", view.Note.Replace("·", "data from"));
        Assert.All(view.Rows, row => Assert.Contains("old", row.Tip));
    }

    [Fact]
    public void A_failure_with_kept_numbers_stays_an_error_badge_and_without_numbers_nothing_is_marked_stale()
    {
        var good = UsageTestData.Snapshot(21, 27, source: UsageTestData.Source("claude"));

        var failed = Build(Empty(UsageState.NotLoggedIn), good);
        var none = Build(Empty(UsageState.NoPlanLimits), Empty(UsageState.NoPlanLimits));

        Assert.Equal(ProviderSeverity.Error, failed.Severity);
        Assert.True(failed.Rows.All(r => r.Stale));
        Assert.False(none.HasData);
        Assert.Equal(ProviderSeverity.None, none.Severity);
        Assert.False(none.IsProblem);
    }

    [Fact]
    public void Fresh_numbers_are_not_marked()
    {
        var good = UsageTestData.Snapshot(21, 27, source: UsageTestData.Source("claude"));

        var view = Build(good, good, UsageTestData.Now.AddMinutes(1));

        Assert.All(view.Rows, row => Assert.False(row.Stale));
        Assert.Equal(ProviderSeverity.None, view.Severity);
    }
}

/// <summary>The clock and the timer keep their place on screen when Expanded or a long sentence makes the widget taller.</summary>
[Collection("process")]
public sealed class WidgetAnchorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PomoQuoteAnchor-" + Guid.NewGuid().ToString("N"));

    public WidgetAnchorTests() => Directory.CreateDirectory(_root);

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
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "the form test did not finish");
        if (failure is not null) throw new InvalidOperationException("The form test failed: " + failure.Message, failure);
    }

    private static void Pump(int milliseconds)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(15);
        }
    }

    private MainForm Form(Point location)
    {
        var usagePath = Path.Combine(_root, "usage.json");
        string[] ids = ["a", "b", "c"];
        var settings = UsageSettings.Default with { Order = ids, Expanded = false, AlertsEnabled = false };
        foreach (var id in ids) settings = settings.WithEnabled(id, true);
        new UsageSettingsStore(usagePath).Save(settings);
        var providers = ids.Select(id => (IUsageProvider)new FakeProvider(id, _ => Task.FromResult(UsageTestData.Snapshot(21, 27, source: UsageTestData.Source(id))))).ToList();
        var store = new ClaudeSnapshotStore(Path.Combine(_root, "claude-usage"));
        var claude = new ClaudeSetup(
            new ClaudeBridgeInstaller(Path.Combine(_root, "claude", "settings.json"), Path.Combine(_root, "state"), () => Path.Combine(_root, "app", "PomoQuote.exe")),
            new ClaudeRefresher(new ClaudeProcessFactory(), store, TimeProvider.System),
            store);
        var http = new HttpClient(new NeverAnswers()) { Timeout = TimeSpan.FromMinutes(10) };
        var form = new MainForm(
            new WeatherService(http), new SettingsStore(Path.Combine(_root, "settings.json")), new PomodoroStore(Path.Combine(_root, "pomodoro.json")),
            TimeProvider.System, new WidgetStore(Path.Combine(_root, "widget.json")), new MascotStore(Path.Combine(_root, "mascot.json")),
            new QuoteSettingsStore(Path.Combine(_root, "quotes.json")), new BundledQuoteProvider(), new Random(1),
            new UsageSettingsStore(usagePath), new UsageCacheStore(Path.Combine(_root, "usage-cache.json")), new UsageAlertStore(Path.Combine(_root, "usage-alerts.json")),
            providers, claude)
        {
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = location,
        };
        form.BridgeNoticeSink = (_, _) => { };
        form.Show();
        form.SetWidgetMode(true);
        form.Location = location;
        Pump(400);
        return form;
    }

    private static WidgetView ViewOf(MainForm form) => (WidgetView)typeof(MainForm).GetField("_widgetView", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;

    private static void ClickToggle(WidgetView view)
    {
        var toggle = (UsageExpandToggle)typeof(WidgetView).GetField("_toggle", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
        typeof(Control).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(toggle, [new MouseEventArgs(MouseButtons.Left, 1, 3, 3, 0)]);
        Pump(300);
    }

    /// <summary>Where the clock and the timer are on screen: the top-left of the widget plus their fixed offsets (6 to 126 logical px).</summary>
    private static Rectangle TopBlockOnScreen(MainForm form) => form.RectangleToScreen(new Rectangle(0, 0, form.ClientSize.Width, (int)(126 * form.DeviceDpi / 96f)));

    [Fact]
    public void Expanding_with_room_below_leaves_the_clock_and_the_timer_where_they_are_and_grows_downward()
    {
        OnStaThread(() =>
        {
            var work = Screen.PrimaryScreen!.WorkingArea;
            using var form = Form(new Point(work.Left + 60, work.Top + 40));
            var view = ViewOf(form);
            var before = TopBlockOnScreen(form);
            var heightBefore = form.Height;

            ClickToggle(view);

            Assert.True(form.Height > heightBefore, "Expanded is taller");
            Assert.Equal(before.Location, TopBlockOnScreen(form).Location);
            Assert.Equal(before.Y, form.RectangleToScreen(Rectangle.Empty).Y); // the top edge did not move
            ClickToggle(view); // and back
            Assert.Equal(before.Location, TopBlockOnScreen(form).Location);
            form.Close();
        });
    }

    [Fact]
    public void With_little_room_below_the_clock_still_does_not_move_only_the_usage_area_gets_shorter_and_scrolls()
    {
        OnStaThread(() =>
        {
            var work = Screen.PrimaryScreen!.WorkingArea;
            var top = work.Bottom - 330;
            using var form = Form(new Point(work.Left + 60, top));
            var view = ViewOf(form);
            var before = TopBlockOnScreen(form);

            ClickToggle(view);

            Assert.Equal(before.Location, TopBlockOnScreen(form).Location);
            Assert.True(form.Bottom <= work.Bottom, "the window stays inside the working area");
            Assert.Equal(top, form.Top);
            form.Close();
        });
    }

    [Theory]
    [InlineData(500, UsageGeometry.MaxExpandedHeight)]
    [InlineData(100, 100)]
    [InlineData(20, UsageGeometry.CompactHeight)]
    [InlineData(-40, UsageGeometry.CompactHeight)]
    public void The_room_below_decides_how_tall_Expanded_may_be_but_never_less_than_Compact_or_more_than_the_cap(int room, int expected)
    {
        Assert.Equal(expected, UsageGeometry.ExpandedLimit(room));
    }

    [Fact]
    public void A_shorter_limit_makes_the_area_scroll_sooner_and_keeps_everything_reachable()
    {
        var views = Enumerable.Range(0, 3).Select(i => new UsageProviderView("P" + i, "", [new UsageRow("5h", "10%", 0.1, "1h", UsageLevel.Normal), new UsageRow("Week", "10%", 0.1, "1d", UsageLevel.Normal)], "x", false, null, "p" + i)).ToList();

        Assert.Equal(100, UsageGeometry.AreaHeight(views, true, 100));
        Assert.Equal(UsageGeometry.ExpandedContentHeight(views) - 100, UsageGeometry.MaxScroll(views, 100));
        Assert.True(UsageGeometry.MaxScroll(views, 100) > UsageGeometry.MaxScroll(views));
    }

    [Fact]
    public void The_work_area_of_a_widget_is_the_one_it_overlaps_most()
    {
        var left = new Rectangle(0, 0, 1920, 1040);
        var right = new Rectangle(1920, 0, 1920, 1040);

        Assert.Equal(right, WidgetPlacement.AreaFor(new Rectangle(2000, 100, 280, 300), [left, right]));
        Assert.Equal(left, WidgetPlacement.AreaFor(new Rectangle(100, 100, 280, 300), [left, right]));
    }
}