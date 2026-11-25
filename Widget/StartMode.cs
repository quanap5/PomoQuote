using System.Text.Json.Serialization;

namespace PomoQuote.Widget;

/// <summary>Which window the app opens with.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StartMode
{
    /// <summary>The compact widget (the default, also when nothing was ever saved).</summary>
    Widget,

    /// <summary>The normal window.</summary>
    Normal,

    /// <summary>The mode that was in use when the app was closed.</summary>
    Remember,
}

/// <summary>The start-mode rule, pure so it is tested.</summary>
internal static class StartModePolicy
{
    /// <summary>True when the app opens as the widget: always for Widget, never for Normal, and for Remember when the widget was the last mode.</summary>
    public static bool StartsAsWidget(StartMode startIn, bool lastModeWasWidget) => startIn switch
    {
        StartMode.Normal => false,
        StartMode.Remember => lastModeWasWidget,
        _ => true,
    };

    public static string Caption(StartMode mode) => mode switch
    {
        StartMode.Normal => "Normal window",
        StartMode.Remember => "Remember last",
        _ => "Widget (default)",
    };
}

/// <summary>Where the app is, as far as the user sees: the compact widget on screen, the widget hidden in the tray, or the normal window.</summary>
public enum ShellState
{
    WidgetVisible,
    WidgetHidden,
    NormalWindow,
}

/// <summary>What can happen to <see cref="ShellState"/>.</summary>
public enum ShellAction
{
    /// <summary>Hide to tray (menu item or Ctrl+H).</summary>
    HideToTray,

    /// <summary>Show the widget (tray menu item, a second launch).</summary>
    ShowWidget,

    /// <summary>A left click on the tray icon: hides a visible widget and shows a hidden one.</summary>
    TrayClick,

    /// <summary>A double click on the tray icon: always shows.</summary>
    TrayDoubleClick,

    /// <summary>Ctrl+W, or "Exit widget mode".</summary>
    LeaveWidgetMode,

    /// <summary>Ctrl+W, or "Widget mode".</summary>
    EnterWidgetMode,

    /// <summary>A second launch of the app: shows whatever is there.</summary>
    SecondLaunch,
}

/// <summary>The transitions of <see cref="ShellState"/>: one table, tested, and the form only carries it out.</summary>
internal static class ShellStateMachine
{
    public static ShellState Next(ShellState state, ShellAction action) => (state, action) switch
    {
        (ShellState.WidgetVisible, ShellAction.HideToTray) => ShellState.WidgetHidden,
        (ShellState.WidgetVisible, ShellAction.TrayClick) => ShellState.WidgetHidden,
        (ShellState.WidgetHidden, ShellAction.TrayClick or ShellAction.TrayDoubleClick or ShellAction.ShowWidget or ShellAction.SecondLaunch) => ShellState.WidgetVisible,
        (ShellState.WidgetVisible, ShellAction.TrayDoubleClick or ShellAction.ShowWidget or ShellAction.SecondLaunch) => ShellState.WidgetVisible,
        (ShellState.WidgetVisible or ShellState.WidgetHidden, ShellAction.LeaveWidgetMode) => ShellState.NormalWindow,
        (ShellState.NormalWindow, ShellAction.EnterWidgetMode) => ShellState.WidgetVisible,
        (ShellState.WidgetHidden, ShellAction.EnterWidgetMode) => ShellState.WidgetVisible,
        // The normal window has no hide-to-tray of this kind (minimising is the mascot's), and no widget click does anything there.
        _ => state,
    };
}