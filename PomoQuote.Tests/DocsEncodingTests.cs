using System.Text;
using System.Text.RegularExpressions;

namespace PomoQuote.Tests;

/// <summary>
/// The documents are UTF-8 and carry real Vietnamese characters. Text that went through a wrong encoding (UTF-8 bytes read as
/// Windows-1252 and saved again) shows up as markers such as "Ã", "á»", "Ä" followed by a symbol, "â€" or the replacement character.
/// </summary>
public sealed class DocsEncodingTests
{
    private static readonly string[] Documents = ["README.md", "CHANGELOG.md", "THIRD_PARTY_ASSETS.md"];

    private static readonly Regex Mojibake = new("Ã|á»|áº|Ä[\u0080-¿‘-›]|â€|Â[ -¿]|Æ°|�", RegexOptions.Compiled);

    private static string RepositoryRoot()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "PomoQuote.csproj"))) root = Path.GetDirectoryName(root);
        return root ?? throw new InvalidOperationException("repository root not found");
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("CHANGELOG.md")]
    [InlineData("THIRD_PARTY_ASSETS.md")]
    public void The_document_is_valid_UTF_8_without_mojibake(string name)
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepositoryRoot(), name));

        var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); // throws on invalid UTF-8

        var markers = Mojibake.Matches(text).Select(m => text[Math.Max(0, m.Index - 20)..Math.Min(text.Length, m.Index + 25)].Replace("\n", " ")).ToList();
        Assert.True(markers.Count == 0, $"{name} has mojibake: " + string.Join(" | ", markers.Take(5)));
    }

    [Fact]
    public void The_new_vietnamese_text_is_written_with_the_real_characters()
    {
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md"), Encoding.UTF8);
        var changelog = File.ReadAllText(Path.Combine(RepositoryRoot(), "CHANGELOG.md"), Encoding.UTF8);

        Assert.Contains("Thứ Ba, 24/11/2026", readme); // Thứ Ba
        Assert.Contains("Âm lịch: 16/10 Bính Ngọ", readme); // Âm lịch: 16/10 Bính Ngọ
        Assert.Contains("Hồ Ngọc Đức", readme); // Hồ Ngọc Đức
        Assert.Contains("Hồ Ngọc Đức", changelog);
        Assert.Contains("nhuận", readme); // nhuận
    }

    [Fact]
    public void The_detector_catches_the_double_encoded_text_it_is_meant_for()
    {
        var good = "Thứ Ba, Âm lịch: Bính Ngọ, Hồ Ngọc Đức";
        // Simulate the mistake: the UTF-8 bytes of the text read as single-byte (Latin-1 / Windows-1252) characters.
        var mistaken = Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(good));

        Assert.DoesNotMatch(Mojibake, good);
        Assert.Matches(Mojibake, mistaken);
    }
}
