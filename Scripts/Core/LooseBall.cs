using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Core;

/// <summary>
/// The two ways a pitch nobody swung at still changes the game: it hits the batter, or it gets
/// past the catcher.
///
/// Neither existed. Every pitch either crossed the plate or did not, and a ball four feet inside
/// was simply ball one — so nobody was ever hit, nobody ever advanced on a pitch in the dirt, and
/// a catcher's hands did not matter. Both are ordinary events: roughly one hit batsman and one
/// wild pitch per club per two games, which over a season is a hundred baserunners a league was
/// pretending did not exist.
/// </summary>
public static class LooseBall
{
    /// <summary>
    /// How far from the centre of the plate, on the batter's side, his body begins. He stands
    /// with the plate at arm's length, so the inside corner and his front hip are about a foot
    /// apart.
    /// </summary>
    public const float BodyEdge = 1.15f;

    /// <summary>Beyond this and the pitch is behind him — he has the time to turn away.</summary>
    public const float BodyBack = 2.30f;

    /// <summary>A pitch below this crosses in the dirt, and the catcher has to block it.</summary>
    public const float DirtHeight = 0.95f;

    /// <summary>Wide enough that he is reaching across his body for it.</summary>
    public const float WideOfTarget = 1.95f;

    /// <summary>
    /// Which way is inside. Positive plate X is the right-handed hitter's outside corner, so
    /// inside to him is negative and inside to a left-hander is positive. A switch hitter has
    /// already turned around to face the arm he is looking at.
    /// </summary>
    public static float InsideSign(PlayerData batter, PlayerData pitcher)
    {
        if (batter == null) return -1f;

        var side = batter.Bats;
        if (side == Handedness.Switch)
            side = pitcher?.Throws == Handedness.Left ? Handedness.Right : Handedness.Left;

        return side == Handedness.Left ? 1f : -1f;
    }

    /// <summary>
    /// Whether a pitch he did not swing at got him.
    ///
    /// It has to be far enough inside to be where he is standing and low enough to be somewhere
    /// he cannot simply lean away from. Most of those he avoids — the ones that land are the ones
    /// that were on him before he read them, so the chance falls away as the pitch runs further
    /// behind him and he has the time to turn.
    /// </summary>
    public static bool HitsBatter(Pitch pitch, PlayerData batter, ref Rng rng)
    {
        if (pitch == null || batter == null) return false;

        float inward = pitch.CrossPoint.X * InsideSign(batter, pitch.Pitcher);
        if (inward < BodyEdge || inward > BodyBack) return false;

        // Above the shoulders he ducks, and below the ankles it bounces past.
        float h = pitch.CrossPoint.Y;
        if (h < 0.35f || h > 5.2f) return false;

        // Deepest right at the front hip, tailing off as it gets behind him.
        float depth = 1f - Mathf.Clamp((inward - BodyEdge) / (BodyBack - BodyEdge), 0f, 1f);

        // A pitch at the knees or on the hands is the one that catches him; up and in he sees.
        float height = h < 3.6f ? 1f : Mathf.Clamp((5.2f - h) / 1.6f, 0f, 1f);

        return rng.Chance(EvadeFailure * (0.35f + 0.65f * depth) * height);
    }

    /// <summary>
    /// How often a pitch square in the batter's box actually lands on him. Sized against the real
    /// rate of roughly 0.4 hit batsmen per club per game; --loose measures what it produces.
    /// </summary>
    public const float EvadeFailure = 0.054f;

    /// <summary>
    /// Whether the catcher failed to keep a pitch in front of him. Only worth asking with a man
    /// on: a pitch that skips to the backstop with the bases empty costs nothing and is not
    /// scored a wild pitch.
    /// </summary>
    public static bool GetsAway(Pitch pitch, PlayerData catcher, ref Rng rng)
    {
        if (pitch == null) return false;

        bool inDirt = pitch.CrossPoint.Y < DirtHeight;
        bool wide = Mathf.Abs(pitch.CrossPoint.X) > WideOfTarget;
        if (!inDirt && !wide) return false;

        // How badly it missed, as a share of the worst that is worth modelling.
        float miss = inDirt
            ? Mathf.Clamp((DirtHeight - pitch.CrossPoint.Y) / DirtHeight, 0f, 1f)
            : Mathf.Clamp((Mathf.Abs(pitch.CrossPoint.X) - WideOfTarget) / 1.2f, 0f, 1f);

        // A good receiver blocks nearly everything he gets to. A poor one does not.
        float hands = catcher != null ? Mathf.Clamp(catcher.Fielding / 100f, 0.1f, 1f) : 0.5f;

        return rng.Chance(BlockFailure * miss * (1.25f - hands * 0.75f));
    }

    /// <summary>The scale on a catcher's misses, sized to about 0.4 wild pitches per club a game.</summary>
    /// <summary>
    /// Raised from 0.725. Wild pitches came out 25% under the majors — 0.57 a game against 0.76 —
    /// so catchers were blocking rather more than catchers do.
    /// </summary>
    public const float BlockFailure = 0.96f;
}
