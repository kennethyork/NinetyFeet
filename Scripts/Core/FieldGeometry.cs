using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Core;

/// <summary>
/// The ballpark in field space: feet, home plate at the origin, +Y toward centre field,
/// +X toward the right-field line. Everything the sim and the renderers need to agree on
/// about where things are lives here.
/// </summary>
public static class FieldGeometry
{
    public const float BasePathLength = 90f;
    public const float MoundDistance = 60.5f;

    /// <summary>Half the diagonal of a base path — bases sit on the 45-degree lines.</summary>
    public const float BaseOffset = BasePathLength * 0.70710678f; // 90 / sqrt(2)

    public static readonly Vector2 Home = new(0f, 0f);
    public static readonly Vector2 First = new(BaseOffset, BaseOffset);
    public static readonly Vector2 Second = new(0f, BaseOffset * 2f);
    public static readonly Vector2 Third = new(-BaseOffset, BaseOffset);
    public static readonly Vector2 Mound = new(0f, MoundDistance);

    /// <summary>Index 0 is home; 1..3 are first, second and third.</summary>
    public static readonly Vector2[] Bases = { Home, First, Second, Third };

    public const float FenceDownTheLines = 330f;
    public const float FenceToCenter = 400f;
    public const float FenceHeight = 12f;

    /// <summary>
    /// The park the current game is being played in — always the home club's. Set once when a
    /// game starts; everything that asks about the wall reads it from here.
    /// </summary>
    public static Stadium Current { get; private set; } = Stadiums.For(0);

    public static void SetStadium(Stadium stadium) => Current = stadium ?? Stadiums.For(0);

    /// <summary>
    /// Wind at the park, in feet per second, positive blowing out toward centre field. A ball
    /// travels through moving air, so this is a real force on a fly ball and not decoration —
    /// which is the point of having weather at all.
    /// </summary>
    public static float Wind { get; private set; }

    /// <summary>
    /// How thick the air is tonight. Cold air is dense and dead; a warm evening carries. This
    /// multiplies the park's own air density, so Denver in July is still Denver.
    /// </summary>
    public static float AirTemperatureFactor { get; private set; } = 1f;

    public static void SetConditions(float windFeetPerSecond, int temperatureF)
    {
        Wind = windFeetPerSecond;

        // Roughly a one per cent change in density for every ten degrees, about right over the
        // range baseball is played in.
        AirTemperatureFactor = Mathf.Clamp(1f + (70 - temperatureF) * 0.0016f, 0.93f, 1.07f);
    }

    /// <summary>Clears the weather back to a still, temperate evening.</summary>
    public static void ClearConditions()
    {
        Wind = 0f;
        AirTemperatureFactor = 1f;
    }

    /// <summary>The park's air, with tonight's temperature folded in.</summary>
    public static float AirDensity => Current.AirDensity * AirTemperatureFactor;

    /// <summary>Backstop distance behind the plate.</summary>
    public const float BackstopDistance = 60f;

    /// <summary>How far from the plate the dirt infield gives way to outfield grass.</summary>
    public const float InfieldDirtRadius = 142f;

    /// <summary>Distance to the fence along a given ball angle, in radians from straightaway centre.</summary>
    public static float FenceDistance(float angleFromCenter) => Current.DistanceAt(angleFromCenter);

    /// <summary>Height of the wall at a given angle, which varies a lot from park to park.</summary>
    public static float FenceHeightAt(float angleFromCenter) => Current.HeightAt(angleFromCenter);

    /// <summary>Angle from straightaway centre for a point on the field, in radians. Negative is toward left.</summary>
    public static float AngleFromCenter(Vector2 point) => Mathf.Atan2(point.X, Mathf.Max(point.Y, 0.001f));

    /// <summary>True when a landing point is in fair territory (inside the 45-degree foul lines).</summary>
    public static bool IsFair(Vector2 point)
    {
        if (point.Y <= 0f) return false;
        return Mathf.Abs(point.X) <= point.Y + 0.001f;
    }

    /// <summary>True when a point has cleared the fence in fair territory.</summary>
    public static bool IsBeyondFence(Vector2 point) =>
        IsFair(point) && point.Length() >= FenceDistance(AngleFromCenter(point));

    /// <summary>
    /// The nine spots someone actually stands in. The defence used to be built by walking the
    /// whole <see cref="Position"/> enum, which was fine until the designated hitter joined it —
    /// he would have taken the field as a tenth man standing on the mound.
    /// </summary>
    public static readonly Position[] DefensiveSlots =
    {
        Position.P, Position.C, Position.First, Position.Second, Position.Third,
        Position.Short, Position.Left, Position.Center, Position.Right,
    };

    /// <summary>Where each fielder stands before the pitch.</summary>
    public static Vector2 StartingSpot(Position position) => position switch
    {
        Position.P => Mound,
        Position.C => new Vector2(0f, -7f),
        Position.First => new Vector2(54f, 84f),
        Position.Second => new Vector2(34f, 132f),
        Position.Third => new Vector2(-54f, 84f),
        Position.Short => new Vector2(-34f, 132f),
        Position.Left => new Vector2(-150f, 245f),
        Position.Center => new Vector2(0f, 300f),
        Position.Right => new Vector2(150f, 245f),
        _ => Mound,
    };

    /// <summary>Which fielder covers a base on a throw.</summary>
    public static Position CoverageFor(int baseIndex) => baseIndex switch
    {
        0 => Position.C,
        1 => Position.First,
        2 => Position.Second,
        3 => Position.Third,
        _ => Position.C,
    };

    public static string BaseName(int baseIndex) => baseIndex switch
    {
        0 => "home",
        1 => "first",
        2 => "second",
        3 => "third",
        _ => "?",
    };
}
