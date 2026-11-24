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
    public void The_real_data_rotates_through_everything_without_a_repeat_in_a_row()
    {
        var all = new BundledQuoteProvider().GetAll();
        var rotator = new QuoteRotator(all, QuoteCategory.All, new Random(2024));

        var firstPass = Enumerable.Range(0, all.Count).Select(_ => rotator.Next()!).ToList();
        Assert.Equal(all.Count, firstPass.Distinct().Count());

        var previous = firstPass[^1];
        for (var i = 0; i < all.Count * 3; i++)
        {
            var next = rotator.Next()!;
            Assert.NotSame(previous, next);
            previous = next;
        }
    }
}
