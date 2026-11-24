namespace PomoQuote.AiUsage.Claude;

/// <summary>
/// Remembers a value for a few seconds so that several readers in the same poll (the provider asks for the bridge state and
/// whether the program is missing every 10 seconds) read the settings file once. <see cref="Invalidate"/> forgets it at once
/// (after a set-up, upgrade or removal).
/// </summary>
internal sealed class CachedValue<T>
{
    private readonly Func<T> _read;
    private readonly TimeSpan _lifetime;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private T? _value;
    private DateTimeOffset _readAt;
    private bool _has;

    public CachedValue(Func<T> read, TimeSpan lifetime, TimeProvider clock)
    {
        _read = read;
        _lifetime = lifetime;
        _clock = clock;
    }

    public T Get()
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            if (_has && now - _readAt < _lifetime) return _value!;
            _value = _read();
            _readAt = now;
            _has = true;
            return _value;
        }
    }

    public void Invalidate()
    {
        lock (_gate) _has = false;
    }
}