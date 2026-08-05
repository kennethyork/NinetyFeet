using System.Linq;
using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Core;

/// <summary>
/// Follows a ground ball through the defence, one stage at a time.
///
/// The league-level symptom has been known for a while: of every ball in play not caught on the
/// fly, 95% goes down as a hit, and there are 0.05 double plays a game against a real 1.44. What
/// has not been known is which stage loses them — whether the infielder never reaches the ball,
/// reaches it and decides not to throw, throws and finds nobody covering, or throws too late.
///
/// Those are four different bugs with four different fixes and the aggregate cannot tell them
/// apart, so this counts each one separately on the same set of batted balls.
/// </summary>
public static class InfieldAudit
{
    public static void Run(int trials)
    {
        var away = RosterGenerator.For(Teams.Get(0));
        var home = RosterGenerator.For(Teams.Get(1));

        int grounders = 0, reached = 0, threw = 0, outs = 0, hits = 0;
        int inPlay = 0, lowAngle = 0;
        var angles = new int[12];
        int stillLoose = 0, heldTheBall = 0;
        float sumFieldTime = 0f, sumBatterTime = 0f;

        var play = new PlaySimulation();

        for (int n = 0; n < trials; n++)
        {
            var rng = new Rng(21_000 + n);
            var sit = new GameSituation();
            sit.Start(away, home, 9);

            var pitcher = sit.FieldingTeam.CurrentPitcher;
            var batter = sit.Batter;
            if (pitcher == null || batter == null) continue;

            CpuBrain.ChoosePitch(sit, pitcher, ref rng, out var type, out var aim);
            var pitch = PitchFactory.Create(pitcher, type, aim, 0f, ref rng);
            var plan = CpuBrain.PlanSwing(sit, batter, pitch, ref rng);
            if (!plan.WillSwing) continue;

            var result = SwingResolver.Resolve(batter, pitch, plan.SwingAt, plan.Cursor, ref rng,
                out var ball, type: plan.Type);
            if (result != SwingResult.InPlay) continue;

            // Every ball in play goes in the histogram; only the ones on the ground go through
            // the funnel below.
            inPlay++;
            int bucket = Mathf.Clamp(Mathf.FloorToInt((ball.LaunchAngle + 30f) / 10f), 0, 11);
            angles[bucket]++;
            if (ball.LaunchAngle < 10f) lowAngle++;

            // The sim's own threshold for a ground ball, not an invented one.
            if (ball.LaunchAngle >= 5f || ball.WasBunt) continue;
            grounders++;

            play.Begin(sit, ball, 55_000 + n);

            bool everHeld = false;
            bool everThrew = false;
            float fieldedAt = -1f;

            int frames = 0;
            while (!play.Finished && frames++ < 2400)
            {
                play.Update(1f / 60f);

                if (!everHeld && play.Phase == PlayPhase.Held)
                {
                    everHeld = true;
                    fieldedAt = frames / 60f;
                }
                if (!everThrew && play.Phase == PlayPhase.Throwing) everThrew = true;
            }

            if (everHeld) { reached++; sumFieldTime += fieldedAt; }
            if (everThrew) threw++;

            // How long the batter had. Home to first is the clock every infield play runs against.
            var runner = play.Runners.FirstOrDefault(r => r.IsBatter);
            if (runner != null)
                sumBatterTime += FieldGeometry.BasePathLength / Mathf.Max(runner.BaseSpeed, 1f);

            if (play.Outcome.IsHit) hits++;
            if (play.Outcome.Outs > 0) outs++;

            // Reached it, never threw it: the fielder decided there was no play and held on.
            if (everHeld && !everThrew) heldTheBall++;
            if (!everHeld) stillLoose++;
        }

        GD.Print($"\n=== THE INFIELD — {inPlay} balls in play, {grounders} of them on the ground ===\n");

        // Where the bat is actually putting the ball. This is the first thing to check, because
        // an infield that never sees a ground ball is not an infield problem.
        GD.Print("  launch angle, in ten-degree bands from -30 up:");
        for (int b = 0; b < angles.Length; b++)
        {
            int lo = -30 + b * 10;
            GD.Print($"    {lo,4} to {lo + 10,4}   {angles[b] * 100f / Mathf.Max(inPlay, 1),5:F1}%" +
                     $"   {new string('#', Mathf.RoundToInt(angles[b] * 60f / Mathf.Max(inPlay, 1)))}");
        }
        GD.Print($"\n  under 10 degrees: {lowAngle * 100f / Mathf.Max(inPlay, 1):F1}%   " +
                 $"(real baseball is around 45%, and about half of those are ground balls)");

        if (grounders == 0)
        {
            GD.Print("\n  NO GROUND BALLS AT ALL. The infield cannot be the problem: the bat is");
            GD.Print("  not putting the ball on the ground in the first place.");
            return;
        }

        string Pc(int n) => $"{n * 100f / grounders,5:F1}%";

        GD.Print($"  reached by a fielder    {Pc(reached)}   ({reached})");
        GD.Print($"  never reached at all    {Pc(stillLoose)}   ({stillLoose})");
        GD.Print($"  a throw was made        {Pc(threw)}   ({threw})");
        GD.Print($"  fielded, then held      {Pc(heldTheBall)}   ({heldTheBall})  " +
                 $"— he decided there was no play");
        GD.Print($"  an out was recorded     {Pc(outs)}   ({outs})");
        GD.Print($"  went down as a hit      {Pc(hits)}   ({hits})");

        GD.Print($"\n  average time to field it   {sumFieldTime / Mathf.Max(reached, 1):F2}s");
        GD.Print($"  average batter to first    {sumBatterTime / grounders:F2}s   " +
                 $"(the real thing is about 4.3)");

        GD.Print("\n  Real baseball retires the batter on roughly three ground balls in four.");
        GD.Print("  A large \"fielded, then held\" means the throw decision is refusing plays it");
        GD.Print("  should take; a large \"never reached\" means the infield has no range.");
    }
}
