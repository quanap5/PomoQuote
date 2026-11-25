namespace PomoQuote.Mascot;

/// <summary>One selectable mascot: its id (what is saved), the name the menus show and its sprite sheet file in Assets/Mascot.</summary>
internal sealed record MascotCharacter(string Id, string Name, string SheetFile);

/// <summary>
/// The characters the app offers, one entry each. A sheet is 24 px cells in the row order of mascot-config.json, so adding a character is one more
/// entry here, its sheet in Assets/Mascot (and its credits in Assets/Mascot/CREDITS.md): the menus, the settings, the loading and the tests all
/// work from this list.
/// </summary>
internal static class MascotCharacters
{
    public const string DefaultId = "robot";

    public static IReadOnlyList<MascotCharacter> All { get; } =
    [
        new(DefaultId, "Robot", "robot-sprites.png"),
        new("animal", "Animal", "animal-sprites.png"),
        new("man", "Man", "man-sprites.png"),
        new("woman", "Woman", "woman-sprites.png"),
    ];

    public static MascotCharacter Default => All[0];

    /// <summary>The character with this id (case does not matter), or the robot when the id is unknown or missing.</summary>
    public static MascotCharacter Find(string? id) => All.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;

    public static bool IsKnown(string? id) => All.Any(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
}