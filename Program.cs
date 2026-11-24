namespace PomoQuote;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // One instance only: a second copy would put a second robot and a second tray icon on screen.
        using var singleInstance = new Mutex(initiallyOwned: true, @"Local\PomoQuote.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance) return;

        // Settings of the former name (HelloWorldBot) are copied over before any store reads its file.
        AppDataMigration.Run();

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
