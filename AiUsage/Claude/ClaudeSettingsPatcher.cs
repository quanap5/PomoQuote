using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PomoQuote.AiUsage.Claude;

/// <summary>What the user-level Claude settings say about the status line.</summary>
public enum StatusLineState
{
    /// <summary>No `statusLine` key: nothing is shown by a custom command.</summary>
    None,

    /// <summary>A `statusLine` command that is not ours.</summary>
    Custom,

    /// <summary>The `statusLine` command is the PomoQuote bridge (alone or wrapping the user's command).</summary>
    Bridged,

    /// <summary>The file is missing, is not valid JSON, is locked, or has a `statusLine` in a shape PomoQuote will not touch.</summary>
    Unreadable,
}

/// <summary>The result of looking at a settings file.</summary>
/// <param name="State">The state.</param>
/// <param name="Command">The current `statusLine.command` (for Custom and Bridged), else null.</param>
/// <param name="Reason">Why it is Unreadable (short, no file content), else empty.</param>
internal sealed record StatusLineInfo(StatusLineState State, string? Command, string Reason = "");

/// <summary>
/// Edits the `statusLine` of a Claude Code `settings.json` as TEXT: only the characters of the value that changes are
/// touched, so every other key, its order, the indentation and the line endings stay exactly as the user wrote them.
/// The file is never re-serialised. Invalid JSON (including comments and trailing commas) is refused, not repaired.
/// All of it is pure string work; reading and writing the file is the installer's job.
/// </summary>
internal static class ClaudeSettingsPatcher
{
    /// <summary>The text that marks a command as the PomoQuote bridge.</summary>
    public const string BridgeMarker = "--bridge claude";

    private static readonly JsonSerializerOptions LiteralOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private sealed record Member(string Key, int KeyStart, int ValueStart, int ValueEnd);

    public static StatusLineInfo Inspect(string json)
    {
        if (!TryParseRoot(json, out var members, out _, out var reason)) return new StatusLineInfo(StatusLineState.Unreadable, null, reason);

        var statusLine = members.FirstOrDefault(m => m.Key == "statusLine");
        if (statusLine is null) return new StatusLineInfo(StatusLineState.None, null);

        var value = json[statusLine.ValueStart..statusLine.ValueEnd];
        if (value.TrimStart().StartsWith('{') is false) return new StatusLineInfo(StatusLineState.Unreadable, null, "statusLine is not an object");

        var command = FindCommand(json, statusLine);
        if (command is null) return new StatusLineInfo(StatusLineState.Unreadable, null, "statusLine has no command string");

        var text = ReadString(json, command);
        return IsBridge(text) ? new StatusLineInfo(StatusLineState.Bridged, text) : new StatusLineInfo(StatusLineState.Custom, text);
    }

    /// <summary>Whether a command string is the PomoQuote bridge.</summary>
    public static bool IsBridge(string? command) => command is not null && command.Contains(BridgeMarker, StringComparison.Ordinal);

    /// <summary>
    /// Case None: adds `"statusLine": { "type": "command", "command": ... }` as the last key. Case Custom: replaces only the
    /// `command` string. Returns null when the file is not in a state this can handle (Unreadable or already Bridged).
    /// </summary>
    public static string? Install(string json, string bridgeCommand)
    {
        var info = Inspect(json);
        TryParseRoot(json, out var members, out var rootEnd, out _);
        switch (info.State)
        {
            case StatusLineState.Custom:
                var command = FindCommand(json, members.First(m => m.Key == "statusLine"))!;
                return json[..command.ValueStart] + Literal(bridgeCommand) + json[command.ValueEnd..];

            case StatusLineState.None:
                return InsertMember(json, members, rootEnd, $"\"statusLine\": {{ \"type\": \"command\", \"command\": {Literal(bridgeCommand)} }}");

            default:
                return null;
        }
    }

    /// <summary>
    /// Undo: for a bridged file, puts <paramref name="originalCommand"/> back, or removes the whole `statusLine` member when
    /// it is null (the user had none). Everything else in the file is left as it is now. Null when the file is not bridged.
    /// </summary>
    public static string? Uninstall(string json, string? originalCommand)
    {
        if (Inspect(json).State != StatusLineState.Bridged) return null;
        TryParseRoot(json, out var members, out var rootEnd, out _);
        var statusLine = members.First(m => m.Key == "statusLine");

        if (originalCommand is not null)
        {
            var command = FindCommand(json, statusLine)!;
            return json[..command.ValueStart] + Literal(originalCommand) + json[command.ValueEnd..];
        }

        return RemoveMember(json, members, statusLine);
    }

    // ---------- text surgery ----------

    private static string InsertMember(string json, List<Member> members, int rootEnd, string member)
    {
        var newline = json.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (members.Count == 0)
        {
            return json[..(rootEnd)] + newline + "  " + member + newline + json[rootEnd..];
        }

        var indent = IndentBefore(json, members[0].KeyStart);
        var last = members[^1];
        return json[..last.ValueEnd] + "," + newline + indent + member + json[last.ValueEnd..];
    }

    private static string RemoveMember(string json, List<Member> members, Member member)
    {
        var index = members.IndexOf(member);
        if (members.Count == 1)
        {
            // The only key: leave an empty object.
            var open = json.IndexOf('{');
            var close = json.LastIndexOf('}');
            return json[..(open + 1)] + json[close..];
        }

        if (index == members.Count - 1)
        {
            // Last key: remove from the end of the previous value (taking its comma with it).
            return json[..members[index - 1].ValueEnd] + json[member.ValueEnd..];
        }

        // Any other key: remove from its start to the start of the next key.
        var from = LineStartOrKey(json, member.KeyStart);
        return json[..from] + json[LineStartOrKey(json, members[index + 1].KeyStart)..];
    }

    /// <summary>The start of the line a key is on when only whitespace precedes it, else the key itself.</summary>
    private static int LineStartOrKey(string json, int keyStart)
    {
        var i = keyStart;
        while (i > 0 && (json[i - 1] == ' ' || json[i - 1] == '\t')) i--;
        return i > 0 && json[i - 1] == '\n' ? i : keyStart;
    }
    private static string IndentBefore(string json, int keyStart)
    {
        var i = keyStart;
        while (i > 0 && (json[i - 1] == ' ' || json[i - 1] == '\t')) i--;
        return i > 0 && (json[i - 1] == '\n' || json[i - 1] == '\r') ? json[i..keyStart] : "  ";
    }

    private static string Literal(string text) => JsonSerializer.Serialize(text, LiteralOptions);

    private static string ReadString(string json, Member value) =>
        JsonSerializer.Deserialize<string>(json[value.ValueStart..value.ValueEnd]) ?? "";

    // ---------- a small scanner over text that JsonDocument has already accepted ----------

    private static bool TryParseRoot(string json, out List<Member> members, out int rootEnd, out string reason)
    {
        members = [];
        rootEnd = 0;
        reason = "";
        try
        {
            using var document = JsonDocument.Parse(json); // strict: no comments, no trailing commas
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                reason = "the file is not a JSON object";
                return false;
            }
        }
        catch (JsonException)
        {
            reason = "the file is not valid JSON";
            return false;
        }

        var i = 0;
        SkipWhitespace(json, ref i);
        members = ReadMembers(json, ref i, out rootEnd);
        return true;
    }

    /// <summary>Reads the members of the object that starts at <paramref name="i"/> (at its brace); <paramref name="closeIndex"/> is the index of the closing brace.</summary>
    private static List<Member> ReadMembers(string s, ref int i, out int closeIndex)
    {
        var list = new List<Member>();
        i++; // {
        while (true)
        {
            SkipWhitespace(s, ref i);
            if (s[i] == '}')
            {
                closeIndex = i;
                i++;
                return list;
            }

            if (s[i] == ',')
            {
                i++;
                continue;
            }

            var keyStart = i;
            var key = ScanString(s, ref i);
            SkipWhitespace(s, ref i);
            i++; // :
            SkipWhitespace(s, ref i);
            var valueStart = i;
            SkipValue(s, ref i);
            list.Add(new Member(key, keyStart, valueStart, i));
        }
    }

    private static Member? FindCommand(string json, Member statusLine)
    {
        var i = statusLine.ValueStart;
        var inner = ReadMembers(json, ref i, out _);
        var command = inner.FirstOrDefault(m => m.Key == "command");
        return command is not null && json[command.ValueStart] == '"' ? command : null;
    }

    private static void SkipValue(string s, ref int i)
    {
        switch (s[i])
        {
            case '"':
                ScanString(s, ref i);
                break;
            case '{':
                ReadMembers(s, ref i, out _);
                break;
            case '[':
                i++;
                while (true)
                {
                    SkipWhitespace(s, ref i);
                    if (s[i] == ']') { i++; return; }
                    if (s[i] == ',') { i++; continue; }
                    SkipValue(s, ref i);
                }

            default:
                while (i < s.Length && ",}] \t\r\n".IndexOf(s[i]) < 0) i++;
                break;
        }
    }

    private static string ScanString(string s, ref int i)
    {
        var sb = new StringBuilder();
        i++; // opening quote
        while (s[i] != '"')
        {
            if (s[i] == '\\') { sb.Append(s[i]); i++; }
            sb.Append(s[i]);
            i++;
        }

        i++; // closing quote
        return sb.ToString();
    }

    private static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
    }
}
