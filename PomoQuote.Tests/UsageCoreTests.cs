using System.Text.Json;
using PomoQuote.AiUsage;

namespace PomoQuote.Tests;

internal static class UsageTestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 24, 6, 0, 0, TimeSpan.Zero);

    public static UsageSource Source(string provider = "codex", string account = "abc123abc123", string plan = "plus") =>
        new(provider, UsageEnvironment.Windows, account, plan);

    public static QuotaWindow Window(QuotaKind kind, double percent, DateTimeOffset? resetsAt, bool real = true) =>
        new(kind, kind == QuotaKind.FiveHour ? 300 : kind == QuotaKind.Weekly ? 10080 : null, percent, resetsAt, "codex", real);

    public static UsageSnapshot Snapshot(double five, double weekly, DateTimeOffset? observed = null, UsageSource? source = null, DateTimeOffset? fiveReset = null, DateTimeOffset? weekReset = null) =>
        new(source ?? Source(),
            [Window(QuotaKind.FiveHour, five, fiveReset ?? Now.AddHours(3)), Window(QuotaKind.Weekly, weekly, weekReset ?? Now.AddDays(3))],
            observed ?? Now, UsageDataSource.CodexAppServer, UsageState.Ok);
}

public sealed class UsageModelTests
{
    [Fact]
    public void Time_left_is_computed_from_the_clock_and_is_null_when_unknown_or_past()
    {
        var window = UsageTestData.Window(QuotaKind.FiveHour, 10, UsageTestData.Now.AddMinutes(90));

        Assert.Equal(TimeSpan.FromMinutes(90), window.TimeLeft(UsageTestData.Now));
        Assert.Equal(TimeSpan.FromMinutes(30), window.TimeLeft(UsageTestData.Now.AddMinutes(60)));
        Assert.Null(window.TimeLeft(UsageTestData.Now.AddMinutes(91)));
        Assert.Null(UsageTestData.Window(QuotaKind.FiveHour, 10, null).TimeLeft(UsageTestData.Now));
    }

    [Theory]
    [InlineData(300, QuotaKind.FiveHour)]
    [InlineData(10080, QuotaKind.Weekly)]
    [InlineData(60, QuotaKind.Other)]
    [InlineData(null, QuotaKind.Other)]
    public void Kinds_come_from_the_window_length(int? minutes, QuotaKind expected) => Assert.Equal(expected, QuotaWindow.KindFor(minutes));

    [Fact]
    public void A_good_snapshot_becomes_stale_after_twice_the_interval()
    {
        var snapshot = UsageTestData.Snapshot(10, 10);
        var interval = TimeSpan.FromMinutes(3);

        Assert.Equal(UsageState.Ok, snapshot.EffectiveState(UsageTestData.Now.AddMinutes(6), interval));
        Assert.Equal(UsageState.Stale, snapshot.EffectiveState(UsageTestData.Now.AddMinutes(6).AddSeconds(1), interval));
        Assert.False(snapshot.IsStale(UsageTestData.Now.AddMinutes(5), interval));
        Assert.True(snapshot.IsStale(UsageTestData.Now.AddMinutes(7), interval));
    }

    [Fact]
    public void Other_states_are_not_turned_into_stale()
    {
        var failed = UsageSnapshot.Empty(UsageTestData.Source(), UsageTestData.Now, UsageDataSource.CodexAppServer, UsageState.NotLoggedIn, "x");

        Assert.Equal(UsageState.NotLoggedIn, failed.EffectiveState(UsageTestData.Now.AddHours(5), TimeSpan.FromMinutes(3)));
    }

    [Fact]
    public void The_source_key_names_provider_environment_and_account_only()
    {
        Assert.Equal("codex|Windows|abc", UsageTestData.Source(account: "abc", plan: "pro").Key);
    }
}

public sealed class UsageAlertEngineTests
{
    private static readonly int[] Levels = [80, 95];

    [Fact]
    public void A_threshold_fires_once_and_not_again_while_the_window_lasts()
    {
        var engine = new UsageAlertEngine();

        var (first, changed) = engine.Evaluate(UsageTestData.Snapshot(82, 10), Levels);
        var (second, changedAgain) = engine.Evaluate(UsageTestData.Snapshot(83, 10), Levels);

        var alert = Assert.Single(first);
        Assert.True(changed);
        Assert.Equal(80, alert.Threshold);
        Assert.Equal(QuotaKind.FiveHour, alert.Kind);
        Assert.Empty(second);
        Assert.False(changedAgain);
    }

    [Fact]
    public void Crossing_two_thresholds_at_once_gives_one_warning_for_the_highest_and_the_lower_one_stays_quiet()
    {
        var engine = new UsageAlertEngine();

        var (alerts, _) = engine.Evaluate(UsageTestData.Snapshot(96, 10), Levels);
        var (later, _) = engine.Evaluate(UsageTestData.Snapshot(97, 10), Levels);

        Assert.Equal(95, Assert.Single(alerts).Threshold);
        Assert.Empty(later);
    }

    [Fact]
    public void Going_from_the_lower_to_the_higher_threshold_fires_the_higher_one()
    {
        var engine = new UsageAlertEngine();

        engine.Evaluate(UsageTestData.Snapshot(81, 10), Levels);
        var (alerts, _) = engine.Evaluate(UsageTestData.Snapshot(96, 10), Levels);

        Assert.Equal(95, Assert.Single(alerts).Threshold);
    }

    [Fact]
    public void A_new_window_after_the_reset_rearms_the_warning()
    {
        var engine = new UsageAlertEngine();
        engine.Evaluate(UsageTestData.Snapshot(90, 10), Levels);

        var nextWindow = UsageTestData.Now.AddHours(5).AddMinutes(3);
        var (alerts, _) = engine.Evaluate(UsageTestData.Snapshot(85, 10, fiveReset: nextWindow), Levels);

        Assert.Equal(80, Assert.Single(alerts).Threshold);
    }

    [Fact]
    public void Dropping_below_the_threshold_rearms_it_in_the_same_window()
    {
        var engine = new UsageAlertEngine();
        engine.Evaluate(UsageTestData.Snapshot(90, 10), Levels);

        engine.Evaluate(UsageTestData.Snapshot(40, 10), Levels);
        var (alerts, _) = engine.Evaluate(UsageTestData.Snapshot(88, 10), Levels);

        Assert.Equal(80, Assert.Single(alerts).Threshold);
    }

    [Fact]
    public void Each_window_kind_and_each_account_has_its_own_warning()
    {
        var engine = new UsageAlertEngine();

        var (both, _) = engine.Evaluate(UsageTestData.Snapshot(85, 90), Levels);
        var (otherAccount, _) = engine.Evaluate(UsageTestData.Snapshot(85, 90, source: UsageTestData.Source(account: "other")), Levels);

        Assert.Equal([QuotaKind.FiveHour, QuotaKind.Weekly], both.Select(a => a.Kind).ToArray());
        Assert.Equal(2, otherAccount.Count);
    }

    [Fact]
    public void Windows_whose_reset_is_not_real_or_unknown_never_alert()
    {
        var engine = new UsageAlertEngine();
        var snapshot = new UsageSnapshot(UsageTestData.Source(),
            [UsageTestData.Window(QuotaKind.FiveHour, 99, UsageTestData.Now.AddHours(1), real: false), UsageTestData.Window(QuotaKind.Weekly, 99, null)],
            UsageTestData.Now, UsageDataSource.CodexAppServer, UsageState.Ok);

        var (alerts, changed) = engine.Evaluate(snapshot, Levels);

        Assert.Empty(alerts);
        Assert.False(changed);
    }

    [Fact]
    public void Failed_reads_never_alert()
    {
        var engine = new UsageAlertEngine();
        var failed = UsageSnapshot.Empty(UsageTestData.Source(), UsageTestData.Now, UsageDataSource.CodexAppServer, UsageState.Error, "x");

        Assert.Empty(engine.Evaluate(failed, Levels).Alerts);
    }

    [Fact]
    public void A_restart_with_the_saved_keys_does_not_repeat_a_warning()
    {
        var first = new UsageAlertEngine();
        first.Evaluate(UsageTestData.Snapshot(90, 10), Levels);

        var restarted = new UsageAlertEngine(first.FiredKeys.ToList());
        var (alerts, _) = restarted.Evaluate(UsageTestData.Snapshot(91, 10), Levels);

        Assert.Empty(alerts);
    }

    [Fact]
    public void Old_windows_are_forgotten_so_the_set_does_not_grow()
    {
        var engine = new UsageAlertEngine();
        for (var i = 0; i < 50; i++)
        {
            engine.Evaluate(UsageTestData.Snapshot(99, 10, fiveReset: UsageTestData.Now.AddHours(5 * (i + 1))), Levels);
        }

        Assert.True(engine.FiredKeys.Count <= 4, $"{engine.FiredKeys.Count} keys kept");
    }
}

public sealed class UsageStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PomoQuoteTests-" + Guid.NewGuid().ToString("N"));

    private string P(string name) => Path.Combine(_directory, "nested", name);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void The_cache_round_trips_the_last_good_snapshot_with_no_temp_file_left()
    {
        var store = new UsageCacheStore(P("usage-cache.json"));
        var snapshot = UsageTestData.Snapshot(21, 27);

        store.Save([snapshot]);

        var loaded = Assert.Single(store.Load());
        Assert.Equal(snapshot.Source, loaded.Source);
        Assert.Equal(snapshot.ObservedAt, loaded.ObservedAt);
        Assert.Equal(snapshot.Windows, loaded.Windows);
        Assert.False(File.Exists(P("usage-cache.json") + ".tmp"));
    }

    [Fact]
    public void The_cache_file_contains_only_whitelisted_keys_and_nothing_that_looks_like_an_id_or_token()
    {
        var store = new UsageCacheStore(P("usage-cache.json"));
        store.Save([UsageTestData.Snapshot(21, 27)]);

        using var doc = JsonDocument.Parse(File.ReadAllText(P("usage-cache.json")));
        var keys = new HashSet<string>();
        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) { keys.Add(p.Name); Walk(p.Value); }
            else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) Walk(x);
        }

        Walk(doc.RootElement);

        Assert.All(keys, key => Assert.Contains(key, UsageCacheStore.AllowedKeys));
        var text = File.ReadAllText(P("usage-cache.json"));
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", text);
    }

    [Fact]
    public void Snapshots_that_are_not_ok_are_not_cached()
    {
        var store = new UsageCacheStore(P("usage-cache.json"));
        store.Save([UsageSnapshot.Empty(UsageTestData.Source(), UsageTestData.Now, UsageDataSource.CodexAppServer, UsageState.Error, "x")]);

        Assert.Empty(store.Load());
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("null")]
    [InlineData("""{"entries":null}""")]
    [InlineData("""{"entries":[{"provider":"codex","environment":"Mars","windows":[]}]}""")]
    [InlineData("""{"entries":[{"provider":"","environment":"Windows","windows":[]}]}""")]
    public void A_damaged_cache_gives_nothing_instead_of_an_error(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(P("usage-cache.json"))!);
        File.WriteAllText(P("usage-cache.json"), content);

        Assert.Empty(new UsageCacheStore(P("usage-cache.json")).Load());
    }

    [Fact]
    public void Cached_windows_with_impossible_percentages_are_dropped()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(P("usage-cache.json"))!);
        File.WriteAllText(P("usage-cache.json"), """
            {"entries":[{"provider":"codex","environment":"Windows","accountKey":"a","planTier":"plus","observedAt":"2026-11-24T06:00:00+00:00","dataSource":"CodexAppServer","state":"Ok",
             "windows":[{"kind":"FiveHour","durationMinutes":300,"percentUsed":150,"isResetReal":true},{"kind":"Weekly","durationMinutes":10080,"percentUsed":12,"isResetReal":true}]}]}
            """);

        var snapshot = Assert.Single(new UsageCacheStore(P("usage-cache.json")).Load());

        Assert.Equal(QuotaKind.Weekly, Assert.Single(snapshot.Windows).Kind);
    }

    [Fact]
    public void Settings_default_to_everything_off_with_a_three_minute_interval()
    {
        var settings = new UsageSettingsStore(P("usage.json")).Load();

        Assert.Empty(settings.EnabledProviders);
        Assert.Equal(3, settings.IntervalMinutes);
        Assert.Equal(TimeSpan.FromMinutes(3), settings.Interval);
        Assert.Equal([80, 95], settings.Thresholds);
        Assert.True(settings.AlertsEnabled);
        Assert.Equal(UsageDisplayMode.Bars, settings.DisplayMode);
    }

    [Fact]
    public void Settings_round_trip_atomically_with_sorted_thresholds()
    {
        var store = new UsageSettingsStore(P("usage.json"));
        var saved = UsageSettings.Default.WithEnabled("codex", true) with { IntervalMinutes = 5, DisplayMode = UsageDisplayMode.Percent, AlertsEnabled = false, Thresholds = [95, 70] };

        store.Save(saved);

        var loaded = store.Load();
        Assert.Equal(["codex"], loaded.EnabledProviders);
        Assert.Equal(5, loaded.IntervalMinutes);
        Assert.Equal(UsageDisplayMode.Percent, loaded.DisplayMode);
        Assert.False(loaded.AlertsEnabled);
        Assert.Equal([70, 95], loaded.Thresholds);
        Assert.False(File.Exists(P("usage.json") + ".tmp"));
        Assert.Contains("\"Percent\"", File.ReadAllText(P("usage.json")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("""{"EnabledProviders":["codex"],"Order":["codex"],"IntervalMinutes":0,"DisplayMode":"Bars","AlertsEnabled":true,"Thresholds":[80]}""")]
    [InlineData("""{"EnabledProviders":["codex"],"Order":["codex"],"IntervalMinutes":3,"DisplayMode":"Sparkles","AlertsEnabled":true,"Thresholds":[80]}""")]
    [InlineData("""{"EnabledProviders":["codex"],"Order":["codex"],"IntervalMinutes":3,"DisplayMode":"Bars","AlertsEnabled":true,"Thresholds":[]}""")]
    [InlineData("""{"EnabledProviders":["codex"],"Order":["codex"],"IntervalMinutes":3,"DisplayMode":"Bars","AlertsEnabled":true,"Thresholds":[80,80]}""")]
    [InlineData("""{"EnabledProviders":["codex"],"Order":["codex"],"IntervalMinutes":3,"DisplayMode":"Bars","AlertsEnabled":true,"Thresholds":[500]}""")]
    public void Damaged_or_invalid_settings_fall_back_to_the_defaults(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(P("usage.json"))!);
        File.WriteAllText(P("usage.json"), content);

        var settings = new UsageSettingsStore(P("usage.json")).Load();

        Assert.Equal(UsageSettings.Default.Thresholds, settings.Thresholds);
        Assert.Empty(settings.EnabledProviders);
    }

    [Fact]
    public void Switching_a_provider_on_and_off_is_idempotent()
    {
        var on = UsageSettings.Default.WithEnabled("codex", true).WithEnabled("codex", true);
        var off = on.WithEnabled("codex", false);

        Assert.Equal(["codex"], on.EnabledProviders);
        Assert.Empty(off.EnabledProviders);
        Assert.True(on.IsEnabled("codex"));
        Assert.False(off.IsEnabled("codex"));
    }

    [Fact]
    public void Alert_keys_survive_a_reload_and_a_damaged_file_gives_none()
    {
        var store = new UsageAlertStore(P("usage-alerts.json"));
        store.Save(["b", "a"]);

        Assert.Equal(["a", "b"], store.Load());
        File.WriteAllText(P("usage-alerts.json"), "{{{");
        Assert.Empty(store.Load());
    }
}
