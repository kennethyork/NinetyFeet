using System.Linq;
using Godot;

namespace SandlotSlugfest.Data;

/// <summary>
/// Checks that editing a club changes its name and nothing else.
///
/// The dangerous half of a customisation screen is not the part that visibly works. A rename that
/// survives a reload is easy to see; what is not easy to see is whether reverting really restored
/// the original or merely wrote the old text back over an override, whether the edit leaked into
/// the club's playing biases, and whether a harness measuring the league is now describing
/// somebody's renamed teams instead of the ones in the source.
///
/// Writes to its own file. A harness that saved over user://teams.cfg would silently destroy
/// whatever the player had renamed, which is the kind of thing that only gets noticed much later.
/// </summary>
public static class TeamEditAudit
{
    public static void Run()
    {
        const string Scratch = "user://teams.audit.cfg";
        string realPath = TeamEdits.Path;

        TeamEdits.Path = Scratch;
        TeamEdits.Enabled = true;
        TeamEdits.Load();

        GD.Print("\n=== CLUB EDITOR ===");

        const int Id = 7;
        var before = Teams.Get(Id);
        string wasCity = before.City, wasNick = before.Nickname, wasAbbrev = before.Abbrev;
        var wasPrimary = before.Primary;
        int wasPower = before.PowerBias, wasPitch = before.PitchingBias;
        var wasLeague = before.League;
        var wasDivision = before.Division;

        GD.Print($"  club {Id} shipped as: {wasCity} {wasNick} ({wasAbbrev})");

        // --- Rename it. ---
        TeamEdits.Set(Id, new TeamEdits.Edit
        {
            City = "Sheffield", Nickname = "Steelmen", Abbrev = "SHF",
            Primary = new Color("#7a4f9c"),
        });

        var now = Teams.Get(Id);
        Pass("rename applied", now.City == "Sheffield" && now.Nickname == "Steelmen"
                            && now.Abbrev == "SHF");
        Pass("colour applied", now.Primary == new Color("#7a4f9c"));

        // --- The parts that must NOT move. ---
        Pass("league untouched", now.League == wasLeague);
        Pass("division untouched", now.Division == wasDivision);
        Pass("playing biases untouched",
            now.PowerBias == wasPower && now.PitchingBias == wasPitch);

        // --- Nobody else moved. ---
        int strays = Enumerable.Range(0, Teams.All.Count)
            .Count(i => i != Id && TeamEdits.For(i) != null);
        Pass("no other club edited", strays == 0);

        // --- It survives a reload. ---
        TeamEdits.Load();
        Teams.Rebuild();
        var reloaded = Teams.Get(Id);
        Pass("survives a reload", reloaded.City == "Sheffield" && reloaded.Abbrev == "SHF");

        // --- Putting it back really puts it back. ---
        TeamEdits.Clear(Id);
        var restored = Teams.Get(Id);
        Pass("reverts to the original",
            restored.City == wasCity && restored.Nickname == wasNick
            && restored.Abbrev == wasAbbrev && restored.Primary == wasPrimary);

        // --- A harness must not see any of it. ---
        TeamEdits.Set(Id, new TeamEdits.Edit { City = "Sheffield" });
        TeamEdits.Enabled = false;
        Teams.Rebuild();
        Pass("a verification run ignores edits", Teams.Get(Id).City == wasCity);

        // Clean up after itself, and leave the real file exactly as it was found.
        TeamEdits.Enabled = true;
        TeamEdits.ClearAll();
        if (FileAccess.FileExists(Scratch))
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(Scratch));

        TeamEdits.Path = realPath;
        TeamEdits.Load();
        Teams.Rebuild();

        GD.Print($"\n  the player's own file at {realPath} was never written to.");
    }

    private static void Pass(string what, bool ok) =>
        GD.Print($"  {(ok ? "ok  " : "FAIL")}  {what}");
}
