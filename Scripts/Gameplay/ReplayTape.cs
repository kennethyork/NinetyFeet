using System.Collections.Generic;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Gameplay;

/// <summary>
/// A recording of one ball in play, so it can be shown again from a better seat.
///
/// The obvious way to build a replay in a game whose simulation is deterministic is to re-run it
/// from the seed. That is wrong here, and expensively so: running the play again would advance the
/// runners, book the outs and change the score a second time, because the simulation mutates the
/// situation as it goes. Untangling that would mean making the whole play engine work on a copy of
/// the game state, which is a large and risky change for a camera angle.
///
/// So this records what was drawn rather than what was decided — the ball, the fielders and the
/// runners, sampled a few dozen times a second. Playing it back cannot possibly disagree with what
/// happened, because it *is* what happened, and it cannot touch the game state because there is
/// nothing in here but positions.
/// </summary>
public sealed class ReplayTape
{
    /// <summary>One sampled instant.</summary>
    public struct Frame
    {
        public Vector2 Ball;
        public float BallHeight;

        /// <summary>Fielder positions, in the order they appear in the play simulation.</summary>
        public Vector2[] Fielders;
        public bool[] HasBall;

        /// <summary>Runner positions, and whether each is still live.</summary>
        public Vector2[] Runners;
        public bool[] RunnerOut;
    }

    private readonly List<Frame> _frames = new();

    /// <summary>Who was in the play, captured once — these do not change mid-play.</summary>
    public PlayerData[] FielderPlayers { get; private set; } = System.Array.Empty<PlayerData>();
    public Position[] FielderSlots { get; private set; } = System.Array.Empty<Position>();
    public PlayerData[] RunnerPlayers { get; private set; } = System.Array.Empty<PlayerData>();

    /// <summary>What the play was, for the caption.</summary>
    public string Caption = "";

    /// <summary>How hard it was struck, which is most of why a replay is worth watching.</summary>
    public float ExitVelocityMph;
    public float LaunchAngle;

    public int Count => _frames.Count;
    public bool HasFootage => _frames.Count > 8;

    /// <summary>Samples are taken at this rate rather than every frame — plenty for a replay.</summary>
    public const float SampleHz = 45f;

    private float _sampleTimer;

    public void Begin(PlaySimulation play, BattedBall ball)
    {
        _frames.Clear();
        _sampleTimer = 0f;
        Caption = "";
        ExitVelocityMph = ball.ExitVelocity / 1.46667f;
        LaunchAngle = ball.LaunchAngle;

        int n = play.Fielders.Count;
        FielderPlayers = new PlayerData[n];
        FielderSlots = new Position[n];
        for (int i = 0; i < n; i++)
        {
            FielderPlayers[i] = play.Fielders[i].Player;
            FielderSlots[i] = play.Fielders[i].Slot;
        }

        int m = play.Runners.Count;
        RunnerPlayers = new PlayerData[m];
        for (int i = 0; i < m; i++) RunnerPlayers[i] = play.Runners[i].Player;
    }

    /// <summary>Takes a sample if enough time has passed. Called while the ball is live.</summary>
    public void Record(PlaySimulation play, float dt)
    {
        _sampleTimer -= dt;
        if (_sampleTimer > 0f) return;
        _sampleTimer = 1f / SampleHz;

        // A very long rundown should not grow without bound.
        if (_frames.Count >= 900) return;

        var f = new Frame
        {
            Ball = play.BallSpot,
            BallHeight = play.BallHeight,
            Fielders = new Vector2[play.Fielders.Count],
            HasBall = new bool[play.Fielders.Count],
            Runners = new Vector2[play.Runners.Count],
            RunnerOut = new bool[play.Runners.Count],
        };

        for (int i = 0; i < play.Fielders.Count; i++)
        {
            f.Fielders[i] = play.Fielders[i].Spot;
            f.HasBall[i] = play.Fielders[i].HasBall;
        }

        for (int i = 0; i < play.Runners.Count; i++)
        {
            f.Runners[i] = play.Runners[i].Spot;
            f.RunnerOut[i] = play.Runners[i].IsOut;
        }

        _frames.Add(f);
    }

    public Frame At(int index) =>
        _frames[Mathf.Clamp(index, 0, Mathf.Max(0, _frames.Count - 1))];

    /// <summary>
    /// Whether this play was worth showing again.
    ///
    /// A replay of a routine ground ball to second is not a replay, it is an interruption. The bar
    /// is deliberately high: something had to happen — the ball left the park, somebody was
    /// retired at a base on a ball hit hard, a run scored, or it was simply struck harder than
    /// almost anything else in the game.
    /// </summary>
    public bool WorthShowing(bool homer, int runs, int outs, bool hit)
    {
        if (!HasFootage) return false;

        if (homer) return true;                              // always
        if (runs > 0 && ExitVelocityMph > 95f) return true;  // a run scored on a struck ball
        if (outs >= 2) return true;                          // a double play
        if (ExitVelocityMph > 108f) return true;             // scorched, whatever came of it
        if (!hit && outs == 1 && ExitVelocityMph > 100f) return true;  // a hard ball run down

        return false;
    }
}
