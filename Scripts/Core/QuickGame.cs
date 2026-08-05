using System.Collections.Generic;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Core;

/// <summary>
/// Plays a complete game with no rendering, driving the same rules engine, pitch factory,
/// swing resolver and field simulation the on-screen game uses. Used to fill in the rest of
/// the league's schedule while the player is busy with their own club's game.
/// </summary>
public static class QuickGame
{
    private const float Step = 1f / 120f;

    /// <summary>Plays it out and hands back the finished situation, box score included.</summary>
    public static GameSituation Simulate(Roster away, Roster home, int innings, int seed,
        int month = 0)
    {
        var rng = new Rng(seed);
        var sit = new GameSituation { Month = month };
        var play = new PlaySimulation();

        away.LineupSpot = 0;
        home.LineupSpot = 0;
        away.StartGame();
        home.StartGame();
        // Only fall back to the ace if nobody has been named. The caller sets the rotation:
        // starting Pitchers[0] here meant the same arm started all thirty-three games, which made
        // a club's whole season depend on one player and blew competitive balance apart.
        if (away.CurrentPitcher == null && away.Pitchers.Count > 0) away.SetPitcher(away.Pitchers[0]);
        if (home.CurrentPitcher == null && home.Pitchers.Count > 0) home.SetPitcher(home.Pitchers[0]);

        FieldGeometry.SetStadium(Stadiums.For(home.Team));

        sit.Start(away, home, innings);

        var pitchCounts = new Dictionary<PlayerData, int>();
        int guard = 0;

        while (!sit.IsOver && guard++ < 6000)
        {
            var pitcher = sit.FieldingTeam.CurrentPitcher;
            if (pitcher == null) break;

            // Runners go before the pitch, which is where a steal actually happens.
            Baserunning.TryStealBeforePitch(sit, ref rng);
            if (sit.IsOver) break;

            pitchCounts.TryGetValue(pitcher, out int thrown);
            pitchCounts[pitcher] = thrown + 1;
            sit.Stats.RecordPitch(pitcher);

            CpuBrain.ChoosePitch(sit, pitcher, ref rng, out var type, out var aim);
            var pitch = PitchFactory.Create(pitcher, type, aim, CpuBrain.Fatigue(pitcher, thrown), ref rng);

            var batter = sit.Batter;
            var plan = CpuBrain.PlanSwing(sit, batter, pitch, ref rng);

            if (plan.WillSwing)
            {
                var result = plan.Bunt
                    ? SwingResolver.ResolveBunt(batter, pitch, plan.SwingAt, plan.Cursor, ref rng, out var ball)
                    : SwingResolver.Resolve(batter, pitch, plan.SwingAt, plan.Cursor, ref rng, out ball,
                        type: plan.Type);

                if (result == SwingResult.InPlay)
                {
                    play.Begin(sit, ball, seed * 31 + guard);
                    int frames = 0;
                    while (!play.Finished && frames++ < 2400) play.Update(Step);
                    play.Apply(sit);
                }
                else
                {
                    sit.AddStrike(foul: result == SwingResult.Foul);
                }
            }
            else if (LooseBall.HitsBatter(pitch, batter, ref rng)) sit.AwardHitByPitch();
            else
            {
                bool endedAtBat = pitch.IsStrike ? sit.AddStrike(foul: false) : sit.AddBall();

                if (!endedAtBat && !sit.IsOver && sit.RunnerCount > 0 &&
                    LooseBall.GetsAway(pitch, sit.FieldingTeam.Fielder(Position.C), ref rng))
                    sit.WildPitch();
            }

            // Go to the bullpen when the man on the mound is spent. Who comes in depends on the
            // inning and the score, so the closer is saved for a lead in the ninth.
            var current = sit.FieldingTeam.CurrentPitcher;
            pitchCounts.TryGetValue(current, out int used);
            var reliever = CpuBrain.Relieve(sit, used);
            if (reliever != null) sit.ChangePitcher(reliever);
        }

        if (sit.IsOver) Finalize(sit);
        return sit;
    }

    /// <summary>Books the decision and the appearances once a game is final.</summary>
    public static void Finalize(GameSituation sit)
    {
        bool homeWon = sit.HomeScore > sit.AwayScore;
        sit.Stats.FinishGame(
            homeWon ? sit.Home : sit.Away,
            homeWon ? sit.Away : sit.Home,
            homeWon ? sit.HomeScore : sit.AwayScore,
            homeWon ? sit.AwayScore : sit.HomeScore);

        RecordWorkload(sit);
    }

    /// <summary>
    /// Books what the night cost each staff. Without this every arm is fresh every day and the
    /// closer pitches a hundred and sixty-two games.
    /// </summary>
    public static void RecordWorkload(GameSituation sit)
    {
        foreach (var roster in new[] { sit.Away, sit.Home })
            foreach (var arm in roster.UsedArms)
            {
                arm.RestDays = 0;
                arm.RecentPitches += sit.Stats.Pitching(arm).Pitches;
            }
    }
}
