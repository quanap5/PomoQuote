namespace PomoQuote;

/// <summary>
/// Lets a second launch tell the first one to show itself: the first instance listens on a named event, the second sets it and exits.
/// The name is a parameter so tests use their own.
/// </summary>
internal sealed class SingleInstanceSignal : IDisposable
{
    public const string DefaultName = @"Local\PomoQuote.ShowRequest";

    private readonly EventWaitHandle _event;
    private readonly RegisteredWaitHandle? _registration;

    /// <summary>Starts listening; <paramref name="onSignal"/> runs on a thread-pool thread each time another launch signals.</summary>
    public SingleInstanceSignal(string name, Action onSignal)
    {
        _event = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        _registration = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) => onSignal(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Signals the running instance; false when there is none listening.</summary>
    public static bool Notify(string name)
    {
        try
        {
            using var existing = EventWaitHandle.OpenExisting(name);
            return existing.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _event.Dispose();
    }
}