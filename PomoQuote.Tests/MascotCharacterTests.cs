using System.Drawing;
using System.Windows.Forms;
using PomoQuote.Mascot;
using PomoQuote.Pomodoro;
using PomoQuote.Quotes;
using PomoQuote.Weather;
using PomoQuote.Widget;

namespace PomoQuote.Tests;

/// <summary>The character registry, the sheets, their credits and the saved choice.</summary>
public sealed class MascotCharacterTests : IDisposable
{
    private static readonly string AssetsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Mascot");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PomoQuoteMascotChar-" + Guid.NewGuid().ToString("N"));

    public MascotCharacterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private static string RepoRoot() => AppContext.BaseDirectory.Split("PomoQuote.Tests")[0];

    // ---------- the registry ----------

    [Fact]
    public void There_are_four_characters_the_robot_first_each_with_its_own_id_name_and_sheet()
    {
        Assert.Equal(["robot", "animal", "man", "woman"], MascotCharacters.All.Select(c => c.Id));
        Assert.Equal(["Robot", "Animal", "Man", "Woman"], MascotCharacters.All.Select(c => c.Name));
        Assert.Equal(4, MascotCharacters.All.Select(c => c.SheetFile).Distinct().Count());
        Assert.Equal("robot", MascotCharacters.Default.Id);
        Assert.Equal("robot-sprites.png", MascotCharacters.Default.SheetFile); // the existing robot file keeps its name
    }

    [Theory]
    [InlineData("robot", "robot")]
    [InlineData("WOMAN", "woman")]
    [InlineData("Man", "man")]
    [InlineData("animal", "animal")]
    [InlineData("dragon", "robot")]
    [InlineData("", "robot")]
    [InlineData(null, "robot")]
    public void Find_is_case_insensitive_and_unknown_ids_mean_the_robot(string? id, string expected)
    {
        Assert.Equal(expected, MascotCharacters.Find(id).Id);
    }

    [Fact]
    public void Every_registered_character_has_a_sheet_with_the_same_grid_and_the_same_frames_as_the_robot()
    {
        var config = MascotConfig.Default;
        using var robot = new Bitmap(Path.Combine(AssetsDirectory, "robot-sprites.png"));

        foreach (var character in MascotCharacters.All)
        {
            var path = Path.Combine(AssetsDirectory, character.SheetFile);
            Assert.True(File.Exists(path), $"{character.Id}: {character.SheetFile} is missing from the output folder");
            using var sheet = new Bitmap(path);
            Assert.Equal((robot.Width, robot.Height), (sheet.Width, sheet.Height));

            // Every animation the robot has is drawn for this character too, frame for frame (same cells filled, none extra).
            foreach (var animation in config.Animations.Values)
            {
                for (var frame = 0; frame < MascotConfig.SheetColumns; frame++)
                {
                    var opaque = Opaque(sheet, frame, animation.Row, config.FrameSize);
                    if (frame < animation.Frames) Assert.True(opaque > 60, $"{character.Id} {animation.State} frame {frame} looks empty ({opaque})");
                    else Assert.Equal(0, opaque);
                }
            }
        }
    }

    private static int Opaque(Bitmap sheet, int column, int row, int size)
    {
        var count = 0;
        for (var x = 0; x < size; x++)
        {
            for (var y = 0; y < size; y++)
            {
                if (sheet.GetPixel(column * size + x, row * size + y).A > 0) count++;
            }
        }

        return count;
    }

    [Fact]
    public void The_characters_look_different_from_each_other_but_fill_the_same_cells()
    {
        var hashes = new HashSet<string>();
        foreach (var character in MascotCharacters.All)
        {
            hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(AssetsDirectory, character.SheetFile)))));
        }

        Assert.Equal(4, hashes.Count);
    }

    private static double Luminance(Color c)
    {
        static double Channel(int v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast(double a, double b) => (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);

    [Fact]
    public void Each_character_is_readable_on_a_dark_and_on_a_light_taskbar()
    {
        var dark = Luminance(Color.FromArgb(32, 32, 32));
        var light = Luminance(Color.FromArgb(243, 243, 243));

        foreach (var character in MascotCharacters.All)
        {
            using var sheet = new Bitmap(Path.Combine(AssetsDirectory, character.SheetFile));
            var colours = new Dictionary<int, int>();
            for (var x = 0; x < sheet.Width; x++)
            {
                for (var y = 0; y < sheet.Height; y++)
                {
                    var p = sheet.GetPixel(x, y);
                    if (p.A == 255) colours[p.ToArgb()] = colours.GetValueOrDefault(p.ToArgb()) + 1;
                }
            }

            var total = colours.Values.Sum();
            // The outline is the darkest colour (it separates the figure on a light taskbar); the fills carry it on a dark one: most of the
            // figure must have a visible contrast against both, the way the robot''s does.
            var visibleOnBoth = colours.Where(c => Contrast(Luminance(Color.FromArgb(c.Key)), dark) >= 1.8 && Contrast(Luminance(Color.FromArgb(c.Key)), light) >= 1.3).Sum(c => c.Value);
            var outline = colours.Where(c => Luminance(Color.FromArgb(c.Key)) < 0.03).Sum(c => c.Value);
            Assert.True(visibleOnBoth >= total * 0.35, $"{character.Id}: only {100.0 * visibleOnBoth / total:0}% of the figure shows on both a dark and a light taskbar");
            Assert.True(outline >= total * 0.08, $"{character.Id}: the outline is missing ({100.0 * outline / total:0}%)");
        }
    }

    // ---------- sources and licenses ----------

    [Fact]
    public void Every_sheet_has_its_source_and_license_recorded_and_the_generator_makes_all_of_them()
    {
        var credits = File.ReadAllText(Path.Combine(RepoRoot(), "Assets", "Mascot", "CREDITS.md"));
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "generate-mascot-sprites.ps1"));

        foreach (var character in MascotCharacters.All)
        {
            Assert.Contains(character.SheetFile, credits);
            Assert.Contains($"'{character.Id}'", script); // the generator draws it
        }

        Assert.Contains("CC0 1.0", credits);
        Assert.Contains("original", credits, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("third-party sprites", credits, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- the saved choice ----------

    [Fact]
    public void The_default_is_the_mascot_on_with_the_robot()
    {
        Assert.True(MascotSettings.Default.Enabled);
        Assert.Equal("robot", MascotSettings.Default.Character);
        var loaded = new MascotStore(Path.Combine(_dir, "none.json")).Load();
        Assert.True(loaded.Enabled);
        Assert.Equal("robot", loaded.Character);
    }

    [Theory]
    [InlineData("robot")]
    [InlineData("animal")]
    [InlineData("man")]
    [InlineData("woman")]
    public void The_choice_survives_a_save_and_a_load(string id)
    {
        var store = new MascotStore(Path.Combine(_dir, "mascot.json"));

        store.Save(MascotSettings.Default with { Character = id, Enabled = false });
        var loaded = store.Load();

        Assert.Equal(id, loaded.Character);
        Assert.False(loaded.Enabled);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("")]
    [InlineData("""{"Character":"dragon"}""")]
    [InlineData("""{"Character":42}""")]
    [InlineData("""{"Character":null}""")]
    [InlineData("""{"Enabled":true,"VolumePercent":50}""")]
    public void A_damaged_unknown_or_older_file_means_the_robot(string content)
    {
        var path = Path.Combine(_dir, "mascot.json");
        File.WriteAllText(path, content);

        Assert.Equal("robot", new MascotStore(path).Load().Character);
    }

    [Fact]
    public void An_older_file_keeps_its_other_choices()
    {
        var path = Path.Combine(_dir, "mascot.json");
        File.WriteAllText(path, """{"Enabled":false,"VolumePercent":50,"SizeScale":3}""");

        var loaded = new MascotStore(path).Load();

        Assert.False(loaded.Enabled);
        Assert.Equal(50, loaded.VolumePercent);
        Assert.Equal(3, loaded.SizeScale);
    }
}

/// <summary>The real window: the menus, the on/off toggle in widget mode, picking a character, and persistence.</summary>
[Collection("process")]
public sealed class MascotCharacterWidgetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PomoQuoteMascotForm-" + Guid.NewGuid().ToString("N"));

    public MascotCharacterWidgetTests() => Directory.CreateDirectory(_root);

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

    private MainForm NewForm(bool widget)
    {
        new WidgetStore(Path.Combine(_root, "widget.json")).Save(new WidgetSettings(Enabled: widget, StartIn: widget ? StartMode.Widget : StartMode.Normal));
        var http = new HttpClient(new NeverAnswers()) { Timeout = TimeSpan.FromMinutes(10) };
        var form = new MainForm(
            new WeatherService(http), new SettingsStore(Path.Combine(_root, "settings.json")), new PomodoroStore(Path.Combine(_root, "pomodoro.json")),
            TimeProvider.System, new WidgetStore(Path.Combine(_root, "widget.json")), new MascotStore(Path.Combine(_root, "mascot.json")),
            new QuoteSettingsStore(Path.Combine(_root, "quotes.json")), new BundledQuoteProvider(), new Random(1),
            new PomoQuote.AiUsage.UsageSettingsStore(Path.Combine(_root, "usage.json")), new PomoQuote.AiUsage.UsageCacheStore(Path.Combine(_root, "usage-cache.json")), new PomoQuote.AiUsage.UsageAlertStore(Path.Combine(_root, "usage-alerts.json")),
            [], null, new WeatherEffectSettingsStore(Path.Combine(_root, "weather-effects.json")))
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-3000, -3000),
            WorkAreaProvider = () => [new Rectangle(-6000, -6000, 4000, 4000)],
            MonitorProvider = () => [new MonitorInfo("TEST", new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040), 96, true)],
        };
        form.BridgeNoticeSink = (_, _) => { };
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

    private static void Pump(int milliseconds)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    [Fact]
    public void By_default_the_mascot_is_on_as_the_robot_and_walks_next_to_the_widget_from_the_start()
    {
        OnStaThread(() =>
        {
            using var form = NewForm(widget: true);
            form.Show();
            Pump(300);

            Assert.True(form.Mascot.Settings.Enabled);
            Assert.Equal("robot", form.Mascot.Settings.Character);
            Assert.True(form.Mascot.IsRunning); // widget mode: the mascot is out, like when the window is minimized
            Assert.Equal("robot", form.Mascot.LoadedCharacter);
            Assert.All(form.MascotMenuForTests.Where(m => m.Text == "Show the &mascot"), m => Assert.True(m.Checked));
        });
    }

    [Fact]
    public void The_normal_window_does_not_send_it_out_until_it_is_minimized_as_before()
    {
        OnStaThread(() =>
        {
            using var form = NewForm(widget: false);
            form.Show();
            Pump(300);

            Assert.False(form.Mascot.IsRunning);
        });
    }

    [Fact]
    public void The_widget_menu_has_a_Show_the_mascot_toggle_that_works_at_once_and_is_remembered()
    {
        OnStaThread(() =>
        {
            using (var form = NewForm(widget: true))
            {
                form.Show();
                Pump(300);
                var item = form.WidgetMenuForTests.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "Show the &mascot");
                Assert.True(item.Checked);
                Assert.True(form.Mascot.IsRunning);

                item.PerformClick();
                Assert.False(form.Mascot.IsRunning);
                Assert.False(item.Checked);
                Assert.False(form.Mascot.Settings.Enabled);

                item.PerformClick();
                Assert.True(form.Mascot.IsRunning);
                Assert.True(item.Checked);

                item.PerformClick(); // off again, and left off
                Assert.False(form.Mascot.IsRunning);
            }

            Assert.False(new MascotStore(Path.Combine(_root, "mascot.json")).Load().Enabled);
            using var next = NewForm(widget: true);
            next.Show();
            Pump(300);
            Assert.False(next.Mascot.IsRunning); // off stays off after a restart
            Assert.All(next.MascotMenuForTests.Where(m => m.Text == "Show the &mascot"), m => Assert.False(m.Checked));
        });
    }

    [Fact]
    public void Every_settings_menu_has_the_mascot_picker_with_the_four_characters_and_the_current_one_ticked()
    {
        OnStaThread(() =>
        {
            using var form = NewForm(widget: true);
            form.Show();
            Pump(200);

            var menu = form.MascotMenuForTests;
            Assert.Equal(4 * 3, form.MascotCharacterItemsForTests.Count); // the main, widget and tray Settings menus each list the four
            Assert.Equal(["Robot", "Animal", "Man", "Woman"], menu.Where(m => m.Text is "Robot" or "Animal" or "Man" or "Woman").Select(m => m.Text).Take(4));
            Assert.All(form.MascotCharacterItemsForTests.Where(c => c.Id == "robot"), c => Assert.True(c.Checked));
            Assert.All(form.MascotCharacterItemsForTests.Where(c => c.Id != "robot"), c => Assert.False(c.Checked));
        });
    }

    [Theory]
    [InlineData("animal")]
    [InlineData("man")]
    [InlineData("woman")]
    public void Picking_a_character_changes_the_walking_mascot_at_once_and_is_remembered(string id)
    {
        OnStaThread(() =>
        {
            using (var form = NewForm(widget: true))
            {
                form.Show();
                Pump(300);
                Assert.Equal("robot", form.Mascot.LoadedCharacter);

                form.SetMascotCharacter(id);

                Assert.True(form.Mascot.IsRunning); // no restart, it just changes
                Assert.Equal(id, form.Mascot.LoadedCharacter);
                Assert.All(form.MascotCharacterItemsForTests.Where(c => c.Id == id), c => Assert.True(c.Checked));
                Assert.All(form.MascotCharacterItemsForTests.Where(c => c.Id != id), c => Assert.False(c.Checked));
                Pump(200);
                Assert.True(form.Mascot.Pose is not null);
            }

            Assert.Equal(id, new MascotStore(Path.Combine(_root, "mascot.json")).Load().Character);
            using var next = NewForm(widget: true);
            next.Show();
            Pump(300);
            Assert.Equal(id, next.Mascot.Settings.Character);
            Assert.Equal(id, next.Mascot.LoadedCharacter);
            Assert.All(next.MascotCharacterItemsForTests.Where(c => c.Id == id), c => Assert.True(c.Checked));
        });
    }

    [Fact]
    public void A_character_picked_while_the_mascot_is_off_is_used_when_it_is_turned_on()
    {
        OnStaThread(() =>
        {
            using var form = NewForm(widget: true);
            form.Show();
            Pump(300);
            form.SetMascotEnabled(false);
            form.SetMascotCharacter("woman");
            Assert.False(form.Mascot.IsRunning);

            form.SetMascotEnabled(true);

            Assert.True(form.Mascot.IsRunning);
            Assert.Equal("woman", form.Mascot.LoadedCharacter);
        });
    }

    [Fact]
    public void A_character_whose_sheet_is_missing_falls_back_to_the_robot_and_is_not_reloaded_on_every_apply()
    {
        OnStaThread(() =>
        {
            using var form = NewForm(widget: true);
            form.Show();
            Pump(300);
            var sheet = Path.Combine(AppContext.BaseDirectory, "Assets", "Mascot", "man-sprites.png");
            var hidden = sheet + ".hidden";
            File.Move(sheet, hidden);
            try
            {
                form.SetMascotCharacter("man");
                Assert.True(form.Mascot.IsRunning);
                Assert.Equal("robot", form.Mascot.LoadedCharacter); // the robot stands in

                File.Move(hidden, sheet); // the file is back, but the choice has not changed: nothing is reloaded by an unrelated Apply
                form.Mascot.Apply(form.Mascot.Settings with { SoundEnabled = false });
                Assert.Equal("robot", form.Mascot.LoadedCharacter);

                form.SetMascotCharacter("woman"); // a real change loads at once
                Assert.Equal("woman", form.Mascot.LoadedCharacter);
                form.SetMascotCharacter("man");
                Assert.Equal("man", form.Mascot.LoadedCharacter);
            }
            finally
            {
                if (File.Exists(hidden)) File.Move(hidden, sheet);
            }
        });
    }

    [Fact]
    public void Leaving_and_entering_widget_mode_sends_the_mascot_out_and_takes_it_in()
    {
        OnStaThread(() =>
        {
            using var form = NewForm(widget: true);
            form.Show();
            Pump(300);
            Assert.True(form.Mascot.IsRunning);

            form.SetWidgetMode(false);
            Assert.False(form.Mascot.IsRunning);

            form.SetWidgetMode(true);
            Assert.True(form.Mascot.IsRunning);
        });
    }

    [Fact]
    public void A_double_click_on_the_mascot_brings_back_a_hidden_widget_and_a_shown_one_stays()
    {
        OnStaThread(() =>
        {
            using var form = NewForm(widget: true);
            form.Show();
            Pump(300);
            form.HideWidgetToTray();
            Assert.Equal(ShellState.WidgetHidden, form.ShellState);
            Assert.True(form.Mascot.IsRunning); // hiding the widget leaves the mascot out

            form.OnMascotDoubleClicked();

            Assert.Equal(ShellState.WidgetVisible, form.ShellState);
            Assert.True(form.Mascot.IsRunning);
        });
    }

    [Fact]
    public void Switching_characters_many_times_does_not_leak_handles()
    {
        OnStaThread(() =>
        {
            using var form = NewForm(widget: true);
            form.Show();
            Pump(300);
            var process = System.Diagnostics.Process.GetCurrentProcess();
            for (var i = 0; i < 12; i++) form.SetMascotCharacter(MascotCharacters.All[i % 4].Id);
            Pump(200);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            process.Refresh();
            var before = process.HandleCount;

            for (var i = 0; i < 80; i++)
            {
                form.SetMascotCharacter(MascotCharacters.All[i % 4].Id);
                Pump(5);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            process.Refresh();
            Assert.True(process.HandleCount - before <= 40, $"handles {before} -> {process.HandleCount}");
        });
    }
}