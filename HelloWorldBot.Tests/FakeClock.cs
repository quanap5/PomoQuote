namespace HelloWorldBot.Tests;

/// <summary>A hand-wound clock: time only moves when a test calls <see cref="Advance"/>.</summary>
internal sealed class FakeClock : TimeProvider
{
    private long _timestamp = 1_000;
    private DateTimeOffset _now = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);

    public override long TimestampFrequency => 1_000; // one timestamp unit per millisecond

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override long GetTimestamp() => _timestamp;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan span)
    {
        _timestamp += (long)span.TotalMilliseconds;
        _now += span;
    }

    /// <summary>Moves only the calendar (e.g. past midnight), not the monotonic timestamp.</summary>
    public void SetCalendar(DateTimeOffset now) => _now = now;
}
