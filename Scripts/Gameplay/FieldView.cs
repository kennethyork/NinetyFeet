using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.UI;

namespace SandlotSlugfest.Gameplay;

/// <summary>
/// The pulled-back look at the whole diamond once the ball is in play: fielders converging,
/// runners churning around the bases, and the ball arcing over the grass.
/// </summary>
public partial class FieldView : Node2D
{
    public GameScene Scene;

    // Fitting the whole park is comfortable on a monitor, but on a six-inch phone it turns the
    // ball and every defender into pinpoints. Keep enough outfield in frame to read a normal fly
    // ball while bringing the playable part of the diamond closer on touch devices.
    // A phone is held much farther from the eye, relative to its size, than a monitor. The old
    // 1.18 multiplier was technically closer but still read like a whole-park tactical view.
    // This frames the infield as the default shot; the follow camera below picks up deep flies.
    private static float ActorScale => TouchControls.MobileLayout
        ? 0.50f * (Game.Instance?.MobileCameraZoom ?? 1.60f) / 1.60f
        : 0.42f;

    private float _scale = 1.4f;
    private Vector2 _origin;      // screen position of home plate
    private bool _cameraReady;
    private float _time;

    public override void _Process(double delta) => _time += (float)delta;

    public void OnPlayStarted() => QueueRedraw();

    /// <summary>
    /// Which base a screen point is over, or -1. Used so a human on defence can simply click the
    /// bag he wants the throw to go to instead of hunting for a number key.
    /// </summary>
    public int BaseUnder(Vector2 screen)
    {
        const float grab = 34f;
        int best = -1;
        float bestDist = grab;
        for (int bag = 0; bag <= 3; bag++)
        {
            float d = ToScreen(FieldGeometry.Bases[bag]).DistanceTo(screen);
            if (d < bestDist) { bestDist = d; best = bag; }
        }
        return best;
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;

        // Fit roughly 430 feet of depth and 800 feet of width into the viewport.
        _scale = ViewScale(size);
        var fittedOrigin = new Vector2(size.X * 0.5f, size.Y - 70f);
        Vector2 previousOrigin = _origin;
        _origin = fittedOrigin;

        // A phone cannot show the entire outfield and keep the live actors readable. Once a ball
        // leaves the infield, follow it like a broadcast camera; settle back behind home as soon
        // as the action returns. The modest smoothing keeps a hard liner from snapping the field.
        if (TouchControls.MobileLayout && Scene.Play != null)
        {
            Vector2 focus = Scene.Play.BallSpot;
            Vector2 wanted = focus.Y > 105f
                ? new Vector2(size.X * 0.5f - focus.X * _scale,
                    size.Y * 0.43f + focus.Y * _scale)
                : fittedOrigin;
            if (!_cameraReady) { _origin = wanted; _cameraReady = true; }
            else _origin = previousOrigin.Lerp(wanted, 0.12f);
        }

        // A replay is shot from closer in and follows the ball, which is the entire reason it is
        // worth watching a second time. The camera pushes in and pans; nothing else changes.
        var replay = Scene.Replay;
        if (replay is { Running: true })
        {
            _scale *= replay.Zoom;
            Vector2 follow = replay.CameraTarget;
            _origin = new Vector2(size.X * 0.5f - follow.X * _scale,
                                  size.Y * 0.62f + follow.Y * _scale);
        }

        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);
        DrawField();
        DrawParkName(size);
        DrawBases();

        if (replay is { Running: true })
        {
            DrawReplay(size);
            DrawCallouts(size);
            return;
        }

        DrawFielders();
        DrawRunners();
        DrawBall();
        DrawCallouts(size);
    }

    /// <summary>
    /// The recorded play, drawn from the tape rather than from the live simulation — which by now
    /// has moved on to the next hitter.
    /// </summary>
    private void DrawReplay(Vector2 size)
    {
        var replay = Scene.Replay;
        var tape = replay.Tape;
        var frame = tape.At(replay.FrameIndex);

        var fieldKit = Scene.Situation.KitOf(Scene.Situation.FieldingTeam);
        var batKit = Scene.Situation.KitOf(Scene.Situation.BattingTeam);

        Vector2 ballAt = ToScreen(frame.Ball) - new Vector2(0f, frame.BallHeight * 0.55f * _scale);

        for (int i = 0; i < frame.Fielders.Length && i < tape.FielderPlayers.Length; i++)
        {
            Vector2 at = ToScreen(frame.Fielders[i]);
            float facing = ballAt.X >= at.X ? 1f : -1f;
            CartoonPlayer.Draw(this, at, ActorScale, facing, Pose.Run, fieldKit,
                tape.FielderPlayers[i], _time, motionPhase: _time * 5f, lookAt: ballAt);

            if (frame.HasBall[i]) DrawCircle(at + new Vector2(0f, -46f), 4f, Palette.Ball);
        }

        for (int i = 0; i < frame.Runners.Length && i < tape.RunnerPlayers.Length; i++)
        {
            Vector2 at = ToScreen(frame.Runners[i]);
            var shirt = frame.RunnerOut[i] ? Palette.GreyedOut(batKit) : batKit;
            CartoonPlayer.Draw(this, at, ActorScale, ballAt.X >= at.X ? 1f : -1f,
                frame.RunnerOut[i] ? Pose.Idle : Pose.Run, shirt, tape.RunnerPlayers[i], _time,
                motionPhase: _time * 6f, lookAt: ballAt);
        }

        // The ball and its shadow.
        DrawCircle(ToScreen(frame.Ball) + new Vector2(0f, frame.BallHeight * 0.10f * _scale),
            Mathf.Max(2f, 5f - frame.BallHeight * 0.015f), new Color(0f, 0f, 0f, 0.28f));
        DrawCircle(ballAt, Mathf.Clamp(4.5f + frame.BallHeight * 0.012f, 4f, 8f), Palette.Ball);

        DrawReplayFurniture(size, tape);
    }

    /// <summary>The broadcast dressing: the bug, the caption and how to get out of it.</summary>
    private void DrawReplayFurniture(Vector2 size, ReplayTape tape)
    {
        var bug = new Rect2(new Vector2(40f, 40f), new Vector2(132f, 30f));
        DrawRect(bug, new Color(0.72f, 0.12f, 0.12f, 0.92f));
        Palette.TextCentered(this, bug.Position + bug.Size * 0.5f, "● REPLAY", 15, Colors.White);

        if (tape.Caption != "")
            Palette.Text(this, new Vector2(184f, 62f), tape.Caption, 16, Palette.Ink);

        Palette.Text(this, new Vector2(40f, 88f),
            $"{tape.ExitVelocityMph:F0} mph off the bat  ·  {tape.LaunchAngle:F0}° launch",
            13, Palette.InkDim);

        Palette.TextCentered(this, new Vector2(size.X * 0.5f, size.Y - 34f),
            "any key to skip", 13, Palette.InkDim);
    }

    /// <summary>Field feet to screen pixels. +Y in field space is toward centre field (up-screen).</summary>
    private Vector2 ToScreen(Vector2 field) =>
        _origin + new Vector2(field.X * _scale, -field.Y * _scale);

    /// <summary>The inverse, so the mouse can steer a fielder around the park.</summary>
    public Vector2 ScreenToField(Vector2 screen)
    {
        // Use the camera actually drawn. On mobile it follows deep balls, so reconstructing the
        // old fitted transform here would make a finger steer to the wrong patch of grass.
        Vector2 size = GetViewportRect().Size;
        float scale = ViewScale(size);
        var origin = _cameraReady ? _origin : new Vector2(size.X * 0.5f, size.Y - 70f);
        return new Vector2((screen.X - origin.X) / scale, (origin.Y - screen.Y) / scale);
    }

    private static float ViewScale(Vector2 size)
    {
        float fit = Mathf.Min((size.Y - 120f) / 430f, size.X / 820f);
        float zoom = TouchControls.MobileLayout ? Game.Instance?.MobileCameraZoom ?? 1.60f : 1f;
        return fit * zoom;
    }

    private void DrawField()
    {
        var park = FieldGeometry.Current;
        Vector2 size = GetViewportRect().Size;

        // --- Everything outside the park: stands, then the neighbourhood beyond them. ---
        DrawRect(new Rect2(Vector2.Zero, size), new Color("#20303a"));
        DrawStands(size, park);

        // --- Foul territory: grass all the way round, not a black void. ---
        DrawGroundApron(park);

        // Fair territory out to the wall, sampled along this park's own fence curve.
        const int segments = 48;
        var fair = new Vector2[segments + 2];
        fair[0] = ToScreen(FieldGeometry.Home);
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Lerp(-Mathf.Pi * 0.25f, Mathf.Pi * 0.25f, i / (float)segments);
            float dist = park.DistanceAt(angle);
            fair[i + 1] = ToScreen(new Vector2(Mathf.Sin(angle) * dist, Mathf.Cos(angle) * dist));
        }
        DrawColoredPolygon(fair, park.Grass);

        // Mowing arcs.
        for (int ring = 1; ring <= 5; ring++)
        {
            if (ring % 2 == 1) continue;
            float r = 80f * ring;
            DrawArc(ToScreen(FieldGeometry.Home), r * _scale, Mathf.Pi * 0.25f, Mathf.Pi * 0.75f,
                48, park.GrassAlt, 26f * _scale);
        }

        // --- Warning track: a dirt band hugging the inside of the wall. ---
        for (int i = 0; i < segments; i++)
        {
            float a0 = Mathf.Lerp(-Mathf.Pi * 0.25f, Mathf.Pi * 0.25f, i / (float)segments);
            float a1 = Mathf.Lerp(-Mathf.Pi * 0.25f, Mathf.Pi * 0.25f, (i + 1) / (float)segments);
            Vector2 p0 = new Vector2(Mathf.Sin(a0), Mathf.Cos(a0)) * (park.DistanceAt(a0) - 8f);
            Vector2 p1 = new Vector2(Mathf.Sin(a1), Mathf.Cos(a1)) * (park.DistanceAt(a1) - 8f);
            DrawLine(ToScreen(p0), ToScreen(p1), park.Dirt.Darkened(0.08f), 15f * _scale);
        }

        // The dirt infield: a wedge anchored at home plate out to the arc behind second, so it
        // reads as a real infield rather than a crescent floating in the grass.
        var dirt = new Vector2[28];
        dirt[0] = ToScreen(new Vector2(0f, -8f));
        for (int i = 0; i < 27; i++)
        {
            float angle = Mathf.Lerp(-Mathf.Pi * 0.25f, Mathf.Pi * 0.25f, i / 26f);
            dirt[i + 1] = ToScreen(new Vector2(
                Mathf.Sin(angle) * FieldGeometry.InfieldDirtRadius,
                Mathf.Cos(angle) * FieldGeometry.InfieldDirtRadius));
        }
        DrawColoredPolygon(dirt, park.Dirt);

        // Grass in the middle of the diamond.
        DrawColoredPolygon(new[]
        {
            ToScreen(new Vector2(0f, 12f)),
            ToScreen(new Vector2(52f, 76f)),
            ToScreen(new Vector2(0f, 140f)),
            ToScreen(new Vector2(-52f, 76f)),
        }, park.Grass);

        // Foul lines and the wall.
        float lineLen = FieldGeometry.FenceDownTheLines;
        DrawLine(ToScreen(FieldGeometry.Home),
            ToScreen(new Vector2(lineLen * 0.7071f, lineLen * 0.7071f)), Palette.Chalk, 2f);
        DrawLine(ToScreen(FieldGeometry.Home),
            ToScreen(new Vector2(-lineLen * 0.7071f, lineLen * 0.7071f)), Palette.Chalk, 2f);

        // The outfield wall. Its drawn thickness tracks the real wall height, so a monster in
        // left or a short porch in right is obvious at a glance.
        for (int i = 0; i < segments; i++)
        {
            float a0 = Mathf.Lerp(-Mathf.Pi * 0.25f, Mathf.Pi * 0.25f, i / (float)segments);
            float a1 = Mathf.Lerp(-Mathf.Pi * 0.25f, Mathf.Pi * 0.25f, (i + 1) / (float)segments);
            Vector2 p0 = new Vector2(Mathf.Sin(a0), Mathf.Cos(a0)) * park.DistanceAt(a0);
            Vector2 p1 = new Vector2(Mathf.Sin(a1), Mathf.Cos(a1)) * park.DistanceAt(a1);

            float h = park.HeightAt((a0 + a1) * 0.5f);
            float thickness = Mathf.Clamp(2.5f + h * 0.42f, 3f, 16f);
            DrawLine(ToScreen(p0), ToScreen(p1), park.Wall.Lightened(0.08f), thickness);
            // A trim stripe along the top of the padding.
            DrawLine(ToScreen(p0), ToScreen(p1), park.WallTrim, Mathf.Max(1.5f, thickness * 0.22f));
        }

        // Worn base paths between the bags, and a dirt circle around the plate.
        for (int bag = 0; bag < 4; bag++)
        {
            Vector2 a = ToScreen(FieldGeometry.Bases[bag]);
            Vector2 b = ToScreen(FieldGeometry.Bases[(bag + 1) % 4]);
            DrawLine(a, b, park.Dirt.Lightened(0.05f), 11f * _scale);
        }
        DrawCircle(ToScreen(FieldGeometry.Home), 16f * _scale, park.Dirt);

        // On-deck circles either side of the plate.
        foreach (int side in new[] { -1, 1 })
            DrawCircle(ToScreen(new Vector2(side * 44f, -26f)), 7f * _scale, park.Dirt.Darkened(0.1f));

        // The mound, with a rubber on it.
        DrawCircle(ToScreen(FieldGeometry.Mound), 9f * _scale, park.Dirt.Darkened(0.18f));
        DrawRect(new Rect2(ToScreen(FieldGeometry.Mound) - new Vector2(5f, 1.5f),
            new Vector2(10f, 3f)), Palette.Chalk);
    }

    /// <summary>Grass beyond the foul lines, so the field is not a diamond floating in a void.</summary>
    private void DrawGroundApron(Stadium park)
    {
        const int steps = 40;
        var pts = new Vector2[steps + 3];
        pts[0] = ToScreen(new Vector2(0f, -FieldGeometry.BackstopDistance));
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(-Mathf.Pi * 0.42f, Mathf.Pi * 0.42f, i / (float)steps);
            float d = park.DistanceAt(Mathf.Clamp(a, -Mathf.Pi * 0.25f, Mathf.Pi * 0.25f)) + 46f;
            pts[i + 1] = ToScreen(new Vector2(Mathf.Sin(a) * d, Mathf.Cos(a) * d));
        }
        pts[steps + 2] = pts[0];
        DrawColoredPolygon(pts, park.GrassAlt.Darkened(0.10f));

        // Dirt apron around the infield and behind the plate.
        DrawCircle(ToScreen(FieldGeometry.Home), 46f * _scale, park.Dirt.Darkened(0.12f));
    }

    /// <summary>A ring of seating outside the wall, with a sprinkling of spectators.</summary>
    private void DrawStands(Vector2 size, Stadium park)
    {
        const int steps = 52;
        var inner = new Vector2[steps + 1];
        var outer = new Vector2[steps + 1];

        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(-Mathf.Pi * 0.40f, Mathf.Pi * 0.40f, i / (float)steps);
            float clamped = Mathf.Clamp(a, -Mathf.Pi * 0.25f, Mathf.Pi * 0.25f);

            // Past the foul poles the seating tucks back in toward the backstop instead of
            // projecting straight out, which was splaying the bowl open at the corners.
            float beyond = Mathf.Max(0f, Mathf.Abs(a) - Mathf.Pi * 0.25f) / (Mathf.Pi * 0.15f);
            float d = park.DistanceAt(clamped) * Mathf.Lerp(1f, 0.62f, Mathf.Clamp(beyond, 0f, 1f));

            inner[i] = ToScreen(new Vector2(Mathf.Sin(a) * (d + 10f), Mathf.Cos(a) * (d + 10f)));
            outer[i] = ToScreen(new Vector2(Mathf.Sin(a) * (d + 72f), Mathf.Cos(a) * (d + 72f)));
        }

        var poly = new Vector2[(steps + 1) * 2];
        for (int i = 0; i <= steps; i++)
        {
            poly[i] = outer[i];
            poly[poly.Length - 1 - i] = inner[i];
        }
        DrawColoredPolygon(poly, new Color("#3a4654"));

        // Rows of seats, and a scattering of people in them.
        var rng = new Rng(park.TeamId * 613 + 5);
        for (int row = 1; row <= 3; row++)
        {
            float t = row / 4f;
            for (int i = 0; i < steps; i++)
            {
                Vector2 p0 = inner[i].Lerp(outer[i], t);
                Vector2 p1 = inner[i + 1].Lerp(outer[i + 1], t);
                DrawLine(p0, p1, new Color("#46536280"), 2f);
                if (rng.Chance(0.55f))
                    DrawCircle(p0.Lerp(p1, 0.5f), 2.6f,
                        new Color(rng.NextFloat(), rng.NextFloat(), rng.NextFloat()).Lightened(0.3f));
            }
        }
    }

    /// <summary>Park name and its posted distances, the way a scoreboard shows them.</summary>
    private void DrawParkName(Vector2 size)
    {
        var park = FieldGeometry.Current;

        Palette.TextCentered(this, new Vector2(size.X * 0.5f, 34f), park.Name.ToUpperInvariant(),
            17, Palette.Ink);
        Palette.TextCentered(this, new Vector2(size.X * 0.5f, 52f), park.Quirk, 12, Palette.InkDim);

        // Posted distances painted on the wall at the lines and in centre.
        DrawWallNumber(new Vector2(-Mathf.Pi * 0.25f + 0.05f, 0f).X, park);
        DrawWallNumber(0f, park);
        DrawWallNumber(Mathf.Pi * 0.25f - 0.05f, park);
    }

    private void DrawWallNumber(float angle, Stadium park)
    {
        float dist = park.DistanceAt(angle);
        Vector2 at = ToScreen(new Vector2(Mathf.Sin(angle) * (dist - 16f), Mathf.Cos(angle) * (dist - 16f)));
        Palette.TextCentered(this, at, ((int)dist).ToString(), 12, new Color(1f, 1f, 1f, 0.75f));
    }

    private void DrawBases()
    {
        for (int i = 1; i <= 3; i++)
        {
            Vector2 at = ToScreen(FieldGeometry.Bases[i]);
            float s = 5f * _scale;
            DrawColoredPolygon(new[]
            {
                at + new Vector2(0f, -s), at + new Vector2(s, 0f),
                at + new Vector2(0f, s), at + new Vector2(-s, 0f),
            }, Palette.Chalk);
        }

        Vector2 home = ToScreen(FieldGeometry.Home);
        float hs = 5f * _scale;
        DrawColoredPolygon(new[]
        {
            home + new Vector2(-hs, -hs * 0.4f), home + new Vector2(hs, -hs * 0.4f),
            home + new Vector2(hs * 0.7f, hs * 0.6f), home + new Vector2(0f, hs * 1.1f),
            home + new Vector2(-hs * 0.7f, hs * 0.6f),
        }, Palette.Chalk);
    }

    /// <summary>
    /// Per-fielder animation state. The defence used to flip between a run pose and a standing
    /// pose on a single distance test, and a throw had no animation at all — the ball simply left
    /// a stationary player. These timers give a release and a catch some duration.
    /// </summary>
    private readonly System.Collections.Generic.Dictionary<int, FielderAnim> _anim = new();

    private struct FielderAnim
    {
        public bool HadBall;
        public float Throw;     // counts down through the throwing motion
        public float Catch;     // counts down through gathering it in
        public float RunHold;   // keeps the run cycle alive briefly after stopping
    }

    private const float ThrowSeconds = 0.46f;
    private const float CatchSeconds = 0.26f;

    private void TickFielderAnimation()
    {
        float dt = (float)GetProcessDeltaTime();

        foreach (var f in Scene.Play.Fielders)
        {
            int key = f.Player?.Id ?? 0;
            _anim.TryGetValue(key, out var a);

            // Losing the ball means he threw it; gaining it means he caught it.
            if (a.HadBall && !f.HasBall) a.Throw = ThrowSeconds;
            else if (!a.HadBall && f.HasBall) a.Catch = CatchSeconds;
            a.HadBall = f.HasBall;

            if (a.Throw > 0f) a.Throw -= dt;
            if (a.Catch > 0f) a.Catch -= dt;

            if (f.Spot.DistanceSquaredTo(f.Target) > 4f) a.RunHold = 0.18f;
            else if (a.RunHold > 0f) a.RunHold -= dt;

            _anim[key] = a;
        }
    }

    /// <summary>Which pose a fielder is in, and how far through it he is.</summary>
    private (Pose Pose, float Phase) FielderPose(FielderAgent f, bool moving)
    {
        if (!_anim.TryGetValue(f.Player?.Id ?? 0, out var a)) return (moving ? Pose.Run : Pose.Field, 0f);

        // A throw wins: it is the most readable thing the defence does.
        if (a.Throw > 0f)
            return (Pose.Pitch, Mathf.Clamp(1f - a.Throw / ThrowSeconds, 0f, 1f));

        // Gathering it in — the glove hand comes up, then he settles.
        if (a.Catch > 0f) return (Pose.Field, 0f);

        // Hold the run cycle briefly so he does not snap to standing the instant he arrives.
        return (moving || a.RunHold > 0f ? Pose.Run : Pose.Field, 0f);
    }

    /// <summary>
    /// Which way a fielder who is not chasing anything is turned.
    ///
    /// Facing is a left-right mirror and nothing more — there is no pose that turns a man toward
    /// the camera — so the best that can be done is put each fielder on the correct side. Comparing
    /// his position against home plate does that for the corners, and fails for anyone standing on
    /// the midline: the pitcher, the catcher and the centre fielder are all at x = 0, the
    /// comparison came out true for each of them, and three of the nine spent every pitch turned to
    /// their right for no reason at all.
    ///
    /// A man in the middle of the field turns toward wherever the play is instead, which is both
    /// correct and the thing that stops the infield looking like a row of statues.
    /// </summary>
    private float FacingWhenStill(FielderAgent f)
    {
        const float Midline = 14f;
        Vector2 home = FieldGeometry.Bases[0];

        if (Mathf.Abs(f.Spot.X - home.X) > Midline)
            return home.X >= f.Spot.X ? 1f : -1f;

        // On the midline. Look at the ball if it is live, otherwise at the hitter.
        Vector2 interest = Scene.Play.Phase == PlayPhase.Dead
            ? home
            : Scene.Play.BallSpot;

        if (Mathf.Abs(interest.X - f.Spot.X) < 1f) return f.Spot.Y > home.Y ? -1f : 1f;
        return interest.X >= f.Spot.X ? 1f : -1f;
    }

    private void DrawFielders()
    {
        var team = Scene.Situation.KitOf(Scene.Situation.FieldingTeam);
        TickFielderAnimation();

        // Draw the far side of the field first so nearer players overlap correctly.
        foreach (var f in Scene.Play.Fielders.OrderByDescending(f => f.Spot.Y))
        {
            Vector2 at = ToScreen(f.Spot);
            bool moving = f.Spot.DistanceSquaredTo(f.Target) > 4f;

            // A fielder who is chasing something faces where he is going. One who is not is
            // watching the hitter, which is the whole of his job between pitches.
            //
            // Facing came only from the direction of travel, and a man standing still has a target
            // equal to his own position — so the comparison was always true and every idle fielder
            // turned to his right. The right side of the diamond spent the game with its back to
            // the plate.
            float facing = moving
                ? (f.Target.X >= f.Spot.X ? 1f : -1f)
                : FacingWhenStill(f);

            // Facing is a left-right mirror and always will be, but where a man is *looking* is a
            // separate thing — and it is the one that reads as paying attention. Nine fielders all
            // tracking the ball is most of what makes a diamond look alive.
            var (pose, phase) = FielderPose(f, moving);
            CartoonPlayer.Draw(this, at, ActorScale, facing, pose, team, f.Player, _time,
                motionPhase: phase, lookAt: BallEye());

            if (f.IsChaser)
            {
                bool mine = Scene.Play.UseManualFielder;
                DrawArc(at, 16f, 0f, Mathf.Tau, 20,
                    mine ? new Color(0.45f, 1f, 0.55f) : Palette.Highlight, mine ? 2.6f : 1.8f);

                // A line from the fielder you are steering to where you have pointed him.
                if (mine)
                {
                    Vector2 goal = ToScreen(Scene.Play.ManualTarget);
                    DrawLine(at, goal, new Color(0.45f, 1f, 0.55f, 0.4f), 2f);
                    DrawArc(goal, 9f, 0f, Mathf.Tau, 16, new Color(0.45f, 1f, 0.55f, 0.7f), 2f);
                    Palette.TextCentered(this, at + new Vector2(0f, -30f), "YOU", 11,
                        new Color(0.45f, 1f, 0.55f));
                }
            }
            if (f.HasBall)
                DrawCircle(at + new Vector2(0f, -46f), 4f, Palette.Ball);

            Palette.TextCentered(this, at + new Vector2(0f, 14f),
                PlayerData.PositionLabel(f.Slot), 11, Palette.Ink);
        }

        DrawVisit();
    }

    /// <summary>
    /// Where the ball is on screen, including its height off the ground — a fly ball is looked up
    /// at, which is the difference between a fielder camped under one and a fielder ignoring it.
    /// Falls back to the plate when the ball is dead, because that is where everybody looks.
    /// </summary>
    private Vector2 BallEye()
    {
        var play = Scene.Play;
        if (play == null || play.Phase == PlayPhase.Dead) return ToScreen(FieldGeometry.Bases[0]);

        return ToScreen(play.BallSpot) - new Vector2(0f, play.BallHeight * 0.55f * _scale);
    }

    /// <summary>
    /// The manager's trip to the mound, and the reliever coming in from the pen.
    ///
    /// He walks out from his own dugout, stands with the pitcher, and walks back. On a change the
    /// new arm jogs in from the bullpen at the same time and the man he is replacing walks off, so
    /// the substitution is something you watch rather than a name that changes between frames.
    /// </summary>
    private void DrawVisit()
    {
        var visit = Scene.Visit;
        if (!visit.Busy) return;

        var kit = Scene.Situation.KitOf(Scene.Situation.FieldingTeam);
        Vector2 mound = ToScreen(FieldGeometry.Mound);

        // The dugouts sit either side of the diamond, behind the corner bags.
        Vector2 dugout = ToScreen(new Vector2(visit.FromAwayDugout ? -62f : 62f, 26f));
        Vector2 bullpen = ToScreen(new Vector2(visit.FromAwayDugout ? -104f : 104f, 96f));

        float t = Mathf.SmoothStep(0f, 1f, visit.Progress);

        // The manager. Drawn a touch larger and in the club's colours — he is not one of the kids
        // but he belongs to the same side.
        Vector2 skipper = dugout.Lerp(mound + new Vector2(visit.FromAwayDugout ? -22f : 22f, 4f), t);
        bool walking = visit.Stage != VisitStage.Talking;
        CartoonPlayer.Draw(this, skipper, 0.46f,
            mound.X >= skipper.X ? 1f : -1f,
            walking ? Pose.Run : Pose.Idle, kit, null, _time, motionPhase: walking ? _time * 6f : 0f);

        Palette.TextCentered(this, skipper + new Vector2(0f, 16f), "MGR", 10, Palette.InkDim);

        if (!visit.IsChange) return;

        // The new man, jogging in.
        Vector2 coming = bullpen.Lerp(mound + new Vector2(0f, 6f), t);
        CartoonPlayer.Draw(this, coming, ActorScale, mound.X >= coming.X ? 1f : -1f,
            Pose.Run, kit, visit.Incoming, _time, motionPhase: _time * 7f);

        if (visit.Incoming != null)
            Palette.TextCentered(this, coming + new Vector2(0f, -52f),
                visit.Incoming.ShortName, 11, Palette.Highlight);

        // And the one being taken out, walking off the other way once he has handed the ball over.
        if (visit.Stage == VisitStage.WalkingBack && visit.Outgoing != null)
        {
            Vector2 going = mound.Lerp(dugout, 1f - visit.Progress);
            CartoonPlayer.Draw(this, going, ActorScale, dugout.X >= going.X ? 1f : -1f,
                Pose.Run, kit, visit.Outgoing, _time, motionPhase: _time * 4.5f);
        }
    }

    /// <summary>
    /// Whether this runner is going in head first. He slides when he is nearly at the bag and the
    /// throw is anywhere near him — sliding into an uncontested base is something only a video
    /// game does, and sliding into home from ninety feet out is worse.
    /// </summary>
    private bool Sliding(RunnerAgent r)
    {
        var play = Scene.Play;
        if (play == null || r.IsOut || r.Held) return false;
        if (r.Progress < 0.78f) return false;

        Vector2 bag = FieldGeometry.Bases[r.ToBase % 4];
        if (play.BallSpot.DistanceTo(bag) < 60f) return true;

        foreach (var f in play.Fielders)
            if (f.HasBall && f.Spot.DistanceTo(bag) < 75f) return true;

        return false;
    }

    /// <summary>The dirt he kicks up. Cheap, and it is half of what sells a slide.</summary>
    private void DrawSlideDust(Vector2 at, float facing)
    {
        for (int i = 0; i < 6; i++)
        {
            float k = i / 5f;
            var puff = at + new Vector2(-facing * (8f + k * 30f) * _scale, (3f - k * 4f) * _scale);
            DrawCircle(puff, (3f + k * 6f) * _scale,
                new Color(0.80f, 0.68f, 0.50f, 0.30f * (1f - k)));
        }
    }

    private void DrawRunners()
    {
        var team = Scene.Situation.KitOf(Scene.Situation.BattingTeam);
        foreach (var r in Scene.Play.Runners.OrderByDescending(r => r.Spot.Y))
        {
            if (r.Scored) continue;
            Vector2 at = ToScreen(r.Spot);

            // A retired runner is greyed out; a live one runs toward the next bag.
            var shirt = r.IsOut ? Palette.GreyedOut(team) : team;
            var pose = r.IsOut ? Pose.Idle : (r.Held ? Pose.Idle : Pose.Run);

            // A runner going somewhere faces the bag he is going to. A man held on the base is
            // watching the pitcher and the hitter like everyone else on the field.
            Vector2 next = FieldGeometry.Bases[r.ToBase % 4];
            float facing = r.Held || r.IsOut
                ? (FieldGeometry.Bases[0].X >= r.Spot.X ? 1f : -1f)
                : (next.X >= r.Spot.X ? 1f : -1f);

            // Sliding into a close bag. A real drawn pose this time — hips down, lead leg out,
            // back leg folded under — rather than a run cycle rotated onto its side.
            if (Sliding(r))
            {
                DrawSlideDust(at, facing);
                pose = Pose.Slide;
            }

            // A runner watches the ball, which is exactly what he is doing when he decides whether
            // to go. A man who has been put out has stopped caring and looks at the plate.
            //
            // His legs also run at his own cadence: the cycle is driven by how far he has actually
            // travelled rather than by a clock every man on the field shared, so a burner's feet
            // move faster than a catcher's.
            float beat = (r.Progress * FieldGeometry.BasePathLength + r.FromBase * 90f) * 0.42f;
            CartoonPlayer.Draw(this, at, ActorScale, facing, pose, shirt, r.Player, _time,
                motionPhase: pose == Pose.Run ? beat : 0f,
                lookAt: r.IsOut ? ToScreen(FieldGeometry.Bases[0]) : BallEye());

            if (!r.IsOut)
                Palette.TextCentered(this, at + new Vector2(0f, 14f), r.Player.LastName, 11, Palette.Highlight);
            else
                Palette.TextCentered(this, at + new Vector2(0f, 14f), "OUT", 11, Palette.Warning);
        }
    }

    private void DrawBall()
    {
        var play = Scene.Play;
        Vector2 at = ToScreen(play.BallSpot);

        // Shadow on the grass, offset by how high the ball is.
        Vector2 shadow = at + new Vector2(0f, play.BallHeight * 0.10f * _scale);
        float shadowR = Mathf.Max(2f, 5f - play.BallHeight * 0.015f);
        DrawCircle(shadow, shadowR, new Color(0f, 0f, 0f, 0.30f));

        // The ball itself lifts up-screen with height so arcs read clearly.
        Vector2 drawn = at - new Vector2(0f, play.BallHeight * 0.55f * _scale);
        float r = Mathf.Clamp(4.5f + play.BallHeight * 0.012f, 4f, 8f);
        DrawCircle(drawn, r, Palette.Ball);
        DrawArc(drawn, r, 0f, Mathf.Tau, 12, new Color("#c9c2ad"), 1f);

        // Where a fly ball is going to come down.
        if (play.Phase == PlayPhase.Flight && play.BallHeight > 12f)
        {
            Vector2 spot = ToScreen(play.PredictedLanding);
            DrawArc(spot, 12f, 0f, Mathf.Tau, 20, new Color(1f, 1f, 1f, 0.45f), 1.5f);
            DrawArc(spot, 5f, 0f, Mathf.Tau, 12, new Color(1f, 1f, 1f, 0.30f), 1.2f);
        }
    }

    private void DrawCallouts(Vector2 size)
    {
        if (!string.IsNullOrEmpty(Scene.Play.LastEvent))
            Palette.TextCentered(this, new Vector2(size.X * 0.5f, 90f), Scene.Play.LastEvent, 22, Palette.Ink);

        // Only prompt for things the player can actually do. With automatic fielding on, the
        // defence plays itself, and telling you to steer or throw would be a lie.
        bool manualDefence = Scene.Play.HumanControlsDefense;

        if (manualDefence && Scene.Play.Phase == PlayPhase.Held)
            Palette.TextCentered(this, new Vector2(size.X * 0.5f, size.Y - 34f),
                "Throw:  click a base, or 1 first · 2 second · 3 third · 4 home", 16, Palette.Highlight);
        else if (manualDefence && !Scene.Play.Finished)
            Palette.TextCentered(this, new Vector2(size.X * 0.5f, size.Y - 34f),
                "Point where you want your fielder to run", 16, new Color(0.45f, 1f, 0.55f));
        else if (Scene.HumanBatting && !Scene.Play.Finished)
            Palette.TextCentered(this, new Vector2(size.X * 0.5f, size.Y - 34f),
                "Hold Q to stop at the bag  ·  Hold E to send them", 16, Palette.Highlight);
    }
}
