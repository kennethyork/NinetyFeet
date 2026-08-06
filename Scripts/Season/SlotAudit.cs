using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>
/// Checks that four leagues can exist side by side without touching one another.
///
/// The dangerous failure here is silent and total: two slots resolving to the same file, or a
/// switch that writes the league you are leaving over the one you are opening. Either destroys a
/// dynasty and neither shows up as a crash — you find out weeks later when the wrong club is on
/// the screen.
///
/// It works in the two high slots and never in slot one, because slot one is where the player's
/// actual league lives.
/// </summary>
public static class SlotAudit
{
    public static void Run()
    {
        GD.Print("\n=== LEAGUE SLOTS ===");
        GD.Print($"  slots available: {SaveGame.Slots}");

        for (int i = 0; i < SaveGame.Slots; i++)
            GD.Print($"    slot {i + 1}  {SaveGame.PathFor(i),-26} {SaveGame.Describe(i)}");

        // Every slot must be a different file, or two leagues silently become one.
        bool distinct = true;
        for (int a = 0; a < SaveGame.Slots; a++)
            for (int b = a + 1; b < SaveGame.Slots; b++)
                if (SaveGame.PathFor(a) == SaveGame.PathFor(b)) distinct = false;
        GD.Print($"\n  every slot a different file: {(distinct ? "yes" : "NO")}");

        // Slot one keeps the original name, or every league that existed before slots is orphaned.
        GD.Print($"  slot 1 keeps the old filename: " +
                 $"{(SaveGame.PathFor(0) == "user://season.json" ? "yes" : "NO")}");

        const int A = 2, B = 3;
        if (SaveGame.Occupied(A) || SaveGame.Occupied(B))
        {
            GD.Print("\n  slots 3 and 4 are in use — not writing over them. Isolation untested.");
            return;
        }

        // Two different leagues, written to two slots, read back.
        SaveGame.Slot = A;
        var first = new SeasonState();
        first.StartNew(RosterGenerator.DefaultLeagueSeed, 5, 40, 9);
        for (int d = 0; d < 6; d++) first.AdvanceDay(simulateUserGame: true);
        SaveGame.Save(first);

        SaveGame.Slot = B;
        var second = new SeasonState();
        second.StartNew(RosterGenerator.DefaultLeagueSeed + 1, 19, 40, 9);
        SaveGame.Save(second);

        SaveGame.Slot = A;
        var backA = SaveGame.Load();
        SaveGame.Slot = B;
        var backB = SaveGame.Load();

        GD.Print($"\n  slot 3 wrote club 5, read back {backA?.UserTeamId.ToString() ?? "nothing"}" +
                 $"   {(backA?.UserTeamId == 5 ? "ok" : "FAIL")}");
        GD.Print($"  slot 4 wrote club 19, read back {backB?.UserTeamId.ToString() ?? "nothing"}" +
                 $"   {(backB?.UserTeamId == 19 ? "ok" : "FAIL")}");
        GD.Print($"  the two did not run into each other: " +
                 $"{(backA?.UserTeamId != backB?.UserTeamId ? "ok" : "FAIL")}");
        GD.Print($"  games survived the round trip: {backA?.GamesPlayed ?? -1} " +
                 $"{(backA is { GamesPlayed: > 0 } ? "ok" : "FAIL")}");
        SaveGame.Slot = A; SaveGame.Save(first);
        using (var broken = FileAccess.Open(SaveGame.PathFor(A), FileAccess.ModeFlags.Write)) broken?.StoreString("{ interrupted");
        var recovered = SaveGame.Load();
        GD.Print($"  corrupt live save recovered from backup: {(recovered?.UserTeamId == 5 ? "ok" : "FAIL")}");

        foreach (int slot in new[] { A, B })
        {
            foreach (string path in new[] { SaveGame.PathFor(slot), SaveGame.BackupPathFor(slot) })
                if (FileAccess.FileExists(path)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        }

        SaveGame.Slot = 0;
        GD.Print($"\n  cleaned up; slot 1 was never opened and still reads: {SaveGame.Describe(0)}");
    }
}
