using PomoQuote.Quotes;

namespace PomoQuote.Tests;

public sealed class QuoteRotatorTests
{
    private static Quote Make(string english, QuoteCategory category = QuoteCategory.Motivation) =>
        new(english, "vi " + english, category, null, "test", "CC0");

    private static IReadOnlyList<Quote> Pool(int count, QuoteCategory category = QuoteCategory.Motivation) =>
        Enumerable.Range(0, count).Select(i => Make($"Sentence {i}", category)).ToList();

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(57)]
    public void The_same_sentence_never_appears_twice_in_a_row_over_many_passes(int size)
    {
        foreach (var seed in Enumerable.Range(0, 25))
        {
            var rotator = new QuoteRotator(Pool(size), QuoteCategory.All, new Random(seed));
            Quote? previous = null;
            for (var i = 0; i < size * 12; i++)
            {
                var next = rotator.Next()!;
                Assert.NotSame(previous, next);
                previous = next;
            }
        }
    }

    [Fact]
    public void Every_sentence_is_shown_once_before_any_repeats()
    {
        var pool = Pool(40);
        var rotator = new QuoteRotator(pool, QuoteCategory.All, new Random(7));

        for (var pass = 0; pass < 4; pass++)
        {
            var seen = Enumerable.Range(0, pool.Count).Select(_ => rotator.Next()!).ToList();
            Assert.Equal(pool.Count, seen.Distinct().Count());
            Assert.True(pool.All(seen.Contains));
        }
    }

    [Fact]
    public void Passes_come_in_different_orders()
    {
        var rotator = new QuoteRotator(Pool(30), QuoteCategory.All, new Random(1));

        var first = Enumerable.Range(0, 30).Select(_ => rotator.Next()!.English).ToList();
        var second = Enumerable.Range(0, 30).Select(_ => rotator.Next()!.English).ToList();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void The_same_seed_gives_the_same_order()
    {
        var a = new QuoteRotator(Pool(20), QuoteCategory.All, new Random(99));
        var b = new QuoteRotator(Pool(20), QuoteCategory.All, new Random(99));

        Assert.Equal(
            Enumerable.Range(0, 60).Select(_ => a.Next()!.English),
            Enumerable.Range(0, 60).Select(_ => b.Next()!.English));
    }

    [Fact]
    public void Only_the_chosen_categories_are_shown()
    {
        var all = Pool(10, QuoteCategory.Motivation).Concat(Pool(10, QuoteCategory.Idiom).Select(q => q with { English = "I " + q.English })).Concat(
            Pool(10, QuoteCategory.Conversation).Select(q => q with { English = "C " + q.English })).ToList();
        var rotator = new QuoteRotator(all, QuoteCategory.Idiom | QuoteCategory.Conversation, new Random(3));

        for (var i = 0; i < 100; i++) Assert.NotEqual(QuoteCategory.Motivation, rotator.Next()!.Category);
        Assert.Equal(20, rotator.PoolSize);
    }

    [Fact]
    public void Changing_the_categories_applies_at_once_and_still_avoids_an_immediate_repeat()
    {
        var all = Pool(5, QuoteCategory.Motivation).Concat(Pool(5, QuoteCategory.Idiom).Select(q => q with { English = "I " + q.English })).ToList();
        var rotator = new QuoteRotator(all, QuoteCategory.Motivation, new Random(5));
        for (var i = 0; i < 7; i++) rotator.Next();

        rotator.SetCategories(QuoteCategory.Idiom);

        var shown = Enumerable.Range(0, 5).Select(_ => rotator.Next()!).ToList();
        Assert.All(shown, quote => Assert.Equal(QuoteCategory.Idiom, quote.Category));
        Assert.Equal(5, shown.Distinct().Count());
    }

    [Fact]
    public void The_last_sentence_of_the_previous_run_does_not_open_the_next_run()
    {
        var pool = Pool(6);
        foreach (var seed in Enumerable.Range(0, 200))
        {
            var rotator = new QuoteRotator(pool, QuoteCategory.All, new Random(seed), lastEnglish: "Sentence 3");

            Assert.Equal("Sentence 3", rotator.Current!.English);
            Assert.NotEqual("Sentence 3", rotator.Next()!.English);
        }
    }

    [Fact]
    public void Different_seeds_do_not_always_start_with_the_same_sentence()
    {
        var firsts = Enumerable.Range(0, 60)
            .Select(seed => new QuoteRotator(Pool(30), QuoteCategory.All, new Random(seed)).Next()!.English)
            .Distinct()
            .Count();

        Assert.True(firsts > 10, $"only {firsts} different first sentences");
    }

    [Fact]
    public void An_unknown_last_sentence_is_ignored()
    {
        var rotator = new QuoteRotator(Pool(4), QuoteCategory.All, new Random(1), lastEnglish: "Not in the list");

        Assert.Null(rotator.Current);
        Assert.NotNull(rotator.Next());
    }

    [Fact]
    public void A_single_sentence_is_shown_again_and_an_empty_selection_gives_nothing()
    {
        var one = new QuoteRotator(Pool(1), QuoteCategory.All, new Random(1));
        var none = new QuoteRotator(Pool(3, QuoteCategory.Motivation), QuoteCategory.Idiom, new Random(1));

        Assert.Equal("Sentence 0", one.Next()!.English);
        Assert.Equal("Sentence 0", one.Next()!.English);
        Assert.Null(none.Next());
        Assert.Equal(0, none.PoolSize);
    }

    [Fact]
    public void The_real_data_shows_every_sentence_of_a_category_before_repeating_one_of_it_and_never_one_twice_in_a_row()
    {
        var all = new BundledQuoteProvider().GetAll();
        var sizes = all.GroupBy(q => q.Category).ToDictionary(g => g.Key, g => g.Count());
        var rotator = new QuoteRotator(all, QuoteCategory.All, new Random(2024));
        var seen = sizes.Keys.ToDictionary(c => c, _ => new HashSet<Quote>(ReferenceEqualityComparer.Instance));
        Quote? previous = null;

        for (var i = 0; i < all.Count * 3; i++)
        {
            var next = rotator.Next()!;
            Assert.NotSame(previous, next);
            previous = next;
            var deck = seen[next.Category];
            if (deck.Count == sizes[next.Category]) deck.Clear(); // the category's pass is complete: a new one starts
            Assert.True(deck.Add(next), $"shown twice before the rest of {next.Category}: {next.English}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void The_categories_come_up_by_their_weights_however_many_sentences_each_has(int seed)
    {
        // conversation has thousands of sentences, the others only a few: the weights decide, not the counts
        var pool = Pool(5000, QuoteCategory.Conversation).Select(q => q with { English = "C " + q.English })
            .Concat(Pool(20, QuoteCategory.Idiom).Select(q => q with { English = "I " + q.English }))
            .Concat(Pool(20, QuoteCategory.Proverb).Select(q => q with { English = "P " + q.English }))
            .Concat(Pool(20, QuoteCategory.Quote).Select(q => q with { English = "Q " + q.English }))
            .Concat(Pool(20, QuoteCategory.Maxim).Select(q => q with { English = "M " + q.English }))
            .Concat(Pool(20, QuoteCategory.Expression).Select(q => q with { English = "E " + q.English }))
            .Concat(Pool(20, QuoteCategory.PhrasalVerb).Select(q => q with { English = "V " + q.English }))
            .Concat(Pool(20, QuoteCategory.Motivation))
            .ToList();
        var rotator = new QuoteRotator(pool, QuoteCategory.All, new Random(seed));
        const int draws = 40000;

        var counts = Enumerable.Range(0, draws).Select(_ => rotator.Next()!.Category).GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());

        foreach (var category in new[] { QuoteCategory.Conversation, QuoteCategory.Idiom, QuoteCategory.Proverb, QuoteCategory.Quote, QuoteCategory.Maxim, QuoteCategory.Expression, QuoteCategory.PhrasalVerb, QuoteCategory.Motivation })
        {
            var share = 100.0 * counts[category] / draws;
            Assert.InRange(share, QuoteRotator.Weight(category) - 1.0, QuoteRotator.Weight(category) + 1.0);
        }
    }

    [Fact]
    public void The_weights_add_up_to_one_hundred_and_conversation_keeps_about_a_third()
    {
        var categories = Enum.GetValues<QuoteCategory>().Where(c => c is not (QuoteCategory.None or QuoteCategory.All)).ToList();

        Assert.Equal(100, categories.Sum(QuoteRotator.Weight));
        Assert.Equal(35, QuoteRotator.Weight(QuoteCategory.Conversation));
        Assert.All(categories, c => Assert.True(QuoteRotator.Weight(c) >= 5));
    }

    [Fact]
    public void Only_the_chosen_categories_share_the_draws_and_their_weights_are_relative()
    {
        var pool = Pool(10, QuoteCategory.Conversation).Select(q => q with { English = "C " + q.English })
            .Concat(Pool(10, QuoteCategory.Idiom).Select(q => q with { English = "I " + q.English })).ToList();
        var rotator = new QuoteRotator(pool, QuoteCategory.Conversation | QuoteCategory.Idiom, new Random(8));

        var idioms = Enumerable.Range(0, 20000).Count(_ => rotator.Next()!.Category == QuoteCategory.Idiom);

        Assert.InRange(idioms / 20000.0, 12 / 47.0 - 0.02, 12 / 47.0 + 0.02);
    }

    [Fact]
    public void Two_categories_of_one_sentence_each_alternate_without_an_immediate_repeat()
    {
        var pool = new[] { Make("a", QuoteCategory.Idiom), Make("b", QuoteCategory.Proverb) };
        var rotator = new QuoteRotator(pool, QuoteCategory.All, new Random(3));
        Quote? previous = null;

        for (var i = 0; i < 50; i++)
        {
            var next = rotator.Next()!;
            Assert.NotSame(previous, next);
            previous = next;
        }
    }
}
