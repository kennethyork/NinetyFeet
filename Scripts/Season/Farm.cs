using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.Stats;

namespace SandlotSlugfest.Season;

/// <summary>
/// The farm system: each club's Triple-A affiliate.
///
/// Before this a draft pick either walked straight onto the big club or evaporated. That made the
/// draft a formality — you were asked to bet on a ceiling with nowhere to develop it and no way to
/// watch it happen. A prospect now goes down, plays a season against other prospects, puts up
/// numbers you can read, and comes up when he is ready or when somebody gets hurt.
///
/// Minor-league games are not simulated pitch by pitch; that would multiply the league's work by
/// three for numbers nobody plays. They are modelled from the player's ratings against the real
/// Triple-A run environment in <see cref="RealBaseball.TripleA"/>, which is a livelier league than
/// the majors — a hitter's line down there reads better than the same hitter's would up here, and
/// that is exactly the trap a scout has to see through.
/// </summary>
public static class Farm
{
    /// <summary>
    /// The rungs of the ladder, best first.
    ///
    /// One level was better than none, but it made every prospect the same kind of prospect: a man
    /// one step from the majors. A real organisation is a queue — a nineteen-year-old drafted this
    /// summer is four years and three promotions away from anything, and the interesting question
    /// is which of the men below Triple-A are worth waiting for.
    /// </summary>
    public enum Level { TripleA, DoubleA, HighA }

    public static readonly Level[] Levels = { Level.TripleA, Level.DoubleA, Level.HighA };

    public static string Name(Level level) => level switch
    {
        Level.TripleA => "Triple-A",
        Level.DoubleA => "Double-A",
        _ => "High-A",
    };

    /// <summary>How many players each affiliate carries. The lower rungs are wider.</summary>
    public static int SizeOf(Level level) => level switch
    {
        Level.TripleA => 14, Level.DoubleA => 16, _ => 18,
    };

    /// <summary>Kept for the many callers that only ever meant Triple-A.</summary>
    public const int Size = 14;

    /// <summary>A prospect this good is ready, whatever his age.</summary>
    public const int ReadyOverall = 7;

    /// <summary>What a man has to be to earn the next rung up.</summary>
    private static int PromotionBar(Level to) => to switch
    {
        Level.TripleA => 6, Level.DoubleA => 5, _ => 1,
    };

    private static readonly Dictionary<int, List<PlayerData>> Rosters = new();

    private static int Key(int teamId, Level level) => teamId * 10 + (int)level;

    /// <summary>One affiliate, created empty if it does not exist yet.</summary>
    public static List<PlayerData> Of(int teamId, Level level)
    {
        int key = Key(teamId, level);
        if (Rosters.TryGetValue(key, out var r)) return r;
        r = new List<PlayerData>();
        Rosters[key] = r;
        return r;
    }

    /// <summary>The club's Triple-A side — the rung the majors actually draw from.</summary>
    public static List<PlayerData> Of(int teamId) => Of(teamId, Level.TripleA);

    /// <summary>Everyone in the organisation below the big club.</summary>
    public static IEnumerable<PlayerData> AllOf(int teamId) =>
        Levels.SelectMany(l => Of(teamId, l));

    /// <summary>Which rung a player is on, or null if he is not in the system.</summary>
    public static Level? LevelOf(int teamId, PlayerData p)
    {
        foreach (var l in Levels)
            if (Of(teamId, l).Contains(p)) return l;
        return null;
    }

    public static void Clear() => Rosters.Clear();

    /// <summary>Fills every affiliate when a league is created.</summary>
    public static void Stock(SeasonState season, int seed)
    {
        Clear();
        var rng = new Rng(seed * 6151 + 907);

        foreach (var t in Teams.All)
            foreach (var level in Levels)
            {
                var farm = Of(t.Id, level);
                for (int i = 0; i < SizeOf(level); i++)
                {
                    var p = RosterGenerator.Prospect(
                        200000 + t.Id * 100 + (int)level * 30 + i, ref rng,
                        i % 4 == 0 ? Data.Position.P : null);

                    p.ServiceYears = 0;
                    p.Salary = Contracts.Minimum;
                    p.ContractYears = 1;

                    // The lower the rung, the younger and rawer the men on it. A High-A side is
                    // teenagers and twenty-year-olds; Triple-A is men waiting for a phone call.
                    p.Age = level switch
                    {
                        Level.TripleA => rng.Range(22, 27),
                        Level.DoubleA => rng.Range(20, 25),
                        _ => rng.Range(18, 22),
                    };

                    farm.Add(p);
                }
            }
    }

    /// <summary>
    /// Roster spots at each rung — the cap, as against <see cref="SizeOf"/>, which is only how many
    /// men the organisation stocks a level with at the start.
    ///
    /// The two have to be different numbers. A level stocked to its own limit has nowhere to put
    /// anybody, so optioning a man down would always have failed on a full farm; the headroom is
    /// what makes the farm somewhere you can actually move players to. Lower rungs carry more,
    /// which is how it works in life — the bottom of an organisation is wide.
    /// </summary>
    public static int Spots(Level level) => SizeOf(level) + 8;

    /// <summary>Spots left at a rung.</summary>
    public static int Free(int teamId, Level level) =>
        Mathf.Max(0, Spots(level) - Of(teamId, level).Count);

    /// <summary>How full a rung is, for the farm screen.</summary>
    public static string SpotsText(int teamId, Level level) =>
        $"{Of(teamId, level).Count}/{Spots(level)}";

    /// <summary>Sends a player down. He keeps his contract; he simply is not on the big club.</summary>
    public static bool SendDown(SeasonState season, int teamId, PlayerData p,
        Level level = Level.TripleA)
    {
        var roster = season.RosterFor(teamId);
        if (!roster.Players.Contains(p)) return false;

        // A club still has to be able to field a side.
        if (roster.Players.Count <= Development.RosterLimit - 2) return false;

        // And the rung he is going to has to have room for him.
        if (Free(teamId, level) <= 0) return false;

        Release(roster, p);
        Of(teamId, level).Add(p);
        TradeEngine.Rebuild(roster);
        return true;
    }

    /// <summary>Moves a man between two rungs of the same organisation.</summary>
    public static bool Move(int teamId, PlayerData p, Level to)
    {
        var from = LevelOf(teamId, p);
        if (from == null || from == to) return false;
        if (Free(teamId, to) <= 0) return false;

        Of(teamId, from.Value).Remove(p);
        Of(teamId, to).Add(p);
        return true;
    }

    /// <summary>Calls a player up to the big club from wherever he is.</summary>
    public static bool CallUp(SeasonState season, int teamId, PlayerData p)
    {
        var at = LevelOf(teamId, p);
        if (at == null) return false;

        var farm = Of(teamId, at.Value);
        if (!farm.Remove(p)) return false;

        var roster = season.RosterFor(teamId);
        roster.Players.Add(p);
        if (p.Position == Data.Position.P) roster.Pitchers.Add(p);
        TradeEngine.Rebuild(roster);
        return true;
    }

    private static void Release(Roster roster, PlayerData p)
    {
        roster.Players.Remove(p);
        roster.Pitchers.Remove(p);
        roster.BattingOrder.Remove(p);
        foreach (var spot in roster.Starters.Where(s => s.Value == p).Select(s => s.Key).ToList())
            roster.Starters.Remove(spot);
    }

    /// <summary>
    /// Plays the affiliate's season in one pass and books the numbers, so a prospect has a line
    /// you can read rather than only a rating you have to trust.
    /// </summary>
    public static void PlaySeason(SeasonState season, int games, int seed)
    {
        var rng = new Rng(seed * 3121 + 55);
        var aaa = RealBaseball.TripleA;

        foreach (var t in Teams.All)
            foreach (var level in Levels)
            {
                // The lower the rung, the tougher it plays for a hitter: the pitching is wilder
                // but the ballparks are bigger and the bats are worse. A .300 in High-A and a
                // .300 in Triple-A are not remotely the same achievement, and a scout's whole job
                // is knowing that.
                float harshness = level switch
                {
                    Level.TripleA => 1.00f, Level.DoubleA => 0.955f, _ => 0.915f,
                };

                foreach (var p in Of(t.Id, level))
                {
                    if (p.Position == Data.Position.P)
                        PitchAaaSeason(season.Book, p, aaa, games, ref rng);
                    else
                        BatAaaSeason(season.Book, p, aaa, games, ref rng, harshness);
                }
            }
    }

    /// <summary>
    /// A hitter's year in Triple-A. Built from the league's real rates, tilted by how good he is:
    /// an eight hits a hundred points above the league and a three is overmatched.
    /// </summary>
    private static void BatAaaSeason(StatBook book, PlayerData p, RealBaseball.League aaa,
        int games, ref Rng rng, float harshness = 1f)
    {
        // A regular plays most days. Bench prospects get fewer at-bats, as they should.
        int played = Mathf.RoundToInt(games * Mathf.Clamp(0.42f + p.Overall / 10f * 0.52f, 0.3f, 0.94f));
        int pa = Mathf.RoundToInt(played * 4.3f);
        if (pa <= 0) return;

        float quality = (p.Overall - 5) / 5f;                       // -0.8 .. +1.0
        float avg = Mathf.Clamp((aaa.Average + quality * 0.052f + (rng.Bell() - 0.5f) * 0.045f)
            * harshness, 0.150f, 0.380f);
        float walkRate = Mathf.Clamp(aaa.Walks / 2f / 38f + p.Contact / 10f * 0.035f, 0.03f, 0.18f);
        float kRate = Mathf.Clamp(aaa.Strikeouts / 2f / 38f - p.Contact / 10f * 0.09f + 0.04f,
            0.08f, 0.40f);

        int walks = Mathf.RoundToInt(pa * walkRate);
        int strikeouts = Mathf.RoundToInt(pa * kRate);
        int atBats = Mathf.Max(1, pa - walks);
        int hits = Mathf.RoundToInt(atBats * avg);

        int homers = Mathf.RoundToInt(atBats * Mathf.Clamp(0.012f + p.Power / 10f * 0.052f, 0f, 0.09f));
        int triples = Mathf.RoundToInt(hits * 0.022f * (0.4f + p.Speed / 10f));
        int doubles = Mathf.RoundToInt(hits * 0.195f);
        homers = Mathf.Min(homers, hits);
        doubles = Mathf.Min(doubles, hits - homers);
        triples = Mathf.Min(triples, hits - homers - doubles);

        var line = book.MinorBatting(p);
        line.Games = played;
        line.PlateAppearances = pa;
        line.AtBats = atBats;
        line.Hits = hits;
        line.Doubles = doubles;
        line.Triples = triples;
        line.HomeRuns = homers;
        line.Walks = walks;
        line.Strikeouts = strikeouts;
        line.Runs = Mathf.RoundToInt(hits * 0.46f + walks * 0.25f);
        line.RunsBattedIn = Mathf.RoundToInt(hits * 0.42f + homers * 1.1f);
        line.StolenBases = Mathf.RoundToInt(played * 0.02f * p.Speed);
    }

    private static void PitchAaaSeason(StatBook book, PlayerData p, RealBaseball.League aaa,
        int games, ref Rng rng)
    {
        bool starts = p.Stamina >= 5;
        int appearances = starts ? Mathf.RoundToInt(games / 5.2f) : Mathf.RoundToInt(games * 0.42f);
        float inningsEach = starts ? 4.6f + p.Stamina / 10f * 1.6f : 1.2f;
        int outs = Mathf.Max(0, Mathf.RoundToInt(appearances * inningsEach * 3f));
        if (outs <= 0) return;

        float quality = (p.Overall - 5) / 5f;
        float era = Mathf.Clamp(aaa.Runs / 2f * 27f / 27f - quality * 1.55f
                                + (rng.Bell() - 0.5f) * 1.10f, 1.40f, 9.50f);

        var line = book.MinorPitching(p);
        line.Games = appearances;
        line.GamesStarted = starts ? appearances : 0;
        line.Outs = outs;
        line.EarnedRuns = Mathf.RoundToInt(era * outs / 27f);
        line.Runs = Mathf.RoundToInt(line.EarnedRuns * 1.09f);
        line.Strikeouts = Mathf.RoundToInt(outs / 3f * (0.72f + p.PitchPower / 10f * 0.62f));
        line.Walks = Mathf.RoundToInt(outs / 3f * Mathf.Clamp(0.75f - p.PitchControl / 10f * 0.42f,
            0.15f, 0.75f));
        line.Hits = Mathf.RoundToInt(outs / 3f * Mathf.Clamp(1.20f - quality * 0.30f, 0.5f, 1.9f));
        line.HomeRunsAllowed = Mathf.RoundToInt(outs / 27f * Mathf.Clamp(1.5f - quality, 0.3f, 2.6f));
        line.Wins = starts ? Mathf.RoundToInt(appearances * Mathf.Clamp(0.30f + quality * 0.22f, 0.1f, 0.7f)) : 0;
        line.Losses = starts ? Mathf.Max(0, appearances / 3 - line.Wins / 2) : 0;
    }

    /// <summary>
    /// The winter: prospects age and develop alongside the big club, the best of them force their
    /// way up, and the affiliate is restocked. Returns what the user should be told about.
    /// </summary>
    public static List<string> RunOffseason(SeasonState season, int seed)
    {
        var rng = new Rng(seed * 991 + 13);
        var news = new List<string>();

        foreach (var t in Teams.All)
        {
            var roster = season.RosterFor(t.Id);

            // Everyone ages, and the men who are never going to make it stop being prospects.
            foreach (var level in Levels)
                foreach (var p in Of(t.Id, level).ToList())
                {
                    p.Age++;
                    Development.DevelopProspect(p, ref rng);

                    int ceiling = level == Level.HighA ? 24 : level == Level.DoubleA ? 26 : 28;
                    if (p.Age > ceiling && p.Overall < PromotionBar(level == Level.TripleA
                            ? Level.TripleA : level - 1))
                        Of(t.Id, level).Remove(p);
                }

            // Then the ladder moves, bottom rung first, so a man can climb more than one step in
            // a winter if he has earned it and there is room above him.
            for (int i = Levels.Length - 1; i >= 1; i--)
            {
                var below = Levels[i];
                var above = Levels[i - 1];

                var climbing = Of(t.Id, below)
                    .Where(p => p.Overall >= PromotionBar(above))
                    .OrderByDescending(p => p.Overall)
                    .Take(Mathf.Max(0, SizeOf(above) - Of(t.Id, above).Count) + 3)
                    .ToList();

                foreach (var p in climbing)
                {
                    if (Of(t.Id, above).Count >= SizeOf(above)) break;
                    Move(t.Id, p, above);
                }
            }

            // And the best of Triple-A gets the phone call, if the big club has room.
            var ready = Of(t.Id, Level.TripleA)
                .Where(p => p.Overall >= ReadyOverall || (p.Age >= 24 && p.Overall >= 6))
                .OrderByDescending(p => p.Overall)
                .Take(3)
                .ToList();

            foreach (var p in ready)
            {
                if (roster.Players.Count >= Development.RosterLimit) break;
                if (!CallUp(season, t.Id, p)) continue;
                news.Add($"{t.Abbrev} promote {p.Name} ({PlayerData.PositionLabel(p.Position)}, " +
                         $"overall {p.Overall}) from Triple-A.");
            }

            // New men come in at the bottom, which is where they come in.
            foreach (var level in Levels)
            {
                var farm = Of(t.Id, level);
                while (farm.Count < SizeOf(level))
                {
                    bool needArm = farm.Count(p => p.Position == Data.Position.P) < 5;
                    var p = RosterGenerator.Prospect(
                        300000 + t.Id * 1000 + season.Year * 37 + (int)level * 91 + farm.Count,
                        ref rng, needArm ? Data.Position.P : null);

                    p.Salary = Contracts.Minimum;
                    p.ContractYears = 1;
                    p.Age = level switch
                    {
                        Level.TripleA => rng.Range(22, 26),
                        Level.DoubleA => rng.Range(20, 24),
                        _ => rng.Range(18, 21),
                    };
                    farm.Add(p);
                }
            }
        }

        return news;
    }
}
