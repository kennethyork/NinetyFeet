using System.Collections.Generic;
using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Core;

/// <summary>What the CPU intends to do with a pitch it is about to see.</summary>
public struct SwingPlan
{
    public bool WillSwing;
    public bool Bunt;
    public float SwingAt;      // pitch progress at which the bat comes through
    public Vector2 Cursor;     // where the CPU thinks the ball will be

    /// <summary>
    /// How he is going after it. Real hitters shorten up with two strikes — more of the zone
    /// covered, less damage done. Without that the swing profile can be exactly right and the
    /// strikeout total still runs 40% over, because every two-strike swing is a full cut.
    /// </summary>
    public SwingType Type;
}

/// <summary>
/// The computer's decisions at the plate and on the mound. Shared by the live game scene and
/// the headless self-test so both exercise exactly the same logic.
/// </summary>
public static class CpuBrain
{
    /// <summary>Picks a pitch type and a target, favouring the pitcher's best offering.</summary>
    public static void ChoosePitch(GameSituation s, PlayerData pitcher, ref Rng rng,
        out PitchType type, out Vector2 aim)
    {
        // Ahead in the count, work off the plate; behind, come back over it. Tuned so that
        // roughly half of all pitches end up in the zone, as they do in a real game.
        bool ahead = s.Strikes > s.Balls;

        // Roughly 47% of these end up in the zone, against a real 49.2%. Closing that gap was
        // tried and is not worth it: walks are extremely sensitive to it — pulling the zone out
        // to 50.1% cost a quarter of the league's walks and bought only two points of strikeout
        // rate back, because a chase is not where the surplus strikeouts were coming from.
        float edge = ahead ? 1.55f : 1.10f;

        aim = new Vector2(
            rng.Range(-edge, edge),
            rng.Range(ahead ? 0.85f : 1.42f, ahead ? 4.3f : 3.58f));

        float roll = rng.NextFloat();

        // A signature move is thrown often enough to be the thing he is known for.
        var want = pitcher.Special switch
        {
            Special.Fireball when roll < 0.45f => PitchType.Fastball,
            Special.CrazyCurve when roll < 0.40f => PitchType.Curveball,
            Special.Corkscrew when roll < 0.40f => PitchType.Slider,
            Special.Knuckleball when roll < 0.55f => PitchType.Knuckler,
            _ => Choose(pitcher, roll, ahead),
        };

        // Never call for something he does not throw. ReleasePitch used to catch this and quietly
        // substitute a fastball, which meant an arm with no changeup threw a fastball every time
        // the brain asked for one — the more distinctive his repertoire, the more predictable he
        // became, which is precisely backwards.
        type = pitcher.Knows((int)want) ? want : PitchType.Fastball;
    }

    /// <summary>
    /// Picks from what this arm actually has.
    ///
    /// The fastball is the backbone and everything else is what he goes to; ahead in the count he
    /// reaches for the pitch that misses bats, and behind it he needs the one he can locate.
    /// </summary>
    private static PitchType Choose(PlayerData pitcher, float roll, bool ahead)
    {
        // Roughly a real major-league mix: a bit under half fastballs, the rest spread over
        // whatever secondary stuff he owns.
        if (roll < (ahead ? 0.36f : 0.50f)) return PitchType.Fastball;

        var secondary = new List<PitchType>();
        foreach (var t in pitcher.Arsenal)
            if (t != PitchType.Fastball) secondary.Add(t);

        if (secondary.Count == 0) return PitchType.Fastball;

        // Spread the remaining probability evenly across his secondary pitches, so a man with one
        // breaking ball leans on it and a man with three keeps a hitter guessing.
        float span = 1f - (ahead ? 0.36f : 0.50f);
        int at = Mathf.Clamp(
            Mathf.FloorToInt((roll - (ahead ? 0.36f : 0.50f)) / span * secondary.Count),
            0, secondary.Count - 1);

        return secondary[at];
    }

    /// <summary>Decides whether to offer at a pitch, and how well the swing will be timed.</summary>
    /// <param name="readError">
    /// Scales how badly the hitter reads the pitch. 1 is the calibrated default and is what every
    /// simulated game uses; difficulty moves it only when a human is pitching.
    /// </param>
    /// <summary>
    /// Where the bat actually goes, as opposed to what the hitter thought he saw.
    ///
    /// These were the same number, and that is why strikeouts and balls in play could never be
    /// tuned apart: one error decided both whether he swung and how well he connected, so every
    /// attempt to raise whiffs also emptied the field of batted balls. In reality a hitter who has
    /// been fooled into chasing a pitch out of the zone is far worse at squaring it up — the
    /// majors run about 85% contact inside the zone against 62% on a chase — while a hitter who
    /// correctly picked up a strike is much better than his overall read suggests.
    /// </summary>
    private static Vector2 BatAt(Pitch pitch, Vector2 guess, float read, ref Rng rng)
    {
        // Built from where the ball actually crosses, not from the hitter's guess: adding this on
        // top of the guess stacked one error on another and nearly doubled the total, which is why
        // a scale of 0.9 still produced twice the whiffs it should have.
        float scale = pitch.IsStrike ? 0.68f : 1.50f;
        return pitch.CrossPoint + new Vector2(
            (rng.Bell() - 0.5f) * read * scale,
            (rng.Bell() - 0.5f) * read * scale);
    }

    public static SwingPlan PlanSwing(GameSituation s, PlayerData batter, Pitch pitch, ref Rng rng,
        float readError = 1f)
    {
        float eye = batter.Contact / 10f;

        // The hitter reads the pitch imperfectly; a good eye reads it better. This error has to
        // stay small next to the bat's sweet spot, or nobody ever squares one up.
        // Compressed on purpose. When this ranged 2.7x between the best and worst eye, it — far
        // more than the bat's sweet spot — was what let elite hitters bat .700.
        // The wrong side of the platoon is mostly a reading problem — the breaking ball moves away
        // from him rather than toward him — so it belongs in the read error, which is also what
        // makes a badly matched hitter chase.
        float read = (1.25f - eye * 0.5f) * 2.1f * readError
                     * Platoon.ReadPenalty(batter, pitch.Pitcher);
        Vector2 guess = pitch.CrossPoint + new Vector2(
            (rng.Bell() - 0.5f) * read,
            (rng.Bell() - 0.5f) * read);

        // Judge against the real zone — the read error above already supplies the uncertainty.
        // Padding this out as well made hitters swing at nearly everything off the plate.
        bool looksLikeStrike = Mathf.Abs(guess.X) <= Pitch.ZoneHalfWidth &&
                               guess.Y >= Pitch.ZoneBottom &&
                               guess.Y <= Pitch.ZoneTop;

        // Swing rates, against the real ones: hitters offer at about 68% of strikes and chase
        // about 31% of balls.
        float swingChance = looksLikeStrike ? 0.60f + eye * 0.14f : 0.19f + (1f - eye) * 0.10f;

        if (s.Strikes >= 2)
        {
            // With two strikes a hitter widens what he is willing to defend. He is not judging
            // whether the pitch is a strike any more — he is judging whether an umpire might say
            // it is, and swinging at anything close rather than being rung up.
            //
            // Without this, every borderline pitch his read put just outside the zone became a
            // called third strike, and strikeouts ran 16% over while the whiff rate per swing was
            // slightly *below* the real one. The surplus was not swings and misses at all; it was
            // men walking back to the dugout with the bat on their shoulder.
            const float Protect = 1.10f;
            looksLikeStrike = Mathf.Abs(guess.X) <= Pitch.ZoneHalfWidth * Protect &&
                              guess.Y >= Pitch.ZoneBottom - (Pitch.ZoneTop - Pitch.ZoneBottom) * 0.08f &&
                              guess.Y <= Pitch.ZoneTop + (Pitch.ZoneTop - Pitch.ZoneBottom) * 0.08f;

            // Protecting the plate means refusing to be struck out looking — not hacking at
            // anything. The old floor applied 88% to every pitch including obvious balls, so a
            // two-strike count became a chase at nearly everything, and those are precisely the
            // swings that miss. Measured at 88% against a real 45%, and it was the single biggest
            // reason strikeouts ran 40% over.
            swingChance = looksLikeStrike
                ? Mathf.Max(swingChance, 0.93f)
                : Mathf.Min(0.47f, swingChance * 1.55f);
        }

        if (s.Balls >= 3 && !looksLikeStrike) swingChance *= 0.45f;           // take the free pass

        var plan = new SwingPlan
        {
            WillSwing = rng.Chance(swingChance),
            Bunt = false,
            Type = s.Strikes >= 2 ? SwingType.Protect : SwingType.Normal,
            // Timing error is expressed in seconds and converted to pitch progress, so changing
            // how long a pitch takes to arrive does not silently change how well the CPU hits.
            SwingAt = 1f + (rng.Bell() - 0.5f) * (0.27f - eye * 0.16f)
                          / Mathf.Max(pitch.FlightTime, 0.05f),
            Cursor = BatAt(pitch, guess, read, ref rng),
        };

        // A pitcher with a runner on and an out to give will lay one down.
        if (s.Outs < 2 && s.RunnerOn(1) && batter.Position == Data.Position.P && rng.Chance(0.35f))
        {
            plan.Bunt = true;
            plan.WillSwing = true;
        }

        return plan;
    }

    /// <summary>How worn down a pitcher is, from his pitch count and stamina.</summary>
    public static float Fatigue(PlayerData pitcher, int pitchesThrown) =>
        Mathf.Clamp(pitchesThrown / (12f + pitcher.Stamina * 9f), 0f, 1f);

    /// <summary>
    /// The pitch count at which the manager starts looking to the bullpen.
    ///
    /// A reliever is not a starter with less stamina — he is a man hired to get through one inning
    /// at full effort and then sit down. Running the staff off one shared formula meant a closer
    /// with a three stamina was left in for forty-three pitches, which is three innings of work
    /// nobody asks of him.
    /// </summary>
    public static int PitchLimit(PlayerData pitcher) => pitcher.Role switch
    {
        StaffRole.Closer => 20,
        StaffRole.Setup => 22,
        StaffRole.Middle => 26,
        StaffRole.Long => 46,

        // A starter carries about six innings — roughly sixty per cent of the night's pitches.
        // At 22 + 7 he was handing over before the fifth and the pen was throwing half the game.
        _ => 26 + pitcher.Stamina * 8,
    };

    /// <summary>A lead small enough to be worth protecting with the back of the bullpen.</summary>
    public static bool IsSaveSituation(GameSituation s)
    {
        int lead = s.FieldingScore - s.BattingScore;
        return lead > 0 && lead <= 3;
    }

    /// <summary>
    /// The job the moment calls for.
    ///
    /// A first version asked only how many innings were left, which meant that in the sixth — where
    /// a reliever almost always first appears — four innings remained and the answer was always
    /// "long relief". One long man then threw three innings and the game was over, so setup men
    /// and closers appeared in one game in twenty. Long relief is not what you want in the sixth;
    /// it is what you want when the starter has been knocked out in the third.
    /// </summary>
    public static StaffRole RoleFor(GameSituation s)
    {
        int inningsLeft = Mathf.Max(0, s.ScheduledInnings - s.Inning + 1);
        bool save = IsSaveSituation(s);

        if (save && inningsLeft <= 1) return StaffRole.Closer;
        if (save && inningsLeft <= 2) return StaffRole.Setup;
        if (s.Inning <= 5) return StaffRole.Long;
        return StaffRole.Middle;
    }

    /// <summary>
    /// Whether the man on the mound comes out, and who replaces him. Shared by the simulated game,
    /// the headless harness and the live scene so a bullpen is run the same way everywhere.
    /// </summary>
    public static PlayerData Relieve(GameSituation sit, int pitchesThrown)
    {
        var current = sit.FieldingTeam.CurrentPitcher;
        if (current == null) return null;

        var wanted = RoleFor(sit);
        bool spent = pitchesThrown >= PitchLimit(current);

        // The ninth inning of a one-run game belongs to the closer whether or not the man out
        // there still has something left. That is the whole point of having one.
        bool wrongMan = current.Role != StaffRole.Starter
                        && wanted is StaffRole.Closer or StaffRole.Setup
                        && current.Role != wanted
                        && pitchesThrown >= 10;

        if (!spent && !wrongMan) return null;

        // Who is actually coming to the plate decides which arm it should be.
        return sit.FieldingTeam.NextArm(wanted, IsSaveSituation(sit), sit.Batter);
    }
}
