using System.Linq;
using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>
/// A league at every size it can be, played to a champion.
///
/// Thirty-two was written into the source in eighty-odd places and most of them were harmless —
/// a comment, a jersey number, a gravity constant, the unicode value of a space. The ones that
/// were not are the ones that decide a season: the circle method that pairs the clubs, the draft
/// order, the trade desk stepping to the next partner, the browser stepping down the list. Each
/// of those quietly assumes both that there are thirty-two clubs and that their identifiers run
/// 0 to 31.
///
/// The second assumption is the dangerous one, because a smaller league keeps the identifiers its
/// clubs shipped with. A sixteen-club league holds 0 to 3 and 8 to 11 and 16 to 19 and 24 to 27,
/// so `id + 1` is not the next club and `for id in 0..count` is not the league. Code written
/// against the old arrangement does not crash on the new one; it schedules a game against a club
/// that is not playing, or offers you a trade with one.
///
/// So every size gets a whole season: a schedule that balances, every game played, a champion
/// crowned, a draft held, and the league rolled into the next year. Anything short of that is not
/// evidence that a size works.
/// </summary>
public static class SizeAudit
{
    public static void Run()
    {
        GD.Print("\n=== LEAGUE SIZE — a season at each ===\n");

        int was = Teams.ActiveCount;
        int failures = 0;

        GD.Print("  clubs   schedule            games   champion              draft   next year");

        foreach (int size in Teams.Sizes)
        {
            Teams.ActiveCount = size;
            RosterGenerator.ResetCache();

            if (Teams.All.Count != size)
            {
                GD.Print($"  {size,5}   asked for {size} and got {Teams.All.Count}");
                failures++;
                continue;
            }

            // Four divisions of the same size, or the pennant races are not comparable and the
            // playoff seeding means nothing.
            var divisions = Teams.All
                .GroupBy(t => (t.League, t.Division))
                .Select(g => g.Count())
                .ToList();

            if (divisions.Count != 4 || divisions.Distinct().Count() != 1)
            {
                GD.Print($"  {size,5}   divisions came out {string.Join("/", divisions)}");
                failures++;
                continue;
            }

            var season = new SeasonState();

            // A short year on purpose: this is a check that a size works at all, and thirty-three
            // games proves that as well as a hundred and sixty-two while leaving time to prove it
            // seven times over.
            season.StartNew(RosterGenerator.DefaultLeagueSeed, Teams.All[0].Id, Schedule.ShortSeason, 9);

            string balance = Schedule.IsBalanced(season.Games, out string problem)
                ? "balanced" : problem;

            // Every fixture must be between two clubs that are actually in the league. This is the
            // check the whole audit exists for: a schedule built on seats rather than identifiers
            // would look perfectly balanced and pair clubs that do not exist.
            var strays = season.Games
                .Where(g => !Teams.InLeague(g.AwayId) || !Teams.InLeague(g.HomeId))
                .ToList();

            if (strays.Count > 0)
                balance = $"{strays.Count} games involve a club not in the league";

            int guard = 0;
            while (season.CurrentDay <= season.FinalDay && guard++ < 800)
                season.AdvanceDay(simulateUserGame: true);
            foreach (var g in season.Games.Where(g => !g.Played).ToList()) season.SimulateGame(g);

            season.BeginPlayoffsIfReady();
            guard = 0;
            while (!season.Playoffs.Finished && guard++ < 200)
                if (season.SimulateNextPlayoffGame() == null) break;

            // Read before the year rolls over, which zeroes it. The first version of this printed
            // the count afterwards and reported every size as having played no games at all, while
            // every other column said the season had gone perfectly — a reminder that a summary
            // line is a measurement too.
            int played = season.GamesPlayed;

            string champion = season.Playoffs.ChampionId >= 0
                ? Teams.Get(season.Playoffs.ChampionId).FullName
                : "NOBODY";

            // The draft, which sizes its class off the club count and would otherwise run out of
            // players or hand out picks nobody holds.
            season.Draft.Order.Clear();
            season.Draft.Begin(season, RosterGenerator.DefaultLeagueSeed + size);
            int picks = season.Draft.Order.Count;
            int taken = 0;
            while (!season.Draft.Complete && taken++ < picks + 4)
                if (season.Draft.AutoPick(season) == null) break;

            string draft = season.Draft.Order.Count == 0 ? "no order"
                : season.Draft.Complete ? $"{season.Draft.Picks.Count} picks" : "stalled";

            // And into next year, which redraws the schedule and reshapes every roster.
            string next;
            try
            {
                season.AdvanceToNextSeason(Schedule.ShortSeason);
                next = Schedule.IsBalanced(season.Games, out string p2) ? "ok" : p2;
            }
            catch (System.Exception e)
            {
                next = e.GetType().Name;
            }

            bool ok = balance == "balanced" && champion != "NOBODY" && draft.EndsWith("picks")
                   && next == "ok";
            if (!ok) failures++;

            GD.Print($"  {size,5}   {balance,-18}  {played,5}   {champion,-20}  " +
                     $"{draft,-6}  {next}");
        }

        Teams.ActiveCount = was;
        RosterGenerator.ResetCache();

        GD.Print(failures == 0
            ? $"\n  All {Teams.Sizes.Length} sizes play a season, crown a champion, hold a draft and roll over."
            : $"\n  {failures} of {Teams.Sizes.Length} sizes are broken.");
    }
}
