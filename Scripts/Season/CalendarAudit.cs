using System.Linq;
using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>
/// Runs a season on the calendar rather than by grinding through a game list, and reports what a
/// manager would actually see: dates, days off, the injury list, and the news feed.
/// </summary>
public static class CalendarAudit
{
    public static void Run(SeasonState season)
    {
        GD.Print($"\n=== CALENDAR AUDIT — season {season.Year} ===");
        GD.Print($"opening day {Calendar.Format(Calendar.DateOf(0))}, " +
                 $"{season.FinalDay + 1} game days, {season.Games.Count} games");

        int days = 0, userGames = 0, restDays = 0, peakHurt = 0;
        var user = Teams.Get(season.UserTeamId);

        while (season.CurrentDay <= season.FinalDay)
        {
            var mine = season.UserGameToday();
            if (mine != null) userGames++;
            else restDays++;

            if (days < 6)
            {
                string opp = mine == null ? "no game"
                    : (mine.HomeId == season.UserTeamId
                        ? "vs " + Teams.Get(mine.AwayId).Abbrev
                        : "at " + Teams.Get(mine.HomeId).Abbrev);
                int hurtNow = season.RosterFor(season.UserTeamId).Players.Count(p => p.IsInjured);
                GD.Print($"  {Calendar.Format(season.Today),-22} {user.Abbrev} {opp,-10} " +
                         $"hurt {hurtNow}");
            }

            int hurt = season.AllRosters.SelectMany(r => r.Players).Count(p => p.IsInjured);
            peakHurt = Mathf.Max(peakHurt, hurt);

            season.AdvanceDay(simulateUserGame: true);
            days++;
        }

        var rec = season.Book.Record(season.UserTeamId);
        GD.Print($"\nadvanced {days} days from {Calendar.FormatShort(Calendar.DateOf(0))} " +
                 $"to {Calendar.FormatShort(season.Today)}");
        GD.Print($"{user.Abbrev}: {userGames} games, {restDays} days without one, record {rec.Wins}-{rec.Losses}");
        GD.Print($"most players hurt league-wide on any one day: {peakHurt}");
        GD.Print($"news items generated: {season.News.Count}");

        foreach (var n in season.News.Take(5))
            GD.Print($"  [{Calendar.FormatShort(Calendar.DateOf(n.Day))}] {n.Headline} — {n.Detail}");

        GD.Print($"\nseason complete: {season.RegularSeasonComplete}   phase now: {Calendar.PhaseLabel(season.Phase)}");
    }
}
