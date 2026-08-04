using SandlotSlugfest.Data;

namespace SandlotSlugfest.Core;

/// <summary>
/// The left-right matchup, which is one of the central facts of baseball and was missing entirely.
///
/// Handedness was stored on every player and used in exactly one place — which way a man pulls the
/// ball. A left-handed hitter faced a left-handed pitcher exactly as he faced a right-hander, and
/// that absence quietly hollowed out most of a manager's job: there was no reason to carry a lefty
/// out of the pen, no reason to pinch hit for the platoon, no reason to balance a lineup, and the
/// bullpen roles the game already models mattered less than they should.
///
/// The advantage is real and well measured. A hitter facing the opposite hand sees the ball out of
/// the pitcher's hand for longer and the breaking stuff moves toward him rather than away, and it
/// is worth something like twenty to thirty points of average across a league. It is not symmetric:
/// left-handed hitters suffer more against left-handed pitching than right-handers do against
/// right-handed pitching, mostly because a lefty who could not cope with lefties never arrives.
///
/// Switch hitters take whichever side is favourable, which is the entire reason the skill exists.
/// </summary>
public static class Platoon
{
    /// <summary>How the batter is fixed relative to the man on the mound.</summary>
    public enum Edge { Batter, Neutral, Pitcher }

    /// <summary>
    /// Which way the matchup falls. A switch hitter always turns around, so he is never at a
    /// disadvantage — that is the whole point of him.
    /// </summary>
    public static Edge EdgeOf(PlayerData batter, PlayerData pitcher)
    {
        if (batter == null || pitcher == null) return Edge.Neutral;
        if (batter.Bats == Handedness.Switch) return Edge.Batter;

        return batter.Bats == pitcher.Throws ? Edge.Pitcher : Edge.Batter;
    }

    /// <summary>True when the hitter has the platoon advantage.</summary>
    public static bool BatterHasEdge(PlayerData batter, PlayerData pitcher) =>
        EdgeOf(batter, pitcher) == Edge.Batter;

    /// <summary>
    /// What the matchup does to a hitter's bat and eye. Above one is the favourable side.
    ///
    /// Sized so the league splits land near the real ones rather than by feel: the whole point of
    /// having a calibration harness is that a number like this gets measured, and --platoon prints
    /// the split it actually produces.
    /// </summary>
    public static float Factor(PlayerData batter, PlayerData pitcher)
    {
        if (batter == null || pitcher == null) return 1f;

        // A switch hitter gets the good side of the matchup, but not quite as good as a natural
        // hitter with the platoon advantage — he is working from his weaker side often enough.
        // Measured, not guessed. The first sizing produced a 35-point edge for right-handers and
        // 45 for lefties against a real 14 and 22 — the bat and the read compound, so a factor
        // that looks modest on its own lands more than twice as hard as intended.
        if (batter.Bats == Handedness.Switch) return 1.013f;

        if (batter.Bats != pitcher.Throws) return 1.018f;

        // Same hand. The left-on-left penalty is the harsher one, but only somewhat — sized at
        // 0.972 it bought lefties a 36-point platoon edge against a real 22.
        return batter.Bats == Handedness.Left ? 0.980f : 0.989f;
    }

    /// <summary>
    /// How much worse the hitter reads the pitch in a bad matchup. The breaking ball moving away
    /// from him rather than toward him is most of the disadvantage, and a read error is the right
    /// place to express that — it is why a hitter on the wrong side chases.
    /// </summary>
    public static float ReadPenalty(PlayerData batter, PlayerData pitcher) =>
        EdgeOf(batter, pitcher) switch
        {
            Edge.Batter => 0.980f,
            Edge.Pitcher => batter.Bats == Handedness.Left ? 1.027f : 1.016f,
            _ => 1f,
        };

    /// <summary>Short form for the broadcast and the bench screens.</summary>
    public static string Text(PlayerData batter, PlayerData pitcher) => EdgeOf(batter, pitcher) switch
    {
        Edge.Batter => "has the platoon edge",
        Edge.Pitcher => "is on the wrong side of it",
        _ => "",
    };

    /// <summary>How a hand is written on a lineup card.</summary>
    public static string Letter(Handedness h) => h switch
    {
        Handedness.Left => "L",
        Handedness.Switch => "S",
        _ => "R",
    };
}
