using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>
/// Verification harness for <see cref="Development"/>. Potential used to be a number the game
/// showed and never honoured, so the point of this is to prove a career arc really happens rather
/// than assume it: young players climbing, veterans declining, and rosters turning over.
/// </summary>
public static class CareerAudit
{
    public static void Run(SeasonState season, int years)
    {
        // Snapshot the young players with real upside, and follow them.
        // Peak is what matters: a player who reached his ceiling at twenty-seven and then declined
        // has still fulfilled his potential. Measuring only the final year would call that a miss.
        var peak = new Dictionary<PlayerData, int>();
        var tracked = new List<(PlayerData P, int StartAge, int StartOvr, int Ceiling)>();
        foreach (var r in season.AllRosters)
            foreach (var p in r.Players)
                if (p.Age <= 24 && p.Potential - p.Overall >= 2)
                {
                    tracked.Add((p, p.Age, p.Overall, p.Potential));
                    peak[p] = p.Overall;
                }

        GD.Print($"\n=== CAREER AUDIT — {years} offseasons ===");
        GD.Print($"tracking {tracked.Count} young players with a ceiling at least 2 above their rating");

        int retiredTotal = 0;
        for (int y = 1; y <= years; y++)
        {
            var report = Development.RunOffseason(season, 1000 + y);
            int retired = report.Count(r => r.Graduated);
            int improved = report.Count(r => r.After > r.Before);
            int declined = report.Count(r => r.After < r.Before);
            retiredTotal += retired;

            foreach (var t in tracked)
                if (t.P.Overall > peak[t.P]) peak[t.P] = t.P.Overall;

            var ages = season.AllRosters.SelectMany(r => r.Players).Select(p => p.Age).ToList();
            GD.Print($"year {y,2}: improved {improved,4}  declined {declined,4}  retired {retired,3}  " +
                     $"avg age {ages.Average():F1}  (min {ages.Min()} max {ages.Max()})");
        }

        int reached = tracked.Count(t => peak[t.P] >= t.Ceiling);
        int close = tracked.Count(t => peak[t.P] >= t.Ceiling - 1);
        int grew = tracked.Count(t => peak[t.P] > t.StartOvr);
        float avgGain = tracked.Count > 0
            ? (float)tracked.Average(t => peak[t.P] - t.StartOvr) : 0f;

        GD.Print($"\nof the {tracked.Count} tracked young players:");
        GD.Print($"  improved at all:        {grew} ({grew * 100 / Mathf.Max(1, tracked.Count)}%)");
        GD.Print($"  reached their ceiling:  {reached} ({reached * 100 / Mathf.Max(1, tracked.Count)}%)");
        GD.Print($"  within one of it:       {close} ({close * 100 / Mathf.Max(1, tracked.Count)}%)");
        GD.Print($"  average peak gain:      {avgGain:F2}");
        GD.Print($"  total retirements:      {retiredTotal}");

        var best = tracked.OrderByDescending(t => peak[t.P] - t.StartOvr).Take(4);
        GD.Print("\nbiggest risers:");
        foreach (var t in best)
            GD.Print($"  {t.P.Name,-24} age {t.StartAge}->{t.P.Age}  OVR {t.StartOvr} -> peak {peak[t.P]} " +
                     $"-> now {t.P.Overall}  (ceiling was {t.Ceiling})");
    }
}
