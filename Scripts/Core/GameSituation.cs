using System;
using System.Collections.Generic;
using SandlotSlugfest.Data;
using SandlotSlugfest.Stats;

namespace SandlotSlugfest.Core;

/// <summary>Which side a human is playing, and whether they handle the defensive half too.</summary>
public enum ControlMode
{
    /// <summary>Run the visitors: bat in the top, pitch and field in the bottom.</summary>
    PlayerVsCpu,
    /// <summary>Run the home club: pitch and field in the top, bat in the bottom.</summary>
    CpuVsPlayer,
    /// <summary>Bat for the visitors only; the computer handles the defensive half.</summary>
    BatOnlyAway,
    /// <summary>Bat for the home club only; the computer handles the defensive half.</summary>
    BatOnlyHome,
    PlayerVsPlayer,
    CpuVsCpu,
}

/// <summary>
/// The rulebook: innings, outs, the count, who is on base and what the score is.
/// Pure state plus transitions — it knows nothing about rendering or input.
/// </summary>
public sealed class GameSituation
{
    public Roster Away;
    public Roster Home;

    public int ScheduledInnings = 9;
    public int Inning = 1;
    public bool TopHalf = true;
    public int Outs;
    public int Balls;
    public int Strikes;

    public int AwayScore;
    public int HomeScore;
    public int AwayHits;
    public int HomeHits;
    public int AwayErrors;
    public int HomeErrors;

    public readonly List<int> AwayLine = new();
    public readonly List<int> HomeLine = new();

    /// <summary>Runners by base. Index 1 = first, 2 = second, 3 = third. Index 0 is unused.</summary>
    public readonly PlayerData[] Runners = new PlayerData[4];

    public bool IsOver { get; private set; }
    public string FinalNote { get; private set; } = "";

    public PlayerData Batter { get; private set; }

    /// <summary>This game's box score. The season book absorbs it when the game goes final.</summary>
    public readonly StatBook Stats = new();

    /// <summary>
    /// Which month of the season this is, April being 0. Set by whoever starts the game; a
    /// friendly or a moment leaves it at zero, which is harmless — it only sorts the splits.
    /// </summary>
    public int Month;

    /// <summary>
    /// The slices this moment's numbers belong in. Read it before moving anybody: a hit with a
    /// man on second is a hit with a runner in scoring position, and once the runner has been
    /// waved home the state no longer says so.
    /// </summary>
    public SplitContext Where => new(!TopHalf, Month, RunnerOn(2) || RunnerOn(3));

    /// <summary>
    /// Where the defence is standing. Set by whoever is managing that side before the pitch; the
    /// play simulation reads it when it puts the fielders out.
    /// </summary>
    public Alignment Defence = Alignment.Straight;

    public PlayerData CurrentPitcher => FieldingTeam.CurrentPitcher;

    public Roster BattingTeam => TopHalf ? Away : Home;
    public Roster FieldingTeam => TopHalf ? Home : Away;

    /// <summary>Whether this club is the host, and so wears its own colours rather than greys.</summary>
    public bool IsHome(Roster roster) => roster == Home;

    /// <summary>The kit a club is actually wearing tonight.</summary>
    public Data.TeamData KitOf(Roster roster) =>
        Data.Uniform.Kit(roster.Team, IsHome(roster));
    public int BattingScore => TopHalf ? AwayScore : HomeScore;
    public int FieldingScore => TopHalf ? HomeScore : AwayScore;

    public bool RunnerOn(int baseIndex) => baseIndex >= 1 && baseIndex <= 3 && Runners[baseIndex] != null;
    public bool BasesLoaded => RunnerOn(1) && RunnerOn(2) && RunnerOn(3);
    public int RunnerCount => (RunnerOn(1) ? 1 : 0) + (RunnerOn(2) ? 1 : 0) + (RunnerOn(3) ? 1 : 0);

    public event Action<string> Announced;
    public event Action<int, bool> RunsScored;      // runs, byAwayTeam
    public event Action HalfInningChanged;
    public event Action GameEnded;

    public void Start(Roster away, Roster home, int innings)
    {
        Away = away;
        Home = home;
        ScheduledInnings = innings;
        Inning = 1;
        TopHalf = true;
        Outs = Balls = Strikes = 0;
        AwayScore = HomeScore = 0;
        AwayHits = HomeHits = AwayErrors = HomeErrors = 0;
        AwayLine.Clear();
        HomeLine.Clear();
        AwayLine.Add(0);
        Array.Clear(Runners, 0, Runners.Length);
        IsOver = false;

        // Both starters go on the relief ledger so the men who follow them have something to be
        // measured against.
        Stats.RecordEntry(away.CurrentPitcher, away.Team.Id, 0, 0, starting: true);
        Stats.RecordEntry(home.CurrentPitcher, home.Team.Id, 0, 0, starting: true);

        NextBatter();
    }

    public void NextBatter()
    {
        Balls = Strikes = 0;
        Batter = BattingTeam.NextHitter();
    }

    /// <summary>
    /// Sends a bat up for the man at the plate. He takes over the at-bat and the lineup spot, so
    /// the order carries on from where it was.
    /// </summary>
    public bool PinchHit(PlayerData replacement)
    {
        if (replacement == null || Batter == null || replacement == Batter) return false;
        if (!BattingTeam.Substitute(Batter, replacement)) return false;

        Batter = replacement;
        return true;
    }

    /// <summary>
    /// Brings a new arm in for the side in the field, and notes the lead he is inheriting.
    ///
    /// Going through here rather than straight to the roster is what makes holds and blown saves
    /// possible: a final line cannot tell you whether a man walked into a one-run game or a rout,
    /// and that difference is most of how a bullpen is judged.
    /// </summary>
    public void ChangePitcher(PlayerData arm)
    {
        if (arm == null) return;
        var side = FieldingTeam;
        side.SetPitcher(arm);
        Stats.RecordEntry(arm, side.Team.Id, FieldingScore, BattingScore, starting: false);
    }

    public void Announce(string message) => Announced?.Invoke(message);

    // -----------------------------------------------------------------------
    // The count
    // -----------------------------------------------------------------------

    /// <summary>Adds a strike. Returns true if the batter struck out.</summary>
    public bool AddStrike(bool foul)
    {
        // A foul ball cannot be strike three.
        if (foul && Strikes >= 2) return false;
        Strikes++;
        if (Strikes >= 3)
        {
            Announce($"{Batter.ShortName} strikes out!");
            Stats.RecordPlateAppearance(Batter, CurrentPitcher, PaResult.Strikeout, 0, 1, true, Where);
            // RecordOut may end the half inning, which already queues the next hitter.
            bool endedHalf = RecordOut();
            if (!IsOver && !endedHalf) NextBatter();
            return true;
        }
        return false;
    }

    /// <summary>Adds a ball. Returns true if the batter walked.</summary>
    public bool AddBall()
    {
        Balls++;
        if (Balls >= 4)
        {
            Announce($"Ball four — {Batter.ShortName} takes a walk.");
            AwardWalk();
            return true;
        }
        return false;
    }

    // -----------------------------------------------------------------------
    // Advancing runners
    // -----------------------------------------------------------------------

    /// <summary>Walk: the batter takes first and forces runners along.</summary>
    public void AwardWalk() => AwardFirstBase(PaResult.Walk);

    /// <summary>
    /// A free pass given on purpose. It moves runners exactly as a walk does, but it is a
    /// different thing on both men's records — an intentional walk says nothing about the
    /// pitcher's control, and a hitter is not credited with an eye for being avoided.
    /// </summary>
    public void AwardIntentionalWalk()
    {
        Announce($"{Batter.ShortName} is put on intentionally.");
        AwardFirstBase(PaResult.IntentionalWalk);
    }

    /// <summary>A pitch that got away and hit him. Same bases, and it is on the pitcher.</summary>
    public void AwardHitByPitch()
    {
        Announce($"{Batter.ShortName} is hit by the pitch.");
        AwardFirstBase(PaResult.HitByPitch);
    }

    /// <summary>The batter takes first however he earned it, and the forced runners move up.</summary>
    private void AwardFirstBase(PaResult how)
    {
        var batter = Batter;
        var pitcher = CurrentPitcher;
        var where = Where;

        int runs = 0;
        if (RunnerOn(1))
        {
            if (RunnerOn(2))
            {
                if (RunnerOn(3))
                {
                    // Bases loaded: the runner on third is forced home.
                    Stats.RecordRun(Runners[3], where);
                    runs++;
                }
                Runners[3] = Runners[2];
            }
            Runners[2] = Runners[1];
        }
        Runners[1] = batter;

        Stats.RecordPlateAppearance(batter, pitcher, how, runs, 0, true, where);
        if (runs > 0) AddRuns(runs);
        if (!IsOver) NextBatter();
    }

    /// <summary>A stolen base: the runner takes the next bag and it goes in his line.</summary>
    public void CompleteSteal(int fromBase)
    {
        if (!RunnerOn(fromBase)) return;
        var runner = Runners[fromBase];
        var where = Where;
        Runners[fromBase + 1] = runner;
        Runners[fromBase] = null;
        Stats.RecordSteal(runner, safe: true, where);
        Announce($"{runner.ShortName} steals {(fromBase == 1 ? "second" : "third")}!");
    }

    /// <summary>
    /// Thrown out trying. The runner is off the bases and it is an out — and now it goes on his
    /// record, which it never did. A thief with fifty steals and no times caught looked free.
    /// </summary>
    public bool CaughtStealing(int fromBase)
    {
        if (!RunnerOn(fromBase)) return false;
        var runner = Runners[fromBase];
        var where = Where;
        Runners[fromBase] = null;
        Stats.RecordSteal(runner, safe: false, where);
        Announce($"{runner.ShortName} is caught stealing.");
        return RecordOut();
    }

    /// <summary>
    /// A balk: every runner is awarded one base and the batter stays at the plate. Used by the
    /// disengagement limit — a third step-off that does not retire the runner is a balk.
    /// </summary>
    public int AwardBalk() => AdvanceAllRunners();

    /// <summary>
    /// Everybody up one bag, the batter staying where he is. A balk and a ball that gets past the
    /// catcher move the same men the same distance.
    /// </summary>
    public int AdvanceAllRunners()
    {
        var where = Where;
        int runs = 0;
        if (RunnerOn(3)) { Stats.RecordRun(Runners[3], where); runs++; Runners[3] = null; }
        if (RunnerOn(2)) { Runners[3] = Runners[2]; Runners[2] = null; }
        if (RunnerOn(1)) { Runners[2] = Runners[1]; Runners[1] = null; }
        if (runs > 0) AddRuns(runs);
        return runs;
    }

    /// <summary>
    /// A pitch the catcher could not keep in front of him. The count stands; the runners do not.
    /// </summary>
    public int WildPitch()
    {
        if (RunnerCount == 0) return 0;

        var arm = CurrentPitcher;
        int runs = AdvanceAllRunners();
        Stats.RecordWildPitch(arm);
        Announce($"Wild pitch — the runners move up.");
        return runs;
    }

    /// <summary>Takes a runner off the bases, when he is picked off or thrown out between pitches.</summary>
    public bool RetireRunner(int baseIndex)
    {
        if (!RunnerOn(baseIndex)) return false;
        Runners[baseIndex] = null;
        return RecordOut();
    }

    /// <summary>Advances every runner by <paramref name="bases"/> and puts the batter on. Returns runs scored.</summary>
    public int AwardHit(int bases)
    {
        var batter = Batter;
        var pitcher = CurrentPitcher;
        var where = Where;

        int runs = 0;
        for (int b = 3; b >= 1; b--)
        {
            if (!RunnerOn(b)) continue;
            int dest = b + bases;
            var runner = Runners[b];
            Runners[b] = null;
            if (dest >= 4) { Stats.RecordRun(runner, where); runs++; }
            else Runners[dest] = runner;
        }

        if (bases >= 4)
        {
            Stats.RecordRun(batter, where);     // the batter himself
            runs++;
        }
        else Runners[bases] = batter;

        Stats.RecordPlateAppearance(batter, pitcher, ResultForBases(bases), runs, 0, true, where);

        if (TopHalf) AwayHits++; else HomeHits++;
        if (runs > 0) AddRuns(runs);
        if (!IsOver) NextBatter();
        return runs;
    }

    public static PaResult ResultForBases(int bases) => bases switch
    {
        1 => PaResult.Single,
        2 => PaResult.Double,
        3 => PaResult.Triple,
        _ => PaResult.HomeRun,
    };

    /// <summary>
    /// Books a ball in play that the field simulation already resolved. Runner placement is
    /// done by the caller; this records the stat line, the outs and the runs.
    /// </summary>
    public void CompleteBattedBall(
        PlayerData batter, PlayerData pitcher, IReadOnlyList<PlayerData> scorers,
        bool isHit, int basesForBatter, int outs, bool errorOnPlay,
        SplitContext where = default, PlayCredit credit = PlayCredit.None)
    {
        foreach (var runner in scorers) Stats.RecordRun(runner, where);

        PaResult result = isHit
            ? ResultForBases(basesForBatter)
            : (errorOnPlay && basesForBatter > 0 ? PaResult.ReachedOnError : PaResult.OutInPlay);

        // Only outs that actually fit in the half inning count toward innings pitched — a double
        // play with two already gone records one out, not two.
        int outsCredited = Math.Min(outs, 3 - Outs);

        // Runs that only scored because of a misplay do not go on the pitcher's ledger.
        Stats.RecordPlateAppearance(batter, pitcher, result, scorers.Count, outsCredited,
            !errorOnPlay, where, credit);

        if (isHit) { if (TopHalf) AwayHits++; else HomeHits++; }

        bool halfEnded = outs > 0 && RecordOut(outs);
        if (!halfEnded && scorers.Count > 0) AddRuns(scorers.Count);
        if (!halfEnded && !IsOver) NextBatter();
    }

    /// <summary>Moves a single runner from one base to another. Returns 1 if he scored.</summary>
    public int MoveRunner(int fromBase, int toBase)
    {
        if (fromBase < 1 || fromBase > 3 || Runners[fromBase] == null) return 0;
        var runner = Runners[fromBase];
        Runners[fromBase] = null;
        if (toBase >= 4)
        {
            AddRuns(1);
            return 1;
        }
        Runners[toBase] = runner;
        return 0;
    }

    public void PlaceBatterOn(int baseIndex)
    {
        if (baseIndex >= 1 && baseIndex <= 3) Runners[baseIndex] = Batter;
    }

    public void RemoveRunner(int baseIndex)
    {
        if (baseIndex >= 1 && baseIndex <= 3) Runners[baseIndex] = null;
    }

    public void AddRuns(int runs)
    {
        if (runs <= 0) return;
        if (TopHalf)
        {
            AwayScore += runs;
            AwayLine[^1] += runs;
        }
        else
        {
            HomeScore += runs;
            HomeLine[^1] += runs;
        }
        RunsScored?.Invoke(runs, TopHalf);
        CheckWalkOff();
    }

    // -----------------------------------------------------------------------
    // Outs and half innings
    // -----------------------------------------------------------------------

    /// <summary>Records an out. Returns true if that ended the half inning.</summary>
    /// <summary>
    /// Every out ever recorded in this game, never reset. The out audit uses it to count a half
    /// inning's outs from the source rather than by tallying each kind by hand — that tally
    /// silently missed a caught stealing the moment baserunning was added.
    /// </summary>
    public int OutsRecorded { get; private set; }

    public bool RecordOut(int count = 1)
    {
        OutsRecorded += count;
        Outs += count;
        if (Outs < 3) return false;
        Outs = 3;
        EndHalfInning();
        return true;
    }

    public void EndHalfInning()
    {
        if (IsOver) return;

        // The home team does not bat if it is already ahead after the scheduled innings.
        if (TopHalf && Inning >= ScheduledInnings && HomeScore > AwayScore)
        {
            Finish($"{Home.Team.FullName} win it {HomeScore}–{AwayScore}.");
            return;
        }

        if (!TopHalf && Inning >= ScheduledInnings && HomeScore != AwayScore)
        {
            var winner = HomeScore > AwayScore ? Home : Away;
            int hi = Math.Max(HomeScore, AwayScore);
            int lo = Math.Min(HomeScore, AwayScore);
            Finish($"{winner.Team.FullName} win it {hi}–{lo}.");
            return;
        }

        Outs = 0;
        Balls = Strikes = 0;
        Array.Clear(Runners, 0, Runners.Length);

        if (TopHalf)
        {
            TopHalf = false;
            HomeLine.Add(0);
        }
        else
        {
            TopHalf = true;
            Inning++;
            AwayLine.Add(0);
        }

        NextBatter();
        HalfInningChanged?.Invoke();
    }

    /// <summary>The home team taking the lead in the last scheduled inning or later ends it immediately.</summary>
    private void CheckWalkOff()
    {
        if (IsOver) return;
        if (!TopHalf && Inning >= ScheduledInnings && HomeScore > AwayScore)
            Finish($"Walk-off! {Home.Team.FullName} win {HomeScore}–{AwayScore}.");
    }

    /// <summary>
    /// Stops the game where it stands. Used by a moment, which is decided the instant its question
    /// is answered rather than at the end of the inning — a walk-off ends on the run crossing.
    /// </summary>
    public void EndNow(string note = "") => Finish(note);

    private void Finish(string note)
    {
        IsOver = true;
        FinalNote = note;
        Announce(note);
        GameEnded?.Invoke();
    }

    public string CountText => $"{Balls}-{Strikes}";

    public string InningText
    {
        get
        {
            string half = TopHalf ? "Top" : "Bottom";
            return $"{half} {Ordinal(Inning)}";
        }
    }

    public static string Ordinal(int n)
    {
        if (n is >= 11 and <= 13) return $"{n}th";
        return (n % 10) switch
        {
            1 => $"{n}st",
            2 => $"{n}nd",
            3 => $"{n}rd",
            _ => $"{n}th",
        };
    }
}
