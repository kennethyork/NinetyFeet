using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.Stats;

namespace SandlotSlugfest.Season;

/// <summary>
/// Creating a career, playing it out, and the organisation's decisions about the man.
///
/// The player never chooses where he plays. He is drafted somewhere, told which rung he is on, and
/// moved when the club decides he has earned it or when it has run out of patience — which is the
/// only thing that makes a promotion mean anything. What he controls is the at-bats.
/// </summary>
public static class CareerEngine
{
    /// <summary>The starting archetypes, so a new career is a real choice rather than a slider.</summary>
    public readonly record struct Build(string Name, string Blurb, Position Position,
        int Contact, int Power, int Speed, int Arm, int Fielding,
        int PitchPower, int PitchControl, int Stamina, int Ceiling);

    public static readonly Build[] Builds =
    {
        new("CONTACT HITTER", "Puts the bat on it. You will not hit many out.",
            Position.Second, 6, 2, 5, 4, 5, 1, 1, 1, 8),
        new("SLUGGER", "Enormous power, and you will strike out a great deal.",
            Position.Left, 3, 7, 2, 4, 3, 1, 1, 1, 8),
        new("FIVE-TOOL OUTFIELDER", "Good at everything, outstanding at nothing. Yet.",
            Position.Center, 4, 4, 6, 5, 5, 1, 1, 1, 9),
        new("SHORTSTOP", "A glove first. The bat has to catch up.",
            Position.Short, 4, 2, 6, 6, 7, 1, 1, 1, 8),
        new("POWER ARM", "You throw very hard and do not always know where.",
            Position.P, 1, 1, 2, 5, 3, 8, 3, 5, 9),
        new("CRAFTY LEFTY", "No velocity, excellent command, and a plan.",
            Position.P, 1, 1, 2, 4, 4, 4, 8, 6, 8),
    };

    /// <summary>Where a new man starts. Nobody starts in the majors.</summary>
    public static CareerState Create(string first, string last, Build build, Handedness bats,
        int teamId, int seed)
    {
        var rng = new Rng(seed);

        var c = new CareerState
        {
            FirstName = first,
            LastName = last,
            Position = build.Position,
            Bats = bats,
            TeamId = teamId,
            Level = Farm.Level.HighA,
            Year = 1,
            Age = 19,
        };

        c.Player = new PlayerData
        {
            Id = 990000,
            FirstName = first,
            LastName = last,
            Number = rng.Range(1, 76),
            Position = build.Position,
            Bats = bats,
            Throws = bats == Handedness.Left ? Handedness.Left : Handedness.Right,
            LookSeed = (int)rng.NextUInt(),
            Age = 19,
            Contact = build.Contact,
            Power = build.Power,
            Speed = build.Speed,
            Arm = build.Arm,
            Fielding = build.Fielding,
            PitchPower = build.PitchPower,
            PitchControl = build.PitchControl,
            Stamina = build.Stamina,
            // What the scouts said, plus or minus what they got wrong.
            //
            // A build handing out a guaranteed ceiling of eight or nine meant every career ended
            // in the majors — twenty-four out of twenty-four, measured. That is a conveyor belt,
            // not a career. The number on the build is what you were told when you were drafted;
            // the ceiling you actually have is rolled around it, so some men are better than the
            // report and some never get there. The game already hides a prospect's ceiling from
            // the club that drafted him; there is no reason it should not hide yours from you.
            // Scouts are optimistic, so the report sits above the truth on average and the truth
            // is spread widely around it. A ceiling of eight means "they think eight": you might
            // be a nine, and you might be a five who never gets out of Double-A.
            Potential = Mathf.Clamp(
                Mathf.RoundToInt(build.Ceiling - 1.5f + (rng.Bell() - 0.5f) * 6f), 2, 10),
            Salary = Contracts.Minimum,
            ContractYears = 1,
            Role = build.Position == Position.P ? StaffRole.Starter : StaffRole.Starter,
            Repertoire = 0b1111,
        };

        c.DraftCeiling = build.Ceiling;
        c.PeakOverall = c.Player.Overall;

        // He joins the organisation like anybody else, at the bottom.
        Farm.Of(teamId, Farm.Level.HighA).Add(c.Player);

        c.Note($"Drafted by {Teams.Get(teamId).FullName} and assigned to High-A.");
        return c;
    }

    // -----------------------------------------------------------------------
    // A game
    // -----------------------------------------------------------------------

    /// <summary>
    /// The side he plays for, built from wherever he is. He is inserted into the lineup because
    /// the whole point is that he plays.
    /// </summary>
    public static Roster SideFor(CareerState c, SeasonState season)
    {
        Roster side = c.Level == null
            ? season?.RosterFor(c.TeamId)
            : Farm.BuildRoster(c.TeamId, c.Level.Value);

        if (side == null) return null;

        // Make sure he is actually in it. A career where you are on the roster and not in the
        // lineup is a career spent watching.
        if (!side.BattingOrder.Contains(c.Player) && !c.IsPitcher)
        {
            if (side.BattingOrder.Count > 0) side.BattingOrder[0] = c.Player;
            else side.BattingOrder.Add(c.Player);

            side.Starters[c.Position] = c.Player;
            if (!side.Players.Contains(c.Player)) side.Players.Add(c.Player);
        }
        else if (c.IsPitcher)
        {
            if (!side.Pitchers.Contains(c.Player)) side.Pitchers.Insert(0, c.Player);
            if (!side.Players.Contains(c.Player)) side.Players.Add(c.Player);
            side.SetPitcher(c.Player);
        }

        return side;
    }

    /// <summary>Somebody to play. Another organisation at the same rung.</summary>
    public static Roster OpponentFor(CareerState c, SeasonState season, int seed)
    {
        var rng = new Rng(seed);
        int start = rng.Range(0, Teams.All.Count);

        for (int i = 0; i < Teams.All.Count; i++)
        {
            int other = (start + i) % Teams.All.Count;
            if (other == c.TeamId) continue;

            var side = c.Level == null
                ? season?.RosterFor(other)
                : Farm.BuildRoster(other, c.Level.Value);
            if (side != null) return side;
        }

        return null;
    }

    /// <summary>Folds one game's line into the season and the career.</summary>
    public static void BookGame(CareerState c, BattingLine game)
    {
        c.Season.Absorb(game);
        c.Career.Absorb(game);
        c.GamesThisYear++;
    }

    // -----------------------------------------------------------------------
    // The organisation's decisions
    // -----------------------------------------------------------------------

    /// <summary>
    /// The end of a career season: he ages, develops, and the club decides what to do with him.
    ///
    /// The bar for a promotion is deliberately about performance rather than about ratings — a man
    /// who hits at a level has earned the next one, which is how it actually works and is the only
    /// version where your at-bats are the thing that matters.
    /// </summary>
    public static List<string> EndSeason(CareerState c, SeasonState season, int seed)
    {
        var news = new List<string>();
        var rng = new Rng(seed * 3919 + c.Year * 17);

        float avg = c.Season.Average;
        float ops = c.Season.Ops;
        bool good = c.Season.AtBats >= 40 && (avg >= 0.290f || ops >= 0.800f);
        bool poor = c.Season.AtBats >= 40 && avg < 0.215f && ops < 0.620f;

        news.Add($"Season {c.Year}: {c.Season.Games} games, " +
                 $"{BattingLine.Rate(avg)} with {c.Season.HomeRuns} home runs, " +
                 $"{c.Season.RunsBattedIn} driven in.");

        // Development. A good year moves him faster, which is a real effect and also the right
        // incentive: your at-bats improve the player.
        c.Age++;
        c.Player.Age = c.Age;
        Development.DevelopProspect(c.Player, ref rng, c.TeamId);
        if (good)
        {
            // A step beyond the ordinary curve for a season that demanded attention.
            Nudge(c.Player, ref rng);
            news.Add("The organisation liked what it saw.");
        }

        // Promotion, or not.
        if (c.Level == null)
        {
            if (poor && rng.Chance(0.35f))
            {
                Farm.Of(c.TeamId, Farm.Level.TripleA).Add(c.Player);
                season?.RosterFor(c.TeamId).Players.Remove(c.Player);
                c.Level = Farm.Level.TripleA;
                news.Add("Optioned back to Triple-A. It happens.");
            }
        }
        else if ((good || c.Player.Overall >= Farm.ReadyOverall) && GoodEnoughFor(c))
        {
            var from = c.Level.Value;
            Farm.Of(c.TeamId, from).Remove(c.Player);

            if (from == Farm.Level.TripleA)
            {
                season?.RosterFor(c.TeamId).Players.Add(c.Player);
                if (c.IsPitcher) season?.RosterFor(c.TeamId).Pitchers.Add(c.Player);
                c.Level = null;
                news.Add($"CALLED UP. {Teams.Get(c.TeamId).FullName} want him in the majors.");
            }
            else
            {
                var to = from == Farm.Level.HighA ? Farm.Level.DoubleA : Farm.Level.TripleA;
                Farm.Of(c.TeamId, to).Add(c.Player);
                c.Level = to;
                news.Add($"Promoted to {Farm.Name(to)}.");
            }
        }
        else if (poor)
        {
            news.Add("Held back. They want to see more.");
        }

        if (c.Player.Overall > c.PeakOverall) c.PeakOverall = c.Player.Overall;

        // Released.
        //
        // Every one of the first twelve careers played out reached the majors, which is not a
        // career mode, it is a conveyor belt — the only way out was old age, so everybody who kept
        // turning up eventually got a good enough season to climb. An organisation does not wait
        // for ever. Past the age a level is for, without the rating that level promotes on, he is
        // let go, which is what happens to most men who are drafted.
        if (!c.InTheMajors && !c.Retired)
        {
            int tooOld = c.Level switch
            {
                Farm.Level.HighA => 24,
                Farm.Level.DoubleA => 26,
                _ => 28,
            };

            if (c.Age > tooOld && !GoodEnoughFor(c))
            {
                c.Retired = true;
                c.EndedBecause = "released";
                news.Add($"Released at {c.Age}. {Farm.Name(c.Level.Value)} is as far as it went.");
            }
        }

        // Retirement, eventually.
        if (!c.Retired && c.Age >= 38 && (c.Player.Overall <= 3 || rng.Chance((c.Age - 37) * 0.22f)))
        {
            c.Retired = true;
            c.EndedBecause = "retired";
            news.Add($"Retired at {c.Age} after {c.Career.Games} games.");
        }

        foreach (string line in news) c.Note(line);

        c.Year++;
        c.GamesThisYear = 0;
        ResetLine(c.Season);

        return news;
    }

    /// <summary>
    /// Whether he is good enough for the rung above the one he is on.
    ///
    /// A promotion used to need only a hot forty games, and forty games carries enough noise that
    /// anybody clears it eventually — measured, forty careers out of forty reached the majors,
    /// which is not a career mode. Hitting is necessary and it is not sufficient: you also have to
    /// be the player the next level is for. A man who cannot get there stalls, ages out and is
    /// released, which is what happens to most of the people who are drafted.
    /// </summary>
    private static bool GoodEnoughFor(CareerState c) => c.Player.Overall >= (c.Level switch
    {
        Farm.Level.HighA => 4,      // to Double-A
        Farm.Level.DoubleA => 5,    // to Triple-A
        _ => 6,                     // to the big club
    });

    /// <summary>One extra rating point for a season worth noticing.</summary>
    private static void Nudge(PlayerData p, ref Rng rng)
    {
        if (p.Position == Position.P)
        {
            if (rng.Chance(0.5f)) p.PitchControl = Mathf.Min(10, p.PitchControl + 1);
            else p.PitchPower = Mathf.Min(10, p.PitchPower + 1);
            return;
        }

        switch (rng.Range(0, 3))
        {
            case 0: p.Contact = Mathf.Min(10, p.Contact + 1); break;
            case 1: p.Power = Mathf.Min(10, p.Power + 1); break;
            default: p.Fielding = Mathf.Min(10, p.Fielding + 1); break;
        }
    }

    private static void ResetLine(BattingLine b)
    {
        b.Games = b.PlateAppearances = b.AtBats = b.Hits = 0;
        b.Doubles = b.Triples = b.HomeRuns = 0;
        b.Runs = b.RunsBattedIn = b.Walks = b.Strikeouts = b.StolenBases = 0;
    }

    /// <summary>How the club currently sees him, which is what a career mode is really about.</summary>
    public static string Standing(CareerState c)
    {
        if (c.Retired) return "Retired.";
        if (c.InTheMajors) return "In the majors.";

        int bar = c.Level switch
        {
            Farm.Level.TripleA => 6,
            Farm.Level.DoubleA => 5,
            _ => 4,
        };

        int gap = bar - c.Player.Overall;
        return gap <= 0
            ? "Ready for the next rung — a good season should do it."
            : $"They want to see more. About {gap} rating point(s) short of the next level.";
    }
}
