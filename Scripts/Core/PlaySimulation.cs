using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Data;
using SandlotSlugfest.Stats;

namespace SandlotSlugfest.Core;

public enum PlayPhase { Flight, Loose, Held, Throwing, Dead }

/// <summary>A fielder on the diamond during a live play.</summary>
public sealed class FielderAgent
{
    public PlayerData Player;
    public Position Slot;
    public Vector2 Spot;          // current location in field feet
    public Vector2 Target;        // where he is running
    public float Speed;           // feet per second
    public bool HasBall;
    public bool IsChaser;
    public float ReactionLeft;    // seconds before he starts moving

    /// <summary>Blocks re-rolling the same fielding chance every frame while the ball sits nearby.</summary>
    public float PickupCooldown;

    /// <summary>How close he has to get to make the play. This is the single biggest lever on
    /// batting average on balls in play, so it is tuned rather than guessed.</summary>
    public float CatchRadius => Player.Special switch
    {
        Special.VacuumGlove => 13.44f,
        Special.Backstop => 9.6f,     // nothing squeaks past him
        _ => 6.9f,
    };

    public float ThrowSpeed =>
        (75f + Player.Arm / 10f * 55f) * (Player.Special == Special.CannonArm ? 1.35f : 1f);
}

/// <summary>A baserunner, tracked as a position along the path between two bags.</summary>
public sealed class RunnerAgent
{
    public PlayerData Player;
    public int StartBase;         // the bag he occupied when the ball was struck
    public int FromBase;          // 0 = home (batter), 1..3 — advances as he runs
    public int ToBase;            // 1..4, where 4 means crossing the plate
    public float Progress;        // 0..1 along the current path
    public bool Forced;
    public bool IsOut;
    public bool Scored;
    public bool Held;             // reached a bag and stopped there
    public float Speed;
    public bool IsBatter;

    /// <summary>Highest bag this runner reached safely. A hit still counts if he is later
    /// thrown out stretching for one more.</summary>
    public int MaxBaseReached;

    /// <summary>
    /// How this man reads the play tonight, as a multiplier on his willingness to go. Rolled once
    /// when the ball is struck, not every frame, or he would dither on the bag.
    ///
    /// This is the whole reason a close play at the plate can exist. Without it a runner and the
    /// defence both worked the decision out from the same estimator and always agreed, so a man
    /// only ever ran when he could not be caught and the throw was therefore never worth making.
    /// A third-base coach is guessing, and he is wrong often enough to matter.
    /// </summary>
    public float Nerve = 1f;

    public float BaseSpeed => Player.Special switch
    {
        Special.TurboLegs => Speed * 1.18f,
        Special.PinchRunner => Speed * 1.12f,
        _ => Speed,
    };

    public Vector2 Spot
    {
        get
        {
            Vector2 a = FieldGeometry.Bases[FromBase % 4];
            Vector2 b = FieldGeometry.Bases[ToBase % 4];
            return a.Lerp(b, Mathf.Clamp(Progress, 0f, 1f));
        }
    }

    /// <summary>The bag he is standing on, or -1 when he is between bags.</summary>
    public int OccupiedBase => Held ? ToBase : -1;
}

/// <summary>What a completed play did to the game.</summary>
public struct PlayOutcome
{
    public int Outs;
    public int Runs;
    public bool IsHomeRun;
    public bool IsFoul;
    public bool IsHit;
    public int BasesForBatter;    // 0 when the batter was retired
    /// <summary>Set when the ball went dead and everyone is simply awarded bases (home run, ground rule).</summary>
    public bool AwardBases;
    public string Description;
}

/// <summary>
/// Runs a ball in play from the crack of the bat until the ball is dead: flight and bounces,
/// fielders converging, throws, and runners deciding whether to take the next bag.
/// </summary>
public sealed class PlaySimulation
{
    public const float Gravity = 32.174f;      // feet per second squared

    /// <summary>
    /// Air resistance, as acceleration = coefficient * speed^2. Set from a baseball's terminal
    /// velocity of about 95 mph: at that speed drag exactly cancels gravity, so the coefficient
    /// is g / v_terminal^2. Too small a value and routine fly balls carry 600 feet.
    /// </summary>
    private const float DragCoefficient = 0.00166f;
    private const float BounceRestitution = 0.40f;
    private const float GroundFriction = 0.66f;
    private const float RollDecay = 0.72f;     // per second while rolling

    public PlayPhase Phase { get; private set; } = PlayPhase.Dead;
    public bool Finished { get; private set; } = true;
    public PlayOutcome Outcome { get; private set; }

    public Vector2 BallSpot;                   // field feet
    public float BallHeight;                   // feet
    public Vector2 BallVelocity;
    public float BallVerticalVelocity;
    public bool HasBounced;
    public bool CaughtInAir;

    public readonly List<FielderAgent> Fielders = new();
    public readonly List<RunnerAgent> Runners = new();

    public FielderAgent BallHolder;
    public int ThrowTargetBase = -1;
    public Vector2 ThrowOrigin;
    public float ThrowElapsed;
    public float ThrowDuration;

    /// <summary>Diagnostic counters for --infield: where the defence actually throws.</summary>
    public static readonly int[] ThrowsMade = new int[4];
    public static int ThrowsRefused;

    public string LastEvent = "";
    public bool HumanControlsDefense;
    public bool HumanControlsOffense;

    /// <summary>
    /// Where the human is steering the fielder chasing the ball, in field feet. Without this the
    /// defence played itself and the only thing a human could do was choose a throw.
    /// </summary>
    public Vector2 ManualTarget;
    public bool UseManualFielder;

    /// <summary>The fielder the human is currently steering, for the on-screen marker.</summary>
    public FielderAgent Controlled =>
        UseManualFielder ? Fielders.FirstOrDefault(f => f.IsChaser) : null;

    private GameSituation _sit;
    private BattedBall _batted;
    private Rng _rng;
    private float _elapsed;
    private float _deadTimer;
    private int _outsRecorded;
    private int _runsScored;
    private bool _isHomeRun;
    private float _reassignTimer;
    private float _heldTimer;
    private bool _landed;
    private bool _errorCharged;

    public float Elapsed => _elapsed;
    public Vector2 PredictedLanding { get; private set; }

    // -----------------------------------------------------------------------
    // Setup
    // -----------------------------------------------------------------------

    public void Begin(GameSituation sit, BattedBall batted, int seed)
    {
        _sit = sit;
        _batted = batted;
        _rng = new Rng(seed);
        _elapsed = 0f;
        _deadTimer = 0f;
        _outsRecorded = 0;
        _runsScored = 0;
        _isHomeRun = false;
        _landed = false;
        _reassignTimer = 0f;
        _heldTimer = 0f;
        _errorCharged = false;
        HasBounced = false;
        CaughtInAir = false;
        BallHolder = null;
        ThrowTargetBase = -1;
        Finished = false;
        Phase = PlayPhase.Flight;
        LastEvent = "";

        // --- Launch the ball. Spray 0 is straight to centre field. ---
        float sprayRad = Mathf.DegToRad(batted.SprayAngle);
        float launchRad = Mathf.DegToRad(batted.LaunchAngle);
        float horizontal = batted.ExitVelocity * Mathf.Cos(launchRad);
        BallVelocity = new Vector2(Mathf.Sin(sprayRad), Mathf.Cos(sprayRad)) * horizontal;
        BallVerticalVelocity = batted.ExitVelocity * Mathf.Sin(launchRad);
        BallSpot = new Vector2(0f, 2f);        // just in front of the plate
        BallHeight = 3f;
        PredictedLanding = PredictLanding();

        // --- Defence takes the field. ---
        Fielders.Clear();
        var defense = sit.FieldingTeam;
        foreach (Position slot in FieldGeometry.DefensiveSlots)
        {
            var player = defense.Fielder(slot);
            if (player == null) continue;

            // Where the manager put him, which used to be nowhere but his ordinary spot.
            var post = Positioning.SpotFor(slot, sit.Defence,
                Positioning.PullsLeft(sit.Batter, sit.CurrentPitcher));

            Fielders.Add(new FielderAgent
            {
                Player = player,
                Slot = slot,
                Spot = post,
                Target = post,
                // A major-league fielder covers ground at roughly 27 feet a second, and the best
                // closer to 30. This started at 14.5 + 13.5, which put an average fielder at 22.6
                // — slower than the batter was running to first. Balls that should have been run
                // down fell in, and the league's BABIP sat at .357 against a real .291.
                Speed = 16.6f + player.Speed / 10f * 10.1f,
                ReactionLeft = Mathf.Max(0.05f, 0.34f - player.Fielding / 10f * 0.20f),
            });
        }

        // --- Runners: everyone already aboard, plus the batter breaking for first. ---
        Runners.Clear();
        bool forceChain = true;                // the batter always forces the runner on first
        for (int b = 1; b <= 3; b++)
        {
            if (!sit.RunnerOn(b)) { forceChain = false; continue; }
            var p = sit.Runners[b];

            // A forced runner has to go, and with two away everyone runs on contact — there is
            // no risk of being doubled off. Otherwise he stays put until the play tells him to
            // run, since breaking for the next bag on every ground ball just gets him thrown out.
            bool goesOnContact = forceChain || sit.Outs == 2;

            Runners.Add(new RunnerAgent
            {
                Player = p,
                StartBase = b,
                FromBase = b,
                ToBase = goesOnContact ? b + 1 : b,
                Progress = goesOnContact ? 0f : 1f,
                Held = !goesOnContact,
                Forced = forceChain,
                MaxBaseReached = b,
                Speed = 21f + p.Speed / 10f * 9f,
                Nerve = 0.72f + _rng.NextFloat() * 0.28f,
            });
        }

        Runners.Add(new RunnerAgent
        {
            Player = sit.Batter,
            FromBase = 0,
            ToBase = 1,
            Progress = 0f,
            Forced = true,
            IsBatter = true,
            // Out of the box is slower than a runner already in motion: he starts from a standstill
            // and has to get rid of the bat. Timed against the real thing, home to first is about
            // 4.35 seconds on average, 4.0 for the quickest men in the game and near 5.2 for a
            // catcher. Over 90 feet that is 20.7 feet a second, not the 24.4 this used to give an
            // average hitter — which had everyone reaching the bag two thirds of a second early
            // and turned routine ground balls into infield hits.
            Speed = 17f + sit.Batter.Speed / 10f * 5.5f,
        });

        AssignFielders();
    }

    // -----------------------------------------------------------------------
    // Per-frame update
    // -----------------------------------------------------------------------

    public void Update(float dt)
    {
        if (Finished) return;
        _elapsed += dt;

        switch (Phase)
        {
            case PlayPhase.Flight:
            case PlayPhase.Loose:
                UpdateBall(dt);
                UpdateFielders(dt);
                TryPickUp();
                break;
            case PlayPhase.Held:
                UpdateFielders(dt);
                DecideThrow(dt);
                break;
            case PlayPhase.Throwing:
                UpdateThrow(dt);
                UpdateFielders(dt);
                break;
            case PlayPhase.Dead:
                break;
        }

        UpdateRunners(dt);
        CheckPlayOver(dt);
    }

    private void UpdateBall(float dt)
    {
        if (Phase == PlayPhase.Dead) return;

        // Air resistance acts against the full 3D velocity, scaled by how thick the air is in
        // this particular park — a mile up, the ball simply keeps going.
        float speed = new Vector3(BallVelocity.X, BallVelocity.Y, BallVerticalVelocity).Length();
        float drag = DragCoefficient * FieldGeometry.AirDensity * speed;

        BallVelocity -= BallVelocity * drag * dt;
        BallVerticalVelocity -= (Gravity + BallVerticalVelocity * drag) * dt;

        // Wind acts on a ball in the air, and only on one that is genuinely up there — a ground
        // ball is not blown to the warning track. It works through drag rather than as a shove,
        // which is why a stiff breeze turns a warning-track out into a home run without making
        // line drives curve.
        if (BallHeight > 6f && FieldGeometry.Wind != 0f)
            BallVelocity += new Vector2(0f, FieldGeometry.Wind) * drag * dt;

        BallSpot += BallVelocity * dt;
        BallHeight += BallVerticalVelocity * dt;

        if (BallHeight <= 0f)
        {
            BallHeight = 0f;
            if (!_landed)
            {
                _landed = true;
                if (!FieldGeometry.IsFair(BallSpot))
                {
                    DeclareFoul();
                    return;
                }
            }

            if (Mathf.Abs(BallVerticalVelocity) > 2.5f)
            {
                BallVerticalVelocity = -BallVerticalVelocity * BounceRestitution;
                BallVelocity *= GroundFriction;
                HasBounced = true;
            }
            else
            {
                BallVerticalVelocity = 0f;
                BallVelocity -= BallVelocity * RollDecay * dt;
                HasBounced = true;
            }
            Phase = PlayPhase.Loose;
        }

        // A ball that clears the wall on the fly is gone — but the wall's height varies wildly
        // from park to park, so a drive that leaves one yard rattles off a monster in another.
        if (!HasBounced && FieldGeometry.IsBeyondFence(BallSpot) &&
            BallHeight > FieldGeometry.FenceHeightAt(FieldGeometry.AngleFromCenter(BallSpot)))
        {
            DeclareHomeRun();
            return;
        }

        // A ball that rolls or bounces past the wall is a ground-rule double.
        if (HasBounced && FieldGeometry.IsBeyondFence(BallSpot))
        {
            LastEvent = "Ground-rule double!";
            EndPlayWithHit(2);
            return;
        }

        // Balls that trickle foul before reaching a bag are foul.
        if (BallHeight <= 0f && !FieldGeometry.IsFair(BallSpot) && BallSpot.Y < FieldGeometry.BaseOffset)
        {
            DeclareFoul();
        }
    }

    private void UpdateFielders(float dt)
    {
        _reassignTimer -= dt;
        if (_reassignTimer <= 0f && Phase is PlayPhase.Flight or PlayPhase.Loose)
        {
            PredictedLanding = PredictLanding();
            AssignFielders();
            _reassignTimer = 0.25f;
        }

        // A human-steered chaser gets his target refreshed every frame, not every quarter second.
        if (UseManualFielder)
        {
            var steered = Fielders.FirstOrDefault(f => f.IsChaser);
            if (steered != null) steered.Target = ManualTarget;
        }

        foreach (var f in Fielders)
        {
            if (f.PickupCooldown > 0f) f.PickupCooldown -= dt;
            if (f.HasBall) continue;
            if (f.ReactionLeft > 0f) { f.ReactionLeft -= dt; continue; }

            Vector2 toTarget = f.Target - f.Spot;
            float dist = toTarget.Length();
            if (dist < 0.5f) continue;
            f.Spot += toTarget / dist * Mathf.Min(f.Speed * dt, dist);
        }
    }

    /// <summary>Sends the best-placed fielder after the ball and puts the rest on their bags.</summary>
    private void AssignFielders()
    {
        FielderAgent best = null;
        float bestTime = float.MaxValue;

        foreach (var f in Fielders)
        {
            f.IsChaser = false;
            float t = InterceptTime(f);
            if (t < bestTime) { bestTime = t; best = f; }
        }

        if (best != null)
        {
            best.IsChaser = true;
            // A human steering the defence overrides the CPU's route to the ball.
            best.Target = UseManualFielder ? ManualTarget : PredictedLanding;
        }

        // Bases are covered by the men whose job it is, not by whoever happens to be nearest.
        //
        // Nearest-first sounds reasonable and looks absurd: on a ball into the left-field corner
        // the right fielder is the closest free man to second, so he would abandon his position
        // and sprint two hundred feet into the infield to stand on the bag. Meanwhile the second
        // baseman, whose job it is, stood still. Every ball in play had three outfielders running
        // the wrong way.
        //
        // Real coverage is a short list of responsibilities with one fallback each, for when the
        // man who normally takes it is the one chasing the ball.
        var free = Fielders.Where(f => !f.IsChaser).ToList();

        FielderAgent Claim(params Position[] preference)
        {
            foreach (var slot in preference)
            {
                var f = free.FirstOrDefault(x => x.Slot == slot);
                if (f == null) continue;
                free.Remove(f);
                return f;
            }
            return null;
        }

        void Cover(int bag, params Position[] preference)
        {
            var man = Claim(preference);
            if (man != null) man.Target = FieldGeometry.Bases[bag];
        }

        Cover(1, Position.First, Position.Second, Position.P);
        Cover(2, Position.Short, Position.Second);
        Cover(3, Position.Third, Position.Short);
        Cover(0, Position.C, Position.P);

        // Nobody stands and watches. An outfielder with no bag to cover moves toward the play to
        // back it up, and an infielder shades that way. They stop short of the ball itself so the
        // man on it still makes the play — this is about the field looking alive, not about
        // quietly handing the defence extra range.
        foreach (var f in free)
        {
            bool outfielder = f.Slot is Position.Left or Position.Center or Position.Right;
            // Kept deliberately small. At a third of the way to the ball the backing-up outfielders
            // were cutting off balls in the gap that should have gone for two, and doubles fell 20%.
            f.Target = f.Spot.Lerp(PredictedLanding, outfielder ? 0.14f : 0.16f);
        }
    }

    /// <summary>Roughly how long this fielder needs to reach the ball, used to pick the chaser.</summary>
    private float InterceptTime(FielderAgent f)
    {
        float travel = f.Spot.DistanceTo(PredictedLanding) / Mathf.Max(f.Speed, 1f);
        return travel + f.ReactionLeft;
    }

    /// <summary>Steps a throwaway copy of the ball forward to find where it will come down.</summary>
    private Vector2 PredictLanding()
    {
        Vector2 pos = BallSpot;
        Vector2 vel = BallVelocity;
        float h = BallHeight;
        float vz = BallVerticalVelocity;

        const float step = 1f / 60f;
        float density = FieldGeometry.AirDensity;
        float wind = FieldGeometry.Wind;

        // The prediction has to model the same forces the ball actually feels, or the outfielders
        // break toward a spot the wind is going to take the ball away from.
        for (int i = 0; i < 480; i++)
        {
            float speed = new Vector3(vel.X, vel.Y, vz).Length();
            float drag = DragCoefficient * density * speed;
            vel -= vel * drag * step;
            vz -= (Gravity + vz * drag) * step;
            if (h > 6f && wind != 0f) vel += new Vector2(0f, wind) * drag * step;
            pos += vel * step;
            h += vz * step;
            if (h <= 0f) return pos;
        }
        return pos;
    }

    private void TryPickUp()
    {
        foreach (var f in Fielders)
        {
            if (f.ReactionLeft > 0f) continue;
            float dist = f.Spot.DistanceTo(BallSpot);
            if (dist > f.CatchRadius) continue;

            // One roll per fielding chance, not one per frame.
            if (f.PickupCooldown > 0f) continue;

            // A ball above the glove can only be caught by someone who can reach it.
            float reach = f.Player.Special == Special.WallClimber ? 14f : 9f;
            if (BallHeight > reach) continue;

            // Clean hands make the play; bad ones kick it around.
            float cleanChance = 0.86f + f.Player.Fielding / 10f * 0.13f;
            bool inAir = !HasBounced && BallHeight > 1.2f;
            if (inAir) cleanChance -= 0.04f;   // catching a liner is harder than a routine grounder

            if (!_rng.Chance(cleanChance))
            {
                // Bobble: the ball squirts away and the fielder loses a beat.
                f.ReactionLeft = 0.45f;
                f.PickupCooldown = 0.6f;
                BallVelocity = BallVelocity.Rotated(_rng.Range(-1.0f, 1.0f)) * 0.35f;
                // Score it once, however many times it gets kicked around.
                if (!_errorCharged)
                {
                    _errorCharged = true;
                    if (_sit.TopHalf) _sit.HomeErrors++; else _sit.AwayErrors++;
                }
                LastEvent = $"{f.Player.ShortName} bobbles it!";
                continue;
            }

            f.HasBall = true;
            BallHolder = f;
            BallSpot = f.Spot;
            BallHeight = 3.5f;
            BallVelocity = Vector2.Zero;
            BallVerticalVelocity = 0f;

            if (inAir)
            {
                CaughtInAir = true;
                HandleCatch(f);
            }
            else
            {
                Phase = PlayPhase.Held;
                _heldTimer = 0f;
                LastEvent = $"{f.Player.ShortName} fields it.";
            }
            return;
        }
    }

    /// <summary>A ball caught on the fly retires the batter; runners must tag up.</summary>
    private void HandleCatch(FielderAgent f)
    {
        var batter = Runners.FirstOrDefault(r => r.IsBatter);
        if (batter != null && !batter.IsOut)
        {
            batter.IsOut = true;
            batter.Held = true;
            _outsRecorded++;
        }

        LastEvent = $"Caught by {f.Player.ShortName}!";

        // Runners who left early must go back; a deep enough fly lets them tag up and go.
        bool deepEnough = BallSpot.Length() > 230f;
        bool outsLeft = _outsRecorded + _sit.Outs < 3;

        foreach (var r in Runners)
        {
            if (r.IsBatter || r.IsOut || r.Scored) continue;
            r.Forced = false;

            if (deepEnough && outsLeft && r.StartBase >= 2)
            {
                // Tag up from the bag he started on and take the next one — the sacrifice fly.
                r.Held = false;
                r.FromBase = r.StartBase;
                r.ToBase = r.StartBase + 1;
                r.Progress = 0f;
            }
            else
            {
                // Retreat to the bag he started on.
                r.FromBase = r.StartBase;
                r.ToBase = r.StartBase;
                r.Progress = 1f;
                r.Held = true;
            }
        }

        Phase = PlayPhase.Held;
        _heldTimer = 0f;
    }

    // -----------------------------------------------------------------------
    // Throws
    // -----------------------------------------------------------------------

    private void DecideThrow(float dt)
    {
        if (BallHolder == null) { Phase = PlayPhase.Loose; return; }

        // Give the fielder a beat to find the grip before he lets it go.
        _heldTimer += dt;
        if (_heldTimer < 0.25f) return;

        // A human on defence throws where they choose. If they do not choose in time the
        // fielder makes the play himself, so the game can never sit waiting forever.
        if (HumanControlsDefense && _heldTimer < 2.0f)
        {
            if (Input.IsActionJustPressed(InputActions.ThrowFirst)) { StartThrow(1); return; }
            if (Input.IsActionJustPressed(InputActions.ThrowSecond)) { StartThrow(2); return; }
            if (Input.IsActionJustPressed(InputActions.ThrowThird)) { StartThrow(3); return; }
            if (Input.IsActionJustPressed(InputActions.ThrowHome)) { StartThrow(0); return; }
            return;
        }

        int target = ChooseThrowTarget();
        if (target < 0) { ThrowsRefused++; return; }   // nothing to get; let the play wind down
        ThrowsMade[target]++;
        StartThrow(target);
    }

    /// <summary>Picks the lead base where the defence can still record an out.</summary>
    private int ChooseThrowTarget()
    {
        int best = -1;
        float bestValue = float.MinValue;

        foreach (var r in Runners)
        {
            if (r.IsOut || r.Scored || r.Held) continue;
            int bag = r.ToBase;
            if (bag > 3) bag = 0;     // a play at the plate

            float runnerTime = RemainingTime(r);
            float throwTime = ThrowTime(FieldGeometry.Bases[bag % 4]);

            // Only chase outs the defence can comfortably get. A real infielder concedes the bag
            // rather than air it out on a play he is going to lose anyway.
            // A quarter of a second of daylight was demanded before anybody threw. That is a
            // fielder declining every close play, and it is the wrong way round: the throw is
            // made and the race decides it, because a runner who reaches the bag first is off the
            // list before the ball arrives. Tightening it puts runners out and takes runs off the
            // board without touching the bat.
            if (throwTime < runnerTime - 0.10f)
            {
                // Take the out you are sure of.
                //
                // This used to score by which base was furthest along — r.ToBase * 10 — so the
                // lead runner won whenever he was catchable at all, and a bang-bang play at the
                // plate beat a certain out at first every single time. A real infielder does the
                // opposite unless the run decides something: he banks the out and concedes the
                // run, and only goes home when he has him or when the run cannot be given away.
                //
                // Certainty is the margin he has to spare, and it now dominates. The base still
                // counts for something, so a comfortable play on the lead runner is still the
                // right one — it is only the marginal ones that are now declined.
                float margin = runnerTime - throwTime;

                // Only the play at the plate is second-guessed. Scoring the whole decision by
                // certainty instead of by base was tried and was much worse: a force at second
                // usually has a thinner margin than the batter at first, so the defence took the
                // sure out every time and the double play went with it — 0.47 a game against a
                // real 1.44. The lead-runner preference is what turns two, and it stays.
                if (bag == 0)
                {
                    // Whether this run can still decide anything. Four up in the third is not the
                    // same as one up in the ninth, and the same throw is right in one and wrong
                    // in the other. When it cannot, a close play at the plate is declined and the
                    // sure out is taken instead — which is what an infielder actually does.
                    int lead = _sit.FieldingScore - _sit.BattingScore;
                    bool decisive = lead is >= -1 and <= 3
                                 || _sit.Inning >= _sit.ScheduledInnings - 1;
                    if (!decisive && margin < 0.6f) continue;
                }

                float value = r.ToBase * 10f + margin;
                if (value > bestValue) { bestValue = value; best = bag; }
            }
        }

        // If nothing is catchable, throw the ball back toward the infield to stop the bleeding.
        if (best < 0 && BallSpot.Length() > 160f) return 2;
        return best;
    }

    private float RemainingTime(RunnerAgent r)
    {
        float remaining = (1f - r.Progress) * FieldGeometry.BasePathLength;
        return remaining / Mathf.Max(r.BaseSpeed, 1f);
    }

    /// <summary>
    /// How long the ball takes to reach a bag, used to decide whether an out is there.
    ///
    /// KNOWN DEFECT, MEASURED, NOT YET FIXED. This charges the play for the glove-to-hand
    /// transfer that DecideThrow has already spent a quarter of a second waiting through, so the
    /// same time is counted twice — and on a routine ground ball the two estimates are separated
    /// by less than that, so the infielder decides he cannot get the batter and holds the ball.
    ///
    /// What --sim measures: of every ball in play not caught on the fly, 95% goes down as a hit.
    /// The defence records 0.75 ground outs a game. There are 0.05 double plays a game against a
    /// real 1.44, and the league's entire out total is carried by fly balls — 68% of balls in
    /// play are caught in the air against a real 45%.
    ///
    /// Dropping the double-charge (using ThrowTravel alone here) is a strict improvement to the
    /// defence and was tried: ground outs went to 1.12 and runners thrown out on the bases went
    /// from 0.8 to 1.8 a game. But it is only part of the fix — the infield still lets almost
    /// every grounder through — and on its own it drops league scoring 15% below the real rate.
    /// Left as it stands so the league stays calibrated. Fixing it properly means giving the
    /// infield real range and then re-deriving the batted-ball calibration behind it.
    /// </summary>
    private float ThrowTime(Vector2 target) =>
        BallHolder == null ? float.MaxValue : BallArrivalTime(target);

    public void StartThrow(int targetBase)
    {
        if (BallHolder == null) return;
        ThrowTargetBase = targetBase;
        ThrowOrigin = BallHolder.Spot;
        ThrowElapsed = 0f;
        Vector2 dest = FieldGeometry.Bases[targetBase % 4];

        // Not every throw is on the bag. A weak or careless arm sails one now and then, which is
        // what lets aggressive runners get away with taking the extra base.
        float offLine = 1f + (1f - BallHolder.Player.Arm / 10f) * 0.35f * _rng.NextFloat();
        ThrowDuration = Mathf.Max(0.18f, ThrowOrigin.DistanceTo(dest) / BallHolder.ThrowSpeed * offLine);
        BallHolder.HasBall = false;
        BallHolder = null;
        Phase = PlayPhase.Throwing;
    }

    private void UpdateThrow(float dt)
    {
        ThrowElapsed += dt;
        float t = Mathf.Clamp(ThrowElapsed / ThrowDuration, 0f, 1f);
        Vector2 dest = FieldGeometry.Bases[ThrowTargetBase % 4];
        BallSpot = ThrowOrigin.Lerp(dest, t);
        BallHeight = 4f + Mathf.Sin(t * Mathf.Pi) * 6f;

        if (t < 1f) return;

        // The throw has arrived. Is anyone covering, and did it beat the runner?
        var cover = Fielders
            .Where(f => f.Spot.DistanceTo(dest) < 8f)
            .OrderBy(f => f.Spot.DistanceSquaredTo(dest))
            .FirstOrDefault();

        if (cover == null)
        {
            // Nobody home — the ball gets away and runners keep going.
            LastEvent = "No one covering!";
            Phase = PlayPhase.Loose;
            BallVelocity = (dest - ThrowOrigin).Normalized() * 25f;
            BallVerticalVelocity = 4f;
            HasBounced = true;
            ThrowTargetBase = -1;
            return;
        }

        cover.HasBall = true;
        BallHolder = cover;
        BallSpot = cover.Spot;
        Phase = PlayPhase.Held;
        _heldTimer = 0f;

        int bag = ThrowTargetBase;
        ThrowTargetBase = -1;

        foreach (var r in Runners)
        {
            if (r.IsOut || r.Scored || r.Held) continue;

            // Nothing is retired once the half inning is full. Every other place that records an
            // out checks this and the throw did not, so a relay that beat a runner to the bag with
            // two already away booked a third and a fourth out into the same half — rare enough
            // that it took a shifted random stream to turn up in a forty-game audit.
            if (_sit.Outs + _outsRecorded >= 3) break;

            int runnerTargetBag = r.ToBase > 3 ? 0 : r.ToBase;
            if (runnerTargetBag != bag) continue;

            if (r.Forced || r.Progress > 0.55f)
            {
                r.IsOut = true;
                r.Held = true;
                _outsRecorded++;
                string where = FieldGeometry.BaseName(bag);
                LastEvent = $"Out at {where}!";
            }
        }
    }

    // -----------------------------------------------------------------------
    // Runners
    // -----------------------------------------------------------------------

    private void UpdateRunners(float dt)
    {
        foreach (var r in Runners)
        {
            if (r.IsOut || r.Scored) continue;

            if (r.Held)
            {
                // Standing on a bag. A runner freezes on a ball in the air, because if it is
                // caught he is doubled off. Once it is down there is nothing left to fear and the
                // only question is whether he beats the throw, which ShouldKeepRunning already
                // asks — and BallArrivalTime already knows how to price a fielder who has the
                // ball in his hand.
                //
                // This also required the ball to still be loose, which meant a runner froze at the
                // bag the instant anyone picked it up. A man on second could not score on a single,
                // because the outfielder had the ball by the time he was allowed to think about it.
                // Runners were stranded at 12.7 a game and the league scored 30% under the real
                // rate while putting exactly the right number of men on base.
                bool ballIsLive = HasBounced && Phase != PlayPhase.Dead;
                if (ballIsLive && ShouldKeepRunning(r))
                {
                    // Read the head start before clearing Held, or he decides on a lead he then
                    // does not get and runs the full ninety feet into a throw he cannot beat.
                    float start = 1f - DistanceToNextBag(r) / FieldGeometry.BasePathLength;
                    r.Held = false;
                    r.ToBase = r.FromBase + 1;
                    r.Progress = Mathf.Clamp(start, 0f, 0.78f);
                }
                continue;
            }

            float perBase = r.BaseSpeed / FieldGeometry.BasePathLength;
            r.Progress += perBase * dt;

            if (r.Progress < 1f) continue;

            r.Progress = 1f;
            r.FromBase = r.ToBase;
            r.MaxBaseReached = Mathf.Max(r.MaxBaseReached, Mathf.Min(r.ToBase, 4));
            // The force that sent him here is satisfied once he touches the bag.
            r.Forced = false;

            if (r.ToBase >= 4)
            {
                r.Scored = true;
                r.Held = true;
                _runsScored++;
                LastEvent = $"{r.Player.ShortName} scores!";
                continue;
            }

            if (ShouldKeepRunning(r))
            {
                r.ToBase = r.FromBase + 1;
                r.Progress = 0f;
            }
            else
            {
                r.Held = true;
            }
        }

        RecomputeForces();
    }

    /// <summary>A runner takes the extra bag when the defence cannot get the ball there in time.</summary>
    private bool ShouldKeepRunning(RunnerAgent r)
    {
        if (_isHomeRun) return true;
        if (r.FromBase >= 4) return false;
        if (Phase == PlayPhase.Dead) return false;

        // Human override: hold or send with the dedicated keys.
        if (HumanControlsOffense)
        {
            if (Input.IsActionPressed(InputActions.HoldRunners)) return false;
            if (Input.IsActionPressed(InputActions.SendRunners)) return true;
        }

        Vector2 dest = FieldGeometry.Bases[(r.FromBase + 1) % 4];
        float runnerTime = DistanceToNextBag(r) / Mathf.Max(r.BaseSpeed, 1f);

        // Near break-even, with the quick guys willing to take a little risk. Anything much
        // above 1.0 means deliberately running into outs, since the defence throws whenever it
        // can beat the runner.
        // Near break-even and genuinely uncertain, rather than a guarantee.
        //
        // This sat at 0.42 to 0.54, which meant a runner only left the bag when he was about
        // twice as fast as the throw. The defence's own test is the mirror of it and calls the
        // same estimator, so the two could never both be satisfied: any runner who had chosen to
        // run had already proved the throw could not get him. Measured over 5,000 batted balls,
        // the defence threw home 0 times and to third 0 times. Every throw in the game went to a
        // man with no choice — the batter, forced to first.
        //
        // Nerve straddles break-even, so the bold ones go when it is close and the throw becomes
        // worth making. Raising the base is cheaper than it looks: DistanceToNextBag already gives
        // a runner his secondary lead, which is why moving this from 0.54 to 0.82 once changed
        // league scoring by five hundredths of a run a game.
        float aggression = (0.66f + r.Player.Speed / 10f * 0.14f) * r.Nerve;
        if (r.Player.Special == Special.PinchRunner) aggression += 0.22f;   // huge jump

        // The batter-runner is the one man on the field with no head start — he begins at the
        // plate, not on a bag, so every base he takes is one he has run the whole way for. A base
        // coach holds him a good deal sooner than he holds a man who was already aboard.
        //
        // Stretching to third is the classic low-percentage play: letting him use a lead runner's
        // margin gave 0.93 triples a game against a real 0.29. Turning singles into doubles is the
        // same mistake one base earlier and far more common — with only the third-base rule in
        // place, singles came out exactly right and doubles ran 25% over.
        // The batter-runner and the men already aboard need separate settings, because they decide
        // different statistics. How boldly a runner on base takes the next bag is what sets the
        // league's run scoring; how boldly the batter takes second is what sets its doubles. Run
        // off one number and there is no setting that gets both right — pulling runs down to +5%
        // cost thirteen per cent of the doubles and nearly two thirds of the triples.
        // Raised when the defence stopped sending outfielders in to cover bases. With the outfield
        // actually manned, a ball in the gap is cut off far sooner, and the batter has to be
        // correspondingly bolder about taking two — doubles fell 19% on the day fielding was fixed.
        // The batter-runner is deliberately left on the old scale.
        //
        // Raising the base aggression for everybody sent triples to 1.48 a game against a real
        // 0.29 — a batter who will gamble on a close play at the plate will also gamble on
        // stretching a double into a triple, and that is the single most punished decision in
        // baseball. These two multipliers are the old ones scaled back by the same factor the
        // base went up, so how boldly the batter runs is exactly where it was measured and only
        // the men already aboard have been given nerve.
        if (r.IsBatter) aggression *= r.FromBase >= 2 ? 0.565f : 0.80f;

        return runnerTime < BallArrivalTime(dest) * aggression;
    }

    /// <summary>
    /// How far this runner still has to go to reach the next bag.
    ///
    /// The two halves of the advance decision were being measured from different moments. The
    /// ball's arrival was priced from now — a fielder standing there with it in his hand — while
    /// the runner was priced as a full ninety feet from a standing start on the bag, as though he
    /// had watched the whole play from there. He had not: he takes a secondary lead and reads the
    /// ball off the bat, so by the time an outfielder comes up with a single he is most of the way
    /// to the next base. Comparing a man who has not moved against a throw that is ready to go
    /// meant a runner on second could not score on a single at any aggression setting, which is
    /// why raising aggression from 0.54 to 0.82 changed the league's run total by 0.05 a game.
    ///
    /// The head start grows with how long the ball has been alive and is capped, so he is well
    /// down the line on a ball hit to the outfield and barely off the bag on an infield grounder.
    /// </summary>
    private float DistanceToNextBag(RunnerAgent r)
    {
        // A runner who has just touched a bag and is deciding whether to take another has the
        // whole ninety feet in front of him — the head start below belongs to a man standing on
        // the base when the ball was struck, not to one already rounding it.
        if (!r.Held) return FieldGeometry.BasePathLength;

        float lead = 14f + Mathf.Min(_elapsed, 2.6f) * r.BaseSpeed * 0.55f;
        return Mathf.Max(20f, FieldGeometry.BasePathLength - lead);
    }

    /// <summary>How long before the defence can have the ball at <paramref name="dest"/>.</summary>
    private float BallArrivalTime(Vector2 dest)
    {
        const float Transfer = 0.75f;    // glove-to-hand plus the crow hop

        if (BallHolder != null)
            return Transfer + ThrowTravel(BallHolder.Spot, dest, BallHolder.ThrowSpeed);

        // Still loose: someone has to run it down before anything can be thrown.
        var chaser = Fielders.FirstOrDefault(f => f.IsChaser) ?? Fielders[0];
        float toBall = chaser.Spot.DistanceTo(PredictedLanding) / Mathf.Max(chaser.Speed, 1f);
        return toBall + Transfer + ThrowTravel(PredictedLanding, dest, chaser.ThrowSpeed);
    }

    /// <summary>Throw time, with a penalty for anything long enough to need a cutoff man.</summary>
    private static float ThrowTravel(Vector2 from, Vector2 to, float speed)
    {
        float dist = from.DistanceTo(to);
        float relay = dist > 140f ? (dist - 140f) / 170f : 0f;
        return dist / Mathf.Max(speed, 1f) + relay;
    }

    /// <summary>Recomputes who is forced, since a force only exists with every bag behind occupied.</summary>
    private void RecomputeForces()
    {
        bool chain = Runners.Any(r => r.IsBatter && !r.IsOut && !r.Held);
        for (int bag = 1; bag <= 3; bag++)
        {
            var occupant = Runners.FirstOrDefault(r =>
                !r.IsOut && !r.Scored && r.ToBase == bag + 1 && !r.Held);
            if (occupant == null) { chain = false; continue; }
            occupant.Forced = chain;
        }
    }

    // -----------------------------------------------------------------------
    // Ending the play
    // -----------------------------------------------------------------------

    private void CheckPlayOver(float dt)
    {
        if (Phase == PlayPhase.Dead) return;

        bool anyoneRunning = Runners.Any(r => !r.IsOut && !r.Scored && !r.Held);
        bool threeOuts = _sit.Outs + _outsRecorded >= 3;

        if (threeOuts)
        {
            EndPlay();
            return;
        }

        if (!anyoneRunning && Phase is PlayPhase.Held)
        {
            _deadTimer += dt;
            if (_deadTimer > 0.5f) EndPlay();
            return;
        }

        // Safety valve: no play lasts forever.
        if (_elapsed > 22f) EndPlay();
    }

    private void DeclareFoul()
    {
        LastEvent = "Foul ball.";
        Phase = PlayPhase.Dead;
        Outcome = new PlayOutcome
        {
            IsFoul = true,
            Description = "Foul ball.",
        };
        Finished = true;
    }

    private void DeclareHomeRun()
    {
        _isHomeRun = true;
        Phase = PlayPhase.Dead;
        LastEvent = "It's outta here!";

        int runs = 1 + Runners.Count(r => !r.IsBatter && !r.IsOut);
        foreach (var r in Runners) { r.Scored = true; r.Held = true; }

        Outcome = new PlayOutcome
        {
            IsHomeRun = true,
            IsHit = true,
            Runs = runs,
            BasesForBatter = 4,
            Description = runs > 1 ? $"{runs}-run home run!" : "Home run!",
        };
        Finished = true;
    }

    private void EndPlayWithHit(int bases)
    {
        Phase = PlayPhase.Dead;
        Outcome = new PlayOutcome
        {
            IsHit = true,
            AwardBases = true,      // the ball is dead; bases are simply awarded
            BasesForBatter = bases,
            Description = LastEvent,
        };
        Finished = true;
    }

    private void EndPlay()
    {
        Phase = PlayPhase.Dead;
        Finished = true;

        var batter = Runners.First(r => r.IsBatter);

        // The official scorer credits the hit for the bag he legitimately reached, even if he
        // is thrown out trying for the next one.
        int reached = batter.MaxBaseReached;
        bool hit = !CaughtInAir && reached >= 1;

        Outcome = new PlayOutcome
        {
            Outs = _outsRecorded,
            Runs = _runsScored,
            IsHit = hit,
            BasesForBatter = hit ? reached : 0,
            Description = BuildDescription(hit, reached, batter.IsOut),
        };
    }

    private string BuildDescription(bool hit, int batterBase, bool batterOut)
    {
        if (hit && batterOut)
            return $"Thrown out stretching it into {(batterBase >= 2 ? "a triple" : "a double")}!";

        if (!hit)
        {
            if (CaughtInAir) return $"{_batted.Descriptor.Capitalize()} out.";
            if (_outsRecorded > 1) return "Double play!";
            if (_outsRecorded == 1) return $"Out on a {_batted.Descriptor}.";
            return "Safe on the play.";
        }

        string label = batterBase switch
        {
            1 => "Base hit",
            2 => "Double!",
            3 => "Triple!",
            _ => "Home run!",
        };
        if (_runsScored > 0) label += $" {_runsScored} run{(_runsScored > 1 ? "s" : "")} in.";
        return label;
    }

    /// <summary>Writes the finished play back into the game state.</summary>
    public void Apply(GameSituation sit)
    {
        var o = Outcome;

        if (o.IsFoul)
        {
            sit.AddStrike(foul: true);
            return;
        }

        // A dead ball just awards bases to everyone — no fielders, no throws.
        if (o.IsHomeRun || o.AwardBases)
        {
            sit.AwardHit(o.BasesForBatter <= 0 ? 4 : o.BasesForBatter);
            return;
        }

        var batter = sit.Batter;
        var pitcher = sit.CurrentPitcher;

        // Read the situation before anybody moves. Once the runners have been picked up, the
        // state no longer says whether this was an at-bat with a man in scoring position.
        var where = sit.Where;
        var credit = CreditFor(sit);

        // Clear the bases and re-seat whoever is still standing on one.
        for (int b = 1; b <= 3; b++) sit.RemoveRunner(b);

        var scorers = new List<PlayerData>();
        foreach (var r in Runners)
        {
            if (r.IsOut) continue;
            if (r.Scored) { scorers.Add(r.Player); continue; }
            int bag = r.Held ? r.ToBase : r.FromBase;
            if (bag >= 1 && bag <= 3) sit.Runners[bag] = r.Player;
        }

        sit.CompleteBattedBall(batter, pitcher, scorers,
            o.IsHit, o.BasesForBatter, o.Outs, _errorCharged, where, credit);
    }

    /// <summary>
    /// What the play was, beyond its result. The scorer's distinctions: a fly ball that scores a
    /// man is a sacrifice and costs the hitter no at-bat, a bunt that moves one along is the
    /// same, and a ground ball that retires two is the one every hitter is charged for.
    ///
    /// All three used to arrive at the book as a plain out, which is why nobody's line ever
    /// showed a sacrifice and why a slow right-handed hitter looked exactly like a fast one.
    /// </summary>
    private PlayCredit CreditFor(GameSituation sit)
    {
        var runner = Runners.FirstOrDefault(r => r.IsBatter);
        if (runner == null || !runner.IsOut || Outcome.IsHit) return PlayCredit.None;

        var credit = PlayCredit.None;
        bool underTwoOut = sit.Outs < 2;

        bool somebodyAdvanced = Runners.Any(r =>
            !r.IsBatter && !r.IsOut && (r.Scored || r.MaxBaseReached > r.StartBase));

        if (CaughtInAir)
        {
            // A sacrifice fly needs a run, not merely an advance.
            if (underTwoOut && Runners.Any(r => !r.IsBatter && r.Scored))
                credit |= PlayCredit.SacrificeFly;
        }
        else if (_batted.WasBunt)
        {
            if (underTwoOut && somebodyAdvanced) credit |= PlayCredit.SacrificeBunt;
        }
        else if (_outsRecorded >= 2 && _batted.LaunchAngle < 10f)
        {
            credit |= PlayCredit.DoublePlay;
        }

        return credit;
    }
}

internal static class StringCaseExtensions
{
    public static string Capitalize(this string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
