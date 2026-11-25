using Microsoft.Win32;
using PomoQuote.AiUsage;
using PomoQuote.Pomodoro;

namespace PomoQuote.Widget;

/// <summary>The widget's theme choice: follow Windows (the default), or always light, or always dark.</summary>
public enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>
/// Every colour the widget draws with, in one place: a theme is a set of tokens, and the widget, its usage area, its small controls and the
/// weather effect take their colours from the one theme in force. <see cref="Dark"/> is the widget's original look, unchanged.
/// </summary>
internal sealed record WidgetTheme
{
    /// <summary>True for the dark theme: the provider logos then use their white variants, and the weather effect keeps its light particles.</summary>
    public required bool IsDark { get; init; }
    public required Color Panel { get; init; }
    public required Color Border { get; init; }
    public required Color PrimaryText { get; init; }
    public required Color SecondaryText { get; init; }
    public required Color Track { get; init; }
    public required Color Card { get; init; }
    public required Color QuoteMark { get; init; }
    public required Color QuoteEnglish { get; init; }
    public required Color QuoteVietnamese { get; init; }
    public required Color PhaseWork { get; init; }
    public required Color PhaseShortBreak { get; init; }
    public required Color PhaseLongBreak { get; init; }
    public required Color Quota { get; init; }
    public required Color Warning { get; init; }
    public required Color Critical { get; init; }

    /// <summary>The colour of the clock mark and the amber ring of a stale reading.</summary>
    public required Color Stale { get; init; }

    /// <summary>The hands of the clock on the amber stale badge.</summary>
    public required Color OnAmber { get; init; }

    /// <summary>The scrollbar thumb.</summary>
    public required Color Scroll { get; init; }

    /// <summary>The dotted ring of keyboard focus.</summary>
    public required Color Focus { get; init; }

    /// <summary>The face and the outline of the Expand/Collapse button (the face is the hot colour under the pointer).</summary>
    public required Color ButtonHot { get; init; }
    public required Color ButtonOutline { get; init; }
    public required Color Glyph { get; init; }
    public required Color GlyphHot { get; init; }

    /// <summary>The tile that stands in for a missing logo, and the letter on it.</summary>
    public required Color TileFill { get; init; }
    public required Color TileText { get; init; }

    /// <summary>The shade the pointer leaves on the logo selector.</summary>
    public required Color SelectorHot { get; init; }

    /// <summary>The provider logo file to use on this panel.</summary>
    public IconBackground LogoBackground => IsDark ? IconBackground.Dark : IconBackground.Light;

    public Color Phase(PomodoroPhase phase) => phase switch
    {
        // Timer colours are never red, orange or yellow: those are for quota warnings only.
        PomodoroPhase.Work => PhaseWork,
        PomodoroPhase.ShortBreak => PhaseShortBreak,
        _ => PhaseLongBreak,
    };

    public static WidgetTheme Dark { get; } = new()
    {
        IsDark = true,
        Panel = Color.FromArgb(28, 32, 40),
        Border = Color.FromArgb(84, 94, 110),
        PrimaryText = Color.FromArgb(244, 246, 248),
        SecondaryText = Color.FromArgb(190, 200, 212),
        Track = Color.FromArgb(62, 70, 84),
        Card = Color.FromArgb(40, 50, 66),
        QuoteMark = Color.FromArgb(70, 160, 180, 210),
        QuoteEnglish = Color.FromArgb(150, 236, 160),
        QuoteVietnamese = Color.FromArgb(206, 216, 228),
        PhaseWork = Color.FromArgb(138, 130, 244),
        PhaseShortBreak = Color.FromArgb(150, 204, 112),
        PhaseLongBreak = Color.FromArgb(104, 168, 240),
        Quota = Color.FromArgb(70, 178, 190),
        Warning = Color.FromArgb(240, 180, 80),
        Critical = Color.FromArgb(240, 98, 82),
        Stale = Color.FromArgb(240, 180, 80),
        OnAmber = Color.FromArgb(40, 34, 20),
        Scroll = Color.FromArgb(150, 162, 178),
        Focus = Color.FromArgb(150, 190, 240),
        ButtonHot = Color.FromArgb(52, 60, 74),
        ButtonOutline = Color.FromArgb(84, 94, 110),
        Glyph = Color.FromArgb(196, 206, 218),
        GlyphHot = Color.White,
        TileFill = Color.FromArgb(70, 80, 96),
        TileText = Color.FromArgb(235, 240, 246),
        SelectorHot = Color.FromArgb(34, 255, 255, 255),
    };

    public static WidgetTheme Light { get; } = new()
    {
        IsDark = false,
        Panel = Color.FromArgb(244, 246, 249),
        Border = Color.FromArgb(186, 194, 206),
        PrimaryText = Color.FromArgb(24, 28, 36),
        SecondaryText = Color.FromArgb(78, 88, 104),
        Track = Color.FromArgb(212, 218, 228),
        Card = Color.FromArgb(226, 232, 242),
        QuoteMark = Color.FromArgb(80, 70, 90, 130),
        QuoteEnglish = Color.FromArgb(20, 108, 46),
        QuoteVietnamese = Color.FromArgb(48, 56, 70),
        PhaseWork = Color.FromArgb(84, 70, 200),
        PhaseShortBreak = Color.FromArgb(40, 128, 60),
        PhaseLongBreak = Color.FromArgb(28, 96, 196),
        Quota = Color.FromArgb(0, 126, 138),
        Warning = Color.FromArgb(176, 106, 0),
        Critical = Color.FromArgb(200, 48, 34),
        Stale = Color.FromArgb(176, 106, 0),
        OnAmber = Color.FromArgb(255, 244, 224),
        Scroll = Color.FromArgb(120, 132, 150),
        Focus = Color.FromArgb(36, 96, 196),
        ButtonHot = Color.FromArgb(220, 226, 236),
        ButtonOutline = Color.FromArgb(160, 170, 186),
        Glyph = Color.FromArgb(70, 80, 96),
        GlyphHot = Color.FromArgb(16, 20, 28),
        TileFill = Color.FromArgb(200, 208, 220),
        TileText = Color.FromArgb(30, 38, 50),
        SelectorHot = Color.FromArgb(30, 0, 0, 0),
    };

    /// <summary>The theme to draw with: the chosen one, or in System mode the one Windows uses (<paramref name="systemIsLight"/>).</summary>
    public static WidgetTheme Resolve(ThemeMode mode, bool systemIsLight) => mode switch
    {
        ThemeMode.Light => Light,
        ThemeMode.Dark => Dark,
        _ => systemIsLight ? Light : Dark,
    };
}

/// <summary>Reads whether Windows uses its light theme for apps.</summary>
internal static class SystemTheme
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>True when apps are set to the light theme. A missing or odd value means light, which is what Windows does when it is not set.</summary>
    public static bool IsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }
}