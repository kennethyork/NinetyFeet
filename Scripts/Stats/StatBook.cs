using System;
using System.Collections.Generic;
using System.Linq;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Stats;

/// <summary>How a plate appearance finished, from the hitter's point of view.</summary>
public enum PaResult
{
    Strikeout, Walk, Single, Double, Triple, HomeRun, OutInPlay, ReachedOnError,
    // Appended so saved and networked values keep their meaning.
    HitByPitch, IntentionalWalk,
}

/// <summary>
/// What else the play was, beyond how it finished. A ground ball that retires two men and a fly
/// ball that scores one both come through as an out; the difference is the whole of a hitter's
/// reputation, so it has to be carried rather than inferred later.
/// </summary>
[Flags]
public enum PlayCredit
{
    None = 0,
    SacrificeFly = 1,
    SacrificeBunt = 2,
    DoublePlay = 4,
}

/// <summary>
/// The league's record book. Keyed by player identity, so a traded player carries his numbers
/// with him. One book covers a season; a game also keeps a fresh book for its own box score.
/// </summary>
public sealed class StatBook
{
    private readonly Dictionary<PlayerData, BattingLine> _batting = new();
    private readonly Dictionary<PlayerData, PitchingLine> _pitching = new();
    private readonly Dictionary<int, TeamRecord> _records = new();

    // Career totals, accumulated every time a season is closed out. Rolling the year used to
    // simply clear the book, so a franchise had no history at all — a ten-year veteran's numbers
    // went in the bin every winter.
    private readonly Dictionary<PlayerData, BattingLine> _careerBatting = new();
    private readonly Dictionary<PlayerData, PitchingLine> _careerPitching = new();
    private readonly Dictionary<PlayerData, int> _seasonsPlayed = new();

    /// <summary>This season's line cut by hand, by ground, by month and by the men on base.</summary>
    public readonly SplitBook Splits = new();

    /// <summary>The same slices over a whole career.</summary>
    public readonly SplitBook CareerSplits = new();

    public BattingLine Batting(PlayerData p)
    {
        if (!_batting.TryGetValue(p, out var line)) _batting[p] = line = new BattingLine();
        return line;
    }

    public PitchingLine Pitching(PlayerData p)
    {
        if (!_pitching.TryGetValue(p, out var line)) _pitching[p] = line = new PitchingLine();
        return line;
    }

    public TeamRecord Record(int teamId)
    {
        if (!_records.TryGetValue(teamId, out var rec)) _records[teamId] = rec = new TeamRecord();
        return rec;
    }

    /// <summary>A player's career totals to date, not counting the season in progress.</summary>
    public BattingLine CareerBatting(PlayerData p)
    {
        if (!_careerBatting.TryGetValue(p, out var line)) _careerBatting[p] = line = new BattingLine();
        return line;
    }

    public PitchingLine CareerPitching(PlayerData p)
    {
        if (!_careerPitching.TryGetValue(p, out var line)) _careerPitching[p] = line = new PitchingLine();
        return line;
    }

    /// <summary>How many seasons this player has appeared in.</summary>
    public int SeasonsPlayed(PlayerData p) => _seasonsPlayed.GetValueOrDefault(p);

    /// <summary>Restores a career length from a save.</summary>
    public void SetSeasonsPlayed(PlayerData p, int seasons)
    {
        if (seasons > 0) _seasonsPlayed[p] = seasons;
    }

    // Triple-A numbers, kept apart from the big-league book on purpose. A .318 in the minors is
    // not a .318 in the majors, and a prospect's card should never let the two be confused.
    private readonly Dictionary<PlayerData, BattingLine> _minorBatting = new();
    private readonly Dictionary<PlayerData, PitchingLine> _minorPitching = new();

    public BattingLine MinorBatting(PlayerData p)
    {
        if (!_minorBatting.TryGetValue(p, out var line)) _minorBatting[p] = line = new BattingLine();
        return line;
    }

    public PitchingLine MinorPitching(PlayerData p)
    {
        if (!_minorPitching.TryGetValue(p, out var line)) _minorPitching[p] = line = new PitchingLine();
        return line;
    }

    public bool HasMinorLine(PlayerData p) =>
        _minorBatting.ContainsKey(p) || _minorPitching.ContainsKey(p);

    /// <summary>
    /// Closes the season out: this year's numbers are folded into every player's career totals,
    /// then the year is cleared.
    /// </summary>
    public void CloseSeason()
    {
        foreach (var (p, line) in _batting)
        {
            if (line.PlateAppearances == 0 && line.Games == 0) continue;
            CareerBatting(p).Absorb(line);
            _seasonsPlayed[p] = _seasonsPlayed.GetValueOrDefault(p) + 1;
        }

        foreach (var (p, line) in _pitching)
        {
            if (line.Outs == 0 && line.Games == 0) continue;
            CareerPitching(p).Absorb(line);
        }

        CareerSplits.Absorb(Splits);

        _batting.Clear();
        _pitching.Clear();
        _records.Clear();
        _minorBatting.Clear();
        _minorPitching.Clear();
        Splits.Clear();
    }

    public IEnumerable<KeyValuePair<PlayerData, BattingLine>> AllBatting => _batting;
    public IEnumerable<KeyValuePair<PlayerData, PitchingLine>> AllPitching => _pitching;

    // -----------------------------------------------------------------------
    // Recording a plate appearance
    // -----------------------------------------------------------------------

    /// <summary>Records one completed plate appearance for both the hitter and the pitcher.</summary>
    public void RecordPlateAppearance(
        PlayerData batter, PlayerData pitcher, PaResult result,
        int runsBattedIn, int outsRecorded, bool runsAreEarned,
        SplitContext where = default, PlayCredit credit = PlayCredit.None)
    {
        // The delta is built once and then applied to the season line and to every slice it
        // belongs in. Doing it any other way means the splits drift from the totals the first
        // time somebody adds a stat and forgets one of the four places it had to go.
        var b = new BattingLine { PlateAppearances = 1, RunsBattedIn = runsBattedIn };
        var p = new PitchingLine { BattersFaced = 1, Outs = outsRecorded, Runs = runsBattedIn };

        if (runsAreEarned) p.EarnedRuns = runsBattedIn;

        bool sacrifice = credit.HasFlag(PlayCredit.SacrificeFly)
                      || credit.HasFlag(PlayCredit.SacrificeBunt);

        // A walk, a hit batsman and a sacrifice are all plate appearances but none is an at-bat.
        bool countsAsAtBat = result is not (PaResult.Walk or PaResult.IntentionalWalk
                                         or PaResult.HitByPitch) && !sacrifice;
        if (countsAsAtBat) b.AtBats = 1;

        if (credit.HasFlag(PlayCredit.SacrificeFly)) b.SacrificeFlies = 1;
        if (credit.HasFlag(PlayCredit.SacrificeBunt)) b.SacrificeBunts = 1;
        if (credit.HasFlag(PlayCredit.DoublePlay)) b.GroundedIntoDoublePlay = 1;

        switch (result)
        {
            case PaResult.Strikeout:
                b.Strikeouts = 1;
                p.Strikeouts = 1;
                break;
            case PaResult.Walk:
                b.Walks = 1;
                p.Walks = 1;
                break;
            case PaResult.IntentionalWalk:
                b.Walks = 1; b.IntentionalWalks = 1;
                p.Walks = 1; p.IntentionalWalksIssued = 1;
                break;
            case PaResult.HitByPitch:
                b.HitByPitch = 1;
                p.HitBatters = 1;
                break;
            case PaResult.Single:
                b.Hits = 1;
                p.Hits = 1;
                break;
            case PaResult.Double:
                b.Hits = 1; b.Doubles = 1;
                p.Hits = 1;
                break;
            case PaResult.Triple:
                b.Hits = 1; b.Triples = 1;
                p.Hits = 1;
                break;
            case PaResult.HomeRun:
                b.Hits = 1; b.HomeRuns = 1;
                p.Hits = 1; p.HomeRunsAllowed = 1;
                break;
        }

        Batting(batter).Absorb(b);
        Pitching(pitcher).Absorb(p);
        ApplySplits(batter, pitcher, b, p, where);
    }

    /// <summary>
    /// Files the same delta under every slice it belongs to. A hitter's slices are named for the
    /// pitcher's hand and his own ground; a pitcher's are the mirror image.
    /// </summary>
    private void ApplySplits(PlayerData batter, PlayerData pitcher,
        BattingLine b, PitchingLine p, SplitContext where)
    {
        var hitterSlices = Splits.Batting(batter);
        hitterSlices.Of(HandSlice(pitcher?.Throws)).Absorb(b);
        hitterSlices.Of(where.BatterAtHome ? Split.AtHome : Split.OnRoad).Absorb(b);
        hitterSlices.Of(SplitContext.MonthSlot(where.Month)).Absorb(b);
        if (where.RunnerInScoringPosition) hitterSlices.Of(Split.ScoringPosition).Absorb(b);

        if (pitcher == null) return;

        var armSlices = Splits.Pitching(pitcher);
        armSlices.Of(HandSlice(SideBattedFrom(batter, pitcher))).Absorb(p);
        armSlices.Of(where.BatterAtHome ? Split.OnRoad : Split.AtHome).Absorb(p);
        armSlices.Of(SplitContext.MonthSlot(where.Month)).Absorb(p);
        if (where.RunnerInScoringPosition) armSlices.Of(Split.ScoringPosition).Absorb(p);
    }

    private static Split HandSlice(Handedness? hand) =>
        hand == Handedness.Left ? Split.VsLeft : Split.VsRight;

    /// <summary>
    /// Which side the hitter actually stood on. A switch hitter turns around to face whoever is
    /// on the mound, so filing him under his listed hand would put every one of his at-bats in a
    /// box he never batted from — and it is the pitcher's split that would carry the error.
    /// </summary>
    private static Handedness SideBattedFrom(PlayerData batter, PlayerData pitcher)
    {
        if (batter == null) return Handedness.Right;
        if (batter.Bats != Handedness.Switch) return batter.Bats;
        return pitcher?.Throws == Handedness.Left ? Handedness.Right : Handedness.Left;
    }

    /// <summary>Credits a run to whoever crossed the plate, in the season line and the slices.</summary>
    public void RecordRun(PlayerData runner, SplitContext where = default)
    {
        Batting(runner).Runs++;
        var slices = Splits.Batting(runner);
        slices.Of(where.BatterAtHome ? Split.AtHome : Split.OnRoad).Runs++;
        slices.Of(SplitContext.MonthSlot(where.Month)).Runs++;
    }

    /// <summary>A stolen base, and the times he was thrown out trying — which nothing kept before.</summary>
    public void RecordSteal(PlayerData runner, bool safe, SplitContext where = default)
    {
        var line = Batting(runner);
        if (safe) line.StolenBases++; else line.CaughtStealing++;

        var slices = Splits.Batting(runner);
        var ground = slices.Of(where.BatterAtHome ? Split.AtHome : Split.OnRoad);
        var month = slices.Of(SplitContext.MonthSlot(where.Month));
        if (safe) { ground.StolenBases++; month.StolenBases++; }
        else { ground.CaughtStealing++; month.CaughtStealing++; }
    }

    public void RecordWildPitch(PlayerData pitcher) => Pitching(pitcher).WildPitches++;

    public void RecordPitch(PlayerData pitcher) => Pitching(pitcher).Pitches++;

    // -----------------------------------------------------------------------
    // The relief ledger
    // -----------------------------------------------------------------------

    /// <summary>
    /// One pitcher's time in a game: what the score was when he walked in, and what it was when
    /// he walked off. Holds and blown saves cannot be worked out from a final line — two men can
    /// finish a game with identical numbers and only one of them let the lead go.
    /// </summary>
    private sealed class Stint
    {
        public PlayerData Arm;
        public int TeamId;
        public int LeadAtEntry;
        public int LeadAtExit;
        public int OutsAtEntry;
        public bool Started;
        public bool Closed;
    }

    private readonly List<Stint> _stints = new();

    /// <summary>Notes a pitcher taking the ball, with the lead he inherited.</summary>
    public void RecordEntry(PlayerData arm, int teamId, int ownScore, int oppScore, bool starting)
    {
        if (arm == null) return;

        // Whoever was out there is done; close his stint at the same score.
        var open = _stints.LastOrDefault(s => s.TeamId == teamId && !s.Closed);
        if (open != null)
        {
            open.LeadAtExit = ownScore - oppScore;
            open.Closed = true;
        }

        _stints.Add(new Stint
        {
            Arm = arm,
            TeamId = teamId,
            LeadAtEntry = ownScore - oppScore,
            OutsAtEntry = Pitching(arm).Outs,
            Started = starting,
        });
    }

    /// <summary>
    /// Settles the relief ledger once the game is final.
    ///
    /// A save situation is a lead of one to three when he takes the ball. Leave without the lead
    /// and it is a blown save; leave with it, having got somebody out, and it is a hold — unless
    /// he finished the game, in which case it is a save and already credited.
    /// </summary>
    private void SettleStints(Roster team, int ownRuns, int oppRuns)
    {
        int lead = ownRuns - oppRuns;

        foreach (var s in _stints.Where(s => s.TeamId == team.Team.Id))
        {
            if (!s.Closed) { s.LeadAtExit = lead; s.Closed = true; }
            if (s.Started) continue;

            bool saveSpot = s.LeadAtEntry >= 1 && s.LeadAtEntry <= 3;
            if (!saveSpot) continue;

            if (s.LeadAtExit <= 0) { Pitching(s.Arm).BlownSaves++; continue; }

            bool gotSomebodyOut = Pitching(s.Arm).Outs > s.OutsAtEntry;
            bool finishedIt = team.UsedArms.Count > 0 && team.UsedArms[^1] == s.Arm;
            if (gotSomebodyOut && !finishedIt) Pitching(s.Arm).Holds++;
        }
    }

    // -----------------------------------------------------------------------
    // Finishing a game
    // -----------------------------------------------------------------------

    /// <summary>
    /// Rolls a finished game's numbers into this book.
    /// </summary>
    /// <param name="countTowardRecord">
    /// False for postseason games. Player statistics still accrue, but the win and loss do not
    /// touch the standings — playoff results were being added to clubs' regular-season records,
    /// so a champion finished a 33-game season showing 61-19.
    /// </param>
    public void Absorb(StatBook game, bool countTowardRecord = true)
    {
        foreach (var (player, line) in game._batting) Batting(player).Absorb(line);
        foreach (var (player, line) in game._pitching) Pitching(player).Absorb(line);
        Splits.Absorb(game.Splits);

        if (!countTowardRecord) return;

        foreach (var (teamId, rec) in game._records)
        {
            var mine = Record(teamId);
            mine.Wins += rec.Wins;
            mine.Losses += rec.Losses;
            mine.RunsScored += rec.RunsScored;
            mine.RunsAllowed += rec.RunsAllowed;
        }
    }

    /// <summary>Marks appearances and the decision once a game is final.</summary>
    public void FinishGame(Roster winner, Roster loser, int winnerRuns, int loserRuns)
    {
        Record(winner.Team.Id).Wins++;
        Record(winner.Team.Id).RunsScored += winnerRuns;
        Record(winner.Team.Id).RunsAllowed += loserRuns;

        Record(loser.Team.Id).Losses++;
        Record(loser.Team.Id).RunsScored += loserRuns;
        Record(loser.Team.Id).RunsAllowed += winnerRuns;

        // The decision used to go to Pitchers[0] on both sides — the ace, every night, whether or
        // not he had thrown a pitch. He finished a season 33-0 while the rest of the staff had no
        // record at all.
        Decide(winner, won: true);
        Decide(loser, won: false);
        CreditSave(winner, winnerRuns - loserRuns);

        CreditStarter(winner, loserRuns);
        CreditStarter(loser, winnerRuns);

        SettleStints(winner, winnerRuns, loserRuns);
        SettleStints(loser, loserRuns, winnerRuns);

        foreach (var p in winner.Players.Concat(loser.Players))
        {
            if (_batting.ContainsKey(p)) Batting(p).Games++;
            if (_pitching.ContainsKey(p)) Pitching(p).Games++;
        }
    }

    /// <summary>
    /// The starter's own marks: a complete game if nobody relieved him, a shutout if nobody
    /// scored, and a quality start for the old six-and-three-or-better standard.
    /// </summary>
    private void CreditStarter(Roster team, int runsAllowedByTeam)
    {
        if (team.UsedArms.Count == 0) return;
        var starter = team.UsedArms[0];
        var line = Pitching(starter);

        if (line.Outs >= 18 && line.EarnedRuns <= 3) line.QualityStarts++;

        if (team.UsedArms.Count > 1) return;
        line.CompleteGames++;
        if (runsAllowedByTeam == 0) line.Shutouts++;
    }

    /// <summary>Who takes the win or the loss, and who is credited with the start.</summary>
    private void Decide(Roster team, bool won)
    {
        var used = team.UsedArms.Count > 0
            ? team.UsedArms
            : team.Pitchers.Count > 0 ? new List<PlayerData> { team.Pitchers[0] } : null;
        if (used == null) return;

        var starter = used[0];
        Pitching(starter).GamesStarted++;

        // A starter has to go five to earn a win. Short of that the credit goes to whichever
        // reliever carried the most of the game — the same shape as the real rule without the
        // official scorer's discretion.
        var decision = starter;
        if (won && Pitching(starter).Outs < 15 && used.Count > 1)
            decision = used.Skip(1).OrderByDescending(p => Pitching(p).Outs).First();

        if (won) Pitching(decision).Wins++;
        else Pitching(decision).Losses++;
    }

    /// <summary>
    /// A save for the man who finished a close one he did not start. He has to have got through
    /// a full inning: without that, anyone who came in to record the last out of a three-run game
    /// was credited, and saves turned up in 61% of games against a real 49%.
    /// </summary>
    private void CreditSave(Roster winner, int margin)
    {
        if (margin > 3 || winner.UsedArms.Count < 2) return;

        var last = winner.UsedArms[^1];
        if (last == winner.UsedArms[0]) return;
        if (Pitching(last).Wins > 0) return;          // he won it, so there is no save
        if (Pitching(last).Outs < 3) return;
        Pitching(last).Saves++;
    }

    /// <summary>Hitters with enough playing time to appear on a leaderboard.</summary>
    public IEnumerable<(PlayerData Player, BattingLine Line)> QualifiedHitters(int minAtBats = 1) =>
        _batting.Where(kv => kv.Value.AtBats >= minAtBats)
                .Select(kv => (kv.Key, kv.Value));

    public IEnumerable<(PlayerData Player, PitchingLine Line)> QualifiedPitchers(int minOuts = 1) =>
        _pitching.Where(kv => kv.Value.Outs >= minOuts)
                 .Select(kv => (kv.Key, kv.Value));
}
