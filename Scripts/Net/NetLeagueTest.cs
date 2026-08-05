using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Net;

/// <summary>
/// A whole shared season driven by nobody, over a real socket, so the wire is tested rather than
/// described.
///
///     godot --headless -- --netleague host [port] --days 40
///     godot --headless -- --netleague join 127.0.0.1 [port] --days 40
///
/// <see cref="LeagueAudit"/> already proves the model: two leagues in one process, results handed
/// between them, identical every day. What it cannot prove is any of the things that only exist
/// once there are two processes — that the terms agree before either side builds a league, that a
/// packet survives being marshalled across an RPC, that a day-done message cannot overtake the
/// ballgame it refers to, and that the daily fingerprints actually reach the other machine and get
/// compared. Those are the parts that fail in the field, and none of them can be reasoned about
/// from one side.
///
/// So both sides run a season here with nobody at the controls: each simulates its own club's
/// games, posts them, says it is done, and waits. If either machine ever reports a drift, the run
/// fails and says which day.
/// </summary>
public partial class NetLeagueTest : Node
{
    public bool IsHost;
    public string Address = "127.0.0.1";
    public int Port = NetLink.DefaultPort;
    public int Days = 40;
    public float Timeout = 600f;

    private const int Seed = 1994;
    private const int HostClub = 0;

    /// <summary>
    /// Whoever the host's club plays first.
    ///
    /// Not a fixed number, so the run is certain to contain the one fixture both owners are in —
    /// the one neither of them posts. Clubs 0 and 1 happened not to meet inside sixty days, and a
    /// run that never reaches the odd case passes without having tested it. The schedule is a pure
    /// function of the seed, so both machines work this out and get the same answer.
    /// </summary>
    private static int GuestClub =>
        LeagueAudit.FirstOpponentOf(HostClub, Seed, Season.Schedule.FullSeason);

    private float _elapsed;
    private bool _open;
    private bool _reported;
    private int _lastSeen = -1;

    /// <summary>How many times the two owners met, so a run cannot pass without having tested
    /// the one fixture neither of them posts.</summary>
    private int _derbies;
    private Season.SeasonState _season;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;

        if (IsHost)
        {
            GD.Print($"[league] hosting on {Port}, {Days} days");
            NetLink.I.Host(Port);
        }
        else
        {
            GD.Print($"[league] joining {Address}:{Port}, {Days} days");
            NetLink.I.Join(Address, Port);
        }

        NetLink.I.LeagueStarted += OnLeagueStarted;
    }

    private void OnLeagueStarted()
    {
        _season = NetLeague.I.Begin(NetLink.I.MatchSeed, NetLink.I.LocalClubId,
            NetLink.I.RemoteClubId, NetLink.I.LeagueGames, NetLink.I.Innings);

        Game.Instance?.AdoptLeague(_season);
        _open = true;

        GD.Print($"[league] open — this machine runs {Teams.Get(NetLeague.I.LocalClubId).FullName}, " +
                 $"the other runs {Teams.Get(NetLeague.I.RemoteClubId).FullName}");
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;

        // The host settles the terms the moment somebody arrives; both sides then ready up.
        if (IsHost && NetLink.I.State == LinkState.Connected && !NetLink.I.LocalReady)
        {
            NetLink.I.SetLeagueTerms(seed: Seed, hostClubId: HostClub, guestClubId: GuestClub,
                gamesPerTeam: Season.Schedule.FullSeason, innings: 9);
            NetLink.I.SetReady(true);
        }

        if (!IsHost && NetLink.I is { State: LinkState.Connected, LocalReady: false, LeagueMode: true })
        {
            NetLink.I.ChooseClub(GuestClub);
            NetLink.I.SetReady(true);
        }

        if (Fail()) return;
        if (!_open) { Timebox(); return; }

        // Stop on the barrier, not in the middle of a day.
        //
        // The first version of this stopped as soon as the calendar read the target, which is a
        // moment when this machine has played its own game and not yet been sent the other's — so
        // both sides reported a league one game further on for themselves than for their opponent,
        // and the two final fingerprints could not possibly match. The run was correct and the
        // report was of a state that never existed on either machine. Ending on a day boundary,
        // where both sides have everything, is the only comparison worth printing.
        if (_season.CurrentDay >= Days) { Settle((float)delta); return; }

        // Settle whatever this club has today, then say so. Everything after that is the other
        // machine's business, and the day turns over on its own when both have spoken.
        foreach (var mine in NetLeague.I.MyGamesToday().ToList())
        {
            if (NetLeague.I.IsDerby(mine)) _derbies++;
            NetLeague.I.Finished(mine, _season.Resolve(mine));
        }

        NetLeague.I.DoneWithToday();

        if (_season.CurrentDay != _lastSeen)
        {
            _lastSeen = _season.CurrentDay;
            if (_lastSeen % 5 == 0)
            {
                var mine = _season.Book.Record(NetLeague.I.LocalClubId);
                GD.Print($"[league] day {_lastSeen,3}  {_season.GamesPlayed,4} games  " +
                         $"{Teams.Get(NetLeague.I.LocalClubId).Abbrev} {mine.Wins}-{mine.Losses}  " +
                         $"agreed through {NetLink.I.LastAgreedDay}");
            }
        }

        Timebox();
    }

    /// <summary>
    /// A moment's grace before quitting, so the last day's fingerprints cross.
    ///
    /// Both machines reach the final barrier within a frame of each other and each reports the day
    /// on the way through it. Quitting on the spot would close the socket on a report the other
    /// side had not read yet, and the run would end saying it had agreed through the day before
    /// the one it actually agreed through — a false negative in a test whose whole job is to
    /// notice disagreement.
    /// </summary>
    private float _settling;

    private void Settle(float delta)
    {
        _settling += delta;

        // As soon as the last day is agreed there is nothing left to wait for; the timer is only
        // there for the case where the other side hangs up first.
        if (NetLink.I.LastAgreedDay == Days - 1 || _settling >= 3f) Pass();
    }

    private bool Fail()
    {
        if (_reported) return true;

        // Once this side has reached the last day, the other one hanging up is not a failure — it
        // is the other one having finished. Whichever process quits first drops the socket out
        // from under the second, and reading that as a desync failed the run that had just passed.
        bool done = _season != null && _season.CurrentDay >= Days;

        string trouble =
            NetLeague.I.Broken != null ? NetLeague.I.Broken
            : NetLink.I.LeagueDrifted ? $"the two leagues drifted apart on day {NetLink.I.DriftedOnDay}"
            : NetLink.I.State == LinkState.Lost && !done ? NetLink.I.Status
            : null;

        if (trouble == null) return false;

        _reported = true;
        GD.PrintErr($"[league] FAILED — {trouble}");
        GetTree().Quit(1);
        return true;
    }

    private void Pass()
    {
        if (_reported) return;
        _reported = true;

        var mine = _season.Book.Record(NetLeague.I.LocalClubId);
        var theirs = _season.Book.Record(NetLeague.I.RemoteClubId);

        // The two sides agreed on every day up to the last one, or the run has not proved anything
        // it set out to prove. A league that quietly stopped comparing halfway through would print
        // exactly the same happy summary as one that never disagreed.
        bool full = NetLink.I.LastAgreedDay == Days - 1;

        GD.Print($"[league] DONE — {Days} days, {_season.GamesPlayed} games, agreed through day " +
                 $"{NetLink.I.LastAgreedDay}{(full ? "" : " — SHORT OF THE LAST DAY")}.");
        GD.Print($"[league]   {Teams.Get(NetLeague.I.LocalClubId).Abbrev} {mine.Wins}-{mine.Losses}   " +
                 $"{Teams.Get(NetLeague.I.RemoteClubId).Abbrev} {theirs.Wins}-{theirs.Losses}");
        GD.Print($"[league]   fingerprint {LeagueFingerprint.Of(_season):X16}");
        GD.Print($"[league]   the two owners met {_derbies} times" +
                 (_derbies == 0 ? " — that fixture went untested" : ", settled by both and posted by neither"));
        GetTree().Quit(full ? 0 : 1);
    }

    private void Timebox()
    {
        if (_elapsed <= Timeout || _reported) return;
        _reported = true;
        GD.PrintErr($"[league] FAILED — timed out after {Timeout:F0}s on day " +
                    $"{(_season?.CurrentDay ?? -1)}, state {NetLink.I.State}. {NetLink.I.Status}");
        GetTree().Quit(1);
    }
}
