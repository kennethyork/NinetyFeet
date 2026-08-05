using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Gameplay;

/// <summary>What a moment asks you to do.</summary>
public enum MomentGoal
{
    /// <summary>Drive in at least one run before the half inning ends.</summary>
    DriveARunIn,
    /// <summary>Tie it or take the lead.</summary>
    TieOrLead,
    /// <summary>Take the lead outright — a tie is not enough.</summary>
    WinIt,
    /// <summary>Record the outs without letting a run score.</summary>
    HoldTheLead,
    /// <summary>Get a hit. Any hit.</summary>
    GetAHit,
}

/// <summary>
/// One scripted situation, dropped into the middle of a ballgame.
///
/// A season asks for hours. A moment asks for one at-bat, and it is the only part of this game
/// that can be picked up and put down in ninety seconds — which is most of why The Show's version
/// is the mode people actually keep playing. It is also the only place the game can ask a specific
/// question: not "can you play baseball" but "can you drive in the tying run from second with two
/// out", which is a different and much sharper thing.
///
/// Every moment is built out of parts that already exist. There is no special rules engine here —
/// it sets up a real situation in a real game and then watches what happens to it.
/// </summary>
public sealed class Moment
{
    public string Name;
    public string Blurb;

    public int Inning = 9;
    public bool TopHalf;
    public int Outs = 2;

    /// <summary>Which bases have somebody on them.</summary>
    public bool OnFirst, OnSecond, OnThird;

    /// <summary>The score, from the player's point of view: his club first.</summary>
    public int MyRuns, TheirRuns;

    /// <summary>True when the player is batting; false when he is holding a lead.</summary>
    public bool Batting = true;

    public MomentGoal Goal = MomentGoal.TieOrLead;

    /// <summary>How many outs he is allowed to make trying. One at-bat, usually.</summary>
    public int OutsAllowed = 1;

    /// <summary>Paid on success — coins, and sometimes a pack.</summary>
    public int Coins = 900;
    public int Pack = -1;

    public string GoalText => Goal switch
    {
        MomentGoal.DriveARunIn => "Drive a run in.",
        MomentGoal.TieOrLead => "Tie it or take the lead.",
        MomentGoal.WinIt => "Take the lead.",
        MomentGoal.HoldTheLead => "Get out of it without a run scoring.",
        _ => "Get a hit.",
    };

    public string BaseText =>
        !OnFirst && !OnSecond && !OnThird ? "bases empty"
        : OnFirst && OnSecond && OnThird ? "bases loaded"
        : string.Join(" and ", new[]
        {
            OnThird ? "third" : null, OnSecond ? "second" : null, OnFirst ? "first" : null,
        }.Where(x => x != null));

    public string SituationText =>
        $"{(TopHalf ? "Top" : "Bottom")} {Inning}, {Outs} out, {BaseText} — " +
        $"{MyRuns}-{TheirRuns}";
}

/// <summary>The moments on offer, and the machinery for judging one.</summary>
public static class Moments
{
    public static readonly Moment[] All =
    {
        new()
        {
            Name = "BOTTOM OF THE NINTH",
            Blurb = "One run down, two out, the tying run ninety feet away. This is the one " +
                    "everybody pictures.",
            Inning = 9, TopHalf = false, Outs = 2, OnThird = true,
            MyRuns = 3, TheirRuns = 4,
            Goal = MomentGoal.TieOrLead, Coins = 1200,
        },
        new()
        {
            Name = "WALK-OFF",
            Blurb = "Tied, bottom of the ninth, a man on second and nobody out. Win it.",
            Inning = 9, TopHalf = false, Outs = 0, OnSecond = true,
            MyRuns = 5, TheirRuns = 5,
            Goal = MomentGoal.WinIt, OutsAllowed = 3, Coins = 1500, Pack = 0,
        },
        new()
        {
            Name = "BASES LOADED, NOBODY OUT",
            Blurb = "Everything is set up. All you have to do is not waste it.",
            Inning = 7, TopHalf = true, Outs = 0,
            OnFirst = true, OnSecond = true, OnThird = true,
            MyRuns = 2, TheirRuns = 2,
            Goal = MomentGoal.DriveARunIn, OutsAllowed = 3, Coins = 900,
        },
        new()
        {
            Name = "GET OUT OF IT",
            Blurb = "You are a run up in the eighth with two on and one out. Hold it.",
            Inning = 8, TopHalf = true, Outs = 1, OnFirst = true, OnSecond = true,
            MyRuns = 4, TheirRuns = 3,
            Batting = false, Goal = MomentGoal.HoldTheLead, OutsAllowed = 2, Coins = 1400,
        },
        new()
        {
            Name = "BREAK THE SLUMP",
            Blurb = "Nothing on the line. Just find a hit — leading off, nobody aboard.",
            Inning = 4, TopHalf = true, Outs = 0,
            MyRuns = 1, TheirRuns = 1,
            Goal = MomentGoal.GetAHit, OutsAllowed = 2, Coins = 600,
        },
        new()
        {
            Name = "SAVE SITUATION",
            Blurb = "Ninth inning, one run up, the tying run on second and nobody out. " +
                    "Three outs to get.",
            Inning = 9, TopHalf = true, Outs = 0, OnSecond = true,
            MyRuns = 6, TheirRuns = 5,
            Batting = false, Goal = MomentGoal.HoldTheLead, OutsAllowed = 3,
            Coins = 2200, Pack = 1,
        },
    };

    // -----------------------------------------------------------------------
    // Setting one up
    // -----------------------------------------------------------------------

    /// <summary>
    /// Forces a live game into the moment's situation.
    ///
    /// Everything here is state the game already keeps — the inning, the outs, the runners, the
    /// score. Nothing is special-cased in the rules; the moment simply starts somewhere other than
    /// the top of the first.
    /// </summary>
    public static void Apply(Moment m, GameSituation sit)
    {
        // The player's club bats in the half the moment names.
        bool playerIsHome = !m.TopHalf == m.Batting || (!m.Batting && m.TopHalf);

        sit.Inning = m.Inning;
        sit.TopHalf = m.TopHalf;
        sit.Outs = m.Outs;
        sit.Balls = 0;
        sit.Strikes = 0;

        var batting = m.TopHalf ? sit.Away : sit.Home;
        var fielding = m.TopHalf ? sit.Home : sit.Away;

        // Score, from whoever is batting.
        bool battingSideIsPlayers = m.Batting;
        int battingRuns = battingSideIsPlayers ? m.MyRuns : m.TheirRuns;
        int fieldingRuns = battingSideIsPlayers ? m.TheirRuns : m.MyRuns;

        if (m.TopHalf) { sit.AwayScore = battingRuns; sit.HomeScore = fieldingRuns; }
        else { sit.HomeScore = battingRuns; sit.AwayScore = fieldingRuns; }

        // Fill in a plausible line score so the scoreboard is not blank.
        FillLine(sit.AwayLine, sit.AwayScore, m.Inning);
        FillLine(sit.HomeLine, sit.HomeScore, m.Inning);

        // Runners: take men from the batting club's order who are not the hitter.
        System.Array.Clear(sit.Runners, 0, sit.Runners.Length);
        var pool = batting.BattingOrder.Where(p => p != sit.Batter).ToList();
        int at = 0;

        if (m.OnFirst && at < pool.Count) sit.Runners[1] = pool[at++];
        if (m.OnSecond && at < pool.Count) sit.Runners[2] = pool[at++];
        if (m.OnThird && at < pool.Count) sit.Runners[3] = pool[at++];

        _ = fielding;
        _ = playerIsHome;
    }

    private static void FillLine(List<int> line, int runs, int throughInning)
    {
        line.Clear();
        int left = runs;
        for (int i = 0; i < throughInning; i++)
        {
            // Spread them plausibly rather than dumping the lot in the first.
            int here = left > 0 && (i % 3 == 1 || i == throughInning - 1) ? Mathf.Min(left, 2) : 0;
            line.Add(here);
            left -= here;
        }
        if (left > 0 && line.Count > 0) line[^1] += left;
    }

    // -----------------------------------------------------------------------
    // Judging one
    // -----------------------------------------------------------------------

    public enum Verdict { Running, Won, Lost }

    /// <summary>
    /// How the attempt is going. Called after every play.
    /// </summary>
    /// <param name="startingRuns">The player's score when the moment began.</param>
    /// <param name="startingTheirs">The opposition's score when it began.</param>
    /// <param name="outsMade">Outs recorded since it began.</param>
    /// <param name="gotHit">Whether the player has managed a hit.</param>
    public static Verdict Judge(Moment m, GameSituation sit, int startingRuns, int startingTheirs,
        int outsMade, bool gotHit)
    {
        int mine = MyScore(m, sit);
        int theirs = TheirScore(m, sit);

        switch (m.Goal)
        {
            case MomentGoal.DriveARunIn:
                if (mine > startingRuns) return Verdict.Won;
                break;

            case MomentGoal.TieOrLead:
                if (mine >= theirs) return Verdict.Won;
                break;

            case MomentGoal.WinIt:
                if (mine > theirs) return Verdict.Won;
                break;

            case MomentGoal.GetAHit:
                if (gotHit) return Verdict.Won;
                break;

            case MomentGoal.HoldTheLead:
                // Conceding a run fails it immediately; otherwise the outs decide.
                if (theirs > startingTheirs) return Verdict.Lost;
                if (outsMade >= m.OutsAllowed) return Verdict.Won;
                return Verdict.Running;
        }

        // Batting moments end when the outs run out, or the half inning does.
        if (outsMade >= m.OutsAllowed || sit.Outs >= 3) return Verdict.Lost;
        return Verdict.Running;
    }

    public static int MyScore(Moment m, GameSituation sit) =>
        m.Batting == m.TopHalf ? sit.AwayScore : sit.HomeScore;

    public static int TheirScore(Moment m, GameSituation sit) =>
        m.Batting == m.TopHalf ? sit.HomeScore : sit.AwayScore;
}
