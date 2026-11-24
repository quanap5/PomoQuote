using PomoQuote.Pomodoro;
using PomoQuote.Quotes;

namespace PomoQuote;

/// <summary>
/// The sentences: which one is shown, and when it changes. A new sentence appears at start, at the start of
/// each Pomodoro phase (unless the interval is Off), on a timer when a minutes interval is chosen, and on
/// request (View > Next sentence, Ctrl+N). The choices are kept in quotes.json.
/// </summary>
internal sealed partial class MainForm
{
    private QuoteSettingsStore _quoteStore = null!;
    private QuoteSettings _quoteSettings = QuoteSettings.Default;
    private QuoteRotator _quoteRotator = null!;
    private readonly System.Windows.Forms.Timer _quoteTimer = new();
    private ToolStripMenuItem _translationItem = null!;
    private PomodoroRunState _lastRunState = PomodoroRunState.Idle;

    /// <summary>The sentence on screen, or null when no category holds one.</summary>
    internal Quote? CurrentQuote { get; private set; }

    private void BuildQuotes(ToolStripMenuItem viewMenu, QuoteSettingsStore store, IQuoteProvider provider, Random random)
    {
        _quoteStore = store;
        _quoteSettings = store.Load();
        _quoteRotator = new QuoteRotator(provider.GetAll(), _quoteSettings.Categories, random, _quoteSettings.LastEnglish);

        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("&Next sentence", null, (_, _) => ShowNextQuote()) { ShortcutKeyDisplayString = "Ctrl+N" });
        _translationItem = new ToolStripMenuItem("Vietnamese &translation", null, (_, _) => ToggleTranslation())
        {
            Checked = _quoteSettings.ShowTranslation,
        };
        viewMenu.DropDownItems.Add(_translationItem);

        // A new phase brings a new sentence: it completed, it was skipped, or the timer was started from idle.
        _pomodoro.Timer.PhaseCompleted += (_, _) => OnPhaseStarted();
        _pomodoro.Timer.PhaseSkipped += (_, _) => OnPhaseStarted();
        _pomodoro.Timer.Changed += (_, _) =>
        {
            var state = _pomodoro.Timer.State;
            var started = _lastRunState == PomodoroRunState.Idle && state == PomodoroRunState.Running;
            _lastRunState = state;
            if (started) OnPhaseStarted();
        };

        _quoteTimer.Tick += (_, _) => ShowNextQuote();
        ConfigureQuoteTimer();
    }

    private void OnPhaseStarted()
    {
        if (_quoteSettings.ChangesOnPhase) ShowNextQuote();
    }

    /// <summary>Shows the next sentence now (and restarts the interval timer so it does not change again at once).</summary>
    internal void ShowNextQuote()
    {
        var quote = _quoteRotator.Next();
        if (quote is not null) CurrentQuote = quote;
        ApplyQuote();
        _quoteSettings = _quoteSettings with { LastEnglish = CurrentQuote?.English };
        SaveQuoteSettings();
        ConfigureQuoteTimer();
    }

    private void ApplyQuote()
    {
        var quote = CurrentQuote;
        var english = quote?.English ?? "";
        var vietnamese = quote is not null && _quoteSettings.ShowTranslation ? quote.Vietnamese : null;
        _quote.SetQuote(english, vietnamese);
        if (_widgetView is not null)
        {
            _widgetView.SetQuote(english, vietnamese);
            FitWidgetHeight();
        }
    }

    private void ToggleTranslation()
    {
        _quoteSettings = _quoteSettings with { ShowTranslation = !_quoteSettings.ShowTranslation };
        _translationItem.Checked = _quoteSettings.ShowTranslation;
        ApplyQuote();
        SaveQuoteSettings();
    }

    private void ShowQuoteSettings()
    {
        using var dialog = new QuoteSettingsDialog(_quoteSettings);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var changedCategories = dialog.ValueFrom(_quoteSettings).Categories != _quoteSettings.Categories;
        _quoteSettings = dialog.ValueFrom(_quoteSettings);
        _translationItem.Checked = _quoteSettings.ShowTranslation;
        if (changedCategories)
        {
            // The sentence on screen may not belong to the new categories, so pick a fitting one right away.
            _quoteRotator.SetCategories(_quoteSettings.Categories);
            ShowNextQuote();
            return;
        }

        ApplyQuote();
        SaveQuoteSettings();
        ConfigureQuoteTimer();
    }

    private void ConfigureQuoteTimer()
    {
        _quoteTimer.Stop();
        if (_quoteSettings.Period is not { } period) return;
        _quoteTimer.Interval = (int)period.TotalMilliseconds;
        _quoteTimer.Start();
    }

    private void SaveQuoteSettings()
    {
        try
        {
            _quoteStore.Save(_quoteSettings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The choices just are not remembered next time.
        }
    }

    private void DisposeQuotes() => _quoteTimer.Dispose();
}
