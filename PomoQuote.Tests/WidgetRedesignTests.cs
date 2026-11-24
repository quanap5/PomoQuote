using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using PomoQuote.AiUsage;
using PomoQuote.Widget;

namespace PomoQuote.Tests;

/// <summary>The measurements, fitting and state of the redesigned widget: pill bars, columns, Compact and Expanded heights, the carousel, text wrapping, icons and the remembered layout.</summary>
public sealed class WidgetRedesignTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static UsageProviderView View(string id, string name, int rows, string plan = "", string note = "updated 10:00", ProviderSeverity severity = ProviderSeverity.None) =>
        new(name, plan, Enumerable.Range(0, rows).Select(i => new UsageRow(i == 0 ? "5h" : "Week", "10%", 0.1, "3h 11m", UsageLevel.Normal)).ToList(), note, false, null, id, severity);

    // ---------- pill bars ----------

    [Fact]
    public void A_bar_at_zero_draws_no_fill_at_all_so_there_is_no_stray_dot()
    {
        Assert.Equal(0f, PillBar.FillWidth(100, 7, 0));
        Assert.Equal(0f, PillBar.FillWidth(100, 7, -0.2));
        Assert.Equal(0f, PillBar.FillWidth(100, 7, double.NaN));
    }

    [Fact]
    public void A_small_value_gets_a_full_round_end_and_full_is_the_whole_track()
    {
        Assert.Equal(7f, PillBar.FillWidth(100, 7, 0.001)); // never narrower than the height: a clean pill, not a sliver
        Assert.Equal(50f, PillBar.FillWidth(100, 7, 0.5));
        Assert.Equal(100f, PillBar.FillWidth(100, 7, 1));
        Assert.Equal(100f, PillBar.FillWidth(100, 7, 3));
        Assert.Equal(4f, PillBar.FillWidth(4, 7, 0.5)); // a track narrower than its height
    }

    [Fact]
    public void The_pill_paths_are_built_for_normal_and_tiny_sizes()
    {
        using var normal = PillBar.Pill(new RectangleF(0, 0, 100, 7));
        using var tiny = PillBar.Pill(new RectangleF(0, 0, 0.2f, 0.2f));
        using var card = PillBar.Rounded(new RectangleF(0, 0, 100, 40), 11);
        Assert.True(normal.PointCount > 4);
        Assert.True(tiny.PointCount >= 4);
        Assert.True(card.PointCount > 8);
    }

    // ---------- the quota row columns and the area heights ----------

    [Theory]
    [InlineData(160f)]
    [InlineData(206f)]
    [InlineData(250f)]
    [InlineData(400f)]
    public void The_columns_of_a_row_never_overlap_and_stay_in_order(float width)
    {
        var c = UsageGeometry.Columns(10, width);

        Assert.True(c.LabelX + c.LabelWidth <= c.BarX);
        Assert.True(c.BarX + c.BarWidth <= c.PercentX);
        Assert.True(c.PercentX + c.PercentWidth <= c.ResetX);
        Assert.True(c.BarWidth >= 24);
    }

    [Fact]
    public void At_the_widget_width_the_rows_leave_room_for_the_bar_in_both_layouts()
    {
        Assert.True(UsageGeometry.Columns(0, UsageGeometry.CompactRowsWidth).BarWidth >= 60);
        Assert.True(UsageGeometry.Columns(0, UsageGeometry.CardRowsWidth).BarWidth >= 44);
        Assert.Equal(UsageGeometry.WidgetWidth - 2 * UsageGeometry.Margin - UsageGeometry.LogoColumn - UsageGeometry.ToggleColumn, UsageGeometry.CompactRowsWidth);
    }

    [Fact]
    public void Compact_keeps_one_fixed_height_whichever_provider_is_shown_and_within_three_lines()
    {
        var none = View("a", "A", 0);
        var two = View("b", "B", 2);
        var three = View("c", "C", 3);

        Assert.Equal(UsageGeometry.CompactHeight, UsageGeometry.AreaHeight([none], expanded: false));
        Assert.Equal(UsageGeometry.CompactHeight, UsageGeometry.AreaHeight([two, three], expanded: false));
        Assert.True(UsageGeometry.CompactHeight <= 3 * UsageGeometry.RowHeight);
        Assert.Equal(0, UsageGeometry.AreaHeight([], expanded: false));
        Assert.Equal(0, UsageGeometry.AreaHeight([], expanded: true));
    }

    [Fact]
    public void Expanded_grows_with_the_cards_up_to_a_cap_and_then_only_that_area_scrolls()
    {
        var one = View("a", "A", 2);
        var five = Enumerable.Range(0, 5).Select(i => View("p" + i, "P" + i, 2)).ToList();

        Assert.Equal(UsageGeometry.CardHeight(one), UsageGeometry.AreaHeight([one], expanded: true));
        Assert.Equal(UsageGeometry.MaxExpandedHeight, UsageGeometry.AreaHeight(five, expanded: true));
        Assert.Equal(0, UsageGeometry.MaxScroll([one]));
        Assert.Equal(UsageGeometry.ExpandedContentHeight(five) - UsageGeometry.MaxExpandedHeight, UsageGeometry.MaxScroll(five));
    }

    [Fact]
    public void A_card_without_data_is_as_tall_as_one_with_one_window()
    {
        Assert.Equal(UsageGeometry.CardHeight(View("a", "A", 1)), UsageGeometry.CardHeight(View("a", "A", 0)));
        Assert.True(UsageGeometry.CardHeight(View("a", "A", 2)) > UsageGeometry.CardHeight(View("a", "A", 1)));
    }

    // ---------- the carousel ----------

    [Fact]
    public void The_carousel_wraps_both_ways_and_one_provider_stays_itself()
    {
        string[] ids = ["codex", "claude", "antigravity"];

        Assert.Equal("claude", ProviderCarousel.Step(ids, "codex", 1));
        Assert.Equal("codex", ProviderCarousel.Step(ids, "antigravity", 1));
        Assert.Equal("antigravity", ProviderCarousel.Step(ids, "codex", -1));
        Assert.Equal("only", ProviderCarousel.Step(["only"], "only", 1));
        Assert.Null(ProviderCarousel.Step([], null, 1));
    }

    [Fact]
    public void A_missing_or_unknown_selection_falls_back_to_the_first_provider()
    {
        string[] ids = ["codex", "claude"];

        Assert.Equal("codex", ProviderCarousel.Resolve(ids, null));
        Assert.Equal("codex", ProviderCarousel.Resolve(ids, "switched-off"));
        Assert.Equal("claude", ProviderCarousel.Resolve(ids, "claude"));
        Assert.Null(ProviderCarousel.Resolve([], "claude"));
        Assert.Equal("Claude (2 of 2)", ProviderCarousel.Position(["Codex", "Claude"], 1));
        Assert.Equal("Codex", ProviderCarousel.Position(["Codex"], 0));
    }

    // ---------- text wrapping (the quote card never cuts the text) ----------

    private static float Chars(string s) => s.Length * 6f;

    [Fact]
    public void Wrapped_lines_fit_and_keep_all_the_words_in_order()
    {
        const string text = "The only way to do great work is to love what you do";

        var lines = TextWrap.Lines(text, 120, Chars);

        Assert.All(lines, line => Assert.True(Chars(line) <= 120));
        Assert.Equal(text, string.Join(" ", lines));
        Assert.True(lines.Count > 1);
    }

    [Fact]
    public void A_word_wider_than_the_line_is_broken_by_characters_instead_of_overflowing()
    {
        var lines = TextWrap.Lines("Supercalifragilisticexpialidocious ok", 60, Chars);

        Assert.All(lines, line => Assert.True(Chars(line) <= 60));
        Assert.Equal("Supercalifragilisticexpialidociousok", string.Concat(lines).Replace(" ", ""));
        Assert.Contains("ok", lines[^1]);
    }

    [Fact]
    public void Only_when_a_line_limit_is_given_the_last_line_ends_with_an_ellipsis()
    {
        const string text = "one two three four five six seven eight nine ten";

        var all = TextWrap.Lines(text, 60, Chars);
        var limited = TextWrap.Lines(text, 60, Chars, maxLines: 2);

        Assert.DoesNotContain(all, l => l.EndsWith('…'));
        Assert.Equal(2, limited.Count);
        Assert.EndsWith("…", limited[^1]);
        Assert.All(limited, line => Assert.True(Chars(line) <= 60));
        Assert.Empty(TextWrap.Lines("   ", 60, Chars));
    }

    // ---------- the remembered layout ----------

    [Fact]
    public void The_layout_and_the_selected_provider_survive_a_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "PomoQuoteLayout-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new UsageSettingsStore(path);
            store.Save(UsageSettings.Default.WithEnabled("codex", true) with { Expanded = true, SelectedProvider = "claude" });

            var loaded = new UsageSettingsStore(path).Load();

            Assert.True(loaded.Expanded);
            Assert.Equal("claude", loaded.SelectedProvider);
            Assert.True(loaded.IsEnabled("codex"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_file_from_before_the_redesign_opens_in_compact_with_the_first_provider()
    {
        var path = Path.Combine(Path.GetTempPath(), "PomoQuoteLayout-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"EnabledProviders\":[\"codex\"],\"Order\":[\"codex\",\"claude\"],\"IntervalMinutes\":3,\"DisplayMode\":\"Percent\",\"AlertsEnabled\":true,\"Thresholds\":[80,95]}");

            var loaded = new UsageSettingsStore(path).Load();

            Assert.False(loaded.Expanded);
            Assert.Null(loaded.SelectedProvider);
            Assert.True(loaded.IsEnabled("codex"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---------- what the widget is told about a provider ----------

    private static UsageStatus Status(string id, string name, UsageSnapshot? latest, UsageSnapshot? good = null) => new(id, name, latest, good ?? latest);

    private static UsageProviderView Build(UsageStatus status, DateTimeOffset? now = null) =>
        UsageViewBuilder.Build(status, UsageSettings.Default.WithEnabled(status.ProviderId, true), now ?? UsageTestData.Now.AddMinutes(1), Utc);

    [Fact]
    public void Fresh_numbers_carry_the_source_the_plan_the_exact_reset_and_no_badge()
    {
        var view = Build(Status("codex", "Codex", UsageTestData.Snapshot(21, 27)));

        Assert.Equal("codex", view.Id);
        Assert.Equal(ProviderSeverity.None, view.Severity);
        Assert.True(view.HasData);
        Assert.Equal("Windows", view.Source);
        Assert.Contains("Codex", view.Tip);
        Assert.Contains("Source: Windows", view.Tip);
        var tip = view.Rows[0].Tip;
        Assert.Contains("used 21%", tip);
        Assert.Contains("left 79%", tip);
        Assert.Contains("Resets", tip);
        Assert.Contains(":", tip); // the exact time of day
    }

    [Fact]
    public void A_missing_quota_is_not_data_so_the_widget_says_N_A_and_never_0_percent()
    {
        var view = Build(Status("claude", "Claude", UsageSnapshot.Empty(UsageTestData.Source(), UsageTestData.Now, UsageDataSource.CodexAppServer, UsageState.NotSetUp, "x")));

        Assert.False(view.HasData);
        Assert.Empty(view.Rows);
        Assert.Equal(ProviderSeverity.None, view.Severity);
    }

    [Theory]
    [InlineData(UsageState.NotLoggedIn)]
    [InlineData(UsageState.NotInstalled)]
    [InlineData(UsageState.Unsupported)]
    [InlineData(UsageState.Error)]
    [InlineData(UsageState.BridgeMissing)]
    public void A_failed_read_is_an_error_badge(UsageState state)
    {
        var view = Build(Status("codex", "Codex", UsageSnapshot.Empty(UsageTestData.Source(), UsageTestData.Now, UsageDataSource.CodexAppServer, state, "x")));

        Assert.Equal(ProviderSeverity.Error, view.Severity);
        Assert.True(view.IsProblem);
    }

    [Fact]
    public void Old_numbers_are_stale_by_icon_and_label_and_tooltip_not_only_by_colour()
    {
        var snapshot = UsageTestData.Snapshot(21, 27);
        var view = Build(Status("codex", "Codex", snapshot), UsageTestData.Now.AddMinutes(40));

        Assert.Equal(ProviderSeverity.Stale, view.Severity);
        Assert.All(view.Rows, row => Assert.True(row.Stale));
        Assert.All(view.Rows, row => Assert.Contains("old", row.Tip));
        Assert.Contains("stale", view.Note);
    }

    [Fact]
    public void A_window_over_the_warning_level_is_a_near_limit_badge()
    {
        var view = Build(Status("codex", "Codex", UsageTestData.Snapshot(86, 10)));

        Assert.Equal(ProviderSeverity.NearLimit, view.Severity);
        Assert.Contains("getting full", view.Tip);
        var critical = Build(Status("codex", "Codex", UsageTestData.Snapshot(97, 10)));
        Assert.Contains("almost used up", critical.Tip);
    }

    [Theory]
    [InlineData(180, "3h")]
    [InlineData(1440, "1d")]
    [InlineData(2880, "2d")]
    [InlineData(20160, "2w")]
    [InlineData(90, "90m")]
    public void A_window_that_is_not_five_hours_or_a_week_is_named_by_its_real_length(int minutes, string label)
    {
        var window = new QuotaWindow(QuotaKind.Other, minutes, 10, UsageTestData.Now.AddHours(1), "", true);

        Assert.Equal(label, UsageViewBuilder.Label(window));
        Assert.Equal("5h", UsageViewBuilder.Label(new QuotaWindow(QuotaKind.FiveHour, 300, 10, null, "", false)));
        Assert.Equal("Week", UsageViewBuilder.Label(new QuotaWindow(QuotaKind.Weekly, 10080, 10, null, "", false)));
        Assert.Equal("premium", UsageViewBuilder.Label(new QuotaWindow(QuotaKind.Other, 43200, 10, null, "premium", false)));
    }

    // ---------- icons ----------

    [Theory]
    [InlineData("codex", IconBackground.Dark)]
    [InlineData("codex", IconBackground.Light)]
    [InlineData("claude", IconBackground.Dark)]
    [InlineData("claude", IconBackground.Light)]
    [InlineData("antigravity", IconBackground.Dark)]
    [InlineData("antigravity", IconBackground.Light)]
    public void Every_provider_has_a_packaged_icon_for_both_backgrounds_at_the_requested_size(string id, IconBackground background)
    {
        using var icons = new ProviderIcons();

        var icon = icons.Get(id, background, 20);

        Assert.NotNull(icon);
        Assert.Equal(20, icon!.Width);
        Assert.Equal(20, icon.Height);
        Assert.Same(icon, icons.Get(id, background, 20)); // loaded and scaled once
    }

    [Fact]
    public void A_missing_unknown_or_corrupt_icon_gives_null_so_the_name_alone_is_shown()
    {
        using var missing = new ProviderIcons(_ => null);
        using var corrupt = new ProviderIcons(_ => new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8]));
        using var empty = new ProviderIcons(_ => new MemoryStream([]));
        using var real = new ProviderIcons();

        Assert.Null(missing.Get("codex", IconBackground.Dark, 20));
        Assert.Null(corrupt.Get("codex", IconBackground.Dark, 20));
        Assert.Null(empty.Get("codex", IconBackground.Dark, 20));
        Assert.Null(real.Get("not-a-provider", IconBackground.Dark, 20));
        Assert.Null(real.Get("", IconBackground.Dark, 20));
        Assert.Null(real.Get("codex", IconBackground.Dark, 2));
    }

    [Fact]
    public void Scaling_a_big_logo_down_keeps_it_visible_and_centred_and_the_cache_stays_small()
    {
        using var icons = new ProviderIcons();

        foreach (var size in new[] { 20, 25, 30, 40 })
        {
            var icon = icons.Get("codex", IconBackground.Dark, size)!;
            var opaque = 0; var coverage = 0.0; var peak = 0;
            for (var y = 0; y < icon.Height; y++)
            {
                for (var x = 0; x < icon.Width; x++) { var a = icon.GetPixel(x, y).A; coverage += a / 255.0; peak = Math.Max(peak, a); if (a > 128) opaque++; }
            }

            Assert.True(coverage > size && peak > 128, $"the {size}px logo is nearly empty: opaque={opaque} coverage={coverage:0.0} peak={peak}");
        }

        for (var i = 0; i < 200; i++) icons.Get("codex", IconBackground.Dark, 20); // 100+ refreshes
        Assert.Equal(4, icons.CachedCount);
    }

    // ---------- the two small controls: keyboard and names ----------

    private static void Press(Control control, Keys key)
    {
        var method = typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(control, [new KeyEventArgs(key)]);
    }

    [Fact]
    public void The_logo_selector_steps_with_the_arrow_keys_and_opens_the_list_with_enter()
    {
        using var selector = new UsageLogoSelector();
        var steps = new List<int>();
        var opened = 0;
        selector.Step += (_, d) => steps.Add(d);
        selector.OpenList += (_, _) => opened++;

        Press(selector, Keys.Down);
        Press(selector, Keys.Right);
        Press(selector, Keys.Up);
        Press(selector, Keys.Left);
        Press(selector, Keys.Enter);
        Press(selector, Keys.Space);
        Press(selector, Keys.F4);
        Press(selector, Keys.A);

        Assert.Equal([1, 1, -1, -1], steps);
        Assert.Equal(3, opened);
        Assert.True(selector.TabStop);
    }

    [Fact]
    public void The_logo_selector_has_an_accessible_name_that_says_which_provider_and_what_to_press()
    {
        using var selector = new UsageLogoSelector();

        selector.SetState(null, "Codex", ProviderSeverity.Stale, otherWarning: true, "AI usage provider: Codex (1 of 2). 5h 42% used.");

        Assert.Contains("Codex (1 of 2)", selector.AccessibleName);
        Assert.Equal(AccessibleRole.ComboBox, selector.AccessibleRole);
    }

    [Fact]
    public void The_expand_button_names_what_it_will_do_and_toggles_by_keyboard()
    {
        using var toggle = new UsageExpandToggle();
        var toggled = 0;
        var scrolled = new List<int>();
        toggle.Toggled += (_, _) => toggled++;
        toggle.ScrollRequested += (_, d) => scrolled.Add(d);

        Assert.Equal("Expand AI usage", toggle.AccessibleName);
        Press(toggle, Keys.Enter);
        Press(toggle, Keys.Down); // not expanded: Up/Down do nothing
        toggle.Expanded = true;
        Press(toggle, Keys.Space);
        Press(toggle, Keys.Down);
        Press(toggle, Keys.Up);

        Assert.Equal("Collapse AI usage", toggle.AccessibleName);
        Assert.Equal(2, toggled);
        Assert.Equal([1, -1], scrolled);
        Assert.Equal(AccessibleRole.PushButton, toggle.AccessibleRole);
    }

    [Fact]
    public void The_provider_logos_are_packaged_with_a_credits_file_for_every_asset()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "PomoQuote.csproj"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        var credits = File.ReadAllText(Path.Combine(root!, "THIRD_PARTY_ASSETS.md"));

        foreach (var file in Directory.GetFiles(Path.Combine(root!, "Assets", "Providers"), "*.png"))
        {
            Assert.Contains(Path.GetFileName(file), credits);
        }

        Assert.Contains("not affiliated", credits, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trademark", credits, StringComparison.OrdinalIgnoreCase);
    }
}
