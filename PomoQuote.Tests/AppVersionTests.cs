using System.Text.RegularExpressions;

namespace PomoQuote.Tests;

public sealed class AppVersionTests
{
    private static readonly string Csproj = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PomoQuote.csproj"));

    private static readonly string Changelog = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "CHANGELOG.md"));

    private static string ProjectVersion() =>
        Regex.Match(File.ReadAllText(Csproj), "<Version>([^<]+)</Version>").Groups[1].Value;

    [Fact]
    public void The_version_the_About_dialog_shows_is_the_one_in_the_project_file()
    {
        Assert.Equal(ProjectVersion(), AboutDialog.GetAppVersion());
    }

    [Fact]
    public void The_changelog_has_an_entry_for_the_project_version_and_an_unreleased_section_above_it()
    {
        var text = File.ReadAllText(Changelog);

        Assert.Matches($@"## \[{Regex.Escape(ProjectVersion())}\] - \d{{4}}-\d{{2}}-\d{{2}}", text);
        Assert.True(text.IndexOf("## [Unreleased]", StringComparison.Ordinal) < text.IndexOf($"## [{ProjectVersion()}]", StringComparison.Ordinal));
        Assert.Contains($"[{ProjectVersion()}]: https://github.com/quanap5/PomoQuote/", text);
        Assert.DoesNotContain("HelloWorldBot/", text); // links point at the renamed repository
    }
}
