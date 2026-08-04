using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.UI;

public enum Pose { Idle, Stance, Swing, Windup, Pitch, Run, Field, Cheer }

/// <summary>
/// Draws the sandlot kids: oversized heads, little bodies, chunky limbs and thick outlines.
/// Everything is procedural and tinted per club, so all 32 rosters get distinct-looking players
/// without a single art asset. Appearance is driven by <see cref="PlayerData.LookSeed"/>, so a
/// given kid always looks the same.
/// </summary>
public static class CartoonPlayer
{
    private static readonly Color Outline = new("#20191b");

    private static readonly Color[] SkinTones =
    {
        new("#f6d3b3"), new("#eebd94"), new("#dda172"), new("#c88553"),
        new("#a9663c"), new("#8a4e2c"), new("#6b3a20"), new("#f7dfc6"),
    };

    private static readonly Color[] HairColors =
    {
        new("#2b1b12"), new("#4a2c1a"), new("#7a4a20"), new("#b5762c"),
        new("#d9a441"), new("#e8dcc0"), new("#8c2f1f"), new("#1c1c22"),
        new("#5c5f66"), new("#c2410c"),
    };

    /// <summary>
    /// The hairstyles a generated player can be given.
    ///
    /// This is a men's league, so the ponytail, braids and long styles are left out of the random
    /// draw — with male names attached they read as women, which is not what the roster is. They
    /// still exist and can be set deliberately on a written character.
    /// </summary>
    private static readonly int[] MensHair = { 0, 1, 2, 4, 5, 6, 8, 9, 0, 5, 4 };

    /// <summary>
    /// A hair colour that belongs with a skin tone. Blond on the darkest skin and jet black on the
    /// palest are the combinations that made generated players look assembled rather than drawn.
    /// </summary>
    private static int HairForSkin(int skin, ref Rng rng)
    {
        // HairColors: 0 near-black, 1 dark brown, 2 mid brown, 3 light brown, 4 blond, 5 platinum,
        // 6 auburn, 7 black, 8 grey, 9 ginger.
        int[] pool = skin >= 5
            ? new[] { 0, 0, 1, 7, 7, 8 }                       // darkest tones
            : skin >= 3
                ? new[] { 0, 1, 1, 2, 7, 6, 8 }                // mid tones
                : new[] { 1, 2, 3, 4, 5, 6, 9, 0, 8 };         // fairest tones
        return pool[rng.Range(0, pool.Length)];
    }

    /// <summary>
    /// The middle of a player's build, from what he does on the field. Catchers and corner power
    /// bats carry weight; middle infielders and outfielders are lean; arms are tall.
    /// </summary>
    private static (float Chub, float Height) BuildFor(PlayerData p)
    {
        if (p == null) return (1.0f, 1.0f);

        var (chub, height) = p.Position switch
        {
            Data.Position.C => (1.24f, 0.98f),
            Data.Position.First => (1.20f, 1.10f),
            Data.Position.P => (1.02f, 1.14f),
            Data.Position.Second or Data.Position.Short => (0.86f, 0.98f),
            Data.Position.Center => (0.88f, 1.04f),
            _ => (1.02f, 1.06f),
        };

        // What he is good at pulls it further: a slugger fills out, a burner does not.
        if (p.Power >= 8) chub += 0.12f;
        if (p.Speed >= 8) { chub -= 0.12f; height += 0.03f; }
        if (p.Contact >= 8 && p.Power <= 4) chub -= 0.06f;

        return (chub, height);
    }

    /// <summary>Cuts that still read as a man's when there is no cap covering them.</summary>
    // Was the set of short cuts given to bare-headed players, who no longer exist: everyone is
    // capped now. Kept only so the hairstyle table's indices are not renumbered by its removal.
    private static readonly int[] BareHeadHair = { 0, 5, 9, 4, 5, 0 };

    /// <summary>Everything about how one kid looks, derived once from his seed.</summary>
    private readonly struct Look
    {
        public readonly Color Skin;
        public readonly Color Hair;
        public readonly int HairStyle;   // 0 short, 1 bowl, 2 curly, 3 ponytail, 4 spiky,
                                         // 5 buzz, 6 afro, 7 braids, 8 mohawk, 9 bald, 10 long
        public readonly float HeadWidth;
        public readonly float HeadSize;  // overall head scale, on top of width
        public readonly float Chub;      // torso width
        public readonly float Height;    // leg and body length
        public readonly bool Freckles;
        public readonly int Mood;
        public readonly bool Glasses;
        public readonly bool Headband;
        public readonly bool EyeBlack;
        public readonly bool Wristbands;
        public readonly int EyeStyle;    // 0 round, 1 wide, 2 narrow
        public readonly int Ears;        // 0 normal, 1 big

        /// <summary>0 forward, 1 backwards, 2 no cap. A cap covered every hairstyle in the game.</summary>
        public readonly int CapStyle;
        public readonly int BrowStyle;   // 0 flat, 1 thick, 2 thin, 3 angled
        public readonly int NoseStyle;   // 0 button, 1 wide, 2 long
        public readonly int MouthStyle;  // 0 smile, 1 grin, 2 straight, 3 smirk
        public readonly float EyeSpacing;
        public readonly float EyeSizeMul;

        /// <summary>The seed itself, so drawn outlines can wobble consistently.</summary>
        public readonly int Seed;

        public Look(int seed, Data.LookSpec? spec = null, PlayerData player = null)
        {
            Seed = seed;
            var rng = new Rng(seed);

            // Skin first, then a hair colour that goes with it. Rolling the two independently
            // produced combinations no illustrator would draw — the written players read better
            // than generated ones largely because their looks were composed rather than rolled.
            int skin = rng.Range(0, SkinTones.Length);
            Skin = SkinTones[skin];
            Hair = HairColors[HairForSkin(skin, ref rng)];
            HairStyle = MensHair[rng.Range(0, MensHair.Length)];

            // Build follows the player rather than floating free. A catcher is not shaped like a
            // centre fielder, and the hand-written characters look deliberate precisely because
            // their proportions match who they are.
            var (chubMid, tallMid) = BuildFor(player);
            HeadWidth = rng.Range(0.86f, 1.18f);
            HeadSize = rng.Range(0.84f, 1.20f);
            Chub = Mathf.Clamp(chubMid + (rng.Bell() - 0.5f) * 0.34f, 0.68f, 1.46f);
            Height = Mathf.Clamp(tallMid + (rng.Bell() - 0.5f) * 0.26f, 0.78f, 1.28f);

            Freckles = rng.Chance(0.28f);
            Mood = rng.Range(0, 4);
            Glasses = rng.Chance(0.16f);
            Headband = rng.Chance(0.14f);
            EyeBlack = rng.Chance(0.22f);
            Wristbands = rng.Chance(0.35f);
            EyeStyle = rng.Range(0, 3);
            Ears = rng.Chance(0.25f) ? 1 : 0;

            // Everybody wears the cap — forward, or turned around.
            //
            // Roughly one player in twelve used to be given a bare head, which was meant as
            // character and read on the field as a man who had not finished getting dressed. A
            // ballclub is nine men in the same uniform; that is most of what makes them a club
            // rather than nine people. The hairstyle still shows under and around the cap, so
            // nothing is lost but the odd man out.
            CapStyle = rng.NextFloat() < 0.80f ? 0 : 1;

            BrowStyle = rng.Range(0, 4);
            NoseStyle = rng.Range(0, 3);
            MouthStyle = rng.Range(0, 4);
            EyeSpacing = rng.Range(0.86f, 1.20f);
            EyeSizeMul = rng.Range(0.80f, 1.28f);

            if (spec is not { } k) return;

            if (k.Skin >= 0) Skin = SkinTones[k.Skin % SkinTones.Length];
            if (k.Hair >= 0) Hair = HairColors[k.Hair % HairColors.Length];
            if (k.HairStyle >= 0) HairStyle = k.HairStyle;
            // A written player's look is honoured, except that nobody goes out bare-headed.
            if (k.Cap >= 0) CapStyle = Mathf.Min(k.Cap, 1);
            if (k.Ears >= 0) Ears = k.Ears;
            if (k.Chub > 0f) Chub = k.Chub;
            if (k.Height > 0f) Height = k.Height;
            if (k.HeadSize > 0f) HeadSize = k.HeadSize;
            if (k.HeadWidth > 0f) HeadWidth = k.HeadWidth;
            if (k.Glasses >= 0) Glasses = k.Glasses == 1;
            if (k.Freckles >= 0) Freckles = k.Freckles == 1;
            if (k.Headband >= 0) Headband = k.Headband == 1;
        }
    }

    /// <summary>
    /// Draws a kid standing on <paramref name="feet"/>. <paramref name="scale"/> is roughly the
    /// height in pixels divided by 100. <paramref name="facing"/> is +1 for right, -1 for left.
    /// </summary>
    /// <param name="withBat">
    /// Draws the bat from the hands the arms actually ended up in. It used to be positioned
    /// independently from the shoulder, so once the arms bent at the elbow the bat floated free
    /// of the grip.
    /// </param>
    /// <param name="motionPhase">
    /// 0 at the start of a swing or a pitching delivery, 1 at the end of the follow-through.
    /// Both used to be single frozen poses, which is why neither read as motion — nothing
    /// actually moved between them.
    /// </param>
    /// <param name="lookAt">
    /// A point on screen this player's eyes should be on — the ball, the plate, the man he is
    /// about to throw to.
    ///
    /// A face was always drawn front-on, with two eyes, but the irises pointed wherever the *body*
    /// pointed. So a hitter stood side-on to the plate as he should and stared off toward the
    /// dugout, and a fielder tracked nothing at all. Where a man is looking is most of what makes
    /// him seem to be paying attention, and it is separate from which way he is standing: a hitter
    /// keeps his chest to the plate and turns his head to the pitcher, which is the whole shape of
    /// a batting stance.
    ///
    /// Null keeps the old behaviour — eyes follow the body.
    /// </param>
    public static void Draw(CanvasItem c, Vector2 feet, float scale, float facing,
        Pose pose, TeamData team, PlayerData player, float time, bool withBat = false,
        float motionPhase = 0f, Vector2? lookAt = null)
    {
        // A named kid gets the look he was written with; everyone else gets one from his seed.
        var look = player is { LegendId: >= 0 }
            ? new Look(player.LookSeed, Legends.Spec(player.LegendId), player)
            : new Look(player?.LookSeed ?? 0, null, player);
        float s = scale;

        // A constant gentle bob keeps everyone feeling alive, at a rate that varies per kid.
        float phase = (player?.LookSeed ?? 0) % 100;
        float bob = Mathf.Sin(time * (2.6f + (phase % 13) * 0.09f) + phase) * 0.9f * s;

        // Proportions: head roughly 40% of total height, with a solid torso and short thick
        // limbs under it. A big head on a narrow, stubby body reads as broken rather than cute.
        Vector2 hip = feet + new Vector2(0f, -38f * s * look.Height + bob);
        Vector2 shoulder = hip + new Vector2(0f, -30f * s * look.Height);
        Vector2 head = shoulder + new Vector2(facing * 1.5f * s, -26f * s * look.HeadSize);

        // Poses lean the whole body, which reads better than moving limbs alone. A swing rotates
        // through: loaded back at the start, open and out over the front foot at the finish.
        float lean = pose switch
        {
            Pose.Swing => Mathf.Lerp(-facing * 0.16f, facing * 0.40f, SwingEase(motionPhase)),
            Pose.Pitch => Mathf.Lerp(-facing * 0.22f, facing * 0.46f, PitchEase(motionPhase)),
            Pose.Windup => -facing * 0.18f,
            Pose.Run => facing * 0.30f,
            Pose.Field => 0.10f,
            _ => 0f,
        };
        head += new Vector2(lean * 26f * s, 0f);
        shoulder += new Vector2(lean * 10f * s, 0f);

        DrawShadow(c, feet, s, look);
        DrawLegs(c, hip, feet, s, facing, pose, team, time, look.Seed, motionPhase);
        DrawTorso(c, shoulder, hip, s, look, team, player);

        // The bat goes on before the hands so the fingers close over the handle.
        var (backHand, frontHand) = HandPositions(shoulder, s, facing, pose, look, time, motionPhase);
        if (withBat) DrawBatInHands(c, backHand, frontHand, s, facing, pose, motionPhase);

        DrawArms(c, shoulder, s, facing, pose, look, team, time, motionPhase);
        DrawHead(c, head, s, facing, lookAt, look, team, pose, time);
    }

    private static void DrawShadow(CanvasItem c, Vector2 feet, float s, Look look)
    {
        // A squashed ellipse, drawn as a scaled circle would be, using a polygon.
        const int steps = 16;
        var pts = new Vector2[steps];
        for (int i = 0; i < steps; i++)
        {
            float a = i / (float)steps * Mathf.Tau;
            pts[i] = feet + new Vector2(Mathf.Cos(a) * 17f * s * look.Chub, Mathf.Sin(a) * 5f * s);
        }
        c.DrawColoredPolygon(pts, new Color(0f, 0f, 0f, 0.22f));
    }

    private static void DrawLegs(CanvasItem c, Vector2 hip, Vector2 feet, float s, float facing,
        Pose pose, TeamData team, float time, int seed, float phase = 0f)
    {
        float stride = pose == Pose.Run ? Mathf.Sin(time * 12f) * 11f * s : 0f;
        float spread = pose is Pose.Stance or Pose.Field ? 9f * s : 6f * s;

        // A delivery: the lead leg lifts, then strides out toward the plate.
        float lift = 0f, strideOut = 0f;
        if (pose == Pose.Pitch)
        {
            float e = PitchEase(phase);
            lift = e < 0.14f ? e / 0.14f : Mathf.Max(0f, 1f - (e - 0.14f) / 0.5f);
            strideOut = Mathf.Clamp((e - 0.10f) / 0.7f, 0f, 1f);
        }

        var pantColor = new Color("#eceadf");
        for (int side = -1; side <= 1; side += 2)
        {
            float dx = side * spread + (side > 0 ? stride : -stride);
            float footY = feet.Y - (pose == Pose.Run ? Mathf.Abs(stride) * 0.35f : 0f);

            // The lead leg is the one on the side he is facing.
            bool lead = pose == Pose.Pitch && Mathf.Sign(side) == Mathf.Sign(facing == 0f ? 1f : facing);
            if (lead)
            {
                dx += facing * strideOut * 26f * s;
                footY -= lift * 34f * s;
            }

            Vector2 foot = new(feet.X + dx * 0.9f, footY);
            Vector2 knee = hip.Lerp(foot, 0.5f) + new Vector2(side * 2f * s, 0f);

            // Thick legs — thin ones under a big head look like a spider.
            Limb(c, hip + new Vector2(side * 6f * s, 0f), knee, 6.5f * s, pantColor, s, seed + side * 7 + 10);
            Limb(c, knee, foot, 5.5f * s, pantColor, s, seed + side * 7 + 20);

            DrawShoe(c, foot, s, facing, team, seed + side * 7 + 40);
        }
    }

    /// <summary>
    /// A chunky cartoon cleat. The old shoe was an untextured four-point rectangle, which read as
    /// a paper tab stuck on the ankle — everything else about these kids is rounded and outlined.
    /// </summary>
    private static void DrawShoe(CanvasItem c, Vector2 foot, float s, float facing, TeamData team,
        int seed)
    {
        float f = facing >= 0f ? 1f : -1f;
        Vector2 heel = foot + new Vector2(-f * 5.5f * s, -1.5f * s);
        Vector2 toe = foot + new Vector2(f * 10.5f * s, -0.5f * s);

        // Sole first, slightly longer and lower than the upper so it reads as a separate slab.
        var soleColour = new Color("#2b2f36");
        Ink.Shape(c, Ink.Capsule(heel + new Vector2(0f, 3.4f * s), toe + new Vector2(f * 1.5f * s, 3.4f * s),
            3.6f * s, 8), soleColour, Outline, 1.6f * s, seed);

        // The upper, in the club's colour.
        Ink.Shape(c, Ink.Capsule(heel, toe, 5.4f * s, 9), team.Secondary, Outline, 2f * s, seed + 1);

        // A white toe cap and a couple of laces, so it is not one flat colour.
        c.DrawColoredPolygon(Ink.Capsule(toe + new Vector2(-f * 1.5f * s, 0.5f * s),
            toe + new Vector2(f * 0.5f * s, 0.5f * s), 4.2f * s, 7), new Color("#f2efe6", 0.92f));

        for (int i = 0; i < 2; i++)
        {
            Vector2 mid = heel.Lerp(toe, 0.34f + i * 0.22f);
            Ink.Line(c, mid + new Vector2(0f, -3.6f * s), mid + new Vector2(0f, 1.2f * s),
                new Color("#f4f1e8"), 1.5f * s, seed + 10 + i, 0.6f);
        }

        // Studs under the sole.
        for (int i = 0; i < 3; i++)
        {
            Vector2 stud = heel.Lerp(toe, 0.12f + i * 0.36f) + new Vector2(0f, 6.4f * s);
            c.DrawCircle(stud, 1.5f * s, soleColour);
        }
    }

    private static void DrawTorso(CanvasItem c, Vector2 shoulder, Vector2 hip, float s,
        Look look, TeamData team, PlayerData player)
    {
        // A rounded torso rather than a quad — a four-point box reads as cardboard.
        float w = 20f * s * look.Chub;
        Vector2 chest = shoulder + new Vector2(0f, 4f * s);
        Vector2 waist = hip + new Vector2(0f, 2f * s);
        var body = Ink.Capsule(chest, waist, w * 0.92f, 9);
        Ink.Shape(c, body, team.Primary, Outline, 2.2f * s, look.Seed + 1);

        // Shading down one side and a highlight on the other, so it has volume.
        c.DrawColoredPolygon(
            Ink.Capsule(chest + new Vector2(w * 0.42f, 0f), waist + new Vector2(w * 0.38f, 0f),
                w * 0.48f, 7),
            new Color(0f, 0f, 0f, 0.15f));
        c.DrawColoredPolygon(
            Ink.Capsule(chest + new Vector2(-w * 0.46f, 0f), waist + new Vector2(-w * 0.44f, 0f),
                w * 0.30f, 7),
            new Color(1f, 1f, 1f, 0.10f));

        // A short neck, so the head is not balanced straight on the shoulders.
        c.DrawColoredPolygon(
            Ink.Capsule(shoulder + new Vector2(0f, -2f * s), shoulder + new Vector2(0f, 6f * s),
                7.5f * s, 6), look.Skin.Darkened(0.12f));

        // Jersey placket and number.
        float torsoH = hip.Y - shoulder.Y;
        c.DrawRect(new Rect2(shoulder + new Vector2(-1.8f * s, 2f * s), new Vector2(3.6f * s, torsoH)),
            team.Secondary);

        if (player != null && s > 0.5f)
        {
            string num = player.Number.ToString();
            int size = Mathf.Max(8, (int)(16f * s));
            Palette.TextCentered(c, shoulder + new Vector2(w * 0.42f, torsoH * 0.62f), num, size,
                team.Secondary);
        }
    }

    /// <summary>
    /// Where the two hands end up for a pose. Shared by the arms and the bat so they can never
    /// disagree about where the grip is.
    /// </summary>
    /// <summary>
    /// Swing timing. Slow to load, explosive through the zone, decelerating into the
    /// follow-through — a linear sweep looks like a windscreen wiper, not a swing.
    /// </summary>
    private static float SwingEase(float p)
    {
        p = Mathf.Clamp(p, 0f, 1f);
        if (p < 0.22f) return Mathf.Lerp(0f, 0.08f, p / 0.22f);          // load
        if (p < 0.55f) return Mathf.Lerp(0.08f, 0.72f, (p - 0.22f) / 0.33f);  // fire
        return Mathf.Lerp(0.72f, 1f, (p - 0.55f) / 0.45f);              // follow through
    }

    /// <summary>
    /// Delivery timing: a slow gather and leg lift, then everything happens at once through
    /// release, then a long relaxed follow-through.
    /// </summary>
    private static float PitchEase(float p)
    {
        p = Mathf.Clamp(p, 0f, 1f);
        if (p < 0.40f) return Mathf.Lerp(0f, 0.14f, p / 0.40f);              // gather and lift
        if (p < 0.62f) return Mathf.Lerp(0.14f, 0.78f, (p - 0.40f) / 0.22f); // stride and whip
        return Mathf.Lerp(0.78f, 1f, (p - 0.62f) / 0.38f);                   // follow through
    }

    /// <summary>
    /// The throwing hand traces a real arm path: down by the hip, swept back and up behind the
    /// ear, over the top to release out front, then across the body.
    /// </summary>
    private static (Vector2 Back, Vector2 Front) PitchHands(Vector2 shoulder, float s, float facing,
        float phase)
    {
        Vector2 gather = shoulder + new Vector2(-facing * 8f * s, 8f * s);
        // The hand has to clear the head, or the arm reads as tucked against the chest — that
        // overhead moment is the most recognisable frame of a delivery.
        Vector2 cocked = shoulder + new Vector2(-facing * 27f * s, -37f * s);
        Vector2 release = shoulder + new Vector2(facing * 33f * s, -22f * s);
        Vector2 finish = shoulder + new Vector2(facing * 18f * s, 16f * s);

        float p = Mathf.Clamp(phase, 0f, 1f);
        Vector2 hand =
            p < 0.40f ? gather.Lerp(cocked, p / 0.40f) :
            p < 0.62f ? cocked.Lerp(release, (p - 0.40f) / 0.22f) :
                        release.Lerp(finish, (p - 0.62f) / 0.38f);

        // The glove hand mirrors him: out front early for balance, tucked in after release.
        Vector2 glove = p < 0.55f
            ? shoulder + new Vector2(facing * 20f * s, -6f * s)
            : shoulder + new Vector2(-facing * 14f * s, 6f * s);

        return (hand, glove);
    }

    /// <summary>Where the hands are at a given point in the swing.</summary>
    private static (Vector2 Back, Vector2 Front) SwingHands(Vector2 shoulder, float s, float facing,
        float phase)
    {
        float e = SwingEase(phase);

        // The hands travel from cocked behind the ear, down through the zone, and out front.
        Vector2 cocked = shoulder + new Vector2(-facing * 19f * s, -19f * s);
        Vector2 contact = shoulder + new Vector2(facing * 20f * s, 2f * s);
        Vector2 finish = shoulder + new Vector2(facing * 30f * s, -14f * s);

        Vector2 grip = e < 0.72f
            ? cocked.Lerp(contact, e / 0.72f)
            : contact.Lerp(finish, (e - 0.72f) / 0.28f);

        return (grip + new Vector2(facing * 4f * s, -3f * s), grip);
    }

    private static (Vector2 Back, Vector2 Front) HandPositions(
        Vector2 shoulder, float s, float facing, Pose pose, Look look, float time,
        float motionPhase = 0f) => pose switch
    {
        // Batting poses keep both hands close together, because they are gripping one bat.
        Pose.Swing => SwingHands(shoulder, s, facing, motionPhase),
        Pose.Stance => (shoulder + new Vector2(-facing * 17f * s, -17f * s),
                        shoulder + new Vector2(-facing * 11f * s, -10f * s)),

        // Set position: hands together at the belt, with a gentle rock.
        Pose.Windup => (shoulder + new Vector2(-facing * 5f * s, 9f * s + Mathf.Sin(time * 2.2f) * 1.5f * s),
                        shoulder + new Vector2(-facing * 11f * s, 9f * s)),
        Pose.Pitch => PitchHands(shoulder, s, facing, motionPhase),
        Pose.Run => (shoulder + new Vector2(Mathf.Sin(time * 12f) * 18f * s, 6f * s),
                     shoulder + new Vector2(-Mathf.Sin(time * 12f) * 18f * s, 6f * s)),
        Pose.Field => (shoulder + new Vector2(-16f * s, 16f * s),
                       shoulder + new Vector2(16f * s, 16f * s)),
        Pose.Cheer => (shoulder + new Vector2(-20f * s, -26f * s),
                       shoulder + new Vector2(20f * s, -26f * s)),
        _ => (shoulder + new Vector2(-18f * s, 14f * s),
              shoulder + new Vector2(18f * s, 14f * s)),
    };

    /// <summary>The bat, running through both hands and out over the shoulder or the plate.</summary>
    /// <summary>The bat's angle at a point in the swing, in radians, in screen space.</summary>
    private static float BatAngle(float facing, float phase)
    {
        // Cocked up and behind, roughly level through contact, wrapping across on the
        // follow-through. About 150 degrees of sweep.
        const float cocked = -1.99f;
        const float finish = 0.62f;
        float a = Mathf.Lerp(cocked, finish, SwingEase(phase));
        return facing >= 0f ? a : Mathf.Pi - a;   // mirror for a hitter facing the other way
    }

    private static void DrawBatInHands(CanvasItem c, Vector2 back, Vector2 front, float s,
        float facing, Pose pose, float phase = 0f)
    {
        Vector2 grip = back.Lerp(front, 0.5f);

        if (pose != Pose.Swing)
        {
            DrawBatShape(c, grip, new Vector2(-facing * 0.40f, -0.92f).Normalized(), s);
            return;
        }

        float angle = BatAngle(facing, phase);
        var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        // Motion trail along the arc already travelled. This, more than the pose itself, is
        // what makes a swing read as fast rather than as a frozen picture of a swing.
        for (int i = 4; i >= 1; i--)
        {
            float ghost = phase - i * 0.06f;
            if (ghost <= 0f) continue;
            float ga = BatAngle(facing, ghost);
            c.DrawLine(grip, grip + new Vector2(Mathf.Cos(ga), Mathf.Sin(ga)) * 66f * s,
                new Color(1f, 1f, 1f, 0.05f * (5 - i)), 7f * s);
        }

        float from = BatAngle(facing, Mathf.Max(0f, phase - 0.32f));
        c.DrawArc(grip, 60f * s, Mathf.Min(from, angle), Mathf.Max(from, angle), 22,
            new Color(1f, 1f, 1f, 0.20f), 6f * s);

        DrawBatShape(c, grip, dir, s);
    }

    /// <summary>The bat itself: tapered, thin at the handle and fat at the barrel.</summary>
    private static void DrawBatShape(CanvasItem c, Vector2 grip, Vector2 dir, float s)
    {
        Vector2 knob = grip - dir * 9f * s;
        Vector2 tip = grip + dir * 68f * s;
        Vector2 mid = grip.Lerp(tip, 0.52f);

        c.DrawColoredPolygon(Ink.Capsule(knob, mid, 3.6f * s, 6), Outline);
        c.DrawColoredPolygon(Ink.Capsule(mid, tip, 5.6f * s, 7), Outline);
        c.DrawColoredPolygon(Ink.Capsule(knob, mid, 2.5f * s, 6), new Color("#c98b48"));
        c.DrawColoredPolygon(Ink.Capsule(mid, tip, 4.4f * s, 7), new Color("#dda86a"));
        c.DrawCircle(knob, 4.2f * s, new Color("#3b2a1a"));
    }

    private static void DrawArms(CanvasItem c, Vector2 shoulder, float s, float facing,
        Pose pose, Look look, TeamData team, float time, float phase = 0f)
    {
        Vector2 lShoulder = shoulder + new Vector2(-17f * s * look.Chub, 3f * s);
        Vector2 rShoulder = shoulder + new Vector2(17f * s * look.Chub, 3f * s);

        // The phase has to reach here too, or the arms stay frozen while the bat swings.
        var (back, front) = HandPositions(shoulder, s, facing, pose, look, time, phase);

        // Arms bend at the elbow rather than running straight from shoulder to hand.
        Vector2 lElbow = lShoulder.Lerp(back, 0.5f) + new Vector2(-4f * s, 5f * s);
        Vector2 rElbow = rShoulder.Lerp(front, 0.5f) + new Vector2(4f * s, 5f * s);

        Limb(c, lShoulder, lElbow, 5.6f * s, team.Primary, s, look.Seed + 30);
        Limb(c, lElbow, back, 5.0f * s, look.Skin, s, look.Seed + 31);
        Limb(c, rShoulder, rElbow, 5.6f * s, team.Primary, s, look.Seed + 32);
        Limb(c, rElbow, front, 5.0f * s, look.Skin, s, look.Seed + 33);

        if (look.Wristbands)
        {
            c.DrawCircle(back.Lerp(lElbow, 0.25f), 6f * s, team.Secondary);
            c.DrawCircle(front.Lerp(rElbow, 0.25f), 6f * s, team.Secondary);
        }

        // Hands.
        Ink.Shape(c, Ink.Blob(back, 6.4f * s, 5.8f * s, 10), look.Skin, Outline, 1.8f * s, look.Seed + 2);
        Ink.Shape(c, Ink.Blob(front, 6.4f * s, 5.8f * s, 10), look.Skin, Outline, 1.8f * s, look.Seed + 3);

        // A glove on the fielding hand.
        if (pose is Pose.Field or Pose.Windup or Pose.Pitch)
        {
            Vector2 gloveAt = pose == Pose.Pitch ? front : back;
            c.DrawCircle(gloveAt, 8f * s, new Color("#8a5a2b"));
            c.DrawArc(gloveAt, 8f * s, 0f, Mathf.Tau, 14, Outline, 1.6f * s);
        }
    }

    private static void DrawHead(CanvasItem c, Vector2 head, float s, float facing, Vector2? lookAt,
        Look look, TeamData team, Pose pose, float time)
    {
        // HeadWidth used to multiply into r alongside HeadSize, which scaled both axes — so it
        // was a second size knob, not a width knob, and every head in the league was the same
        // shape. Width is now its own horizontal radius, giving narrow and broad faces.
        float r = 24f * s * look.HeadSize;
        float rx = r * look.HeadWidth;

        if (look.Ears == 1)
        {
            c.DrawCircle(head + new Vector2(-rx * 0.98f, r * 0.06f), r * 0.26f, look.Skin);
            c.DrawCircle(head + new Vector2(rx * 0.98f, r * 0.06f), r * 0.26f, look.Skin);
        }

        // Neck.
        Stroke(c, head + new Vector2(0f, r * 0.7f), head + new Vector2(0f, r * 1.15f), 8f * s, look.Skin);

        // The head — slightly egg-shaped rather than a perfect circle, and hand-outlined.
        var skull = Ink.Blob(head, rx * 1.02f, r * 1.06f, 22);
        Ink.Shape(c, skull, look.Skin, Outline, 2.3f * s, look.Seed + 4);

        // A crescent of shade on the far side, and a highlight on the lit side.
        c.DrawCircle(head + new Vector2(r * 0.24f, r * 0.14f), r * 0.90f,
            new Color(0f, 0f, 0f, 0.10f));
        c.DrawCircle(head + new Vector2(-r * 0.32f, -r * 0.32f), r * 0.32f,
            new Color(1f, 1f, 1f, 0.14f));

        // Cheeks give the face some warmth.
        c.DrawCircle(head + new Vector2(-rx * 0.54f, r * 0.32f), r * 0.17f,
            new Color(0.86f, 0.48f, 0.42f, 0.10f));
        c.DrawCircle(head + new Vector2(rx * 0.54f, r * 0.32f), r * 0.17f,
            new Color(0.86f, 0.48f, 0.42f, 0.10f));

        DrawHair(c, head, rx, r, s, look);
        // Where the eyes go. Normalised and gently clamped, so a man looking at something behind
        // him turns his eyes as far as they go rather than rolling them out of his head.
        Vector2 gaze = new(facing, 0f);
        if (lookAt is { } target)
        {
            Vector2 d = target - head;
            if (d.LengthSquared() > 1f)
            {
                d = d.Normalized();
                gaze = new Vector2(Mathf.Clamp(d.X * 1.6f, -1f, 1f), Mathf.Clamp(d.Y * 1.1f, -0.8f, 0.8f));
            }
        }

        DrawFace(c, head, rx, r, s, facing, look, pose, time, gaze);
        DrawCap(c, head, rx, r, s, facing, team, look);

        // Drawn last, on purpose. Everything DrawHair puts on the crown is painted over by the
        // cap, which left every kid in the game bare-headed under identical headwear — the single
        // biggest reason they all looked alike.
        if (look.CapStyle != 2) DrawHairBelowCap(c, head, rx, r, s, look);
    }

    private static void DrawHair(CanvasItem c, Vector2 head, float rx, float r, float s, Look look)
    {
        switch (look.HairStyle)
        {
            case 1: // bowl cut
                c.DrawArc(head, rx * 0.98f, Mathf.Pi, Mathf.Tau, 20, look.Hair, 9f * s);
                break;
            case 2: // curls
                for (int i = 0; i < 6; i++)
                {
                    float a = Mathf.Pi + i / 5f * Mathf.Pi;
                    c.DrawCircle(head + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * r) * 0.92f, 7f * s, look.Hair);
                }
                break;
            case 3: // ponytail
                c.DrawArc(head, rx * 0.98f, Mathf.Pi, Mathf.Tau, 20, look.Hair, 8f * s);
                c.DrawCircle(head + new Vector2(-rx * 0.95f, r * 0.25f), 9f * s, look.Hair);
                c.DrawCircle(head + new Vector2(-rx * 1.25f, r * 0.6f), 7f * s, look.Hair);
                break;
            case 4: // spiky
                for (int i = 0; i < 5; i++)
                {
                    float t = i / 4f;
                    Vector2 baseAt = head + new Vector2(Mathf.Lerp(-rx * 0.75f, rx * 0.75f, t), -r * 0.68f);
                    c.DrawColoredPolygon(new[]
                    {
                        baseAt + new Vector2(-5f * s, 4f * s),
                        baseAt + new Vector2(5f * s, 4f * s),
                        baseAt + new Vector2(0f, -11f * s),
                    }, look.Hair);
                }
                break;
            case 5: // buzz
                c.DrawArc(head, rx * 0.94f, Mathf.Pi * 1.1f, Mathf.Tau * 0.95f, 18, look.Hair, 5f * s);
                break;
            case 6: // afro — a dome on top, not a mass around the face
                for (int i = 0; i < 9; i++)
                {
                    float a = Mathf.Pi * 1.0f + i / 8f * Mathf.Pi * 1.0f;
                    c.DrawCircle(head + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * r) * 1.02f,
                        10f * s, look.Hair);
                }
                break;
            case 7: // braids
                c.DrawArc(head, rx * 0.96f, Mathf.Pi, Mathf.Tau, 20, look.Hair, 8f * s);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 root = head + new Vector2(side * rx * 0.92f, -r * 0.10f);
                    c.DrawCircle(root, 6f * s, look.Hair);
                    c.DrawCircle(root + new Vector2(side * 4f * s, 12f * s), 5.5f * s, look.Hair);
                    c.DrawCircle(root + new Vector2(side * 7f * s, 23f * s), 5f * s, look.Hair);
                }
                break;
            case 8: // mohawk
                for (int i = 0; i < 5; i++)
                {
                    float t = i / 4f;
                    float a = Mathf.Pi * 1.15f + t * Mathf.Pi * 0.7f;
                    Vector2 baseAt = head + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * r) * 0.92f;
                    c.DrawLine(baseAt, baseAt + new Vector2(0f, -14f * s), look.Hair, 8f * s);
                }
                break;
            case 9: // bald, just a shine
                c.DrawArc(head + new Vector2(-rx * 0.25f, -r * 0.3f), r * 0.42f,
                    Mathf.Pi * 1.15f, Mathf.Pi * 1.62f, 10, new Color(1f, 1f, 1f, 0.30f), 3f * s);
                break;
            case 10: // long
                c.DrawArc(head, rx * 0.98f, Mathf.Pi, Mathf.Tau, 22, look.Hair, 9f * s);
                for (int side = -1; side <= 1; side += 2)
                    c.DrawColoredPolygon(new[]
                    {
                        head + new Vector2(side * rx * 0.96f, -r * 0.3f),
                        head + new Vector2(side * rx * 1.16f, r * 0.75f),
                        head + new Vector2(side * rx * 0.62f, r * 0.80f),
                        head + new Vector2(side * rx * 0.70f, -r * 0.2f),
                    }, look.Hair);
                break;
            default: // short, with a bit of fringe
                c.DrawArc(head, rx * 0.96f, Mathf.Pi, Mathf.Tau, 20, look.Hair, 8f * s);
                c.DrawCircle(head + new Vector2(rx * 0.42f, -r * 0.52f), 7f * s, look.Hair);
                break;
        }
    }

    /// <summary>
    /// The part of a hairstyle that shows around and below a cap: sideburns, the mass at the back
    /// and sides, and whatever hangs down. Without this the cap erases the hairstyle entirely.
    /// </summary>
    private static void DrawHairBelowCap(CanvasItem c, Vector2 head, float rx, float r, float s, Look look)
    {
        var hair = look.Hair;

        // Sideburns, on everyone who is not shaved.
        if (look.HairStyle != 9)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float len = look.HairStyle is 5 ? 0.10f : 0.26f;
                c.DrawColoredPolygon(new[]
                {
                    head + new Vector2(side * rx * 0.99f, -r * 0.30f),
                    head + new Vector2(side * rx * 1.04f, -r * 0.30f),
                    head + new Vector2(side * rx * 0.96f, r * len),
                    head + new Vector2(side * rx * 0.86f, r * (len - 0.06f)),
                }, hair);
            }
        }

        switch (look.HairStyle)
        {
            case 2:   // a little curl showing at the temple, and no lower
                for (int side = -1; side <= 1; side += 2)
                    c.DrawCircle(head + new Vector2(side * rx * 0.99f, -r * 0.30f), 6f * s, hair);
                break;

            case 3:   // ponytail out the back
                c.DrawCircle(head + new Vector2(-rx * 0.95f, r * 0.25f), 9f * s, hair);
                c.DrawCircle(head + new Vector2(-rx * 1.25f, r * 0.60f), 7f * s, hair);
                break;

            case 6:   // afro: only the part beside the cap, never across the crown
                // This runs after the cap is drawn, so sweeping it over the top painted the cap
                // out entirely and left a bare head under a ball of hair.
                for (int side = -1; side <= 1; side += 2)
                {
                    c.DrawCircle(head + new Vector2(side * rx * 1.02f, -r * 0.34f), 8f * s, hair);
                    c.DrawCircle(head + new Vector2(side * rx * 0.98f, -r * 0.12f), 7f * s, hair);
                }
                break;

            case 7:   // braids
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 root = head + new Vector2(side * rx * 0.92f, -r * 0.10f);
                    c.DrawCircle(root, 6f * s, hair);
                    c.DrawCircle(root + new Vector2(side * 4f * s, 12f * s), 5.5f * s, hair);
                    c.DrawCircle(root + new Vector2(side * 7f * s, 23f * s), 5f * s, hair);
                }
                break;

            case 10:  // long hair down past the jaw
                for (int side = -1; side <= 1; side += 2)
                    c.DrawColoredPolygon(new[]
                    {
                        head + new Vector2(side * rx * 0.98f, -r * 0.30f),
                        head + new Vector2(side * rx * 1.18f, r * 0.75f),
                        head + new Vector2(side * rx * 0.64f, r * 0.80f),
                        head + new Vector2(side * rx * 0.72f, -r * 0.24f),
                    }, hair);
                break;
        }
    }

    private static void DrawFace(CanvasItem c, Vector2 head, float rx, float r, float s, float facing,
        Look look, Pose pose, float time, Vector2 gaze)
    {
        float eyeY = head.Y + r * 0.06f;
        // Spacing and size vary independently of style. Three eye styles across a whole league
        // meant everyone shared one of three faces.
        float gap = rx * (look.EyeStyle == 1 ? 0.42f : look.EyeStyle == 2 ? 0.32f : 0.36f)
                      * look.EyeSpacing;
        float eyeR = r * (look.EyeStyle == 1 ? 0.25f : look.EyeStyle == 2 ? 0.15f : 0.20f)
                       * look.EyeSizeMul;

        // Eye black smudges under the eyes.
        if (look.EyeBlack)
            for (int side = -1; side <= 1; side += 2)
                c.DrawRect(new Rect2(
                    new Vector2(head.X + side * gap - r * 0.16f, eyeY + r * 0.24f),
                    new Vector2(r * 0.32f, r * 0.11f)), new Color(0.12f, 0.12f, 0.14f, 0.85f));

        // Eyes squeeze shut on a big swing, which sells the effort.
        bool squint = pose == Pose.Swing;

        for (int side = -1; side <= 1; side += 2)
        {
            Vector2 eye = new(head.X + side * gap + gaze.X * r * 0.06f, eyeY + gaze.Y * r * 0.03f);
            if (squint)
            {
                c.DrawLine(eye + new Vector2(-eyeR, 0f), eye + new Vector2(eyeR, 0f), Outline, 2.4f * s);
                continue;
            }

            // White, then a coloured iris, pupil and catchlight — flat dots read as dead eyes.
            c.DrawCircle(eye, eyeR, Colors.White);
            c.DrawCircle(eye + new Vector2(0f, -eyeR * 0.25f), eyeR * 0.95f,
                new Color(1f, 1f, 1f, 0.55f));
            c.DrawArc(eye, eyeR, 0f, Mathf.Tau, 16, Outline, 1.5f * s);

            Vector2 iris = eye + new Vector2(gaze.X * eyeR * 0.42f,
                eyeR * 0.06f + gaze.Y * eyeR * 0.34f);
            c.DrawCircle(iris, eyeR * 0.60f, look.Hair.Lightened(0.25f));
            c.DrawCircle(iris, eyeR * 0.34f, Outline);
            c.DrawCircle(iris + new Vector2(-eyeR * 0.22f, -eyeR * 0.24f), eyeR * 0.16f,
                new Color(1f, 1f, 1f, 0.85f));
        }

        // Nose.
        var noseAt = new Vector2(head.X + gaze.X * rx * 0.10f, eyeY + r * 0.26f);
        var noseColour = look.Skin.Darkened(0.22f);
        switch (look.NoseStyle)
        {
            case 1:   // wide
                c.DrawCircle(noseAt + new Vector2(-r * 0.06f, 0f), r * 0.085f, noseColour);
                c.DrawCircle(noseAt + new Vector2(r * 0.06f, 0f), r * 0.085f, noseColour);
                break;
            case 2:   // long
                c.DrawLine(noseAt + new Vector2(0f, -r * 0.14f), noseAt + new Vector2(0f, r * 0.06f),
                    noseColour, 2.6f * s);
                c.DrawCircle(noseAt + new Vector2(0f, r * 0.06f), r * 0.07f, noseColour);
                break;
            default:
                c.DrawCircle(noseAt, r * 0.09f, noseColour);
                break;
        }

        // Brows set the attitude.
        float browY = eyeY - r * 0.34f;
        float browTilt = look.Mood switch { 0 => 0.16f, 1 => -0.10f, _ => 0f };
        float browWeight = look.BrowStyle switch { 1 => 4.4f, 2 => 1.8f, _ => 2.8f };
        float browSpan = look.BrowStyle switch { 3 => 0.15f, 1 => 0.24f, _ => 0.20f };
        if (look.BrowStyle == 3) browTilt += 0.14f;   // angled, a permanent scowl

        for (int side = -1; side <= 1; side += 2)
        {
            Vector2 a = new(head.X + side * gap - r * browSpan, browY + side * browTilt * r);
            Vector2 b = new(head.X + side * gap + r * browSpan, browY - side * browTilt * r);
            c.DrawLine(a, b, look.Hair.Darkened(0.2f), browWeight * s);
        }

        // Mouth.
        float mouthY = head.Y + r * 0.46f;
        if (pose == Pose.Swing || pose == Pose.Cheer)
        {
            // Open-mouthed effort or celebration.
            c.DrawCircle(new Vector2(head.X, mouthY), r * 0.18f, new Color("#8c3b3b"));
        }
        else
        {
            switch (look.MouthStyle)
            {
                case 1:   // open grin
                    c.DrawArc(new Vector2(head.X, mouthY - r * 0.14f), r * 0.32f,
                        0.18f * Mathf.Pi, 0.82f * Mathf.Pi, 14, Outline, 2.6f * s);
                    break;
                case 2:   // straight line, all business
                    c.DrawLine(new Vector2(head.X - r * 0.20f, mouthY),
                        new Vector2(head.X + r * 0.20f, mouthY), Outline, 2.4f * s);
                    break;
                case 3:   // lopsided smirk
                    c.DrawArc(new Vector2(head.X + facing * r * 0.08f, mouthY - r * 0.08f), r * 0.24f,
                        0.30f * Mathf.Pi, 0.66f * Mathf.Pi, 10, Outline, 2.4f * s);
                    break;
                default:
                    c.DrawArc(new Vector2(head.X, mouthY - r * 0.10f), r * 0.28f,
                        0.25f * Mathf.Pi, 0.75f * Mathf.Pi, 12, Outline, 2.2f * s);
                    break;
            }
        }

        if (look.Freckles)
        {
            for (int i = -1; i <= 1; i += 2)
                for (int j = 0; j < 3; j++)
                    c.DrawCircle(new Vector2(head.X + i * (gap + j * 3f * s), mouthY - r * 0.22f),
                        1.2f * s, new Color(0.55f, 0.33f, 0.22f, 0.7f));
        }

        // Glasses sit over the eyes, drawn after them so the frames read clearly.
        if (look.Glasses && !squint)
        {
            for (int side = -1; side <= 1; side += 2)
                c.DrawArc(new Vector2(head.X + side * gap, eyeY), eyeR * 1.5f, 0f, Mathf.Tau, 16,
                    Outline, 2.2f * s);
            c.DrawLine(new Vector2(head.X - gap + eyeR * 1.5f, eyeY),
                new Vector2(head.X + gap - eyeR * 1.5f, eyeY), Outline, 2f * s);
        }
    }

    private static void DrawCap(CanvasItem c, Vector2 head, float rx, float r, float s, float facing,
        TeamData team, Look look)
    {
        if (look.CapStyle == 2) return;   // bare-headed, so the hairstyle carries the character

        // Crown: a dome closed by its own chord. Adding a near-duplicate start point here makes
        // a zero-area sliver that Godot's triangulator rejects.
        const int steps = 18;
        var crown = new Vector2[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Pi + i / (float)steps * Mathf.Pi;
            crown[i] = head + new Vector2(Mathf.Cos(a) * rx * 1.02f, Mathf.Sin(a) * r * 1.02f - r * 0.06f);
        }
        FillOutlined(c, crown, team.Primary, s);

        // Brim. Backwards caps put it behind him, which is a strong silhouette cue at this size.
        float dir = look.CapStyle == 1 ? -facing : facing;
        float reach = look.CapStyle == 1 ? 1.10f : 1.42f;
        var brim = new[]
        {
            head + new Vector2(dir * -rx * 0.30f, -r * 0.26f),
            head + new Vector2(dir * rx * reach, -r * 0.34f),
            head + new Vector2(dir * rx * reach, -r * 0.10f),
            head + new Vector2(dir * -rx * 0.30f, -r * 0.04f),
        };
        FillOutlined(c, brim, team.Primary.Darkened(0.28f), s);

        // Button and trim stripe.
        c.DrawCircle(head + new Vector2(0f, -r * 0.98f), 2.6f * s, team.Secondary);
        c.DrawArc(head, rx * 0.72f, Mathf.Pi * 1.08f, Mathf.Tau * 0.96f, 16, team.Secondary, 3f * s);

        // A headband worn under the cap, in the club's trim colour.
        if (look.Headband)
            c.DrawArc(head, r * 0.90f, Mathf.Pi * 1.02f, Mathf.Tau * 0.98f, 18,
                team.Secondary.Lightened(0.15f), 5f * s);
    }

    // -----------------------------------------------------------------------
    // Small drawing helpers
    // -----------------------------------------------------------------------

    /// <summary>A rounded limb drawn as an outlined capsule, with a little shading along it.</summary>
    private static void Limb(CanvasItem c, Vector2 a, Vector2 b, float radius, Color colour, float s, int seed)
    {
        var caps = Ink.Capsule(a, b, radius, 7);
        Ink.Shape(c, caps, colour, Outline, 2f * s, seed);
        c.DrawColoredPolygon(Ink.Capsule(a, b, radius * 0.42f, 5),
            new Color(1f, 1f, 1f, 0.10f));
    }

    /// <summary>A thick rounded limb with an outline.</summary>
    private static void Stroke(CanvasItem c, Vector2 a, Vector2 b, float width, Color color)
    {
        c.DrawLine(a, b, Outline, width + 3f);
        c.DrawLine(a, b, color, width);
        c.DrawCircle(b, width * 0.5f, color);
    }

    private static void FillOutlined(CanvasItem c, Vector2[] points, Color fill, float s)
    {
        c.DrawColoredPolygon(points, fill);
        for (int i = 0; i < points.Length; i++)
            c.DrawLine(points[i], points[(i + 1) % points.Length], Outline, 2.2f * s);
    }

    /// <summary>
    /// The bat, drawn in the hitter's hands. Takes the same player as <see cref="Draw"/> so the
    /// grip tracks his build — otherwise a short or tall kid holds a bat floating off his hands.
    /// </summary>
    public static void DrawBat(CanvasItem c, Vector2 feet, float scale, float facing,
        bool swinging, float time, PlayerData player = null)
    {
        float s = scale;
        float height = new Look(player?.LookSeed ?? 0).Height;
        // Must match the hip/shoulder offsets in Draw.
        Vector2 shoulder = feet + new Vector2(0f, -(38f + 30f) * s * height);
        Vector2 grip = swinging
            ? shoulder + new Vector2(facing * 30f * s, -2f * s)
            : shoulder + new Vector2(-facing * 11f * s, -10f * s);

        // Cocked: up and back over the shoulder. Swung: extended out toward the pitcher.
        Vector2 dir = swinging
            ? new Vector2(facing * 0.96f, -0.28f)
            : new Vector2(-facing * 0.42f, -0.91f);
        Vector2 tip = grip + dir * 66f * s;

        c.DrawLine(grip, tip, Outline, 11f * s);
        c.DrawLine(grip, tip, new Color("#c98b48"), 8f * s);
        c.DrawLine(grip.Lerp(tip, 0.62f), tip, new Color("#dda86a"), 9f * s);
        c.DrawCircle(grip, 4f * s, new Color("#3b2a1a"));

        // A swoosh behind a swing sells the speed.
        if (swinging)
        {
            float end = Mathf.Atan2(dir.Y, dir.X * facing);
            c.DrawArc(shoulder, 60f * s, end - 1.6f, end + 0.15f, 18,
                new Color(1f, 1f, 1f, 0.28f), 5f * s);
        }
    }
}
