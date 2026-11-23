namespace HelloWorldBot;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // One instance only: a second copy would put a second robot and a second tray icon on screen.
        using var singleInstance = new Mutex(initiallyOwned: true, @"Local\HelloWorldBot.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
