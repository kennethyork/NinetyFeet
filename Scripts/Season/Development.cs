using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>What happened to one player over an offseason, for the report screen.</summary>
public struct Progress
{
    public PlayerData Player;
    public int TeamId;
    public int Before, After;      // overall rating
    public bool Graduated;
    public string Note;
}

/// <summary>
/// The offseason: everyone gets a year older, and a year better or worse.
///
/// Until now <see cref="PlayerData.Potential"/> was generated, displayed with a letter grade and
/// then never used for anything — the draft asked you to bet on a ceiling the game had no way of
/// ever reaching. This is what makes that promise real, and it is the shape of a real career: a
/// young player climbs toward his ceiling, peaks around twenty-seven, holds it for a few seasons,
/// then declines and eventually retires.
/// </summary>
public static class Development
{
    /// <summary>
    /// How many players a club carries. Without a cap the league grew by two per team per year
    /// from the draft alone — after eight seasons rosters had doubled and the average age was
    /// falling every year, because nobody was ever released.
    /// </summary>
    public const int RosterLimit = 26;

    /// <summary>Arms a club has to carry: five to start and eight to relieve.</summary>
    public const int StaffSize = 13;

    /// <summary>A professional career: debut around twenty-one, done by the early forties.</summary>
    public const int RookieAge = 21;
    public const int PeakAge = 27;
    public const int MaxAge = 42;

    /// <summary>
    /// How much of the remaining gap to his ceiling a player closes in one year. Fastest in the
    /// early twenties and gone by the peak, which is what makes a 22-year-old with a high ceiling
    /// worth more than a 26-year-old with the same one.
    /// </summary>
    private static float GrowthRate(int age) => age switch
    {
        <= 21 => 0.42f,
        22 => 0.38f,
        23 => 0.32f,
        24 => 0.26f,
        25 => 0.19f,
        26 => 0.12f,
        27 => 0.06f,
        _ => 0f,
    };

    /// <summary>
    /// Rating points lost per year once a player is past his prime. Gentle at first, then steep —
    /// the shape that makes a thirty-five-year-old a risk rather than merely older.
    /// </summary>
    private static float DeclineRate(int age) => age switch
    {
        <= 29 => 0f,
        30 => 0.4f,
        31 => 0.7f,
        32 => 1.0f,
        33 => 1.4f,
        34 => 1.8f,
        35 => 2.3f,
        36 => 2.8f,
        _ => 3.4f,
    };

    /// <summary>The eight ratings that actually develop, and how to read and write each.</summary>
    private static readonly (System.Func<PlayerData, int> Get, System.Action<PlayerData, int> Set)[] Ratings =
    {
        (p => p.Contact,      (p, v) => p.Contact = v),
        (p => p.Power,        (p, v) => p.Power = v),
        (p => p.Speed,        (p, v) => p.Speed = v),
        (p => p.Arm,          (p, v) => p.Arm = v),
        (p => p.Fielding,     (p, v) => p.Fielding = v),
        (p => p.PitchPower,   (p, v) => p.PitchPower = v),
        (p => p.PitchControl, (p, v) => p.PitchControl = v),
        (p => p.Stamina,      (p, v) => p.Stamina = v),
    };

    /// <summary>
    /// Ages and develops every player in the league, graduates anyone too old, and refills the
    /// clubs from the prospect pool. Returns a report, most improved first.
    /// </summary>
    public static List<Progress> RunOffseason(SeasonState season, int seed)
    {
        var rng = new Rng(seed * 2411 + 17);
        var report = new List<Progress>();

        foreach (var team in Teams.All)
        {
            var roster = season.RosterFor(team.Id);

            foreach (var p in roster.Players.ToList())
            {
                int before = p.Overall;
                p.Age++;

                if (ShouldRetire(p, ref rng))
                {
                    p.Retired = true;
                    report.Add(new Progress
                    {
                        Player = p, TeamId = team.Id, Before = before, After = before,
                        Graduated = true, Note = $"retired at {p.Age}",
                    });
                    continue;
                }

                // The coach you hired, and the man himself. Two players with the same ratings and
                // the same coach no longer improve at the same rate, which is the whole reason to
                // prefer one prospect over another.
                Develop(p, ref rng,
                    Coaches.DevelopmentFactor(team.Id, p) * Temperament.GrowthFactor(p));

                int after = p.Overall;
                report.Add(new Progress
                {
                    Player = p, TeamId = team.Id, Before = before, After = after,
                    Note = after > before ? "improved" : after < before ? "slipped" : "held steady",
                });
            }

            // Everyone who retired leaves.
            foreach (var p in roster.Players.Where(p => p.Retired).ToList()) Remove(roster, p);

            // Then the club trims to its limit, releasing the least useful first. A veteran who
            // has slipped goes before a young player with something still ahead of him.
            while (roster.Players.Count > RosterLimit)
            {
                var cut = roster.Players
                    .OrderBy(p => p.Overall + Mathf.Max(0, p.Potential - p.Overall) * 0.8f
                                  - (p.Age >= 33 ? 1.5f : 0f))
                    .FirstOrDefault(p => !Essential(roster, p));
                if (cut == null) break;

                Remove(roster, cut);
                report.Add(new Progress
                {
                    Player = cut, TeamId = team.Id, Before = cut.Overall, After = cut.Overall,
                    Graduated = true, Note = "released",
                });
            }

            // Refilling is no longer done here. Signing generated replacements at this point beat
            // free agency to the empty roster spots, so the winter market had nowhere to put
            // anyone and every club opened the year with a squad of strangers it had conjured
            // rather than one it had built. See FreeAgency.FillShortRosters.
            TradeEngine.Rebuild(roster);
        }

        report.Sort((a, b) => (b.After - b.Before).CompareTo(a.After - a.Before));
        return report;
    }

    /// <summary>
    /// Moves a player along his career curve: toward his ceiling while young, away from it once he
    /// is past his prime. A bad year is always possible — otherwise drafting for upside carries no
    /// risk at all.
    /// </summary>
    /// <summary>
    /// A winter in the minors. The same career curve as anyone else's — a young man climbs toward
    /// his ceiling — exposed so the farm system can put its prospects through it. Without this a
    /// prospect sat at the rating he was drafted with and never became anything, which made the
    /// whole ladder a filing cabinet rather than a development system.
    /// </summary>
    public static void DevelopProspect(PlayerData p, ref Rng rng, int teamId = -1) =>
        Develop(p, ref rng, teamId < 0 ? 1f : Coaches.DevelopmentFactor(teamId, p));

    /// <param name="coaching">
    /// What the club's staff is worth at this man's job — see <see cref="Coaches"/>. A club with
    /// nobody in the post develops worse than one with an ordinary coach, which is what makes the
    /// job worth paying for. Only growth is affected: no hitting coach on earth stops a
    /// thirty-eight-year-old declining.
    /// </param>
    private static void Develop(PlayerData p, ref Rng rng, float coaching = 1f)
    {
        float rate = GrowthRate(p.Age) * coaching;
        float decline = DeclineRate(p.Age);
        int gap = p.Potential - p.Overall;

        // Steps are individual rating points, but the gap is measured in Overall — and Overall is
        // an average of roughly five ratings, so one rating point moves it about 0.2. Without this
        // conversion a player closed a quarter of his gap over an entire career: the ceiling was
        // still, in practice, unreachable.
        const float RatingsPerOverall = 5f;

        int steps;
        if (decline > 0f)
        {
            // Past the peak. Older players lose more, and it varies year to year.
            steps = -Mathf.RoundToInt(decline * RatingsPerOverall * 0.45f * rng.Range(0.6f, 1.5f));
        }
        else
        {
            // Taken as a whole number of steps plus a chance at one more, rather than rounded.
            //
            // Rounding here quietly threw away every modifier smaller than half a step. A typical
            // young player works out at about 2.2 steps, so a coaching or work-ethic factor of
            // ±18% moved him to 1.98 or 2.42 and he took two either way — --people measured the
            // hardest workers in the league developing no faster than the men who coast, and the
            // reason was not the factor, it was this line. Flooring and rolling for the remainder
            // has the same expectation and lets a small factor actually land.
            // The guaranteed step is 0.65 rather than a whole one. Rounding was biased — round(2.2)
            // is 2, but flooring and rolling the remainder averages 2.2 — so switching to the
            // unbiased form quietly made everybody develop faster, and career mode went from 36
            // men in 40 reaching the majors to 39. This takes that back out.
            float raw = Mathf.Max(gap, 0) * rate * RatingsPerOverall + (rate > 0f ? 0.65f : 0f);
            steps = Mathf.FloorToInt(raw);
            if (rng.Chance(raw - steps)) steps++;

            // A poor year: some stall, some go backwards.
            if (rng.Chance(0.12f)) steps = -Mathf.Max(1, steps / 3);
        }

        for (int i = 0; i < Mathf.Abs(steps); i++)
        {
            // Weight growth toward what the player already is, so a slugger grows into a slugger
            // rather than drifting into a generic average.
            var candidates = Ratings
                .Select((r, idx) => (r, idx, val: r.Get(p)))
                .Where(x => steps > 0 ? x.val < 10 : x.val > 1)
                .ToList();
            if (candidates.Count == 0) break;

            // Prefer a rating that already matters for this player's game.
            var pick = candidates[rng.Range(0, candidates.Count)];
            for (int tries = 0; tries < 2; tries++)
            {
                var alt = candidates[rng.Range(0, candidates.Count)];
                bool altIsStrength = alt.val >= pick.val;
                if (steps > 0 == altIsStrength) pick = alt;
            }

            pick.r.Set(p, Mathf.Clamp(pick.val + (steps > 0 ? 1 : -1), 1, 10));
        }

        // Scouting sharpens with age: the ceiling drifts toward what he has actually become.
        if (p.Age >= 25)
            p.Potential = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(p.Potential, p.Overall, 0.35f)), 1, 10);
    }

    /// <summary>
    /// Whether a player hangs them up. Fringe players go early, stars hang on, and nobody plays
    /// past <see cref="MaxAge"/>.
    /// </summary>
    private static bool ShouldRetire(PlayerData p, ref Rng rng)
    {
        if (p.Age > MaxAge) return true;
        if (p.Age < 30) return false;

        // A good player at thirty-four is still wanted; a replacement-level one is not.
        float baseChance = (p.Age - 29) * 0.055f;
        float quality = Mathf.Clamp((p.Overall - 3) / 7f, 0f, 1f);
        return rng.Chance(Mathf.Clamp(baseChance * (1.35f - quality), 0f, 1f));
    }

    /// <summary>
    /// A club cannot release its way below a playable side: nine in the field and enough arms.
    /// </summary>
    private static bool Essential(Roster roster, PlayerData p)
    {
        if (p.Position == Data.Position.P) return roster.Pitchers.Count <= StaffSize;
        return roster.Players.Count(x => x.Position == p.Position) <= 1;
    }

    private static void Remove(Roster roster, PlayerData p)
    {
        roster.Players.Remove(p);
        roster.Pitchers.Remove(p);
        roster.BattingOrder.Remove(p);
        foreach (var spot in roster.Starters.Where(s => s.Value == p).Select(s => s.Key).ToList())
            roster.Starters.Remove(spot);
    }

    /// <summary>Brings in a replacement — a young player, the way a club restocks.</summary>
    private static void Sign(Roster roster, SeasonState season, ref Rng rng)
    {
        // Sign for what is missing. Left to chance a club that lost four arms over the winter
        // replaced them with four outfielders and went to camp with a nine-man staff.
        bool needArm = roster.Players.Count(p => p.Position == Data.Position.P) < StaffSize;

        // A written prospect if the reserve still has one; the generator only as a last resort.
        // The reserve is only drawn on when it happens to hold the kind of player the club needs.
        int id = 70000 + rng.Range(0, 900000);
        var p = Legends.DrawFromReserve(season.ReserveUsed, id, needArm)
                ?? RosterGenerator.Prospect(id, ref rng, needArm ? Data.Position.P : null);

        roster.Players.Add(p);
        if (p.Position == Data.Position.P) roster.Pitchers.Add(p);
    }
}
