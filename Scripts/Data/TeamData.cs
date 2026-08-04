using Godot;

namespace SandlotSlugfest.Data;

public enum League { American, National }

public enum Division { East, West }

/// <summary>
/// A club in the 32-team league. Every team is an original, parody club tied to a
/// real major-league market — 30 existing markets plus two expansion cities.
/// </summary>
public sealed class TeamData
{
    public int Id;
    public string City;        // Display market, e.g. "San Francisco"
    public string Nickname;    // e.g. "Fog"
    public string Abbrev;      // 3 letters, e.g. "SFF"
    public League League;
    public Division Division;

    /// <summary>Jersey base colour — used for caps, uniforms and UI accents.</summary>
    public Color Primary;
    /// <summary>Trim colour — used for numbers, piping and highlights.</summary>
    public Color Secondary;

    /// <summary>Flavour line shown on the team-select screen.</summary>
    public string Motto;

    /// <summary>Team-wide tilts applied on top of generated player ratings (-2..+2).</summary>
    public int PowerBias;
    public int SpeedBias;
    public int PitchingBias;
    public int DefenseBias;

    public string FullName => $"{City} {Nickname}";

    /// <summary>A readable contrast colour for text drawn on top of <see cref="Primary"/>.</summary>
    public Color TextOnPrimary =>
        Primary.Luminance > 0.45f ? new Color(0.08f, 0.08f, 0.10f) : Colors.White;

    public override string ToString() => FullName;
}
