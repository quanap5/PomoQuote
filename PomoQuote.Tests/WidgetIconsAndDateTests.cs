using System.Drawing;
using System.Reflection;
using System.Text.RegularExpressions;
using PomoQuote.AiUsage;
using PomoQuote.Pomodoro;
using PomoQuote.Widget;

namespace PomoQuote.Tests;

/// <summary>The Pomodoro state icon, the date after the clock, and the Claude icon with its fallback.</summary>
public sealed class WidgetIconsAndDateTests
{
    private static string RepositoryRoot()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "PomoQuote.csproj"))) root = Path.GetDirectoryName(root);
        return root ?? throw new InvalidOperationException("repository root not found");
    }

    // ---------- the Pomodoro state icon ----------

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 1, 0)] // a break that has not been started yet is Idle too
    [InlineData(1, 0, 1)]
    [InlineData(2, 0, 1)]
    [InlineData(1, 1, 2)]
    [InlineData(1, 2, 2)] // long break shares Rest
    [InlineData(2, 2, 2)]
    public void The_icon_follows_the_real_timer_state(int stateNumber, int phaseNumber, int expectedNumber)
    {
        Assert.Equal((PomodoroIconState)expectedNumber, PomodoroIcon.StateOf((PomodoroPhase)phaseNumber, (PomodoroRunState)stateNumber));
    }

    [Theory]
    [InlineData(0, 0, "Pomodoro idle, not running")]
    [InlineData(1, 0, "Pomodoro work")]
    [InlineData(2, 0, "Pomodoro work, paused")]
    [InlineData(1, 1, "Pomodoro rest")]
    [InlineData(2, 2, "Pomodoro rest, paused")]
    public void Each_state_has_an_accessible_name(int stateNumber, int phaseNumber, string expected)
    {
        Assert.Equal(expected, PomodoroIcon.Name((PomodoroPhase)phaseNumber, (PomodoroRunState)stateNumber));
    }

    [Theory]
    [InlineData(14)]
    [InlineData(17)]
    [InlineData(21)]
    [InlineData(28)]
    public void Every_state_draws_something_visible_inside_its_box_at_100_125_150_and_200_percent(int size)
    {
        foreach (var state in Enum.GetValues<PomodoroIconState>())
        {
            foreach (var paused in new[] { false, true })
            {
                if (state == PomodoroIconState.Idle && paused) continue;
                using var bitmap = new Bitmap(size + 8, size + 8);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.FromArgb(28, 32, 40));
                    PomodoroIcon.Draw(graphics, new RectangleF(4, 4, size, size), state, Color.FromArgb(138, 130, 244), paused, size / 14f);
                }

                var ink = 0;
                var outside = 0;
                for (var y = 0; y < bitmap.Height; y++)
                {
                    for (var x = 0; x < bitmap.Width; x++)
                    {
                        var pixel = bitmap.GetPixel(x, y);
                        var different = Math.Abs(pixel.R - 28) + Math.Abs(pixel.G - 32) + Math.Abs(pixel.B - 40) > 60;
                        if (!different) continue;
                        ink++;
                        if (x < 3 || y < 3 || x >= size + 5 || y >= size + 5) outside++;
                    }
                }

                Assert.True(ink >= size * 1.5, $"{state} paused={paused} at {size}px draws too little ({ink})");
                Assert.True(outside == 0, $"{state} paused={paused} at {size}px draws outside its box ({outside})");
            }
        }
    }

    [Fact]
    public void The_three_states_and_the_paused_mark_look_different()
    {
        string Picture(PomodoroIconState state, bool paused)
        {
            using var bitmap = new Bitmap(28, 28);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Black);
                PomodoroIcon.Draw(graphics, new RectangleF(2, 2, 24, 24), state, Color.White, paused, 1.7f);
            }

            var bits = new System.Text.StringBuilder();
            for (var y = 0; y < 28; y++) for (var x = 0; x < 28; x++) bits.Append(bitmap.GetPixel(x, y).R > 90 ? '1' : '0');
            return bits.ToString();
        }

        var pictures = new[] { Picture(PomodoroIconState.Idle, false), Picture(PomodoroIconState.Work, false), Picture(PomodoroIconState.Rest, false), Picture(PomodoroIconState.Work, true), Picture(PomodoroIconState.Rest, true) };

        Assert.Equal(pictures.Length, pictures.Distinct().Count());
    }

    [Fact]
    public void The_icon_uses_the_weather_icons_left_edge_size_gap_and_the_timer_tooltip_keeps_working()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "WidgetView.cs"));

        // Weather: iconSize = round(14 * u), x = left, text after icon + 5 * u. The timer row must use the very same numbers.
        Assert.Matches(new Regex(@"var iconSize = \(int\)Math\.Round\(14 \* u\);[\s\S]*var iconSize = \(int\)Math\.Round\(14 \* u\);"), source);
        Assert.Contains("new RectangleF((int)Math.Round(left),", source);
        Assert.Contains("left + iconSize + 5 * u", source);
        Assert.Contains("TimerTooltip.Text(", source);
        Assert.Contains("PomodoroIcon.Name(", source);
    }

    // ---------- the date after the clock ----------

    [Fact]
    public void The_longest_date_lines_fit_beside_the_clock_at_280_px_at_the_smallest_size()
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        using var clockFont = new Font("Segoe UI Light", 30f, FontStyle.Regular, GraphicsUnit.Pixel);
        if (clockFont.Name != "Segoe UI Light") return; // the design font is not installed here
        using var small = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var preferred = new Font("Segoe UI", 11.5f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces };
        var clock = graphics.MeasureString("88:88:88", clockFont, int.MaxValue, format).Width;
        var room = UsageGeometry.WidgetWidth - 2 * UsageGeometry.Margin - clock - 4;

        var widestSolar = 0f;
        var widestLunar = 0f;
        var widestLeap = 0f;
        var start = new DateOnly(2000, 1, 1);
        for (var offset = 0; offset < 366 * 4; offset++)
        {
            var date = start.AddDays(offset * 4 + offset % 4);
            widestSolar = Math.Max(widestSolar, graphics.MeasureString(DateLines.Solar(date), small, int.MaxValue, format).Width);
            var lunar = DateLines.Lunar(date);
            var width = graphics.MeasureString(lunar, small, int.MaxValue, format).Width;
            widestLunar = Math.Max(widestLunar, width);
            if (lunar.Contains("nhuận")) widestLeap = Math.Max(widestLeap, width);
        }

        Assert.True(room > 100, $"the room after the clock is {room:0} px");
        Assert.True(widestSolar <= room, $"the longest solar line is {widestSolar:0} px at 10 px, the room is {room:0} px");
        Assert.True(widestLunar <= room, $"the longest lunar line is {widestLunar:0} px at 10 px, the room is {room:0} px");
        // At the preferred size the ordinary lines fit as well (only a leap month may need a smaller size).
        Assert.True(graphics.MeasureString(DateLines.Solar(new DateOnly(2026, 12, 28)), preferred, int.MaxValue, format).Width <= room);
        Assert.True(graphics.MeasureString("Âm lịch: 30/12 Quý Mão", preferred, int.MaxValue, format).Width <= room || widestLeap > 0);
    }

    [Fact]
    public void The_date_draws_after_the_clock_with_no_overlap_in_the_source_layout()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "WidgetView.cs"));

        Assert.Contains("MeasureString(\"88:88:88\", timeFont", source); // the widest the clock gets: the date never moves or overlaps
        Assert.Contains("DrawDate(graphics, u, widestClock", source);
        Assert.Contains("DateLineCache", source);
        Assert.Contains("RefreshDate();", source);
    }

    // ---------- the Claude icon ----------

    [Fact]
    public void Claude_has_its_own_icon_and_a_neutral_fallback_when_that_file_is_missing_or_damaged()
    {
        using var real = new ProviderIcons();
        using var missing = new ProviderIcons(key => key.StartsWith("claude-neutral") ? new MemoryStream(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "Assets", "Providers", key + ".png"))) : null);
        using var damaged = new ProviderIcons(key => key.StartsWith("claude-neutral") ? new MemoryStream(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "Assets", "Providers", key + ".png"))) : new MemoryStream([1, 2, 3]));
        using var none = new ProviderIcons(_ => null);

        var own = real.Get("claude", IconBackground.Dark, 20)!;
        var fallbackMissing = missing.Get("claude", IconBackground.Dark, 20);
        var fallbackDamaged = damaged.Get("claude", IconBackground.Light, 20);

        Assert.NotNull(own);
        Assert.NotNull(fallbackMissing);
        Assert.NotNull(fallbackDamaged);
        Assert.Null(none.Get("claude", IconBackground.Dark, 20)); // no neutral file either: the name alone
        Assert.Null(missing.Get("codex", IconBackground.Dark, 20)); // other providers have no fallback
        Assert.NotEqual(own.GetPixel(10, 10), fallbackMissing!.GetPixel(10, 10)); // it really is the other image
    }

    /// <summary>SHA-256 of "Claude Spark - Clay.png" in Anthropic's press kit (zip SHA-256 C68AC92D...7151, retrieved 2026-11-24).</summary>
    private const string SparkSha256 = "C7D8415B1EDBA67E5337D3684B480B563826F07BE9E28E10CCC14BF92037EF6D";

    [Theory]
    [InlineData("claude-on-dark.png")]
    [InlineData("claude-on-light.png")]
    public void The_claude_files_are_byte_for_byte_the_press_kit_file(string name)
    {
        var path = Path.Combine(RepositoryRoot(), "Assets", "Providers", name);

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

        Assert.Equal(SparkSha256, hash);
    }

    [Fact]
    public void The_lobe_icons_files_and_references_are_gone_and_the_official_source_is_recorded()
    {
        var root = RepositoryRoot();
        var credits = File.ReadAllText(Path.Combine(root, "THIRD_PARTY_ASSETS.md"));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));

        Assert.False(Directory.Exists(Path.Combine(root, "Assets", "Providers", "source")));
        foreach (var text in new[] { credits, readme, changelog })
        {
            Assert.DoesNotContain("lobe-icons", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("LobeHub", text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("https://assets.claude.com/ae59ca4ca194dac9c9dc3bc78c5829468cb0e8af.zip", credits);
        Assert.Contains("C68AC92DF86C825F95177E24016FCC9A8863A3FD4CA344FE6F0700B2C1E07151", credits); // the zip
        Assert.Contains(SparkSha256, credits); // the file
        Assert.Contains("Claude Spark - Clay.png", credits);
        Assert.Contains("no written permission", credits, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("marketing@anthropic.com", credits);
        // The three guideline quotes are word for word (anthropic.com/legal/trademark-guidelines, checked 2026-11-24).
        Assert.Contains("You may only use our trademarks as specifically permitted by us and only in materials we approve beforehand.", credits);
        Assert.Contains("No alterations of our trademarks (changes to color, font, proportion, or otherwise) are permitted.", credits);
        Assert.Contains("You may not use our trademarks in a manner that implies Anthropic\u2019s sponsorship or endorsement, or a relationship or affiliation with Anthropic, except as we expressly authorize.", credits);
        Assert.Contains("If you have an existing business relationship with Anthropic", credits); // the contact is published for that case only
        Assert.DoesNotContain("only as specifically permitted by us", credits.Replace("You may only use our trademarks as specifically permitted by us", "")); // no paraphrased version left

        Assert.Contains("accepts", credits, StringComparison.OrdinalIgnoreCase); // the owner's acceptance of the risk
        Assert.Contains("claude-neutral-on-dark.png", credits);
        Assert.Contains("not affiliated", readme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_claude_spark_keeps_the_official_colour_and_is_not_recoloured_by_the_loader()
    {
        using var icons = new ProviderIcons();
        var icon = icons.Get("claude", IconBackground.Dark, 40)!;
        var official = 0;
        var other = 0;
        for (var y = 0; y < icon.Height; y++)
        {
            for (var x = 0; x < icon.Width; x++)
            {
                var pixel = icon.GetPixel(x, y);
                if (pixel.A < 240) continue;
                if (Math.Abs(pixel.R - 217) < 12 && Math.Abs(pixel.G - 119) < 12 && Math.Abs(pixel.B - 87) < 12) official++;
                else other++;
            }
        }

        Assert.True(official > 150, $"only {official} px of Anthropic's clay colour");
        Assert.True(other <= official * 2, $"{other} edge pixels against {official} exact ones");
    }

    [Fact]
    public void The_resource_names_of_the_packaged_icons_match_the_keys_the_code_asks_for()
    {
        var names = typeof(ProviderIcons).Assembly.GetManifestResourceNames().Where(n => n.StartsWith("PomoQuote.providers.", StringComparison.Ordinal)).ToList();

        var keys = new List<string> { "codex-on-dark", "codex-on-light", "claude-neutral-on-dark", "claude-neutral-on-light", "antigravity-on-dark", "antigravity-on-light" };
        if (File.Exists(Path.Combine(RepositoryRoot(), "Assets", "Providers", "claude-on-dark.png"))) keys.AddRange(["claude-on-dark", "claude-on-light"]); // the two files that may be deleted to revert
        foreach (var key in keys)
        {
            Assert.Contains($"PomoQuote.providers.{key}.png", names);
        }
    }
}
