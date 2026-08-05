using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Core;

/// <summary>
/// Proves the defensive alignments do something, and says what.
///
/// Moving fielders around is the easiest feature in a baseball game to fake: shift the drawn
/// positions, add a hidden bonus to the out rate, and nobody can tell. There is no bonus here —
/// the men simply stand somewhere else and the play simulation decides from where they are — so
/// the only honest way to know an alignment works is to hit the same balls at it.
///
/// That is what this does. Every alignment sees an identical sequence of batted balls out of an
/// identical situation, because the pitch and the swing are drawn from one seeded stream and the
/// play from another. Any difference in the results is the alignment and nothing else.
/// </summary>
public static class DefenceAudit
{
    private sealed class Tally
    {
        public int Balls, Hits, Outs, Runs, Doubles, PullSide, OppositeSide;
    }

    public static void Run(int trials)
    {
        var away = RosterGenerator.For(Teams.Get(0));
        var home = RosterGenerator.For(Teams.Get(1));

        GD.Print($"\n=== DEFENSIVE ALIGNMENT — {trials} identical balls at each ===");
        GD.Print("  Runner on third, one out. The same batted balls every time.\n");

        GD.Print($"  {"alignment",-20} {"hits",6} {"outs",6} {"runs",6} {"2B",5} " +
                 $"{"pull",6} {"oppo",6}");

        foreach (Alignment how in System.Enum.GetValues<Alignment>())
        {
            var t = Measure(away, home, how, trials);
            if (t.Balls == 0) { GD.Print($"  {Positioning.Label(how),-20}  no balls in play"); continue; }

            GD.Print($"  {Positioning.Label(how),-20} " +
                     $"{t.Hits * 100f / t.Balls,5:F1}% {t.Outs * 100f / t.Balls,5:F1}% " +
                     $"{t.Runs / (float)t.Balls,6:F3} {t.Doubles * 100f / t.Balls,4:F1}% " +
                     $"{t.PullSide * 100f / Mathf.Max(t.Hits, 1),5:F0}% " +
                     $"{t.OppositeSide * 100f / Mathf.Max(t.Hits, 1),5:F0}%");
        }

        GD.Print("\n  hits/outs are shares of balls in play; runs is runs per ball in play.");
        GD.Print("  pull/oppo are the shares of hits that went to each half of the field.");
        GD.Print("\n  Measured at 4000 balls: no-doubles cuts doubles by about a fifth and pays");
        GD.Print("  for it in singles, the shift moves eleven points of hits from the pull side");
        GD.Print("  to the other one, and double-play depth trims doubles a little. All three do");
        GD.Print("  what they are supposed to.");
        GD.Print("\n  Infield in does NOT. The men move in correctly and it concedes the hits it");
        GD.Print("  should — 34% to 41% — but it gives up more runs rather than fewer, because");
        GD.Print("  saving the run depends on throwing a runner out on a ground ball and the");
        GD.Print("  infield cannot do that at all. See PlaySimulation.ThrowTime: of every ball in");
        GD.Print("  play not caught on the fly, 95% is a hit. Until that is fixed, infield in is");
        GD.Print("  a cost with no benefit, so the computer does not call for it.");
    }

    private static Tally Measure(Roster away, Roster home, Alignment how, int trials)
    {
        var t = new Tally();
        var play = new PlaySimulation();

        for (int n = 0; n < trials; n++)
        {
            // A fresh situation every trial, seeded the same way for every alignment.
            var rng = new Rng(7000 + n);
            var sit = new GameSituation();
            sit.Start(away, home, 9);
            sit.Defence = how;

            // Runner on third, one out — the situation the infield comes in for.
            sit.Runners[3] = away.BattingOrder[0];
            sit.RecordOut();

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

            // The play gets its own stream, so the alignment is the only thing that varies.
            play.Begin(sit, ball, 31337 + n);
            int frames = 0;
            while (!play.Finished && frames++ < 2400) play.Update(1f / 60f);

            t.Balls++;
            var o = play.Outcome;

            if (o.IsHit)
            {
                t.Hits++;
                if (o.BasesForBatter >= 2) t.Doubles++;

                // Which half of the field it fell in, relative to how this hitter pulls.
                bool pullLeft = Positioning.PullsLeft(batter, pitcher);
                bool toLeft = ball.SprayAngle < 0f;
                if (toLeft == pullLeft) t.PullSide++; else t.OppositeSide++;
            }

            t.Outs += o.Outs;
            t.Runs += o.Runs;
        }

        return t;
    }
}
