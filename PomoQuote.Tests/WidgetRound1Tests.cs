using System.Drawing;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using PomoQuote.AiUsage;
using PomoQuote.AiUsage.Claude;
using PomoQuote.Mascot;
using PomoQuote.Pomodoro;
using PomoQuote.Quotes;
using PomoQuote.Weather;
using PomoQuote.Widget;

namespace PomoQuote.Tests;

/// <summary>The timer tooltip, the quote card's text size and corner, the width fit, the drawing code's disposals, the About notice and the keyboard of Expanded.</summary>
public sealed class WidgetRound1Tests
{
    private static string RepositoryRoot()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "PomoQuote.csproj"))) root = Path.GetDirectoryName(root);
        return root ?? throw new InvalidOperationException("repository root not found");
    }

    // ---------- the timer tooltip ----------

    [Fact]
    public void A_running_timer_says_the_phase_elapsed_remaining_and_the_exact_end_time()
    {
        var now = new DateTime(2026, 11, 24, 17, 0, 0);

        var text = TimerTooltip.Text(PomodoroPhase.Work, PomodoroRunState.Running, TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(30), now);

        Assert.Contains("Work", text);
        Assert.Contains("Elapsed 12:30 of 25:00", text);
        Assert.Contains("Remaining 12:30", text);
        Assert.Contains("Ends at 17:12:30", text);
    }

    [Fact]
    public void A_paused_or_idle_timer_has_no_end_time_to_promise()
    {
        var now = new DateTime(2026, 11, 24, 17, 0, 0);

        var paused = TimerTooltip.Text(PomodoroPhase.ShortBreak, PomodoroRunState.Paused, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(2), now);
        var idle = TimerTooltip.Text(PomodoroPhase.Work, PomodoroRunState.Idle, TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(25), now);

        Assert.Contains("(paused)", paused);
        Assert.Contains("Elapsed 03:00 of 05:00", paused);
        Assert.DoesNotContain("Ends at", paused);
        Assert.Contains("not started", idle);
        Assert.Contains("Length 25:00", idle);
        Assert.DoesNotContain("Ends at", idle);
    }

    [Fact]
    public void The_tooltip_clamps_a_remaining_time_outside_the_phase()
    {
        var now = new DateTime(2026, 11, 24, 17, 0, 0);

        var over = TimerTooltip.Text(PomodoroPhase.Work, PomodoroRunState.Running, TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(30), now);
        var under = TimerTooltip.Text(PomodoroPhase.Work, PomodoroRunState.Running, TimeSpan.FromMinutes(25), TimeSpan.FromSeconds(-3), now);

        Assert.Contains("Remaining 25:00", over);
        Assert.Contains("Remaining 00:00", under);
    }

    // ---------- the quote card: text size and the closing mark ----------

    private static Func<float, int, IReadOnlyList<string>> Wrapper(string text, float widthAt12 = 222)
    {
        // A fake font: 0.5 em per character, so the width scales with the size.
        return (pixels, maxLines) => TextWrap.Lines(text, widthAt12, s => s.Length * pixels * 0.5f, maxLines);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(140)]
    public void A_text_that_fits_the_clamp_keeps_12_px_or_more_and_is_never_cut(int words)
    {
        var english = string.Join(" ", Enumerable.Repeat("carpe", words));
        var vietnamese = string.Join(" ", Enumerable.Repeat("diem", words));

        var plan = QuoteGeometry.Choose(Wrapper(english), Wrapper(vietnamese));

        if (!plan.BelowMinimum)
        {
            Assert.True(plan.EnglishPixels >= 12 && plan.VietnamesePixels >= 12);
            Assert.False(plan.Ellipsized);
            Assert.Equal(english, string.Join(" ", plan.English));
            Assert.Equal(vietnamese, string.Join(" ", plan.Vietnamese!));
        }

        Assert.True(plan.CardHeight <= QuoteGeometry.MaxCardHeight);
    }

    [Fact]
    public void Only_when_the_clamp_is_reached_does_the_text_go_below_12_px_and_the_plan_says_so()
    {
        var long1 = string.Join(" ", Enumerable.Repeat("extraordinarily", 60));
        var long2 = string.Join(" ", Enumerable.Repeat("phenomenal", 60));

        var plan = QuoteGeometry.Choose(Wrapper(long1), Wrapper(long2));

        Assert.True(plan.BelowMinimum);
        Assert.True(plan.EnglishPixels < 12);
        Assert.True(plan.CardHeight <= QuoteGeometry.MaxCardHeight);
    }

    [Fact]
    public void Past_the_smallest_size_the_last_line_ends_with_an_ellipsis_and_the_card_stays_at_the_clamp()
    {
        var huge = string.Join(" ", Enumerable.Repeat("extraordinarily", 400));

        var plan = QuoteGeometry.Choose(Wrapper(huge), null);

        Assert.True(plan.Ellipsized);
        Assert.True(plan.BelowMinimum);
        Assert.EndsWith("…", plan.English[^1]);
        Assert.True(plan.CardHeight <= QuoteGeometry.MaxCardHeight);
    }

    [Fact]
    public void A_short_quote_gets_the_largest_size_and_a_card_of_its_own_height()
    {
        var plan = QuoteGeometry.Choose(Wrapper("Actions have consequences."), Wrapper("Hành động có hậu quả."));

        Assert.Equal(13f, plan.EnglishPixels);
        Assert.Equal(12f, plan.VietnamesePixels);
        Assert.False(plan.BelowMinimum);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 2)]
    [InlineData(6, 5)]
    [InlineData(12, 9)]
    public void The_closing_mark_always_starts_below_the_last_text_line(int englishLines, int vietnameseLines)
    {
        var height = QuoteGeometry.Height(englishLines, 13, vietnameseLines, 12);

        Assert.True(QuoteGeometry.ClosingMarkTop(height) >= QuoteGeometry.LastLineBottom(height));
        Assert.True(QuoteGeometry.ClosingMarkVisibleHeight <= QuoteGeometry.PaddingBottom);
        // and the drawing uses these numbers, not its own
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "WidgetView.cs"));
        Assert.Contains("QuoteGeometry.ClosingMarkVisibleHeight", source);
    }

    // ---------- the supported width ----------

    [Fact]
    public void The_widget_is_one_fixed_width_and_no_narrower_layout_is_offered()
    {
        Assert.Equal(280, UsageGeometry.WidgetWidth);
        Assert.Equal(280, WidgetView.LogicalSize.Width);
        Assert.Equal(WidgetView.LogicalSize.Width, new Size(UsageGeometry.WidgetWidth, 1).Width);
    }

    [Fact]
    public void At_280_px_the_longest_labels_percents_and_countdowns_fit_their_columns_in_both_layouts()
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        using var regular = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var semibold = new Font("Segoe UI Semibold", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        if (regular.Name != "Segoe UI") return; // the design font is not installed on this machine
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces };
        float Width(string text, Font font) => graphics.MeasureString(text, font, int.MaxValue, format).Width;

        foreach (var columns in new[] { UsageGeometry.Columns(0, UsageGeometry.CompactRowsWidth), UsageGeometry.Columns(0, UsageGeometry.CardRowsWidth) })
        {
            Assert.True(Width("Week", regular) <= columns.LabelWidth, "the longest standard label");
            Assert.True(Width("3h", regular) <= columns.LabelWidth);
            Assert.True(Width("100%", semibold) + 10 <= columns.PercentWidth, "100% plus the stale clock");
            foreach (var reset in new[] { "23h 59m", "6d 23h", "13h 16m", "<1m", "due" }) Assert.True(Width(reset, regular) <= columns.ResetWidth, reset);
            Assert.True(columns.BarWidth >= 44);
            Assert.True(columns.Right <= UsageGeometry.WidgetWidth - UsageGeometry.Margin);
        }
    }

    [Fact]
    public void A_tall_pill_for_the_scrollbar_covers_its_whole_rectangle_not_a_dot()
    {
        using var tall = PillBar.Pill(new RectangleF(10, 20, 3, 100));
        using var wide = PillBar.Pill(new RectangleF(10, 20, 100, 7));

        var tallBounds = tall.GetBounds();
        var wideBounds = wide.GetBounds();

        Assert.InRange(tallBounds.Height, 98, 101);
        Assert.InRange(tallBounds.Width, 2.5f, 3.5f);
        Assert.InRange(wideBounds.Width, 98, 101);
        Assert.InRange(wideBounds.Height, 6, 8);
    }

    // ---------- the drawing code disposes what it creates ----------

    [Fact]
    public void Every_pen_brush_font_string_format_and_path_the_widget_code_creates_is_in_a_using()
    {
        var root = RepositoryRoot();
        var files = new[] { "WidgetView.cs", "WidgetView.Usage.cs", "UsageLogoSelector.cs", "UsageExpandToggle.cs", "WidgetGlyphs.cs", Path.Combine("Widget", "PillBar.cs"), Path.Combine("Widget", "PomodoroIcon.cs") };
        var creation = new Regex(@"new (Pen|SolidBrush|Font|StringFormat|GraphicsPath|ImageAttributes|LinearGradientBrush)\(|PillBar\.(Pill|Rounded)\(|= Pill\(");
        var offenders = new List<string>();

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(Path.Combine(root, file));
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!creation.IsMatch(line) || line.TrimStart().StartsWith("///") || line.TrimStart().StartsWith("//")) continue;
                if (line.Contains("using ")) continue;
                // The helpers that build and return a path are the one place a path is not disposed where it is created.
                if (file.EndsWith("PillBar.cs") && (line.Contains("var path = new GraphicsPath()") || line.Contains("public static GraphicsPath"))) continue;
                offenders.Add($"{file}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0, "created without a using:\n" + string.Join("\n", offenders));
    }

    // ---------- the About notice ----------

    [Fact]
    public void The_about_dialog_carries_the_marks_notice_and_the_readme_links_the_credits()
    {
        Assert.Contains("OpenAI and the OpenAI Blossom logo belong to OpenAI", AboutDialog.MarksNotice);
        Assert.Contains("Anthropic", AboutDialog.MarksNotice);
        Assert.Contains("Google", AboutDialog.MarksNotice);
        Assert.Contains("not affiliated with or endorsed by", AboutDialog.MarksNotice);
        Assert.Contains("THIRD_PARTY_ASSETS.md", AboutDialog.MarksNotice);
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md"));
        Assert.Contains("(THIRD_PARTY_ASSETS.md)", readme);
        Assert.Contains("About", readme);
    }

    [Fact]
    public void The_about_dialog_shows_the_notice_with_an_accessible_name()
    {
        using var dialog = new AboutDialog();

        var label = FindLabel(dialog, AboutDialog.MarksNotice);

        Assert.NotNull(label);
        Assert.Equal("Third-party marks", label!.AccessibleName);
    }

    private static Label? FindLabel(Control parent, string text)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is Label label && label.Text == text) return label;
            if (FindLabel(child, text) is { } found) return found;
        }

        return null;
    }
}

/// <summary>Expanded works as a keyboard list: focus, Up/Down/Home/End move between the cards and scroll them into view, and every card has an accessible name and description.</summary>
[Collection("process")]
public sealed class ExpandedKeyboardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PomoQuoteKeys-" + Guid.NewGuid().ToString("N"));

    public ExpandedKeyboardTests() => Directory.CreateDirectory(_root);

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

    private MainForm ExpandedForm(string[] ids)
    {
        var usagePath = Path.Combine(_root, "usage.json");
        var settings = UsageSettings.Default with { Order = ids, Expanded = true, AlertsEnabled = false };
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
            Location = new Point(-3000, -3000),
        };
        form.BridgeNoticeSink = (_, _) => { };
        form.Show();
        form.SetWidgetMode(true);
        Pump(400);
        return form;
    }

    private static WidgetView ViewOf(MainForm form) => (WidgetView)typeof(MainForm).GetField("_widgetView", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;

    private static void Press(Control control, Keys key) =>
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, [new KeyEventArgs(key)]);

    private static void Focus(Control control) =>
        typeof(Control).GetMethod("OnGotFocus", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, [EventArgs.Empty]);

    [Fact]
    public void Down_Up_Home_and_End_move_between_the_cards_and_scroll_the_focused_card_into_view()
    {
        OnStaThread(() =>
        {
            using var form = ExpandedForm(["a", "b", "c", "d", "e"]);
            var view = ViewOf(form);
            Assert.True(view.TabStop, "Expanded is a tab stop");
            Focus(view);
            Assert.Equal(0, view.FocusedCard);
            Assert.Equal(0, view.ScrollOffset);

            Press(view, Keys.Down);
            Press(view, Keys.Down);
            Assert.Equal(2, view.FocusedCard);

            Press(view, Keys.End);
            Assert.Equal(4, view.FocusedCard);
            Assert.True(view.ScrollOffset > 0, "the last card was scrolled into view");

            Press(view, Keys.Up);
            Assert.Equal(3, view.FocusedCard);
            Press(view, Keys.Home);
            Assert.Equal(0, view.FocusedCard);
            Assert.Equal(0, view.ScrollOffset);
            Press(view, Keys.Up);
            Assert.Equal(0, view.FocusedCard); // never below the first
            form.Close();
        });
    }

    [Fact]
    public void Every_card_has_an_accessible_list_item_with_a_name_and_a_description()
    {
        OnStaThread(() =>
        {
            using var form = ExpandedForm(["a", "b", "c"]);
            var view = ViewOf(form);
            Focus(view);
            Pump(300);

            var accessible = view.AccessibilityObject!;
            var items = Enumerable.Range(0, accessible.GetChildCount()).Select(i => accessible.GetChild(i)!).Where(c => c.Role == AccessibleRole.ListItem).ToList();

            Assert.Equal(3, items.Count);
            Assert.Equal(["A, plus", "B, plus", "C, plus"], items.Select(i => i.Name ?? "").ToArray()); // name and plan
            Assert.All(items, item => Assert.False(string.IsNullOrWhiteSpace(item.Description)));
            Assert.Contains(items, item => item.State.HasFlag(AccessibleStates.Focused));
            Assert.All(items, item => Assert.True(item.Bounds.Width > 0 && item.Bounds.Height > 0));
            form.Close();
        });
    }

    [Fact]
    public void Compact_is_not_a_list_tab_stop_and_has_no_card_items()
    {
        OnStaThread(() =>
        {
            using var form = ExpandedForm(["a", "b"]);
            var view = ViewOf(form);
            var toggle = (UsageExpandToggle)typeof(WidgetView).GetField("_toggle", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
            typeof(Control).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(toggle, [new MouseEventArgs(MouseButtons.Left, 1, 3, 3, 0)]);
            Pump(300);

            Assert.False(view.TabStop);
            var accessible = view.AccessibilityObject!;
            Assert.DoesNotContain(Enumerable.Range(0, accessible.GetChildCount()).Select(i => accessible.GetChild(i)!), c => c.Role == AccessibleRole.ListItem);
            form.Close();
        });
    }
}
