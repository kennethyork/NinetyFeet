using Godot;

namespace SandlotSlugfest.Data;

/// <summary>
/// A ballpark. The outfield wall is described by five control points — left-field line, left
/// gap, straightaway centre, right gap, right-field line — with a distance and a wall height at
/// each, interpolated smoothly in between. That is enough to express a short porch, a deep
/// alley or a monster wall without hand-authoring a polygon per park.
/// </summary>
public sealed class Stadium
{
    public int TeamId;
    public string Name;
    public string Quirk;

    /// <summary>Fence distance in feet at the five control angles, left line to right line.</summary>
    public float[] Distances = { 330f, 375f, 400f, 375f, 330f };

    /// <summary>Wall height in feet at the same five points.</summary>
    public float[] Heights = { 8f, 8f, 8f, 8f, 8f };

    /// <summary>
    /// How much the ball carries here. Thin mountain air carries; cold heavy sea air does not.
    /// Applied as a multiplier on aerodynamic drag, so above 1 means a pitcher's park.
    /// </summary>
    public float AirDensity = 1f;

    /// <summary>Roughly how much room there is behind the plate and down the lines.</summary>
    public float FoulTerritory = 1f;

    public Color Grass = new("#2f7d43");
    public Color GrassAlt = new("#276b39");
    public Color Dirt = new("#b1793f");
    public Color Wall = new("#2b3a4a");
    public Color WallTrim = new("#8b98a6");

    /// <summary>True for a park with a roof, where the ball flies the same way every night.</summary>
    public bool Covered;

    /// <summary>Distance to the wall at an angle measured from straightaway centre, in radians.</summary>
    public float DistanceAt(float angleFromCenter) => Sample(Distances, angleFromCenter);

    /// <summary>Wall height at an angle from straightaway centre, in radians.</summary>
    public float HeightAt(float angleFromCenter) => Sample(Heights, angleFromCenter);

    /// <summary>
    /// Interpolates a five-point profile. -45 degrees maps to index 0 and +45 to index 4, so
    /// the array reads naturally from the left-field line across to the right-field line.
    /// </summary>
    private static float Sample(float[] points, float angleFromCenter)
    {
        float t = Mathf.Clamp((angleFromCenter / (Mathf.Pi * 0.25f) + 1f) * 0.5f, 0f, 1f);
        float scaled = t * (points.Length - 1);
        int i = Mathf.Clamp((int)scaled, 0, points.Length - 2);
        float frac = scaled - i;
        // Smoothstep between control points so the wall curves rather than kinking.
        frac = frac * frac * (3f - 2f * frac);
        return Mathf.Lerp(points[i], points[i + 1], frac);
    }

    public float ShortestFence
    {
        get
        {
            float min = Distances[0];
            foreach (float d in Distances) min = Mathf.Min(min, d);
            return min;
        }
    }

    public float DeepestFence
    {
        get
        {
            float max = Distances[0];
            foreach (float d in Distances) max = Mathf.Max(max, d);
            return max;
        }
    }

    public float TallestWall
    {
        get
        {
            float max = Heights[0];
            foreach (float h in Heights) max = Mathf.Max(max, h);
            return max;
        }
    }
}
