using System.Drawing.Text;
using System.Runtime.InteropServices;
using HelloWorldBot.Pomodoro;
using HelloWorldBot.Weather;
using HelloWorldBot.Widget;

namespace HelloWorldBot;

/// <summary>Shows "Hello World" in a bundled digital font with a real-time analog clock below it.</summary>
internal sealed partial class MainForm : Form
{
    private const string DisplayText = "Hello World";

    // DSEG's space glyph is very narrow; its "!" glyph is a blank, full-width digit cell.
    private const string DigitalFontDrawnText = "Hello!World";

    private const float FontSizePoints = 36f;
    private const string FallbackFontFamily = "Consolas";

    private static readonly Size LogicalClientSize = new(800, 520);
    private static readonly Size LogicalTextPadding = new(48, 48);
    private const int LogicalMinimumClockSize = 120;
    private const int LogicalPomodoroWidth = 250;
    private static readonly TimeSpan WeatherRefreshInterval = TimeSpan.FromMinutes(10);

    // The collection must outlive any Font created from it.
    private readonly PrivateFontCollection _fonts = new();
    private readonly Font _displayFont;
    private readonly CenteredTextLabel _label;
    private readonly AnalogClockControl _clock;
    private readonly MenuStrip _menu;
    private readonly WeatherPanel _weather = new() { Dock = DockStyle.Top };
    private readonly PomodoroController _pomodoro;
    private readonly PomodoroPanel _pomodoroPanel;
    private readonly ToolStripMenuItem _cityItem;
    private readonly System.Windows.Forms.Timer _weatherTimer = new() { Interval = (int)WeatherRefreshInterval.TotalMilliseconds };
    private readonly WeatherService _weatherService;
    private readonly SettingsStore _settingsStore;
    private AppSettings _settings;
    private CancellationTokenSource? _weatherRefresh;
    private readonly WallpaperBackground _wallpaper = new();
    private readonly ToolStripMenuItem _noWallpaperItem;
    private readonly Dictionary<Wallpaper, ToolStripMenuItem> _wallpaperItems = [];

    // A light dial keeps the dark hands readable over any wallpaper.
    private static readonly Color WallpaperClockFace = Color.FromArgb(200, 255, 255, 255);

    public MainForm() : this(
        WeatherService.Shared,
        new SettingsStore(SettingsStore.DefaultPath),
        new PomodoroStore(PomodoroStore.DefaultPath),
        TimeProvider.System,
        new WidgetStore(WidgetStore.DefaultPath))
    {
    }

    public MainForm(WeatherService weatherService, SettingsStore settingsStore, PomodoroStore pomodoroStore, TimeProvider clock, WidgetStore widgetStore)
    {
        _widgetStore = widgetStore;
        _widgetSettings = widgetStore.Load();
        _pomodoro = new PomodoroController(clock, pomodoroStore, Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds"));
        _pomodoroPanel = new PomodoroPanel(_pomodoro) { Dock = DockStyle.Right };
        _pomodoro.TitleChanged += (_, title) => Text = title is null ? DisplayText : $"{title} - {DisplayText}";
        _weatherService = weatherService;
        _settingsStore = settingsStore;
        _settings = settingsStore.Load();

        var digitalFont = LoadDigitalFont();
        _displayFont = digitalFont ?? new Font(FallbackFontFamily, FontSizePoints, FontStyle.Regular, GraphicsUnit.Point);

        Text = DisplayText;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        ResizeRedraw = true;

        _label = new CenteredTextLabel(digitalFont is null ? DisplayText : DigitalFontDrawnText)
        {
            Text = DisplayText,
            Font = _displayFont,
            ForeColor = Color.Green,
            BackColor = Color.Transparent,
            Dock = DockStyle.Top,
        };
        _clock = new AnalogClockControl { Dock = DockStyle.Fill };

        // The clock fills what remains below the text band, so the two never overlap.
        Controls.Add(_clock);
        Controls.Add(_pomodoroPanel);
        Controls.Add(_weather);
        Controls.Add(_label);

        // Added last so it docks first, at the very top, above the text band.
        var aboutItem = new ToolStripMenuItem("&About", null, (_, _) => ShowAboutDialog());
        var helpMenu = new ToolStripMenuItem("&Help");
        helpMenu.DropDownItems.Add(aboutItem);
        var wallpaperMenu = new ToolStripMenuItem("&Wallpaper");
        _noWallpaperItem = new ToolStripMenuItem("&None", null, (_, _) => SelectWallpaper(null)) { Checked = true };
        wallpaperMenu.DropDownItems.Add(_noWallpaperItem);
        foreach (var wallpaper in WallpaperBackground.Available)
        {
            var item = new ToolStripMenuItem(wallpaper.Name, null, (_, _) => SelectWallpaper(wallpaper));
            _wallpaperItems[wallpaper] = item;
            wallpaperMenu.DropDownItems.Add(item);
        }

        _cityItem = new ToolStripMenuItem("&City...", null, (_, _) => ChangeCity());
        var pomodoroItem = new ToolStripMenuItem("&Pomodoro...", null, (_, _) => ShowPomodoroSettings());
        var settingsMenu = new ToolStripMenuItem("&Settings");
        settingsMenu.DropDownItems.Add(_cityItem);
        settingsMenu.DropDownItems.Add(pomodoroItem);

        var viewMenu = new ToolStripMenuItem("&View");
        viewMenu.DropDownItems.Add(wallpaperMenu);
        BuildWidget(viewMenu);
        _menu = new MenuStrip { Dock = DockStyle.Top };
        _menu.Items.Add(viewMenu);
        _menu.Items.Add(settingsMenu);
        _menu.Items.Add(helpMenu);
        MainMenuStrip = _menu;
        Controls.Add(_menu);

        _weather.SetCity(_settings.City.DisplayName);
        _weatherTimer.Tick += (_, _) => _ = RefreshWeatherAsync();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Sizes depend on the display scale, so they are applied once the window has a DPI.
        var textBand = _label.MeasureDrawnText() + LogicalToDeviceUnits(LogicalTextPadding);
        _label.Height = textBand.Height;
        _weather.Height = _weather.MeasureHeight();
        _pomodoroPanel.Width = LogicalToDeviceUnits(LogicalPomodoroWidth);
        var belowWeather = Math.Max(LogicalToDeviceUnits(LogicalMinimumClockSize), _pomodoroPanel.MeasureMinimumHeight());
        var minimumClientSize = new Size(
            Math.Max(textBand.Width, _pomodoroPanel.Width + LogicalToDeviceUnits(LogicalMinimumClockSize)),
            _menu.Height + textBand.Height + _weather.Height + belowWeather);
        MinimumSize = SizeFromClientSize(minimumClientSize);
        ClientSize = LogicalToDeviceUnits(LogicalClientSize);
        CenterToScreen();
        _normalBounds = Bounds;
        if (_widgetSettings.Enabled) SetWidgetMode(true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _weatherTimer.Start();
        _pomodoro.Start();
        _ = RefreshWeatherAsync();
    }

    private void ShowPomodoroSettings()
    {
        using var dialog = new PomodoroSettingsDialog(_pomodoro.Timer.Settings);
        if (dialog.ShowDialog(this) == DialogResult.OK) _pomodoro.ApplySettings(dialog.Value);
    }

    /// <summary>
    /// Fetches the current weather without blocking the UI. A newer refresh cancels an older one,
    /// and a failure keeps the last known values on screen, marked as stale.
    /// </summary>
    private async Task RefreshWeatherAsync()
    {
        _weatherRefresh?.Cancel();
        var refresh = _weatherRefresh = new CancellationTokenSource();
        try
        {
            var report = await _weatherService.GetCurrentAsync(_settings.City, refresh.Token);
            if (!refresh.IsCancellationRequested && !IsDisposed) _weather.ShowReport(report, DateTime.Now);
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer refresh, or the window is closing.
        }
        catch (WeatherException)
        {
            if (!refresh.IsCancellationRequested && !IsDisposed) _weather.ShowUnavailable();
        }
        finally
        {
            if (ReferenceEquals(_weatherRefresh, refresh)) _weatherRefresh = null;
            refresh.Dispose();
        }
    }

    /// <summary>Asks for a city, resolves it, and only then switches; any failure keeps the previous city.</summary>
    private async void ChangeCity()
    {
        string requested;
        using (var dialog = new CityDialog(_settings.City.Name))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            requested = dialog.Value;
        }

        SetCityItemsEnabled(false);
        try
        {
            var city = await _weatherService.GeocodeAsync(requested);
            if (IsDisposed) return;
            if (city is null)
            {
                ShowCityMessage($"No city named \"{requested}\" was found. The city was not changed.");
                return;
            }

            _settings = new AppSettings(city);
            try
            {
                _settingsStore.Save(_settings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowCityMessage($"{city.DisplayName} is shown now, but could not be saved for the next start.");
            }

            _weather.SetCity(city.DisplayName);
            _ = RefreshWeatherAsync();
        }
        catch (WeatherException)
        {
            if (!IsDisposed) ShowCityMessage("Could not look up the city (no connection to the weather service?). The city was not changed.");
        }
        finally
        {
            if (!IsDisposed) SetCityItemsEnabled(true);
        }
    }

    private void ShowCityMessage(string message) =>
        MessageBox.Show(this, message, "Settings - City", MessageBoxButtons.OK, MessageBoxIcon.Information);

    // Children are transparent, so the form's own background shows the wallpaper behind them.
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (!_widgetMode) _wallpaper.Paint(e.Graphics, ClientRectangle);
    }

    /// <summary>Shows the chosen wallpaper, or the plain background for null. Exactly one menu item stays checked.</summary>
    private void SelectWallpaper(Wallpaper? wallpaper)
    {
        if (wallpaper is not null)
        {
            try
            {
                _wallpaper.Load(wallpaper);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException or OutOfMemoryException)
            {
                MessageBox.Show(this, $"Could not load the \"{wallpaper.Name}\" wallpaper.", DisplayText, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }
        else
        {
            _wallpaper.Clear();
        }

        _noWallpaperItem.Checked = wallpaper is null;
        foreach (var (item, menuItem) in _wallpaperItems)
        {
            menuItem.Checked = item == wallpaper;
        }

        _clock.FaceColor = wallpaper is null ? Color.Transparent : WallpaperClockFace;
        _weather.OnWallpaper = wallpaper is not null;
        _pomodoroPanel.OnWallpaper = wallpaper is not null;
        Invalidate(true);
    }

    private void ShowAboutDialog()
    {
        using var dialog = new AboutDialog();
        dialog.ShowDialog(this);
    }

    private Font? LoadDigitalFont()
    {
        try
        {
            _fonts.AddFontFile(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "DSEG14Classic-Regular.ttf"));
            return _fonts.Families.Length > 0
                ? new Font(_fonts.Families[0], FontSizePoints, FontStyle.Regular, GraphicsUnit.Point)
                : null;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException)
        {
            return null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _weatherTimer.Dispose();
            DisposeWidget();
            _pomodoro.Dispose();
            _weatherRefresh?.Cancel();
            _wallpaper.Dispose();
            _displayFont.Dispose();
            _fonts.Dispose();
        }
    }
}
