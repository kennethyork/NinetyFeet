using System.Linq;
using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>
/// Checks that personality is a mechanic rather than a label.
///
/// It would be easy to add four numbers to a player, print them on his card, and have nothing in
/// the league behave differently — and impossible to notice, because a work ethic that does
/// nothing looks exactly like one that does. So this plays several seasons and asks whether the
/// hard workers actually improved more than the coasters, whether morale moves at all, and
/// whether it moves for reasons rather than drifting.
/// </summary>
public static class TemperamentAudit
{
    public static void Run(int seasons)
    {
        // Deliberately short seasons. What is being checked is whether personality does anything
        // across several winters, and a winter is a winter whether the year before it was 162
        // games or 40 — but a full schedule with three farm rungs underneath it takes minutes a
        // season, and four of those is long enough that nobody runs the harness. Playing time
        // thresholds below are scaled to match.
        const int Games = 40;

        var season = new SeasonState();
        season.StartNew(RosterGenerator.DefaultLeagueSeed, 0, Games, 9);

        var everyone = Teams.All.SelectMany(t => season.RosterFor(t.Id).Players).ToList();

        GD.Print($"\n=== PERSONALITY — {everyone.Count} players, {seasons} seasons ===");

        // --- Are they actually different from one another? ---
        GD.Print($"\n  work ethic  {Spread(everyone.Select(p => p.WorkEthic))}");
        GD.Print($"  loyalty     {Spread(everyone.Select(p => p.Loyalty))}");
        GD.Print($"  poise       {Spread(everyone.Select(p => p.Poise))}");
        GD.Print($"  morale      {Spread(everyone.Select(p => p.Morale))}");

        // Two men with the same personality everywhere would mean Assign never ran.
        int distinct = everyone.Select(p => p.WorkEthic * 1000 + p.Loyalty * 100 + p.Poise)
                               .Distinct().Count();
        GD.Print($"  distinct personalities: {distinct} of {everyone.Count}");

        // --- Follow the hard workers and the coasters through several winters. ---
        var grafters = everyone.Where(p => p.WorkEthic >= 7 && p.Age <= 26).ToList();
        var coasters = everyone.Where(p => p.WorkEthic <= 4 && p.Age <= 26).ToList();

        var startGraft = grafters.ToDictionary(p => p, p => p.Overall);
        var startCoast = coasters.ToDictionary(p => p, p => p.Overall);

        GD.Print($"\n  following {grafters.Count} young grafters and {coasters.Count} young coasters");
        GD.Print($"  ({Games}-game seasons, so almost nobody clears the played-enough bar — the");
        GD.Print($"   morale movement below is mostly winning, losing and contract years)");

        // One short season so there are numbers in the book for morale to be worked out from,
        // and then the winters on their own.
        //
        // Playing whole seasons for each winter is the obvious way to write this and it made the
        // harness take upwards of ten minutes, which is the same as not having it: a check nobody
        // runs catches nothing. Development is a function of the off-season and needs no games
        // played in front of it, so the winters are called directly.
        // AdvanceDay, not SimulateThrough: the latter deliberately leaves the user's own games
        // alone, so a loop waiting for every game to be played never finishes.
        for (int d = 0; d < Games * 2 && season.Games.Any(g => !g.Played); d++)
            season.AdvanceDay(simulateUserGame: true);

        Temperament.EndOfSeason(season);

        for (int y = 0; y < seasons; y++)
            Development.RunOffseason(season, season.LeagueSeed + y * 31);

        float graftGain = Gain(startGraft);
        float coastGain = Gain(startCoast);

        GD.Print($"  grafters gained  {graftGain:+0.00;-0.00} points of overall");
        GD.Print($"  coasters gained  {coastGain:+0.00;-0.00}");
        GD.Print($"  difference       {graftGain - coastGain:+0.00;-0.00}   " +
                 $"{(graftGain > coastGain ? "work ethic is doing something" : "WORK ETHIC IS DOING NOTHING")}");

        // --- Did morale move, and for reasons? ---
        var now = Teams.All.SelectMany(t => season.RosterFor(t.Id).Players).ToList();
        GD.Print($"\n  morale after {seasons} seasons: {Spread(now.Select(p => p.Morale))}");

        var winners = Teams.All.OrderByDescending(t => season.Book.Record(t.Id).WinPct).Take(6);
        var losers = Teams.All.OrderBy(t => season.Book.Record(t.Id).WinPct).Take(6);

        float happyAtGood = (float)winners.SelectMany(t => season.RosterFor(t.Id).Players)
                                          .DefaultIfEmpty().Average(p => p?.Morale ?? 5);
        float happyAtBad = (float)losers.SelectMany(t => season.RosterFor(t.Id).Players)
                                        .DefaultIfEmpty().Average(p => p?.Morale ?? 5);

        GD.Print($"  average morale at the six best clubs:  {happyAtGood:F2}");
        GD.Print($"  average morale at the six worst clubs: {happyAtBad:F2}");
        GD.Print($"  {(happyAtGood > happyAtBad ? "winning makes them happier, as it should" : "WINNING IS NOT MOVING MORALE")}");

        // --- What personality does to a contract. ---
        var cheap = now.OrderBy(x => Temperament.AskingFactor(x)).FirstOrDefault();
        var dear = now.OrderByDescending(x => Temperament.AskingFactor(x)).FirstOrDefault();
        if (cheap != null && dear != null)
        {
            GD.Print($"\n  cheapest to re-sign: {cheap.Name} at " +
                     $"{Temperament.AskingFactor(cheap) * 100f:F0}% of market — {Temperament.Summary(cheap)}");
            GD.Print($"  dearest to re-sign:  {dear.Name} at " +
                     $"{Temperament.AskingFactor(dear) * 100f:F0}% of market — {Temperament.Summary(dear)}");
        }

        GD.Print("\n  Morale deliberately does not touch a bat or an arm. A league whose run");
        GD.Print("  environment moves with how happy everybody is would be a league whose");
        GD.Print("  calibration cannot be trusted.");
    }

    private static float Gain(System.Collections.Generic.Dictionary<PlayerData, int> before)
    {
        var alive = before.Where(kv => !kv.Key.Retired).ToList();
        return alive.Count == 0 ? 0f : (float)alive.Average(kv => kv.Key.Overall - kv.Value);
    }

    private static string Spread(System.Collections.Generic.IEnumerable<int> values)
    {
        var v = values.ToList();
        if (v.Count == 0) return "none";
        return $"min {v.Min()}  mean {v.Average():F2}  max {v.Max()}   " +
               $"[{string.Join(" ", Enumerable.Range(0, 11).Select(n => v.Count(x => x == n)))}]";
    }
}
