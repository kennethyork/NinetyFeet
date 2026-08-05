using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.UI;

namespace SandlotSlugfest.Gameplay;

/// <summary>
/// The view from behind the catcher: the pitcher out on the mound, the strike zone floating
/// over the plate, and the ball growing as it comes in.
/// </summary>
public partial class BattingView : Node2D
{
    public GameScene Scene;

    /// <summary>Screen pixels per foot in the plane of home plate.</summary>
    public const float PixelsPerFoot = 104f;

    // The hitter's framing. These three numbers have to stay in step with the bat length in
    // CartoonPlayer.DrawBat: the swung bat must actually sweep through the strike zone, or the
    // whole view reads as if you cannot reach the ball.
    private const float BatterScale = 1.55f;

    /// <summary>
    /// The chalked batter's box, in screen space. The hitter's standing position is derived from
    /// this rather than from its own constants: the two used to be independent numbers, and the
    /// hitter stood 176px from the plate while the box centred about 121px out — so he was drawn
    /// with his feet on the outer chalk line or beyond it.
    /// </summary>
    private Vector2[] BatterBox(float side)
    {
        Vector2 plate = ToScreen(new Vector2(0f, Pitch.ZoneBottom - 0.55f));
        return new[]
        {
            plate + new Vector2(side * 62f, -18f),
            plate + new Vector2(side * 158f, -18f),
            plate + new Vector2(side * 196f, 74f),
            plate + new Vector2(side * 70f, 74f),
        };
    }

    /// <summary>
    /// Where the hitter's feet go. He stands toward the outer edge of his box rather than the
    /// middle of it: dead centre put his body across the inside corner of the strike zone, so he
    /// blocked the very thing you are trying to watch.
    /// </summary>
    private Vector2 BatterStand(float side)
    {
        var box = BatterBox(side);

        // Front-outer and back-outer corners are indices 1 and 2 for a right-hand box.
        var inner = (box[0] + box[3]) * 0.5f;
        var outer = (box[1] + box[2]) * 0.5f;

        // 0 is right on the plate, 1 is the outer chalk. Two thirds out keeps him inside the box
        // and clear of the zone.
        return inner.Lerp(outer, 0.66f) + new Vector2(0f, 12f);
    }

    private Vector2 _zoneCenter;
    private float _time;

    /// <summary>How far behind the plate the camera sits, in feet.</summary>
    private const float CameraSetback = 9f;

    /// <summary>
    /// How far along its apparent journey the ball is, from the release point to the plate.
    /// Apparent position goes as 1 / distance, not linearly with time — an object coming
    /// straight at you barely seems to move for most of its flight and then arrives all at once.
    /// A linear approximation put the ball visually inside the strike zone for the last 60% of
    /// the flight, so it looked hittable roughly a full second before it actually was, and every
    /// swing came in hopelessly early.
    /// </summary>
    /// <summary>
    /// How far along its screen journey the ball is, at progress <paramref name="t"/>.
    ///
    /// True one-over-distance perspective is what a camera behind the plate would see, and it made
    /// the game close to unplayable. Measured, it put the ball at 11% of its screen travel halfway
    /// through the flight, 34% at four-fifths, and 54% at nine-tenths — so it crossed nearly half
    /// the screen and doubled in size inside the last hundred and forty milliseconds, which is
    /// exactly the moment a hitter has to decide.
    ///
    /// A hitter sees a small dot hang almost still for a second and then explode past him. He
    /// cannot track it, cannot put the bat on it, and cannot time it, and every one of those reads
    /// as a different complaint about hitting when it is one cause.
    ///
    /// Real hitters cope with this because they have two eyes, depth, and a lifetime of reading a
    /// release point. Through a flat screen it is simply unfair, so the curve is flattened toward
    /// linear. The ball still accelerates toward you — it still reads as coming — but it does most
    /// of its travelling where you can see it happen.
    ///
    /// This is a view-side change only. The swing is resolved against the pitch's crossing point
    /// and the progress at which the bat came through, never against a screen position, so no
    /// calibrated rate moves by a thousandth.
    /// </summary>
    private const float PerspectiveRealism = 0.34f;

    public static float Perspective(float t)
    {
        float far = FieldGeometry.MoundDistance + CameraSetback;
        float near = CameraSetback;
        float dist = Mathf.Max(1.5f, Mathf.Lerp(far, near, Mathf.Clamp(t, 0f, 1.35f)));

        float invFar = 1f / far;
        float invNear = 1f / near;
        float trueP = Mathf.Clamp((1f / dist - invFar) / (invNear - invFar), 0f, 1.5f);

        // Blend toward a straight line. Both ends stay pinned — the ball still leaves the hand at
        // the mound and still arrives at the plate exactly on time — only the middle is readable.
        float linear = Mathf.Clamp(t, 0f, 1.35f);
        return Mathf.Lerp(linear, trueP, PerspectiveRealism);
    }

    public override void _Process(double delta) => _time += (float)delta;

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        _zoneCenter = new Vector2(size.X * 0.5f, size.Y * 0.60f);

        DrawBackdrop(size);
        DrawPitcher(size);
        DrawGroundMarkings();
        DrawBatterAndCatcher(size);

        // The zone frame goes on top of the hitter. It is a sighting aid, and an aid you cannot
        // see through your own batter is worse than none.
        DrawZoneFrame();

        if (Scene.CurrentPitch != null) DrawBall();
        if (Scene.HumanBatting) DrawBatCursor();
        if (Scene.HumanPitching && Scene.Phase == AtBatPhase.PitchSelect) DrawPitchAim();

        DrawTimingMeter(size);
    }

    /// <summary>Maps a point in the plate plane (feet) to the screen.</summary>
    private Vector2 ToScreen(Vector2 platePoint) =>
        _zoneCenter + new Vector2(platePoint.X * PixelsPerFoot, -(platePoint.Y - 2.5f) * PixelsPerFoot);

    /// <summary>The inverse: a screen point back into plate-plane feet, for mouse aiming.</summary>
    public Vector2 ScreenToPlate(Vector2 screen)
    {
        // _zoneCenter is set during _Draw; derive it here so this works before the first frame.
        Vector2 size = GetViewportRect().Size;
        var center = new Vector2(size.X * 0.5f, size.Y * 0.60f);
        return new Vector2(
            (screen.X - center.X) / PixelsPerFoot,
            2.5f - (screen.Y - center.Y) / PixelsPerFoot);
    }

    // Horizon lines for the behind-the-plate camera, as fractions of viewport height.
    private const float SkyBottom = 0.20f;
    private const float WallBottom = 0.29f;
    private const float DirtTop = 0.52f;

    private void DrawBackdrop(Vector2 size)
    {
        var park = FieldGeometry.Current;
        float horizon = size.Y * SkyBottom;

        if (park.Covered)
        {
            Scenery.SkyGradient(this, new Rect2(Vector2.Zero, new Vector2(size.X, horizon)),
                new Color("#1b1f27"), new Color("#2b3240"));
        }
        else
        {
            // Sky, sun, clouds, then the neighbourhood the park sits in.
            Scenery.SkyGradient(this, new Rect2(Vector2.Zero, new Vector2(size.X, horizon)),
                new Color("#4d8fc4"), new Color("#a9d4e8"));
            Scenery.Sun(this, new Vector2(size.X * 0.80f, horizon * 0.30f), 26f);
            Scenery.Clouds(this, new Rect2(Vector2.Zero, new Vector2(size.X, horizon * 0.85f)),
                park.TeamId, _time * 0.6f);
            Scenery.Neighbourhood(this, size.X, horizon + 6f, park.TeamId);
        }

        // Outfield grass behind where the wall will sit.
        DrawRect(new Rect2(new Vector2(0f, size.Y * SkyBottom),
            new Vector2(size.X, size.Y * (1f - SkyBottom))), park.Grass);

        // The wall itself, built from this park's real profile.
        DrawOutfieldWall(size, park);

        // Mow stripes fanning out from the mound give the shot its depth.
        for (int i = 0; i < 10; i += 2)
        {
            float t0 = i / 10f;
            float t1 = (i + 1) / 10f;
            float topY = size.Y * WallBottom;
            DrawColoredPolygon(new[]
            {
                new Vector2(Mathf.Lerp(size.X * 0.40f, size.X * 0.60f, t0), topY),
                new Vector2(Mathf.Lerp(size.X * 0.40f, size.X * 0.60f, t1), topY),
                new Vector2(Mathf.Lerp(-size.X * 0.7f, size.X * 1.7f, t1), size.Y),
                new Vector2(Mathf.Lerp(-size.X * 0.7f, size.X * 1.7f, t0), size.Y),
            }, park.GrassAlt);
        }

        // The infield dirt: a shallow arc across the lower half, not a giant disc.
        DrawDirtArc(size, park.Dirt);

        // The mound, sitting on the grass just under the pitcher. It is kept above the strike
        // zone box so the two never sit on top of each other.
        DrawEllipse(new Vector2(size.X * 0.5f, size.Y * 0.405f),
            size.X * 0.10f, size.Y * 0.030f, park.Dirt.Darkened(0.18f));

        // Park name on a hanging sign, so it stays readable against the neighbourhood.
        string sign = park.Name.ToUpperInvariant();
        float sw = Palette.TextWidth(sign, 16) + 34f;
        var signRect = new Rect2(new Vector2(size.X * 0.5f - sw * 0.5f, size.Y * SkyBottom - 30f),
            new Vector2(sw, 26f));
        DrawRect(signRect, park.Wall.Darkened(0.5f));
        DrawRect(signRect, park.WallTrim, false, 2f);
        Palette.TextCentered(this, signRect.Position + signRect.Size * 0.5f, sign, 16,
            new Color(1f, 1f, 1f, 0.92f));
    }

    /// <summary>
    /// Draws the outfield wall from the home park's own five-point profile. Screen x maps to a
    /// spray angle, so a deep alley sits higher up the frame (further away) and a tall wall is
    /// visibly taller. Without this every one of the 32 parks looked identical from the box —
    /// the differences were real in the simulation but invisible where you actually play.
    /// </summary>
    private void DrawOutfieldWall(Vector2 size, Stadium park)
    {
        const int steps = 56;
        const float halfView = Mathf.Pi * 0.25f;   // the full 90 degrees of fair territory

        var top = new Vector2[steps + 1];
        var bottom = new Vector2[steps + 1];

        for (int i = 0; i <= steps; i++)
        {
            float f = i / (float)steps;
            float x = f * size.X;
            // Left of frame is the left-field line.
            float angle = Mathf.Lerp(-halfView, halfView, f);

            float dist = park.DistanceAt(angle);
            float wallFt = park.HeightAt(angle);

            // Deeper fence sits nearer the horizon; 300 ft is low in frame, 440 ft is high.
            float depth = Mathf.InverseLerp(300f, 440f, dist);
            float baseY = size.Y * Mathf.Lerp(0.335f, 0.268f, Mathf.Clamp(depth, 0f, 1f));

            // A wall further away also looks shorter, so scale height by depth as well.
            float px = wallFt * Mathf.Lerp(2.4f, 1.5f, Mathf.Clamp(depth, 0f, 1f));

            bottom[i] = new Vector2(x, baseY);
            top[i] = new Vector2(x, baseY - px);
        }

        // Fill between the two edges.
        var poly = new Vector2[(steps + 1) * 2];
        for (int i = 0; i <= steps; i++)
        {
            poly[i] = top[i];
            poly[poly.Length - 1 - i] = bottom[i];
        }
        // Darkened hard, because a wall painted close to the grass colour disappears into it.
        DrawColoredPolygon(poly, park.Wall.Darkened(0.55f));

        // A bright base line where the wall meets the warning track, so the boundary is legible.
        for (int i = 0; i < steps; i++)
            DrawLine(bottom[i], bottom[i + 1], park.Wall.Lightened(0.25f), 3f);

        // A crowd leaning over the wall, then the padded rail along the top.
        if (!park.Covered)
        {
            float railY = 0f;
            for (int i = 0; i <= steps; i++) railY += top[i].Y;
            Scenery.Crowd(this, size.X, railY / (steps + 1), park.TeamId, _time);
        }

        for (int i = 0; i < steps; i++)
            DrawLine(top[i], top[i + 1], park.WallTrim, 5f);

        // Posted distances at the lines and in centre, painted on the wall.
        foreach (float f in new[] { 0.06f, 0.5f, 0.94f })
        {
            float angle = Mathf.Lerp(-halfView, halfView, f);
            int idx = Mathf.Clamp(Mathf.RoundToInt(f * steps), 0, steps);
            var at = new Vector2(f * size.X, (top[idx].Y + bottom[idx].Y) * 0.5f + 5f);
            Palette.TextCentered(this, at, ((int)park.DistanceAt(angle)).ToString(), 13,
                new Color(1f, 1f, 1f, 0.8f));
        }

        // A roof over a covered park, so a dome reads as a dome.
        if (park.Covered)
        {
            float roofY = size.Y * SkyBottom;
            DrawRect(new Rect2(Vector2.Zero, new Vector2(size.X, roofY)), new Color("#20242c"));
            for (int i = 0; i < 9; i++)
            {
                float x = size.X * (i / 8f);
                DrawLine(new Vector2(size.X * 0.5f, -40f), new Vector2(x, roofY),
                    new Color(1f, 1f, 1f, 0.06f), 3f);
            }
            DrawRect(new Rect2(new Vector2(0f, roofY - 5f), new Vector2(size.X, 5f)),
                park.WallTrim.Darkened(0.3f));
        }
    }

    /// <summary>The infield, drawn as a wide flattened arc so it reads as ground, not a circle.</summary>
    private void DrawDirtArc(Vector2 size, Color dirt)
    {
        const int steps = 40;
        var pts = new Vector2[steps + 3];
        float topY = size.Y * DirtTop;

        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float x = Mathf.Lerp(-size.X * 0.15f, size.X * 1.15f, t);
            // A gentle rise in the middle, flattening toward the edges.
            float lift = Mathf.Sin(t * Mathf.Pi);
            float y = topY + (1f - lift) * size.Y * 0.10f;
            pts[i] = new Vector2(x, y);
        }
        pts[steps + 1] = new Vector2(size.X * 1.15f, size.Y);
        pts[steps + 2] = new Vector2(-size.X * 0.15f, size.Y);

        DrawColoredPolygon(pts, dirt);
    }

    private void DrawEllipse(Vector2 center, float rx, float ry, Color color)
    {
        const int steps = 28;
        var pts = new Vector2[steps];
        for (int i = 0; i < steps; i++)
        {
            float a = i / (float)steps * Mathf.Tau;
            pts[i] = center + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        DrawColoredPolygon(pts, color);
    }

    private void DrawPitcher(Vector2 size)
    {
        var team = Scene.Situation.KitOf(Scene.Situation.FieldingTeam);
        var pitcher = Scene.Situation.FieldingTeam.CurrentPitcher;

        // He stands on top of the mound, sixty feet away, so he is drawn small.
        var feet = new Vector2(size.X * 0.5f, size.Y * 0.40f);
        // He stays in the set position until he actually starts his motion.
        var pose = Scene.Phase == AtBatPhase.PitchSelect && !Scene.Delivering
            ? Pose.Windup : Pose.Pitch;
        // He is looking in at the plate, which from this camera is straight down the screen.
        CartoonPlayer.Draw(this, feet, 0.60f, -1f, pose, team, pitcher, _time,
            motionPhase: Scene.DeliveryPhase,
            lookAt: new Vector2(size.X * 0.5f, size.Y * 0.92f));

        if (Scene.Phase == AtBatPhase.PitchSelect)
        {
            Palette.TextCentered(this, feet - new Vector2(0f, 102f), pitcher.ShortName, 15, Palette.Ink);
            Palette.TextCentered(this, feet - new Vector2(0f, 86f),
                $"VEL {pitcher.PitchPower}  CMD {pitcher.PitchControl}  ·  {Pitch.Label(Scene.SelectedPitch)}",
                12, Palette.InkDim);
        }
    }

    private void DrawBatterAndCatcher(Vector2 size)
    {
        var batTeam = Scene.Situation.KitOf(Scene.Situation.BattingTeam);
        var fieldTeam = Scene.Situation.KitOf(Scene.Situation.FieldingTeam);
        var batter = Scene.Situation.Batter;

        // The batter stands on the side of the plate that matches his handedness, big in the
        // foreground. He is drawn before the catcher so the catcher never covers him.
        float sign = batter.Bats == Handedness.Right ? -1f : 1f;
        var batterAt = BatterStand(sign);
        bool swinging = Scene.SwingFlash > 0f;

        // His chest stays square to the plate — that is what a batting stance is — and his head
        // turns to the pitcher, and then to the ball once it is on its way. Watching the hitter
        // pick the ball up is the single thing that makes him look like he is in the at-bat.
        Vector2 watching = Scene.CurrentPitch != null && Scene.Phase == AtBatPhase.PitchFlight
            ? BallScreenPos()
            : new Vector2(size.X * 0.5f, size.Y * 0.40f);

        CartoonPlayer.Draw(this, batterAt, BatterScale, -sign,
            swinging ? Pose.Swing : Pose.Stance, batTeam, batter, _time,
            withBat: true, motionPhase: Scene.SwingPhase, lookAt: watching);

        // The catcher is nearest the camera: cropped by the bottom edge, framing the shot.
        var catcher = Scene.Situation.FieldingTeam.Fielder(Data.Position.C);
        // Far enough down that only his head and shoulders show, framing the bottom of the shot.
        CartoonPlayer.Draw(this, new Vector2(size.X * 0.5f, size.Y + 112f), 1.55f, 1f,
            Pose.Field, fieldTeam, catcher, _time,
            lookAt: Scene.CurrentPitch != null && Scene.Phase == AtBatPhase.PitchFlight
                ? BallScreenPos()
                : new Vector2(size.X * 0.5f, size.Y * 0.40f));

        // Name plate for the hitter, out to his side so it never covers him or the zone.
        float plateX = batterAt.X + sign * 132f;
        var label = new Rect2(new Vector2(plateX - 118f, batterAt.Y - 232f), new Vector2(236f, 46f));
        DrawRect(label, new Color(0f, 0f, 0f, 0.55f));
        DrawRect(new Rect2(label.Position, new Vector2(5f, label.Size.Y)), batTeam.Secondary);

        Palette.TextCentered(this, label.Position + new Vector2(label.Size.X * 0.5f, 16f),
            batter.ShortName, 16, Palette.Ink);
        Palette.TextCentered(this, label.Position + new Vector2(label.Size.X * 0.5f, 34f),
            batter.Special != Data.Special.None
                ? $"CON {batter.Contact}  POW {batter.Power}  ·  {batter.SpecialText}"
                : $"{batter.PositionText}  ·  CON {batter.Contact}  POW {batter.Power}",
            12, Palette.InkDim);
    }

    private void DrawZoneFrame()
    {
        Vector2 topLeft = ToScreen(new Vector2(-Pitch.ZoneHalfWidth, Pitch.ZoneTop));
        Vector2 bottomRight = ToScreen(new Vector2(Pitch.ZoneHalfWidth, Pitch.ZoneBottom));
        var rect = new Rect2(topLeft, bottomRight - topLeft);

        // The true zone, to the inch.
        //
        // This was drawn at 0.86 of full size — fourteen per cent inside the boundary the umpire
        // actually rules on — on the grounds that at full size it crowded the pitch. That is a
        // reason to draw it more lightly, not to draw it in the wrong place. A hitter judging
        // balls and strikes against this frame was being lied to on every borderline pitch: he
        // laid off something visibly outside the box and was rung up, and there was no way to
        // learn his way out of it, because the thing he was learning was false.
        //
        // Full size, drawn lighter. If it competes with the reticle the answer is a fainter line.
        DrawRect(rect, new Color(1f, 1f, 1f, 0.26f), false, 1.4f);

        float tickX = rect.Size.X * 0.22f, tickY = rect.Size.Y * 0.22f;
        var bright = new Color(1f, 1f, 1f, 0.72f);
        foreach (var (corner, sx, sy) in new[]
        {
            (rect.Position, 1f, 1f),
            (new Vector2(rect.End.X, rect.Position.Y), -1f, 1f),
            (new Vector2(rect.Position.X, rect.End.Y), 1f, -1f),
            (rect.End, -1f, -1f),
        })
        {
            DrawLine(corner, corner + new Vector2(sx * tickX, 0f), bright, 2.4f);
            DrawLine(corner, corner + new Vector2(0f, sy * tickY), bright, 2.4f);
        }

        // Thirds, faint — enough to read location, not enough to draw the eye.
        for (int i = 1; i < 3; i++)
        {
            float fx = rect.Position.X + rect.Size.X * i / 3f;
            float fy = rect.Position.Y + rect.Size.Y * i / 3f;
            DrawLine(new Vector2(fx, rect.Position.Y), new Vector2(fx, rect.End.Y), new Color(1f, 1f, 1f, 0.09f), 1f);
            DrawLine(new Vector2(rect.Position.X, fy), new Vector2(rect.End.X, fy), new Color(1f, 1f, 1f, 0.09f), 1f);
        }

        return;
    }

    /// <summary>Chalk and the plate: painted on the dirt, so they belong under the players.</summary>
    private void DrawGroundMarkings()
    {
        // Chalked batter's boxes either side of the plate, drawn in perspective.
        Vector2 plate = ToScreen(new Vector2(0f, Pitch.ZoneBottom - 0.55f));
        var chalk = new Color(1f, 1f, 1f, 0.42f);
        for (int side = -1; side <= 1; side += 2)
        {
            var box = BatterBox(side);
            for (int i = 0; i < 4; i++)
                DrawLine(box[i], box[(i + 1) % 4], chalk, 3f);
        }

        // Home plate, with a soft shadow so it sits on the dirt.
        DrawColoredPolygon(new[]
        {
            plate + new Vector2(-46f, 4f), plate + new Vector2(46f, 4f),
            plate + new Vector2(34f, 22f), plate + new Vector2(0f, 32f),
            plate + new Vector2(-34f, 22f),
        }, new Color(0f, 0f, 0f, 0.18f));

        DrawColoredPolygon(new[]
        {
            plate + new Vector2(-46f, 0f),
            plate + new Vector2(46f, 0f),
            plate + new Vector2(34f, 18f),
            plate + new Vector2(0f, 28f),
            plate + new Vector2(-34f, 18f),
        }, Palette.Chalk);
    }

    /// <summary>
    /// Where the ball is on screen right now, so the hitter's eyes can be on it. Mirrors the
    /// perspective in <see cref="DrawBall"/> — the ball starts at the release point and travels
    /// out to its crossing position as it approaches.
    /// </summary>
    private Vector2 BallScreenPos()
    {
        var pitch = Scene.CurrentPitch;
        if (pitch == null) return new Vector2(GetViewportRect().Size.X * 0.5f,
                                              GetViewportRect().Size.Y * 0.40f);

        float t = Mathf.Clamp(Scene.PitchProgress, 0f, 1.3f);
        Vector2 target = ToScreen(pitch.PositionAt(t));
        Vector2 release = new(GetViewportRect().Size.X * 0.5f,
                              GetViewportRect().Size.Y * 0.385f);

        return release.Lerp(target, Mathf.Clamp(t, 0f, 1f));
    }

    private void DrawBall()
    {
        var pitch = Scene.CurrentPitch;
        float t = Mathf.Clamp(Scene.PitchProgress, 0f, 1.3f);

        Vector2 platePoint = pitch.PositionAt(t);
        Vector2 target = ToScreen(platePoint);

        // Where the pitch is going to end up.
        //
        // This existed at twenty-six per cent opacity, which is to say it did not exist. Without
        // it a hitter cannot tell a ball from a strike until the pitch has arrived, because for
        // most of the flight the ball is nowhere near its plate position on screen — it converges
        // only at the very end. "I cannot tell what is a strike" is that, exactly.
        //
        // So it is a real mark now, and it appears early enough to be acted on. It says where,
        // never when: the timing still has to come off the ball.
        if (Scene.HumanBatting && t > 0.14f && t < 1.30f)
        {
            Vector2 mark = ToScreen(pitch.CrossPoint);
            float fade = Mathf.Clamp((t - 0.14f) / 0.16f, 0f, 1f);

            var tint = new Color(1f, 1f, 1f, fade * 0.60f);
            DrawArc(mark, 13f, 0f, Mathf.Tau, 24, tint, 2f);
            DrawLine(mark - new Vector2(19f, 0f), mark - new Vector2(7f, 0f), tint, 1.8f);
            DrawLine(mark + new Vector2(7f, 0f), mark + new Vector2(19f, 0f), tint, 1.8f);
            DrawLine(mark - new Vector2(0f, 19f), mark - new Vector2(0f, 7f), tint, 1.8f);
            DrawLine(mark + new Vector2(0f, 7f), mark + new Vector2(0f, 19f), tint, 1.8f);
        }

        // Both the position and the apparent size follow 1 / distance, so the ball reads as
        // genuinely coming at you and its arrival is unmistakable.
        Vector2 release = new(GetViewportRect().Size.X * 0.5f, GetViewportRect().Size.Y * 0.385f);
        float persp = Perspective(t);
        Vector2 at = release.Lerp(target, persp);

        // The ball used to leave the hand at two and a half pixels across, against a crowd, a
        // scoreboard and a grass pattern. That is not a ball you can pick up, and no amount of
        // timing help fixes a pitch you never saw. It starts twice that size now.
        float radius = Mathf.Lerp(5f, 17f, persp);

        // The trail. Longer and far more visible than it was — this is what makes the break
        // readable, and reading the break is the whole skill.
        for (int i = 1; i <= 7; i++)
        {
            float tt = Mathf.Max(0f, t - i * 0.040f);
            float trailP = Perspective(tt);
            Vector2 trailAt = release.Lerp(ToScreen(pitch.PositionAt(tt)), trailP);
            DrawCircle(trailAt, Mathf.Lerp(5f, 17f, trailP) * 0.66f,
                new Color(1f, 1f, 1f, 0.34f * (8 - i) / 7f));
        }

        // An outline, so the ball reads against grass, dirt, crowd and scoreboard alike.
        DrawCircle(at, radius + 2f, new Color(0.06f, 0.07f, 0.09f, 0.85f));
        DrawCircle(at, radius, Palette.Ball);

        // No ring. It was tried and it is not wanted, and on reflection it was the wrong answer to
        // the right problem: a countdown drawn on the ball tells you when to swing without ever
        // teaching you to read a pitch, which is the opposite of what "harder, like The Show"
        // means. The Show has no such thing either.
        //
        // The timing has to be carried by the pitch itself — a ball you can actually see, with a
        // trail long enough to show the break — and by the bracket closing on the reticle. That is
        // what the work above is for.

        // Seams coloured by pitch type. Reading the spin out of the pitcher's hand is a real
        // skill, and this is how a video game makes it legible.
        var seam = pitch.Type switch
        {
            PitchType.Fastball => new Color("#e2453f"),      // red — straight and hard
            PitchType.Curveball => new Color("#4b8ef0"),     // blue — the big drop
            PitchType.Changeup => new Color("#46b566"),      // green — slow
            PitchType.Slider => new Color("#e0b23a"),        // amber — sideways
            PitchType.Sinker => new Color("#e07a3a"),        // orange — a fastball that dies
            PitchType.Cutter => new Color("#c65fd0"),        // violet — a fastball with a wrinkle
            PitchType.Splitter => new Color("#3fc4c0"),      // teal — falls off the table
            PitchType.Knuckler => new Color("#cfd3da"),      // grey — no spin to read at all
            _ => new Color("#e0b23a"),
        };
        if (radius > 5f)
        {
            // The seam pattern rotates as it travels, which sells the spin. A knuckleball barely
            // turns over — that near-stillness is exactly what a hitter is trying to spot.
            float rate = pitch.Type == PitchType.Knuckler ? 1.5f : 22f;
            float spin = t * rate * (pitch.Type is PitchType.Curveball or PitchType.Splitter ? -1f : 1f);
            DrawArc(at, radius * 0.62f, spin + 0.6f, spin + 2.4f, 10, seam, 1.8f);
            DrawArc(at, radius * 0.62f, spin + 3.7f, spin + 5.6f, 10, seam, 1.8f);
        }
    }

    /// <summary>
    /// The hitting reticle. This used to be a filled disc the size of the bat's whole reach, with
    /// a second ring three times wider closing onto it — together they covered most of the strike
    /// zone and buried the pitch. It is now a small bracket on the sweet spot, with the timing
    /// read off a thin ring around it, so you can actually see the ball you are trying to hit.
    /// </summary>
    /// <summary>
    /// How much wider the sweet spot is than it is tall.
    ///
    /// The resolver squeezes horizontal misses — <c>new Vector2(delta.X * 0.72f, delta.Y)</c> —
    /// because a bat is long and thin, so the region you are actually judged against is an ellipse
    /// almost forty per cent wider than it is high. The reticle was drawn as a square. High and
    /// low misses punished you harder than the shape on screen implied and there was no way to
    /// learn that from playing, which is the difference between a hard game and an unfair one.
    /// </summary>
    private const float Wide = 1f / 0.72f;

    /// <summary>An ellipse outline, since Godot's DrawArc only draws circles.</summary>
    private void DrawEllipseOutline(Vector2 centre, float rx, float ry, Color tint, float width)
    {
        const int Steps = 40;
        var points = new Vector2[Steps + 1];
        for (int i = 0; i <= Steps; i++)
        {
            float a = i / (float)Steps * Mathf.Tau;
            points[i] = centre + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        DrawPolyline(points, tint, width);
    }

    private void DrawBatCursor()
    {
        var batter = Scene.Situation.Batter;
        var type = Scene.PendingSwing;

        // Two radii, both taken straight from the resolver rather than picked to look right.
        //
        //   reach  — where the bat touches the ball at all. Widened by difficulty.
        //   r      — where a well-timed swing actually squares it up. NOT widened by difficulty,
        //            because the assist has never made anybody hit the ball harder.
        //
        // The bracket marks the second one, because that is the thing worth aiming at. The first
        // is drawn as a thin outline: a hitter should be able to see what he can reach without it
        // being a disc big enough to bury the pitch behind, which is what it used to be.
        float platoon = Scene.CurrentPitch != null
            ? Platoon.Factor(batter, Scene.CurrentPitch.Pitcher) : 1f;

        float reach = SwingResolver.BarrelRadius(batter, Scene.BatAssist, type) * PixelsPerFoot;
        float r = SwingResolver.SquareUpRadius(batter, type, platoon) * PixelsPerFoot;

        Vector2 at = ToScreen(Scene.BatCursor);

        var tint = type switch
        {
            SwingType.Power => new Color(1f, 0.52f, 0.38f),
            SwingType.Contact => new Color(0.55f, 0.86f, 1f),
            _ => new Color(1f, 0.88f, 0.5f),
        };
        if (Scene.SwingFlash > 0f) tint = Palette.Highlight;

        var pitch = Scene.CurrentPitch;
        bool onTime = false;
        float t = 0f;
        if (pitch != null && !Scene.SwingTaken)
        {
            t = Scene.PitchProgress;
            onTime = Mathf.Abs(t - 1f) <= Scene.TimingWindowSeconds(type) / pitch.FlightTime;
        }

        var live = onTime ? new Color(0.42f, 1f, 0.52f) : tint;

        // The bracket itself is the timing cue: it starts wide and closes onto the sweet spot,
        // arriving exactly at contact. No ring, no circle — the shape you are already watching
        // tells you when to swing.
        float closeR = pitch != null && !Scene.SwingTaken
            ? Mathf.Lerp(r * 2.7f, r, Mathf.Clamp(t, 0f, 1f))
            : r;

        // The bracket. Thicker and darker-backed than it was: a two-pixel line in the club's trim
        // colour over a sunlit outfield is genuinely hard to find, and "aiming is fiddly" is what
        // that looks like from the other side of the screen.
        float arm = r * 0.5f;
        for (int i = 0; i < 4; i++)
        {
            float sx = (i & 1) == 0 ? -1f : 1f;
            float sy = (i & 2) == 0 ? -1f : 1f;
            var corner = at + new Vector2(sx * closeR * Wide, sy * closeR);

            var shadow = new Color(0f, 0f, 0f, 0.55f);
            DrawLine(corner, corner + new Vector2(-sx * arm, 0f), shadow, onTime ? 5.6f : 4.6f);
            DrawLine(corner, corner + new Vector2(0f, -sy * arm), shadow, onTime ? 5.6f : 4.6f);

            DrawLine(corner, corner + new Vector2(-sx * arm, 0f), live, onTime ? 3.4f : 2.6f);
            DrawLine(corner, corner + new Vector2(0f, -sy * arm), live, onTime ? 3.4f : 2.6f);
        }

        // The full reach: where the bat touches the ball at all. This is the ring difficulty
        // actually moves — a contact-7 hitter reaches 95 pixels on Pro and 62 on Simulation — and
        // it was drawn at sixteen per cent opacity, which meant the one part of the reticle that
        // responds to the setting was the one part nobody could see. The bracket inside it is the
        // square-up radius and is deliberately identical at every difficulty, so with the outer
        // ring invisible the whole indicator looked unchanged. It is a real ring now.
        // Four short ticks at the compass points, and nothing else. A full ring at any weight
        // reads as clutter over the pitch — drawn faintly it was invisible, drawn boldly it was
        // worse than invisible. Ticks give the size without putting a second shape between the
        // hitter and the ball, which is the thing he is actually trying to watch.
        for (int i = 0; i < 4; i++)
        {
            bool horizontal = i < 2;
            float sign = (i & 1) == 0 ? -1f : 1f;
            Vector2 edge = horizontal
                ? new Vector2(sign * reach * Wide, 0f)
                : new Vector2(0f, sign * reach);
            Vector2 inward = horizontal
                ? new Vector2(-sign * 9f, 0f)
                : new Vector2(0f, -sign * 9f);
            DrawLine(at + edge, at + edge + inward, new Color(live.R, live.G, live.B, 0.34f), 2f);
        }

        // A faint centre mark so the aim point is unambiguous without covering anything.
        DrawLine(at + new Vector2(-5f, 0f), at + new Vector2(5f, 0f), new Color(live.R, live.G, live.B, 0.85f), 1.6f);
        DrawLine(at + new Vector2(0f, -5f), at + new Vector2(0f, 5f), new Color(live.R, live.G, live.B, 0.85f), 1.6f);

        // Four faint ticks mark where the bracket will land, so "closed" has a visible target.
        if (pitch != null && !Scene.SwingTaken && closeR > r * 1.12f)
        {
            for (int i = 0; i < 4; i++)
            {
                float sx = (i & 1) == 0 ? -1f : 1f;
                float sy = (i & 2) == 0 ? -1f : 1f;
                var mark = at + new Vector2(sx * r * Wide, sy * r);
                DrawLine(mark, mark + new Vector2(-sx * arm * 0.5f, 0f), new Color(1f, 1f, 1f, 0.22f), 1.4f);
                DrawLine(mark, mark + new Vector2(0f, -sy * arm * 0.5f), new Color(1f, 1f, 1f, 0.22f), 1.4f);
            }
        }

        Palette.TextCentered(this, at + new Vector2(0f, r + 34f),
            SwingProfile.Label(type).ToUpperInvariant(), 11, new Color(tint.R, tint.G, tint.B, 0.75f));
    }

    private void DrawPitchAim()
    {
        Vector2 at = ToScreen(Scene.PitchAim);
        DrawArc(at, 16f, 0f, Mathf.Tau, 20, Palette.Accent, 2f);
        DrawLine(at - new Vector2(22f, 0f), at + new Vector2(22f, 0f), Palette.Accent, 1.5f);
        DrawLine(at - new Vector2(0f, 22f), at + new Vector2(0f, 22f), Palette.Accent, 1.5f);
    }

    /// <summary>
    /// Just the control hint. There is deliberately no timing bar: Backyard Baseball never had
    /// one, and a meter at the bottom of the screen pulls the eye away from the ball, which is
    /// the thing you actually need to be watching. The ring converging on the ball does that job.
    /// </summary>
    private void DrawTimingMeter(Vector2 size)
    {
        if (!Scene.HumanBatting) return;

        // Tutorial text that never leaves is clutter. It teaches for the first few pitches, then
        // fades to a single dim line you can still glance at.
        const float Full = 16f, Fade = 6f;
        float fade = Mathf.Clamp(1f - (_time - Full) / Fade, 0f, 1f);

        // Two hands: the mouse aims, the keyboard swings. Asking one hand to track a moving
        // target and hit a beat at the same time is what made this feel awkward.
        if (fade > 0.01f)
        {
            Palette.TextCentered(this, new Vector2(size.X * 0.5f, size.Y - 54f),
                "RIGHT HAND aims with the mouse   ·   LEFT HAND swings on the keyboard",
                15, Palette.Highlight with { A = fade });
        }

        float dim = Mathf.Lerp(0.32f, 1f, fade);
        Palette.TextCentered(this, new Vector2(size.X * 0.5f, size.Y - 34f),
            "SPACE normal  ·  F power  ·  C contact  ·  B bunt      (clicks still work)",
            13, Palette.InkDim with { A = dim });
    }
}
