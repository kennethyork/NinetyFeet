using Godot;
using SandlotSlugfest.Core;

namespace SandlotSlugfest.Net;

/// <summary>
/// Drives a whole online match with no window and no hands, so the protocol can be checked
/// against a second process rather than reasoned about.
///
///     godot --headless -- --netplay host [port]
///     godot --headless -- --netplay join 127.0.0.1 [port]
///
/// Both sides play themselves and compare fingerprints every half inning. If the two machines
/// ever disagree about the state of the game, the run says so and fails; if they finish with the
/// same final score, the decision-exchange model is doing its job.
/// </summary>
public partial class NetTest : Node
{
    public bool IsHost;
    public string Address = "127.0.0.1";
    public int Port = NetLink.DefaultPort;

    /// <summary>Give up rather than hang forever if the other side never appears.</summary>
    public float Timeout = 150f;

    private float _elapsed;
    private float _tick;
    private bool _started;
    private bool _reported;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;

        if (IsHost)
        {
            GD.Print($"[net] hosting on {Port}");
            NetLink.I.Host(Port);
        }
        else
        {
            GD.Print($"[net] joining {Address}:{Port}");
            NetLink.I.Join(Address, Port);
        }

        NetLink.I.MatchStarted += OnMatchStarted;
    }

    private void OnMatchStarted()
    {
        GD.Print($"[net] match starting — this machine runs the " +
                 $"{(NetLink.I.LocalIsAway ? "visitors" : "home club")}");

        var g = Game.Instance;
        g.Mode = ControlMode.PlayerVsPlayer;
        g.AwayTeamId = NetLink.I.AwayTeamId;
        g.HomeTeamId = NetLink.I.HomeTeamId;
        g.Innings = NetLink.I.Innings;
        g.AutoPlayNextGame = true;
        g.GoTo("res://Scenes/Game.tscn");
        _started = true;
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;

        // The host sets the terms and readies up as soon as somebody arrives.
        if (IsHost && NetLink.I.State == LinkState.Connected && !NetLink.I.LocalReady)
        {
            NetLink.I.SetTerms(seed: 1994, awayTeamId: 2, homeTeamId: 31, innings: 9);
            NetLink.I.SetReady(true);
        }

        if (!IsHost && NetLink.I.State == LinkState.Connected && !NetLink.I.LocalReady)
            NetLink.I.SetReady(true);

        if (NetLink.I.Desynced && !_reported)
        {
            _reported = true;
            GD.PrintErr("[net] FAILED — the two machines disagree about the game state.");
            GetTree().Quit(1);
            return;
        }

        if (_started && Finish()) return;

        // A progress line, so a stalled match is distinguishable from a slow one.
        _tick += (float)delta;
        if (_started && _tick > 8f)
        {
            _tick = 0f;
            var scene = GetTree().CurrentScene as Gameplay.GameScene;
            var s = scene?.Situation;
            if (s != null)
                GD.Print($"[net] {(s.TopHalf ? "top" : "bot")} {s.Inning}  " +
                         $"{s.AwayScore}-{s.HomeScore}  {s.Outs} out  count {s.CountText}  " +
                         $"| {scene.NetDebug}");
        }

        if (_elapsed > Timeout && !_reported)
        {
            _reported = true;
            GD.PrintErr($"[net] FAILED — timed out after {Timeout:F0}s in state {NetLink.I.State}. " +
                        NetLink.I.Status);
            GetTree().Quit(1);
        }
    }

    /// <summary>Reports the result once the game on this machine is over.</summary>
    private bool Finish()
    {
        var scene = GetTree().CurrentScene as Gameplay.GameScene;
        if (scene?.Situation is not { IsOver: true } sit) return false;
        if (_reported) return true;

        _reported = true;
        GD.Print($"[net] FINAL  away {sit.AwayScore}  home {sit.HomeScore}  " +
                 $"after {sit.Inning} innings, {(NetLink.I.Desynced ? "DESYNCED" : "in step")}");
        GetTree().Quit(NetLink.I.Desynced ? 1 : 0);
        return true;
    }
}
