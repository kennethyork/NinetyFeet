using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Core;

/// <summary>
/// The pitches. The first four keep their old positions on purpose — the repertoire is a bitmask
/// and a netplay command carries the type as an integer, so renumbering would silently change what
/// every existing arm throws and what a command off the wire means.
/// </summary>
public enum PitchType
{
    Fastball, Curveball, Changeup, Slider,
    Sinker, Cutter, Splitter, Knuckler,
}

/// <summary>Broadcast names for the pitch types.</summary>
public static class SwingProfileNames
{
    public static string Of(PitchType t) => t switch
    {
        PitchType.Curveball => "Curve",
        PitchType.Changeup => "Change",
        PitchType.Slider => "Slider",
        PitchType.Sinker => "Sinker",
        PitchType.Cutter => "Cutter",
        PitchType.Splitter => "Splitter",
        PitchType.Knuckler => "Knuckler",
        _ => "Fastball",
    };

    /// <summary>The short form the mound overlay uses, where there is no room for the long one.</summary>
    public static string Short(PitchType t) => t switch
    {
        PitchType.Curveball => "CB",
        PitchType.Changeup => "CH",
        PitchType.Slider => "SL",
        PitchType.Sinker => "SI",
        PitchType.Cutter => "FC",
        PitchType.Splitter => "FS",
        PitchType.Knuckler => "KN",
        _ => "FB",
    };
}

/// <summary>
/// A pitch in flight, described in the plane of home plate: X is horizontal offset in feet
/// (positive toward the right-handed batter's back foot side of the plate) and Z is height.
/// </summary>
public sealed class Pitch
{
    /// <summary>
    /// Real pitches cross the plate in under half a second. Stretch that so a human can actually
    /// read the pitch, move the bat onto it and time a swing — the way an arcade baseball game
    /// has to feel. At 2.5 a fastball takes a bit over a second from release to the plate.
    /// </summary>
    public const float TimeScale = 3.2f;

    public const float ZoneHalfWidth = 0.85f;   // feet, plate plus a ball's width
    public const float ZoneBottom = 1.55f;      // feet off the ground
    public const float ZoneTop = 3.45f;

    public PitchType Type;
    public PlayerData Pitcher;

    /// <summary>Where the pitcher aimed, in plate-plane feet.</summary>
    public Vector2 AimPoint;

    /// <summary>Where the ball actually crosses the plate after command error and break.</summary>
    public Vector2 CrossPoint;

    /// <summary>Sideways and vertical break applied over the flight, in feet.</summary>
    public Vector2 Break;

    /// <summary>Seconds from release to crossing the plate, already stretched by <see cref="TimeScale"/>.</summary>
    public float FlightTime;

    public float SpeedMph;

    /// <summary>0 at release, 1 at the plate.</summary>
    public float Progress;

    public bool IsStrike => Mathf.Abs(CrossPoint.X) <= ZoneHalfWidth &&
                            CrossPoint.Y >= ZoneBottom && CrossPoint.Y <= ZoneTop;

    /// <summary>Ball position in the plate plane at the current progress, including break.</summary>
    public Vector2 PositionAt(float progress)
    {
        float t = Mathf.Clamp(progress, 0f, 1.4f);
        // Release point is roughly over the middle; break accumulates late, so square the term.
        Vector2 straight = new Vector2(0f, 5.9f).Lerp(CrossPoint - Break, t);
        return straight + Break * (t * t);
    }

    public static string Label(PitchType type) => type switch
    {
        PitchType.Fastball => "Fastball",
        PitchType.Curveball => "Curveball",
        PitchType.Changeup => "Changeup",
        PitchType.Slider => "Slider",
        _ => "?",
    };
}

public static class PitchFactory
{
    /// <summary>
    /// Builds a pitch from the pitcher's ratings and where he aimed. Weaker command scatters the
    /// ball further from the target; each pitch type has its own speed and break signature.
    /// </summary>
    /// <param name="command">
    /// Scales how far the ball strays from where it was aimed. A human who has taken the time to
    /// place the reticle should mostly get the pitch they asked for, so the game scene passes a
    /// small value here; the CPU passes 1 and lives with its pitcher's control rating.
    /// </param>
    public static Pitch Create(PlayerData pitcher, PitchType type, Vector2 aim, float effortFatigue,
        ref Rng rng, float command = 1f, float speedScale = 1f)
    {
        float power = pitcher.PitchPower / 10f;
        float control = pitcher.PitchControl / 10f;

        // Fatigue quietly drains velocity and command as the outing wears on.
        power = Mathf.Max(0.25f, power - effortFatigue * 0.35f);
        control = Mathf.Max(0.2f, control - effortFatigue * 0.30f);

        float baseMph = type switch
        {
            PitchType.Fastball => 78f + power * 24f,
            PitchType.Curveball => 62f + power * 16f,
            PitchType.Changeup => 66f + power * 15f,
            PitchType.Slider => 72f + power * 19f,

            // A sinker gives up a little off the four-seam for run and sink; a cutter gives up
            // less again for a short glove-side move; a splitter arrives at changeup speed and
            // falls off the table; a knuckler barely travels and nobody knows where it goes.
            PitchType.Sinker => 75f + power * 23f,
            PitchType.Cutter => 76f + power * 22f,
            PitchType.Splitter => 70f + power * 17f,
            PitchType.Knuckler => 58f + power * 9f,
            _ => 75f,
        };

        // Arm side is to the right for a right-hander and the reverse for a left-hander, which is
        // what makes a sinker and a cutter opposites rather than two names for the same pitch.
        float armSide = pitcher.Throws == Handedness.Left ? -1f : 1f;

        Vector2 brk = type switch
        {
            PitchType.Fastball => new Vector2(rng.Range(-0.15f, 0.15f), 0.25f + power * 0.35f),
            PitchType.Curveball => new Vector2(rng.Range(-0.5f, 0.5f), -(1.5f + power * 1.4f)),
            PitchType.Changeup => new Vector2(rng.Range(-0.4f, 0.4f), -(0.7f + power * 0.6f)),
            PitchType.Slider => new Vector2((rng.Chance(0.5f) ? -1f : 1f) * (1.1f + power * 1.0f), -0.5f),

            PitchType.Sinker => new Vector2(
                armSide * (0.65f + power * 0.55f) + rng.Range(-0.12f, 0.12f),
                -(0.35f + power * 0.35f)),

            PitchType.Cutter => new Vector2(
                -armSide * (0.45f + power * 0.45f) + rng.Range(-0.1f, 0.1f),
                rng.Range(-0.1f, 0.15f)),

            PitchType.Splitter => new Vector2(
                rng.Range(-0.3f, 0.3f), -(1.15f + power * 0.75f)),

            // The whole point of a knuckleball is that it has no signature.
            PitchType.Knuckler => new Vector2(rng.Range(-2.0f, 2.0f), rng.Range(-1.5f, 1.1f)),

            _ => Vector2.Zero,
        };

        // Signature moves exaggerate what the pitch already does.
        switch (pitcher.Special)
        {
            case Special.Fireball when type == PitchType.Fastball:
                baseMph += 7f;
                break;
            case Special.CrazyCurve when type == PitchType.Curveball:
                brk.Y *= 1.7f;
                brk.X *= 2.0f;
                break;
            case Special.Corkscrew when type == PitchType.Slider:
                brk.X *= 1.9f;
                brk.Y -= 0.8f;
                break;
            case Special.Heatseeker when type == PitchType.Fastball:
                baseMph += 4f;
                brk.Y += 0.9f;              // it keeps climbing
                break;
            case Special.Knuckleball when type is PitchType.Changeup or PitchType.Curveball:
                // Drifts in a direction nobody, including the pitcher, can predict.
                brk = new Vector2(rng.Range(-2.2f, 2.2f), rng.Range(-1.6f, 1.2f));
                baseMph -= 6f;
                break;
        }

        // Ice veins: the arm does not lose the strike zone as the outing wears on.
        if (pitcher.Special == Special.IceVeins) control = Mathf.Min(1f, control + effortFatigue * 0.30f);

        // Command error: a wild arm misses the spot by a foot or more. This is what puts hitters
        // in favourable counts, so it has to be big enough that walks actually happen.
        // Command was loose enough to walk 7.4 a game against a real 5.66. Tightening it pulls
        // walks down and strikeouts up, and both were out in that direction.
        // Same reasoning as the hitting side: command matters, but not so much that a good arm
        // and an ordinary one are different sports.
        float scatter = (0.95f - control * 0.55f) * 1.82f * command;
        Vector2 miss = new(
            (rng.Bell() - 0.5f) * scatter * 2.2f,
            (rng.Bell() - 0.5f) * scatter * 2.0f);

        // The target is where the pitch finishes, so break must not be added again here —
        // Pitch.PositionAt curves the path on its way to exactly this point.
        Vector2 cross = aim + miss;
        cross.X = Mathf.Clamp(cross.X, -2.6f, 2.6f);
        cross.Y = Mathf.Clamp(cross.Y, 0.2f, 6.2f);

        // Difficulty leans on velocity as well as the bat: a harder level genuinely gives you
        // less time, which is the thing a hitter actually feels.
        baseMph *= speedScale;

        float feetPerSecond = baseMph * 1.46667f;
        float flight = FieldGeometry.MoundDistance / feetPerSecond * Pitch.TimeScale;

        return new Pitch
        {
            Type = type,
            Pitcher = pitcher,
            AimPoint = aim,
            Break = brk,
            CrossPoint = cross,
            SpeedMph = baseMph,
            FlightTime = flight,
            Progress = 0f,
        };
    }
}
