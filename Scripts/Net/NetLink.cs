using System.Collections.Generic;
using Godot;

namespace SandlotSlugfest.Net;

public enum LinkState { Offline, Hosting, Connecting, Connected, Playing, Lost }

/// <summary>
/// The connection to another player.
///
/// Two machines, one ballgame. The model is decision exchange over a shared seed rather than state
/// replication: both peers build the identical league, start the identical game, and then trade
/// only what each player decides to do. Because the simulation is deterministic — one seeded
/// xorshift generator and a fixed timestep — applying the same decisions in the same order lands
/// both machines on the same result, pitch for pitch. A state stream would cost a hundred times
/// the bandwidth and still be an approximation of what this gets exactly.
///
/// The host is the referee, not the simulation. It owns the ordering of decisions and nothing
/// else. Every command is sent to the host, stamped with a sequence number and echoed to both
/// peers, and both apply strictly in that order — so a steal sign and a pitch thrown in the same
/// instant can never be applied one way on one machine and the other way on the other.
///
/// The one exception is the swing, which the batter applies the moment he makes it. It is the only
/// decision that can happen while a pitch is in the air, so nothing can race it, and routing it
/// through the host would put a round trip between pressing the button and the bat moving.
/// </summary>
public partial class NetLink : Node
{
    public static NetLink I { get; private set; }

    public const int DefaultPort = 27015;

    public LinkState State { get; private set; } = LinkState.Offline;

    /// <summary>True once a game is running over the wire.</summary>
    public bool IsOnline => State is LinkState.Connected or LinkState.Playing;

    public bool IsHost { get; private set; }

    /// <summary>Which club this machine is running. The host takes the home side.</summary>
    public bool LocalIsAway => !IsHost;

    /// <summary>What went wrong, for the lobby to show.</summary>
    public string Status { get; private set; } = "";

    /// <summary>Agreed before the first pitch, so both machines build the same league.</summary>
    public int MatchSeed { get; private set; }
    public int AwayTeamId { get; private set; }
    public int HomeTeamId { get; private set; }
    public int Innings { get; private set; } = 9;

    /// <summary>Set when the other side has picked a club and is ready to play.</summary>
    public bool RemoteReady { get; private set; }
    public bool LocalReady { get; private set; }

    /// <summary>Raised on the main thread when the match should begin.</summary>
    public event System.Action MatchStarted;
    public event System.Action Changed;

    private ENetMultiplayerPeer _peer;
    private int _nextSequence;

    /// <summary>Sequenced commands waiting to be applied, in order.</summary>
    private readonly List<NetCommand> _inbox = new();
    private int _applied;

    /// <summary>Commands the host has stamped but not yet applied locally.</summary>
    public bool HasCommand => _inbox.Count > _applied;

    /// <summary>How many decisions have arrived and how many have been acted on, for the self-test.</summary>
    public string Traffic => $"in={_inbox.Count} done={_applied} seq={_nextSequence}";

    public override void _EnterTree()
    {
        I = this;
        ProcessMode = ProcessModeEnum.Always;

        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    // -----------------------------------------------------------------------
    // Opening and closing the socket
    // -----------------------------------------------------------------------

    public bool Host(int port = DefaultPort)
    {
        Shutdown();

        _peer = new ENetMultiplayerPeer();
        var error = _peer.CreateServer(port, 1);          // one opponent, not a lobby of many
        if (error != Error.Ok)
        {
            Status = $"Could not open port {port}: {error}. Something else may be using it.";
            State = LinkState.Lost;
            Changed?.Invoke();
            return false;
        }

        Multiplayer.MultiplayerPeer = _peer;
        IsHost = true;
        State = LinkState.Hosting;
        Status = $"Waiting for an opponent on port {port}.";
        Changed?.Invoke();
        return true;
    }

    public bool Join(string address, int port = DefaultPort)
    {
        Shutdown();

        _peer = new ENetMultiplayerPeer();
        var error = _peer.CreateClient(address, port);
        if (error != Error.Ok)
        {
            Status = $"Could not reach {address}:{port}: {error}.";
            State = LinkState.Lost;
            Changed?.Invoke();
            return false;
        }

        Multiplayer.MultiplayerPeer = _peer;
        IsHost = false;
        State = LinkState.Connecting;
        Status = $"Connecting to {address}:{port}…";
        Changed?.Invoke();
        return true;
    }

    public void Shutdown()
    {
        if (_peer != null)
        {
            _peer.Close();
            _peer = null;
        }

        Multiplayer.MultiplayerPeer = null;
        State = LinkState.Offline;
        IsHost = false;
        RemoteReady = LocalReady = false;
        _inbox.Clear();
        _applied = 0;
        _nextSequence = 0;
        Changed?.Invoke();
    }

    private void OnPeerConnected(long id)
    {
        State = LinkState.Connected;
        Status = "Opponent connected.";

        // The host sets the terms, so the newcomer is told them straight away.
        if (IsHost) RpcId(id, MethodName.ReceiveTerms, MatchSeed, AwayTeamId, HomeTeamId, Innings);
        Changed?.Invoke();
    }

    private void OnPeerDisconnected(long id)
    {
        State = LinkState.Lost;
        Status = "The other player disconnected.";
        Changed?.Invoke();
    }

    private void OnConnectedToServer()
    {
        State = LinkState.Connected;
        Status = "Connected.";
        Changed?.Invoke();
    }

    private void OnConnectionFailed()
    {
        State = LinkState.Lost;
        Status = "Could not connect. Check the address, the port, and that the host is listening.";
        Changed?.Invoke();
    }

    private void OnServerDisconnected()
    {
        State = LinkState.Lost;
        Status = "The host closed the game.";
        Changed?.Invoke();
    }

    // -----------------------------------------------------------------------
    // Agreeing on the match
    // -----------------------------------------------------------------------

    /// <summary>The host chooses the ground rules; the guest is told them.</summary>
    public void SetTerms(int seed, int awayTeamId, int homeTeamId, int innings)
    {
        if (!IsHost) return;

        MatchSeed = seed;
        AwayTeamId = awayTeamId;
        HomeTeamId = homeTeamId;
        Innings = innings;

        if (State is LinkState.Connected or LinkState.Playing)
            Rpc(MethodName.ReceiveTerms, seed, awayTeamId, homeTeamId, innings);
        Changed?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveTerms(int seed, int awayTeamId, int homeTeamId, int innings)
    {
        MatchSeed = seed;
        AwayTeamId = awayTeamId;
        HomeTeamId = homeTeamId;
        Innings = innings;
        Changed?.Invoke();
    }

    public void SetReady(bool ready)
    {
        LocalReady = ready;
        if (IsOnline) Rpc(MethodName.ReceiveReady, ready);
        Changed?.Invoke();
        TryStart();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveReady(bool ready)
    {
        RemoteReady = ready;
        Changed?.Invoke();
        TryStart();
    }

    private void TryStart()
    {
        if (!IsHost || !LocalReady || !RemoteReady || State != LinkState.Connected) return;

        _inbox.Clear();
        _applied = 0;
        _nextSequence = 0;

        // CallLocal is true, so this fires here as well. Calling it again by hand started the
        // match twice on the host.
        Rpc(MethodName.ReceiveStart);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveStart()
    {
        State = LinkState.Playing;
        Status = "Play ball.";
        _inbox.Clear();
        _applied = 0;
        Changed?.Invoke();
        MatchStarted?.Invoke();
    }

    // -----------------------------------------------------------------------
    // Decisions
    // -----------------------------------------------------------------------

    /// <summary>
    /// Offers a decision for sequencing. The host stamps it immediately; a guest asks the host to.
    /// Nothing is applied here — both machines act only when the stamped command comes back, which
    /// is what keeps two simultaneous decisions in the same order on both sides.
    /// </summary>
    public void Send(NetVerb verb, float a = 0f, float b = 0f, float c = 0f, float d = 0f)
    {
        if (!IsOnline) return;

        if (IsHost) Stamp((int)verb, LocalIsAway, a, b, c, d);
        else RpcId(1, MethodName.Stamp, (int)verb, LocalIsAway, a, b, c, d);
    }

    /// <summary>
    /// The swing, which skips the sequencer.
    ///
    /// It is the only decision that can be made while a pitch is in the air, so nothing can race
    /// it and its position in the order is never in doubt. Sending it through the host instead
    /// would put a round trip between the button and the bat, which is the one place in this game
    /// where that is unaffordable.
    /// </summary>
    public void SendSwing(bool bunt, Vector2 cursor, float atProgress, int swingType, bool powered)
    {
        if (!IsOnline) return;
        Rpc(MethodName.ReceiveSwing, bunt, cursor.X, cursor.Y, atProgress, swingType, powered);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveSwing(bool bunt, float cx, float cy, float atProgress, int swingType, bool powered)
    {
        RemoteSwing = new SwingOrder(bunt, new Vector2(cx, cy), atProgress, swingType, powered);
    }

    /// <summary>
    /// He let it go. Sent explicitly rather than inferred from silence, because from here a pitch
    /// nobody swung at and a swing still in flight look exactly the same.
    /// </summary>
    public void SendTake()
    {
        if (!IsOnline) return;
        Rpc(MethodName.ReceiveTake);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveTake() => RemoteTook = true;

    /// <summary>A swing the other player has taken, waiting to be played out here.</summary>
    public SwingOrder? RemoteSwing { get; private set; }

    /// <summary>Set when the other player has let the pitch go by.</summary>
    public bool RemoteTook { get; private set; }

    public void ClearRemoteSwing()
    {
        RemoteSwing = null;
        RemoteTook = false;
    }

    public readonly record struct SwingOrder(
        bool Bunt, Vector2 Cursor, float AtProgress, int SwingType, bool Powered);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void Stamp(int verb, bool fromAway, float a, float b, float c, float d)
    {
        if (!IsHost) return;

        // ReceiveCommand is CallLocal, so this reaches the host as well as the guest. Calling it
        // again by hand put every decision into the host's own list twice — the host threw two
        // pitches for each one the guest saw, and the two machines were playing different games
        // inside the first at-bat.
        var command = new NetCommand(_nextSequence++, (NetVerb)verb, fromAway, a, b, c, d);
        Rpc(MethodName.ReceiveCommand, command.Pack());
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveCommand(float[] packed)
    {
        var command = NetCommand.Unpack(packed);

        // Insert in sequence order. ENet delivers reliably and in order on the default channel,
        // so this is belt and braces rather than the usual case.
        int at = _inbox.Count;
        while (at > 0 && _inbox[at - 1].Sequence > command.Sequence) at--;
        _inbox.Insert(at, command);
    }

    /// <summary>Looks at the next decision without consuming it.</summary>
    public NetCommand? Peek() => _applied >= _inbox.Count ? null : _inbox[_applied];

    /// <summary>Takes the next decision to act on, or null if there is nothing waiting.</summary>
    public NetCommand? Next()
    {
        if (_applied >= _inbox.Count) return null;
        return _inbox[_applied++];
    }

    // -----------------------------------------------------------------------
    // Catching a desync rather than living with one
    // -----------------------------------------------------------------------

    /// <summary>
    /// Both machines report a fingerprint of the situation at the end of every half inning. If
    /// they ever disagree the game is already wrong, and saying so plainly beats letting the two
    /// players watch different ballgames and argue about the score.
    /// </summary>
    public void ReportChecksum(int fingerprint)
    {
        if (!IsOnline) return;
        Rpc(MethodName.ReceiveChecksum, fingerprint);
        _localFingerprint = fingerprint;
        CompareFingerprints();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveChecksum(int fingerprint)
    {
        _remoteFingerprint = fingerprint;
        CompareFingerprints();
    }

    private int? _localFingerprint;
    private int? _remoteFingerprint;

    /// <summary>Set once the two machines have provably diverged.</summary>
    public bool Desynced { get; private set; }

    // -----------------------------------------------------------------------
    // A shared league, day by day
    // -----------------------------------------------------------------------

    /// <summary>
    /// The day the two sides last agreed on, and the day they stopped.
    ///
    /// A shared league runs on the same principle as a shared game — both machines hold the same
    /// league and exchange only what the humans decide — but over a season rather than an evening,
    /// and that changes what a desync costs. Inside a game it costs the game and you see it at
    /// once. Across a season it costs the season, and without this you would find out in August
    /// when the two of you disagree about who is in first place.
    /// </summary>
    public int LastAgreedDay { get; private set; } = -1;

    /// <summary>Set the first day the two leagues do not match, and never cleared.</summary>
    public int DriftedOnDay { get; private set; } = -1;

    public bool LeagueDrifted => DriftedOnDay >= 0;

    private long? _localLeague;
    private long? _remoteLeague;
    private int _reportedDay = -1;

    /// <summary>Sends this machine's fingerprint of the whole league for a given day.</summary>
    public void ReportLeagueDay(int day, ulong fingerprint)
    {
        if (!IsOnline || LeagueDrifted) return;

        _reportedDay = day;
        _localLeague = unchecked((long)fingerprint);
        Rpc(MethodName.ReceiveLeagueDay, day, _localLeague.Value);
        CompareLeague(day);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
         TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveLeagueDay(int day, long fingerprint)
    {
        // A day arriving out of step is itself a disagreement worth catching.
        if (_reportedDay >= 0 && day != _reportedDay)
        {
            DriftedOnDay = System.Math.Min(day, _reportedDay);
            Status = $"The two leagues are on different days ({_reportedDay} and {day}).";
            Changed?.Invoke();
            return;
        }

        _remoteLeague = fingerprint;
        CompareLeague(day);
    }

    private void CompareLeague(int day)
    {
        if (_localLeague is not { } mine || _remoteLeague is not { } theirs) return;

        if (mine == theirs) LastAgreedDay = day;
        else
        {
            DriftedOnDay = day;
            Status = $"The two leagues drifted apart on day {day}. Nothing after this can be trusted.";
            GD.PushError($"League desync on day {day}: local {mine:X16} against remote {theirs:X16}");
            Changed?.Invoke();
        }

        _localLeague = null;
        _remoteLeague = null;
        _reportedDay = -1;
    }

    private void CompareFingerprints()
    {
        if (_localFingerprint is not { } mine || _remoteFingerprint is not { } theirs) return;

        if (mine != theirs)
        {
            Desynced = true;
            Status = "The two games have drifted apart. This match cannot be trusted.";
            GD.PushError($"Netplay desync: local {mine:X8} against remote {theirs:X8}");
            Changed?.Invoke();
        }

        _localFingerprint = null;
        _remoteFingerprint = null;
    }
}
