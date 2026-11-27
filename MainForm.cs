using System.Runtime.InteropServices;
using PomoQuote.AiUsage;
using PomoQuote.AiUsage.Claude;
using PomoQuote.Mascot;
using PomoQuote.Pomodoro;
using PomoQuote.Quotes;
using PomoQuote.Weather;
using PomoQuote.Widget;

namespace PomoQuote;

/// <summary>The main window: an English sentence with its Vietnamese translation, the weather, a Pomodoro timer and a real-time analog clock.</summary>
internal sealed partial class MainForm : Form
{
    private const string DisplayText = AboutDialog.AppName;

    private static readonly Size LogicalClientSize = new(800, 520);
    private const int LogicalMinimumClockSize = 120;
    private const int LogicalMinimumWidth = 440;
    private const int LogicalPomodoroWidth = 250;
    private const double QuoteBandShare = 0.34;
    private static readonly TimeSpan WeatherRefreshInterval = TimeSpan.FromMinutes(10);

    private readonly QuoteLabel _quote;
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
        new WidgetStore(WidgetStore.DefaultPath),
        new MascotStore(MascotStore.DefaultPath))
    {
    }

    public MainForm(
        WeatherService weatherService, SettingsStore settingsStore, PomodoroStore pomodoroStore, TimeProvider clock,
        WidgetStore widgetStore, MascotStore mascotStore, QuoteSettingsStore? quoteStore = null, IQuoteProvider? quoteProvider = null, Random? random = null,
        UsageSettingsStore? usageSettings = null, UsageCacheStore? usageCache = null, UsageAlertStore? usageAlerts = null, IReadOnlyList<IUsageProvider>? usageProviders = null, ClaudeSetup? claudeSetup = null,
        WeatherEffectSettingsStore? weatherEffects = null)
    {
        _mascotStore = mascotStore;
        _mascotSettings = mascotStore.Load();
        _tray = new TrayIconHost();
        _widgetStore = widgetStore;
        _widgetSettings = widgetStore.Load();
        _pomodoro = new PomodoroController(clock, pomodoroStore, Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds"), _tray);
        _pomodoroPanel = new PomodoroPanel(_pomodoro) { Dock = DockStyle.Right };
        _pomodoro.TitleChanged += (_, title) => Text = title is null ? DisplayText : $"{title} - {DisplayText}";
        _weatherService = weatherService;
        _settingsStore = settingsStore;
        _settings = settingsStore.Load();

        Text = DisplayText;
        Icon = AppIcon.Load(); // title bar, taskbar button and Alt+Tab (the form keeps and disposes it)
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        ResizeRedraw = true;

        _quote = new QuoteLabel { Dock = DockStyle.Top };
        _clock = new AnalogClockControl { Dock = DockStyle.Fill };

        // The clock fills what remains below the sentence band, so the two never overlap.
        Controls.Add(_clock);
        Controls.Add(_pomodoroPanel);
        Controls.Add(_weather);
        Controls.Add(_quote);

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
        settingsMenu.DropDownItems.Add(BuildMascotMenu(fromTray: false));
        settingsMenu.DropDownItems.Add(new ToolStripMenuItem("&Quotes...", null, (_, _) => ShowQuoteSettings()));
        settingsMenu.DropDownItems.Add(new ToolStripMenuItem("&AI Usage...", null, (_, _) => ShowUsageSettings()));
        settingsMenu.DropDownItems.Add(new ToolStripMenuItem("Weather &effects...", null, (_, _) => ShowWeatherEffectsSettings()));
        settingsMenu.DropDownItems.Add(BuildThemeMenu());
        settingsMenu.DropDownItems.Add(BuildStartInMenu());

        var viewMenu = new ToolStripMenuItem("&View");
        viewMenu.DropDownItems.Add(wallpaperMenu);
        BuildQuotes(viewMenu, quoteStore ?? new QuoteSettingsStore(QuoteSettingsStore.DefaultPath), quoteProvider ?? new BundledQuoteProvider(), random ?? Random.Shared);
        BuildWidget(viewMenu);
        _weatherEffectsStore = weatherEffects ?? new WeatherEffectSettingsStore(WeatherEffectSettingsStore.DefaultPath);
        _widgetView.ApplyEffectSettings(_weatherEffectsStore.Load());
        BuildUsage(usageSettings, usageCache, usageAlerts, usageProviders, claudeSetup);
        BuildMascot(clock);
        _menu = new MenuStrip { Dock = DockStyle.Top };
        _menu.Items.Add(viewMenu);
        _menu.Items.Add(settingsMenu);
        _menu.Items.Add(helpMenu);
        MainMenuStrip = _menu;
        Controls.Add(_menu);

        _weather.SetCity(_settings.City.DisplayName);
        _weatherTimer.Tick += (_, _) => _ = RefreshWeatherAsync();
        ShowNextQuote();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Sizes depend on the display scale, so they are applied once the window has a DPI.
        _weather.Height = _weather.MeasureHeight();
        _pomodoroPanel.Width = LogicalToDeviceUnits(LogicalPomodoroWidth);
        var belowWeather = Math.Max(LogicalToDeviceUnits(LogicalMinimumClockSize), _pomodoroPanel.MeasureMinimumHeight());
        var minimumBand = LogicalToDeviceUnits(QuoteLabel.LogicalMinimumHeight);
        var minimumClientSize = new Size(
            Math.Max(LogicalToDeviceUnits(LogicalMinimumWidth), _pomodoroPanel.Width + LogicalToDeviceUnits(LogicalMinimumClockSize)),
            _menu.Height + minimumBand + _weather.Height + belowWeather);
        MinimumSize = SizeFromClientSize(minimumClientSize);
        ClientSize = LogicalToDeviceUnits(LogicalClientSize);
        UpdateQuoteBand();
        CenterToScreen();
        _normalBounds = Bounds;
        if (StartModePolicy.StartsAsWidget(_widgetSettings.StartIn, _widgetSettings.Enabled)) SetWidgetMode(true);
    }

    /// <summary>The sentence band takes about a third of the height, never less than its minimum, so it grows with the window.</summary>
    private void UpdateQuoteBand()
    {
        if (_quote is null || _widgetMode) return;
        _quote.Height = Math.Max(LogicalToDeviceUnits(QuoteLabel.LogicalMinimumHeight), (int)(ClientSize.Height * QuoteBandShare));
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _weatherTimer.Start();
        _pomodoro.Start();
        _usage.Start();
        UpdateWidgetMascot(); // a start in widget mode: the mascot walks beside it
        _autoRefresh.Configure(_usage.Settings.ClaudeAutoRefresh, _usage.Settings.ClaudeAutoRefreshMinutes);
        _ = StartBridgeCheck();
        _ = RefreshWeatherAsync();
    }

    private void ShowPomodoroSettings()
    {
        using var dialog = new PomodoroSettingsDialog(_pomodoro.Timer.Settings);
        if (DialogHost.Show(dialog, this) == DialogResult.OK) _pomodoro.ApplySettings(dialog.Value);
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
            if (DialogHost.Show(dialog, this) != DialogResult.OK) return;
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
        DialogHost.Message(this, message, "Settings - City", MessageBoxButtons.OK, MessageBoxIcon.Information);

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
                DialogHost.Message(this, $"Could not load the \"{wallpaper.Name}\" wallpaper.", DisplayText, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        _quote.OnWallpaper = wallpaper is not null;
        _pomodoroPanel.OnWallpaper = wallpaper is not null;
        Invalidate(true);
    }

    private void ShowAboutDialog()
    {
        using var dialog = new AboutDialog();
        DialogHost.Show(dialog, this);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _weatherTimer.Dispose();
            DisposeQuotes();
            DisposeUsage();
        _quoteTip.Dispose();
            DisposeWidget();
            DisposeMascot();
            _pomodoro.Dispose();
            _tray.Dispose();
            _weatherRefresh?.Cancel();
            _wallpaper.Dispose();
        }
    }
}
