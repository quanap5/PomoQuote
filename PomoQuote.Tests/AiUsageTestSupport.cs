using System.Text.Json;
using PomoQuote.AiUsage;

namespace PomoQuote.Tests;

/// <summary>A scripted stand-in for `codex app-server`: records what was sent and answers from a handler.</summary>
internal sealed class FakeSession : IJsonLineSession
{
    private readonly Func<string, string[]> _onSend;
    private readonly Queue<string> _pending = new();
    private readonly bool _hangWhenEmpty;

    public FakeSession(Func<string, string[]> onSend, bool hangWhenEmpty = false)
    {
        _onSend = onSend;
        _hangWhenEmpty = hangWhenEmpty;
    }

    public List<string> Sent { get; } = [];

    public bool Disposed { get; private set; }

    public Task SendAsync(string line, CancellationToken cancellationToken)
    {
        Sent.Add(line);
        foreach (var reply in _onSend(line)) _pending.Enqueue(reply);
        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count > 0) return _pending.Dequeue();
        if (_hangWhenEmpty) await Task.Delay(Timeout.Infinite, cancellationToken);
        return null; // the process ended
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeSessionFactory : IJsonLineSessionFactory
{
    private readonly Func<FakeSession> _create;

    public FakeSessionFactory(Func<FakeSession> create) => _create = create;

    public FakeSession? Last { get; private set; }

    public int Starts { get; private set; }

    public IJsonLineSession Start()
    {
        Starts++;
        return Last = _create();
    }
}

internal sealed class ThrowingFactory : IJsonLineSessionFactory
{
    private readonly Exception _exception;

    public ThrowingFactory(Exception exception) => _exception = exception;

    public IJsonLineSession Start() => throw _exception;
}

internal static class CodexSamples
{
    /// <summary>The shape verified against the real Codex 0.171.0 (numbers and ids here are made up).</summary>
    public const string RateLimitsResult = """
        {"ordinaryUsageAllowed":true,
         "rateLimits":{"limitId":"codex","limitName":null,"normalModelSlug":null,
           "primary":{"usedPercent":21,"windowDurationMins":300,"resetsAt":1795510747},
           "secondary":{"usedPercent":27,"windowDurationMins":10080,"resetsAt":1795771509},
           "credits":{"hasCredits":false,"unlimited":false,"balance":"0"},
           "individualLimit":null,"spendControlReached":false,"planType":"plus","rateLimitReachedType":null},
         "rateLimitsByLimitId":{"codex":{"limitId":"codex"}},
         "rateLimitResetCredits":{"availableCount":0,"credits":[]},
         "accountId":"acct-0000-test","rateLimitUpsell":null}
        """;

    public static string Reply(int id, string resultJson) => $$"""{"id":{{id}},"result":{{resultJson}}}""";

    /// <summary>A server that answers initialize and the rate-limit read like the real one.</summary>
    public static string[] Healthy(string line, string resultJson = RateLimitsResult)
    {
        using var doc = JsonDocument.Parse(line);
        if (!doc.RootElement.TryGetProperty("id", out var id)) return [];
        var number = id.GetInt32();
        return number == 1
            ? ["""{"method":"remoteControl/status/changed","params":{}}""", Reply(1, """{"userAgent":"x","codexHome":"x","platformFamily":"windows","platformOs":"windows"}""")]
            : ["not json at all", """{"method":"account/updated","params":{}}""", Reply(number, resultJson)];
    }
}
