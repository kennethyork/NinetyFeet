using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Core;

/// <summary>
/// Plays complete games with no window and no input, driving the same rules engine, pitch
/// factory, swing resolver and field simulation the live game uses. Run it with:
///     godot --headless -- --sim [games]
/// It is the project's smoke test: if the ballgame can't finish here, it can't finish on screen.
/// </summary>
public static class HeadlessSim
{
    private const float FixedStep = 1f / 60f;

    /// <summary>
    /// Audits the pitching staff over a stretch of a real season: who starts, who relieves, how
    /// often the closer is used and whether anyone is being run into the ground. A bullpen that
    /// looks right on paper can still be managed wrongly, and only a season shows it.
    ///     godot --headless -- --pen [games]
    /// </summary>
    public static void AuditBullpen(int games)
    {
        var season = new Season.SeasonState();
        season.StartNew(RosterGenerator.DefaultLeagueSeed, 0, games, 9);
        for (int i = 0; i < games + 10 && season.CurrentDay <= season.FinalDay; i++)
            season.AdvanceDay(simulateUserGame: true);

        var staff = Teams.All
            .SelectMany(t => season.RosterFor(t.Id).Pitchers.Select(p => (Team: t, Arm: p)))
            .ToList();

        GD.Print($"\n=== staff audit, {season.GamesPlayed} club-games played ===");
        GD.Print($"clubs {Teams.All.Count}   arms per club " +
                 $"{staff.Count / (float)Teams.All.Count:F1}   " +
                 $"position players {season.RosterFor(0).Players.Count - season.RosterFor(0).Pitchers.Count}");

        foreach (var role in new[]
                 { StaffRole.Starter, StaffRole.Long, StaffRole.Middle, StaffRole.Setup, StaffRole.Closer })
        {
            var group = staff.Where(x => x.Arm.Role == role).ToList();
            if (group.Count == 0) continue;

            var lines = group.Select(x => season.Book.Pitching(x.Arm)).ToList();
            float apps = (float)lines.Average(l => l.Games);
            float ip = (float)lines.Average(l => l.InningsPitched);
            float era = lines.Sum(l => l.Outs) > 0
                ? lines.Sum(l => l.EarnedRuns) * 27f / lines.Sum(l => l.Outs) : 0f;

            GD.Print($"  {PlayerData.RoleLabel(role),-14} n={group.Count,4}  " +
                     $"apps {apps,5:F1}  IP {ip,6:F1}  starts {lines.Average(l => l.GamesStarted),5:F1}  " +
                     $"saves {lines.Sum(l => l.Saves),4}  ERA {era,5:F2}");
        }

        // A club's whole season should not rest on one arm, and nobody should appear in more
        // games than the club has played.
        int overworked = staff.Count(x => season.Book.Pitching(x.Arm).Games > season.GamesPerTeam * 0.72f);
        GD.Print($"  arms appearing in over 72% of their club's games: {overworked}");

        // Every inning a club plays has to be pitched by exactly one of its arms. If the two
        // sides of this do not agree, outs are being credited to the wrong man somewhere.
        float clubGames = season.Games.Count(g => g.Played) * 2f / Teams.All.Count;
        float pitched = staff.Sum(x => season.Book.Pitching(x.Arm).InningsPitched) / Teams.All.Count;
        GD.Print($"  innings pitched per club {pitched:F1} against about {clubGames * 9f:F1} " +
                 "expected (a club pitches nine innings in every game it plays)");

        var wins = staff.Sum(x => season.Book.Pitching(x.Arm).Wins);
        var losses = staff.Sum(x => season.Book.Pitching(x.Arm).Losses);
        GD.Print($"  decisions recorded: {wins}W {losses}L against " +
                 $"{season.Games.Count(g => g.Played)} games played");

        // Roster integrity. A player who is on the pitching staff but not on the roster still
        // takes the ball and records outs, and none of it is ever counted — the record book walks
        // the roster. It cost five arms a club and was invisible in every box score.
        int ghosts = 0, missingArms = 0, wrongSize = 0;
        foreach (var t in Teams.All)
        {
            var r = season.RosterFor(t.Id);
            ghosts += r.Pitchers.Count(p => !r.Players.Contains(p));
            missingArms += r.Players.Count(p => p.Position == Data.Position.P && !r.Pitchers.Contains(p));
            if (r.Players.Count != Season.Development.RosterLimit) wrongSize++;
            foreach (var slot in r.Starters.Values)
                if (!r.Players.Contains(slot)) ghosts++;
        }

        // Clubs run a little over twenty-six when several written players land on the same side;
        // every one of them gets a place, and the winter trims the surplus. That is intended.
        // What is not intended is a name in a lineup or on a staff that is not on the roster.
        GD.Print($"  integrity: {ghosts} players in a lineup or on a staff but not on the roster, " +
                 $"{missingArms} pitchers missing from their staff, " +
                 $"{wrongSize} clubs carrying other than {Season.Development.RosterLimit} " +
                 $"(sizes {Teams.All.Min(t => season.RosterFor(t.Id).Players.Count)}" +
                 $"–{Teams.All.Max(t => season.RosterFor(t.Id).Players.Count)})");

        GD.Print($"\n  one club's staff ({Teams.Get(0).FullName}, " +
                 $"{season.Games.Count(g => g.Played && g.Involves(0))} games played):");
        foreach (var arm in season.RosterFor(0).Pitchers)
        {
            var l = season.Book.Pitching(arm);
            GD.Print($"    {PlayerData.RoleLabel(arm.Role),-14} {arm.ShortName,-16} " +
                     $"G {l.Games,3}  GS {l.GamesStarted,3}  IP {l.InningsText,6}  " +
                     $"SV {l.Saves,3}  {l.Wins}-{l.Losses}  ERA {l.Era,5:F2}" +
                     (season.RosterFor(0).Players.Contains(arm) ? "" : "   NOT ON THE ROSTER LIST"));
        }
    }

    /// <summary>
    /// Plays one game and audits out accounting: every half inning must record exactly three
    /// outs (except a walk-off or a home half that is never played), outs must never exceed
    /// three, and the count must reset between halves.
    /// </summary>
    public static void AuditOuts(int games)
    {
        int problems = 0;

        for (int g = 0; g < games; g++)
        {
            var rng = new Rng(500 + g);
            var sit = new GameSituation();
            var play = new PlaySimulation();

            var away = RosterGenerator.For(Teams.Get(g % 32));
            var home = RosterGenerator.For(Teams.Get((g * 5 + 3) % 32));
            away.LineupSpot = home.LineupSpot = 0;
            away.SetPitcher(away.Pitchers[0]);
            home.SetPitcher(home.Pitchers[0]);
            FieldGeometry.SetStadium(Stadiums.For(home.Team));

            int halfIndex = 0;
            int outsAtHalfStart = 0;
            var log = new List<string>();

            sit.HalfInningChanged += () =>
            {
                // Counted from the situation itself, so any source of an out is included.
                int made = sit.OutsRecorded - outsAtHalfStart;
                if (made != 3)
                    log.Add($"    half #{halfIndex} ended with {made} outs, expected 3");
                halfIndex++;
                outsAtHalfStart = sit.OutsRecorded;
            };

            sit.Start(away, home, 9);

            int guard = 0;
            while (!sit.IsOver && guard++ < 4000)
            {
                // Runners go before the pitch. The out count has to be read after that, or a
                // caught stealing makes every later consistency check compare against a stale
                // number — which is what produced four phantom audit failures.
                Baserunning.TryStealBeforePitch(sit, ref rng);
                if (sit.IsOver) break;

                int before = sit.Outs;
                if (before > 3) { log.Add($"    outs went to {before}"); break; }

                var pitcher = sit.FieldingTeam.CurrentPitcher;
                CpuBrain.ChoosePitch(sit, pitcher, ref rng, out var type, out var aim);
                var pitch = PitchFactory.Create(pitcher, type, aim, 0f, ref rng);
                var plan = CpuBrain.PlanSwing(sit, sit.Batter, pitch, ref rng);

                int halfBefore = halfIndex;

                if (plan.WillSwing)
                {
                    var r = SwingResolver.Resolve(sit.Batter, pitch, plan.SwingAt, plan.Cursor,
                        ref rng, out var ball, type: plan.Type);
                    if (r == SwingResult.InPlay)
                    {
                        play.Begin(sit, ball, 7000 + guard);
                        int f = 0;
                        while (!play.Finished && f++ < 2400) play.Update(1f / 120f);
                        int recorded = play.Outcome.Outs;

                        play.Apply(sit);

                        if (halfIndex == halfBefore && sit.Outs != Mathf.Min(before + recorded, 3))
                            log.Add($"    play recorded {recorded} outs: {before} -> {sit.Outs}");
                        if (before + recorded > 3)
                            log.Add($"    play recorded {recorded} outs with {before} already away");
                    }
                    else
                    {
                        sit.AddStrike(foul: r == SwingResult.Foul);
                    }
                }
                else if (pitch.IsStrike)
                {
                    sit.AddStrike(foul: false);
                }
                else sit.AddBall();
            }

            if (log.Count > 0)
            {
                problems++;
                GD.Print($"  game {g} ({away.Team.Abbrev} at {home.Team.Abbrev}):");
                foreach (var l in log) GD.Print(l);
            }
        }

        GD.Print(problems == 0
            ? $"\nOUT AUDIT: {games} games clean — every half inning recorded exactly three outs."
            : $"\nOUT AUDIT: {problems} of {games} games had out-accounting problems.");
    }

    /// <summary>
    /// Hitting lab. Stands in for a human at the plate: aims at the pitch with a realistic
    /// amount of error and swings with a realistic amount of timing error, then reports what
    /// actually comes of it. Guessing at whether hitting "feels good" is how it kept getting
    /// tuned in the wrong direction; this measures it.
    /// </summary>
    public static void HitLab(int pitches)
    {
        // Aim error in feet, timing error in milliseconds, one sigma.
        var players = new (string Name, float Aim, float TimeMs)[]
        {
            ("sharp   ", 0.15f, 55f),
            ("average ", 0.30f, 95f),
            ("sloppy  ", 0.50f, 150f),
        };

        var rngSpeed = new Rng(99);
        var roster = RosterGenerator.For(Teams.Get(0));
        var batter = roster.BattingOrder[2];
        var pitcher = RosterGenerator.For(Teams.Get(1)).Pitchers[0];


        GD.Print($"\n=== HITTING LAB — {batter.Name} (CON {batter.Contact} POW {batter.Power}) " +
                 $"vs {pitcher.Name} (VEL {pitcher.PitchPower} CMD {pitcher.PitchControl}) ===");

        foreach (var level in new[]
                 { Difficulty.Rookie, Difficulty.Pro, Difficulty.AllStar, Difficulty.Legend,
                   Difficulty.Simulation })
        {
        var tune = DifficultyTuning.For(level);
        float assist = tune.BatAssist;
        float timingAssist = tune.TimingAssist;
        // Show what the level does to the ball as well as to the bat: a fastball's velocity and
        // the reaction time it leaves you.
        var demo = PitchFactory.Create(pitcher, PitchType.Fastball, new Vector2(0f, 2.6f), 0f,
            ref rngSpeed, 1f, tune.PitchSpeed);
        GD.Print($"\n-- {tune.Name} (bat x{assist:0.00}, timing x{timingAssist:0.00}, " +
                 $"velocity x{tune.PitchSpeed:0.00} -> {demo.SpeedMph:F0} mph, " +
                 $"{demo.FlightTime * 1000f:F0} ms to react) --");
        GD.Print($"{"player",-9} {"whiff",6} {"foul",6} {"inplay",7} | of contact: " +
                 $"{"weak",6} {"solid",6} {"crushed",8} | {"avg mph",8} {"avg deg",8}");
        foreach (var (name, aimSigma, timeSigma) in players)
        {
            var rng = new Rng(4242);
            int miss = 0, foul = 0, inplay = 0, weak = 0, solid = 0, crushed = 0;
            float mphSum = 0f, degSum = 0f;

            for (int i = 0; i < pitches; i++)
            {
                CpuBrain.ChoosePitch(new GameSituation { Away = roster, Home = roster },
                    pitcher, ref rng, out var type, out var aim);
                var pitch = PitchFactory.Create(pitcher, type, aim, 0f, ref rng);

                // The player can see where it will cross, but not perfectly.
                var cursor = pitch.CrossPoint + new Vector2(
                    (rng.Bell() - 0.5f) * aimSigma * 3.4f,
                    (rng.Bell() - 0.5f) * aimSigma * 3.4f);

                float swingAt = 1f + (rng.Bell() - 0.5f) * (timeSigma / 1000f) * 3.4f
                                     / Mathf.Max(pitch.FlightTime, 0.05f);

                var result = SwingResolver.Resolve(batter, pitch, swingAt, cursor, ref rng,
                    out var ball, assist, SwingType.Normal, timingAssist);

                switch (result)
                {
                    case SwingResult.Miss: miss++; break;
                    case SwingResult.Foul: foul++; break;
                    default:
                        inplay++;
                        float mph = ball.ExitVelocity / 1.46667f;
                        mphSum += mph;
                        degSum += ball.LaunchAngle;
                        if (mph < 80f) weak++;
                        else if (mph < 98f) solid++;
                        else crushed++;
                        break;
                }
            }

            float n = pitches;
            float c = Mathf.Max(inplay, 1);
            GD.Print($"{name,-9} {miss / n * 100f,5:F1}% {foul / n * 100f,5:F1}% {inplay / n * 100f,6:F1}% | " +
                     $"{weak / c * 100f,5:F0}% {solid / c * 100f,5:F0}% {crushed / c * 100f,7:F0}% | " +
                     $"{mphSum / c,8:F1} {degSum / c,8:F1}");
        }
        }


        GD.Print("\nA good arcade target: an average player whiffs well under a third of swings,\n" +
                 "and aiming well is visibly rewarded with harder contact.");
    }

    /// <summary>
    /// Runs a whole season start to finish with no window: builds the schedule, checks it is
    /// balanced, simulates every game, then plays out the postseason and crowns a champion.
    /// </summary>
    public static void RunSeason(int gamesPerTeam)
    {
        var season = new Season.SeasonState();
        season.StartNew(RosterGenerator.DefaultLeagueSeed, userTeamId: 0, gamesPerTeam, innings: 9);

        if (!Season.Schedule.IsBalanced(season.Games, out string problem))
        {
            GD.PrintErr($"SCHEDULE PROBLEM: {problem}");
            return;
        }

        int days = 0;
        foreach (var g in season.Games) days = Mathf.Max(days, g.Day);
        GD.Print($"Schedule: {season.Games.Count} games over {days + 1} days, " +
                 $"{season.Games.Count * 2 / 32} per club. Balanced.");

        foreach (var g in season.Games) season.SimulateGame(g);

        GD.Print("\n=== FINAL STANDINGS ===");
        foreach (var league in new[] { League.American, League.National })
        foreach (var division in new[] { Division.East, Division.West })
        {
            GD.Print($"\n{Teams.DivisionName(league, division)}");
            foreach (var (team, rec) in season.Standings(league, division))
                GD.Print($"  {team.Abbrev,-4} {team.Nickname,-14} {rec.Wins,3}-{rec.Losses,-3} " +
                         $"{rec.WinPctText}  RS {rec.RunsScored,4}  RA {rec.RunsAllowed,4}  " +
                         $"{(rec.RunDifferential > 0 ? "+" : "")}{rec.RunDifferential}");
        }

        season.BeginPlayoffsIfReady();
        GD.Print("\n=== POSTSEASON ===");

        int guard = 0;
        while (!season.Playoffs.Finished && guard++ < 200)
            if (season.SimulateNextPlayoffGame() == null) break;

        foreach (var s in season.Playoffs.Series) GD.Print("  " + s.Line);

        if (season.Playoffs.ChampionId >= 0)
        {
            var champ = Teams.Get(season.Playoffs.ChampionId);
            GD.Print($"\nCHAMPIONS: {champ.FullName}");
        }
        else GD.PrintErr("\nPostseason did not resolve a champion.");

        // The offseason draft: worst record should pick first, and every pick must land.
        var draft = season.Draft;
        draft.Begin(season, season.LeagueSeed);
        int classSize = draft.Available.Count;
        int guardD = 0;
        while (!draft.Complete && guardD++ < 300) draft.AutoPick(season);

        GD.Print($"\n=== DRAFT ===");
        GD.Print($"Class of {classSize} prospects, {draft.Picks.Count} picks over {Season.Draft.Rounds} rounds.");

        var firstFive = draft.Picks.Take(5);
        foreach (var pick in firstFive)
        {
            var t = Teams.Get(pick.TeamId);
            var rec = season.Book.Record(pick.TeamId);
            GD.Print($"  {pick.Overall,2}. {t.Abbrev,-4} ({rec.Wins}-{rec.Losses})  " +
                     $"{pick.Player.PositionText,-2} {pick.Player.Name,-22} age {pick.Player.Age}  " +
                     $"now {pick.Player.Overall} ceiling {pick.Player.Potential}  {pick.Player.PotentialGrade}");
        }

        if (draft.Picks.Count > 0)
        {
            var firstTeam = Teams.Get(draft.Picks[0].TeamId);
            var worst = season.AllStandings().Last().Team;
            GD.Print(firstTeam.Id == worst.Id
                ? $"  Order check: {firstTeam.Abbrev} picked first and had the worst record. Correct."
                : $"  ORDER PROBLEM: {firstTeam.Abbrev} picked first but {worst.Abbrev} was worst.");

            float avgCeiling = (float)draft.Picks.Average(p => p.Player.Potential);
            float avgNow = (float)draft.Picks.Average(p => p.Player.Overall);
            GD.Print($"  Drafted average: now {avgNow:F1}, ceiling {avgCeiling:F1} " +
                     $"(+{avgCeiling - avgNow:F1} of growth ahead)");
            GD.Print($"  Rosters are now {season.RosterFor(0).Players.Count} deep.");
        }

        // A league leader, to confirm season-long stats actually accumulated.
        var best = season.HittingLeaders(l => l.Average, minAtBats: 40, take: 3);
        GD.Print("\nBatting leaders (min 40 AB):");
        foreach (var (p, line) in best)
        {
            var club = season.TeamOf(p);
            GD.Print($"  {p.Name,-22} {club?.Abbrev ?? "??",-4} G{line.Games,3} PA{line.PlateAppearances,4} " +
                     $"{Stats.BattingLine.Rate(line.Average)}  {line.Hits}/{line.AtBats}  " +
                     $"{line.HomeRuns} HR  {line.RunsBattedIn} RBI");
        }

        // How wide is the league's spread of batting averages? Real baseball runs roughly .200
        // at the bottom to .330 at the top among regulars.
        var avgs = new List<float>();
        foreach (var (_, line) in season.Book.AllBatting)
            if (line.AtBats >= 40) avgs.Add(line.Average);
        avgs.Sort();
        if (avgs.Count > 4)
        {
            float Pick(float p) => avgs[Mathf.Clamp((int)(avgs.Count * p), 0, avgs.Count - 1)];
            GD.Print($"\nBatting average spread among {avgs.Count} regulars (real: .200 low, " +
                     $".250 median, .330 high):");
            GD.Print($"  p10 {Stats.BattingLine.Rate(Pick(0.10f))}   " +
                     $"median {Stats.BattingLine.Rate(Pick(0.50f))}   " +
                     $"p90 {Stats.BattingLine.Rate(Pick(0.90f))}   " +
                     $"max {Stats.BattingLine.Rate(avgs[^1])}");
        }

        // League-wide totals, to tell a real outlier from a stat that is being counted twice.
        int totalPa = 0, totalHits = 0, totalHr = 0, tracked = 0;
        foreach (var (_, line) in season.Book.AllBatting)
        {
            totalPa += line.PlateAppearances;
            totalHits += line.Hits;
            totalHr += line.HomeRuns;
            tracked++;
        }
        GD.Print($"\nLeague totals: {tracked} hitters, {totalPa} PA, {totalHits} H, {totalHr} HR " +
                 $"over {season.Games.Count} games");
        GD.Print($"  Expected PA per game ~{totalPa / (float)season.Games.Count:F1} (a 6-inning " +
                 $"game should be near 50)");
    }

    /// <summary>Prints every ballpark, so the 32 parks can be eyeballed without playing in them.</summary>
    public static void ListParks()
    {
        GD.Print("\n=== BALLPARKS ===");
        GD.Print($"{"CLUB",-6} {"PARK",-18} {"LF",4} {"LCF",4} {"CF",4} {"RCF",4} {"RF",4}  " +
                 $"{"WALLS",-22} {"AIR",5}  QUIRK");
        foreach (var park in Stadiums.All)
        {
            var team = Teams.Get(park.TeamId);
            string d = string.Join(" ", System.Array.ConvertAll(park.Distances, x => $"{(int)x,4}"));
            string h = string.Join("/", System.Array.ConvertAll(park.Heights, x => $"{(int)x}"));
            string air = park.AirDensity < 0.97f ? "thin " : park.AirDensity > 1.03f ? "heavy" : "even ";
            GD.Print($"{team.Abbrev,-6} {park.Name,-18} {d}  {h,-22} {air}  {park.Quirk}");
        }
    }

    public static void RunSeries(int games)
    {
        var totals = new SeriesTotals();

        for (int i = 0; i < games; i++)
        {
            var away = Teams.Get((i * 7) % 32);
            var home = Teams.Get((i * 7 + 13) % 32);
            var report = PlayGame(away, home, seed: 1000 + i);
            GD.Print(report.Text);
            totals.Absorb(report);
        }

        GD.Print(totals.Summary(games));
    }

    private sealed class GameReport
    {
        public string Text;
        public int Runs;
        public int Hits;
        public int HomeRuns;
        public int Strikeouts;
        public int Walks;
        public int Pitches;
        public int Innings;
        public int Steals, Caught;

        // The pitches that got away from somebody.
        public int HitBatsmen, WildPitches;

        // The scorer's distinctions, read back out of the book.
        public int SacrificeFlies, SacrificeBunts, DoublePlays;

        // Double-play diagnostics: chances offered against chances taken.
        public int TwoOutPlays, ForceChances, ManOnFirst, OnFirstUnderTwo, OnFirstNotCaught;
        public int NotCaught, NotCaughtOut;

        // Pitch-level diagnostics, so balance work is measured rather than guessed.
        public int InZone, Swings, Misses, Fouls, InPlay, CalledStrikes, CalledBalls;
        public int PlateAppearances, Doubles, Triples;

        // How far well-struck balls actually carry, which decides doubles and home runs.
        public float CarrySum, CarryMax;
        public int CarryCount;

        // Baserunning: how often runners are erased, and how far they get.
        public int BaseOuts, RunnersAdvanced, RunnersStranded, BallsInPlay;

        // Where batted balls actually go, in thirds of fair territory.
        public int SprayLeft, SprayCenter, SprayRight;
    }

    private sealed class SeriesTotals
    {
        private int _runs, _hits, _hr, _k, _bb, _pitches, _innings, _sb, _cs, _hbp, _wp;
        private int _sf, _sh, _gidp, _twoOut, _forceChances, _onFirst;
        private int _onFirstUnderTwo, _onFirstNotCaught, _notCaught, _notCaughtOut;
        private int _zone, _swings, _miss, _foul, _inPlay, _called, _balls, _pa;
        private int _doubles, _triples, _carryCount;
        private float _carrySum, _carryMax;

        public void Absorb(GameReport r)
        {
            _runs += r.Runs; _hits += r.Hits; _hr += r.HomeRuns;
            _k += r.Strikeouts; _bb += r.Walks; _pitches += r.Pitches; _innings += r.Innings;
            _sb += r.Steals; _cs += r.Caught; _hbp += r.HitBatsmen; _wp += r.WildPitches;
            _sf += r.SacrificeFlies; _sh += r.SacrificeBunts; _gidp += r.DoublePlays;
            _twoOut += r.TwoOutPlays; _forceChances += r.ForceChances; _onFirst += r.ManOnFirst;
            _onFirstUnderTwo += r.OnFirstUnderTwo; _onFirstNotCaught += r.OnFirstNotCaught;
            _notCaught += r.NotCaught; _notCaughtOut += r.NotCaughtOut;
            _zone += r.InZone; _swings += r.Swings; _miss += r.Misses; _foul += r.Fouls;
            _inPlay += r.InPlay; _called += r.CalledStrikes; _balls += r.CalledBalls;
            _pa += r.PlateAppearances; _doubles += r.Doubles; _triples += r.Triples;
            _carrySum += r.CarrySum; _carryCount += r.CarryCount;
            if (r.CarryMax > _carryMax) _carryMax = r.CarryMax;
            _baseOuts += r.BaseOuts; _advanced += r.RunnersAdvanced;
            _stranded += r.RunnersStranded; _bip += r.BallsInPlay;
            _sprayL += r.SprayLeft; _sprayC += r.SprayCenter; _sprayR += r.SprayRight;
        }

        private int _baseOuts, _advanced, _stranded, _bip;
        private int _sprayL, _sprayC, _sprayR;

        public string Summary(int games)
        {
            float p = Mathf.Max(_pitches, 1);
            float sw = Mathf.Max(_swings, 1);
            return
                $"\n=== {games} games (both clubs combined per game) ===\n" +
                $"Runs {(float)_runs / games:F2}   Hits {(float)_hits / games:F2}   " +
                $"HR {(float)_hr / games:F2}   K {(float)_k / games:F2}   BB {(float)_bb / games:F2}\n" +
                $"Pitches {p / games:F1}   PA {(float)_pa / games:F1}   " +
                $"Pitches/PA {p / Mathf.Max(_pa, 1):F2}   Half-innings {_innings}\n" +
                $"Zone% {_zone / p * 100f:F1}   Swing% {sw / p * 100f:F1}   " +
                $"Whiff/swing {_miss / sw * 100f:F1}%   Foul/swing {_foul / sw * 100f:F1}%   " +
                $"InPlay/swing {_inPlay / sw * 100f:F1}%\n" +
                $"Called strikes {_called / p * 100f:F1}%   Called balls {_balls / p * 100f:F1}%\n" +
                $"2B {(float)_doubles / games:F2}   3B {(float)_triples / games:F2}   " +
                $"Carry avg {_carrySum / Mathf.Max(_carryCount, 1):F0}ft  max {_carryMax:F0}ft " +
                $"(fence 330-400)\n" +
                $"Spray: left {_sprayL * 100f / Mathf.Max(_bip, 1):F0}%  centre " +
                $"{_sprayC * 100f / Mathf.Max(_bip, 1):F0}%  right {_sprayR * 100f / Mathf.Max(_bip, 1):F0}% " +
                $"(want roughly 35/30/35)\n" +
                $"BABIP {(_hits - _hr) / Mathf.Max(_bip, 1f):F3}   (real {RealBaseball.MlbBabip:F3})\n" +
                $"Balls in play {(float)_bip / games:F1}   On-base runners: advanced {(float)_advanced / games:F1}  " +
                $"held {(float)_stranded / games:F1}  thrown out {(float)_baseOuts / games:F1}\n" +
                Compare(games);
        }

        /// <summary>
        /// This run against the real major-league season, side by side. Anything more than a few
        /// per cent out is a calibration problem, not noise.
        /// </summary>
        private string Compare(int games)
        {
            var r = RealBaseball.Mlb;
            string Row(string name, float mine, float real) =>
                $"  {name,-10} {mine,7:F2} {real,8:F2} {(real > 0 ? (mine - real) / real * 100f : 0f),8:+0.0;-0.0}%\n";

            return $"\n=== against {r.Name} (per game, both clubs) ===\n" +
                   $"  {"stat",-10} {"mine",7} {"real",8} {"diff",9}\n" +
                   Row("runs", (float)_runs / games, r.Runs) +
                   Row("hits", (float)_hits / games, r.Hits) +
                   Row("2B", (float)_doubles / games, r.Doubles) +
                   Row("3B", (float)_triples / games, r.Triples) +
                   Row("HR", (float)_hr / games, r.HomeRuns) +
                   Row("BB", (float)_bb / games, r.Walks) +
                   Row("K", (float)_k / games, r.Strikeouts) +
                   Row("SB", (float)_sb / games, r.StolenBases) +
                   Row("CS", (float)_cs / games, RealBaseball.MlbCaughtStealing) +
                   Row("HBP", (float)_hbp / games, RealBaseball.MlbHitByPitch) +
                   Row("WP", (float)_wp / games, RealBaseball.MlbWildPitches) +
                   Row("SF", (float)_sf / games, RealBaseball.MlbSacrificeFlies) +
                   Row("SH", (float)_sh / games, RealBaseball.MlbSacrificeBunts) +
                   Row("GIDP", (float)_gidp / games, RealBaseball.MlbDoublePlays) +
                   Defence(games) +
                   Reference;
        }

        /// <summary>
        /// Where the defence's outs actually come from.
        ///
        /// Added because a missing statistic turned out to be a missing play: the double-play
        /// column read zero, and the reason was not the scorer but an infield that never threw
        /// anybody out. This block is what told the two apart, so it stays.
        /// </summary>
        private string Defence(int games)
        {
            float bip = Mathf.Max(_bip, 1);
            float caught = _bip - _notCaught;

            return $"\n=== where the outs come from ===\n" +
                   $"  balls in play {bip / games:F1}/game — " +
                   $"caught in the air {caught * 100f / bip:F0}% (real about 45%), " +
                   $"not caught {_notCaught * 100f / bip:F0}%\n" +
                   $"  of those not caught, an out {_notCaughtOut * 100f / Mathf.Max(_notCaught, 1):F0}% " +
                   $"— {(float)_notCaughtOut / games:F2}/game (real is nearer 15)\n" +
                   $"  with a man on first: {(float)_onFirst / games:F2} balls in play, " +
                   $"{(float)_onFirstUnderTwo / games:F2} under two out, " +
                   $"{(float)_forceChances / games:F2} retired, " +
                   $"{(float)_gidp / games:F2} double plays\n";
        }

        /// <summary>
        /// Real major-league rates, measured rather than remembered: 87,799 pitches over 300
        /// games, sampled across 22 dates spread through the 2024 season, pulled from Statcast
        /// (baseballsavant.mlb.com). Regenerate with scratchpad/statcast_ref.py.
        /// </summary>
        private const string Reference =
            "MEASURED MLB 2024 (87,799 pitches / 300 games):\n" +
            "  Runs 8.96  Hits 16.50  HR 2.31  K 16.60  BB 6.39  2B 3.22  3B 0.23\n" +
            "  Pitches 292.7  PA 75.3  Pitches/PA 3.89\n" +
            "  Zone 49.2  Swing 47.7  Whiff/swing 24.9  Foul/swing 38.4  InPlay/swing 36.7\n" +
            "  Exit velo mean 82.5 (p50 82, p90 102, max 120)  Launch angle mean 17.7 (p50 20)\n" +
            "  Batted-ball distance mean 157, p90 323, max 478";
    }

    private static GameReport PlayGame(TeamData awayTeam, TeamData homeTeam, int seed)
    {
        var rng = new Rng(seed);
        var sit = new GameSituation();
        var play = new PlaySimulation();
        var report = new GameReport();

        var away = RosterGenerator.For(awayTeam);
        var home = RosterGenerator.For(homeTeam);
        away.LineupSpot = 0;
        home.LineupSpot = 0;

        // Turn the rotation over, the same way a season does. Forcing Pitchers[0] here meant every
        // game in the calibration sample was started by an ace, while three quarters of real starts
        // come from the second, third and fourth arms — so the rates being matched against the
        // majors were measured on a league that never actually gets played.
        away.StartGame();
        home.StartGame();
        var awayRotation = away.Rotation.ToList();
        var homeRotation = home.Rotation.ToList();
        int slot = Mathf.Abs(seed);
        if (awayRotation.Count > 0) away.SetPitcher(awayRotation[slot % awayRotation.Count]);
        if (homeRotation.Count > 0) home.SetPitcher(homeRotation[(slot + 1) % homeRotation.Count]);

        // Every arm reports rested. The harness plays thousands of one-off games with no calendar
        // between them, so without this the whole league is on fumes by the tenth game and the
        // bullpen measurements describe a staff that would never take the field.
        foreach (var arm in away.Pitchers.Concat(home.Pitchers))
        {
            arm.RestDays = 3;
            arm.RecentPitches = 0;
        }

        FieldGeometry.SetStadium(Stadiums.For(homeTeam));

        // And the weather, which the harness did not play in.
        //
        // SeasonState.SimulateGame sets conditions from Weather.For and this did not, so every
        // number the calibration table reported was measured in still, neutral air while a real
        // season was played in wind and heat. The two were not describing the same game — a
        // club's box scores over a fortnight came out around 11 hits a game against the 8.2 the
        // harness was reporting.
        //
        // Sampled with the same shape Weather.For uses: a summer temperature curve, and wind that
        // is mostly gentle and blows in as often as out.
        var sky = new Rng(seed * 6151 + 17);
        float summer = sky.NextFloat();
        FieldGeometry.SetConditions(
            (sky.Bell() - 0.5f) * 44f,
            Mathf.RoundToInt(Mathf.Lerp(48f, 88f, summer) + sky.Range(-9f, 9f)));

        var pitchCounts = new Dictionary<PlayerData, int>();
        sit.HalfInningChanged += () => report.Innings++;

        sit.Start(away, home, innings: 9);

        int guard = 0;
        int playSeed = seed * 31;

        while (!sit.IsOver && guard++ < 4000)
        {
            // Runners go before the pitch, which is where a steal actually happens.
            var steal = Baserunning.TryStealBeforePitch(sit, ref rng);
            if (steal.Attempted) { if (steal.Safe) report.Steals++; else report.Caught++; }
            if (sit.IsOver) break;

            var pitcher = sit.FieldingTeam.CurrentPitcher;
            pitchCounts.TryGetValue(pitcher, out int thrown);
            pitchCounts[pitcher] = thrown + 1;
            report.Pitches++;

            CpuBrain.ChoosePitch(sit, pitcher, ref rng, out var type, out var aim);
            var pitch = PitchFactory.Create(pitcher, type, aim, CpuBrain.Fatigue(pitcher, thrown), ref rng);

            var batter = sit.Batter;
            sit.Defence = Positioning.Suggested(sit);
            var plan = CpuBrain.PlanSwing(sit, batter, pitch, ref rng);

            sit.Stats.RecordPitch(pitcher);
            if (pitch.IsStrike) report.InZone++;

            if (plan.WillSwing)
            {
                report.Swings++;
                var result = plan.Bunt
                    ? SwingResolver.ResolveBunt(batter, pitch, plan.SwingAt, plan.Cursor, ref rng, out var ball)
                    : SwingResolver.Resolve(batter, pitch, plan.SwingAt, plan.Cursor, ref rng, out ball,
                        type: plan.Type);

                if (result == SwingResult.Miss) report.Misses++;
                else if (result == SwingResult.Foul) report.Fouls++;
                else report.InPlay++;

                if (result == SwingResult.InPlay)
                {
                    // Read before the play, since Apply picks the runners up off the bases.
                    bool runnerOnFirst = sit.RunnerOn(1);
                    int outsBefore = sit.Outs;

                    play.Begin(sit, ball, playSeed++);

                    if (ball.LaunchAngle > 15f)
                    {
                        float carry = play.PredictedLanding.Length();
                        report.CarrySum += carry;
                        report.CarryCount++;
                        if (carry > report.CarryMax) report.CarryMax = carry;
                    }

                    int frames = 0;
                    while (!play.Finished && frames++ < 2400) play.Update(FixedStep);
                    if (play.Outcome.IsHomeRun) report.HomeRuns++;
                    if (play.Outcome.IsHit) report.Hits++;

                    // Told apart so a missing double play can be blamed on the right thing: a
                    // simulation that never turns two, or a scorer that never recognises it.
                    if (play.Outcome.Outs >= 2) report.TwoOutPlays++;
                    if (runnerOnFirst) report.ManOnFirst++;
                    if (!play.CaughtInAir) report.NotCaught++;
                    if (!play.CaughtInAir && !play.Outcome.IsHit) report.NotCaughtOut++;
                    if (runnerOnFirst && outsBefore < 2) report.OnFirstUnderTwo++;
                    if (runnerOnFirst && outsBefore < 2 && !play.CaughtInAir)
                        report.OnFirstNotCaught++;
                    if (runnerOnFirst && outsBefore < 2 && !play.CaughtInAir &&
                        !play.Outcome.IsHit) report.ForceChances++;

                    report.BallsInPlay++;
                    if (ball.SprayAngle < -15f) report.SprayLeft++;
                    else if (ball.SprayAngle > 15f) report.SprayRight++;
                    else report.SprayCenter++;

                    foreach (var r in play.Runners)
                    {
                        if (r.IsBatter) continue;
                        if (r.IsOut) report.BaseOuts++;
                        else if (r.Scored || r.MaxBaseReached > r.StartBase) report.RunnersAdvanced++;
                        else report.RunnersStranded++;
                    }

                    play.Apply(sit);
                }
                else
                {
                    sit.AddStrike(foul: result == SwingResult.Foul);
                }
            }
            else if (LooseBall.HitsBatter(pitch, batter, ref rng))
            {
                report.HitBatsmen++;
                sit.AwardHitByPitch();
            }
            else
            {
                bool endedAtBat;
                if (pitch.IsStrike)
                {
                    report.CalledStrikes++;
                    endedAtBat = sit.AddStrike(foul: false);
                }
                else
                {
                    report.CalledBalls++;
                    endedAtBat = sit.AddBall();
                }

                // The ball is only wild if there is somebody to advance, and only if the at-bat
                // is still going — a walk has already put the runners where they belong.
                if (!endedAtBat && !sit.IsOver && sit.RunnerCount > 0 &&
                    LooseBall.GetsAway(pitch, sit.FieldingTeam.Fielder(Position.C), ref rng))
                {
                    report.WildPitches++;
                    sit.WildPitch();
                }
            }

            // Managers go to the pen when the man on the mound is spent.
            var current = sit.FieldingTeam.CurrentPitcher;
            pitchCounts.TryGetValue(current, out int used);
            var reliever = CpuBrain.Relieve(sit, used);
            if (reliever != null) sit.ChangePitcher(reliever);
        }

        report.Runs = sit.AwayScore + sit.HomeScore;
        report.Hits = sit.AwayHits + sit.HomeHits;

        // Read the counting stats straight from the book rather than inferring them.
        foreach (var (_, line) in sit.Stats.AllPitching)
        {
            report.Strikeouts += line.Strikeouts;
            report.Walks += line.Walks;
        }
        foreach (var (_, line) in sit.Stats.AllBatting)
        {
            report.PlateAppearances += line.PlateAppearances;
            report.Doubles += line.Doubles;
            report.Triples += line.Triples;

            // The scorer's distinctions. These are read from the book on purpose: they are the
            // only proof that a sacrifice or a double play was ever recognised as one, rather
            // than passing through as a plain out the way they used to.
            report.SacrificeFlies += line.SacrificeFlies;
            report.SacrificeBunts += line.SacrificeBunts;
            report.DoublePlays += line.GroundedIntoDoublePlay;
        }

        if (sit.IsOver)
        {
            bool homeWon = sit.HomeScore > sit.AwayScore;
            sit.Stats.FinishGame(
                homeWon ? home : away, homeWon ? away : home,
                homeWon ? sit.HomeScore : sit.AwayScore,
                homeWon ? sit.AwayScore : sit.HomeScore);
        }

        var sb = new StringBuilder();
        sb.Append($"{awayTeam.Abbrev} {sit.AwayScore} at {homeTeam.Abbrev} {sit.HomeScore}");
        sb.Append($"   ({sit.AwayHits}H/{sit.AwayErrors}E vs {sit.HomeHits}H/{sit.HomeErrors}E)");
        sb.Append($"   {sit.Inning} inn, {report.Pitches} pitches");
        if (guard >= 4000) sb.Append("   [ABORTED: game never ended]");
        if (!sit.IsOver) sb.Append("   [WARNING: incomplete]");
        report.Text = sb.ToString();
        return report;
    }
}
