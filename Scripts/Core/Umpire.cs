using Godot;

namespace SandlotSlugfest.Core;

/// <summary>
/// The man behind the plate, and the challenge system that second-guesses him.
///
/// Major League Baseball's competition committee approved the Automated Ball-Strike system for the
/// 2026 season on 23 September 2025: a batter, pitcher or catcher may challenge a call by tapping
/// his helmet or cap, each club starts with two challenges, keeps one whenever a challenge
/// succeeds, and is granted more in extra innings.
///
/// None of that means anything unless the umpire can actually be wrong, and until now he read the
/// ball's true position straight off the pitch and was right every single time. He now misses
/// calls the way a real one does — almost never down the middle, often on the black.
/// </summary>
public static class Umpire
{
    /// <summary>Challenges each club carries into a game.</summary>
    public const int ChallengesPerGame = 2;

    /// <summary>Extra challenges granted once a game goes past regulation.</summary>
    public const int ExtraInningBonus = 1;

    /// <summary>
    /// How often a pitch at a given distance from the edge of the zone is called wrong.
    ///
    /// Real umpires are near-perfect in the middle and close to a coin flip on the black; the
    /// error is a function of how far the pitch is from the boundary, not a flat rate.
    /// </summary>
    public static float MissChance(Pitch pitch)
    {
        // Distance outside (positive) or inside (negative) the zone, in feet, on each axis.
        float dx = Mathf.Abs(pitch.CrossPoint.X) - Pitch.ZoneHalfWidth;
        float dy = pitch.CrossPoint.Y > Pitch.ZoneTop ? pitch.CrossPoint.Y - Pitch.ZoneTop
                 : pitch.CrossPoint.Y < Pitch.ZoneBottom ? Pitch.ZoneBottom - pitch.CrossPoint.Y
                 : -Mathf.Min(pitch.CrossPoint.Y - Pitch.ZoneBottom, Pitch.ZoneTop - pitch.CrossPoint.Y);

        // How close the pitch is to the boundary; 0 means right on the line.
        float edge = Mathf.Min(Mathf.Abs(dx), Mathf.Abs(dy));

        // Measured to land near the real rate of about 8% of taken pitches. A shallow falloff put
        // a quarter of all calls in doubt, because most taken pitches sit within a few inches of
        // the boundary; the band of genuine uncertainty is far narrower than that.
        return Mathf.Clamp(0.38f - edge * 1.85f, 0f, 0.38f);
    }

    /// <summary>
    /// What the umpire says. Usually the truth, occasionally not, and more often wrong the closer
    /// the pitch is to the edge of the zone.
    /// </summary>
    public static bool CallsStrike(Pitch pitch, ref Rng rng)
    {
        bool truth = pitch.IsStrike;
        return rng.Chance(MissChance(pitch)) ? !truth : truth;
    }
}

/// <summary>A club's remaining challenges, and what came of the last one.</summary>
public sealed class ChallengeBank
{
    public int Away = Umpire.ChallengesPerGame;
    public int Home = Umpire.ChallengesPerGame;

    /// <summary>Set once extra innings have granted their bonus, so it is only granted once.</summary>
    private bool _extrasGranted;

    public int Remaining(bool awayClub) => awayClub ? Away : Home;

    public bool Any(bool awayClub) => Remaining(awayClub) > 0;

    /// <summary>Spends a challenge. A successful one is handed straight back, as the rule says.</summary>
    public void Spend(bool awayClub, bool upheld)
    {
        if (upheld) return;         // "retains a challenge if it is successful"
        if (awayClub) Away = Mathf.Max(0, Away - 1);
        else Home = Mathf.Max(0, Home - 1);
    }

    /// <summary>Grants the extra-innings allowance, once, when a game runs long.</summary>
    public void EnterExtraInnings()
    {
        if (_extrasGranted) return;
        _extrasGranted = true;
        Away += Umpire.ExtraInningBonus;
        Home += Umpire.ExtraInningBonus;
    }

    public void Reset()
    {
        Away = Home = Umpire.ChallengesPerGame;
        _extrasGranted = false;
    }
}
