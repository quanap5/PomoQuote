using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using PomoQuote.Mascot;
using PomoQuote.Pomodoro;
using PomoQuote.Weather;
using PomoQuote.Widget;

namespace PomoQuote.Tests;

/// <summary>
/// Real windows, off screen: every dialog and message box opened over the always-on-top widget is above it, owned by the window that opened it,
/// beside the widget inside the (here made-up) screen area, and the widget is as it was afterwards.
/// </summary>
public sealed class DialogHostTests : IDisposable
{
    private static readonly Rectangle Area = new(-6000, -6000, 1920, 1032);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PomoQuoteTests-" + Guid.NewGuid().ToString("N"));

    public DialogHostTests() => Directory.CreateDirectory(_root);

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
        var mascot = new MascotStore(Path.Combine(_root, "mascot.json"));
        mascot.Save(mascot.Load() with { Enabled = false }); // no character walking over the real taskbar while the tests run
        var http = new HttpClient(new NeverAnswers()) { Timeout = TimeSpan.FromMinutes(10) };
        return new MainForm(
            new WeatherService(http), new SettingsStore(Path.Combine(_root, "settings.json")), new PomodoroStore(Path.Combine(_root, "pomodoro.json")),
            TimeProvider.System, new WidgetStore(Path.Combine(_root, "widget.json")), mascot,
            new Quotes.QuoteSettingsStore(Path.Combine(_root, "quotes.json")), null, new Random(1),
            new PomoQuote.AiUsage.UsageSettingsStore(Path.Combine(_root, "usage.json")), new PomoQuote.AiUsage.UsageCacheStore(Path.Combine(_root, "usage-cache.json")), new PomoQuote.AiUsage.UsageAlertStore(Path.Combine(_root, "usage-alerts.json")),
            [], null, new WeatherEffectSettingsStore(Path.Combine(_root, "weather-effects.json")))
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(Area.Left + 100, Area.Top + 100),
            WorkAreaProvider = () => [Area],
        };
    }

    /// <summary>The widget at the top right of the made-up screen, where people usually keep it.</summary>
    private MainForm NewWidget()
    {
        var form = NewForm();
        form.Show();
        form.SetWidgetMode(true);
        form.Location = new Point(Area.Right - form.Width, Area.Top);
        Application.DoEvents();
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(90)), "the dialog test did not finish");
        if (failure is not null) throw new InvalidOperationException("The dialog test failed: " + failure.Message, failure);
    }

    /// <summary>Runs <paramref name="open"/>, which blocks in a modal window; looks at the window from inside its loop and then closes it.</summary>
    private static void While(Action open, Action<IntPtr> look)
    {
        Exception? failure = null;
        var seen = false;
        var ticks = 0;
        var existing = ThreadWindows().ToHashSet();
        using var timer = new System.Windows.Forms.Timer { Interval = 150 };
        timer.Tick += (_, _) =>
        {
            var window = ThreadWindows().FirstOrDefault(h => !existing.Contains(h) && IsWindowVisible(h) && IsWindowEnabled(h) && GetWindow(h, GwOwner) != IntPtr.Zero && TextLength(h) > 0);
            if (window == IntPtr.Zero)
            {
                if (++ticks > 60) { timer.Stop(); Application.OpenForms.Cast<Form>().LastOrDefault(f => f.Modal)?.Close(); }
                return;
            }

            timer.Stop();
            seen = true;
            try { look(window); }
            catch (Exception ex) { failure = ex; }
            finally
            {
                PostMessage(window, WmCommand, IdNo, IntPtr.Zero); // a Yes/No box has no close button
                PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero);
            }
        };
        timer.Start();
        open();
        timer.Stop();
        if (failure is not null) throw failure;
        Assert.True(seen, "no modal window appeared");
    }

    private static void AssertInFrontBesideTheWidget(IntPtr window, MainForm form, IntPtr owner)
    {
        var bounds = BoundsOf(window);
        Assert.Equal(owner, GetWindow(window, GwOwner));
        Assert.True(IsTopMost(window), "the dialog is not in the always-on-top group");
        Assert.True(IsAbove(window, form.Handle), "the widget is above the dialog");
        Assert.True(Area.Contains(bounds), $"{bounds} is outside the screen area {Area}");
        Assert.False(bounds.IntersectsWith(form.Bounds), $"{bounds} covers the widget {form.Bounds}");
    }

    private static ToolStripMenuItem Item(ToolStripItemCollection items, params string[] path)
    {
        ToolStripMenuItem? found = null;
        foreach (var name in path)
        {
            found = items.OfType<ToolStripMenuItem>().First(i => i.Text!.Replace("&", "") == name);
            items = found.DropDownItems;
        }

        return found!;
    }

    [Theory]
    [InlineData("Pomodoro...")]
    [InlineData("Quotes...")]
    [InlineData("Weather effects...")]
    [InlineData("City...")]
    [InlineData("Mascot/Options...")]
    public void A_settings_dialog_from_the_widget_menu_is_in_front_beside_the_widget_and_leaves_the_widget_as_it_was(string entry)
    {
        OnStaThread(() =>
        {
            using var form = NewWidget();
            var before = (form.Bounds, form.TopMost, form.Opacity);

            While(() => Item(form.WidgetMenuForTests.Items, ["Settings", .. entry.Split('/')]).PerformClick(),
                window => AssertInFrontBesideTheWidget(window, form, form.Handle));

            Assert.Equal(before, (form.Bounds, form.TopMost, form.Opacity));
            Assert.True(form.IsWidgetMode && form.Visible && form.Enabled);
            Assert.True(IsTopMost(form.Handle));
            Assert.Equal(IntPtr.Zero, DialogHost.FrontTopMostWindow);
        });
    }

    [Theory]
    [InlineData("Pomodoro...")]
    [InlineData("City...")]
    [InlineData("Weather effects...")]
    public void From_the_tray_while_the_widget_is_hidden_the_widget_comes_back_and_the_dialog_is_beside_it(string entry)
    {
        OnStaThread(() =>
        {
            using var form = NewWidget();
            form.HideWidgetToTray();

            While(() => Item(form.TrayMenuForTests.Items, "Settings", entry).PerformClick(),
                window => AssertInFrontBesideTheWidget(window, form, form.Handle));

            Assert.Equal(ShellState.WidgetVisible, form.ShellState);
        });
    }

    [Fact]
    public void A_message_box_from_the_widget_is_in_front_beside_it()
    {
        OnStaThread(() =>
        {
            using var form = NewWidget();

            While(() => DialogHost.Message(form, "A question?", "Refresh Claude now", MessageBoxButtons.YesNo, MessageBoxIcon.Information),
                window => AssertInFrontBesideTheWidget(window, form, form.Handle));
        });
    }

    [Fact]
    public void With_the_widget_hidden_a_message_box_still_shows_on_the_widget_s_screen()
    {
        OnStaThread(() =>
        {
            using var form = NewWidget();
            form.HideWidgetToTray();

            While(() => DialogHost.Message(form, "A note.", "PomoQuote", MessageBoxButtons.OK, MessageBoxIcon.Information), window =>
            {
                Assert.True(IsWindowVisible(window));
                Assert.True(IsTopMost(window));
                Assert.True(Area.Contains(BoundsOf(window)), $"{BoundsOf(window)} is outside {Area}");
            });

            Assert.Equal(ShellState.WidgetHidden, form.ShellState);
        });
    }

    [Fact]
    public void A_child_dialog_and_a_message_box_of_a_dialog_are_above_the_dialog_and_the_widget()
    {
        OnStaThread(() =>
        {
            using var form = NewWidget();
            using var parent = new QuoteSettingsDialog(Quotes.QuoteSettings.Default);

            While(() => DialogHost.Show(parent, form), first =>
            {
                using var child = new Form { Text = "child", ClientSize = new Size(560, 240), FormBorderStyle = FormBorderStyle.FixedDialog, ShowInTaskbar = false };
                While(() => DialogHost.Show(child, parent), second =>
                {
                    AssertInFrontBesideTheWidget(second, form, parent.Handle);
                    Assert.True(IsAbove(second, first));
                });
                While(() => DialogHost.Message(parent, "Sure?", "Remove bridge", MessageBoxButtons.YesNo, MessageBoxIcon.Question), box =>
                {
                    AssertInFrontBesideTheWidget(box, form, parent.Handle);
                    Assert.True(IsAbove(box, first));
                });
            });
        });
    }

    [Fact]
    public void A_dialog_that_grows_stays_beside_the_widget_and_on_the_screen()
    {
        OnStaThread(() =>
        {
            using var form = NewWidget();
            form.Location = new Point(Area.Right - form.Width, Area.Bottom - form.Height);
            using var dialog = new Form { Text = "growing", ClientSize = new Size(300, 200), FormBorderStyle = FormBorderStyle.FixedDialog, ShowInTaskbar = false };

            While(() => DialogHost.Show(dialog, form), window =>
            {
                dialog.ClientSize = new Size(520, 600);
                Application.DoEvents();
                AssertInFrontBesideTheWidget(window, form, form.Handle);
            });
        });
    }

    [Fact]
    public void A_dialog_larger_than_the_screen_area_is_cut_to_it_and_scrolls_to_its_buttons()
    {
        OnStaThread(() =>
        {
            using var form = NewWidget();
            var small = new Rectangle(Area.Left, Area.Top, 1000, 420);
            form.WorkAreaProvider = () => [small];
            form.Location = new Point(small.Right - form.Width, small.Top);
            using var dialog = new AboutDialog(); // taller than the small area

            While(() => DialogHost.Show(dialog, form), _ =>
            {
                Assert.True(small.Contains(dialog.Bounds), $"{dialog.Bounds} is outside {small}");
                Assert.True(dialog.VerticalScroll.Visible);
                Assert.False(dialog.HorizontalScroll.Visible);
                var ok = (Button)dialog.AcceptButton!;
                dialog.ScrollControlIntoView(ok);
                var inDialog = dialog.RectangleToClient(ok.RectangleToScreen(ok.ClientRectangle));
                Assert.True(dialog.ClientRectangle.Contains(inDialog), $"OK at {inDialog} cannot be reached in {dialog.ClientRectangle}");
            });
        });
    }

    [Fact]
    public void In_the_normal_window_a_dialog_is_centred_over_it_and_not_made_always_on_top()
    {
        OnStaThread(() =>
        {
            using var form = NewForm();
            form.Show();
            form.SetWidgetMode(false); // the app starts as the widget
            form.Location = new Point(Area.Left + 100, Area.Top + 100);
            Application.DoEvents();

            While(() => Item(form.MainMenuStrip!.Items, "Help", "About").PerformClick(), window =>
            {
                var bounds = BoundsOf(window);
                Assert.Equal(form.Handle, GetWindow(window, GwOwner));
                Assert.False(IsTopMost(window));
                Assert.True(IsAbove(window, form.Handle));
                Assert.True(Area.Contains(bounds));
                Assert.InRange(bounds.X + bounds.Width / 2, form.Left + form.Width / 2 - 2, form.Left + form.Width / 2 + 2);
            });

            Assert.False(form.TopMost);
        });
    }

    [Fact]
    public void The_walking_character_goes_under_an_open_dialog_instead_of_over_it()
    {
        OnStaThread(() =>
        {
            using var form = NewWidget();
            using var mascot = new MascotWindow { Location = new Point(Area.Left, Area.Top) };
            mascot.Show();
            using var dialog = new QuoteSettingsDialog(Quotes.QuoteSettings.Default);

            While(() => DialogHost.Show(dialog, form), window =>
            {
                mascot.KeepOnTop();
                Assert.True(IsAbove(window, mascot.Handle), "the character is above the dialog");
                Assert.True(IsTopMost(mascot.Handle));
            });

            mascot.KeepOnTop(); // with no dialog open it goes back to the front
            Assert.True(IsAbove(mascot.Handle, form.Handle));
        });
    }

    [Fact]
    public void No_dialog_or_message_box_of_the_app_bypasses_the_dialog_host()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PomoQuote.csproj"))) root = root.Parent;
        var sources = Directory.GetFiles(root!.FullName, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Regex.IsMatch(Path.GetRelativePath(root.FullName, file), @"^(PomoQuote\.Tests|bin|obj|tools)[\\/]"))
            .Where(file => Path.GetFileName(file) != "DialogHost.cs");

        var direct = sources.SelectMany(file => File.ReadLines(file).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                && Regex.IsMatch(line, @"\.ShowDialog\(|MessageBox\.Show\(|TaskDialog\.ShowDialog"))
            .Select(line => $"{Path.GetFileName(file)}: {line.Trim()}")).ToList();

        Assert.Empty(direct);
    }

    private const int GwHwndNext = 2;
    private const int GwOwner = 4;
    private const int WmClose = 0x0010;
    private const int WmCommand = 0x0111;
    private const int IdNo = 7;

    private static List<IntPtr> ThreadWindows()
    {
        var windows = new List<IntPtr>();
        EnumThreadWindows(GetCurrentThreadId(), (window, _) => { windows.Add(window); return true; }, IntPtr.Zero);
        return windows;
    }

    private static bool IsTopMost(IntPtr window) => (GetWindowLong(window, -20) & 0x8) != 0;

    /// <summary>Whether <paramref name="upper"/> comes before <paramref name="lower"/> in the screen's z-order, from the top down.</summary>
    private static bool IsAbove(IntPtr upper, IntPtr lower)
    {
        for (var window = GetTopWindow(IntPtr.Zero); window != IntPtr.Zero; window = GetWindow(window, GwHwndNext))
        {
            if (window == upper) return true;
            if (window == lower) return false;
        }

        return false;
    }

    private static Rectangle BoundsOf(IntPtr window)
    {
        Assert.True(GetWindowRect(window, out var r));
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    private static int TextLength(IntPtr window) => GetWindowTextLength(window);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool WindowProc(IntPtr window, IntPtr parameter);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint thread, WindowProc proc, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern IntPtr GetTopWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
