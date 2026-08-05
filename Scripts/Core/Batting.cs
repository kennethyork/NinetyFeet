using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Core;

public enum SwingResult { Miss, Foul, InPlay }

/// <summary>
/// How the batter went after it. Trading plate coverage against power is the central decision
/// in a modern baseball game, so it is a real choice here rather than one swing for everything.
/// </summary>
public enum SwingType
{
    Contact,
    Normal,
    Power,

    /// <summary>
    /// Shortening up with two strikes. Deliberately much milder than a full contact swing: using
    /// that profile here more than undid the sweet-spot narrowing and sent BABIP to .366.
    /// </summary>
    Protect,
}

/// <summary>Per-swing modifiers for each swing type.</summary>
public readonly struct SwingProfile
{
    public readonly float Barrel;    // multiplier on the sweet spot
    public readonly float Window;    // multiplier on the timing window
    public readonly float Exit;      // multiplier on exit velocity
    public readonly float Launch;    // degrees added to launch angle

    private SwingProfile(float barrel, float window, float exit, float launch)
    {
        Barrel = barrel; Window = window; Exit = exit; Launch = launch;
    }

    public static SwingProfile For(SwingType type) => type switch
    {
        // A contact swing covers far more of the zone but does not drive the ball.
        SwingType.Contact => new SwingProfile(1.34f, 1.26f, 0.87f, -3f),
        // A little more of the zone covered, a little less damage — not a different swing.
        // Mild on purpose, and it has to stay that way. Widening it to 1.30 with the exit
        // velocity cut to 0.86 to compensate was tried: strikeouts fell 3%, but BABIP went to
        // .314 and hits rose 19%. Weak contact in this model still finds grass, so trading a
        // strikeout for a softly struck ball is not the bargain it looks like.
        SwingType.Protect => new SwingProfile(1.13f, 1.10f, 0.95f, -1f),
        // A power swing is all or nothing: a small barrel, but it leaves in a hurry.
        SwingType.Power => new SwingProfile(0.70f, 0.80f, 1.13f, 4f),
        _ => new SwingProfile(1f, 1f, 1f, 0f),
    };

    public static string Label(SwingType type) => type switch
    {
        SwingType.Contact => "Contact",
        SwingType.Protect => "Protect",
        SwingType.Power => "Power",
        _ => "Normal",
    };
}

/// <summary>A ball off the bat, in the terms the field simulation needs.</summary>
public struct BattedBall
{
    public float ExitVelocity;   // feet per second
    public float LaunchAngle;    // degrees above horizontal
    public float SprayAngle;     // degrees from straightaway centre; negative is toward left field
    public float Quality;        // 0..1, how well it was struck
    public bool WasBunt;

    // Always filled in, even on a swing and miss, so the game can tell the hitter what went
    // wrong. Negative timing is early, positive is late; 0 is dead on.
    public float TimingNorm;
    public bool MadeContact;

    /// <summary>
    /// Where the bat was relative to the ball, in barrel radii. Positive X is toward the
    /// right-handed hitter's outside corner; positive Y is above the ball.
    ///
    /// Timing was reported and placement was not, which meant half of every swing was invisible.
    /// A hitter told only that he was late cannot tell whether he was also under it by a foot.
    /// </summary>
    public float MissX, MissY;

    /// <summary>Plain-language verdict on where the bat was, when it was not close enough.</summary>
    public string PlacementVerdict
    {
        get
        {
            float ax = Mathf.Abs(MissX), ay = Mathf.Abs(MissY);
            if (ax < 0.45f && ay < 0.45f) return "on it";

            string v = ay >= 0.45f ? (MissY > 0f ? "over it" : "under it") : "";
            string h = ax >= 0.45f ? (MissX > 0f ? "outside" : "inside") : "";

            if (v != "" && h != "") return $"{v}, {h}";
            return v != "" ? v : h;
        }
    }

    /// <summary>Plain-language verdict on the timing, the way a batting practice readout reads.</summary>
    public string TimingVerdict => TimingNorm switch
    {
        < -0.75f => "Way early",
        < -0.35f => "Early",
        < -0.12f => "A shade early",
        <= 0.12f => "Perfect timing",
        <= 0.35f => "A shade late",
        <= 0.75f => "Late",
        _ => "Way late",
    };

    /// <summary>Plain-language label used by the announcer line.</summary>
    public string Descriptor
    {
        get
        {
            if (WasBunt) return "bunt";
            if (LaunchAngle < 5f) return "ground ball";
            if (LaunchAngle < 22f) return "line drive";
            if (LaunchAngle < 45f) return "fly ball";
            return "pop up";
        }
    }
}

public static class SwingResolver
{
    /// <summary>
    /// How well a swing must be struck, on the real bat, to touch the ball at all.
    ///
    /// This sat at 0.30, which almost nothing failed: the CPU put 43.1% of its swings into play
    /// against a real 36.7% and missed on 19.6% against a real 24.9%. Too many balls reaching
    /// play is the single reason the league ran 23.5% over on hits — and the extra baserunners
    /// stretched innings far enough to push the strikeout count 26% over as well, even though the
    /// strikeout *rate* per plate appearance was below the real one. One number caused both.
    /// </summary>
    private const float WhiffFloor = 0.355f;

    /// <summary>The sweet-spot radius in plate-plane feet, for drawing the coverage indicator.</summary>
    /// <summary>
    /// How much of the plate a hitter's bat covers, and what the reticle draws.
    ///
    /// The rating's share of this is deliberately larger for a human than for the simulation. The
    /// base carries most of the ability in the resolver on purpose — small rating gaps must not
    /// become enormous win gaps across a league of simulated games — but that same compression
    /// meant your best hitter and your backup catcher felt nearly identical in your hands, and
    /// feeling the difference between them is most of why a roster is interesting.
    ///
    /// The assist is only ever anything other than 1 for a human, so steepening the spread by it
    /// widens the gap where a person can feel it and leaves every league statistic exactly where
    /// it was measured.
    /// </summary>
    /// <summary>
    /// The radius inside which a well-timed swing actually squares the ball up, in feet.
    ///
    /// This is the number the reticle should be built around, and until now nothing drew it. The
    /// bracket was pinned at 0.42 of the contact radius — a figure that corresponds to nothing in
    /// the resolver, chosen because a ring the size of the real bat buried the pitch behind it.
    ///
    /// Derived rather than picked. With perfect timing the resolver scores a swing at
    /// <c>0.68 · trueSpatial^1.6 + 0.32</c>, and a struck ball needs about 0.60 of that, which
    /// works back to being within roughly 43% of the true barrel. Note what it is *not* multiplied
    /// by: the assist. Difficulty widens the region where you make contact and never widens the
    /// region where you do damage, and a hitter should be able to see that.
    /// </summary>
    public static float SquareUpRadius(PlayerData batter, SwingType type, float platoon = 1f)
    {
        float barrelTrue = (0.505f + batter.Contact / 10f * 0.125f)
                           * SwingProfile.For(type).Barrel * platoon;
        if (batter.Special == Special.ContactMaster) barrelTrue *= 1.35f;
        return barrelTrue * 0.43f;
    }

    public static float BarrelRadius(PlayerData batter, float assist, SwingType type)
    {
        float rated = batter.Contact / 10f;

        // At assist == 1 this is the old expression to the digit; above it, ability leans harder.
        float lean = 1f + (assist - 1f) * 0.85f;
        float barrel = (0.50f + rated * 0.26f * lean) * assist * SwingProfile.For(type).Barrel;

        if (batter.Special == Special.ContactMaster) barrel *= 1.35f;
        return barrel;
    }

    /// <summary>
    /// Resolves a swing. <paramref name="swingProgress"/> is the pitch's progress toward the
    /// plate when the bat came through — 1.0 is dead on.
    /// </summary>
    /// <param name="assist">Widens the bat's sweet spot — the placement half of the skill.</param>
    /// <param name="timingAssist">
    /// Widens the timing window. Kept separate from <paramref name="assist"/> because a player
    /// aiming with a mouse is tracking a moving target and clicking on a beat with one hand;
    /// placement is the part that input is good at, timing is the part it fights.
    /// </param>
    public static SwingResult Resolve(
        PlayerData batter, Pitch pitch, float swingProgress, Vector2 cursor,
        ref Rng rng, out BattedBall ball, float assist = 1f, SwingType type = SwingType.Normal,
        float timingAssist = 1f)
    {
        ball = default;
        var profile = SwingProfile.For(type);

        float contact = batter.Contact / 10f;
        float power = batter.Power / 10f;
        bool contactMaster = batter.Special == Special.ContactMaster;

        // The left-right matchup. A hitter facing the opposite hand sees the ball longer and the
        // breaking stuff moves toward him instead of away, and it is worth real points of average.
        // Applied to the bat rather than bolted onto the result, so it shows up where it actually
        // comes from — a man on the wrong side of it squares fewer balls up, he is not handed a
        // worse outcome after hitting the ball the same way.
        float platoon = Platoon.Factor(batter, pitch.Pitcher);

        // --- Timing: how many seconds early or late the swing was. ---
        float timingSec = (swingProgress - 1f) * pitch.FlightTime;
        // Ratings are deliberately compressed. Real hitters range from about .200 to .330, not
        // .150 to .776 — giving contact full leverage over both the timing window and the sweet
        // spot let a 10-rated hitter square up virtually everything he swung at.
        // Two tiers. The assisted values decide whether you make contact at all — that is the
        // part that should be forgiving. The true, unassisted values decide how WELL you struck
        // it. Letting the assist inflate quality too meant a helped swing produced a 110 mph
        // rocket every time, so there was no gradient between a scraped foul and a screamer.
        // Base contact was tuned against a reference that claimed the majors strike out 16.6 times
        // a game. The official 2024 figure is 13.3 — see RealBaseball — so hitters here were made
        //25% easier to strike out than real ones to hit a target that never existed.
        // Rating sensitivity is deliberately shallow. Clubs differ by only about 5% in paper
        // strength, yet a measured season produced a talent spread of 0.161 in win percentage
        // against roughly 0.070 in the majors — the simulation was turning small rating gaps into
        // enormous win gaps, and the best club finished 31-2. The base carries most of the ability;
        // the rating tilts it.
        float windowTrue = (0.066f + contact * 0.022f) * profile.Window * platoon;
        if (contactMaster) windowTrue *= 1.4f;
        float window = windowTrue * timingAssist;

        float timingQuality = 1f - Mathf.Abs(timingSec) / window;

        // Reported whatever happens, so a miss still tells the hitter how far off he was.
        ball.TimingNorm = Mathf.Clamp(timingSec / window, -2f, 2f);

        // --- Placement: how close the bat was to the ball in the plate plane. ---
        Vector2 crossing = pitch.CrossPoint;
        Vector2 delta = cursor - crossing;
        // A little forgiveness horizontally — the bat is long.
        float spatialDist = new Vector2(delta.X * 0.72f, delta.Y).Length();
        // Whiff-per-swing was running at 12% against a real 23%: hitters were making contact on
        // nearly everything, so far too many balls reached play and the hit total ran 18% over
        // even with BABIP correct. The sweet spot has to be small enough to be missed.
        float barrelTrue = (0.505f + contact * 0.125f) * profile.Barrel * platoon;
        if (contactMaster) barrelTrue *= 1.35f;
        // The assisted bat leans on ability harder than the true one — see BarrelRadius. At
        // assist == 1 this is exactly barrelTrue, so the simulation is untouched; above it, a good
        // hitter's advantage in a human's hands grows. The reticle is drawn from the same
        // expression, so what is on screen stays what the swing uses.
        float lean = 1f + (assist - 1f) * 0.85f;
        float barrel = (0.505f + contact * 0.125f * lean) * profile.Barrel * platoon * assist;
        if (contactMaster) barrel *= 1.35f;

        // Reported whatever happens, the same as the timing, so a miss tells the hitter both what
        // went wrong and in which direction.
        ball.MissX = delta.X / barrel;
        ball.MissY = delta.Y / barrel;

        float spatialQuality = 1f - spatialDist / barrel;

        if (timingQuality <= 0f || spatialQuality <= 0f) return SwingResult.Miss;

        // Whether you make contact at all is forgiving — you rarely whiff outright.
        float quality = timingQuality * 0.45f + spatialQuality * 0.55f;
        if (quality < 0.30f) return SwingResult.Miss;

        // How WELL you struck it is a different, much sharper question, driven mostly by how
        // close to the middle of the bat the ball was. Making contact easy while leaving the
        // outcome uncontrollable is what made hitting feel simultaneously trivial and useless:
        // every swing produced the same mush. Squaring it up now has to be earned.
        // Judged against the real bat and the real window, so precision — not the assist — is
        // what earns a hard-hit ball. For the CPU the two tiers are identical (assist == 1), so
        // the simulation's balance is untouched.
        float trueSpatial = Mathf.Clamp(1f - spatialDist / barrelTrue, 0f, 1f);
        float trueTiming = Mathf.Clamp(1f - Mathf.Abs(timingSec) / windowTrue, 0f, 1f);
        float squared = Mathf.Pow(trueSpatial, 1.6f) * 0.68f + Mathf.Pow(trueTiming, 1.4f) * 0.32f;

        // --- Whiffs. ---
        // The assist was buying total immunity from missing: a human at Pro swung and missed on
        // 0.0% of swings against a real 23%, and a sharp one missed on 0.0% at every level up to
        // and including Legend. With no way to lose the bat entirely, the only thing left to
        // punish a bad swing was a foul, so climbing the difficulty ladder bought fouls rather
        // than risk — an average hitter went from 30% fouls at Pro to 54% at Legend while whiffs
        // crawled from 0% to 8%. Fouling one off is the least satisfying way to be beaten: the
        // at-bat does not resolve and the hitter learns nothing.
        //
        // So a swing that was genuinely bad on the real bat is a miss, and the assist decides how
        // often you are rescued from it rather than whether the miss exists. At assist == 1 the
        // rescue is zero and no roll is taken, so the CPU's swing — and every league statistic
        // measured against it — is untouched.
        if (trueTiming * 0.45f + trueSpatial * 0.55f < WhiffFloor)
        {
            float rescue = WhiffRescue(assist);
            if (rescue <= 0f || !rng.Chance(rescue)) return SwingResult.Miss;
        }

        // --- Where it goes. ---
        // Timing is only part of it. If spray came from timing alone, every well-struck ball
        // would go dead centre, because good contact means near-zero timing error. Location on
        // the plate matters just as much: a pitch on the hitter's hands gets pulled, one on the
        // outer half goes the other way. Plate X and field X share a sign, so this works for
        // both left and right handed hitters without a special case.
        float pullSign = batter.Bats == Handedness.Right ? 1f : -1f;
        float timingNorm = Mathf.Clamp(timingSec / window, -1.6f, 1.6f);

        float timingSpray = timingNorm * 38f * pullSign;
        float locationSpray = Mathf.Clamp(crossing.X, -1.6f, 1.6f) * 40f;

        // A pull-happy slugger yanks it; a spray hitter uses the whole field.
        float tendency = batter.Archetype == Archetype.Slugger ? -14f * pullSign : 0f;
        if (batter.Special == Special.SprayHitter) tendency = 0f;

        float spray = timingSpray + locationSpray + tendency + (rng.Bell() - 0.5f) * 34f;

        // Anything past the foul lines is gone outright, so a wide spray is a hidden second foul
        // rule. Pull an assisted swing's spray in a little, or a corner pitch struck cleanly
        // still sails foul through no fault of the hitter.
        spray *= Mathf.Lerp(1f, 0.82f, Mathf.Clamp((assist - 1f) / 0.6f, 0f, 1f));

        // A swing has a slight uppercut, so squaring one up already lifts it. Squared-up contact
        // needs to land near 26 degrees — the angle that actually carries — otherwise the only
        // way to elevate is to miss the ball's centre, which kills exit velocity at the same time.
        // A wider spread of launch angles, so fewer balls land in the ideal carrying band and
        // the park does not give up five home runs a night.
        float launch = 24f + profile.Launch - delta.Y * 22f + (rng.Bell() - 0.5f) * 22f;
        if (batter.Special == Special.MoonShot && squared > 0.7f) launch += 12f;
        launch = Mathf.Clamp(launch, -22f, 62f);

        // Tuned so an average ball leaves the bat around 85 mph and the very best around 112,
        // which lands average carry near 300 feet against the drag model above.
        // A spray hitter goes with the pitch instead of trying to yank everything.
        if (batter.Special == Special.SprayHitter) spray *= 0.55f;

        // Calibrated against measured Statcast 2024: exit velocity averages 82.5 mph, p90 is
        // 102 and the record is 120. Quality has to dominate the flat power term, or a maximum
        // power hitter leaves the bat at 115 every single swing and turns any small park into a
        // home run derby — which is exactly what happened when power was a multiplier.
        // The gap in average exit velocity between the league's best power hitter and its worst
        // is roughly ten mph in reality, not twenty-five.
        // Exit velocity keys off how squarely it was struck, not the lenient contact score, so
        // finding the sweet spot is visibly rewarded and edge contact is visibly punished.
        // A lower floor and a steeper climb, so mishit balls actually come off slowly instead of
        // everything leaving the bat hot. Keeps the top end where it was.
        // Widening the contact window to fix strikeouts also made every ball harder-struck, which
        // pushed hits, doubles and homers well past real rates. Quality is eased back to
        // compensate: the same number of balls in play, struck a little less well.
        // Weight the barrel harder and the floor softer. A flat rise in exit velocity lifts weak
        // contact into cheap singles as much as it lifts barrels over the fence, which is how the
        // sim ended up with 11% too many hits and 23% too few home runs at the same time.
        // Steeper still: a barrelled ball has to carry, and a mis-hit has to die. A shallow curve
        // gives a league of bloop singles — high BABIP, no extra-base hits, and no runs.
        // Base raised to compensate for power's smaller share, but 52 overshot the old average
        // and homers went 61% over. Average power is about 0.5, so this keeps the league mean
        // where it was while narrowing the gap between a slugger and a singles hitter.
        // Trimmed at the barrel end rather than the base. Homers were running 9% over while balls
        // in play were right, so what needed taking off was the top of the range — the difference
        // between a ball that clears the fence and one caught on the track — not the weak contact,
        // which was landing exactly where it should.
        float mph = (48.5f + power * 8f + squared * 66.5f + (rng.Bell() - 0.5f) * 15f) * profile.Exit;
        if (batter.Special == Special.MoonShot && squared > 0.7f) mph *= 1.10f;
        // Gap power: line drives in the 8-to-28 degree band really carry.
        if (batter.Special == Special.GapPower && launch is > 8f and < 28f) mph *= 1.14f;

        float reportedTiming = ball.TimingNorm;
        ball = new BattedBall
        {
            ExitVelocity = mph * 1.46667f,
            LaunchAngle = launch,
            SprayAngle = Mathf.Clamp(spray, -62f, 62f),
            Quality = squared,
            TimingNorm = reportedTiming,
            MadeContact = true,
        };

        // Anything sprayed past the foul lines is gone foul outright.
        if (Mathf.Abs(ball.SprayAngle) > 45f) return SwingResult.Foul;

        // Mistiming mostly, but not always, fouls one off. A probability rather than a hard cut,
        // so a pulled or opposite-field ball can still find grass instead of every fair ball
        // being one that was struck dead centre.
        float mistimed = Mathf.Abs(timingNorm);
        float foulChance = Mathf.Clamp((mistimed - 0.06f) * 2.30f, 0f, 0.86f)
                         + Mathf.Clamp((0.46f - squared) * 0.75f, 0f, 0.34f);

        // Fouling off pitch after pitch is the least satisfying way to be punished — the at-bat
        // never resolves. An assisted swing therefore gets the same forgiveness on fouls that it
        // already gets on contact. The CPU swings with assist == 1 and is untouched, which keeps
        // the league's strikeout and BABIP rates on their measured calibration.
        foulChance *= FoulForgiveness(assist);
        if (batter.Special == Special.SprayHitter) foulChance *= 0.6f;

        if (rng.Chance(foulChance)) return SwingResult.Foul;

        return SwingResult.InPlay;
    }

    /// <summary>
    /// How much of the normal foul rate an assisted swing pays. Exactly 1 for the CPU, so the
    /// league calibration is the baseline; a discount for anyone with a hand on the bat.
    ///
    /// This used to scale smoothly from the assist, which meant Legend — where the assist is
    /// 1.02 — got a discount of 2% and fouled off 54% of an average player's swings. The point
    /// of a hard level is that missing is likelier, not that nothing ever resolves, so the
    /// discount now starts as soon as a human is swinging and deepens with the help.
    /// </summary>
    private static float FoulForgiveness(float assist) =>
        assist <= 1f ? 1f : Mathf.Lerp(0.72f, 0.44f, Mathf.Clamp((assist - 1f) / 0.9f, 0f, 1f));

    /// <summary>
    /// How often the assist saves a swing that the real bat would have missed. Zero for the CPU,
    /// which is what keeps simulated baseball on its measured numbers, and generous on Rookie.
    /// </summary>
    private static float WhiffRescue(float assist) =>
        assist <= 1f ? 0f : Mathf.Lerp(0.30f, 0.88f, Mathf.Clamp((assist - 1f) / 0.9f, 0f, 1f));

    /// <summary>A bunt: soft contact, low launch, placed roughly where the batter aimed.</summary>
    public static SwingResult ResolveBunt(
        PlayerData batter, Pitch pitch, float swingProgress, Vector2 cursor,
        ref Rng rng, out BattedBall ball)
    {
        ball = default;

        float timingSec = (swingProgress - 1f) * pitch.FlightTime;
        // Bunting is forgiving on timing but needs the bat near the ball.
        if (Mathf.Abs(timingSec) > 0.16f) return SwingResult.Miss;
        if (Mathf.Abs(cursor.Y - pitch.CrossPoint.Y) > 1.3f) return SwingResult.Miss;

        float spray = Mathf.Sign(cursor.X == 0f ? 1f : cursor.X) * rng.Range(18f, 40f);
        bool master = batter.Special == Special.BuntMaster;

        ball = new BattedBall
        {
            ExitVelocity = rng.Range(22f, 42f),
            LaunchAngle = rng.Range(2f, 9f),
            SprayAngle = Mathf.Clamp(spray, -44f, 44f),
            Quality = master ? 0.8f : 0.45f,
            WasBunt = true,
        };

        // A bunt master never rolls one foul.
        if (!master && rng.Chance(0.22f)) return SwingResult.Foul;
        return SwingResult.InPlay;
    }
}
