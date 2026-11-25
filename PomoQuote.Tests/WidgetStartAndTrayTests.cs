using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using PomoQuote.AiUsage;
using PomoQuote.Mascot;
using PomoQuote.Pomodoro;
using PomoQuote.Quotes;
using PomoQuote.Weather;
using PomoQuote.Widget;

namespace PomoQuote.Tests;

/// <summary>The start mode, the hide-to-tray state machine, the second-launch signal (all pure or on throwaway files).</summary>
public sealed class StartModeAndShellStateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PomoQuoteStart-" + Guid.NewGuid().ToString("N"));

    public StartModeAndShellStateTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData(StartMode.Widget, false, true)]
    [InlineData(StartMode.Widget, true, true)]
    [InlineData(StartMode.Normal, false, false)]
    [InlineData(StartMode.Normal, true, false)]
    [InlineData(StartMode.Remember, false, false)]
    [InlineData(StartMode.Remember, true, true)]
    public void The_start_mode_decides_the_window_the_app_opens_with(StartMode startIn, bool lastWasWidget, bool expectedWidget)
    {
        Assert.Equal(expectedWidget, StartModePolicy.StartsAsWidget(startIn, lastWasWidget));
    }

    [Fact]
    public void A_fresh_profile_and_an_old_file_without_the_setting_start_as_the_widget()
    {
        Assert.Equal(StartMode.Widget, new WidgetStore(Path.Combine(_root, "none.json")).Load().StartIn);

        // A file from before the setting existed (it has only the last mode and the position): the new default applies.
        var path = Path.Combine(_root, "widget.json");
        File.WriteAllText(path, "{\"Enabled\": false, \"X\": 10, \"Y\": 20, \"OpacityPercent\": 80}");
        var old = new WidgetStore(path).Load();
        Assert.Equal(StartMode.Widget, old.StartIn);
        Assert.Equal(80, old.OpacityPercent);
        Assert.True(StartModePolicy.StartsAsWidget(old.StartIn, old.Enabled));
    }

    [Theory]
    [InlineData(StartMode.Widget)]
    [InlineData(StartMode.Normal)]
    [InlineData(StartMode.Remember)]
    public void Each_choice_is_saved_atomically_and_read_back(StartMode mode)
    {
        var path = Path.Combine(_root, "sub", "widget.json");
        var store = new WidgetStore(path);

        store.Save(new WidgetSettings(Enabled: true, X: 5, Y: 6, OpacityPercent: 60, StartIn: mode));

        var loaded = new WidgetStore(path).Load();
        Assert.Equal(mode, loaded.StartIn);
        Assert.Equal(new Point(5, 6), loaded.Position);
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Contains($"\"{mode}\"", File.ReadAllText(path)); // a readable name, not a number
    }

    [Theory]
    [InlineData("not json {")]
    [InlineData("")]
    [InlineData("{\"StartIn\": \"Sideways\"}")]
    [InlineData("{\"StartIn\": 7}")]
    public void A_damaged_file_or_an_unknown_choice_falls_back_to_the_widget(string content)
    {
        var path = Path.Combine(_root, "widget.json");
        File.WriteAllText(path, content);

        Assert.Equal(StartMode.Widget, new WidgetStore(path).Load().StartIn);
    }

    [Fact]
    public void Remember_last_uses_the_mode_the_window_was_left_in()
    {
        var left = new WidgetSettings(Enabled: false, StartIn: StartMode.Remember);
        var inWidget = left with { Enabled = true };

        Assert.False(StartModePolicy.StartsAsWidget(left.StartIn, left.Enabled));
        Assert.True(StartModePolicy.StartsAsWidget(inWidget.StartIn, inWidget.Enabled));
    }

    [Theory]
    [InlineData(ShellState.WidgetVisible, ShellAction.HideToTray, ShellState.WidgetHidden)]
    [InlineData(ShellState.WidgetVisible, ShellAction.TrayClick, ShellState.WidgetHidden)]
    [InlineData(ShellState.WidgetHidden, ShellAction.TrayClick, ShellState.WidgetVisible)]
    [InlineData(ShellState.WidgetHidden, ShellAction.TrayDoubleClick, ShellState.WidgetVisible)]
    [InlineData(ShellState.WidgetVisible, ShellAction.TrayDoubleClick, ShellState.WidgetVisible)]
    [InlineData(ShellState.WidgetHidden, ShellAction.ShowWidget, ShellState.WidgetVisible)]
    [InlineData(ShellState.WidgetHidden, ShellAction.SecondLaunch, ShellState.WidgetVisible)]
    [InlineData(ShellState.WidgetVisible, ShellAction.SecondLaunch, ShellState.WidgetVisible)]
    [InlineData(ShellState.WidgetVisible, ShellAction.LeaveWidgetMode, ShellState.NormalWindow)]
    [InlineData(ShellState.WidgetHidden, ShellAction.LeaveWidgetMode, ShellState.NormalWindow)]
    [InlineData(ShellState.NormalWindow, ShellAction.EnterWidgetMode, ShellState.WidgetVisible)]
    [InlineData(ShellState.WidgetHidden, ShellAction.EnterWidgetMode, ShellState.WidgetVisible)]
    [InlineData(ShellState.NormalWindow, ShellAction.HideToTray, ShellState.NormalWindow)] // no widget to hide
    [InlineData(ShellState.NormalWindow, ShellAction.TrayClick, ShellState.NormalWindow)]
    [InlineData(ShellState.NormalWindow, ShellAction.SecondLaunch, ShellState.NormalWindow)]
    [InlineData(ShellState.WidgetVisible, ShellAction.EnterWidgetMode, ShellState.WidgetVisible)]
    public void The_shell_state_machine(ShellState from, ShellAction action, ShellState expected)
    {
        Assert.Equal(expected, ShellStateMachine.Next(from, action));
    }

    [Fact]
    public void Every_state_and_action_has_a_defined_result()
    {
        foreach (var state in Enum.GetValues<ShellState>())
        {
            foreach (var action in Enum.GetValues<ShellAction>()) Assert.True(Enum.IsDefined(ShellStateMachine.Next(state, action)));
        }
    }

    [Fact]
    public void A_second_launch_signal_reaches_the_first_instance_and_is_false_without_one()
    {
        var name = @"Local\PomoQuote.Test." + Guid.NewGuid().ToString("N");
        Assert.False(SingleInstanceSignal.Notify(name)); // nothing listening

        using var received = new ManualResetEventSlim();
        using (new SingleInstanceSignal(name, received.Set))
        {
            Assert.True(SingleInstanceSignal.Notify(name));
            Assert.True(received.Wait(TimeSpan.FromSeconds(5)), "the first instance was not told");
        }

        // The listener is gone. Dispose waits for the unregistration, so this is false at once; polling briefly keeps the test calm on a loaded machine.
        var gone = false;
        for (var i = 0; i < 40 && !gone; i++)
        {
            gone = !SingleInstanceSignal.Notify(name);
            if (!gone) Thread.Sleep(50);
        }

        Assert.True(gone, "the named event still existed after the listener was disposed");
    }

    [Fact]
    public void The_program_signals_the_first_instance_when_a_second_one_starts()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "Program.cs"));

        Assert.Contains("SingleInstanceSignal.Notify", source);
        Assert.Contains("new SingleInstanceSignal(", source);
        Assert.Contains("ShowFromSecondLaunch", source);
    }

    private static string RepositoryRoot()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "PomoQuote.csproj"))) root = Path.GetDirectoryName(root);
        return root!;
    }

    [Fact]
    public void The_json_of_the_start_mode_is_a_string()
    {
        var text = JsonSerializer.Serialize(new WidgetSettings(StartIn: StartMode.Remember));

        Assert.Contains("\"StartIn\":\"Remember\"", text);
    }
}

/// <summary>
/// The real window: it starts in the chosen mode, hides to the tray and comes back, keeps its Pomodoro, weather and usage work while hidden,
/// stops its effect timer, restores its taskbar button after widget mode (issue 30), and does not leak over 100 mode switches and 100 tray toggles.
/// Runs on throwaway settings in an off-screen window; nothing touches the real files or the running app.
/// </summary>
[Collection("process")]
public sealed class WidgetTrayFormTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PomoQuoteTray-" + Guid.NewGuid().ToString("N"));

    public WidgetTrayFormTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);

    private sealed class NeverAnswers : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        }
    }

    private MainForm NewForm(WidgetSettings? widget = null, TimeProvider? clock = null)
    {
        if (widget is not null) new WidgetStore(Path.Combine(_root, "widget.json")).Save(widget);
        var http = new HttpClient(new NeverAnswers()) { Timeout = TimeSpan.FromMinutes(10) };
        return new MainForm(
            new WeatherService(http), new SettingsStore(Path.Combine(_root, "settings.json")), new PomodoroStore(Path.Combine(_root, "pomodoro.json")),
            clock ?? TimeProvider.System, new WidgetStore(Path.Combine(_root, "widget.json")), new MascotStore(Path.Combine(_root, "mascot.json")),
            new QuoteSettingsStore(Path.Combine(_root, "quotes.json")), new BundledQuoteProvider(), new Random(1),
            new UsageSettingsStore(Path.Combine(_root, "usage.json")), new UsageCacheStore(Path.Combine(_root, "usage-cache.json")), new UsageAlertStore(Path.Combine(_root, "usage-alerts.json")),
            [], null, new WeatherEffectSettingsStore(Path.Combine(_root, "weather-effects.json")))
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-3000, -3000),
            WorkAreaProvider = () => [new Rectangle(-6000, -6000, 4000, 4000)], // the window stays off screen
        };
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(180)), "the form test did not finish");
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

    private static MainForm Shown(MainForm form)
    {
        form.Show();
        form.WidgetViewForTests.OcclusionProbe = _ => false;
        form.WidgetViewForTests.SystemAnimationsProbe = () => true;
        Pump(TimeSpan.FromMilliseconds(150));
        return form;
    }

    // ---------- the start mode ----------

    [Fact]
    public void A_fresh_profile_starts_as_the_widget()
    {
        OnStaThread(() =>
        {
            using var form = Shown(NewForm());

            Assert.True(form.IsWidgetMode);
            Assert.Equal(ShellState.WidgetVisible, form.ShellState);
            Assert.Equal(StartMode.Widget, form.StartIn);
            Assert.True(form.TrayIconVisible, "the tray icon is always there in widget mode");
        });
    }

    [Theory]
    [InlineData(StartMode.Widget, false, true)]
    [InlineData(StartMode.Widget, true, true)]
    [InlineData(StartMode.Normal, true, false)]
    [InlineData(StartMode.Normal, false, false)]
    [InlineData(StartMode.Remember, true, true)]
    [InlineData(StartMode.Remember, false, false)]
    public void Each_start_choice_opens_the_right_window(StartMode startIn, bool lastWasWidget, bool expectWidget)
    {
        OnStaThread(() =>
        {
            using var form = Shown(NewForm(new WidgetSettings(Enabled: lastWasWidget, StartIn: startIn)));

            Assert.Equal(expectWidget, form.IsWidgetMode);
            Assert.Equal(expectWidget ? ShellState.WidgetVisible : ShellState.NormalWindow, form.ShellState);
        });
    }

    [Fact]
    public void Choosing_Start_in_is_saved_and_the_menu_follows_it_and_Remember_restores_the_last_mode()
    {
        OnStaThread(() =>
        {
            using (var form = Shown(NewForm()))
            {
                form.SetStartMode(StartMode.Remember);
                form.SetWidgetMode(false); // the last mode used is now the normal window
            }

            var saved = new WidgetStore(Path.Combine(_root, "widget.json")).Load();
            Assert.Equal(StartMode.Remember, saved.StartIn);
            Assert.False(saved.Enabled);

            using var next = Shown(NewForm());
            Assert.False(next.IsWidgetMode); // Remember last: the normal window
            next.SetWidgetMode(true);
        });
        OnStaThread(() =>
        {
            using var again = Shown(NewForm());
            Assert.True(again.IsWidgetMode); // and now the widget was the last mode
        });
    }

    // ---------- hide to the tray ----------

    private static ToolStripItem Item(ContextMenuStrip menu, string text) =>
        menu.Items.Cast<ToolStripItem>().First(i => i.Text.Replace("&", "") == text);

    [Fact]
    public void Hide_to_tray_from_the_menu_and_the_shortcut_and_show_from_the_tray()
    {
        OnStaThread(() =>
        {
            using var form = Shown(NewForm());
            var hide = (ToolStripMenuItem)Item(form.WidgetMenuForTests, "Hide to tray");
            Assert.Equal("Ctrl+H", hide.ShortcutKeyDisplayString);
            Assert.Equal(Keys.Control | Keys.H, MainForm.HideToTrayShortcut);

            hide.PerformClick();
            Assert.Equal(ShellState.WidgetHidden, form.ShellState);
            Assert.False(form.Visible);
            Assert.True(form.TrayIconVisible);

            form.OnTrayClicked(); // a click shows it
            Assert.Equal(ShellState.WidgetVisible, form.ShellState);
            Assert.True(form.Visible);

            Assert.True(form.HandleShortcut(Keys.Control | Keys.H)); // the shortcut hides it
            Assert.Equal(ShellState.WidgetHidden, form.ShellState);
            form.OnTrayClicked();
            Assert.Equal(ShellState.WidgetVisible, form.ShellState);

            form.OnTrayClicked(); // hidden again by a click
            form.OnTrayDoubleClicked(); // a double click always shows
            Assert.Equal(ShellState.WidgetVisible, form.ShellState);
            Assert.True(form.Visible);
        });
    }

    [Fact]
    public void The_shortcut_does_nothing_in_the_normal_window()
    {
        OnStaThread(() =>
        {
            using var form = Shown(NewForm(new WidgetSettings(StartIn: StartMode.Normal)));

            Assert.False(form.HandleShortcut(Keys.Control | Keys.H));
            form.HideWidgetToTray();

            Assert.Equal(ShellState.NormalWindow, form.ShellState);
            Assert.True(form.Visible);
            Assert.False(form.TrayIconVisible); // the normal window shows its tray icon only while the mascot has it
        });
    }

    [Fact]
    public void While_hidden_the_effect_timer_and_the_clock_stop_and_come_back_on_their_own()
    {
        OnStaThread(() =>
        {
            using var form = Shown(NewForm());
            var view = form.WidgetViewForTests;
            form.WeatherPanelForTests.ShowReport(new WeatherReport(20, 70, 5, 63, IsDay: false), DateTime.Now);
            form.ApplyWeatherEffectSettings(new WeatherEffectSettings(true, WeatherEffectIntensity.Normal, false));
            Pump(TimeSpan.FromMilliseconds(400));
            Assert.True(view.EffectTimerRunning);

            form.HideWidgetToTray();
            Pump(TimeSpan.FromMilliseconds(400)); // no manual re-evaluation: the window being hidden is noticed by the widget itself
            Assert.False(view.EffectTimerRunning, "the effect timer ran while the widget was hidden in the tray");
            var ticks = view.EffectTicks;
            Pump(TimeSpan.FromSeconds(1.5));
            Assert.Equal(ticks, view.EffectTicks);

            form.ShowWidgetFromTray();
            Pump(TimeSpan.FromMilliseconds(800));
            Assert.True(view.EffectTimerRunning);
            Assert.True(view.EffectTicks > ticks);
        });
    }

    [Fact]
    public void While_hidden_the_pomodoro_weather_and_alerts_keep_working()
    {
        OnStaThread(() =>
        {
            var clock = new FakeClock();
            using var form = Shown(NewForm(clock: clock));
            form.PomodoroForTests.Timer.Start();
            form.HideWidgetToTray();
            Assert.Equal(ShellState.WidgetHidden, form.ShellState);

            clock.Advance(TimeSpan.FromMinutes(26)); // the work phase is over while nothing is on screen
            Pump(TimeSpan.FromSeconds(2));

            Assert.Equal(1, form.PomodoroForTests.CompletedToday); // the phase end was handled: count, sound and balloon come from the same handler
            Assert.True(form.TrayIconVisible);

            var weatherTimer = (System.Windows.Forms.Timer)typeof(MainForm).GetField("_weatherTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(form)!;
            Assert.True(weatherTimer.Enabled, "the weather refresh timer must keep running while hidden");
        });
    }

    [Fact]
    public void A_phase_end_while_hidden_requests_the_balloon_and_the_sound_and_the_widget_stays_hidden()
    {
        OnStaThread(() =>
        {
            var clock = new FakeClock();
            using var form = Shown(NewForm(clock: clock));
            var pomodoro = form.PomodoroForTests;
            var balloons = new List<(string Title, string Message)>();
            var sounds = new List<bool>();
            pomodoro.BalloonObserver = (title, message) => balloons.Add((title, message));
            pomodoro.SoundSink = workEnd => sounds.Add(workEnd); // reported instead of played: the test is silent
            pomodoro.ApplySettings(new PomodoroSettings(25, 5, 15, 4, SoundEnabled: true, AutoStartNext: false));
            pomodoro.Timer.Start();
            form.HideWidgetToTray();

            clock.Advance(TimeSpan.FromMinutes(26));
            Pump(TimeSpan.FromSeconds(2));

            var balloon = Assert.Single(balloons);
            Assert.Equal("Pomodoro", balloon.Title);
            Assert.StartsWith("Work session complete", balloon.Message);
            Assert.Equal([true], sounds); // the work-end sound was requested once
            Assert.Equal(ShellState.WidgetHidden, form.ShellState); // and nothing brought the widget back
            Assert.False(form.Visible);
            Assert.True(form.TrayIconVisible);
        });
    }
    [Fact]
    public void Restoring_keeps_the_position_and_the_opacity_and_moves_the_widget_back_onto_a_screen()
    {
        OnStaThread(() =>
        {
            using var form = Shown(NewForm(new WidgetSettings(Enabled: true, X: -5200, Y: -5300, OpacityPercent: 80)));
            Assert.Equal(0.8, form.Opacity, 2);
            var place = form.Location;

            form.HideWidgetToTray();
            form.ShowWidgetFromTray();
            Assert.Equal(place, form.Location);
            Assert.Equal(0.8, form.Opacity, 2);

            // The monitor it was on is gone: the work area is somewhere else when it comes back.
            form.HideWidgetToTray();
            form.WorkAreaProvider = () => [new Rectangle(-3500, -3500, 1000, 800)];
            form.ShowWidgetFromTray();
            var area = new Rectangle(-3500, -3500, 1000, 800);
            Assert.True(area.Contains(form.Bounds.Location), $"restored at {form.Location}, outside the visible area");
            Assert.Equal(0.8, form.Opacity, 2);
        });
    }

    [Fact]
    public void A_second_launch_shows_the_hidden_widget()
    {
        OnStaThread(() =>
        {
            using var form = Shown(NewForm());
            form.HideWidgetToTray();
            Assert.False(form.Visible);

            form.ShowFromSecondLaunch();
            Pump(TimeSpan.FromMilliseconds(400));

            Assert.True(form.Visible);
            Assert.Equal(ShellState.WidgetVisible, form.ShellState);
        });
    }

    [Fact]
    public void The_tray_menu_has_the_widget_items_and_the_exit()
    {
        OnStaThread(() =>
        {
            using var form = Shown(NewForm());
            var menu = form.TrayMenuForTests;
            var texts = menu.Items.Cast<ToolStripItem>().Select(i => i.Text.Replace("&", "")).ToList();

            Assert.Contains("Hide widget", texts);
            Assert.Contains("Open", texts);
            Assert.Contains("Settings", texts);
            Assert.Contains("Exit", texts);
            var settings = (ToolStripMenuItem)Item(menu, "Settings");
            Assert.Contains(settings.DropDownItems.Cast<ToolStripItem>(), i => i.Text.Replace("&", "") == "Start in");
            var widgetMenuSettings = (ToolStripMenuItem)Item(form.WidgetMenuForTests, "Settings");
            Assert.Contains(widgetMenuSettings.DropDownItems.Cast<ToolStripItem>(), i => i.Text.Replace("&", "") == "Start in");
        });
    }

    // ---------- issue 30: the normal window after widget mode ----------

    // ---------- the real taskbar (UI Automation) ----------

    /// <summary>
    /// How many taskbar buttons of this process the real taskbar shows right now (one per taskbar, so two on two monitors), read with UI Automation
    /// from a Windows PowerShell child; -1 when it cannot be read (no taskbar, no Windows PowerShell). The button is named after the program.
    /// </summary>
    private static int TaskbarButtons()
    {
        var program = Process.GetCurrentProcess().ProcessName;
        var script = "Add-Type -AssemblyName UIAutomationClient; Add-Type -AssemblyName UIAutomationTypes; "
            + "$A = [System.Windows.Automation.AutomationElement]; $root = $A::RootElement; $bars = @(); "
            + "foreach ($cls in 'Shell_TrayWnd', 'Shell_SecondaryTrayWnd') { $c = New-Object System.Windows.Automation.PropertyCondition($A::ClassNameProperty, $cls); foreach ($e in $root.FindAll([System.Windows.Automation.TreeScope]::Children, $c)) { $bars += $e } }; "
            + "$bc = New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button); "
            + "$n = 0; foreach ($b in $bars) { foreach ($e in $b.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bc)) { if ($e.Current.Name -like '" + program + " -*') { $n++ } } }; $n";        var file = Path.Combine(Path.GetTempPath(), "pomoquote-taskbar-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(file, script);
        try
        {
            var info = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{file}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(info);
            if (process is null) return -1;
            var output = new System.Text.StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            var limit = DateTime.UtcNow.AddSeconds(30);
            while (!process.HasExited && DateTime.UtcNow < limit) Pump(TimeSpan.FromMilliseconds(30)); // the window keeps answering Windows while the child looks at the taskbar
            if (!process.HasExited) { process.Kill(); return -1; }
            process.WaitForExit();
            return int.TryParse(output.ToString().Trim(), out var count) ? count : -1;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return -1;
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    /// <summary>Waits for the taskbar to catch up, then checks: no button (expectButton false) or at least one.</summary>
    private static void AssertTaskbar(bool expectButton, string when)
    {
        var count = -2;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            Pump(TimeSpan.FromMilliseconds(500));
            count = TaskbarButtons();
            if (count < 0) return; // the taskbar cannot be read here (no desktop session): the style-flag test below still runs
            if ((count > 0) == expectButton) return;
        }

        Assert.Fail($"{when}: the real taskbar shows {count} button(s) of this program, expected {(expectButton ? "one or more" : "none")}");
    }

    [Fact]
    public void The_real_taskbar_shows_a_button_for_the_normal_window_and_never_for_the_widget()
    {
        OnStaThread(() =>
        {
            using var form = NewForm(new WidgetSettings(Enabled: true, StartIn: StartMode.Remember));
            form.Show(); // created the way the program creates it: started in widget mode
            form.WidgetViewForTests.OcclusionProbe = _ => false;
            Pump(TimeSpan.FromMilliseconds(300));
            AssertTaskbar(false, "fresh start as the widget");

            form.SetWidgetMode(false);
            Pump(TimeSpan.FromMilliseconds(300));
            AssertNormalWindowPresence(form);
            AssertTaskbar(true, "after leaving widget mode (issue 30)");

            form.SetWidgetMode(true);
            AssertTaskbar(false, "after a Ctrl+W round trip back to the widget");

            form.HideWidgetToTray();
            AssertTaskbar(false, "hidden in the tray");
            form.OnTrayClicked();
            AssertTaskbar(false, "shown again by a tray click");

            form.HideWidgetToTray();
            form.ShowFromSecondLaunch();
            Pump(TimeSpan.FromMilliseconds(300));
            Assert.True(form.Visible);
            AssertTaskbar(false, "shown again by a second launch");

            for (var i = 0; i < 3; i++)
            {
                form.SetWidgetMode(false);
                AssertTaskbar(true, $"normal window, round {i + 1}");
                form.SetWidgetMode(true);
                form.HideWidgetToTray();
                form.ShowWidgetFromTray();
                AssertTaskbar(false, $"widget again, round {i + 1}");
            }
        });
    }

    private static readonly int WS_EX_APPWINDOW = 0x40000;

    /// <summary>The window style of a button-less window: not an app window and owned by the hidden parking window (Windows never gives such a window a taskbar button).</summary>
    private static bool IsButtonlessStyle(Form form)
    {
        const int GWL_EXSTYLE = -20;
        const uint GW_OWNER = 4;
        return !form.ShowInTaskbar && (GetWindowLong(form.Handle, GWL_EXSTYLE) & WS_EX_APPWINDOW) == 0 && GetWindow(form.Handle, GW_OWNER) != IntPtr.Zero;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint command);

    [Fact]
    public void The_visible_widget_has_the_button_less_window_style_in_every_path_and_the_normal_window_does_not()
    {
        OnStaThread(() =>
        {
            using var form = NewForm(new WidgetSettings(Enabled: true, StartIn: StartMode.Remember));
            form.Show();
            Pump(TimeSpan.FromMilliseconds(200));
            Assert.True(IsButtonlessStyle(form), "fresh start as the widget");

            form.HideWidgetToTray();
            form.ShowWidgetFromTray();
            Assert.True(IsButtonlessStyle(form), "after a tray show");

            form.HideWidgetToTray();
            form.ShowFromSecondLaunch();
            Pump(TimeSpan.FromMilliseconds(200));
            Assert.True(IsButtonlessStyle(form), "after a second launch");

            form.SetWidgetMode(false);
            Pump(TimeSpan.FromMilliseconds(200));
            Assert.False(IsButtonlessStyle(form), "the normal window must have its button");
            AssertNormalWindowPresence(form);

            form.SetWidgetMode(true);
            Assert.True(IsButtonlessStyle(form), "after a Ctrl+W round trip");
            form.HideWidgetToTray();
            form.ShowWidgetFromTray();
            Assert.True(IsButtonlessStyle(form), "after a round trip and a hide and show");
        });
    }
    private static void AssertNormalWindowPresence(MainForm form)
    {
        const int WS_EX_TOOLWINDOW = 0x80;
        const int GWL_EXSTYLE = -20;
        const int WM_GETICON = 0x7F;
        Assert.True(form.ShowInTaskbar);
        Assert.True(form.ShowIcon);
        Assert.NotNull(form.Icon);
        Assert.Equal(FormBorderStyle.Sizable, form.FormBorderStyle);
        Assert.True(form.Visible);
        Assert.Equal(0, GetWindowLong(form.Handle, GWL_EXSTYLE) & WS_EX_TOOLWINDOW); // a tool window has no taskbar button
        Assert.NotEqual(IntPtr.Zero, SendMessage(form.Handle, WM_GETICON, (IntPtr)0, IntPtr.Zero)); // the title-bar logo (ICON_SMALL)
        Assert.NotEqual(IntPtr.Zero, SendMessage(form.Handle, WM_GETICON, (IntPtr)1, IntPtr.Zero)); // the Alt+Tab and taskbar logo (ICON_BIG)
    }

    [Fact]
    public void Switching_modes_and_hiding_to_the_tray_repeatedly_keeps_everything_working()
    {
        OnStaThread(() =>
        {
            using var form = Shown(NewForm());
            for (var i = 0; i < 6; i++)
            {
                form.SetWidgetMode(false);
                Pump(TimeSpan.FromMilliseconds(50));
                AssertNormalWindowPresence(form);
                form.SetWidgetMode(true);
                form.HideWidgetToTray();
                Assert.False(form.Visible);
                form.ShowWidgetFromTray();
                Assert.True(form.Visible);
            }

            form.SetWidgetMode(false); // hidden in the tray, then "Normal window" from the tray menu
            form.SetWidgetMode(true);
            form.HideWidgetToTray();
            form.SetWidgetMode(false);
            Pump(TimeSpan.FromMilliseconds(100));
            Assert.True(form.Visible);
            AssertNormalWindowPresence(form);
        });
    }

    // ---------- the leak check ----------

    [Fact]
    public void A_hundred_mode_switches_and_a_hundred_tray_toggles_do_not_leak()
    {
        OnStaThread(() =>
        {
            using var form = Shown(NewForm());
            var process = Process.GetCurrentProcess();

            void Cycle()
            {
                form.SetWidgetMode(false);
                form.SetWidgetMode(true);
                form.HideWidgetToTray();
                form.ShowWidgetFromTray();
            }

            // The counters are process-wide: finalizers and timers of earlier tests can still move them for a moment. A reading is taken once it has stopped changing,
            // and a leak is growth that is still there after the same settling, so the bounds mean what they always meant.
            (int Handles, uint Gdi, uint User, long Managed) Settled()
            {
                (int, uint, uint, long) last = (-1, 0, 0, 0);
                for (var i = 0; i < 20; i++)
                {
                    Pump(TimeSpan.FromMilliseconds(150));
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    process.Refresh();
                    var now = (process.HandleCount, GetGuiResources(process.Handle, 0), GetGuiResources(process.Handle, 1), GC.GetTotalMemory(true) / (512 * 1024));
                    if (now == last) break;
                    last = now;
                }

                return (last.Item1, last.Item2, last.Item3, last.Item4 * 512 * 1024);
            }

            for (var i = 0; i < 10; i++) Cycle(); // warm-up
            var (handles, gdi, user, managed) = Settled();

            for (var i = 0; i < 100; i++)
            {
                form.SetWidgetMode(false); // 100 mode switches (each a pair)
                form.SetWidgetMode(true);
                if (i % 10 == 0) Pump(TimeSpan.FromMilliseconds(20));
            }

            for (var i = 0; i < 100; i++)
            {
                form.HideWidgetToTray(); // and 100 tray toggles
                form.ShowWidgetFromTray();
                if (i % 10 == 0) Pump(TimeSpan.FromMilliseconds(20));
            }

            var (handlesAfter, gdiAfter, userAfter, managedAfter) = Settled();

            Assert.True(handlesAfter - handles <= 60, $"handles {handles} -> {handlesAfter}");
            Assert.True((long)gdiAfter - gdi <= 12, $"GDI objects {gdi} -> {gdiAfter}");
            Assert.True((long)userAfter - user <= 12, $"USER objects {user} -> {userAfter}");
            Assert.True(managedAfter - managed <= 4 * 1024 * 1024, $"managed memory {managed} -> {managedAfter}");
            Assert.Equal(ShellState.WidgetVisible, form.ShellState);
        });
    }
}
