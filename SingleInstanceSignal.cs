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
        if (_registration is not null)
        {
            // Unregister(null) returns at once and the pool keeps its own handle to the event until a callback in flight is done; waiting for the
            // unregistration to complete means the named event is really gone when Dispose returns.
            using var done = new ManualResetEvent(false);
            if (_registration.Unregister(done)) done.WaitOne(TimeSpan.FromSeconds(2));
        }

        _event.Dispose();
    }
}