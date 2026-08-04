using System.Collections.Generic;
using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Core;

/// <summary>
/// Measures the platoon split the simulation actually produces.
///
/// The advantage is easy to assert and easy to get wrong by a factor of three, and by the time it
/// shows up in a season's statistics it is tangled with everything else. So it gets measured the
/// same way every other rate in this game is: put a lot of plate appearances through the real
/// swing code, sort them by matchup, and print what came out beside what the majors actually do.
///
/// Real 2024, batting average by matchup, both leagues:
///   right-handed hitter vs left-handed pitching   .259
///   right-handed hitter vs right-handed pitching  .245
///   left-handed hitter  vs right-handed pitching  .254
///   left-handed hitter  vs left-handed pitching   .232
///
/// So the platoon advantage is worth roughly fourteen points to a right-hander and twenty-two to
/// a left-hander. That asymmetry is the thing most easily lost, and it is the reason a lefty
/// specialist is a job that exists.
/// </summary>
public static class PlatoonAudit
{
    private sealed class Split
    {
        public int AtBats;
        public int Hits;
        public int HomeRuns;
        public int Strikeouts;
        public int Swings;
        public int Misses;

        public float Average => AtBats == 0 ? 0f : Hits / (float)AtBats;
        public float WhiffRate => Swings == 0 ? 0f : Misses / (float)Swings;
    }

    public static void Run(int plateAppearances)
    {
        var league = new Data.Roster[Teams.All.Count];
        for (int i = 0; i < Teams.All.Count; i++) league[i] = RosterGenerator.For(Teams.All[i]);

        // Four buckets: the batter's hand against the pitcher's.
        var splits = new Dictionary<string, Split>();
        Split Bucket(string key) =>
            splits.TryGetValue(key, out var s) ? s : splits[key] = new Split();

        var rng = new Rng(20240711);
        var sit = new GameSituation();
        sit.Start(league[0], league[1], 9);

        var hitters = new List<PlayerData>();
        var arms = new List<PlayerData>();
        foreach (var r in league)
        {
            foreach (var p in r.BattingOrder) hitters.Add(p);
            foreach (var p in r.Pitchers) arms.Add(p);
        }

        int switchHitters = 0, lefties = 0;
        foreach (var h in hitters)
        {
            if (h.Bats == Handedness.Switch) switchHitters++;
            else if (h.Bats == Handedness.Left) lefties++;
        }

        for (int i = 0; i < plateAppearances; i++)
        {
            var batter = hitters[rng.Range(0, hitters.Count)];
            var arm = arms[rng.Range(0, arms.Count)];

            string key = $"{Platoon.Letter(batter.Bats)}H vs {Platoon.Letter(arm.Throws)}HP";
            var bucket = Bucket(key);

            // One plate appearance, resolved through the real code.
            int balls = 0, strikes = 0;
            while (true)
            {
                CpuBrain.ChoosePitch(sit, arm, ref rng, out var type, out var aim);
                var pitch = PitchFactory.Create(arm, type, aim, 0f, ref rng);
                var plan = CpuBrain.PlanSwing(sit, batter, pitch, ref rng);

                if (!plan.WillSwing)
                {
                    if (pitch.IsStrike) strikes++; else balls++;
                }
                else
                {
                    bucket.Swings++;
                    var result = SwingResolver.Resolve(batter, pitch, plan.SwingAt, plan.Cursor,
                        ref rng, out var ball, type: plan.Type);

                    if (result == SwingResult.Miss) { bucket.Misses++; strikes++; }
                    else if (result == SwingResult.Foul) { if (strikes < 2) strikes++; }
                    else
                    {
                        // A batted ball, judged by how it was struck rather than by running a
                        // full defensive simulation — this audit is about the bat, not the gloves.
                        bucket.AtBats++;
                        bool homer = ball.ExitVelocity / 1.46667f > 100f &&
                                     ball.LaunchAngle is > 22f and < 38f && rng.Chance(0.42f);
                        if (homer) { bucket.HomeRuns++; bucket.Hits++; }
                        else if (rng.Chance(RealBaseball.MlbBabip * ball.Quality * 1.55f)) bucket.Hits++;
                        break;
                    }
                }

                if (strikes >= 3) { bucket.AtBats++; bucket.Strikeouts++; break; }
                if (balls >= 4) break;                    // a walk is not an at-bat
            }
        }

        GD.Print($"\n=== PLATOON SPLIT — {plateAppearances:N0} plate appearances ===");
        GD.Print($"hitters: {lefties} left, {switchHitters} switch, " +
                 $"{hitters.Count - lefties - switchHitters} right (of {hitters.Count})");

        // Broken down by where the man came from, because the two halves of the league get their
        // handedness from different code and only one of them was ever wrong.
        int wl = 0, ws = 0, wn = 0, gl = 0, gs = 0, gn = 0;
        foreach (var h in hitters)
        {
            bool written = h.IsLegend;
            switch (h.Bats)
            {
                case Handedness.Left: if (written) wl++; else gl++; break;
                case Handedness.Switch: if (written) ws++; else gs++; break;
                default: if (written) wn++; else gn++; break;
            }
        }
        GD.Print($"  written:   {wl} L / {ws} S / {wn} R");
        GD.Print($"  generated: {gl} L / {gs} S / {gn} R");

        var hands = Legends.HandCheck();
        GD.Print($"  source of the written list: {hands.Left} L / {hands.Switch} S / " +
                 $"{hands.Right} R  ({hands.Authored} authored by hand)");
        GD.Print("  matchup          AVG     real     K%    whiff/swing");

        var real = new Dictionary<string, float>
        {
            ["RH vs LHP"] = 0.259f, ["RH vs RHP"] = 0.245f,
            ["LH vs RHP"] = 0.254f, ["LH vs LHP"] = 0.232f,
        };

        foreach (var key in new[] { "RH vs LHP", "RH vs RHP", "LH vs RHP", "LH vs LHP",
                                    "SH vs LHP", "SH vs RHP" })
        {
            if (!splits.TryGetValue(key, out var s) || s.AtBats == 0) continue;
            string expected = real.TryGetValue(key, out float r) ? $"{r:F3}" : "  — ";
            GD.Print($"  {key,-14} {s.Average:F3}    {expected}   " +
                     $"{s.Strikeouts / (float)s.AtBats * 100f,4:F1}%   {s.WhiffRate * 100f,4:F1}%");
        }

        // The number that matters: how much the platoon advantage is worth to each hand.
        float rhEdge = Diff(splits, "RH vs LHP", "RH vs RHP");
        float lhEdge = Diff(splits, "LH vs RHP", "LH vs LHP");

        GD.Print($"\n  platoon advantage — right-handed hitters: {rhEdge * 1000f:F0} points " +
                 "(real 14)");
        GD.Print($"  platoon advantage — left-handed hitters:  {lhEdge * 1000f:F0} points " +
                 "(real 22)");
        GD.Print(lhEdge > rhEdge
            ? "  asymmetry correct: lefties gain more from the platoon than right-handers."
            : "  WRONG WAY: right-handers are gaining more than lefties.");
    }

    private static float Diff(Dictionary<string, Split> splits, string good, string bad)
    {
        if (!splits.TryGetValue(good, out var g) || !splits.TryGetValue(bad, out var b)) return 0f;
        if (g.AtBats == 0 || b.AtBats == 0) return 0f;
        return g.Average - b.Average;
    }
}
