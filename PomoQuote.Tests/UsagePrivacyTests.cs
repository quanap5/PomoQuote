using System.Text.RegularExpressions;

namespace PomoQuote.Tests;

/// <summary>PomoQuote never reads or stores a credential: the AI usage code and the files it writes are scanned for the names credentials go by.</summary>
public sealed class UsagePrivacyTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    // Names of credential files and fields used by the CLIs (the spike listed them); none may appear in the usage code.
    private static readonly string[] Forbidden =
    [
        "auth.json", ".credentials.json", "credentials.json", "access_token", "refresh_token", "id_token", "accessToken", "refreshToken",
        "OPENAI_API_KEY", "ANTHROPIC_API_KEY", "api_key", "apiKey", "Authorization", "Bearer", "cookie", "keyring", "claudeAiOauth", "sessionKey",
    ];

    private static IEnumerable<string> UsageSourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(Root, "AiUsage"), "*.cs")
            .Concat([Path.Combine(Root, "MainForm.Usage.cs"), Path.Combine(Root, "UsageSettingsDialog.cs")]);

    [Fact]
    public void The_usage_source_files_contain_no_credential_file_or_field_names()
    {
        foreach (var file in UsageSourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var word in Forbidden)
            {
                Assert.False(text.Contains(word, StringComparison.OrdinalIgnoreCase), $"{Path.GetFileName(file)} mentions \"{word}\"");
            }
        }
    }

    [Fact]
    public void The_usage_code_never_reads_or_enumerates_the_cli_home_folders()
    {
        foreach (var file in UsageSourceFiles())
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotMatch(@"\.codex[\\/""]", text);
            Assert.DoesNotMatch(@"\.claude[\\/""]", text);
            Assert.DoesNotMatch(@"\.gemini[\\/""]", text);
            Assert.DoesNotContain("UserProfile", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_only_files_the_usage_code_writes_are_its_own_json_files_in_the_app_data_folder()
    {
        var names = new HashSet<string>();
        foreach (var file in UsageSourceFiles())
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"PomoQuote\", \"([^\"]+)\"")) names.Add(m.Groups[1].Value);
        }

        Assert.Equal(["usage-alerts.json", "usage-cache.json", "usage.json"], names.OrderBy(n => n).ToArray());
    }

    [Fact]
    public void The_settings_dialog_has_no_place_to_type_a_secret()
    {
        var text = File.ReadAllText(Path.Combine(Root, "UsageSettingsDialog.cs"));

        Assert.DoesNotContain("PasswordChar", text);
        Assert.DoesNotContain("UseSystemPasswordChar", text);
        Assert.DoesNotContain("new TextBox", text);
        Assert.Contains("never asks for a password, key or token", text);
    }

    [Fact]
    public void The_codex_executable_is_only_ever_started_with_known_read_only_arguments()
    {
        var text = File.ReadAllText(Path.Combine(Root, "AiUsage", "CodexInstall.cs"));

        Assert.Contains("\"app-server\"", text);
        Assert.Contains("\"--version\"", text);
        Assert.Contains("\"login\", \"status\"", text);
        Assert.DoesNotContain("\"logout\"", text);
        Assert.DoesNotContain("\"login\")", text);
    }
}
