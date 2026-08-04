using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>
/// Injuries. A season where the same nine men play every game is not a season a manager has to
/// manage — losing your shortstop for three weeks is what forces the decisions that make a
/// franchise mode interesting.
/// </summary>
public static class Injuries
{
    private static readonly (string Name, int Min, int Max, float Weight)[] Ailments =
    {
        ("tight hamstring",     3, 10, 0.30f),
        ("jammed thumb",        2,  7, 0.20f),
        ("sore shoulder",       5, 18, 0.16f),
        ("strained oblique",    8, 25, 0.12f),
        ("sprained ankle",      6, 20, 0.10f),
        ("back spasms",         4, 14, 0.07f),
        ("elbow inflammation", 14, 45, 0.05f),
    };

    /// <summary>
    /// Chance per player per game of picking something up. Pitchers break down more often, and
    /// older players more often again.
    /// </summary>
    private static float Risk(PlayerData p)
    {
        float baseRisk = p.Position == Data.Position.P ? 0.0055f : 0.0035f;
        float age = 1f + Mathf.Max(0, p.Age - 28) * 0.09f;

        // Durability tracks stamina for arms and fielding-adjacent athleticism for everyone else.
        float durable = 1f - (p.Position == Data.Position.P ? p.Stamina : p.Speed) / 10f * 0.35f;
        return baseRisk * age * durable;
    }

    /// <summary>Ticks a day of the calendar for one club: heal the hurt, and hurt the healthy.</summary>
    public static void Advance(Roster roster, ref Rng rng)
    {
        foreach (var p in roster.Players)
        {
            if (p.DaysOut > 0)
            {
                p.DaysOut--;
                if (p.DaysOut == 0) p.Injury = "";
                continue;
            }

            if (!rng.Chance(Risk(p))) continue;

            float roll = rng.NextFloat();
            float acc = 0f;
            foreach (var (name, min, max, weight) in Ailments)
            {
                acc += weight;
                if (roll > acc) continue;
                p.Injury = name;
                p.DaysOut = rng.Range(min, max + 1);
                break;
            }
        }

        // A club that has lost a starter promotes from its own bench rather than playing short.
        Cover(roster);
    }

    /// <summary>Fills any lineup spot or rotation slot left empty by an injury.</summary>
    public static void Cover(Roster roster)
    {
        foreach (var spot in roster.Starters.Keys.ToList())
        {
            // The mound is filled from the staff below, not from the bench — this loop would
            // otherwise answer an injured starter by sending the backup catcher out to pitch.
            if (spot == Data.Position.P) continue;

            var starter = roster.Starters[spot];
            if (!starter.IsInjured) continue;

            var fill = roster.Players
                .Where(p => !p.IsInjured && p != starter && !roster.Starters.ContainsValue(p)
                            && !roster.Pitchers.Contains(p))
                .OrderByDescending(p => (p.Position == spot ? 3 : 0) + p.Overall)
                .FirstOrDefault();
            if (fill == null) continue;

            roster.Starters[spot] = fill;
            int at = roster.BattingOrder.IndexOf(starter);
            if (at >= 0) roster.BattingOrder[at] = fill;
        }

        // The rotation always leads with someone who can actually take the ball. A healthy
        // starter first — reaching for the closer here would burn him on the first pitch.
        if (roster.CurrentPitcher is { IsInjured: true })
        {
            var arm = roster.Rotation.FirstOrDefault(p => !p.IsInjured)
                      ?? roster.Pitchers.OrderByDescending(p => p.Stamina)
                                        .FirstOrDefault(p => !p.IsInjured);
            if (arm != null) roster.SetPitcher(arm);
        }
    }

    /// <summary>Everyone starts a new season healthy.</summary>
    public static void ClearAll(SeasonState season)
    {
        foreach (var r in season.AllRosters)
            foreach (var p in r.Players) { p.DaysOut = 0; p.Injury = ""; }
    }
}
