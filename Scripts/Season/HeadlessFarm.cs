using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>
/// Checks that a farm game is actually playable, for every club and every rung.
///
/// Simulating an affiliate's season only ever needed a list of men. Playing one needs a side: nine
/// in the batting order, somebody at every position, and an arm to start. Those are different
/// requirements, and the first says nothing about the second — a level stocked entirely with
/// outfielders would simulate perfectly well and be impossible to take the field with.
///
/// So this builds all ninety-six affiliates and refuses to be satisfied by anything less.
/// </summary>
public static class HeadlessFarm
{
    public static void Audit()
    {
        var season = new SeasonState();
        season.StartNew(RosterGenerator.DefaultLeagueSeed, 0, Schedule.FullSeason, 9);

        int checkedSides = 0, failed = 0, shortBench = 0;

        foreach (var team in Teams.All)
            foreach (var level in Farm.Levels)
            {
                checkedSides++;
                var side = Farm.BuildRoster(team.Id, level);

                if (side == null)
                {
                    failed++;
                    GD.Print($"  CANNOT FIELD: {team.Abbrev} {Farm.Name(level)} " +
                             $"({Farm.Of(team.Id, level).Count} men)");
                    continue;
                }

                // A side with no bench cannot absorb an injury or a pinch hitter.
                if (side.Players.Count - side.BattingOrder.Count - side.Pitchers.Count < 1)
                    shortBench++;

                // Every spot in the field has to have somebody standing in it.
                var empty = side.Starters.Where(s => s.Value == null).Select(s => s.Key).ToList();
                if (empty.Count > 0)
                {
                    failed++;
                    GD.Print($"  EMPTY SPOTS: {team.Abbrev} {Farm.Name(level)} — " +
                             string.Join(", ", empty));
                }
            }

        // And that a fixture can be made — an affiliate needs somebody to play.
        int noFixture = 0;
        foreach (var level in Farm.Levels)
        {
            var (away, home, _) = Farm.Fixture(0, level, 12345, false);
            if (away == null || home == null) noFixture++;
        }

        GD.Print($"\n=== FARM PLAYABILITY — {checkedSides} affiliates ===");
        GD.Print($"cannot field a side: {failed}   thin bench: {shortBench}   " +
                 $"levels with no fixture: {noFixture}");
        GD.Print(failed == 0 && noFixture == 0
            ? "FARM AUDIT: every affiliate can take the field."
            : "FARM AUDIT: FAILED — some affiliates cannot play.");
    }
}
