namespace PomoQuote.Quotes;

/// <summary>
/// Picks the next sentence: a shuffled pass through every sentence of the chosen categories before any
/// repeats, and never the same sentence twice in a row (also across the end of one pass and the start of the
/// next, across a change of categories, and across a restart when the last one shown is passed in).
/// The random source is injected so the order can be tested.
/// </summary>
internal sealed class QuoteRotator
{
    private readonly IReadOnlyList<Quote> _all;
    private readonly Random _random;
    private readonly Queue<Quote> _upcoming = new();
    private List<Quote> _pool;
    private Quote? _last;

    public QuoteRotator(IReadOnlyList<Quote> all, QuoteCategory categories, Random random, string? lastEnglish = null)
    {
        _all = all;
        _random = random;
        _pool = Filter(categories);
        _last = lastEnglish is null ? null : all.FirstOrDefault(quote => quote.English == lastEnglish);
    }

    /// <summary>The sentence returned by the last <see cref="Next"/>, or the one restored from a previous run.</summary>
    public Quote? Current => _last;

    /// <summary>Number of sentences in the chosen categories.</summary>
    public int PoolSize => _pool.Count;

    /// <summary>Changes the categories; the pass starts again from a fresh shuffle.</summary>
    public void SetCategories(QuoteCategory categories)
    {
        _pool = Filter(categories);
        _upcoming.Clear();
    }

    /// <summary>The next sentence, or null when the chosen categories hold none.</summary>
    public Quote? Next()
    {
        if (_pool.Count == 0) return null;
        if (_upcoming.Count == 0) Refill();
        _last = _upcoming.Dequeue();
        return _last;
    }

    private List<Quote> Filter(QuoteCategory categories) => _all.Where(quote => (quote.Category & categories) != 0).ToList();

    private void Refill()
    {
        var order = _pool.ToArray();
        for (var i = order.Length - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        // The pass must not open with the sentence that just closed the previous one.
        if (order.Length > 1 && ReferenceEquals(order[0], _last))
        {
            var j = _random.Next(1, order.Length);
            (order[0], order[j]) = (order[j], order[0]);
        }

        foreach (var quote in order) _upcoming.Enqueue(quote);
    }
}
