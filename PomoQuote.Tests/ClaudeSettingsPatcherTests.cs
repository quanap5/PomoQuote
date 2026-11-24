using System.Text.Json;
using PomoQuote.AiUsage.Claude;

namespace PomoQuote.Tests;

public sealed class ClaudeSettingsPatcherTests
{
    private const string Bridge = "\"C:/Apps/PomoQuote/PomoQuote.exe\" --bridge claude --chain-b64 QUJD";

    private const string CustomFile = "{\n  \"theme\": \"dark\",\n  \"statusLine\": {\n    \"command\": \"node \\\"C:/tools/line.js\\\"\",\n    \"type\": \"command\",\n    \"padding\": 2,\n    \"refreshInterval\": 5\n  },\n  \"hooks\": { \"x\": [1, 2, { \"y\": \"}\" }] }\n}\n";

    private static string Command(string json) => JsonDocument.Parse(json).RootElement.GetProperty("statusLine").GetProperty("command").GetString()!;

    [Fact]
    public void A_file_without_a_status_line_is_none()
    {
        var info = ClaudeSettingsPatcher.Inspect("{ \"theme\": \"dark\" }");

        Assert.Equal(StatusLineState.None, info.State);
        Assert.Null(info.Command);
    }

    [Fact]
    public void A_status_line_with_a_command_is_custom_and_the_command_is_decoded()
    {
        var info = ClaudeSettingsPatcher.Inspect(CustomFile);

        Assert.Equal(StatusLineState.Custom, info.State);
        Assert.Equal("node \"C:/tools/line.js\"", info.Command);
    }

    [Fact]
    public void A_command_containing_the_bridge_marker_is_bridged()
    {
        var info = ClaudeSettingsPatcher.Inspect("{ \"statusLine\": { \"type\": \"command\", \"command\": " + JsonSerializer.Serialize(Bridge) + " } }");

        Assert.Equal(StatusLineState.Bridged, info.State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{ \"a\": 1, }")]
    [InlineData("{ // comment\n \"a\": 1 }")]
    [InlineData("{ \"statusLine\": \"just text\" }")]
    [InlineData("{ \"statusLine\": { \"type\": \"command\" } }")]
    [InlineData("{ \"statusLine\": { \"command\": 5 } }")]
    [InlineData("{ \"statusLine\": null }")]
    public void Damaged_or_unusual_files_are_unreadable_and_never_patched(string json)
    {
        var info = ClaudeSettingsPatcher.Inspect(json);

        Assert.Equal(StatusLineState.Unreadable, info.State);
        Assert.NotEmpty(info.Reason);
        Assert.Null(ClaudeSettingsPatcher.Install(json, Bridge));
        Assert.Null(ClaudeSettingsPatcher.Uninstall(json, "x"));
    }

    [Fact]
    public void Installing_over_a_custom_command_changes_only_that_string()
    {
        var patched = ClaudeSettingsPatcher.Install(CustomFile, Bridge)!;

        Assert.Equal(Bridge, Command(patched));
        // Every character outside the command value is identical.
        var before = CustomFile.Replace("\"node \\\"C:/tools/line.js\\\"\"", "@@");
        var after = patched.Replace(JsonSerializer.Serialize(Bridge, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), "@@");
        Assert.Equal(before, after);
        var statusLine = JsonDocument.Parse(patched).RootElement.GetProperty("statusLine");
        Assert.Equal(2, statusLine.GetProperty("padding").GetInt32());
        Assert.Equal(5, statusLine.GetProperty("refreshInterval").GetInt32());
        Assert.Equal("command", statusLine.GetProperty("type").GetString());
    }

    [Fact]
    public void Installing_when_there_is_no_status_line_adds_one_as_the_last_key_keeping_the_rest_byte_for_byte()
    {
        const string original = "{\n  \"theme\": \"dark\",\n  \"hooks\": {\n    \"a\": 1\n  }\n}\n";

        var patched = ClaudeSettingsPatcher.Install(original, Bridge)!;

        var root = JsonDocument.Parse(patched).RootElement;
        Assert.Equal(Bridge, root.GetProperty("statusLine").GetProperty("command").GetString());
        Assert.Equal("command", root.GetProperty("statusLine").GetProperty("type").GetString());
        Assert.StartsWith(original[..^3], patched); // everything up to the end of the last value is untouched
        Assert.EndsWith("\n}\n", patched);
        Assert.Contains("\n  \"statusLine\":", patched); // same two-space indentation
    }

    [Fact]
    public void Crlf_line_endings_and_tab_indentation_are_kept_for_the_new_key()
    {
        const string original = "{\r\n\t\"theme\": \"dark\"\r\n}\r\n";

        var patched = ClaudeSettingsPatcher.Install(original, Bridge)!;

        Assert.Contains(",\r\n\t\"statusLine\":", patched);
        Assert.DoesNotContain("\n\n", patched.Replace("\r\n", "\n"));
        Assert.Equal(Bridge, Command(patched));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ }")]
    [InlineData("{\n}\n")]
    public void An_empty_object_gets_a_status_line_and_is_still_valid_json(string original)
    {
        var patched = ClaudeSettingsPatcher.Install(original, Bridge)!;

        Assert.Equal(Bridge, Command(patched));
    }

    [Fact]
    public void An_already_bridged_file_is_not_patched_again()
    {
        var bridged = ClaudeSettingsPatcher.Install(CustomFile, Bridge)!;

        Assert.Null(ClaudeSettingsPatcher.Install(bridged, Bridge));
    }

    [Fact]
    public void Undo_of_a_custom_file_restores_the_original_text_exactly()
    {
        var bridged = ClaudeSettingsPatcher.Install(CustomFile, Bridge)!;

        var restored = ClaudeSettingsPatcher.Uninstall(bridged, "node \"C:/tools/line.js\"")!;

        Assert.Equal(CustomFile, restored);
    }

    [Theory]
    [InlineData("{\n  \"theme\": \"dark\"\n}\n")]
    [InlineData("{\n  \"theme\": \"dark\",\n  \"hooks\": { \"a\": [1, 2] }\n}\n")]
    [InlineData("{ \"theme\": \"dark\" }")]
    [InlineData("{}")]
    public void Undo_of_a_file_that_had_no_status_line_removes_it_again_without_leaving_a_trace(string original)
    {
        var bridged = ClaudeSettingsPatcher.Install(original, Bridge)!;

        var restored = ClaudeSettingsPatcher.Uninstall(bridged, null)!;

        Assert.Equal(JsonDocument.Parse(original).RootElement.GetRawText().Replace(" ", ""), JsonDocument.Parse(restored).RootElement.GetRawText().Replace(" ", ""));
        Assert.DoesNotContain("statusLine", restored);
        if (original.Contains('\n')) Assert.Equal(original, restored);
    }

    [Fact]
    public void Undo_removes_a_status_line_that_sits_in_the_middle_or_first()
    {
        const string middle = "{\n  \"a\": 1,\n  \"statusLine\": { \"type\": \"command\", \"command\": \"--bridge claude\" },\n  \"b\": 2\n}\n";
        const string first = "{\n  \"statusLine\": { \"type\": \"command\", \"command\": \"--bridge claude\" },\n  \"b\": 2\n}\n";

        Assert.Equal("{\n  \"a\": 1,\n  \"b\": 2\n}\n", ClaudeSettingsPatcher.Uninstall(middle, null));
        Assert.Equal("{\n  \"b\": 2\n}\n", ClaudeSettingsPatcher.Uninstall(first, null));
    }

    [Fact]
    public void Edits_the_user_made_after_the_install_survive_the_undo()
    {
        var bridged = ClaudeSettingsPatcher.Install(CustomFile, Bridge)!;
        var edited = bridged.Replace("\"theme\": \"dark\"", "\"theme\": \"light\"").Replace("\"padding\": 2", "\"padding\": 4");

        var restored = ClaudeSettingsPatcher.Uninstall(edited, "node \"C:/tools/line.js\"")!;

        Assert.Contains("\"theme\": \"light\"", restored);
        Assert.Contains("\"padding\": 4", restored);
        Assert.Equal("node \"C:/tools/line.js\"", Command(restored));
    }

    [Fact]
    public void Undo_does_nothing_when_the_status_line_is_no_longer_ours()
    {
        Assert.Null(ClaudeSettingsPatcher.Uninstall(CustomFile, "x"));
        Assert.Null(ClaudeSettingsPatcher.Uninstall("{ }", null));
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("with \"quotes\" and \\ backslashes")]
    [InlineData("unicode: Hãy tin vào chính mình & <tags> 'single'")]
    [InlineData("tab\tand\nnewline")]
    public void Awkward_characters_in_a_command_round_trip_through_install_and_undo(string original)
    {
        var literal = JsonSerializer.Serialize(original);
        var file = "{\n  \"statusLine\": { \"type\": \"command\", \"command\": " + literal + " }\n}\n";

        var info = ClaudeSettingsPatcher.Inspect(file);
        var bridged = ClaudeSettingsPatcher.Install(file, Bridge)!;
        var restored = ClaudeSettingsPatcher.Uninstall(bridged, info.Command)!;

        Assert.Equal(original, info.Command);
        Assert.Equal(original, Command(restored));
        Assert.Equal(Bridge, Command(bridged));
    }

    [Fact]
    public void Braces_and_quotes_inside_other_values_do_not_confuse_the_scanner()
    {
        const string tricky = "{ \"a\": \"}{ \\\"statusLine\\\": 1\", \"b\": [ { \"c\": \"]\" } ], \"statusLine\": { \"command\": \"x\" } }";

        var info = ClaudeSettingsPatcher.Inspect(tricky);
        var patched = ClaudeSettingsPatcher.Install(tricky, Bridge)!;

        Assert.Equal(StatusLineState.Custom, info.State);
        Assert.Equal(Bridge, Command(patched));
        Assert.Contains("\"a\": \"}{ \\\"statusLine\\\": 1\"", patched);
    }
}
