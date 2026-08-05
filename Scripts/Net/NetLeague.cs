using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Data;
using SandlotSlugfest.Season;

namespace SandlotSlugfest.Net;

/// <summary>
/// A season two people are running at once.
///
/// The model is the one netplay already uses, stretched from an evening to a year: both machines
/// hold the identical league, built from a shared seed, and exchange only the things a person did
/// that the other machine could not work out for itself. Almost nothing qualifies. Every game
/// neither owner is playing is simulated by both sides from the fixture's own seed and lands on
/// the same score to the last run, which is why a whole shared season costs a few kilobytes a day
/// rather than a stream of standings.
///
/// The exception is the ballgames the owners play, and those travel as <see cref="GameResult"/>
/// packets.
///
/// The calendar is the thing that needs discipline. A shared league cannot let one owner run ahead
/// — the day he simulates is a day the other has not finished with, and there is no way back — so
/// the day is a barrier. Each side says when it has finished with today; only when both have said
/// it does either turn the page. Then both fingerprint the whole league and compare, so a
/// disagreement is caught on the day it happens rather than in August.
///
/// One deliberate limitation, stated plainly because it is the sort of thing that is easier to
/// discover than to be told: the two owners' games are the only ones anybody plays. When they meet
/// each other they play head to head over the existing netplay, and otherwise each plays his own
/// club while the other's is simulated. Neither can sit in on the other's game, because a shared
/// game is a shared simulation and there is only one of those.
/// </summary>
public sealed class NetLeague
{
    public static NetLeague I { get; } = new();

    /// <summary>True while a shared season is running.</summary>
    public bool Active { get; private set; }

    /// <summary>The club this machine's owner runs, and the club the other one runs.</summary>
    public int LocalClubId { get; private set; } = -1;
    public int RemoteClubId { get; private set; } = -1;

    /// <summary>The day each side has finished with. They advance when the two agree.</summary>
    public int LocalDoneDay { get; private set; } = -1;
    public int RemoteDoneDay { get; private set; } = -1;

    /// <summary>What the screen should say about the other owner.</summary>
    public string Waiting { get; private set; } = "";

    /// <summary>Set when a packet could not be applied. The league is not to be trusted after this.</summary>
    public string Broken { get; private set; }

    private SeasonState _season;

    /// <summary>Packets that arrived for a day this machine has not reached yet.</summary>
    private readonly List<int[]> _pending = new();

    /// <summary>Raised when the calendar has turned, so the screen redraws.</summary>
    public event System.Action Advanced;

    // -----------------------------------------------------------------------
    // Opening and closing
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds the shared league. Both machines call this with the same terms and get the same
    /// league; the only thing that differs is which club each of them is holding.
    /// </summary>
    public SeasonState Begin(int seed, int localClubId, int remoteClubId,
        int gamesPerTeam, int innings)
    {
        var season = new SeasonState();
        season.StartNew(seed, localClubId, gamesPerTeam, innings);

        _season = season;
        LocalClubId = localClubId;
        RemoteClubId = remoteClubId;
        LocalDoneDay = RemoteDoneDay = -1;
        _pending.Clear();
        Broken = null;
        Waiting = "";
        Active = true;

        // Day zero, before anybody has done anything, is the cheapest possible chance to find out
        // that the two machines did not in fact build the same league.
        NetLink.I?.ReportLeagueDay(-1, LeagueFingerprint.Of(season));
        return season;
    }

    public void End()
    {
        Active = false;
        _season = null;
        _pending.Clear();
        LocalClubId = RemoteClubId = -1;
        LocalDoneDay = RemoteDoneDay = -1;
        Waiting = "";
    }

    /// <summary>Whether a fixture is the two owners playing each other.</summary>
    public bool IsDerby(ScheduledGame g) =>
        Active && g != null && g.Involves(LocalClubId) && g.Involves(RemoteClubId);

    /// <summary>This owner's fixtures on the day the league is sitting on.</summary>
    public IEnumerable<ScheduledGame> MyGamesToday() =>
        !Active || _season == null
            ? Enumerable.Empty<ScheduledGame>()
            : _season.Games.Where(g => g.Day == _season.CurrentDay && !g.Played
                                       && g.Involves(LocalClubId));

    // -----------------------------------------------------------------------
    // Finishing with a day
    // -----------------------------------------------------------------------

    /// <summary>
    /// Books a game this owner has just finished and posts it to the other machine.
    ///
    /// The derby is the one fixture that is not posted: both owners were in it, both simulations
    /// ran it, and both already hold the identical result. Sending it would apply it twice.
    /// </summary>
    public void Finished(ScheduledGame game, Core.GameSituation sit)
    {
        if (!Active || _season == null || game == null || game.Played) return;

        if (IsDerby(game))
        {
            // Both owners are in this one, so both machines settle it and neither posts it. Which
            // means it has to be settled the same way on both, and whatever was handed in here is
            // thrown away: if one of them played it by hand, his result is his alone and recording
            // it would split the league on the spot. The fixture's own seed is the only answer
            // both sides can arrive at.
            //
            // This is the guard, rather than the screen that decides whether to offer a PLAY
            // button, because a screen that forgets loses somebody's season.
            _season.RecordPlayedGame(game, _season.Resolve(game));
            return;
        }

        int[] packet = GameResult.Pack(_season, game, sit);
        NetLink.I?.PostGameResult(packet);
        _season.RecordPlayedGame(game, sit);
    }

    /// <summary>
    /// Says this owner has finished with today. The calendar does not move until the other one
    /// says the same, and it moves on both machines at once when he does.
    /// </summary>
    public void DoneWithToday()
    {
        if (!Active || _season == null || Broken != null) return;
        if (MyGamesToday().Any()) return;              // he still has a ballgame to settle

        LocalDoneDay = _season.CurrentDay;
        NetLink.I?.PostDayDone(LocalDoneDay);
        TryAdvance();
    }

    /// <summary>Called by the link when the other owner has finished with a day.</summary>
    public void RemoteFinished(int day)
    {
        RemoteDoneDay = day;
        TryAdvance();
    }

    /// <summary>Called by the link when a game the other owner played arrives.</summary>
    public void ReceiveGame(int[] packet)
    {
        if (!Active || _season == null) return;

        // A packet for a day this machine has not reached yet is held rather than applied.
        //
        // The ordering makes this all but impossible — a day-done message cannot overtake the
        // ballgame it refers to, so by the time the other owner is playing Tuesday this machine
        // has already turned the page. But "all but impossible" is not a thing to apply a future
        // game on, and the day is right there in the packet. Applying it early would put a result
        // in the book before the day that produced it.
        int day = GameResult.DayOf(packet);
        if (day > _season.CurrentDay) { _pending.Add(packet); return; }

        string trouble = GameResult.Apply(_season, packet);
        if (trouble == null) { Advanced?.Invoke(); return; }

        Broken = trouble;
        GD.PushError($"Shared league: {trouble}");
    }

    private void TryAdvance()
    {
        if (!Active || _season == null || Broken != null) return;

        int day = _season.CurrentDay;
        if (LocalDoneDay != day || RemoteDoneDay != day)
        {
            Waiting = LocalDoneDay == day
                ? $"Waiting for {Teams.Get(RemoteClubId).Nickname} to finish {Calendar.FormatShort(_season.Today)}."
                : "";
            return;
        }

        // Both sides have finished with today, so the page turns. Everything either owner played
        // is already in the books — his own directly, the other's from a packet — and this plays
        // out the rest of the league, which both machines derive identically.
        _season.AdvanceDay(simulateUserGame: true);
        _season.BeginPlayoffsIfReady();

        // Anything that arrived early was waiting on this.
        RetryPending();

        Waiting = "";
        NetLink.I?.ReportLeagueDay(day, LeagueFingerprint.Of(_season));
        Advanced?.Invoke();
    }

    /// <summary>
    /// Applies anything that arrived early, now that the calendar has caught up with it.
    ///
    /// A packet that is still in the future stays where it is; one whose day has now been passed
    /// is a fault rather than a wait, and is reported as one. A held packet that quietly never
    /// applies is a game missing from one owner's league and present in the other's, which is the
    /// exact failure this whole arrangement exists to prevent.
    /// </summary>
    private void RetryPending()
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            if (GameResult.DayOf(_pending[i]) > _season.CurrentDay) continue;

            string trouble = GameResult.Apply(_season, _pending[i]);
            _pending.RemoveAt(i);
            if (trouble == null) continue;

            Broken = trouble;
            GD.PushError($"Shared league: {trouble}");
            return;
        }
    }

    /// <summary>A line for the season screen: whose league this is and what it is waiting on.</summary>
    public string Status()
    {
        if (!Active) return "";
        if (Broken != null) return $"This league has come apart: {Broken}";
        if (NetLink.I is { LeagueDrifted: true })
            return $"The two leagues drifted apart on day {NetLink.I.DriftedOnDay}. " +
                   "Nothing after that day can be trusted.";
        if (!string.IsNullOrEmpty(Waiting)) return Waiting;

        return $"Shared league with {Teams.Get(RemoteClubId).FullName}" +
               (NetLink.I is { LastAgreedDay: >= 0 } l
                   ? $"  ·  agreed through day {l.LastAgreedDay}" : "");
    }
}
