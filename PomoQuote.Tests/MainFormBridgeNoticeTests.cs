using System.Drawing;
using System.Windows.Forms;
using PomoQuote.AiUsage;
using PomoQuote.AiUsage.Claude;
using PomoQuote.Mascot;
using PomoQuote.Pomodoro;
using PomoQuote.Quotes;
using PomoQuote.Weather;
using PomoQuote.Widget;

namespace PomoQuote.Tests;

/// <summary>
/// The start-up notice goes through the real MainForm: the check runs when the window is shown (its handle exists then) and the
/// notice reaches the UI thread. Runs on throwaway settings and files; the real ~/.claude is never opened and no balloon is shown.
/// </summary>
[Collection("process")]
public sealed class MainFormBridgeNoticeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PomoQuoteForm-" + Guid.NewGuid().ToString("N"));

    public MainFormBridgeNoticeTests() => Directory.CreateDirectory(_root);

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

    /// <summary>A throwaway settings file holding an old-style bridge whose program does not exist.</summary>
    private ClaudeSetup BrokenBridge()
    {
        var claudeDirectory = Path.Combine(_root, "claude");
        Directory.CreateDirectory(claudeDirectory);
        var settings = Path.Combine(claudeDirectory, "settings.json");
        var command = "\"" + Path.Combine(_root, "gone", "PomoQuote.exe").Replace('\\', '/') + "\" --bridge claude --chain-b64 " + ClaudeBridge.EncodeChain("node x.js");
        File.WriteAllText(settings, "{\n  \"statusLine\": { \"type\": \"command\", \"command\": " + System.Text.Json.JsonSerializer.Serialize(command) + " }\n}\n");
        var store = new ClaudeSnapshotStore(Path.Combine(_root, "usage"));
        return new ClaudeSetup(
            new ClaudeBridgeInstaller(settings, Path.Combine(_root, "state"), () => Path.Combine(_root, "app", "PomoQuote.exe"), () => @"C:\Git\bin\bash.exe"),
            new ClaudeRefresher(new ClaudeProcessFactory(), store, TimeProvider.System),
            store);
    }

    private MainForm NewForm(ClaudeSetup setup)
    {
        var http = new HttpClient(new NeverAnswers()) { Timeout = TimeSpan.FromMinutes(10) };
        return new MainForm(
            new WeatherService(http), new SettingsStore(Path.Combine(_root, "settings.json")), new PomodoroStore(Path.Combine(_root, "pomodoro.json")),
            TimeProvider.System, new WidgetStore(Path.Combine(_root, "widget.json")), new MascotStore(Path.Combine(_root, "mascot.json")),
            new QuoteSettingsStore(Path.Combine(_root, "quotes.json")), new BundledQuoteProvider(), new Random(1),
            new UsageSettingsStore(Path.Combine(_root, "usage.json")), new UsageCacheStore(Path.Combine(_root, "usage-cache.json")), new UsageAlertStore(Path.Combine(_root, "usage-alerts.json")),
            [], setup);
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

    private static bool PumpUntil(Func<bool> done, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until)
        {
            Application.DoEvents();
            if (done()) return true;
            Thread.Sleep(20);
        }

        return done();
    }

    [Fact]
    public void The_notice_is_delivered_once_the_window_is_shown()
    {
        OnStaThread(() =>
        {
            var notices = new List<(string Title, string Message)>();
            using var form = NewForm(BrokenBridge());
            form.BridgeNoticeSink = (title, message) => notices.Add((title, message));
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-3000, -3000); // off screen: nothing flashes on the developer's desktop

            form.Show(); // OnShown starts the check

            Assert.True(PumpUntil(() => notices.Count > 0, TimeSpan.FromSeconds(20)), "no notice reached the UI thread");
            var notice = Assert.Single(notices);
            Assert.Contains("old-style bridge has no fail-safe", notice.Message);
            Assert.Contains("shows no status line now", notice.Message);
            form.Close();
        });
    }

    [Fact]
    public void A_check_started_before_the_handle_exists_neither_throws_nor_shows_anything()
    {
        OnStaThread(() =>
        {
            var notices = new List<string>();
            using var form = NewForm(BrokenBridge());
            form.BridgeNoticeSink = (_, message) => notices.Add(message);
            Assert.False(form.IsHandleCreated);

            var task = form.StartBridgeCheck(); // the old code threw InvalidOperationException here, inside an unobserved task

            Assert.True(task.Wait(TimeSpan.FromSeconds(20)));
            Assert.False(task.IsFaulted);
            PumpUntil(() => false, TimeSpan.FromMilliseconds(300));
            Assert.Empty(notices);
        });
    }
}
