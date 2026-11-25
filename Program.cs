using PomoQuote.AiUsage.Claude;

namespace PomoQuote;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // `PomoQuote.exe --bridge claude ...` is what Claude Code runs as its status line once the bridge is installed:
        // it must start and finish fast and never open a window, so it is handled before anything else is loaded.
        if (args.Length >= 2 && args[0] == "--bridge" && args[1] == "claude") return ClaudeBridge.RunFromProcess(args);

        // One instance only: a second copy would put a second robot and a second tray icon on screen.
        using var singleInstance = new Mutex(initiallyOwned: true, @"Local\PomoQuote.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            // The first copy may be hidden in the tray: ask it to show itself instead of silently doing nothing.
            SingleInstanceSignal.Notify(SingleInstanceSignal.DefaultName);
            return 0;
        }

        // Settings of the former name (HelloWorldBot) are copied over before any store reads its file.
        AppDataMigration.Run();

        ApplicationConfiguration.Initialize();
        using var form = new MainForm();
        using var signal = new SingleInstanceSignal(SingleInstanceSignal.DefaultName, form.ShowFromSecondLaunch);
        Application.Run(form);
        return 0;
    }
}
