using System.Linq;
using Godot;
using SandlotSlugfest.Data;
using SandlotSlugfest.Season;

namespace SandlotSlugfest.Core;

/// <summary>
/// Autoloaded session state: which clubs are playing, how long the game is, who the human
/// controls. Survives scene changes so the menus can hand a matchup to the game scene.
/// </summary>
public partial class Game : Node
{
    public static Game Instance { get; private set; }

    public int AwayTeamId = 2;    // Bronx Bombardiers
    public int HomeTeamId = 31;   // San Francisco Fog

    /// <summary>Set by the netplay self-test: the next game plays itself.</summary>
    public bool AutoPlayNextGame;
    private int _innings = 9;

    /// <summary>Innings per game, set in Settings. Nine by default, the same as the real thing.</summary>
    public int Innings
    {
        get => _innings;
        set { _innings = Mathf.Clamp(value, 1, 12); Settings.SaveInnings(_innings); }
    }

    private int _seasonLength = Season.Schedule.FullSeason;

    /// <summary>Games per club when a new league is started.</summary>
    public int SeasonLength
    {
        get => _seasonLength;
        set { _seasonLength = value; Settings.SaveSeasonLength(value); }
    }
    public ControlMode Mode = ControlMode.PlayerVsCpu;
    public int LeagueSeed = RosterGenerator.DefaultLeagueSeed;

    /// <summary>
    /// When true the defence plays itself: fielders chase and throw without you. On by default,
    /// because chasing the ball is the least interesting thing a human does in a baseball game
    /// and being forced into it turns every ball in play into a chore.
    /// </summary>
    public bool AutoFielding = true;

    /// <summary>
    /// Manager mode: you run the club and never take the field. Games are simulated, the calendar
    /// is the whole interface, and the front office is the game — the way a management sim works.
    /// Off means player-manager: same franchise, but you play your club's games yourself.
    /// </summary>
    private bool _managerOnly;

    public bool ManagerOnly
    {
        get => _managerOnly;
        set { _managerOnly = value; Settings.SaveManagerOnly(value); }
    }

    private Difficulty _difficulty = Difficulty.Pro;

    /// <summary>The chosen difficulty. Persisted, so it survives a restart.</summary>
    public Difficulty Difficulty
    {
        get => _difficulty;
        set { _difficulty = value; Settings.SaveDifficulty(value); }
    }

    /// <summary>The knobs for the current difficulty.</summary>
    public DifficultyTuning Tuning => DifficultyTuning.For(_difficulty);

    public TeamData AwayTeam => Teams.Get(AwayTeamId);
    public TeamData HomeTeam => Teams.Get(HomeTeamId);

    /// <summary>The league in progress. Loaded from disk if a season is saved.</summary>
    public SeasonState League { get; private set; }

    /// <summary>
    /// A side built out of collected cards, playing as the visitors. Set only by the collection
    /// screen and cleared when the game ends, so nothing else has to know it exists.
    /// </summary>
    public Roster CardClubRoster;

    /// <summary>
    /// A farm game: two affiliates of the same rung, playing each other.
    ///
    /// The lower levels have always simulated — men develop down there and their lines show up in
    /// the Front Office — but a season is more interesting when you can go and watch the kid you
    /// drafted actually play, or take the dugout yourself for a night in Double-A. Set by the farm
    /// screen and cleared when the game ends, so nothing else has to know it exists.
    /// </summary>
    public Roster FarmAwayRoster;
    public Roster FarmHomeRoster;

    /// <summary>Which rung a farm game is being played at, for the banner and the box score.</summary>
    public string FarmLevelName = "";

    /// <summary>
    /// The modelled result this farm game is standing in for: whose affiliate, which rung, and the
    /// score the simulation had already booked. Playing it for real replaces that.
    /// </summary>
    public (int TeamId, int OpponentId, int Level, (int Mine, int Theirs) Was)? FarmReplacing;

    public bool IsFarmGame => FarmAwayRoster != null && FarmHomeRoster != null;

    /// <summary>
    /// Where leaving a game should go back to, when it is not the main menu or the season hub.
    /// A farm game is started from the Front Office and should end there — being dropped on the
    /// title screen after watching your Double-A club is a small thing that feels broken.
    /// </summary>
    public string ReturnTo;

    public void ClearFarmGame()
    {
        FarmAwayRoster = null;
        FarmHomeRoster = null;
        FarmLevelName = "";
        FarmReplacing = null;
    }

    /// <summary>Rosters always come from the league, so trades are reflected in games.</summary>
    public Roster AwayRoster => FarmAwayRoster ?? CardClubRoster ?? League.RosterFor(AwayTeamId);
    public Roster HomeRoster => FarmHomeRoster ?? League.RosterFor(HomeTeamId);

    public void NewSeason(int userTeamId, int gamesPerTeam = Season.Schedule.FullSeason)
    {
        League = new SeasonState();
        League.StartNew(LeagueSeed, userTeamId, gamesPerTeam, Innings);
        SaveGame.Save(League);
    }

    public void SaveLeague() => SaveGame.Save(League);

    /// <summary>Result of the most recent completed game, shown on the results screen.</summary>
    public string LastResultHeadline = "";
    public string LastResultLine = "";

    /// <summary>
    /// Set when the game about to be played is a scheduled season game. The game scene books the
    /// result against it and returns to the season hub instead of the main menu.
    /// </summary>
    public Season.ScheduledGame PendingSeasonGame;

    public override void _EnterTree()
    {
        Instance = this;
        InputActions.Register();

        // Flags that affect how the league is built have to be read here, not in _Ready: the
        // league is constructed a few lines below, which is before _Ready ever runs.
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--nolegends") >= 0)
            Data.RosterGenerator.IncludeLegends = false;

        // Pick up a season in progress, or start a fresh league.
        //
        // A verification run must never do this. Loading a save rebuilds every roster from the
        // file and populates the generator's cache under the same key the harness asks for, so
        // the measurement silently describes whatever league happens to be saved on this machine
        // rather than a freshly generated one. That is exactly what happened when rosters grew
        // from eighteen to twenty-six: the harness kept reporting on the old eighteen-man clubs
        // and a flag meant to change the batting order produced byte-identical output.
        League = IsVerificationRun() ? null : SaveGame.Load();
        if (League == null)
        {
            League = new SeasonState();
            League.StartNew(LeagueSeed, HomeTeamId, _seasonLength, _innings);
        }
        LeagueSeed = League.LeagueSeed;
    }

    /// <summary>The headless harnesses, each of which wants a clean league built from the seed.</summary>
    private static readonly string[] HarnessFlags =
    {
        "--sim", "--hitlab", "--audit-outs", "--unique", "--written", "--flavour", "--drift",
        "--swings", "--legends", "--season", "--balance", "--schedule", "--calendar",
        "--franchise", "--sfxdump", "--parks", "--roster", "--pen",

        // Forgetting to list a harness here is not a small mistake: it silently measures whatever
        // league happens to be on disk instead of a clean one. The platoon audit reported every
        // written player as right-handed for three runs, because the save it was reading had been
        // written before handedness was generated properly — the code was right and the
        // measurement was of something else entirely.
        "--platoon", "--farm",
    };

    private static bool IsVerificationRun()
    {
        var args = OS.GetCmdlineUserArgs();
        foreach (string flag in HarnessFlags)
            if (System.Array.IndexOf(args, flag) >= 0) return true;
        return false;
    }

    public override void _Ready()
    {
        // `godot --headless -- --sim [games]` plays full games with no window and prints box
        // scores, so the rules and physics can be exercised without a controller.
        _difficulty = Settings.LoadDifficulty();
        _managerOnly = Settings.LoadManagerOnly();
        _innings = Settings.LoadInnings();
        _seasonLength = Settings.LoadSeasonLength();
        AutoFielding = Settings.LoadAutoFielding();

        var args = OS.GetCmdlineUserArgs();



        // `--fast N` runs the clock N times faster, for unattended verification runs.
        int fast = System.Array.IndexOf(args, "--fast");
        if (fast >= 0 && fast + 1 < args.Length && float.TryParse(args[fast + 1], out float scale))
            Engine.TimeScale = Mathf.Clamp(scale, 0.1f, 50f);

        TryStartScreenshotRunner(args);

        int seasonArg = System.Array.IndexOf(args, "--season");
        if (seasonArg >= 0)
        {
            int len = Season.Schedule.ShortSeason;
            if (seasonArg + 1 < args.Length && int.TryParse(args[seasonArg + 1], out int parsedLen))
                len = Mathf.Clamp(parsedLen, 8, 200);
            HeadlessSim.RunSeason(len);
            GetTree().Quit();
            return;
        }

        if (System.Array.IndexOf(args, "--hitlab") >= 0)
        {
            HeadlessSim.HitLab(3000);
            GetTree().Quit();
            return;
        }

        if (System.Array.IndexOf(args, "--audit-outs") >= 0)
        {
            HeadlessSim.AuditOuts(40);
            GetTree().Quit();
            return;
        }

        // `--netplay host|join [address] [port]` runs one side of an online match with no hands on
        // it, so the protocol can be tested against a second process.
        int netplay = System.Array.IndexOf(args, "--netplay");
        if (netplay >= 0 && netplay + 1 < args.Length)
        {
            bool host = args[netplay + 1] == "host";
            var test = new Net.NetTest { IsHost = host };

            int at = netplay + 2;
            if (!host && at < args.Length && args[at].Contains('.')) test.Address = args[at++];
            if (at < args.Length && int.TryParse(args[at], out int port)) test.Port = port;

            // The self-test used to give up after 150 seconds, which is the second inning — so it
            // had never once run a whole game through the wire. `--minutes N` gives it long enough
            // to finish, which is what actually needs proving.
            int mins = System.Array.IndexOf(args, "--minutes");
            if (mins >= 0 && mins + 1 < args.Length && int.TryParse(args[mins + 1], out int m))
                test.Timeout = Mathf.Clamp(m, 1, 120) * 60f;

            AddChild(test);
            return;
        }

        // `--pen [games]` audits how the league's pitching staffs are actually used over a season.
        // `--platoon [PA]` measures the left-right split against the real one.
        int plat = System.Array.IndexOf(args, "--platoon");
        if (plat >= 0)
        {
            int pa = 60000;
            if (plat + 1 < args.Length && int.TryParse(args[plat + 1], out int n)) pa = n;
            PlatoonAudit.Run(Mathf.Clamp(pa, 2000, 400000));
            GetTree().Quit();
            return;
        }

        // `--farm` checks that every club's three affiliates can actually field a side, which is
        // what playing or watching a farm game needs and what simulating one never did.
        if (System.Array.IndexOf(args, "--farm") >= 0)
        {
            Season.HeadlessFarm.Audit();
            GetTree().Quit();
            return;
        }

        int pen = System.Array.IndexOf(args, "--pen");
        if (pen >= 0)
        {
            int len = 60;
            if (pen + 1 < args.Length && int.TryParse(args[pen + 1], out int penGames)) len = penGames;
            HeadlessSim.AuditBullpen(Mathf.Clamp(len, 4, 162));
            GetTree().Quit();
            return;
        }

        // `--sfxdump <dir>` writes every effect as a .wav so the synthesis can be inspected.
        int dump = System.Array.IndexOf(args, "--sfxdump");
        if (dump >= 0 && dump + 1 < args.Length)
        {
            string dir = args[dump + 1];
            DirAccess.MakeDirRecursiveAbsolute(dir);
            var all = Audio.Sfx.DumpAll();
            foreach (var (k, v) in Audio.Music.DumpAll()) all[k] = v;
            foreach (var (name, bytes) in all)
            {
                using var f = FileAccess.Open($"{dir}/{name}.wav", FileAccess.ModeFlags.Write);
                if (f != null) f.StoreBuffer(bytes);
            }
            GD.Print($"Wrote {all.Count} clips to {dir}");
            GetTree().Quit();
            return;
        }

        // `--legends` shows where every written kid ended up, and proves each one is actually on
        // a roster rather than merely defined.
        if (System.Array.IndexOf(args, "--legends") >= 0)
        {
            int placed = 0, starters = 0;
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var t in Data.Teams.All)
            {
                var roster = Data.RosterGenerator.For(t);
                foreach (var p in roster.Players)
                {
                    if (!p.IsLegend) continue;
                    placed++;
                    seen.Add(p.LegendId);
                    bool isStarter = roster.Starters.ContainsValue(p) || roster.Pitchers.Contains(p);
                    if (isStarter) starters++;
                    GD.Print($"{t.Abbrev,-4} #{p.Number,-3} {p.Name,-24} {p.Position,-6} " +
                             $"OVR {p.Overall,2} POT {p.Potential,2} {(isStarter ? "starter" : "bench")}");
                }
            }
            GD.Print($"\nplaced {placed} of {Data.Legends.Count} written kids; " +
                     $"distinct {seen.Count}; starting {starters}");
            GetTree().Quit();
            return;
        }

        // `--careers N` runs N offseasons and reports whether a career arc actually happens:
        // young players reaching their ceilings, veterans declining, and players retiring.
        int sched = System.Array.IndexOf(args, "--schedule");
        if (sched >= 0)
        {
            int n = sched + 1 < args.Length && int.TryParse(args[sched + 1], out int v) ? v : 32;
            Season.ScheduleAudit.Run(n);
            GetTree().Quit();
            return;
        }

        if (System.Array.IndexOf(args, "--calendar") >= 0)
        {
            Season.CalendarAudit.Run(League);
            GetTree().Quit();
            return;
        }

        int fran = System.Array.IndexOf(args, "--franchise");
        if (fran >= 0)
        {
            int fy = fran + 1 < args.Length && int.TryParse(args[fran + 1], out int n) ? n : 5;
            Season.FranchiseAudit.Run(League, fy);
            GetTree().Quit();
            return;
        }

        int careers = System.Array.IndexOf(args, "--careers");
        if (careers >= 0)
        {
            int years = careers + 1 < args.Length && int.TryParse(args[careers + 1], out int y) ? y : 8;
            Season.CareerAudit.Run(League, years);
            GetTree().Quit();
            return;
        }

        // `--unique` checks nobody in the league shares a name or a face.
        if (System.Array.IndexOf(args, "--unique") >= 0)
        {
            var names = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();
            var looks = new System.Collections.Generic.Dictionary<int, int>();
            int total = 0;
            foreach (var t in Data.Teams.All)
                foreach (var p in Data.RosterGenerator.For(t).Players)
                {
                    total++;
                    if (!names.TryGetValue(p.Name, out var list)) names[p.Name] = list = new System.Collections.Generic.List<string>();
                    list.Add(t.Abbrev);
                    looks[p.LookSeed] = (looks.TryGetValue(p.LookSeed, out int lc) ? lc : 0) + 1;
                }

            var dupNames = names.Where(kv => kv.Value.Count > 1).ToList();
            int dupLooks = looks.Count(kv => kv.Value > 1);
            GD.Print($"\n=== UNIQUENESS — {total} players across 32 clubs ===");
            GD.Print($"duplicate names: {dupNames.Count}   duplicate look seeds: {dupLooks}");
            foreach (var kv in dupNames.Take(8))
                GD.Print($"  {kv.Key} appears on {string.Join(", ", kv.Value)}");
            GetTree().Quit();
            return;
        }

        // `--names` lists every first name actually in use, so the pool can be eyeballed.
        if (System.Array.IndexOf(args, "--names") >= 0)
        {
            var used = new System.Collections.Generic.SortedSet<string>();
            int n = 0;
            foreach (var t in Data.Teams.All)
                foreach (var p in Data.RosterGenerator.For(t).Players) { used.Add(p.FirstName); n++; }
            GD.Print($"{n} players, {used.Count} distinct first names:");
            GD.Print(string.Join(", ", used));
            GetTree().Quit();
            return;
        }

        // `--deadline` walks the calendar and checks trades open and shut when they should.
        if (System.Array.IndexOf(args, "--deadline") >= 0)
        {
            var st = League;
            GD.Print($"\nschedule runs {st.FinalDay + 1} game days; deadline on day {st.TradeDeadlineDay} " +
                     $"({Season.Calendar.Format(Season.Calendar.DateOf(st.TradeDeadlineDay))})");

            var mine = st.RosterFor(st.UserTeamId).Players[0];
            int partner = (st.UserTeamId + 1) % 32;
            var theirs = st.RosterFor(partner).Players[0];
            var give = new System.Collections.Generic.List<Data.PlayerData> { mine };
            var get = new System.Collections.Generic.List<Data.PlayerData> { theirs };

            bool everBlockedEarly = false, everAllowedLate = false;
            int openDays = 0, shutDays = 0;
            while (st.CurrentDay <= st.FinalDay)
            {
                var v = Season.TradeEngine.Evaluate(st, partner, give, get);
                bool refusedForDeadline = v.Reason != null && v.Reason.Contains("deadline");
                bool past = st.CurrentDay > st.TradeDeadlineDay;

                if (!past && refusedForDeadline) everBlockedEarly = true;
                if (past && !refusedForDeadline) everAllowedLate = true;
                if (past) shutDays++; else openDays++;

                st.AdvanceDay(simulateUserGame: true);
            }

            GD.Print($"days with the window open: {openDays}   shut: {shutDays}");
            GD.Print($"blocked while it should have been open: {everBlockedEarly}");
            GD.Print($"allowed after it should have shut:      {everAllowedLate}");
            GD.Print(!everBlockedEarly && !everAllowedLate ? "DEADLINE OK" : "DEADLINE WRONG");
            GetTree().Quit();
            return;
        }

        // `--rotation` proves the staff actually turns over, and `--skin` reports the spread of
        // complexions across the league.
        if (System.Array.IndexOf(args, "--rotation") >= 0)
        {
            var st = League;
            var used = new System.Collections.Generic.Dictionary<string, int>();
            int guard = 0;
            while (st.CurrentDay <= st.FinalDay && guard++ < 200)
            {
                foreach (var g in st.Games.Where(x => x.Day == st.CurrentDay && !x.Played))
                {
                    var arm = st.RosterFor(g.HomeId).CurrentPitcher;
                    if (arm != null) used[arm.Name] = used.TryGetValue(arm.Name, out int c) ? c + 1 : 1;
                }
                st.AdvanceDay(simulateUserGame: true);
            }
            // Read from the book rather than sampling CurrentPitcher, which lags a day.
            GD.Print("\nstarts and innings by your staff this season:");
            foreach (var p in st.RosterFor(st.UserTeamId).Pitchers)
            {
                var line = st.Book.Pitching(p);
                GD.Print($"  {p.Name,-24} {line.GamesStarted,2} starts  {line.InningsText,5} IP  " +
                         $"{(line.Outs > 0 ? line.Era.ToString("F2") : "-"),5} ERA");
            }
            GetTree().Quit();
            return;
        }

        if (System.Array.IndexOf(args, "--skin") >= 0)
        {
            var tally = new int[8];
            int n = 0;
            foreach (var t in Data.Teams.All)
                foreach (var p in Data.RosterGenerator.For(t).Players)
                {
                    // Mirrors how CartoonPlayer derives the tone from the look seed.
                    var rng = new Rng(p.LookSeed);
                    tally[rng.Range(0, 8)]++;
                    n++;
                }
            GD.Print($"\nskin tones across {n} players (lightest to darkest):");
            for (int i = 0; i < tally.Length; i++)
                GD.Print($"  tone {i}: {tally[i],4}  ({tally[i] * 100f / n:F1}%)");
            GetTree().Quit();
            return;
        }

        // `--strength` shows how far apart the clubs actually are on paper.
        if (System.Array.IndexOf(args, "--strength") >= 0)
        {
            var rows = Data.Teams.All.Select(t =>
            {
                var r = Data.RosterGenerator.For(t);
                float bat = (float)r.BattingOrder.Average(p => p.Overall);
                float arm = (float)r.Pitchers.Take(4).Average(p => p.Overall);
                return (t.Abbrev, Bat: bat, Arm: arm, All: bat + arm);
            }).OrderByDescending(x => x.All).ToList();

            double mean = rows.Average(x => x.All);
            double sd = System.Math.Sqrt(rows.Sum(x => (x.All - mean) * (x.All - mean)) / rows.Count);
            GD.Print($"\nclub strength (lineup OVR + rotation OVR): mean {mean:F2}  sd {sd:F2}");
            GD.Print($"strongest {rows[0].Abbrev} {rows[0].All:F2}   weakest {rows[^1].Abbrev} {rows[^1].All:F2}");
            GD.Print($"biases: power {Data.Teams.All.Max(t => t.PowerBias)}/{Data.Teams.All.Min(t => t.PowerBias)}  " +
                     $"pitch {Data.Teams.All.Max(t => t.PitchingBias)}/{Data.Teams.All.Min(t => t.PitchingBias)}  " +
                     $"def {Data.Teams.All.Max(t => t.DefenseBias)}/{Data.Teams.All.Min(t => t.DefenseBias)}");
            GetTree().Quit();
            return;
        }

        // `--seasonrates` checks a real 162-game season produces the same run environment the
        // calibration harness measures, rather than the two being tuned in separate worlds.
        if (System.Array.IndexOf(args, "--seasonrates") >= 0)
        {
            var st = League;
            int guard = 0;
            while (st.CurrentDay <= st.FinalDay && guard++ < 400) st.AdvanceDay(simulateUserGame: true);

            var played = st.Games.Where(g => g.Played).ToList();
            float runs = (float)played.Average(g => g.AwayRuns + g.HomeRuns);

            long h = 0, hr = 0, bb = 0, k = 0, ab = 0;
            foreach (var (_, line) in st.Book.AllBatting)
            { h += line.Hits; hr += line.HomeRuns; bb += line.Walks; k += line.Strikeouts; ab += line.AtBats; }

            GD.Print($"\n=== A PLAYED 162-GAME SEASON — {played.Count} games, {st.Innings} innings ===");
            GD.Print($"  runs/game {runs:F2}   (real {Core.RealBaseball.Mlb.Runs:F2})");
            GD.Print($"  hits/game {h / (float)played.Count:F2}   (real {Core.RealBaseball.Mlb.Hits:F2})");
            GD.Print($"  HR/game   {hr / (float)played.Count:F2}   (real {Core.RealBaseball.Mlb.HomeRuns:F2})");
            GD.Print($"  BB/game   {bb / (float)played.Count:F2}   (real {Core.RealBaseball.Mlb.Walks:F2})");
            GD.Print($"  K/game    {k / (float)played.Count:F2}   (real {Core.RealBaseball.Mlb.Strikeouts:F2})");
            GD.Print($"  league AVG {(ab > 0 ? h / (float)ab : 0f):.000}   (real {Core.RealBaseball.Mlb.Average:.000})");
            GetTree().Quit();
            return;
        }

        // `--umpire` measures how often calls are missed, and checks the challenge rules.
        if (System.Array.IndexOf(args, "--umpire") >= 0)
        {
            var rng = new Rng(4242);
            var roster = Data.RosterGenerator.For(Data.Teams.Get(0));
            var arm = roster.Pitchers[0];
            var sit = new GameSituation { Away = roster, Home = roster };

            int total = 0, missed = 0, edgeCalls = 0, edgeMissed = 0, middle = 0, middleMissed = 0;
            for (int i = 0; i < 20000; i++)
            {
                CpuBrain.ChoosePitch(sit, arm, ref rng, out var type, out var aim);
                var pitch = PitchFactory.Create(arm, type, aim, 0f, ref rng);

                bool called = Umpire.CallsStrike(pitch, ref rng);
                bool wrong = called != pitch.IsStrike;
                total++;
                if (wrong) missed++;

                float chance = Umpire.MissChance(pitch);
                if (chance > 0.20f) { edgeCalls++; if (wrong) edgeMissed++; }
                else if (chance < 0.02f) { middle++; if (wrong) middleMissed++; }
            }

            GD.Print($"\n=== UMPIRE — {total} taken pitches ===");
            GD.Print($"  calls missed overall:     {missed * 100f / total:F1}%   (real umpires miss about 8%)");
            GD.Print($"  on the black:             {(edgeCalls > 0 ? edgeMissed * 100f / edgeCalls : 0):F1}%  of {edgeCalls}");
            GD.Print($"  well inside or outside:   {(middle > 0 ? middleMissed * 100f / middle : 0):F1}%  of {middle}");

            var bank = new ChallengeBank();
            GD.Print($"\n=== CHALLENGE RULES ===");
            GD.Print($"  start of game:            {bank.Away} each");
            bank.Spend(true, upheld: true);
            GD.Print($"  after a successful one:   {bank.Away}   (rule: the club keeps it)");
            bank.Spend(true, upheld: false);
            GD.Print($"  after a failed one:       {bank.Away}");
            bank.EnterExtraInnings();
            bank.EnterExtraInnings();
            GD.Print($"  into extra innings:       {bank.Away}   (granted once, not per inning)");
            bank.Spend(true, false); bank.Spend(true, false);
            GD.Print($"  spent out:                {bank.Away}, any left: {bank.Any(true)}");
            GetTree().Quit();
            return;
        }

        // `--rules2026` exercises the pitch-timer and disengagement rules directly.
        if (System.Array.IndexOf(args, "--rules2026") >= 0)
        {
            GD.Print("\n=== PITCH TIMER ===");
            GD.Print($"  bases empty:  {Gameplay.GameScene.PitchClockEmpty:F0}s   (real 15)");
            GD.Print($"  runners on:   {Gameplay.GameScene.PitchClockRunners:F0}s   (real 18)");
            GD.Print($"  batter set by: {Gameplay.GameScene.BatterMustBeSetAt:F0}s left   (real 8)");
            GD.Print("  pitcher over time -> ball; batter not set -> strike");

            GD.Print("\n=== DISENGAGEMENTS ===");
            GD.Print($"  allowed per hitter: {Gameplay.GameScene.DisengagementLimit}   (real 2)");
            GD.Print("  a third that fails to retire the runner is a balk");

            // Balk mechanics, checked directly on a situation.
            var roster = Data.RosterGenerator.For(Data.Teams.Get(0));
            var sit = new GameSituation();
            sit.Start(roster, roster, innings: 9);
            sit.Runners[1] = roster.BattingOrder[0];
            sit.Runners[3] = roster.BattingOrder[1];
            int before = sit.HomeScore + sit.AwayScore;
            int runs = sit.AwardBalk();
            GD.Print($"\n  balk with runners on 1st and 3rd:");
            GD.Print($"    run scored from third: {runs}   (expected 1)");
            GD.Print($"    runner now on second:  {sit.RunnerOn(2)}   (expected True)");
            GD.Print($"    first now empty:       {!sit.RunnerOn(1)}   (expected True)");

            sit.Runners[2] = roster.BattingOrder[2];
            int outsBefore = sit.Outs;
            sit.RetireRunner(2);
            GD.Print($"\n  picked off second: outs {outsBefore} -> {sit.Outs}, base empty {!sit.RunnerOn(2)}");
            GetTree().Quit();
            return;
        }

        // `--written` compares the hand-written players against the generated population.
        if (System.Array.IndexOf(args, "--written") >= 0)
        {
            var written = new System.Collections.Generic.List<Data.PlayerData>();
            var made = new System.Collections.Generic.List<Data.PlayerData>();
            foreach (var t in Data.Teams.All)
                foreach (var p in Data.RosterGenerator.For(t).Players)
                    (p.IsLegend ? written : made).Add(p);

            static string Line(string tag, System.Collections.Generic.List<Data.PlayerData> xs) =>
                $"  {tag,-11} n={xs.Count,4}  OVR {xs.Average(p => p.Overall):F2}  " +
                $"CON {xs.Average(p => p.Contact):F2}  POW {xs.Average(p => p.Power):F2}  " +
                $"SPD {xs.Average(p => p.Speed):F2}  FLD {xs.Average(p => p.Fielding):F2}";

            GD.Print("\n=== WRITTEN vs GENERATED ===");
            GD.Print(Line("written", written));
            GD.Print(Line("generated", made));
            GD.Print($"  gap in overall: {written.Average(p => p.Overall) - made.Average(p => p.Overall):+0.00;-0.00}");
            GetTree().Quit();
            return;
        }

        // `--swings` measures how often hitters offer at strikes versus balls.
        if (System.Array.IndexOf(args, "--swings") >= 0)
        {
            var rng = new Rng(31337);
            var roster = Data.RosterGenerator.For(Data.Teams.Get(0));
            var arm = roster.Pitchers[0];
            var sit = new GameSituation { Away = roster, Home = roster };

            int zTot = 0, zSw = 0, oTot = 0, oSw = 0, twoO = 0, twoOSw = 0;
            for (int i = 0; i < 40000; i++)
            {
                var hitter = roster.BattingOrder[i % 9];
                CpuBrain.ChoosePitch(sit, arm, ref rng, out var t, out var aim);
                var pitch = PitchFactory.Create(arm, t, aim, 0f, ref rng);
                sit.Strikes = i % 3 == 0 ? 2 : i % 2;

                var plan = CpuBrain.PlanSwing(sit, hitter, pitch, ref rng);
                if (pitch.IsStrike) { zTot++; if (plan.WillSwing) zSw++; }
                else
                {
                    oTot++; if (plan.WillSwing) oSw++;
                    if (sit.Strikes == 2) { twoO++; if (plan.WillSwing) twoOSw++; }
                }
            }
            sit.Strikes = 0;

            GD.Print("\n=== SWING DECISIONS ===");
            GD.Print($"  at strikes (Z-Swing):     {zSw * 100f / zTot:F1}%   (real 68%)");
            GD.Print($"  at balls  (O-Swing):      {oSw * 100f / oTot:F1}%   (real 31%)");
            GD.Print($"  at balls with two strikes:{twoOSw * 100f / System.Math.Max(1, twoO):F1}%   (real about 45%)");
            GetTree().Quit();
            return;
        }

        // `--drift` runs seasons and watches how the written share of the league changes as
        // players retire and draft classes arrive.
        int drift = System.Array.IndexOf(args, "--drift");
        if (drift >= 0)
        {
            int years = drift + 1 < args.Length && int.TryParse(args[drift + 1], out int dy) ? dy : 10;
            var st = League;
            GD.Print($"\n=== WRITTEN SHARE OVER {years} SEASONS ===");
            GD.Print($"{"yr",3} {"players",8} {"written",8} {"share",7} {"wOVR",6} {"gOVR",6} {"sameNo",7}");

            for (int y = 0; y <= years; y++)
            {
                var all = st.AllRosters.SelectMany(r => r.Players).ToList();
                var w = all.Where(p => p.IsLegend).ToList();
                var g = all.Where(p => !p.IsLegend).ToList();

                // Two men on one club in the same jersey. Numbers are handed out when a roster is
                // first built and never again, so every trade, callup, waiver claim and signing
                // can put a second number 22 on the field.
                int sameNumber = Data.Teams.All.Sum(t => st.RosterFor(t.Id).Players
                    .GroupBy(p => p.Number)
                    .Sum(grp => grp.Count() - 1));

                GD.Print($"{st.Year,3} {all.Count,8} {w.Count,8} {w.Count * 100f / all.Count,6:F1}% " +
                         $"{(w.Count > 0 ? w.Average(p => p.Overall) : 0),6:F2} " +
                         $"{(g.Count > 0 ? g.Average(p => p.Overall) : 0),6:F2} {sameNumber,7}");
                if (y == years) break;

                int guard = 0;
                while (st.CurrentDay <= st.FinalDay && guard++ < 400) st.AdvanceDay(simulateUserGame: true);
                st.Draft.Begin(st, st.LeagueSeed + st.Year);
                int g2 = 0;
                while (!st.Draft.Complete && g2++ < 600) st.Draft.AutoPick(st);
                st.AdvanceToNextSeason();
            }
            GetTree().Quit();
            return;
        }

        // `--flavour` shows what generated players are actually described as, to check the line
        // fits the man rather than being pulled off a shelf.
        if (System.Array.IndexOf(args, "--flavour") >= 0)
        {
            var rng = new Rng(808);
            GD.Print("\n=== GENERATED PLAYERS, AS DESCRIBED ===");
            for (int i = 0; i < 14; i++)
            {
                var p = Data.RosterGenerator.Prospect(50000 + i * 3121, ref rng);
                p.Age = 22 + i % 14;
                string what = p.Position == Data.Position.P
                    ? $"VEL {p.PitchPower} CMD {p.PitchControl} STA {p.Stamina}"
                    : $"CON {p.Contact} POW {p.Power} SPD {p.Speed} FLD {p.Fielding} ARM {p.Arm}";
                GD.Print($"  {p.Name,-26} {p.Position,-6} age {p.Age,2}  {what,-38}");
                GD.Print($"      \"{Data.Flavour.For(p)}\"");
            }
            GetTree().Quit();
            return;
        }

        // `--balance` plays a season and reports the spread of records against real baseball.
        if (System.Array.IndexOf(args, "--balance") >= 0)
        {
            var st = League;
            var talents = new System.Collections.Generic.List<double>();

            // A single 33-game season is a noisy read; average a few before drawing a conclusion.
            for (int season = 0; season < 4; season++)
            {
            int guard = 0;
            while (st.CurrentDay <= st.FinalDay && guard++ < 500) st.AdvanceDay(simulateUserGame: true);

            var pcts = Data.Teams.All.Select(t => st.Book.Record(t.Id))
                .Where(r => r.Wins + r.Losses > 0)
                .Select(r => r.Wins / (float)(r.Wins + r.Losses))
                .OrderByDescending(x => x).ToList();

            double mean = pcts.Average();
            double sd = System.Math.Sqrt(pcts.Sum(x => (x - mean) * (x - mean)) / pcts.Count);
            int played = st.Book.Record(0).Wins + st.Book.Record(0).Losses;

            // With no talent difference at all, a season's records still scatter by this much.
            double luck = System.Math.Sqrt(0.25 / played);

            GD.Print($"\n=== COMPETITIVE BALANCE — {played}-game season ===");
            GD.Print($"best {pcts[0]:F3}   worst {pcts[^1]:F3}   spread {pcts[0] - pcts[^1]:F3}");
            GD.Print($"standard deviation of win%: {sd:F3}");
            GD.Print($"expected from luck alone:   {luck:F3}");
            GD.Print($"talent component:           {System.Math.Sqrt(System.Math.Max(0, sd * sd - luck * luck)):F3}");
            GD.Print($"real MLB talent component:  0.070   (best clubs finish near .600)");
            talents.Add(System.Math.Sqrt(System.Math.Max(0, sd * sd - luck * luck)));
            st.AdvanceToNextSeason();
            }
            GD.Print($"\naveraged over {talents.Count} seasons: talent {talents.Average():F3} (real 0.070)");
            GetTree().Quit();
            return;
        }

        if (System.Array.IndexOf(args, "--parks") >= 0)
        {
            HeadlessSim.ListParks();
            GetTree().Quit();
            return;
        }

        int simIndex = System.Array.IndexOf(args, "--sim");
        if (simIndex < 0) return;

        int games = 5;
        if (simIndex + 1 < args.Length && int.TryParse(args[simIndex + 1], out int parsed))
            games = Mathf.Clamp(parsed, 1, 500);

        HeadlessSim.RunSeries(games);
        GetTree().Quit();
    }

    /// <summary>Development capture mode: `godot -- --shot <dir> [interval] [count]`.</summary>
    private void TryStartScreenshotRunner(string[] args)
    {
        int i = System.Array.IndexOf(args, "--shot");
        if (i < 0) return;

        var runner = new ScreenshotRunner();
        if (i + 1 < args.Length) runner.Directory = args[i + 1];
        if (i + 2 < args.Length && float.TryParse(args[i + 2], out float every)) runner.Interval = every;
        if (i + 3 < args.Length && int.TryParse(args[i + 3], out int count)) runner.Count = count;

        runner.HumanBats = System.Array.IndexOf(args, "--bat") >= 0;

        int sceneArg = System.Array.IndexOf(args, "--scene");
        if (sceneArg >= 0 && sceneArg + 1 < args.Length) runner.Scene = args[sceneArg + 1];

        // Lets a capture run pick the matchup, so different ballparks can be compared.
        int homeArg = System.Array.IndexOf(args, "--home");
        if (homeArg >= 0 && homeArg + 1 < args.Length && int.TryParse(args[homeArg + 1], out int h))
            HomeTeamId = Mathf.Clamp(h, 0, 31);
        int awayArg = System.Array.IndexOf(args, "--away");
        if (awayArg >= 0 && awayArg + 1 < args.Length && int.TryParse(args[awayArg + 1], out int a))
            AwayTeamId = Mathf.Clamp(a, 0, 31);

        AddChild(runner);
    }

    /// <summary>True when a human is batting in the current half inning.</summary>
    public bool HumanBats(bool topHalf) => Mode switch
    {
        ControlMode.PlayerVsCpu or ControlMode.BatOnlyAway => topHalf,
        ControlMode.CpuVsPlayer or ControlMode.BatOnlyHome => !topHalf,
        ControlMode.PlayerVsPlayer => true,
        _ => false,
    };

    /// <summary>
    /// True when a human pitches and fields in the current half inning. The bat-only modes
    /// return false so the player is never parked on a fielding prompt they did not ask for.
    /// </summary>
    public bool HumanFields(bool topHalf) => Mode switch
    {
        ControlMode.PlayerVsCpu => !topHalf,
        ControlMode.CpuVsPlayer => topHalf,
        ControlMode.PlayerVsPlayer => true,
        _ => false,
    };

    public void GoTo(string scenePath) => GetTree().ChangeSceneToFile(scenePath);
}
