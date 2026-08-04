using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Core;

/// <summary>
/// Stolen bases.
///
/// The statistic existed, was saved to disk and was displayed on player cards — and nothing in the
/// game ever incremented it, so every player in the league finished every season with none. Real
/// baseball runs about 1.5 steals a game at a success rate close to 80%, and it is one of the
/// pillars of the sport rather than a detail.
/// </summary>
public static class Baserunning
{
    /// <summary>Result of one attempt, for the game to narrate.</summary>
    public readonly struct StealAttempt
    {
        public readonly bool Attempted, Safe;
        public readonly int FromBase;
        public readonly PlayerData Runner;

        public StealAttempt(bool attempted, bool safe, int fromBase, PlayerData runner)
        {
            Attempted = attempted; Safe = safe; FromBase = fromBase; Runner = runner;
        }

        public static readonly StealAttempt None = new(false, false, 0, null);
    }

    /// <summary>
    /// How willing a runner is to go, per pitch.
    ///
    /// Note the per-pitch part: this is offered before every delivery, so a runner standing on
    /// first through a six-pitch at-bat gets six chances at it. Sized as a per-opportunity rate it
    /// produced 5.3 steals a game against a real 1.5.
    /// </summary>
    /// Sized against a league that saw 3.57 pitches a plate appearance. Two-strike protection
    /// pushed that to 3.77, and because this is offered per pitch rather than per at-bat, steals
    /// rose 21% without anybody changing how willing a runner is to go.
    ///
    /// It cuts the other way too. Giving pitchers real repertoires shortened at-bats — a man with
    /// a sinker and a changeup gets outs sooner than one throwing four pitches at random — and
    /// fewer pitches an at-bat meant fewer chances offered, which cost 16% of the league's steals
    /// without anybody touching baserunning. This is sized back up to meet it.
    private static float Willingness(PlayerData runner) =>
        Mathf.Clamp((runner.Speed - 4.5f) / 10f * 0.114f, 0f, 0.114f);

    /// <summary>
    /// Chance the throw beats him. A catcher's arm matters, but a fast runner beats most of them —
    /// the real league throws out roughly one in five.
    /// </summary>
    private static float SafeChance(PlayerData runner, PlayerData catcher, int toBase)
    {
        float legs = runner.Speed / 10f;
        float arm = catcher?.Arm / 10f ?? 0.5f;

        // Third is a longer run and a shorter throw, so it is a harder base to take.
        float baseline = toBase == 3 ? 0.68f : 0.80f;
        return Mathf.Clamp(baseline + (legs - 0.5f) * 0.34f - (arm - 0.5f) * 0.26f, 0.30f, 0.95f);
    }

    /// <summary>The runner who would go, or 0 if nobody can. Only into an empty base.</summary>
    public static int LeadRunner(GameSituation sit) =>
        sit.RunnerOn(2) && !sit.RunnerOn(3) ? 2
        : sit.RunnerOn(1) && !sit.RunnerOn(2) ? 1
        : 0;

    /// <summary>
    /// Offers the lead runner a chance to go before a pitch. Returns what happened so the caller
    /// can narrate it; the situation is already updated.
    /// </summary>
    /// <param name="forced">
    /// The human manager has flashed the steal sign, so the runner goes regardless of whether the
    /// computer would have sent him. Without this a human had no way to steal a base at all —
    /// the computer ran on him a hundred and fifty times a season and he could not answer.
    /// </param>
    public static StealAttempt TryStealBeforePitch(GameSituation sit, ref Rng rng, bool forced = false)
    {
        int from = LeadRunner(sit);
        if (from == 0) return StealAttempt.None;

        var runner = sit.Runners[from];
        if (runner == null) return StealAttempt.None;

        if (forced)
        {
            var sentCatcher = sit.FieldingTeam.Fielder(Position.C);
            bool made = rng.Chance(SafeChance(runner, sentCatcher, from + 1));
            if (made) sit.CompleteSteal(from);
            else sit.CaughtStealing(from);
            return new StealAttempt(true, made, from, runner);
        }

        float chance = Willingness(runner);

        // Nobody runs when the game is out of reach, and everybody runs when a base is worth more:
        // two out and a run needed makes the extra ninety feet cheap.
        if (sit.Outs == 2) chance *= 1.25f;
        if (from == 2) chance *= 0.45f;              // stealing third is rarer

        if (!rng.Chance(chance)) return StealAttempt.None;

        var catcher = sit.FieldingTeam.Fielder(Position.C);
        bool safe = rng.Chance(SafeChance(runner, catcher, from + 1));

        if (safe) sit.CompleteSteal(from);
        else sit.CaughtStealing(from);

        return new StealAttempt(true, safe, from, runner);
    }
}
