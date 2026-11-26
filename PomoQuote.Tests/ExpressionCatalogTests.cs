using System.Text.Json;
using System.Text.Json.Nodes;
using PomoQuote.Quotes;

namespace PomoQuote.Tests;

/// <summary>The rules of the expression data files: an incomplete or wrongly licensed record is refused, never skipped.</summary>
public sealed class ExpressionCatalogTests
{
    private static JsonObject Idiom(string id = "idiom-1") => new()
    {
        ["id"] = id,
        ["type"] = "idiom",
        ["lang"] = "en",
        ["text"] = "break the ice",
        ["meanings"] = new JsonArray(new JsonObject { ["lang"] = "en", ["text"] = "To start a conversation among strangers." }),
        ["translations"] = new JsonArray(new JsonObject { ["lang"] = "vi", ["text"] = "phá vỡ sự ngại ngùng", ["status"] = "machine_unreviewed", ["by"] = "a model", ["license"] = "CC-BY-SA-4.0" }),
        ["examples"] = new JsonArray(new JsonObject { ["en"] = "A joke helped to break the ice.", ["vi"] = "Một câu đùa giúp mọi người bớt ngại." }),
        ["source"] = new JsonObject { ["dataset"] = "English Wiktionary", ["url"] = "https://en.wiktionary.org/wiki/break_the_ice", ["retrieved"] = "2026-11-26", ["license"] = "CC-BY-SA-4.0", ["attribution_text"] = "From English Wiktionary, by its contributors" },
    };

    private static JsonObject Saying(string type = "quote", string id = "quote-1") => new()
    {
        ["id"] = id,
        ["type"] = type,
        ["lang"] = "en",
        ["text"] = "Well done is better than well said.",
        ["author"] = new JsonObject { ["name"] = "Benjamin Franklin", ["died"] = 1790 },
        ["attribution"] = new JsonObject { ["status"] = "verified", ["work"] = "Poor Richard's Almanack" },
        ["translations"] = new JsonArray(new JsonObject { ["lang"] = "vi", ["text"] = "Làm tốt hơn nói hay.", ["status"] = "machine_unreviewed", ["by"] = "a model", ["license"] = "CC0-1.0" }),
        ["source"] = new JsonObject { ["dataset"] = "The author's work", ["url"] = "https://example.org/work", ["retrieved"] = "2026-11-26", ["license"] = "Public-Domain", ["attribution_text"] = "Benjamin Franklin, Poor Richard's Almanack" },
    };

    private static Quote One(JsonObject record) => Assert.Single(ExpressionCatalog.Parse(record.ToJsonString()));

    private static JsonException Refused(string lines) => Assert.Throws<JsonException>(() => ExpressionCatalog.Parse(lines));

    private static JsonException Refused(JsonObject record) => Refused(record.ToJsonString());

    /// <summary>The object holding the last step of a path such as "translations.0.status", and that step.</summary>
    private static (JsonObject Parent, string Name) Locate(JsonObject record, string path)
    {
        JsonNode node = record;
        var steps = path.Split('.');
        foreach (var step in steps[..^1]) node = int.TryParse(step, out var index) ? node[index]! : node[step]!;
        return (node.AsObject(), steps[^1]);
    }

    [Fact]
    public void A_complete_record_becomes_a_sentence_with_its_meaning_example_and_credit()
    {
        var quote = One(Idiom());

        Assert.Equal("break the ice", quote.English);
        Assert.Equal("phá vỡ sự ngại ngùng", quote.Vietnamese);
        Assert.Equal(QuoteCategory.Idiom, quote.Category);
        Assert.Equal("English Wiktionary", quote.Source);
        Assert.Equal("CC-BY-SA-4.0", quote.License);
        Assert.Null(quote.Author);
        Assert.Null(quote.TatoebaId);
        var info = quote.Info!;
        Assert.Equal("idiom-1", info.Id);
        Assert.Equal(TranslationStatus.MachineUnreviewed, info.Status);
        Assert.Equal("https://en.wiktionary.org/wiki/break_the_ice", info.SourceUrl);
        Assert.Equal("machine translation, not reviewed", info.Badge);
        Assert.Equal("From English Wiktionary, by its contributors (CC-BY-SA-4.0).", quote.Attribution);
        Assert.Equal(
            "Meaning: To start a conversation among strangers.\nExample: A joke helped to break the ice.\nMột câu đùa giúp mọi người bớt ngại.\n" +
            "Vietnamese: machine translation, not reviewed.\nFrom English Wiktionary, by its contributors (CC-BY-SA-4.0).",
            quote.Details);
    }

    [Theory]
    [InlineData("idiom", QuoteCategory.Idiom)]
    [InlineData("proverb", QuoteCategory.Proverb)]
    [InlineData("expression", QuoteCategory.Expression)]
    [InlineData("phrasal_verb", QuoteCategory.PhrasalVerb)]
    public void Each_type_with_a_meaning_goes_to_its_category(string type, QuoteCategory category)
    {
        var record = Idiom();
        record["type"] = type;

        Assert.Equal(category, One(record).Category);
    }

    [Theory]
    [InlineData("quote", QuoteCategory.Quote)]
    [InlineData("maxim", QuoteCategory.Maxim)]
    public void Quotes_and_maxims_name_their_author_and_work(string type, QuoteCategory category)
    {
        var quote = One(Saying(type));

        Assert.Equal(category, quote.Category);
        Assert.Equal("Benjamin Franklin", quote.Author);
        Assert.Equal("Poor Richard's Almanack", quote.Info!.Work);
        Assert.Contains("— Benjamin Franklin, Poor Richard's Almanack", quote.Details);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("type")]
    [InlineData("lang")]
    [InlineData("text")]
    [InlineData("meanings")]
    [InlineData("meanings.0.text")]
    [InlineData("translations")]
    [InlineData("translations.0.lang")]
    [InlineData("translations.0.text")]
    [InlineData("translations.0.status")]
    [InlineData("translations.0.license")]
    [InlineData("translations.0.by")]
    [InlineData("source")]
    [InlineData("source.dataset")]
    [InlineData("source.url")]
    [InlineData("source.retrieved")]
    [InlineData("source.license")]
    [InlineData("source.attribution_text")]
    public void A_record_missing_a_required_part_or_holding_it_blank_is_refused(string path)
    {
        var removed = Idiom();
        var (parent, name) = Locate(removed, path);
        Assert.True(parent.Remove(name), path);
        Assert.Contains("data.jsonl line 1:", Refused(removed).Message);

        var blank = Idiom();
        (parent, name) = Locate(blank, path);
        if (parent[name] is JsonValue) parent[name] = "  ";
        else parent[name] = new JsonArray();
        Assert.Contains("data.jsonl line 1:", Refused(blank).Message);
    }

    [Theory]
    [InlineData("author")]
    [InlineData("author.name")]
    [InlineData("author.died")]
    [InlineData("attribution")]
    [InlineData("attribution.status")]
    [InlineData("attribution.work")]
    public void A_quote_without_a_proven_author_is_refused(string path)
    {
        foreach (var type in new[] { "quote", "maxim" })
        {
            var record = Saying(type);
            var (parent, name) = Locate(record, path);
            Assert.True(parent.Remove(name), path);
            Refused(record);
        }
    }

    [Theory]
    [InlineData(1955, true)]
    [InlineData(1956, false)]
    [InlineData(2020, false)]
    public void An_author_must_have_died_long_enough_ago(int died, bool accepted)
    {
        var record = Saying();
        record["author"]!["died"] = died;

        if (accepted) One(record);
        else Assert.Contains("1955", Refused(record).Message);
    }

    [Theory]
    [InlineData("disputed")]
    [InlineData("misattributed")]
    [InlineData("unverified")]
    public void Only_a_verified_attribution_is_accepted(string status)
    {
        var record = Saying();
        record["attribution"]!["status"] = status;

        Assert.Contains("verified", Refused(record).Message);
    }

    [Theory]
    [InlineData("type", "poem", "unknown type")]
    [InlineData("lang", "fr", "English")]
    [InlineData("translations.0.status", "good", "unknown translation status")]
    [InlineData("source.license", "CC-BY-NC-4.0", "unknown licence")]
    [InlineData("source.license", "GPL-3.0", "unknown licence")]
    [InlineData("translations.0.license", "CC-BY-2.0-FR", "cannot carry")]
    [InlineData("translations.0.license", "CC0-1.0", "cannot carry")]
    public void Unknown_values_and_a_second_licence_in_a_record_are_refused(string path, string value, string expected)
    {
        var record = Idiom();
        var (parent, name) = Locate(record, path);
        parent[name] = value;

        Assert.Contains(expected, Refused(record).Message);
    }

    [Theory]
    [InlineData("CC0-1.0", true)]
    [InlineData("Public-Domain", true)]
    [InlineData("CC-BY-SA-4.0", false)]
    public void Public_domain_words_take_a_public_domain_or_CC0_translation(string license, bool accepted)
    {
        var record = Saying();
        record["translations"]![0]!["license"] = license;

        if (accepted) One(record);
        else Refused(record);
    }

    [Theory]
    [InlineData("source", "Source")]
    [InlineData("sampled_ok", "SampledOk")]
    [InlineData("human_reviewed", "HumanReviewed")]
    public void A_translation_from_the_source_or_reviewed_carries_no_warning(string status, string expected)
    {
        var record = Idiom();
        record["translations"]![0]!["status"] = status;
        if (status == "source") record["translations"]![0]!.AsObject().Remove("by"); // a person's translation from the source needs no drafter

        var quote = One(record);

        Assert.Equal(expected, quote.Info!.Status.ToString());
        Assert.Null(quote.Info.Badge);
        Assert.DoesNotContain("not reviewed", quote.Details);
    }

    [Fact]
    public void One_file_holds_one_licence()
    {
        var error = Refused(Idiom().ToJsonString() + "\n" + Saying().ToJsonString());

        Assert.Contains("line 2", error.Message);
        Assert.Contains("one licence", error.Message);
    }

    [Fact]
    public void An_id_is_used_once_in_a_file_and_across_files()
    {
        Assert.Contains("duplicate id", Refused(Idiom().ToJsonString() + "\n" + Idiom().ToJsonString()).Message);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        ExpressionCatalog.Parse(new StringReader(Idiom().ToJsonString()), "a.jsonl", ids);
        var error = Assert.Throws<JsonException>(() => ExpressionCatalog.Parse(new StringReader(Idiom().ToJsonString()), "b.jsonl", ids));
        Assert.Contains("b.jsonl line 1", error.Message);
    }

    [Fact]
    public void Blank_lines_are_ignored_and_a_broken_line_is_named()
    {
        Assert.Equal(2, ExpressionCatalog.Parse("\n" + Idiom("a").ToJsonString() + "\r\n\r\n" + Idiom("b").ToJsonString() + "\n").Count);

        Assert.Contains("data.jsonl line 2", Refused(Idiom().ToJsonString() + "\n{ broken").Message);
        Assert.Contains("data.jsonl line 1", Refused("null").Message);
    }

    [Fact]
    public void The_same_source_and_credit_text_is_held_once()
    {
        var quotes = ExpressionCatalog.Parse(Idiom("a").ToJsonString() + "\n" + Idiom("b").ToJsonString());

        Assert.Same(quotes[0].Source, quotes[1].Source);
        Assert.Same(quotes[0].License, quotes[1].License);
        Assert.Same(quotes[0].Info!.AttributionText, quotes[1].Info!.AttributionText);
    }

    [Fact]
    public void A_sentence_that_is_not_a_record_has_the_same_tooltip_as_before()
    {
        var tatoeba = new Quote("I have to go to sleep.", "Tôi phải đi ngủ.", QuoteCategory.Conversation, "vinhan", "Tatoeba", "CC BY 2.0 FR", 1277, 5662, "evaZ");
        var own = new Quote("Haste makes waste.", "Dục tốc bất đạt.", QuoteCategory.Idiom, null, "s", "Public domain");

        Assert.Equal(tatoeba.Attribution, tatoeba.Details);
        Assert.Null(own.Details);
    }
}
