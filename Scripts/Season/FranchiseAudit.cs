using System.Linq;
using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>
/// Plays whole seasons back to back — regular season, playoffs, draft, offseason, repeat — to
/// prove the franchise loop actually closes rather than ending after one year.
/// </summary>
public static class FranchiseAudit
{
    public static void Run(SeasonState season, int years)
    {
        GD.Print($"\n=== FRANCHISE AUDIT — {years} seasons ===");
        GD.Print($"{"yr",3} {"champion",-22} {"best record",-16} {"avg age",7} {"hurt",6} {"retired",8} {"careerPA",9}");

        for (int y = 0; y < years; y++)
        {
            // Regular season, played on the calendar rather than straight down the game list —
            // injuries are a daily tick now, so grinding through SimulateGame skipped them all.
            var everHurt = new System.Collections.Generic.HashSet<PlayerData>();
            int guardDays = 0;
            while (season.CurrentDay <= season.FinalDay && guardDays++ < 500)
            {
                season.AdvanceDay(simulateUserGame: true);
                foreach (var r in season.AllRosters)
                    foreach (var p in r.Players)
                        if (p.IsInjured) everHurt.Add(p);
            }
            foreach (var g in season.Games.Where(g => !g.Played).ToList()) season.SimulateGame(g);

            // Playoffs.
            season.BeginPlayoffsIfReady();
            int guard = 0;
            while (!season.Playoffs.Finished && guard++ < 400) season.SimulateNextPlayoffGame();

            // Draft.
            season.Draft.Begin(season, season.LeagueSeed + season.Year);
            guard = 0;
            while (!season.Draft.Complete && guard++ < 500) season.Draft.AutoPick(season);

            var champ = season.Playoffs.ChampionId >= 0
                ? Teams.Get(season.Playoffs.ChampionId).City + " " + Teams.Get(season.Playoffs.ChampionId).Nickname
                : "none";
            var best = season.AllStandings().OrderByDescending(x => x.Record.WinPct).First();
            var players = season.AllRosters.SelectMany(r => r.Players).ToList();
            int rookies = players.Count(p => p.Age <= 22);
            int hurt = everHurt.Count;

            int yr = season.Year;
            season.AdvanceToNextSeason();
            int retired = season.LastOffseason.Count(r => r.Graduated);

            long careerPa = season.AllRosters.SelectMany(r => r.Players)
                .Sum(p => (long)season.Book.CareerBatting(p).PlateAppearances);

            GD.Print($"{yr,3} {champ,-22} {best.Team.Abbrev + " " + best.Record.Wins + "-" + best.Record.Losses,-16} " +
                     $"{players.Average(p => p.Age),7:F1} {hurt,6} {retired,8} {careerPa,9}");
        }

        var all = season.AllRosters.SelectMany(r => r.Players).ToList();
        GD.Print($"\nafter {years} seasons: {all.Count} players, " +
                 $"ages {all.Min(p => p.Age)}-{all.Max(p => p.Age)}, average {all.Average(p => p.Age):F1}");
        GD.Print($"champions on record: {season.History.Count}");
        GD.Print($"distinct champions:  {season.History.Select(h => h.TeamId).Distinct().Count()}");
    }
}
