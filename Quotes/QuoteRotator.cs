namespace PomoQuote.Quotes;

/// <summary>
/// Picks the next sentence. Each chosen category has its own shuffled deck; the category is drawn by weight (see <see cref="Weight"/>), then
/// the next card of its deck, so a category with thousands of sentences does not drown the smaller ones. A category shows every one of its
/// sentences once before repeating any, and the same sentence never appears twice in a row (also across the end of a deck, across a change of
/// categories, and across a restart when the last one shown is passed in). With one category chosen it is a plain shuffled pass through it.
/// The random source is injected so the order can be tested.
/// </summary>
internal sealed class QuoteRotator
{
    private sealed class Deck(QuoteCategory category, Quote[] pool)
    {
        public QuoteCategory Category { get; } = category;
        public Quote[] Pool { get; } = pool;
        public Queue<Quote> Upcoming { get; } = new();
    }

    /// <summary>
    /// How often a category comes up when several are chosen, relative to each other (percentages when all are chosen). Conversation keeps about
    /// a third; the rest share the remainder, so the new categories are seen although conversation has by far the most sentences.
    /// </summary>
    public static int Weight(QuoteCategory category) => category switch
    {
        QuoteCategory.Conversation => 35,
        QuoteCategory.Idiom => 12,
        QuoteCategory.Proverb => 12,
        QuoteCategory.Quote => 10,
        QuoteCategory.Maxim => 10,
        QuoteCategory.Expression => 8,
        QuoteCategory.PhrasalVerb => 8,
        QuoteCategory.Motivation => 5,
        _ => 1,
    };

    private readonly IReadOnlyList<Quote> _all;
    private readonly Random _random;
    private List<Deck> _decks;
    private Quote? _last;

    public QuoteRotator(IReadOnlyList<Quote> all, QuoteCategory categories, Random random, string? lastEnglish = null)
    {
        _all = all;
        _random = random;
        _decks = Build(categories);
        _last = lastEnglish is null ? null : all.FirstOrDefault(quote => quote.English == lastEnglish);
    }

    /// <summary>The sentence returned by the last <see cref="Next"/>, or the one restored from a previous run.</summary>
    public Quote? Current => _last;

    /// <summary>Number of sentences in the chosen categories.</summary>
    public int PoolSize => _decks.Sum(deck => deck.Pool.Length);

    /// <summary>Changes the categories; every pass starts again from a fresh shuffle.</summary>
    public void SetCategories(QuoteCategory categories) => _decks = Build(categories);

    /// <summary>The next sentence, or null when the chosen categories hold none.</summary>
    public Quote? Next()
    {
        if (_decks.Count == 0) return null;
        var deck = Draw();
        if (deck.Upcoming.Count == 0) Refill(deck);
        _last = deck.Upcoming.Dequeue();
        return _last;
    }

    private List<Deck> Build(QuoteCategory categories) => _all
        .Where(quote => (quote.Category & categories) != 0)
        .GroupBy(quote => quote.Category)
        .OrderBy(group => (int)group.Key)
        .Select(group => new Deck(group.Key, group.ToArray()))
        .ToList();

    private Deck Draw()
    {
        // A deck that holds nothing but the sentence just shown is skipped while there is another one.
        var candidates = _decks.Where(deck => !(deck.Pool.Length == 1 && ReferenceEquals(deck.Pool[0], _last))).ToList();
        if (candidates.Count == 0) candidates = _decks;
        if (candidates.Count == 1) return candidates[0];

        var pick = _random.Next(candidates.Sum(deck => Weight(deck.Category)));
        foreach (var deck in candidates)
        {
            pick -= Weight(deck.Category);
            if (pick < 0) return deck;
        }

        return candidates[^1];
    }

    private void Refill(Deck deck)
    {
        var order = (Quote[])deck.Pool.Clone();
        for (var i = order.Length - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        // A pass must not open with the sentence that was just shown.
        if (order.Length > 1 && ReferenceEquals(order[0], _last))
        {
            var j = _random.Next(1, order.Length);
            (order[0], order[j]) = (order[j], order[0]);
        }

        foreach (var quote in order) deck.Upcoming.Enqueue(quote);
    }
}
