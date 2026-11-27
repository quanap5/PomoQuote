namespace PomoQuote;

/// <summary>What <see cref="DialogHost"/> needs to know to place the app's dialogs: where the widget is and which screen areas there are.</summary>
internal sealed partial class MainForm : IDialogAnchor
{
    Rectangle? IDialogAnchor.WidgetOnScreen => _widgetMode && Visible && WindowState != FormWindowState.Minimized ? Bounds : null;

    IReadOnlyList<Rectangle> IDialogAnchor.DialogWorkAreas => WorkAreaProvider();
}
