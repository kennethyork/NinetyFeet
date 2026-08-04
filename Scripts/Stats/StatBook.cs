using System.Collections.Generic;
using System.Linq;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Stats;

/// <summary>How a plate appearance finished, from the hitter's point of view.</summary>
public enum PaResult { Strikeout, Walk, Single, Double, Triple, HomeRun, OutInPlay, ReachedOnError }

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

        _batting.Clear();
        _pitching.Clear();
        _records.Clear();
        _minorBatting.Clear();
        _minorPitching.Clear();
    }

    public IEnumerable<KeyValuePair<PlayerData, BattingLine>> AllBatting => _batting;
    public IEnumerable<KeyValuePair<PlayerData, PitchingLine>> AllPitching => _pitching;

    /// <summary>Records one completed plate appearance for both the hitter and the pitcher.</summary>
    public void RecordPlateAppearance(
        PlayerData batter, PlayerData pitcher, PaResult result,
        int runsBattedIn, int outsRecorded, bool runsAreEarned)
    {
        var b = Batting(batter);
        var p = Pitching(pitcher);

        b.PlateAppearances++;
        b.RunsBattedIn += runsBattedIn;

        // A walk is a plate appearance but not an at-bat.
        bool countsAsAtBat = result != PaResult.Walk;
        if (countsAsAtBat) b.AtBats++;

        switch (result)
        {
            case PaResult.Strikeout:
                b.Strikeouts++;
                p.Strikeouts++;
                break;
            case PaResult.Walk:
                b.Walks++;
                p.Walks++;
                break;
            case PaResult.Single:
                b.Hits++;
                p.Hits++;
                break;
            case PaResult.Double:
                b.Hits++; b.Doubles++;
                p.Hits++;
                break;
            case PaResult.Triple:
                b.Hits++; b.Triples++;
                p.Hits++;
                break;
            case PaResult.HomeRun:
                b.Hits++; b.HomeRuns++;
                p.Hits++; p.HomeRunsAllowed++;
                break;
        }

        p.Outs += outsRecorded;
        p.Runs += runsBattedIn;
        if (runsAreEarned) p.EarnedRuns += runsBattedIn;
    }

    /// <summary>Credits a run to whoever crossed the plate.</summary>
    public void RecordRun(PlayerData runner) => Batting(runner).Runs++;

    public void RecordPitch(PlayerData pitcher) => Pitching(pitcher).Pitches++;

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

        foreach (var p in winner.Players.Concat(loser.Players))
        {
            if (_batting.ContainsKey(p)) Batting(p).Games++;
            if (_pitching.ContainsKey(p)) Pitching(p).Games++;
        }
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
