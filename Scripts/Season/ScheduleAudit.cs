using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>
/// Checks a generated schedule is actually playable and fair. A schedule bug is invisible in a
/// box score and poisons a whole season — an earlier version had one club playing every game at
/// home, and nothing but a check like this catches it.
/// </summary>
public static class ScheduleAudit
{
    public static void Run(int gamesPerTeam)
    {
        var games = Schedule.Build(gamesPerTeam, RosterGenerator.DefaultLeagueSeed);
        int maxDay = games.Max(g => g.Day);

        GD.Print($"\n=== SCHEDULE AUDIT — target {gamesPerTeam} games ===");
        GD.Print($"{games.Count} games across {maxDay + 1} dates");

        // --- Games, home/away and opponents per club ---
        var counts = new Dictionary<int, (int G, int H, int A)>();
        foreach (var t in Teams.All) counts[t.Id] = (0, 0, 0);
        foreach (var g in games)
        {
            var h = counts[g.HomeId]; counts[g.HomeId] = (h.G + 1, h.H + 1, h.A);
            var a = counts[g.AwayId]; counts[g.AwayId] = (a.G + 1, a.H, a.A + 1);
        }

        int minG = counts.Values.Min(v => v.G), maxG = counts.Values.Max(v => v.G);
        float worstHomeShare = counts.Values.Max(v => Mathf.Abs(v.H / (float)v.G - 0.5f));
        var worst = counts.OrderByDescending(kv => Mathf.Abs(kv.Value.H / (float)kv.Value.G - 0.5f)).First();

        GD.Print($"games per club: {minG}-{maxG}   (every club equal: {(minG == maxG ? "yes" : "NO")})");
        GD.Print($"worst home/away imbalance: {Teams.Get(worst.Key).Abbrev} " +
                 $"{worst.Value.H}H/{worst.Value.A}A = {worst.Value.H * 100 / worst.Value.G}% home");

        // --- Nobody plays twice on one date ---
        int doubleBooked = games.GroupBy(g => g.Day)
            .Sum(day => day.SelectMany(g => new[] { g.HomeId, g.AwayId })
                           .GroupBy(id => id).Count(x => x.Count() > 1));
        GD.Print($"clubs double-booked on a date: {doubleBooked}");

        // --- Series and rest ---
        var byDay = games.GroupBy(g => g.Day).ToDictionary(x => x.Key, x => x.ToList());
        int restDays = Enumerable.Range(0, maxDay + 1).Count(d => !byDay.ContainsKey(d));

        // Longest run of consecutive dates one club faces the same opponent at one venue.
        var seriesLengths = new List<int>();
        foreach (var t in Teams.All)
        {
            var mine = games.Where(g => g.Involves(t.Id)).OrderBy(g => g.Day).ToList();
            int run = 0;
            for (int i = 0; i < mine.Count; i++)
            {
                bool same = i > 0
                    && mine[i].HomeId == mine[i - 1].HomeId && mine[i].AwayId == mine[i - 1].AwayId
                    && mine[i].Day == mine[i - 1].Day + 1;
                run = same ? run + 1 : 1;
                if (i == mine.Count - 1 || !(i + 1 < mine.Count
                        && mine[i + 1].HomeId == mine[i].HomeId && mine[i + 1].AwayId == mine[i].AwayId
                        && mine[i + 1].Day == mine[i].Day + 1))
                    seriesLengths.Add(run);
            }
        }

        GD.Print($"league-wide rest dates: {restDays}");
        GD.Print($"series length: min {seriesLengths.Min()}  max {seriesLengths.Max()}  " +
                 $"average {seriesLengths.Average():F2}");

        // --- A club's own view of its first fortnight ---
        var club = Teams.Get(0);
        GD.Print($"\n{club.Abbrev} opening stretch:");
        foreach (var g in games.Where(g => g.Involves(0)).OrderBy(g => g.Day).Take(7))
            GD.Print($"  {Calendar.Format(Calendar.DateOf(g.Day)),-22} " +
                     $"{(g.HomeId == 0 ? "vs " + Teams.Get(g.AwayId).Abbrev : "at " + Teams.Get(g.HomeId).Abbrev)}");
    }
}
