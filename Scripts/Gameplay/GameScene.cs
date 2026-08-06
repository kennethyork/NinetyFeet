using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Audio;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.UI;

namespace SandlotSlugfest.Gameplay;

public enum AtBatPhase { Intro, PitchSelect, PitchFlight, InPlay, Result, HalfBreak, Over }

/// <summary>
/// Drives a single ballgame: sets the matchup, runs the at-bat loop, hands the ball in play
/// to the field simulation, and keeps the two views and the scoreboard in sync.
/// </summary>
public partial class GameScene : Node2D
{
    public GameSituation Situation { get; private set; }
    public PlaySimulation Play { get; private set; }
    public Pitch CurrentPitch { get; private set; }
    public AtBatPhase Phase { get; private set; } = AtBatPhase.Intro;

    /// <summary>Where the batter has the bat aimed, in plate-plane feet.</summary>
    public Vector2 BatCursor = new(0f, 2.5f);
    /// <summary>Where the pitcher is aiming, in plate-plane feet.</summary>
    public Vector2 PitchAim = new(0f, 2.5f);
    public PitchType SelectedPitch = PitchType.Fastball;
    private int _pendingPadPitchSlot = -1;

    /// <summary>Seconds a human pitcher has to deliver before the umpire calls for the pitch.</summary>
    /// <summary>
    /// The pitch timer, to the real rule: fifteen seconds with the bases empty, eighteen with a
    /// runner on. It was a flat six that simply threw the pitch for you when it expired — no
    /// violation, no penalty, and less than half the real allowance.
    /// </summary>
    public const float PitchClockEmpty = 15f;
    public const float PitchClockRunners = 18f;

    /// <summary>The allowance for the situation on the bases right now.</summary>
    public float PitchClockSeconds =>
        Situation.RunnerOn(1) || Situation.RunnerOn(2) || Situation.RunnerOn(3)
            ? PitchClockRunners : PitchClockEmpty;

    // Single source of truth for the hitting assists. The batting view draws the coverage
    // indicator from these exact numbers, so what is on screen is what the swing actually uses —
    // they were drifting apart, and an indicator that lies is worse than none at all.
    /// <summary>
    /// The bat both players swing online.
    ///
    /// A swing has to resolve identically on both machines, and the assists are per-machine: the
    /// hitter's side used its own difficulty setting while the pitcher's side, where the hitter is
    /// "not human", resolved the same swing with a bare bat. The guest put a ball in play and the
    /// host called it a strike, from the same pitch and the same swing.
    ///
    /// Online therefore ignores both players' difficulty and gives everyone the same bat. That is
    /// also simply the right answer for a game between two people: neither of them gets a wider
    /// sweet spot because of a menu setting on his own machine.
    /// </summary>
    public const float OnlineBatAssist = 1.46f;
    public const float OnlineTimingAssist = 2.05f;

    /// <summary>
    /// The pitch both players throw online, for exactly the same reason as the bat above — and this
    /// one was worse, because it moved the ball rather than the bat.
    ///
    /// A pitch is built once per machine from the same seeded draws, but the scatter those draws
    /// are multiplied by came from whichever difficulty knob applied on that side. The pitcher's
    /// machine used HumanCommand and the hitter's machine used CpuCommand — 0.30 against 1.00 on
    /// Pro, and further apart still if the two players had picked different difficulties. The
    /// random numbers stayed in step, so nothing looked wrong; the ball simply crossed the plate
    /// three inches apart on the two screens. The same swing at the same instant then produced a
    /// hundred-and-seven off one bat and a hundred-and-forty off the other, and the first ball or
    /// strike that flipped ended any agreement about the game.
    ///
    /// Online both men are human and both are aiming a reticle, so both get the human number.
    /// </summary>
    public const float OnlineCommand = 0.30f;
    public const float OnlinePitchSpeed = 1f;

    public float BatAssist =>
        Online ? OnlineBatAssist : HumanBatting ? Game.Instance.Tuning.BatAssist : 1f;

    public float TimingAssist =>
        Online ? OnlineTimingAssist : HumanBatting ? Game.Instance.Tuning.TimingAssist : 1f;

    /// <summary>The timing window for the current hitter, in seconds either side of perfect.</summary>
    public float TimingWindowSeconds(SwingType type)
    {
        var batter = Situation?.Batter;
        if (batter == null) return 0.1f;

        // Taken from the resolver's own expression rather than a second copy of it. These had
        // drifted apart — the screen drew a window 21% narrower than the one a swing was actually
        // judged against for a weak hitter — and an indicator that disagrees with the rule is the
        // exact thing this game keeps promising not to ship.
        float w = (0.066f + batter.Contact / 10f * 0.022f)
                  * TimingAssist * SwingProfile.For(type).Window;
        if (batter.Special == Data.Special.ContactMaster) w *= 1.4f;
        return w;
    }

    /// <summary>Time left on the pitch clock. Only meaningful while a human is on the mound.</summary>
    public float PitchClock { get; private set; }

    /// <summary>
    /// Whether the hitter is set. Under the pitch-timer rule he must be in the box and alert with
    /// eight seconds left, and a violation is an automatic strike.
    /// </summary>
    public bool BatterSet { get; private set; }

    public const float BatterMustBeSetAt = 8f;

    /// <summary>
    /// Step-offs and pickoff throws used against the current hitter. A pitcher gets two; a third
    /// that fails to retire the runner is a balk.
    /// </summary>
    public int Disengagements { get; private set; }

    public const int DisengagementLimit = 2;

    /// <summary>Set once the hitter moves his stance, which counts as stepping in.</summary>
    private bool _batterMoved;

    /// <summary>True when a pickoff is available: a runner on, and the human on the mound.</summary>
    public bool CanThrowOver =>
        HumanPitching && Phase == AtBatPhase.PitchSelect && !Delivering && LeadRunnerBase > 0;

    /// <summary>The furthest occupied base, which is the one worth throwing to.</summary>
    public int LeadRunnerBase =>
        Situation.RunnerOn(3) ? 3 : Situation.RunnerOn(2) ? 2 : Situation.RunnerOn(1) ? 1 : 0;

    public bool HumanBatting { get; private set; }
    public bool HumanPitching { get; private set; }

    /// <summary>Tickets sold tonight, and the sky. Shown on the broadcast strip.</summary>
    public int Crowd { get; private set; }
    public Season.Conditions Conditions { get; private set; }

    /// <summary>
    /// How loud the park is, as a multiplier on every crowd sound.
    ///
    /// A half-empty park in April and a full one in the ninth inning of a one-run game used to
    /// make exactly the same noise, which meant the crowd carried no information at all. It should
    /// tell you what the situation is worth before the commentary does.
    /// </summary>
    public float CrowdEnergy
    {
        get
        {
            float house = Mathf.Clamp(Crowd / Season.Attendance.Capacity, 0.25f, 1f);

            // Late, and close. A blowout empties a park's voice even when the seats are full.
            int margin = Mathf.Abs(Situation.HomeScore - Situation.AwayScore);
            float late = Mathf.Clamp((Situation.Inning - 5) / 4f, 0f, 1f);
            float tension = margin <= 1 ? 1f : margin <= 3 ? 0.82f : margin <= 6 ? 0.60f : 0.42f;

            return Mathf.Clamp(house * (0.72f + late * 0.5f) * tension, 0.20f, 1.35f);
        }
    }

    /// <summary>Plays a crowd sound at whatever volume tonight's house and situation deserve.</summary>
    private void CrowdSound(Sound sound, float weight) =>
        Sfx.Instance?.Play(sound, Mathf.Clamp(weight * CrowdEnergy, 0.05f, 1f));
    public bool SwingTaken { get; private set; }
    public float SwingFlash { get; private set; }

    /// <summary>How long a swing takes to play out, start of load to end of follow-through.</summary>
    public const float SwingDuration = 0.42f;

    /// <summary>0 at the start of the swing, 1 at the end. Drives the swing animation.</summary>
    /// <summary>
    /// A delivery, from the gather to the end of the follow-through. The ball leaves the hand
    /// partway through — at <see cref="DeliveryLead"/> — so the wind-up genuinely precedes the
    /// pitch instead of playing over a ball that has already gone.
    /// </summary>
    public const float DeliveryDuration = 0.62f;

    /// <summary>How much of the delivery happens before the ball is released.</summary>
    public const float DeliveryLead = 0.42f;

    private float _deliveryElapsed = -1f;

    /// <summary>0 at the gather, 1 at the end of the follow-through.</summary>
    public float DeliveryPhase => _deliveryElapsed < 0f ? 0f
        : Mathf.Clamp(_deliveryElapsed / DeliveryDuration, 0f, 1f);

    /// <summary>True once he has started his motion — the pitch is committed and unaimable.</summary>
    public bool Delivering => _deliveryElapsed >= 0f;

    public float SwingPhase => SwingFlash <= 0f ? 0f
        : Mathf.Clamp(1f - SwingFlash / SwingDuration, 0f, 1f);
    public string BannerText { get; private set; } = "";

    public readonly List<string> Log = new();

    private BattingView _batting;
    private FieldView _field;
    private Hud _hud;

    private float _phaseTimer;
    private float _pitchProgress;
    private float _playAccumulator;
    private bool _resultCameFromPlay;
    private Vector2 _lastMousePos;

    /// <summary>Which device is currently aiming. Latched, so the two do not fight.</summary>
    private enum Aiming { Mouse, Pad }

    private Aiming _aimWith = Aiming.Mouse;

    /// <summary>
    /// Hands the reticle to a touchscreen drag and keeps the dormant mouse from taking it back on
    /// the next frame. Batting and pitching have slightly different legal target areas.
    /// </summary>
    public void SetTouchAim(Vector2 aim)
    {
        _aimWith = Aiming.Pad;

        if (HumanPitching)
        {
            PitchAim = new Vector2(
                Mathf.Clamp(aim.X, -1.9f, 1.9f),
                Mathf.Clamp(aim.Y, 0.8f, 4.6f));
        }
        else
        {
            BatCursor = new Vector2(
                Mathf.Clamp(aim.X, -2.2f, 2.2f),
                Mathf.Clamp(aim.Y, 0.6f, 5.0f));
            _batterMoved = true;
        }
    }

    public void SetTouchFieldTarget(Vector2 screen)
    {
        if (Phase != AtBatPhase.InPlay || Play?.Controlled == null
            || Game.Instance.AutoFielding || Online) return;
        _aimWith = Aiming.Pad;
        Play.ManualTarget = _field.ScreenToField(screen);
    }

    /// <summary>
    /// Decides whether the mouse or the pad is driving the reticle this frame.
    ///
    /// This was "has the mouse moved more than one pixel", which is not a decision, it is a
    /// coin toss: a pad player's cursor was snatched away and teleported by desk vibration, a
    /// trackpad resting a thumb, or a mouse nudged by a cable — mid-pitch, every pitch. The pad
    /// worked perfectly and felt broken, which is the worst way for something to be broken.
    ///
    /// So the device latches. It takes a deliberate movement to hand control to the mouse, and
    /// any real stick deflection to hand it to the pad, and whichever one has it keeps it.
    /// </summary>
    private bool MouseIsAiming(Vector2 mouse, Vector2 stick)
    {
        // Six pixels is a movement; one is a table being leant on.
        if (mouse.DistanceSquaredTo(_lastMousePos) > 36f)
        {
            _lastMousePos = mouse;
            _aimWith = Aiming.Mouse;
        }
        else if (stick.LengthSquared() > 0.04f)
        {
            _aimWith = Aiming.Pad;
        }

        return _aimWith == Aiming.Mouse;
    }

    /// <summary>The aim stick, as a vector. Analog on a pad, full deflection on the keys.</summary>
    private static Vector2 AimStick() => Input.GetVector(
        InputActions.AimLeft, InputActions.AimRight,
        InputActions.AimDown, InputActions.AimUp);
    private string _pendingHalfBanner;

    /// <summary>
    /// Outs as the scoreboard should show them. When a play retires the side the rules engine
    /// has already reset the count to zero, but the player still needs to see the third out.
    /// </summary>
    public int DisplayOuts =>
        _pendingHalfBanner != null || (_justRetiredSide && ToastTimer > 0f) ? 3 : Situation.Outs;

    private bool _justRetiredSide;
    private Rng _rng = new(7);
    private int _playSeed = 1;

    // CPU batter intentions, decided the moment the pitch is released.
    private bool _cpuWillSwing;
    private float _cpuSwingAt;
    private Vector2 _cpuCursor;
    private bool _cpuBunt;

    private readonly Dictionary<PlayerData, int> _pitchCounts = new();

    /// <summary>How many this arm has thrown tonight, for the between-innings card.</summary>
    public int PitchesThrownBy(PlayerData arm) =>
        arm != null && _pitchCounts.TryGetValue(arm, out int n) ? n : 0;

    /// <summary>Signature moves, spent as charges rather than applied passively.</summary>
    public readonly PowerUpLedger PowerUps = new();

    /// <summary>Set when the human has armed their special for the next pitch or swing.</summary>
    public bool PowerUpArmed { get; private set; }

    /// <summary>Arms or disarms the current player's signature move.</summary>
    public void TogglePowerUp()
    {
        var who = HumanBatting ? Situation.Batter : Situation.FieldingTeam.CurrentPitcher;
        if (who == null || !PowerUps.Available(who)) return;

        bool usable = HumanBatting
            ? PowerUpLedger.IsHitting(who.Special)
            : PowerUpLedger.IsPitching(who.Special);
        if (!usable) return;

        PowerUpArmed = !PowerUpArmed;
        Toast(PowerUpArmed
            ? $"{who.ShortName} winds up: {PowerUpLedger.Describe(who.Special)}!"
            : "Stood down.", 1.2f);
    }

    /// <summary>The crowd bed belongs to the ballpark, so it stops when you leave it.</summary>
    public override void _ExitTree()
    {
        Sfx.Instance?.StopCrowd();
        Music.Instance?.Stop();
    }

    public override void _Ready()
    {
        Situation = new GameSituation();
        Play = new PlaySimulation();

        // A ballpark is never silent. The bed sits well under the effects.
        Sfx.Instance?.StartCrowd();
        Music.Instance?.Play(Tune.Ballpark);

        // Each home park has its own announcer.
        Narrator.Instance?.SetHomeTeam(Game.Instance.HomeTeamId);
        Narrator.Instance?.Say(VoiceLine.PlayBall, 3);
        Narrator.Instance?.Colour(ColourLine.CFirstPitch, 0.9f);

        var g = Game.Instance;
        var away = g.AwayRoster;
        var home = g.HomeRoster;

        // Rosters are shared singletons, so reset the parts a game mutates.
        away.LineupSpot = 0;
        home.LineupSpot = 0;
        away.StartGame();
        home.StartGame();
        Visit.Reset();

        // A season names the day's starter before the game is handed over; only fall back to the
        // top of the rotation when nobody has been given the ball.
        if (away.CurrentPitcher == null || away.CurrentPitcher.Role != StaffRole.Starter)
            away.SetPitcher(away.Rotation.FirstOrDefault() ?? away.Pitchers[0]);
        else away.SetPitcher(away.CurrentPitcher);

        if (home.CurrentPitcher == null || home.CurrentPitcher.Role != StaffRole.Starter)
            home.SetPitcher(home.Rotation.FirstOrDefault() ?? home.Pitchers[0]);
        else home.SetPitcher(home.CurrentPitcher);

        AutoPlay = g.AutoPlayNextGame;

        Situation.Announced += OnAnnounced;
        Situation.HalfInningChanged += OnHalfInningChanged;
        Situation.GameEnded += OnGameEnded;

        // Games are played in the home club's park, which decides the wall shape and the air.
        FieldGeometry.SetStadium(Stadiums.For(home.Team));

        // Tonight's conditions and gate. A scheduled season game has both worked out from the
        // date and the standings; an exhibition gets a fair evening and a decent house.
        var scheduled = g.PendingSeasonGame;
        if (scheduled != null && g.League != null)
        {
            Conditions = Season.Weather.For(g.League, scheduled);
            Crowd = scheduled.Crowd > 0 ? scheduled.Crowd : Season.Attendance.For(g.League, scheduled);
            scheduled.Crowd = Crowd;
        }
        else if (g.IsFarmGame)
        {
            // A minor-league park is small and the crowd is a few thousand on a warm evening.
            // Playing a Double-A game to a packed forty-one thousand would be the wrong night out.
            Conditions = new Season.Conditions(Season.Sky.Clear, 76, 0f);
            Crowd = Mathf.RoundToInt(Season.Attendance.Capacity * 0.12f);
        }
        else
        {
            Conditions = new Season.Conditions(Season.Sky.Clear, 74, 0f);
            Crowd = Mathf.RoundToInt(Season.Attendance.Capacity * 0.7f);
        }

        FieldGeometry.SetConditions(Conditions.Wind, Conditions.TemperatureF);

        // Read once, here, rather than every frame: changing how you hit in the middle of an
        // at-bat is not a thing anybody wants and reading a config file per frame is not either.
        HitStyle = Settings.LoadHitting();
        PitchStyle = Settings.LoadPitching();

        // Which month this is, so the at-bats land in the right monthly split. A friendly or a
        // moment has no calendar behind it and stays in the first bucket.
        Situation.Month = Game.Instance.League != null
            ? Season.Calendar.MonthIndex(Game.Instance.League.CurrentDay)
            : 0;

        Situation.Start(away, home, g.Innings);

        // A moment starts somewhere other than the top of the first. Nothing in the rules is
        // special-cased for it — the situation is simply forced, and then played out for real.
        if (g.PendingMoment is { } moment)
        {
            Moments.Apply(moment, Situation);
            _momentRunsAtStart = Moments.MyScore(moment, Situation);
            _momentTheirsAtStart = Moments.TheirScore(moment, Situation);
            _momentOuts = 0;
            _momentHit = false;
        }

        _field = new FieldView { Scene = this, Visible = false };
        AddChild(_field);
        _batting = new BattingView { Scene = this };
        AddChild(_batting);
        _hud = new Hud { Scene = this };
        AddChild(_hud);

        BannerText = $"{away.Team.FullName}\nat\n{home.Team.FullName}";
        SetPhase(AtBatPhase.Intro, 1.4f);
        RefreshControlFlags();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // A tap is a press and a release. Held down for ever, one touch of the swing button is a
        // bat that never comes back — so last frame's presses are let go before this frame reads
        // anything.
        TouchControls.Release();

        if (Input.IsActionJustPressed(InputActions.Pause)) OpenPauseMenu();

        // Decisions from the other machine are acted on before anything else this frame, so both
        // sides run the same tick against the same state.
        PumpNetwork();
        RunAutoPlayer();

        _phaseTimer -= dt;
        if (SwingFlash > 0f) SwingFlash -= dt;
        if (ChallengeWindow > 0f) ChallengeWindow -= dt;
        if (Situation.Inning > Game.Instance.Innings) Challenges.EnterExtraInnings();
        if (VerdictTimer > 0f) VerdictTimer -= dt;
        if (SwingFeedbackTimer > 0f) SwingFeedbackTimer -= dt;
        if (ToastTimer > 0f) ToastTimer -= dt;
        Pen.Update(dt);

        switch (Phase)
        {
            case AtBatPhase.Intro:
                if (_phaseTimer <= 0f) BeginPitchSelect();
                break;
            case AtBatPhase.PitchSelect:
                UpdatePitchSelect(dt);
                break;
            case AtBatPhase.PitchFlight:
                UpdatePitchFlight(dt);
                break;
            case AtBatPhase.InPlay:
                UpdateInPlay(dt);
                break;
            case AtBatPhase.Result:
                // A replay waits for the live call to finish, then rolls before play resumes.
                if (Replay.Running)
                {
                    // Just-pressed, not held. IsAnythingPressed is true while any key is down, so
                    // a finger still resting on the swing button would cancel every replay the
                    // instant it started.
                    if (Input.IsActionJustPressed(InputActions.Action) ||
                        Input.IsActionJustPressed(InputActions.Back)) Replay.Stop();
                    else Replay.Update(dt);
                    break;
                }

                if (_phaseTimer <= 0f && _replayPending != null)
                {
                    Replay.Start(_replayPending);
                    _replayPending = null;
                    if (Replay.Running) break;
                }

                if (_phaseTimer <= 0f) AfterResult();
                break;
            case AtBatPhase.HalfBreak:
                if (_phaseTimer <= 0f) BeginPitchSelect();
                break;
            case AtBatPhase.Over:
                if (Input.IsActionJustPressed(InputActions.Action)) LeaveGame();
                break;
        }

        // The field view is only meaningful while a ball is actually in play. Showing it for a
        // swinging strike or a called ball cuts to an empty diamond with nobody on it.
        // A trip to the mound is the one thing worth cutting to the diamond for that is not a ball
        // in play — the whole point of it is watching the manager walk out.
        bool showField = Phase == AtBatPhase.InPlay || Visit.Busy || Replay.Running ||
                         (Phase == AtBatPhase.Result && _resultCameFromPlay);
        _field.Visible = showField;
        _batting.Visible = !showField;

        _batting.QueueRedraw();
        _field.QueueRedraw();
        _hud.QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // The pad first. It posts real action events, so everything below and every
        // Input.IsActionJustPressed in this file reads a thumb exactly as it reads a key.
        if (TouchControls.Handle(@event, this, GetViewportRect().Size)) return;

        // On a controller the face buttons name the four pitches. A is also the generic Action
        // button, so letting the InputMap handle it would reselect pitch one immediately before
        // every delivery. Consume the raw face button here: one press selects its pitch and a
        // second press on the selected pitch deals it, exactly like the clickable picker.
        if (HumanPitching && Phase == AtBatPhase.PitchSelect && !Delivering
            && @event is InputEventJoypadButton { Pressed: true } pad)
        {
            _pendingPadPitchSlot = pad.ButtonIndex switch
            {
                JoyButton.A => 0,
                JoyButton.B => 1,
                JoyButton.X => 2,
                JoyButton.Y => 3,
                _ => -1,
            };
            if (_pendingPadPitchSlot >= 0) return;
        }

        // Tap the helmet: challenge the call that just went against you.
        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.R })
        {
            ChallengeCall();
            return;
        }

        if (@event is InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape })
        {
            // Escape used to abandon the game. It now pauses, so the front office is reachable
            // without throwing away whatever inning you were in.
            OpenPauseMenu();
            return;
        }

        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
            return;

        // HUD buttons take precedence over anything behind them.
        if (_hud.Clicks.Click(click.Position)) { GetViewport().SetInputAsHandled(); return; }

        // A click on the field over a base is a throw to that base.
        if (Phase == AtBatPhase.InPlay && HumanPitching && Play.Phase == PlayPhase.Held)
        {
            int bag = _field.BaseUnder(click.Position);
            if (bag >= 0)
            {
                Play.StartThrow(bag);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        // Anywhere else, a click on the results screen moves things along.
        if (Phase == AtBatPhase.Over) LeaveGame();
    }

    /// <summary>Back to wherever this game was started from — the season hub, or the menu.</summary>
    /// <summary>Freezes play and puts the pause menu over the top.</summary>
    /// <summary>
    /// Taps the helmet and asks for the call to be reviewed. The automated system rules on where
    /// the ball actually crossed; a successful challenge is handed straight back to the club.
    /// </summary>
    public void ChallengeCall()
    {
        if (!CanChallenge) return;

        bool awayClub = HumanBatting ? Situation.TopHalf : !Situation.TopHalf;
        bool truth = CurrentPitch.IsStrike;
        bool wrong = truth != LastCallWasStrike;

        Challenges.Spend(awayClub, upheld: wrong);
        ChallengeWindow = 0f;
        VerdictTimer = 2.6f;

        if (!wrong)
        {
            ChallengeVerdict = $"Call stands — {(LastCallWasStrike ? "strike" : "ball")}. " +
                               $"{Challenges.Remaining(awayClub)} challenge(s) left.";
            Sfx.Instance?.Play(Sound.UiBack, 0.7f);
            Toast(ChallengeVerdict, 2.4f);
            ApplyCall(LastCallWasStrike);
            return;
        }

        ChallengeVerdict = $"Overturned — it was a {(truth ? "strike" : "ball")}.";
        Sfx.Instance?.Play(Sound.UiSelect, 0.8f);
        CrowdSound(Sound.CrowdCheer, 0.5f);
        Toast(ChallengeVerdict, 2.4f);

        LastCallWasStrike = truth;
        ApplyCall(truth);
    }

    private void OpenPauseMenu()
    {
        if (_paused != null && IsInstanceValid(_paused)) return;

        _paused = new UI.PauseMenu { OnQuit = LeaveGame };
        AddChild(_paused);
    }

    private UI.PauseMenu _paused;

    private void LeaveGame()
    {
        var g = Game.Instance;
        g.TutorialMode = false;
        bool fromSeason = g.PendingSeasonGame != null;
        g.PendingSeasonGame = null;

        if (g.ReturnTo is { } back && back != "")
        {
            g.ReturnTo = null;
            g.GoTo(back);
            return;
        }

        g.GoTo(fromSeason ? "res://Scenes/Season.tscn" : "res://Scenes/MainMenu.tscn");
    }

    /// <summary>Lets the HUD deal a pitch from a mouse click.</summary>
    public void DealNow()
    {
        if (Phase == AtBatPhase.PitchSelect && HumanPitching) CommitPitch();
    }

    private void SetPhase(AtBatPhase phase, float duration = 0f)
    {
        // A fresh pitch starts from the set position, not mid-follow-through.
        if (phase == AtBatPhase.PitchSelect) _deliveryElapsed = -1f;

        Phase = phase;
        _phaseTimer = duration;
    }

    /// <summary>True while this game is being played against another machine.</summary>
    public bool Online => Net.NetLink.I is { State: Net.LinkState.Playing };

    private void RefreshControlFlags()
    {
        var g = Game.Instance;

        if (Online)
        {
            // Online there is no computer on either side. This machine runs one club: the guest
            // takes the visitors and bats in the top half, the host takes the home side.
            bool mineIsAway = Net.NetLink.I.LocalIsAway;
            HumanBatting = Situation.TopHalf == mineIsAway;
            HumanPitching = !HumanBatting;
            return;
        }

        HumanBatting = g.HumanBats(Situation.TopHalf);
        HumanPitching = g.HumanFields(Situation.TopHalf);
    }

    // -----------------------------------------------------------------------
    // Choosing and throwing the pitch
    // -----------------------------------------------------------------------

    private void BeginPitchSelect()
    {
        if (Situation.IsOver) { SetPhase(AtBatPhase.Over); return; }

        RefreshControlFlags();

        // Offered once per hitter, not once per pitch — the analyst fills the gap between
        // batters, which is where a real booth talks.
        if (Situation.Balls == 0 && Situation.Strikes == 0) OfferColourForSituation();

        SwingTaken = false;
        _justRetiredSide = false;
        _pitchProgress = 0f;
        CurrentPitch = null;
        BannerText = "";
        // The bat cursor deliberately stays where the hitter left it. Resetting it every pitch
        // meant re-aiming from the middle of the zone inside the pitch's flight time, which is
        // not enough time to both find the ball and swing at it.

        BatterSet = !HumanBatting;      // only a human has to get himself ready
        _batterMoved = false;
        if (Situation.Balls == 0 && Situation.Strikes == 0) Disengagements = 0;

        if (Online)
        {
            // Two humans, so neither side is ever handed to the computer. Whichever machine is
            // pitching offers its decision; the other simply waits for it to come back stamped.
            _pitchSent = false;
            _takeSent = false;
            Net.NetLink.I.ClearRemoteSwing();

            // The hitter counts as set the moment the half starts. His timer exists to stop a
            // human dawdling against a computer that is ready instantly; online the pitch cannot
            // come until the other player throws it, so running the clock here only rang the
            // batter up for strike after strike while he waited for a pitch to be sent.
            BatterSet = true;
            PitchClock = PitchClockSeconds;

            SetPhase(AtBatPhase.PitchSelect);
            return;
        }

        if (!HumanPitching)
        {
            ChooseCpuPitch();

            // Facing a computer pitcher, the clock is the hitter's: he has to be set with eight
            // seconds left or it is a strike. Being set is what lets the pitch come.
            PitchClock = HumanBatting ? PitchClockSeconds : 0f;
            SetPhase(AtBatPhase.PitchSelect, HumanBatting ? 0f : 0.35f);
        }
        else
        {
            // The clock runs whether or not you act, and letting it expire is a violation.
            PitchClock = PitchClockSeconds;
            SetPhase(AtBatPhase.PitchSelect);
        }
    }

    /// <summary>
    /// Separate from the game's RNG on purpose. Varying a sound's pitch is cosmetic, and drawing
    /// those numbers from the simulation stream would shift every pitch and CPU decision after it.
    /// </summary>
    private Rng _audioRng = new(0x5A17D0);

    /// <summary>
    /// The decisions a manager makes between pitches.
    ///
    /// A human had none of these. The computer stole a base and a half a game against him with no
    /// answer available, could not be forced to face a different hitter, and never had to be taken
    /// out. Managing is half of baseball and it was entirely one-sided.
    /// </summary>
    private void HandleManagerInput()
    {
        if (Delivering || Situation.IsOver) return;

        // Every one of these either consumes a random number or moves a player, so online they
        // are offered to the host for sequencing and acted on when the stamped order comes back.
        // Acting locally and telling the other machine afterwards would let two decisions made in
        // the same instant land in a different order on each side, and from there the two games
        // are quietly playing different baseball.
        void Do(Net.NetVerb verb, System.Action apply, float a = 0f)
        {
            if (Online) Net.NetLink.I.Send(verb, a);
            else apply();
        }

        // --- Batting: send the runner, or send up a bat. ---
        if (HumanBatting)
        {
            if (Input.IsActionJustPressed(InputActions.Steal) &&
                Baserunning.LeadRunner(Situation) > 0)
            {
                Do(Net.NetVerb.Steal, ApplyNetSteal);
                return;
            }

            // Only before the first pitch of an at-bat, the way a real substitution works.
            if (Input.IsActionJustPressed(InputActions.PinchHit) &&
                Situation.Balls == 0 && Situation.Strikes == 0)
                Do(Net.NetVerb.PinchHit, ApplyNetPinchHit);
        }

        // --- Pitching: go to the pen, or put him on. ---
        if (!HumanPitching) return;

        if (Input.IsActionJustPressed(InputActions.ChangePitcher))
            Do(Net.NetVerb.ChangePitcher, ApplyNetPitchingChange);

        if (Input.IsActionJustPressed(InputActions.MoundVisit))
            Do(Net.NetVerb.MoundVisit, ApplyNetMoundVisit);

        if (Input.IsActionJustPressed(InputActions.IntentionalWalk))
            Do(Net.NetVerb.IntentionalWalk, ApplyNetIntentionalWalk);

        // --- Getting somebody up. ---
        //
        // Offline only, for the same reason as the alignment: how warm a reliever is changes the
        // pitches he throws, so both machines have to agree on it or they diverge the moment he
        // comes in. Online, a change is still instant.
        if (!Online && Input.IsActionJustPressed(InputActions.WarmUp))
        {
            var next = NextArmToWarm();
            if (next == null) Toast("Nobody left in the pen.", 1.6f);
            else
            {
                Pen.StartWarming(next);
                Toast($"{next.Name} ({PlayerData.RoleLabel(next.Role)}) is getting up.", 2f);
                AddLog($"{next.Name} starts to warm.");
            }
        }

        // --- Where the defence stands. ---
        //
        // Offline only, and deliberately. The alignment decides where nine men are standing when
        // the ball is struck, so the two machines would resolve the same batted ball differently
        // the instant one of them moved somebody — this is exactly the kind of per-machine
        // decision that caused the last desync. Online, both sides read the same alignment out of
        // the shared situation. Giving this to a networked manager means a verb of its own,
        // ordered by the host like every other command.
        if (!Online && Input.IsActionJustPressed(InputActions.SetDefence))
        {
            Situation.Defence = NextAlignment(Situation.Defence);
            Toast($"Defence: {Positioning.Label(Situation.Defence)} — " +
                  $"{Positioning.Why(Situation.Defence)}", 2.4f);
        }
    }

    /// <summary>
    /// The next man to get up: whoever the bench would reach for, and pressing it again walks on
    /// down the pen rather than naming the same arm twice.
    /// </summary>
    private PlayerData NextArmToWarm()
    {
        var side = Situation.FieldingTeam;
        var available = side.Bullpen
            .Where(p => p != side.CurrentPitcher && !side.UsedArms.Contains(p) && !p.IsInjured)
            .ToList();

        if (available.Count == 0) return null;
        if (Pen.Warming == null) return side.BestAvailableReliever() ?? available[0];

        int at = available.IndexOf(Pen.Warming);
        return available[(at + 1) % available.Count];
    }

    /// <summary>Cycles round the alignments in the order a bench would run through them.</summary>
    private static Alignment NextAlignment(Alignment from) => from switch
    {
        Alignment.Straight => Alignment.DoublePlay,
        Alignment.DoublePlay => Alignment.InfieldIn,
        Alignment.InfieldIn => Alignment.NoDoubles,
        Alignment.NoDoubles => Alignment.Shift,
        _ => Alignment.Straight,
    };

    private void ApplyNetSteal()
    {
        int from = Baserunning.LeadRunner(Situation);
        if (from == 0) return;

        var runner = Situation.Runners[from];
        var attempt = Baserunning.TryStealBeforePitch(Situation, ref _rng, forced: true);
        if (!attempt.Attempted) return;

        Sfx.Instance?.Play(attempt.Safe ? Sound.CrowdCheer : Sound.GloveCatch, 0.6f);
        ShowResult(attempt.Safe
            ? $"{runner.ShortName} steals {BaseName(attempt.FromBase + 1)}!"
            : $"{runner.ShortName} is thrown out at {BaseName(attempt.FromBase + 1)}.");
    }

    private void ApplyNetPinchHit()
    {
        var bench = Situation.BattingTeam.Bench
            .Where(p => !p.IsInjured && p != Situation.Batter)
            .OrderByDescending(p => p.Contact * 1.2f + p.Power)
            .FirstOrDefault();

        if (bench == null) { Toast("Nobody left on the bench.", 1.4f); return; }

        var outgoing = Situation.Batter;
        if (!Situation.PinchHit(bench)) return;

        Toast($"Pinch hitter: {bench.ShortName} bats for {outgoing.ShortName}.", 2f);
        AddLog($"{Situation.BattingTeam.Team.Abbrev} send {bench.Name} up for {outgoing.Name}.");
        Narrator.Instance?.SayName(bench);
    }

    private void ApplyNetPitchingChange()
    {
        if (Visit.Busy) return;

        var team = Situation.FieldingTeam;
        var reliever = team.NextArm(CpuBrain.RoleFor(Situation), CpuBrain.IsSaveSituation(Situation),
            Situation.Batter);
        if (reliever == null) { Toast("The bullpen is empty.", 1.4f); return; }

        // The change is not applied here any more — the manager has to walk out and take the ball
        // first, and UpdateVisit makes the substitution when he reaches the mound. What you watch
        // and what happens are then the same event rather than a number changing between frames.
        Visit.Begin(Situation.FieldingTeam == Situation.Away, change: true,
            team.CurrentPitcher, reliever);
    }

    /// <summary>
    /// A trip out to settle the pitcher rather than to take him out.
    ///
    /// Five a game. The sixth is not allowed, so it becomes a change — which is the real rule and
    /// the reason a manager thinks twice in the fourth about a trip he might want in the eighth.
    /// </summary>
    private void ApplyNetMoundVisit()
    {
        if (Visit.Busy || Situation.FieldingTeam.CurrentPitcher == null) return;

        bool away = Situation.FieldingTeam == Situation.Away;
        if (Visit.MustChange(away))
        {
            Toast("No visits left — that has to be a change.", 1.8f);
            ApplyNetPitchingChange();
            return;
        }

        Visit.Begin(away, change: false, Situation.FieldingTeam.CurrentPitcher, null);
    }

    private void ApplyNetIntentionalWalk()
    {
        var walked = Situation.Batter;
        Situation.AwardWalk();
        Sfx.Instance?.Play(Sound.Walk, 0.6f);
        AddLog($"{walked.Name} is walked intentionally.");
        ShowResult($"{walked.ShortName} is put on intentionally.");
    }

    /// <summary>The manager's trip to the mound, if he is out there.</summary>
    public readonly MoundVisit Visit = new();

    /// <summary>
    /// The pen. A reliever used to appear at his best the instant you named him, which removed
    /// the only decision the bullpen is really about: committing to somebody an inning early.
    /// </summary>
    public readonly Bullpen Pen = new();

    /// <summary>
    /// Runs the manager out to the mound and holds the game while he is there.
    ///
    /// Nothing may be pitched, aimed or decided during a visit — that is the point of it. The
    /// change itself is applied when he reaches the mound rather than when the button was pressed,
    /// so what you see and what happens are the same event.
    /// </summary>
    private bool UpdateVisit(float dt)
    {
        if (!Visit.Busy) return false;

        bool wasTalking = Visit.Stage == VisitStage.Talking;
        bool finished = Visit.Update(dt);

        // He arrives, and the ball changes hands.
        if (!wasTalking && Visit.Stage == VisitStage.Talking)
        {
            if (Visit.IsChange && Visit.Incoming != null)
            {
                Pen.BringIn(Visit.Incoming);
                Situation.ChangePitcher(Visit.Incoming);
                Toast($"{Visit.Incoming.ShortName} " +
                      $"({PlayerData.RoleLabel(Visit.Incoming.Role)}) comes in.", 2f);
                AddLog($"Pitching change: {Visit.Incoming.Name} replaces {Visit.Outgoing?.Name}.");
                CrowdSound(Sound.CrowdCheer, 0.35f);
            }
            else
            {
                // A word settles him. Worth about a hitter of steadier command, which is what a
                // visit is actually for and why five of them is a resource rather than a formality.
                var arm = Situation.FieldingTeam.CurrentPitcher;
                if (arm != null)
                {
                    arm.RecentPitches = Mathf.Max(0, arm.RecentPitches - 8);
                    _pitchCounts.TryGetValue(arm, out int thrown);
                    _pitchCounts[arm] = Mathf.Max(0, thrown - 6);
                }
                AddLog($"Mound visit for {Situation.FieldingTeam.Team.Abbrev}.");
            }
        }

        if (Visit.Caption != "") BannerText = Visit.Caption;
        if (finished) BannerText = "";
        return true;
    }

    private void UpdatePitchSelect(float dt)
    {
        // The manager is out there; nothing happens until he walks back.
        if (UpdateVisit(dt)) return;

        HandleManagerInput();

        // Once he has started his motion the pitch is locked in: no more aiming, and the ball
        // leaves his hand at the whip rather than the instant the button was pressed.
        if (Delivering)
        {
            _deliveryElapsed += dt;
            if (HumanBatting) MoveBatCursor(dt);
            if (_deliveryElapsed >= DeliveryLead) ReleasePitch();
            return;
        }

        // A hitter can set his stance before the pitch is released, rather than having to find
        // the zone from scratch during the flight.
        if (HumanBatting) MoveBatCursor(dt);

        // Facing a computer pitcher: the hitter's own clock.
        if (HumanBatting && !HumanPitching)
        {
            PitchClock -= dt;

            // Any sign of life counts as stepping in and being alert.
            if (!BatterSet && (Input.IsActionJustPressed(InputActions.Action) || _batterMoved))
            {
                BatterSet = true;
                _phaseTimer = 0.45f;      // the pitcher gets on with it
            }

            if (!BatterSet && PitchClock <= BatterMustBeSetAt) { BatterTimerViolation(); return; }
            if (!BatterSet) return;       // nothing happens until he is ready
        }

        if (HumanPitching)
        {
            // Mouse or pad, whichever has the latch.
            Vector2 mouse = _batting.GetGlobalMousePosition();
            Vector2 stick = AimStick();

            if (MouseIsAiming(mouse, stick)) PitchAim = _batting.ScreenToPlate(mouse);
            else PitchAim += stick * 3.4f * dt;
            PitchAim.X = Mathf.Clamp(PitchAim.X, -1.9f, 1.9f);
            PitchAim.Y = Mathf.Clamp(PitchAim.Y, 0.8f, 4.6f);

            // The number keys index this arm's own repertoire, matching the picker on screen.
            // They used to be hardwired to the first four pitch types, so a pitcher whose second
            // pitch was a sinker had no key for it at all.
            var arm = Situation.FieldingTeam.CurrentPitcher;
            var arsenal = arm.Arsenal.ToArray();

            if (_pendingPadPitchSlot >= 0)
            {
                int slot = _pendingPadPitchSlot;
                _pendingPadPitchSlot = -1;
                if (slot < arsenal.Length)
                {
                    if (SelectedPitch == arsenal[slot]) CommitPitch();
                    else SelectedPitch = arsenal[slot];
                }
                return;
            }

            void Choose(int slot)
            {
                if (slot < arsenal.Length) SelectedPitch = arsenal[slot];
            }

            if (Input.IsActionJustPressed(InputActions.Pitch1)) Choose(0);
            if (Input.IsActionJustPressed(InputActions.Pitch2)) Choose(1);
            if (Input.IsActionJustPressed(InputActions.Pitch3)) Choose(2);
            if (Input.IsActionJustPressed(InputActions.Pitch4)) Choose(3);
            if (Input.IsActionJustPressed(InputActions.PowerUp)) TogglePowerUp();

            PitchClock -= dt;

            // --- The meter, when that is how this pitcher is being asked to throw. ---
            //
            // Offline only, and for the same reason as the defence and the pen: the meter decides
            // how much command the pitch has, so two machines running it separately would resolve
            // the same pitch differently. Sending it means a payload of its own.
            if (PitchStyle == PitchingStyle.Meter && !Online)
            {
                UpdatePitchMeter(dt);
                if (_meterStage != 0) return;
            }

            if (Input.IsActionJustPressed(InputActions.Action)) { CommitPitch(); return; }

            // Time up: a pitch-timer violation by the pitcher is an automatic ball. Online it is
            // an award like any other and has to be agreed, or one machine gives up a ball the
            // other never saw.
            if (PitchClock <= 0f)
            {
                if (!Online) PitchTimerViolation();
                else if (!_pitchSent)
                {
                    _pitchSent = true;                  // stops it firing every frame
                    Net.NetLink.I.Send(Net.NetVerb.PitchClock);
                }
            }
            return;
        }

        if (_phaseTimer <= 0f) CommitPitch();
    }

    /// <summary>
    /// The pitcher let the timer run out. Under the pitch-timer rule that is an automatic ball,
    /// not a free pitch — the old clock simply delivered for you, which meant the timer had no
    /// teeth at all.
    /// </summary>
    private void PitchTimerViolation()
    {
        PitchClock = 0f;
        Sfx.Instance?.Play(Sound.Walk, 0.5f);

        if (Situation.AddBall())
        {
            CrowdSound(Sound.CrowdCheer, 0.4f);
            Narrator.Instance?.Say(VoiceLine.Walk, 3);
        }

        ShowResult("Pitch timer violation — ball.");
    }

    /// <summary>
    /// The hitter was not set with eight seconds left. Under the pitch-timer rule that is an
    /// automatic strike against him.
    /// </summary>
    private void BatterTimerViolation()
    {
        PitchClock = 0f;
        BatterSet = true;                // the violation resolves the pitch
        Sfx.Instance?.Play(Sound.Out, 0.5f);

        if (Situation.AddStrike(foul: false))
        {
            Narrator.Instance?.Say(VoiceLine.Strikeout, 3);
            Narrator.Instance?.SayName(Situation.Batter);
        }
        ShowResult("Batter timer violation — strike.");
    }

    /// <summary>
    /// A throw over, or a step off the rubber. A pitcher gets two against a hitter; a third that
    /// does not retire the runner is a balk and every runner moves up a base.
    /// </summary>
    public void ThrowOver(int baseIndex)
    {
        if (!CanThrowOver || !Situation.RunnerOn(baseIndex)) return;

        Disengagements++;
        Sfx.Instance?.Play(Sound.GloveCatch, 0.7f);

        // A pickoff is a long shot, and a quick runner makes it longer.
        var runner = Situation.Runners[baseIndex];
        float chance = Mathf.Clamp(0.09f - runner.Speed / 10f * 0.05f, 0.015f, 0.09f);
        bool caught = _rng.Chance(chance);

        if (caught)
        {
            Situation.RetireRunner(baseIndex);
            CrowdSound(Sound.CrowdCheer, 0.5f);
            ShowResult($"Picked off at {BaseName(baseIndex)}!");
            Disengagements = 0;          // retiring the runner resets the allowance
            return;
        }

        if (Disengagements > DisengagementLimit)
        {
            int runs = Situation.AwardBalk();
            Sfx.Instance?.Play(Sound.Walk, 0.6f);
            ShowResult(runs > 0
                ? $"Balk — third disengagement. A run scores."
                : "Balk — third disengagement. Runners advance.");
            Disengagements = 0;
            return;
        }

        Toast($"Throw to {BaseName(baseIndex)} — safe. " +
              $"{DisengagementLimit - Disengagements + 1} disengagement(s) left.", 1.6f);
    }

    private static string BaseName(int i) => i switch { 1 => "first", 2 => "second", _ => "third" };

    private void ChooseCpuPitch()
    {
        var pitcher = Situation.FieldingTeam.CurrentPitcher;
        CpuBrain.ChoosePitch(Situation, pitcher, ref _rng, out var type, out var aim);
        SelectedPitch = type;
        PitchAim = aim;
    }

    /// <summary>Starts the wind-up. The ball is released later, from <see cref="UpdatePitchSelect"/>.</summary>
    private void CommitPitch()
    {
        if (Delivering) return;

        // Online, the decision goes to the host to be put in order and comes back stamped. The
        // delivery starts then, on both machines at once, rather than here on one of them.
        if (Online)
        {
            if (!HumanPitching) return;               // not this machine's pitch to throw
            if (_pitchSent) return;
            _pitchSent = true;
            Net.NetLink.I.Send(Net.NetVerb.Pitch,
                (int)SelectedPitch, PitchAim.X, PitchAim.Y, PowerUpArmed ? 1f : 0f);
            return;
        }

        _deliveryElapsed = 0f;
    }

    /// <summary>Set once this machine has offered its pitch, so the button cannot be spammed.</summary>
    private bool _pitchSent;

    /// <summary>Set once this machine has told the other that its hitter took the pitch.</summary>
    private bool _takeSent;

    /// <summary>
    /// Plays this machine's side automatically. Used to test an online match end to end: two
    /// instances connect, both play themselves, and their fingerprints are compared every half
    /// inning. Netcode that has not been run against another process is netcode that does not work.
    /// </summary>
    public bool AutoPlay;

    /// <summary>
    /// The stand-in player's own randomness, deliberately kept out of the simulation's stream.
    /// Drawing his decisions from <c>_rng</c> would advance it on one machine and not the other,
    /// which is the exact desync this whole design exists to avoid.
    /// </summary>
    private Rng _botRng = new(0xB07);

    private void RunAutoPlayer()
    {
        if (!AutoPlay || Situation.IsOver) return;

        if (Phase == AtBatPhase.PitchSelect && HumanPitching && !Delivering && !_pitchSent)
        {
            // Online, the stand-in manages his staff over the wire like a person would. Without
            // this the self-test never issued a pitching change, and the desync that lived in that
            // path survived every clean run the harness produced.
            if (Online)
            {
                var arm = Situation.FieldingTeam.CurrentPitcher;
                _pitchCounts.TryGetValue(arm, out int thrown);
                if (thrown >= CpuBrain.PitchLimit(arm))
                {
                    Net.NetLink.I.Send(Net.NetVerb.ChangePitcher);
                    return;
                }
            }

            CpuBrain.ChoosePitch(Situation, Situation.FieldingTeam.CurrentPitcher, ref _botRng,
                out var type, out var aim);
            if (Situation.FieldingTeam.CurrentPitcher.Knows((int)type)) SelectedPitch = type;
            PitchAim = aim;
            CommitPitch();
            return;
        }

        if (Phase == AtBatPhase.PitchFlight && HumanBatting && !SwingTaken && CurrentPitch != null)
        {
            _autoPlan ??= CpuBrain.PlanSwing(Situation, Situation.Batter, CurrentPitch, ref _botRng);
            var plan = _autoPlan.Value;
            if (plan.WillSwing && _pitchProgress >= plan.SwingAt)
            {
                _autoPlan = null;
                TakeSwing(plan.Bunt, plan.Cursor, plan.SwingAt, plan.Type);
            }
            return;
        }

        if (Phase != AtBatPhase.PitchFlight) _autoPlan = null;
    }

    private SwingPlan? _autoPlan;

    /// <summary>One line describing where the online loop has got to, for the self-test.</summary>
    public string NetDebug =>
        $"{Phase} bat={HumanBatting} pitch={HumanPitching} sent={_pitchSent} take={_takeSent} " +
        $"delivering={Delivering} prog={_pitchProgress:F2} " +
        (Online ? Net.NetLink.I.Traffic : "offline");

    /// <summary>
    /// Plays out the swing the other player took, at the moment he took it.
    ///
    /// His decision arrives while the ball is still in the air — he makes it a few hundred
    /// milliseconds before it matters — so by the time the bat has to move, the message is
    /// almost always already here. That is the whole reason a swing is the one thing that does
    /// not go through the host: a round trip inside the flight time is free, and a round trip
    /// between the button and the bat is not.
    /// </summary>
    private void PlayRemoteSwing()
    {
        if (Net.NetLink.I.RemoteSwing is not { } order) return;
        if (_pitchProgress < order.AtProgress) return;

        // The hitter sends whether he spent his signature move, and this threw it away — so a
        // powered swing got a doubled bat on his machine and a bare one here. It has not bitten
        // yet only because signature moves are held off in an online game; it would have the
        // moment they were switched on.
        PowerUpArmed = order.Powered;

        TakeSwing(order.Bunt, order.Cursor, order.AtProgress, (SwingType)order.SwingType);
    }

    /// <summary>
    /// Starts the delivery from a decision that has come back through the host, and gives the
    /// pitch its own stream of random numbers.
    ///
    /// This reseeding is what makes an online game reproducible. Some of the draws in a pitch are
    /// made on an animation timer rather than the instant the decision is applied — the ball is
    /// built partway through the wind-up — so on one machine a pitch could be built before the
    /// previous take was booked and on the other after it. Same decisions, same order, different
    /// random numbers, and from one flipped ball-strike call the two games never agreed again.
    ///
    /// Handing every pitch its own stream, keyed to its place in the agreed order, removes the
    /// whole class of problem: it no longer matters when within the pitch a draw is taken, only
    /// that both machines take the same ones in the same order, which they do.
    /// </summary>
    private void ApplyNetPitch(int sequence, PitchType type, Vector2 aim, bool powered)
    {
        SelectedPitch = type;
        PitchAim = aim;
        _deliveryElapsed = 0f;

        // One flag serves both the pitcher's signature move and the hitter's, which offline is
        // fine because only one of them is ever a human. Online both are, and the pitcher's flag
        // was arriving here and arming the hitter's bat on one machine but not the other. Signature
        // moves are off in an online game until they each have their own flag.
        PowerUpArmed = false;

        unchecked
        {
            _rng = new Rng(Net.NetLink.I.MatchSeed * 7919 + sequence * 2654435761u.GetHashCode());
            _playSeed = Net.NetLink.I.MatchSeed * 31 + sequence * 104729;
        }

        Net.NetLink.I.ClearRemoteSwing();
    }

    /// <summary>
    /// Acts on decisions the host has put in order. Both machines walk the same list in the same
    /// order, which is what keeps two simultaneous decisions from being applied one way here and
    /// the other way there.
    /// </summary>
    private void PumpNetwork()
    {
        if (!Online) return;

        while (Net.NetLink.I.Peek() is { } waiting)
        {
            // A pitch waits until this machine is actually standing on the mound ready to throw
            // it. The two sides finish a ball in play at slightly different moments, so the next
            // pitch could arrive here while the last one was still being run out — it armed the
            // delivery, and then the new at-bat wiped it. The pitch was consumed and never thrown,
            // and both machines sat waiting for each other for the rest of the game.
            if (waiting.Verb == Net.NetVerb.Pitch &&
                (Phase != AtBatPhase.PitchSelect || Delivering))
                break;

            var command = Net.NetLink.I.Next()!.Value;

            if (AutoPlay)
                GD.Print($"[cmd] {command}  before: {Situation.CountText} " +
                         $"{Situation.Outs}out {Phase} swung={SwingTaken}");

            switch (command.Verb)
            {
                case Net.NetVerb.Pitch:
                    ApplyNetPitch(command.Sequence, (PitchType)(int)command.A,
                        new Vector2(command.B, command.C), command.D > 0.5f);
                    break;

                case Net.NetVerb.Steal:
                    ApplyNetSteal();
                    break;

                case Net.NetVerb.ThrowOver:
                    ThrowOver((int)command.A);
                    break;

                case Net.NetVerb.ChangePitcher:
                    ApplyNetPitchingChange();
                    break;

                case Net.NetVerb.MoundVisit:
                    ApplyNetMoundVisit();
                    break;

                case Net.NetVerb.IntentionalWalk:
                    ApplyNetIntentionalWalk();
                    break;

                case Net.NetVerb.PinchHit:
                    ApplyNetPinchHit();
                    break;

                case Net.NetVerb.Take:
                    if (!SwingTaken) TakePitch();
                    break;

                case Net.NetVerb.PitchClock:
                    PitchTimerViolation();
                    break;
            }
        }
    }

    // -----------------------------------------------------------------------
    // The pitching meter
    // -----------------------------------------------------------------------

    /// <summary>Which way this pitcher is being asked to throw. Read once a game.</summary>
    public PitchingStyle PitchStyle { get; private set; } = PitchingStyle.Classic;

    /// <summary>0 not started, 1 climbing for power, 2 coming back down for command.</summary>
    public int MeterStage => _meterStage;

    /// <summary>Where the bar is, 0 to 1, for whoever draws it.</summary>
    public float MeterAt => _meterT;

    /// <summary>What the power stop came out at, once it has been taken.</summary>
    public float MeterPower => _meterPower;

    private int _meterStage;
    private float _meterT;
    private float _meterPower;

    /// <summary>
    /// How far off the pitch ended up, 0 for a perfect one. Fed into the same term fatigue uses,
    /// because a pitcher who has lost his command and one who is tired miss in the same way.
    /// </summary>
    private float _meterMiss;

    private const float MeterSpeed = 1.55f;      // full sweep in about two thirds of a second

    /// <summary>
    /// Start it, stop it at the top for everything you have, stop it again on the mark for where
    /// you meant it.
    ///
    /// The power stop is the easy one and the command stop is the one that costs you, which is
    /// the right way round: a pitcher's problem is rarely how hard he can throw. Missing the mark
    /// does not move the ball somewhere random — it widens the spread around where he aimed,
    /// which is what losing your release point actually does.
    /// </summary>
    private void UpdatePitchMeter(float dt)
    {
        bool hit = Input.IsActionJustPressed(InputActions.Action);

        switch (_meterStage)
        {
            case 0:
                if (!hit) return;
                _meterStage = 1;
                _meterT = 0f;
                _meterMiss = 0f;
                return;

            case 1:
                _meterT += MeterSpeed * dt;
                if (_meterT >= 1f) { _meterT = 1f; _meterPower = 1f; _meterStage = 2; return; }
                if (!hit) return;
                _meterPower = _meterT;
                _meterStage = 2;
                return;

            default:
                _meterT -= MeterSpeed * dt;

                // Let go of it entirely and it is a pitch with nothing on it and no idea where.
                if (_meterT <= 0f)
                {
                    _meterT = 0f;
                    _meterMiss = 1f;
                    Finish();
                    return;
                }

                if (!hit) return;

                // The mark is the bottom of the bar. How far above it he stopped is his miss, and
                // a weak power stop makes the whole thing harder to control.
                _meterMiss = Mathf.Clamp(_meterT * (1.35f - _meterPower * 0.35f), 0f, 1f);
                Finish();
                return;
        }

        void Finish()
        {
            _meterStage = 0;
            CommitPitch();
        }
    }

    private void ReleasePitch()
    {
        // The side in the field sets itself before the ball is thrown. A human managing the
        // defence keeps whatever he called for; otherwise the bench decides, and both machines in
        // a networked game read the same answer out of the same situation.
        if (!HumanPitching || Online) Situation.Defence = Positioning.Suggested(Situation);

        var pitcher = Situation.FieldingTeam.CurrentPitcher;
        _pitchCounts.TryGetValue(pitcher, out int thrown);
        _pitchCounts[pitcher] = thrown + 1;

        // A pitcher can only throw what he knows.
        if (!pitcher.Knows((int)SelectedPitch)) SelectedPitch = PitchType.Fastball;

        // Spending the signature move guarantees the pitch it belongs to, at its best.
        bool powered = false;
        if (PowerUpArmed && PowerUps.Spend(pitcher))
        {
            powered = true;
            PowerUpArmed = false;
            SelectedPitch = pitcher.Special switch
            {
                Special.CrazyCurve => PitchType.Curveball,
                Special.Corkscrew => PitchType.Slider,
                Special.Knuckleball => PitchType.Changeup,
                _ => PitchType.Fastball,
            };
            Toast($"{pitcher.ShortName} — {PowerUpLedger.Describe(pitcher.Special).ToUpperInvariant()}!", 1.4f);
        }

        // A man rushed in from the pen has not found his release point yet. It reads as fatigue
        // because that is exactly what it looks like from the batter's box — the ball is up and
        // it is not where he meant it — and it goes away over about ten pitches.
        float fatigue = powered ? 0f : CpuBrain.Fatigue(pitcher, thrown) + Pen.Coldness(pitcher, thrown)
                                       + _meterMiss * 0.55f;
        _meterMiss = 0f;

        // A human who aimed the reticle deliberately should get the pitch they asked for. The
        // pitcher's control rating still matters, just far less violently than for the CPU.
        // Difficulty tightens or loosens both sides, but only where a human is in the at-bat —
        // a CPU-versus-CPU pitch keeps command 1 and stays on its calibration.
        var tuning = Game.Instance.Tuning;
        float command = Online ? OnlineCommand
                      : HumanPitching ? tuning.HumanCommand
                      : HumanBatting ? tuning.CpuCommand
                      : 1f;
        // Velocity only rises when a human is in the box. Keying this off HumanPitching instead
        // would have applied the difficulty to computer-versus-computer games too, which is every
        // simulated game in the league — the whole run environment would drift with the setting.
        float speed = Online ? OnlinePitchSpeed : HumanBatting ? tuning.PitchSpeed : 1f;
        CurrentPitch = PitchFactory.Create(pitcher, SelectedPitch, PitchAim, fatigue, ref _rng,
            command, speed);

        // The half-inning fingerprint is far too coarse to find a pitch that is a fraction of an
        // inch out on the two machines — it hashes the score and the runners, so two games can
        // disagree about where the ball crossed and still check out clean. Dumping the pitch
        // itself lets the two self-test logs be diffed directly, and costs the wire nothing.
        if (Online && AutoPlay)
            GD.Print($"[pitch] {SelectedPitch} cross=({CurrentPitch.CrossPoint.X:F4}," +
                     $"{CurrentPitch.CrossPoint.Y:F4}) flight={CurrentPitch.FlightTime:F4} " +
                     $"mph={CurrentPitch.SpeedMph:F2} fatigue={fatigue:F4} thrown={thrown} " +
                     $"arm={pitcher.Id}");

        LastPitchMph = CurrentPitch.SpeedMph;
        LastPitchName = SwingProfileNames.Of(SelectedPitch);
        _pitchProgress = 0f;
        SwingTaken = false;

        // Online both hitters are human, and planning a swing the computer will never take would
        // still draw from the shared random stream — which is enough on its own to put the two
        // machines out of step for the rest of the game.
        if (!HumanBatting && !Online) PlanCpuSwing();

        SetPhase(AtBatPhase.PitchFlight);
    }

    // -----------------------------------------------------------------------
    // The pitch on its way, and the swing
    // -----------------------------------------------------------------------

    private void UpdatePitchFlight(float dt)
    {
        if (Delivering && _deliveryElapsed < DeliveryDuration) _deliveryElapsed += dt;

        _pitchProgress += dt / CurrentPitch.FlightTime;
        CurrentPitch.Progress = _pitchProgress;

        if (!SwingTaken)
        {
            if (HumanBatting) HandleHumanBatting(dt);
            else if (Online) PlayRemoteSwing();
            // Judge the CPU on the timing it intended, not on wherever the frame boundary landed,
            // so a long frame does not turn a good swing into a whiff.
            else if (_pitchProgress >= _cpuSwingAt && _cpuWillSwing)
                TakeSwing(_cpuBunt, _cpuCursor, _cpuSwingAt, _cpuSwingType);
        }

        // Past the plate with no swing: the umpire decides. Online, this machine cannot call it
        // until the other player has said what he did — silence and a swing still crossing the
        // wire look identical from here, and guessing would call a strike on a man who hit it.
        if (!SwingTaken && _pitchProgress >= 1.18f)
        {
            if (!Online)
            {
                TakePitch();
            }
            else if (HumanBatting && !_takeSent)
            {
                // Taking a pitch makes the umpire reach for a random number, so it has to be
                // ordered by the host like every other draw. Calling it here and telling the other
                // machine afterwards let the host throw the next pitch before it had applied the
                // take — the same two draws in the opposite order — and from that one flipped
                // ball-strike call the two games never agreed again.
                _takeSent = true;
                Net.NetLink.I.Send(Net.NetVerb.Take);
            }
        }

        // A call being held open for a possible challenge stands once the moment passes.
        if (_callPending && ChallengeWindow <= 0f) ApplyCall(LastCallWasStrike);
    }

    /// <summary>
    /// Moves the bat around the zone. The mouse drives it directly — point where you want to
    /// swing — and the arrow keys still work for anyone who prefers them.
    /// </summary>
    /// <summary>Which of the three ways of hitting is in force. Read once a game, not per frame.</summary>
    public HittingStyle HitStyle { get; private set; } = HittingStyle.Zone;

    private void MoveBatCursor(float dt)
    {
        var beforeCursor = BatCursor;
        Vector2 mouse = _batting.GetGlobalMousePosition();
        Vector2 stick = AimStick();

        // --- Timing: there is no aiming. ---
        //
        // The bat goes where the ball is going, and the only question left is when it comes
        // through. Everything the resolver does with placement still happens; the placement is
        // simply handed over. This is the setting that makes the game playable with one button,
        // and it is easier because it asks less of you rather than because anything was widened.
        if (HitStyle == HittingStyle.Timing)
        {
            if (CurrentPitch != null) BatCursor = CurrentPitch.CrossPoint;
            return;
        }

        // --- Directional: four coarse quadrants rather than a free point. ---
        //
        // Up or down for high and low, left or right for in and away, and nothing in between. A
        // guess at where the pitch is going rather than a hand on it — which is what a real hitter
        // has, and it sits between the other two rather than being a lesser version of either.
        if (HitStyle == HittingStyle.Directional)
        {
            Vector2 want = new(0f, (Pitch.ZoneBottom + Pitch.ZoneTop) * 0.5f);
            if (stick.LengthSquared() > 0.09f)
            {
                want.X += Mathf.Sign(stick.X) * (Mathf.Abs(stick.X) > 0.3f ? 0.62f : 0f);
                want.Y += Mathf.Sign(stick.Y) * (Mathf.Abs(stick.Y) > 0.3f ? 0.78f : 0f);
            }

            // Eased rather than snapped, so a late change of mind costs something.
            BatCursor = BatCursor.Lerp(want, Mathf.Min(1f, dt * 9f));
            if (BatCursor.DistanceSquaredTo(beforeCursor) > 0.0001f) _batterMoved = true;
            return;
        }

        if (MouseIsAiming(mouse, stick))
        {
            BatCursor = _batting.ScreenToPlate(mouse);
        }
        else
        {
            // The stick has to cross the zone inside a pitch or it is not a way to play. The zone
            // is about 1.7 feet across and a fastball is in the air a bit over a second, so 6.2
            // feet a second left a pad player unable to reach the other corner in time.
            const float CursorSpeed = 11.5f;   // feet per second at full deflection
            BatCursor += stick * CursorSpeed * dt;
        }

        BatCursor.X = Mathf.Clamp(BatCursor.X, -2.2f, 2.2f);
        BatCursor.Y = Mathf.Clamp(BatCursor.Y, 0.6f, 5.0f);

        // Shifting the stance is a hitter stepping in and being alert.
        if (BatCursor.DistanceSquaredTo(beforeCursor) > 0.0001f) _batterMoved = true;
    }

    private void HandleHumanBatting(float dt)
    {
        MoveBatCursor(dt);

        if (Input.IsActionJustPressed(InputActions.PowerUp)) TogglePowerUp();

        if (Input.IsActionJustPressed(InputActions.Bunt))
            TakeSwing(true, BatCursor, _pitchProgress, SwingType.Normal);
        else if (Input.IsActionJustPressed(InputActions.SwingPower))
            TakeSwing(false, BatCursor, _pitchProgress, SwingType.Power);
        else if (Input.IsActionJustPressed(InputActions.SwingContact))
            TakeSwing(false, BatCursor, _pitchProgress, SwingType.Contact);
        else if (Input.IsActionJustPressed(InputActions.Action))
            TakeSwing(false, BatCursor, _pitchProgress, SwingType.Normal);
    }

    /// <summary>The swing type the hitter currently has loaded up, for the coverage indicator.</summary>
    public SwingType PendingSwing =>
        Input.IsActionPressed(InputActions.SwingPower) ? SwingType.Power
        : Input.IsActionPressed(InputActions.SwingContact) ? SwingType.Contact
        : SwingType.Normal;

    /// <summary>What the last swing did, shown to the hitter so the timing is learnable.</summary>
    public BattedBall LastSwing { get; private set; }
    public SwingResult LastSwingResult { get; private set; }
    public float SwingFeedbackTimer { get; private set; }
    public SwingType LastSwingType { get; private set; }

    private SwingType _cpuSwingType = SwingType.Normal;

    private void PlanCpuSwing()
    {
        // Only sharpen or dull the CPU hitter when a human is on the mound.
        float read = HumanPitching ? Game.Instance.Tuning.CpuRead : 1f;
        var plan = CpuBrain.PlanSwing(Situation, Situation.Batter, CurrentPitch, ref _rng, read);
        _cpuWillSwing = plan.WillSwing;
        _cpuSwingType = plan.Type;
        _cpuBunt = plan.Bunt;
        _cpuSwingAt = plan.SwingAt;
        _cpuCursor = plan.Cursor;
    }

    private void TakeSwing(bool bunt, Vector2 cursor, float atProgress, SwingType type)
    {
        // Tell the other machine before resolving, so his bat starts moving at the earliest
        // possible moment. Only the hitter's own machine originates a swing.
        if (Online && HumanBatting)
            Net.NetLink.I.SendSwing(bunt, cursor, atProgress, (int)type, PowerUpArmed);

        SwingTaken = true;
        SwingFlash = SwingDuration;

        var batter = Situation.Batter;

        // A human is reacting to a pitch on screen, so give them a wider sweet spot and a much
        // more forgiving timing window. The CPU swings with 1 and 1, which is what the balance
        // numbers were tuned against, so none of this moves the simulation.
        float assist = BatAssist;
        float timingAssist = TimingAssist;

        // A spent hitting power-up makes this swing count: the bat covers the plate and the
        // timing forgives almost anything. Backyard's specials were moments, not modifiers.
        if (PowerUpArmed && PowerUps.Spend(batter))
        {
            PowerUpArmed = false;
            assist *= 2.2f;
            timingAssist *= 1.8f;
            if (batter.Special == Special.BuntMaster) bunt = true;
            Toast($"{batter.ShortName} — {PowerUpLedger.Describe(batter.Special).ToUpperInvariant()}!", 1.4f);
        }

        // Every input to a swing, so the two self-test logs can be diffed and the one that differs
        // named rather than guessed at. The pitch trace above cleared the pitch; this covers
        // everything else the resolver is handed, including where the shared random stream is.
        if (Online && AutoPlay)
            GD.Print($"[swing] bunt={bunt} cur=({cursor.X:F4},{cursor.Y:F4}) at={atProgress:F5} " +
                     $"type={type} assist={assist:F4} timing={timingAssist:F4} " +
                     $"bat={batter.Id} rng={_rng.Peek():X8}");

        SwingResult result = bunt
            ? SwingResolver.ResolveBunt(batter, CurrentPitch, atProgress, cursor, ref _rng, out var ball)
            : SwingResolver.Resolve(batter, CurrentPitch, atProgress, cursor, ref _rng, out ball,
                assist, type, timingAssist);

        if (Game.Instance.Vibration && InputActions.GamepadConnected)
        {
            float strength = result == SwingResult.InPlay ? 0.55f
                : result == SwingResult.Foul ? 0.28f : 0.12f;
            Input.StartJoyVibration(Input.GetConnectedJoypads()[0], strength, strength * 0.7f, 0.12f);
        }

        if (HumanBatting)
        {
            LastSwing = ball;
            LastSwingResult = result;
            LastSwingType = type;
            SwingFeedbackTimer = 1.3f;

            // Every human swing, with the numbers that decided it. Reasoning about why hitting
            // feels wrong from a synthetic model of a hitter has produced four wrong diagnoses;
            // this is the actual swing a person actually took.
            GD.Print($"[swing] at={atProgress:F3} timing={ball.TimingNorm:+0.00;-0.00;0.00} " +
                     $"cursor=({cursor.X:F2},{cursor.Y:F2}) " +
                     $"cross=({CurrentPitch.CrossPoint.X:F2},{CurrentPitch.CrossPoint.Y:F2}) " +
                     $"miss=({ball.MissX:+0.00;-0.00;0.00},{ball.MissY:+0.00;-0.00;0.00}) " +
                     $"strike={CurrentPitch.IsStrike} {SwingProfile.Label(type)} -> {result}");
        }

        switch (result)
        {
            case SwingResult.Miss:
                Sfx.Instance?.Play(Sound.Whiff, 0.55f, 0.94f + _audioRng.NextFloat() * 0.14f);
                if (Situation.AddStrike(foul: false))
                {
                    Sfx.Instance?.Play(Sound.Out, 0.6f);
                    if (HumanBatting) CrowdSound(Sound.CrowdGroan, 0.4f);
                    else CrowdSound(Sound.CrowdCheer, 0.45f);
                    Narrator.Instance?.Say(VoiceLine.Strikeout, 3);
                    Narrator.Instance?.SayName(Situation.Batter);
                    Narrator.Instance?.Colour(ColourLine.CStrikeout, 0.45f);
                    JudgeMoment(1, false);
                }
                else Narrator.Instance?.Say(VoiceLine.SwingMiss);
                ShowResult("Swing and a miss.");
                break;

            case SwingResult.Foul:
                Situation.AddStrike(foul: true);
                Sfx.Instance?.Play(Sound.Foul, 0.8f, 0.92f + _audioRng.NextFloat() * 0.18f);
                Narrator.Instance?.Say(VoiceLine.Foul);
                // A long foul-off battle is worth remarking on, but only once it is really one.
                if (Situation.Strikes >= 2) Narrator.Instance?.Colour(ColourLine.CFoulTrouble, 0.22f);
                ShowResult("Fouled off.");
                break;

            case SwingResult.InPlay:
                // How hard it was struck picks the sample: a barrelled ball cracks, everything
                // else is the duller contact sample, so the sound tells you how you did.
                if (type == SwingType.Contact && ball.WasBunt)
                    Sfx.Instance?.Play(Sound.Bunt, 0.8f);
                else if (ball.Quality >= 0.55f)
                    Sfx.Instance?.Play(Sound.Crack, 1f, 0.96f + _audioRng.NextFloat() * 0.1f);
                else
                    Sfx.Instance?.Play(Sound.Contact, 0.85f, 0.90f + _audioRng.NextFloat() * 0.2f);
                StartPlay(ball);
                break;
        }
    }

    /// <summary>Challenges left to each club, under the 2026 automated ball-strike rule.</summary>
    public ChallengeBank Challenges { get; } = new();

    /// <summary>The call standing on the last taken pitch, and how long it can still be challenged.</summary>
    public bool LastCallWasStrike { get; private set; }
    public float ChallengeWindow { get; private set; }
    public string ChallengeVerdict { get; private set; } = "";
    public float VerdictTimer { get; private set; }

    /// <summary>True while the human's club may tap the helmet on the call that just went against them.</summary>
    public bool CanChallenge =>
        ChallengeWindow > 0f && CurrentPitch != null &&
        Challenges.Any(HumanBatting ? Situation.TopHalf : !Situation.TopHalf);

    private void TakePitch()
    {
        SwingTaken = true;

        // Before anything is called: did it hit him. There is no umpire's judgement to make and
        // no challenge to take — he is on first and the count is gone.
        if (LooseBall.HitsBatter(CurrentPitch, Situation.Batter, ref _rng))
        {
            Sfx.Instance?.Play(Sound.Out, 0.5f);
            ShowResult("Hit by the pitch.");
            AddLog($"{Situation.Batter.Name} is hit by a pitch.");
            Situation.AwardHitByPitch();
            return;
        }

        // Either way it thumps into the catcher's mitt.
        Sfx.Instance?.Play(Sound.MittPop, 0.6f, 0.92f + _audioRng.NextFloat() * 0.16f);

        // The umpire's call, which is not always the truth — see Umpire.
        bool called = Umpire.CallsStrike(CurrentPitch, ref _rng);
        LastCallWasStrike = called;

        // A club with a challenge left gets a beat to tap the helmet before the call stands.
        // The count is not touched until then: a strikeout or a walk removes the batter, and
        // unwinding that afterwards is far messier than simply not doing it yet.
        bool humanInvolved = HumanBatting || HumanPitching;
        bool awayClub = HumanBatting ? Situation.TopHalf : !Situation.TopHalf;
        if (humanInvolved && Challenges.Any(awayClub))
        {
            _callPending = true;
            ChallengeWindow = 1.7f;
            Toast(called ? "Strike." : "Ball.", 1.4f);
            return;
        }

        ApplyCall(called);
    }

    private bool _callPending;

    /// <summary>Puts a ball-strike call into the count and reports it.</summary>
    private void ApplyCall(bool called)
    {
        _callPending = false;
        ChallengeWindow = 0f;

        bool endedAtBat;

        if (called)
        {
            // AddStrike reports strike three, which deserves more than a called-strike toast.
            if (endedAtBat = Situation.AddStrike(foul: false))
            {
                Sfx.Instance?.Play(Sound.Out, 0.6f);
                if (HumanBatting) CrowdSound(Sound.CrowdGroan, 0.4f);
                else CrowdSound(Sound.CrowdCheer, 0.45f);
                Narrator.Instance?.Say(VoiceLine.Strikeout, 3);
                Narrator.Instance?.SayName(Situation.Batter);
                Narrator.Instance?.Colour(ColourLine.CStrikeout, 0.45f);
                JudgeMoment(1, false);
            }
            else Narrator.Instance?.Say(VoiceLine.CalledStrike);
            ShowResult("Called strike.");
        }
        else
        {
            if (endedAtBat = Situation.AddBall())
            {
                Sfx.Instance?.Play(Sound.Walk, 0.6f);
                Narrator.Instance?.Say(VoiceLine.Walk, 3);
                Narrator.Instance?.SayName(Situation.Batter);
                Narrator.Instance?.Colour(ColourLine.CWalk, 0.5f);
            }
            else Narrator.Instance?.Say(VoiceLine.Ball);
            ShowResult("Ball.");
        }

        // And whether the catcher kept it in front of him. Only worth asking while the at-bat is
        // still alive and there is somebody out there to take the extra bag.
        if (endedAtBat || Situation.IsOver || Situation.RunnerCount == 0) return;
        if (!LooseBall.GetsAway(CurrentPitch,
                Situation.FieldingTeam.Fielder(Data.Position.C), ref _rng))
            return;

        int scored = Situation.WildPitch();
        Sfx.Instance?.Play(Sound.Out, 0.4f);
        ShowResult(scored > 0 ? "Wild pitch — a run scores!" : "Wild pitch!");
        AddLog($"Wild pitch by {Situation.CurrentPitcher?.Name}. Runners move up.");
    }

    // -----------------------------------------------------------------------
    // Ball in play
    // -----------------------------------------------------------------------

    /// <summary>The ball that started the current play, for the play-by-play call.</summary>
    private BattedBall _ballInPlay;

    /// <summary>Who struck it. The situation has moved on by the time the play resolves.</summary>
    private PlayerData _ballInPlayBatter;

    private void StartPlay(BattedBall ball)
    {
        _ballInPlay = ball;
        _ballInPlayBatter = Situation.Batter;
        // Both machines have to run the identical ball in play, and these two flags are per-machine
        // by their nature: the fielding side would steer a fielder by hand while the batting side
        // simulated him, and the batting side would read hold-and-send keys the other side cannot
        // see. Either one makes the same batted ball land in two different places.
        //
        // Online the play therefore runs itself on both sides. Steering a fielder or waving a
        // runner round needs its own decision on the wire before it can be allowed back in.
        Play.HumanControlsDefense = !Online && HumanPitching && !Game.Instance.AutoFielding;
        Play.HumanControlsOffense = !Online && HumanBatting;
        Play.Begin(Situation, ball, _playSeed++);
        Replay.Tape.Begin(Play, ball);
        _playAccumulator = 0f;
        _field.OnPlayStarted();
        SetPhase(AtBatPhase.InPlay);
    }

    /// <summary>
    /// The ball in play is stepped at a fixed rate rather than at the frame rate. Integrating
    /// projectile flight and fielder pursuit with a long frame's delta moves the ball tens of
    /// feet per step, which makes fielders sail past catchable balls.
    /// </summary>
    private const float PlayStep = 1f / 120f;

    private void UpdateInPlay(float dt)
    {
        // Steer the chasing fielder, but only if the player asked to. Automatic is the default.
        //
        // StartPlay is careful to switch manual fielding off online, and this line ran every frame
        // afterwards and switched it straight back on. The fielding side steered a man by hand
        // while the batting side simulated him, from the same batted ball — so the guard has to be
        // here too, where the flag is actually maintained, not only where it is first set.
        bool manual = !Online && HumanPitching && !Game.Instance.AutoFielding;
        Play.UseManualFielder = manual;
        Play.HumanControlsDefense = manual;

        if (manual)
        {
            Vector2 mouse = _field.GetGlobalMousePosition();
            Vector2 stick = Input.GetVector(
                InputActions.AimLeft, InputActions.AimRight,
                InputActions.AimDown, InputActions.AimUp);
            Vector2 aim = MouseIsAiming(mouse, stick)
                ? _field.ScreenToField(mouse)
                : Play.ManualTarget;

            // Arrow keys and the stick nudge him too, for anyone not using a mouse.
            if (stick.LengthSquared() > 0.04f)
            {
                var chaser = Play.Controlled;
                if (chaser != null) aim = chaser.Spot + stick * 60f;
            }

            Play.ManualTarget = aim;
        }

        _playAccumulator += dt;

        int guard = 0;
        while (_playAccumulator >= PlayStep && !Play.Finished && guard++ < 4000)
        {
            Play.Update(PlayStep);
            Replay.Tape.Record(Play, PlayStep);
            _playAccumulator -= PlayStep;
        }

        if (!Play.Finished) return;
        _playAccumulator = 0f;

        var outcome = Play.Outcome;
        bool humanHitting = HumanBatting;

        if (AutoPlay)
            GD.Print($"[play] ev={_ballInPlay.ExitVelocity:F2} la={_ballInPlay.LaunchAngle:F2} " +
                     $"spray={_ballInPlay.SprayAngle:F2} q={_ballInPlay.Quality:F3} " +
                     $"-> hit={outcome.IsHit} hr={outcome.IsHomeRun} runs={outcome.Runs} " +
                     $"outs={outcome.Outs}");

        Play.Apply(Situation);

        // The crowd follows the human's fortunes, not the home team's, so a good play always
        // sounds good to the person holding the controller.
        if (outcome.IsHomeRun)
        {
            Sfx.Instance?.Play(Sound.Homer, 1f);
            CrowdSound(Sound.CrowdCheer, humanHitting ? 0.9f : 0.4f);
        }
        else if (outcome.Runs > 0)
        {
            CrowdSound(Sound.CrowdCheer, humanHitting ? 0.7f : 0.35f);
        }
        else if (outcome.Outs > 0)
        {
            Sfx.Instance?.Play(Sound.GloveCatch, 0.7f);
            Sfx.Instance?.Play(Sound.Out, 0.55f);
            if (humanHitting) CrowdSound(Sound.CrowdGroan, 0.4f);
        }

        NarrateOutcome(outcome);
        ShowResult(string.IsNullOrEmpty(outcome.Description) ? "The play is over." : outcome.Description,
            fromPlay: true);

        JudgeMoment(outcome.Outs, outcome.IsHit);

        // Was that worth another look? The bar is deliberately high — a replay of a routine ground
        // ball is an interruption, not a replay. Online it is off entirely: one machine pausing to
        // watch something again while the other plays on is a desync waiting to happen.
        bool worth = Replay.Tape.WorthShowing(outcome.IsHomeRun, outcome.Runs, outcome.Outs,
            outcome.IsHit);

        if (!Online && !AutoPlay && worth)
            _replayPending = string.IsNullOrEmpty(outcome.Description)
                ? "That one again" : outcome.Description;
    }

    /// <summary>The replay that will roll once the live call has finished, if there is one.</summary>
    private string _replayPending;

    // --- The moment in progress, if this is one. ---
    private int _momentRunsAtStart, _momentTheirsAtStart, _momentOuts;
    private bool _momentHit;

    /// <summary>
    /// Checks a moment against what has just happened, and ends it the instant it is decided.
    ///
    /// A moment that keeps going after its question has been answered is not a moment, it is a
    /// short game — so the walk-off ends on the run crossing, not at the end of the inning.
    /// </summary>
    private void JudgeMoment(int outsMade, bool wasHit)
    {
        var g = Game.Instance;
        if (g.PendingMoment is not { } moment) return;

        _momentOuts += outsMade;
        if (wasHit) _momentHit = true;

        var verdict = Moments.Judge(moment, Situation, _momentRunsAtStart, _momentTheirsAtStart,
            _momentOuts, _momentHit);
        if (verdict == Moments.Verdict.Running) return;

        bool completedBefore = Moments.Completed(moment);
        Moments.Finish(moment, verdict == Moments.Verdict.Won);

        if (verdict == Moments.Verdict.Won)
        {
            int coins = completedBefore ? Mathf.Max(100, moment.Coins / 5) : moment.Coins;
            Cards.Collection.Load();
            Cards.Collection.Earn(coins);
            if (!completedBefore && moment.Pack >= 0) Cards.Collection.Stash(moment.Pack);
            Cards.Program.BookGame(true, 1, 0);
            Cards.Collection.Save();

            g.LastResultHeadline = $"{moment.Name} — done.";
            g.LastResultLine = !completedBefore && moment.Pack >= 0
                ? $"{Cards.Market.Coins(coins)} and a {Cards.Market.Packs[moment.Pack].Name}."
                : completedBefore ? $"Replay complete — {Cards.Market.Coins(coins)} earned."
                : $"{Cards.Market.Coins(coins)} earned.";
            CrowdSound(Sound.CrowdCheer, 0.9f);
        }
        else
        {
            g.LastResultHeadline = $"{moment.Name} — not this time.";
            g.LastResultLine = moment.GoalText;
        }

        g.PendingMoment = null;
        Situation.EndNow();
    }

    /// <summary>The broadcast's replay: what it recorded and how it is shown.</summary>
    public readonly ReplayDirector Replay = new();

    /// <summary>
    /// Picks the play-by-play line for a completed play. Priorities rise with how big the moment
    /// is, so a home run call is never cut off by a routine one.
    /// </summary>
    private void NarrateOutcome(PlayOutcome outcome)
    {
        var n = Narrator.Instance;
        if (n == null) return;

        // Whoever hit it — captured before the situation moves on to the next batter.
        var hitter = _ballInPlayBatter;

        if (outcome.IsHomeRun)
        {
            n.Say(VoiceLine.Homer, 5);
            n.SayName(hitter, full: true);        // a home run earns the full name
            n.Colour(ColourLine.CHomer, 0.85f);
            return;
        }

        if (outcome.Outs >= 2)
        {
            n.Say(VoiceLine.DoublePlay, 4);
            n.Colour(ColourLine.CNiceDefense, 0.6f);
            return;
        }

        if (outcome.IsHit)
        {
            switch (outcome.BasesForBatter)
            {
                case >= 3: n.Say(VoiceLine.Triple, 4); n.SayName(hitter); return;
                case 2: n.Say(VoiceLine.Double, 3); n.SayName(hitter); return;
                default: n.Say(VoiceLine.Single, 2); n.SayName(hitter); return;
            }
        }

        if (outcome.Outs == 1)
        {
            // A ball caught on the fly versus one thrown across the diamond.
            n.Say(outcome.BasesForBatter == 0 && _ballInPlay.LaunchAngle >= 22f
                ? VoiceLine.FlyOut : VoiceLine.GroundOut, 2);
            return;
        }

        if (outcome.Runs > 0) n.Say(VoiceLine.Rally, 3);
    }

    /// <summary>
    /// The analyst's between-pitch read of the game. Offered once per hitter rather than per
    /// pitch — he is filling a gap, not narrating.
    /// </summary>
    private void OfferColourForSituation()
    {
        var n = Narrator.Instance;
        if (n == null) return;

        var s = Situation;
        int margin = Mathf.Abs(s.HomeScore - s.AwayScore);
        bool late = s.Inning >= Mathf.Max(3, Game.Instance.Innings - 1);

        if (late && margin <= 1) { n.Colour(ColourLine.CLateClose, 0.5f); return; }
        if (margin >= 6) { n.Colour(ColourLine.CBigLead, 0.3f); return; }
        if (s.RunnerOn(2) || s.RunnerOn(3)) { n.Colour(ColourLine.CScoringPos, 0.45f); return; }
        if (s.Outs == 2) { n.Colour(ColourLine.CTwoOuts, 0.25f); return; }

        // Otherwise let him talk about the pitcher, based on how the outing is going.
        _pitchCounts.TryGetValue(s.FieldingTeam.CurrentPitcher, out int thrown);
        if (thrown > 55) n.Colour(ColourLine.CPitcherTired, 0.3f);
        else if (thrown > 12) n.Colour(ColourLine.CPitcherGood, 0.18f);
    }

    /// <summary>The last pitch's velocity and type, for the readout. Zero before the first pitch.</summary>
    public float LastPitchMph { get; private set; }
    public string LastPitchName { get; private set; } = "";

    /// <summary>Transient text that appears without stopping play.</summary>
    public string ToastText { get; private set; } = "";
    public float ToastTimer { get; private set; }

    private void Toast(string text, float seconds = 1.6f)
    {
        ToastText = text;
        ToastTimer = seconds;
    }

    /// <summary>
    /// Reports an outcome. A ball, a called strike or a swing and miss does not stop the game
    /// at all — the message is a toast and the pitcher is already getting back on the rubber.
    /// Only a ball in play holds the camera, and only long enough to see the field settle.
    /// </summary>
    private void ShowResult(string text, bool fromPlay = false)
    {
        Toast(text);
        AddLog(text);
        _resultCameFromPlay = fromPlay;

        if (fromPlay) SetPhase(AtBatPhase.Result, 1.15f);
        else AfterResult();     // straight back to live play, no dead time
    }

    private void AfterResult()
    {
        if (Situation.IsOver) { SetPhase(AtBatPhase.Over); return; }

        // The side was retired on that play: show the inning change now that the result has
        // been read, rather than letting it be swallowed.
        if (_pendingHalfBanner != null)
        {
            BannerText = _pendingHalfBanner;
            _pendingHalfBanner = null;
            _resultCameFromPlay = false;
            MaybeChangePitcher();

            // Long enough to actually read the between-innings card. At 1.1 seconds it was a
            // blink — barely enough to register that the side had been retired, let alone to see
            // who is due up and what the man on the mound has thrown. A broadcast takes a beat
            // here, and the beat is doing real work now that there is something in it.
            SetPhase(AtBatPhase.HalfBreak, 2.6f);
            return;
        }

        _justRetiredSide = false;

        MaybeChangePitcher();
        BeginPitchSelect();
    }

    /// <summary>
    /// Pulls a tiring pitcher for the right arm out of the pen. The human manages his own staff,
    /// so this only acts for the side the computer is running.
    /// </summary>
    private void MaybeChangePitcher()
    {
        var team = Situation.FieldingTeam;

        // Online this is poison, and it was the last thing keeping the two games apart.
        //
        // The test is "is a human pitching", and that answer is different on the two machines for
        // the same club: the side actually running it returns here and keeps its man, while the
        // side batting against it sees a computer on the mound, runs the bullpen logic, and swaps
        // the opposing pitcher out on its own. From then on one machine had a fresh reliever and
        // the other had the starter at eighty-two pitches and completely spent, and every pitch
        // after that was a different pitch.
        //
        // A pitching change is a decision, so online it goes on the wire like every other one and
        // both machines apply it in the host's order.
        if (Online) return;

        if (HumanPitching) return;
        if (Visit.Busy) return;

        var pitcher = team.CurrentPitcher;
        _pitchCounts.TryGetValue(pitcher, out int thrown);

        var reliever = CpuBrain.Relieve(Situation, thrown);
        if (reliever == null) return;

        // The computer's manager walks out too. A change that happened between two frames was the
        // one moment of baseball this game was quietly skipping.
        Visit.Begin(team == Situation.Away, change: true, pitcher, reliever);
    }

    // -----------------------------------------------------------------------
    // Situation callbacks
    // -----------------------------------------------------------------------

    private void OnAnnounced(string message) => AddLog(message);

    /// <summary>
    /// A half inning ends in the middle of resolving a strikeout or a ball in play, so this
    /// cannot take over the phase immediately — the result banner that follows would just
    /// overwrite it, and the out count would snap from two to zero with nothing explaining it.
    /// The change is queued and shown once the play's own result has been read.
    /// </summary>
    /// <summary>
    /// A fingerprint of everything that matters, compared with the other machine every half
    /// inning. Two players watching different ballgames and arguing about the score is a far
    /// worse failure than being told plainly that the link has gone wrong.
    /// </summary>
    private int Fingerprint()
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + Situation.Inning;
            h = h * 31 + (Situation.TopHalf ? 1 : 0);
            h = h * 31 + Situation.Outs;
            h = h * 31 + Situation.AwayScore;
            h = h * 31 + Situation.HomeScore;
            h = h * 31 + Situation.AwayHits;
            h = h * 31 + Situation.HomeHits;
            h = h * 31 + Situation.Away.LineupSpot;
            h = h * 31 + Situation.Home.LineupSpot;
            for (int b = 1; b <= 3; b++) h = h * 31 + (Situation.Runners[b]?.Id ?? 0);
            return h;
        }
    }

    private void OnHalfInningChanged()
    {
        if (Online) Net.NetLink.I.ReportChecksum(Fingerprint());

        RefreshControlFlags();
        _pendingHalfBanner = $"{Situation.InningText}\n{Situation.BattingTeam.Team.FullName} batting";
        _justRetiredSide = true;
        AddLog($"— {Situation.InningText} —");
    }

    private void OnGameEnded()
    {
        var g = Game.Instance;
        g.LastResultHeadline = Situation.FinalNote;
        g.LastResultLine = $"{Situation.Away.Team.Abbrev} {Situation.AwayScore} — " +
                           $"{Situation.Home.Team.Abbrev} {Situation.HomeScore}";

        // A farm game is a night out, not a fixture. The affiliate's season is simulated whole at
        // the end of the year, so booking this against the league as well would credit a prospect
        // twice for the same evening — once for the game you attended and once for the same game
        // inside the simulation.
        // A career game books one man's line and nothing else. The rest of the box score belongs
        // to nobody — these are affiliates playing an exhibition as far as the league is concerned.
        if (g.CareerPlayer is { } me)
        {
            var career = Season.CareerState.Load();
            if (career != null)
            {
                Season.CareerEngine.BookGame(career, Situation.Stats.Batting(me));
                career.Save();

                var line = Situation.Stats.Batting(me);
                g.LastResultHeadline = $"{me.Name} — {line.Hits} for {line.AtBats}";
                g.LastResultLine =
                    $"{Situation.Away.Team.Abbrev} {Situation.AwayScore} — " +
                    $"{Situation.Home.Team.Abbrev} {Situation.HomeScore}" +
                    (line.HomeRuns > 0 ? $"   ·   {line.HomeRuns} home run(s)" : "");
            }

            g.CareerPlayer = null;
            g.ClearFarmGame();
            return;
        }

        if (g.IsFarmGame)
        {
            g.LastResultHeadline = $"{g.FarmLevelName} — {Situation.FinalNote}";

            // The night you went to counts, and the modelled result it stands in for does not.
            if (g.FarmReplacing is { } swap)
            {
                var level = (Season.Farm.Level)swap.Level;
                bool home = Situation.Home.Team.Id == swap.TeamId;
                int mine = home ? Situation.HomeScore : Situation.AwayScore;
                int theirs = home ? Situation.AwayScore : Situation.HomeScore;

                // Both clubs, or the two sides of the table would stop agreeing about what
                // happened on the field.
                Season.FarmSeason.ReplaceResult(swap.TeamId, level,
                    mine, theirs, swap.Was.Mine, swap.Was.Theirs);
                Season.FarmSeason.ReplaceResult(swap.OpponentId, level,
                    theirs, mine, swap.Was.Theirs, swap.Was.Mine);

                g.LastResultLine +=
                    $"   ·   {Season.FarmSeason.Of(swap.TeamId, level).Text}";
                if (g.League != null) Season.SaveGame.Save(g.League);
            }

            g.ClearFarmGame();
            return;
        }

        // A game played with a collected side pays a purse and is not booked against the league —
        // those men are on real clubs and their season numbers are not yours to write in.
        if (g.CardClubRoster != null)
        {
            bool youWon = Situation.AwayScore > Situation.HomeScore;
            int purse = Cards.Market.Purse(youWon, Situation.AwayScore, Situation.HomeScore);
            Cards.Collection.Earn(purse);

            // The game also counts towards the reward program, which is where the packs you do
            // not pay for come from.
            var rewards = Cards.Program.BookGame(youWon, Situation.AwayScore, Situation.HomeScore);
            Cards.Collection.Save();

            g.LastResultLine += $"   ·   {Cards.Market.Coins(purse)} earned";
            if (rewards.Count > 0) g.LastResultLine += $"   ·   {rewards[0]}";
            g.CardClubRoster = null;
            return;
        }

        // A season or dynasty game counts towards the program too. Running a franchise is playing
        // the game, and only paying the collection mode would quietly punish you for preferring
        // the other one.
        int userTeam = g.League?.UserTeamId ?? -1;
        if (userTeam >= 0 &&
            (Situation.Away.Team.Id == userTeam || Situation.Home.Team.Id == userTeam))
        {
            bool home = Situation.Home.Team.Id == userTeam;
            int mine = home ? Situation.HomeScore : Situation.AwayScore;
            int theirs = home ? Situation.AwayScore : Situation.HomeScore;

            Cards.Collection.Load();
            Cards.Program.BookGame(mine > theirs, mine, theirs);
            Cards.Collection.Save();
        }

        // Book the decision, then fold the box score into the season and save it.
        bool homeWon = Situation.HomeScore > Situation.AwayScore;
        Situation.Stats.FinishGame(
            homeWon ? Situation.Home : Situation.Away,
            homeWon ? Situation.Away : Situation.Home,
            homeWon ? Situation.HomeScore : Situation.AwayScore,
            homeWon ? Situation.AwayScore : Situation.HomeScore);

        // A scheduled season game is booked against its slot; a one-off exhibition just adds
        // its numbers to the record book.
        if (g.PendingSeasonGame != null && Net.NetLeague.I.Active)
        {
            // In a shared league the game goes to the other owner before it goes into the book,
            // and the calendar does not move — it belongs to both of them, and it turns over when
            // both have finished with it.
            Net.NetLeague.I.Finished(g.PendingSeasonGame, Situation);
            Net.NetLeague.I.DoneWithToday();
        }
        else if (g.PendingSeasonGame != null)
        {
            g.League.RecordUserGame(g.PendingSeasonGame, Situation);
            g.League.BeginPlayoffsIfReady();
        }
        else g.League.RecordGame(Situation);

        g.SaveLeague();

        BannerText = Situation.FinalNote;
        SetPhase(AtBatPhase.Over);
    }

    public void AddLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Log.Add(text);
        if (Log.Count > 6) Log.RemoveAt(0);
    }

    public float PitchProgress => _pitchProgress;
}
