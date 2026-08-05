using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Core;

/// <summary>Where the defence stands, which is a decision and was not one.</summary>
public enum Alignment
{
    /// <summary>Nobody moves. Every ball in play was fielded from here, all game, every game.</summary>
    Straight,

    /// <summary>Infield in to cut the run off at the plate. Costs range everywhere else.</summary>
    InfieldIn,

    /// <summary>Middle infielders at double-play depth, a step toward second and a step in.</summary>
    DoublePlay,

    /// <summary>Corners on the lines and the outfield deep. Concedes the single, takes the double.</summary>
    NoDoubles,

    /// <summary>Three infielders on the hitter's pull side. Takes the ground ball, opens the other half.</summary>
    Shift,
}

/// <summary>
/// The defensive alignment, and what standing somewhere else actually costs.
///
/// The defence took the field in exactly one shape and never moved. A run on third with one out
/// played identically to a runner on first in the second inning: the corners never came in, the
/// middle never cheated toward the bag, and nobody ever guarded a line. That is half of what a
/// manager does with a defence, and none of it existed.
///
/// Everything here is a shift in feet from a fielder's ordinary spot. There is no fudge factor
/// and no hidden bonus: moving a man closer to the plate really does get him to a soft ground
/// ball sooner and really does let a harder one past him, because the play simulation is already
/// deciding both of those from where he is standing.
/// </summary>
public static class Positioning
{
    /// <summary>
    /// Where this fielder stands under the given alignment.
    ///
    /// <paramref name="pullLeft"/> says which way the hitter pulls the ball, which is the only
    /// thing a shift can be built on — it is drawn from his handedness by the caller so a switch
    /// hitter is read from the side he is actually batting from.
    /// </summary>
    public static Vector2 SpotFor(Position slot, Alignment how, bool pullLeft)
    {
        var at = FieldGeometry.StartingSpot(slot);

        switch (how)
        {
            case Alignment.InfieldIn:
                // The four infielders on the grass. A ball through is a ball through, but a
                // ground ball anywhere near them gets the runner at the plate.
                if (slot is Position.First or Position.Third)
                    return at + new Vector2(0f, -22f);
                if (slot is Position.Second or Position.Short)
                    return at + new Vector2(0f, -34f);
                return at;

            case Alignment.DoublePlay:
                // A step toward the bag and a step in, so the feed is quicker. It opens the holes.
                if (slot == Position.Second) return at + new Vector2(-14f, -10f);
                if (slot == Position.Short) return at + new Vector2(14f, -10f);
                return at;

            case Alignment.NoDoubles:
                // Corners hug the lines, outfield backs up. The single in front is conceded.
                if (slot == Position.First) return at + new Vector2(16f, -6f);
                if (slot == Position.Third) return at + new Vector2(-16f, -6f);
                if (slot is Position.Left or Position.Right) return at + new Vector2(0f, 34f);
                if (slot == Position.Center) return at + new Vector2(0f, 28f);
                return at;

            case Alignment.Shift:
            {
                // Three men on the pull side. The second baseman crosses over behind the bag and
                // the shortstop slides with him; the far side of the infield is simply given up.
                float side = pullLeft ? -1f : 1f;

                if (slot == Position.Second)
                    return pullLeft ? new Vector2(-14f, 138f) : new Vector2(46f, 120f);
                if (slot == Position.Short)
                    return pullLeft ? new Vector2(-62f, 120f) : new Vector2(6f, 138f);
                if (slot is Position.First or Position.Third)
                    return at + new Vector2(side * 10f, 0f);

                // The outfield leans with it, which is most of why a shift works at all.
                if (slot is Position.Left or Position.Center or Position.Right)
                    return at + new Vector2(side * 26f, 0f);

                return at;
            }

            default:
                return at;
        }
    }

    /// <summary>Which way this hitter pulls, resolved for a switch hitter by who is pitching.</summary>
    public static bool PullsLeft(PlayerData batter, PlayerData pitcher)
    {
        if (batter == null) return true;

        var side = batter.Bats;
        if (side == Handedness.Switch)
            side = pitcher?.Throws == Handedness.Left ? Handedness.Right : Handedness.Left;

        // A right-handed hitter pulls to left field.
        return side == Handedness.Right;
    }

    public static string Label(Alignment how) => how switch
    {
        Alignment.InfieldIn => "Infield in",
        Alignment.DoublePlay => "Double play depth",
        Alignment.NoDoubles => "No doubles",
        Alignment.Shift => "Shift",
        _ => "Straight up",
    };

    public static string Short(Alignment how) => how switch
    {
        Alignment.InfieldIn => "IN",
        Alignment.DoublePlay => "DP",
        Alignment.NoDoubles => "NO 2B",
        Alignment.Shift => "SHIFT",
        _ => "STRAIGHT",
    };

    /// <summary>What the alignment is for, in the words a bench coach would use.</summary>
    public static string Why(Alignment how) => how switch
    {
        Alignment.InfieldIn => "Cuts the run off at the plate. More gets through.",
        Alignment.DoublePlay => "Quicker to the bag. Wider holes.",
        Alignment.NoDoubles => "Give up the single, take away the extra base.",
        Alignment.Shift => "Three on his pull side. The other half is open.",
        _ => "Everyone where they belong.",
    };

    /// <summary>
    /// What the computer calls for, which is currently nothing, and that is a measured decision
    /// rather than an unfinished one.
    ///
    /// Every alignment except straight up trades range for something else: the infield comes in
    /// to cut off a run, the middle cheats toward the bag to turn two, the corners guard the
    /// lines to take a double. All three of those trades are paid for in hits and all three are
    /// only worth it because of the out they buy — and this infield cannot buy that out. Of every
    /// ball in play not caught on the fly, 95% goes down as a hit; there are 0.05 double plays a
    /// game against a real 1.44. See the write-up at PlaySimulation.ThrowTime.
    ///
    /// So the cost is real and the benefit is missing. Turning these on for the computer was
    /// measured: league scoring went from +1.1% against the real rate to +5.3%, and hits from
    /// +0.2% to +3.9%, purely because the defence kept giving away range for force outs it was
    /// never going to record. Playing straight up is genuinely the computer's best defence today.
    ///
    /// The alignments themselves are not the problem and are not disabled — --defence shows the
    /// shift moving eleven points of hits off the pull side and no-doubles cutting doubles by a
    /// fifth, exactly as they should. They are offered to the player, whose call it is. Restore
    /// the situational rules below the day the infield can field a ground ball.
    /// </summary>
    public static Alignment Suggested(GameSituation sit)
    {
        if (sit == null) return Alignment.Straight;

        //  int outs = sit.Outs, lead = sit.FieldingScore - sit.BattingScore;
        //  if (sit.RunnerOn(3) && outs < 2 && lead is >= -1 and <= 1) return Alignment.InfieldIn;
        //  if (sit.RunnerOn(1) && outs < 2) return Alignment.DoublePlay;
        //  if (lead is >= 1 and <= 3 && sit.Inning >= sit.ScheduledInnings - 1)
        //      return Alignment.NoDoubles;

        return Alignment.Straight;
    }
}
