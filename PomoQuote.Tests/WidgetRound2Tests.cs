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
        foreach (var theme in new[] { WidgetTheme.Dark, WidgetTheme.Light }) AssertNotWarmColour(theme.Phase((PomodoroPhase)phaseNumber), phaseNumber);
    }

    private static void AssertNotWarmColour(Color colour, int phaseNumber)
    {

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
        form.SetTheme(ThemeMode.Dark); // these tests measure the dark panel; the theme tests cover the others
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

    // ---------- the separator, the centred button and the plain blocks ----------

    private static UsageExpandToggle ToggleOf(WidgetView view) => (UsageExpandToggle)typeof(WidgetView).GetField("_toggle", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;

    private static UsageLogoSelector SelectorOf(WidgetView view) => (UsageLogoSelector)typeof(WidgetView).GetField("_selector", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;

    [Fact]
    public void The_expand_button_sits_centred_on_the_separator_line_in_both_layouts_and_not_in_the_margin()
    {
        OnStaThread(() =>
        {
            var work = Screen.PrimaryScreen!.WorkingArea;
            using var form = Form(new Point(work.Left + 60, work.Top + 40));
            var view = ViewOf(form);
            var toggle = ToggleOf(view);

            foreach (var expanded in new[] { false, true })
            {
                if (expanded) ClickToggle(view);
                var unit = view.ClientSize.Width / (float)UsageGeometry.WidgetWidth;
                var centre = new PointF(toggle.Left + toggle.Width / 2f, toggle.Top + toggle.Height / 2f);

                Assert.True(toggle.Visible);
                Assert.InRange(centre.X, view.ClientSize.Width / 2f - 1.5f, view.ClientSize.Width / 2f + 1.5f); // horizontally centred
                Assert.InRange(centre.Y, view.SeparatorY * unit - 1.5f, view.SeparatorY * unit + 1.5f); // on the line
                Assert.True(toggle.Left > UsageGeometry.Margin * unit * 3, "not in the left margin");
                Assert.True(toggle.Right < view.ClientSize.Width - UsageGeometry.Margin * unit * 3, "not in the right margin");
                Assert.True(toggle.TabStop && toggle.AccessibleName.Length > 0, "keyboard access and an accessible name");
                Assert.Equal(expanded ? "Collapse AI usage" : "Expand AI usage", toggle.AccessibleName);
            }

            form.Close();
        });
    }

    [Fact]
    public void The_separator_is_between_the_quote_and_the_usage_area_and_adds_no_row_in_compact()
    {
        OnStaThread(() =>
        {
            var work = Screen.PrimaryScreen!.WorkingArea;
            using var form = Form(new Point(work.Left + 60, work.Top + 40));
            var view = ViewOf(form);

            Assert.True(view.SeparatorY < view.UsageTop, "the line is above the first row");
            Assert.True(view.UsageTop - view.SeparatorY <= UsageGeometry.AreaTopGap);
            // Compact is still one fixed area of at most three lines below the line.
            Assert.Equal(UsageGeometry.CompactHeight, UsageGeometry.AreaHeight([new UsageProviderView("a", "", [], "x", false)], false));
            form.Close();
        });
    }

    [Fact]
    public void Switching_provider_in_Compact_never_changes_the_widget_height()
    {
        OnStaThread(() =>
        {
            var work = Screen.PrimaryScreen!.WorkingArea;
            using var form = Form(new Point(work.Left + 60, work.Top + 40));
            var view = ViewOf(form);
            var heights = new HashSet<int> { view.LogicalHeight };
            var heightsPixels = new HashSet<int> { form.Height };
            var wheel = typeof(Control).GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)!;

            for (var i = 0; i < 7; i++)
            {
                wheel.Invoke(SelectorOf(view), [new MouseEventArgs(MouseButtons.None, 0, 3, 3, -120)]);
                Pump(120);
                heights.Add(view.LogicalHeight);
                heightsPixels.Add(form.Height);
            }

            Assert.Single(heights);
            Assert.Single(heightsPixels);
            form.Close();
        });
    }

    [Fact]
    public void Expanded_paints_no_card_background_border_or_rounded_container()
    {
        OnStaThread(() =>
        {
            var work = Screen.PrimaryScreen!.WorkingArea;
            using var form = Form(new Point(work.Left + 60, work.Top + 40));
            var view = ViewOf(form);
            ClickToggle(view);
            using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            var unit = view.ClientSize.Width / (float)UsageGeometry.WidgetWidth;
            var panel = Color.FromArgb(28, 32, 40);
            var border = form.Size - form.ClientSize; // DrawToBitmap includes the window frame, if any
            var frame = new Point(border.Width / 2, border.Height - border.Width / 2);

            // Where the old cards were: the empty strips between the blocks and the empty end of the footer lines.
            foreach (var blockTop in Enumerable.Range(0, 2).Select(i => view.UsageTop + i * (UsageGeometry.CardHeight(new UsageProviderView("a", "", [new UsageRow("5h", "1%", 0.01, "1h", UsageLevel.Normal), new UsageRow("Week", "1%", 0.01, "1h", UsageLevel.Normal)], "x", false)) + UsageGeometry.CardGap)))
            {
                var y = (int)((blockTop + 4) * unit) + frame.Y;
                foreach (var x in new[] { 3 + (int)(UsageGeometry.Margin * unit), (int)((UsageGeometry.WidgetWidth - UsageGeometry.Margin - UsageGeometry.ScrollGutter - 4) * unit) })
                {
                    var pixel = bitmap.GetPixel(Math.Min(x + frame.X, bitmap.Width - 1), Math.Min(y, bitmap.Height - 1));
                    Assert.True(pixel == panel || (Math.Abs(pixel.R - panel.R) < 14 && Math.Abs(pixel.G - panel.G) < 14 && Math.Abs(pixel.B - panel.B) < 14), $"a card background is painted at ({x},{y}): {pixel}");
                }
            }

            // And the code that draws the blocks has no fill, rounded path or card colour.
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "WidgetView.Usage.cs")).Replace("\r\n", "\n");
            var start = source.IndexOf("private void DrawExpanded(", StringComparison.Ordinal);
            var end = source.IndexOf("private void DrawScrollbar(", StringComparison.Ordinal);
            var method = source[start..end];
            Assert.DoesNotContain("CardColor", method);
            Assert.DoesNotContain("PillBar.Rounded", method);
            Assert.DoesNotContain("FillPath", method);
            Assert.DoesNotContain("FillRectangle", method);
            form.Close();
        });
    }

    private static string RepositoryRoot()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "PomoQuote.csproj"))) root = Path.GetDirectoryName(root);
        return root ?? throw new InvalidOperationException("repository root not found");
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