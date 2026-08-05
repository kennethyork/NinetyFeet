using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Data;
using SandlotSlugfest.Stats;

namespace SandlotSlugfest.Season;

/// <summary>
/// The live league: every club's roster, the season record book, and the standings. Rosters
/// here are the authoritative ones — trades mutate them, and games record into the same book.
/// </summary>
public sealed class SeasonState
{
    public int LeagueSeed = RosterGenerator.DefaultLeagueSeed;

    /// <summary>
    /// Which season this is. A franchise runs year on year: play a season, settle the playoffs,
    /// draft, then age and develop the whole league and start again with the same clubs and the
    /// same players a year older. Before this the game ended after one season and the draft class
    /// you picked never played a game.
    /// </summary>
    public int Year = 1;

    /// <summary>Games per club, remembered so next year is the same length as this one.</summary>
    public int GamesPerTeam = Schedule.FullSeason;

    /// <summary>Reserve written players already handed out, so nobody appears twice.</summary>
    public readonly System.Collections.Generic.HashSet<int> ReserveUsed = new();

    /// <summary>Champions so far, most recent last, as (year, team id).</summary>
    public List<(int Year, int TeamId)> History = new();

    /// <summary>What happened to the league in the most recent offseason.</summary>
    public List<Progress> LastOffseason = new();

    /// <summary>Last winter's business — signings, promotions, waiver claims.</summary>
    public List<string> LastWinter = new();

    /// <summary>The trophies handed out at the end of the season just finished.</summary>
    public List<AwardWin> LastAwards = new();

    /// <summary>The schedule day the league is currently sitting on.</summary>
    public int CurrentDay;

    /// <summary>Today's date.</summary>
    public System.DateTime Today => Calendar.DateOf(CurrentDay);

    /// <summary>Where the league is in its year.</summary>
    public SeasonPhase Phase =>
        !HasSchedule ? SeasonPhase.Preseason
        : !RegularSeasonComplete ? SeasonPhase.RegularSeason
        : !Playoffs.Finished ? SeasonPhase.Playoffs
        : !Draft.Complete || Draft.Order.Count == 0 ? SeasonPhase.Draft
        : SeasonPhase.Offseason;

    /// <summary>The schedule day the trade deadline falls on.</summary>
    public int TradeDeadlineDay => Mathf.RoundToInt(FinalDay * Calendar.TradeDeadlineFraction);

    /// <summary>Whether clubs are still doing business.</summary>
    public bool TradesOpen => Phase == SeasonPhase.Offseason
                              || Phase == SeasonPhase.Preseason
                              || CurrentDay <= TradeDeadlineDay;

    /// <summary>Game days left before the deadline, or 0 once it has passed.</summary>
    public int DaysToDeadline => Mathf.Max(0, TradeDeadlineDay - CurrentDay);

    /// <summary>The last schedule day that has any games on it.</summary>
    public int FinalDay => Games.Count == 0 ? 0 : Games.Max(g => g.Day);

    /// <summary>The user's next game, if it falls on the current day.</summary>
    public ScheduledGame UserGameToday() =>
        Games.FirstOrDefault(g => !g.Played && g.Day == CurrentDay && g.Involves(UserTeamId));

    /// <summary>Headlines from the days just gone, newest first.</summary>
    public List<NewsItem> News = new();

    private void Report(string headline, string detail = "", int teamId = -1) =>
        News.Insert(0, new NewsItem { Day = CurrentDay, Headline = headline, Detail = detail, TeamId = teamId });

    /// <summary>True once the draft is done and the league is ready to roll into next year.</summary>
    public bool OffseasonReady => Playoffs.Finished && Draft.Order.Count > 0 && Draft.Complete;
    public int GamesPlayed;

    /// <summary>The club the human is running, for the trade screen.</summary>
    public int UserTeamId;

    public readonly StatBook Book = new();
    private readonly Dictionary<int, Roster> _rosters = new();
    private readonly Dictionary<int, ClubBook> _books = new();

    /// <summary>A club's money and gate for the season.</summary>
    public ClubBook Books(int teamId)
    {
        if (_books.TryGetValue(teamId, out var b)) return b;
        b = new ClubBook { Budget = Finances.BaselineBudget };
        _books[teamId] = b;
        return b;
    }

    /// <summary>Players with no club, waiting for someone to sign them.</summary>
    public List<PlayerData> FreeAgents = new();

    /// <summary>Awards, champions and record holders, year by year.</summary>
    public readonly LeagueHistory Annals = new();

    /// <summary>The full season schedule, ordered by day.</summary>
    public List<ScheduledGame> Games = new();

    /// <summary>Innings each game is played to. Kept with the season so it stays consistent.</summary>
    /// <summary>
    /// Nine, like real baseball. This used to be six while the calibration harness measured nine,
    /// so every rate matched against the majors applied to a game length the league never played —
    /// a real season came out about a third short on runs.
    /// </summary>
    public int Innings = 9;

    public readonly PlayoffBracket Playoffs = new();

    /// <summary>The amateur draft, held once the postseason is over.</summary>
    public readonly Draft Draft = new();

    /// <summary>True once a champion is crowned and the draft can be held.</summary>
    public bool DraftReady => Playoffs.Finished && Draft.Order.Count == 0;

    public bool HasSchedule => Games.Count > 0;
    public bool RegularSeasonComplete => HasSchedule && Games.All(g => g.Played);

    /// <summary>The user's next unplayed game, or null once their season is done.</summary>
    public ScheduledGame NextUserGame() =>
        Games.FirstOrDefault(g => !g.Played && g.Involves(UserTeamId));

    /// <summary>Games left on the user's slate.</summary>
    public int UserGamesRemaining() => Games.Count(g => !g.Played && g.Involves(UserTeamId));

    public int UserGamesPlayed() => Games.Count(g => g.Played && g.Involves(UserTeamId));

    public Roster RosterFor(int teamId)
    {
        if (_rosters.TryGetValue(teamId, out var r)) return r;
        r = RosterGenerator.For(Teams.Get(teamId), LeagueSeed);
        _rosters[teamId] = r;
        return r;
    }

    public IEnumerable<Roster> AllRosters => Teams.All.Select(t => RosterFor(t.Id));

    public void StartNew(int seed, int userTeamId, int gamesPerTeam = Schedule.FullSeason, int innings = 9)
    {
        GamesPerTeam = gamesPerTeam;
        LeagueSeed = seed;
        UserTeamId = userTeamId;
        GamesPlayed = 0;
        Innings = innings;
        _rosters.Clear();
        RosterGenerator.ResetCache();
        foreach (var t in Teams.All) RosterFor(t.Id);

        Games = Schedule.Build(gamesPerTeam, seed);
        Playoffs.Series.Clear();
        Playoffs.ChampionId = -1;
        Year = 1;
        History.Clear();
        News.Clear();
        CurrentDay = 0;
        Injuries.ClearAll(this);

        // Everyone starts the league on a contract, worked out from how long he has been up.
        var money = new Core.Rng(seed * 2971 + 401);
        foreach (var r in AllRosters)
            foreach (var p in r.Players)
                Contracts.Establish(p, ref money);

        FreeAgents.Clear();
        Finances.OpenBooks(this);
        Farm.Stock(this, seed);
        Farm.PlaySeason(this, gamesPerTeam, seed + 211);
        FarmSeason.Clear();
        Coaches.Stock(seed);

        Inbox.Clear();
        Inbox.OpeningDay(this);
        Inbox.ScoutingReport(this);
    }

    /// <summary>
    /// Closes the year out and starts the next one: the champion goes into the record books, every
    /// player ages and develops, retirements are filled, and a fresh schedule is drawn. The clubs
    /// and their players carry over — that continuity is the whole point of a franchise.
    /// </summary>
    public void AdvanceToNextSeason(int gamesPerTeam = 0)
    {
        if (gamesPerTeam <= 0) gamesPerTeam = GamesPerTeam;
        if (Playoffs.ChampionId >= 0) History.Add((Year, Playoffs.ChampionId));

        // The order here matters, and each step reads something the next one destroys.
        //
        // Trophies and records are decided off the season that has just finished, so they have to
        // be settled before the book is closed. The clubs' books are worked out from the standings
        // and the gate, which the same close wipes. Only then can careers be totted up, which is
        // what a hall-of-fame case is made of — evaluating a retiring player any earlier judges him
        // on every season but his last.
        LastAwards = Awards.Decide(this);
        foreach (var a in LastAwards)
            Report($"{a.PlayerName} wins the {a.Award}", a.Line, a.TeamId);

        Finances.CloseBooks(this);
        Book.CloseSeason();

        LastOffseason = Development.RunOffseason(this, LeagueSeed + Year * 31);

        foreach (var gone in LastOffseason.Where(p => p.Player.Retired))
        {
            var enshrined = Awards.ConsiderForHall(this, gone.Player);
            if (enshrined == null) continue;
            Report($"{enshrined.Name} is elected to the Hall of Fame", enshrined.Career, gone.TeamId);
        }

        // The owner's verdict on the year that just finished, before the winter starts.
        {
            var mine = Book.Record(UserTeamId);
            Inbox.SeasonReview(this, mine.Wins, mine.Losses,
                Playoffs.Started && Playoffs.Series.Any(x => x.Involves(UserTeamId)),
                Playoffs.ChampionId == UserTeamId);
        }

        LastWinter = new List<string>();

        // The bill for a payroll comes before the winter's shopping, so a club that spent last year
        // goes into free agency with less. That is the whole point of a tax rather than a cap.
        LastWinter.AddRange(Finances.SettleTax(this));

        LastWinter.AddRange(Coaches.RunOffseason(LeagueSeed + Year * 907));
        LastWinter.AddRange(Farm.RunOffseason(this, LeagueSeed + Year * 131));
        LastWinter.AddRange(FreeAgency.Run(this, LeagueSeed + Year * 61));
        foreach (var line in LastWinter) Report(line);

        Year++;
        GamesPlayed = 0;

        // The affiliates play their own season the moment the calendar turns, so a prospect always
        // has a current line to be judged on rather than last year's. The records start again with
        // the year, the same as the big club's.
        Farm.PlaySeason(this, gamesPerTeam, LeagueSeed + Year * 211);
        FarmSeason.Clear();

        Inbox.OpeningDay(this);
        Inbox.ScoutingReport(this);

        // A new schedule each year, so the same club does not face the same opponents in the same
        // order every season.
        GamesPerTeam = gamesPerTeam;
        Games = Schedule.Build(gamesPerTeam, LeagueSeed + Year * 7919);

        Playoffs.Series.Clear();
        Playoffs.ChampionId = -1;

        // Everyone reports to camp healthy, and the calendar goes back to opening day.
        Injuries.ClearAll(this);
        CurrentDay = 0;
        News.Clear();

        Draft.Available.Clear();
        Draft.Picks.Clear();
        Draft.Order.Clear();
        Draft.Current = 0;

        // Last year's numbers are folded into career totals, then the year is cleared.
        Book.CloseSeason();
    }

    // -----------------------------------------------------------------------
    // Advancing the season
    // -----------------------------------------------------------------------

    /// <summary>
    /// Simulates every unplayed game on the same day as the user's next game, apart from the
    /// user's own, so the standings move with them.
    /// </summary>
    public void SimulateRestOfDay(ScheduledGame userGame)
    {
        if (userGame == null) return;
        foreach (var g in Games.Where(g => g.Day == userGame.Day && !g.Played && g != userGame).ToList())
            SimulateGame(g);
    }

    private Core.Rng _injuryRng = new(0x1E7A);

    /// <summary>Names the day's starter from the rotation, skipping anyone hurt.</summary>
    private static void TakeTheBall(Roster roster, int day)
    {
        if (roster.Pitchers.Count == 0) return;

        var rotation = roster.Rotation.ToList();
        for (int i = 0; i < rotation.Count; i++)
        {
            var arm = rotation[(day + i) % rotation.Count];
            if (arm.IsInjured) continue;
            roster.SetPitcher(arm);
            return;
        }

        // The whole rotation is hurt, so a long man makes a spot start.
        var spot = roster.Pitchers
            .Where(p => !p.IsInjured)
            .OrderByDescending(p => p.Stamina)
            .FirstOrDefault();
        if (spot != null) roster.SetPitcher(spot);
    }

    /// <summary>Simulates a single scheduled game and books the result.</summary>
    public void SimulateGame(ScheduledGame game)
    {
        if (game.Played) return;

        // Injuries used to be rolled here, once per club per game. With a calendar that is wrong
        // twice over: a club that plays twice on one date got two rolls, and a club with a day off
        // got none — so nobody healed on an off day. It is now a daily tick in AdvanceDay.
        Injuries.Cover(RosterFor(game.AwayId));
        Injuries.Cover(RosterFor(game.HomeId));

        // Turn the rotation over. Clubs carry four starters and a closer, and who takes the ball
        // follows the calendar the way a real rotation does.
        TakeTheBall(RosterFor(game.AwayId), game.Day);
        TakeTheBall(RosterFor(game.HomeId), game.Day);

        // The same sky the human would have played under. A simulated game and a played one have
        // to run under identical conditions or the standings are decided partly by which games
        // the user happened to attend.
        var sky = Weather.For(this, game);
        Core.FieldGeometry.SetConditions(sky.Wind, sky.TemperatureF);

        var sit = Core.QuickGame.Simulate(
            RosterFor(game.AwayId), RosterFor(game.HomeId), Innings,
            LeagueSeed * 7919 + game.Day * 131 + game.HomeId);

        Core.FieldGeometry.ClearConditions();

        game.AwayRuns = sit.AwayScore;
        game.HomeRuns = sit.HomeScore;
        game.Played = true;
        game.Crowd = Attendance.For(this, game);
        Attendance.Record(this, game, game.Crowd);
        Book.Absorb(sit.Stats);
        GamesPlayed++;
    }

    /// <summary>Plays out every remaining game the user is not involved in, up to a given day.</summary>
    public void SimulateThrough(int day)
    {
        foreach (var g in Games.Where(g => g.Day <= day && !g.Played && !g.Involves(UserTeamId)).ToList())
            SimulateGame(g);
    }

    /// <summary>
    /// Moves the league on one day: everyone heals or gets hurt, every game on the date is played,
    /// and anything worth reading about is written up. The user's own game is left alone unless
    /// <paramref name="simulateUserGame"/> is set, so they can go and play it.
    /// </summary>
    public void AdvanceDay(bool simulateUserGame = false)
    {
        // A day passes for every club, whether or not it has a game.
        foreach (var t in Teams.All)
        {
            var roster = RosterFor(t.Id);
            var before = roster.Players.Where(p => !p.IsInjured).ToList();

            // Arms recover. A day off is worth a day of rest whether or not the club played, and
            // the recent workload that decides who is available fades over about three days.
            foreach (var arm in roster.Pitchers)
            {
                arm.RestDays = Mathf.Min(arm.RestDays + 1, 9);
                arm.RecentPitches = Mathf.Max(0, arm.RecentPitches - 12);
            }

            Injuries.Advance(roster, ref _injuryRng);

            foreach (var p in before)
                if (p.IsInjured)
                    Report($"{p.Name} — {p.Injury}",
                           $"{t.Abbrev} lose him for about {p.DaysOut} games.", t.Id);
        }

        foreach (var g in Games.Where(g => g.Day == CurrentDay && !g.Played).ToList())
        {
            if (!simulateUserGame && g.Involves(UserTeamId)) continue;
            SimulateGame(g);
        }

        // The affiliates play the same day. The whole organisation moves at once, so a club's
        // Double-A side has a record that means something by June rather than a table of numbers
        // that appears from nowhere in October.
        FarmSeason.PlayDay(this, CurrentDay);

        // And anybody on the staff with something worth saying says it.
        Inbox.Daily(this);

        bool wasOpen = CurrentDay <= TradeDeadlineDay;
        CurrentDay++;
        if (wasOpen && CurrentDay > TradeDeadlineDay)
            Report("The trade deadline has passed", "Rosters are locked for the rest of the season.");

        // Trim the feed; nobody reads back a month.
        if (News.Count > 60) News.RemoveRange(60, News.Count - 60);
    }

    /// <summary>
    /// Advances until the user's club has a game to play, or the regular season runs out. Returns
    /// that game, or null if there is nothing left to play.
    /// </summary>
    public ScheduledGame AdvanceToNextUserGame(int maxDays = 400)
    {
        for (int i = 0; i < maxDays; i++)
        {
            var mine = UserGameToday();
            if (mine != null) return mine;
            if (CurrentDay > FinalDay) return null;
            AdvanceDay();
        }
        return null;
    }

    /// <summary>Advances a run of days, stopping early if the user's club is due to play.</summary>
    public void AdvanceDays(int days)
    {
        for (int i = 0; i < days; i++)
        {
            if (UserGameToday() != null) return;
            if (CurrentDay > FinalDay) return;
            AdvanceDay();
        }
    }

    /// <summary>Records a game the player actually played, from its finished situation.</summary>
    public void RecordUserGame(ScheduledGame game, Core.GameSituation sit)
    {
        if (game == null || game.Played) return;
        game.AwayRuns = sit.AwayScore;
        game.HomeRuns = sit.HomeScore;
        game.Played = true;
        if (game.Crowd <= 0) game.Crowd = Attendance.For(this, game);
        Attendance.Record(this, game, game.Crowd);
        Book.Absorb(sit.Stats);
        GamesPlayed++;

        // Once the human's game is in the books the rest of the date plays out and the calendar
        // moves on. This must not live in SimulateGame: every simulated game would then advance
        // the calendar, and advancing the calendar simulates games — the league ran away from you
        // at about seventeen days per click.
        if (game.Day == CurrentDay) AdvanceDay(simulateUserGame: true);
    }

    /// <summary>Builds the bracket once the regular season is finished.</summary>
    public void BeginPlayoffsIfReady()
    {
        if (!RegularSeasonComplete || Playoffs.Started) return;
        Playoffs.Build(this);
        Playoffs.Advance(this);
    }

    /// <summary>Plays the next postseason game. Returns the series it belonged to.</summary>
    public PlayoffSeries SimulateNextPlayoffGame()
    {
        var series = Playoffs.NextLive();
        if (series == null) return null;

        int gameNo = series.GamesPlayed + 1;
        bool highHosts = series.HighSeedHosts(gameNo);
        int homeId = highHosts ? series.HighSeedId : series.LowSeedId;
        int awayId = highHosts ? series.LowSeedId : series.HighSeedId;

        var sit = Core.QuickGame.Simulate(
            RosterFor(awayId), RosterFor(homeId), Innings,
            LeagueSeed * 104729 + series.Round.Length * 7717 + gameNo * 313 + homeId);

        int winner = sit.HomeScore > sit.AwayScore ? homeId : awayId;
        Playoffs.RecordGame(series, winner);

        // The bracket keeps its own result; the standings are a record of the regular season.
        Book.Absorb(sit.Stats, countTowardRecord: false);
        Playoffs.Advance(this);
        return series;
    }

    /// <summary>Rolls a completed game into the season book.</summary>
    public void RecordGame(Core.GameSituation game)
    {
        Book.Absorb(game.Stats);
        GamesPlayed++;
    }

    // -----------------------------------------------------------------------
    // Standings
    // -----------------------------------------------------------------------

    public IEnumerable<(TeamData Team, TeamRecord Record)> Standings(League league, Division division) =>
        Teams.In(league, division)
             .Select(t => (t, Book.Record(t.Id)))
             .OrderByDescending(x => x.Item2.WinPct)
             .ThenByDescending(x => x.Item2.RunDifferential);

    public IEnumerable<(TeamData Team, TeamRecord Record)> AllStandings() =>
        Teams.All.Select(t => (t, Book.Record(t.Id)))
                 .OrderByDescending(x => x.Item2.WinPct)
                 .ThenByDescending(x => x.Item2.RunDifferential);

    /// <summary>Which club a player currently belongs to.</summary>
    public TeamData TeamOf(PlayerData player)
    {
        foreach (var t in Teams.All)
            if (RosterFor(t.Id).Players.Contains(player)) return t;
        return null;
    }

    // -----------------------------------------------------------------------
    // Leaderboards
    // -----------------------------------------------------------------------

    public List<(PlayerData Player, BattingLine Line)> HittingLeaders(
        System.Func<BattingLine, float> by, int minAtBats, int take = 10) =>
        Book.QualifiedHitters(minAtBats)
            .OrderByDescending(x => by(x.Line))
            .Take(take)
            .ToList();

    public List<(PlayerData Player, PitchingLine Line)> PitchingLeaders(
        System.Func<PitchingLine, float> by, bool ascending, int minOuts, int take = 10)
    {
        var q = Book.QualifiedPitchers(minOuts);
        var ordered = ascending
            ? q.OrderBy(x => by(x.Line))
            : q.OrderByDescending(x => by(x.Line));
        return ordered.Take(take).ToList();
    }
}
