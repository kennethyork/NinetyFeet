using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using SandlotSlugfest.Data;
using SandlotSlugfest.Stats;

namespace SandlotSlugfest.Season;

/// <summary>
/// Reads and writes the season to <c>user://season.json</c>. Players are stored in full rather
/// than regenerated from the seed, because trades move them between clubs.
/// </summary>
public static class SaveGame
{
    private const string Path = "user://season.json";

    private sealed class PlayerDto
    {
        public int Id { get; set; }
        public string First { get; set; }
        public string Last { get; set; }
        public int Number { get; set; }
        public int Pos { get; set; }
        public int Bats { get; set; }
        public int Throws { get; set; }
        public int Special { get; set; }
        public int Arch { get; set; }
        public int Age { get; set; }
        public int Pot { get; set; }
        public int Rep { get; set; }

        /// <summary>
        /// Which written kid this is, or -1 for a generated player. Saved because loading a season
        /// rebuilds every roster from this file — without it, the named kids lost their identity
        /// (and so their written appearance and biography) the moment a save was reloaded.
        /// Defaults to 0 in older saves, so it is stored offset by one.
        /// </summary>
        public int Legend { get; set; }
        public int Con { get; set; }
        public int Pow { get; set; }
        public int Spd { get; set; }
        public int Arm { get; set; }
        public int Fld { get; set; }
        public int PPow { get; set; }
        public int PCon { get; set; }
        public int Sta { get; set; }
        public int Look { get; set; }

        // --- The contract and the job. All default to 0 in an older save, which is why the
        // loader treats a zero salary as "never had one" and works him out a fresh deal. ---
        public int Sal { get; set; }
        public int CYr { get; set; }
        public int Svc { get; set; }
        public int Role { get; set; }

        public int[] Bat { get; set; }     // batting counters
        public int[] Pit { get; set; }     // pitching counters
        public int[] Car { get; set; }     // career batting, then career pitching
        public int[] Min { get; set; }     // Triple-A batting, then Triple-A pitching
        public int Seasons { get; set; }
    }

    private sealed class TeamDto
    {
        public int Id { get; set; }
        public int[] PlayerIds { get; set; }
        public int[] FarmIds { get; set; }

        /// <summary>The lower two rungs. Absent in a save written before they were stored.</summary>
        public int[] DoubleAIds { get; set; }
        public int[] HighAIds { get; set; }

        /// <summary>Each affiliate's record, in level order: Triple-A, Double-A, High-A.</summary>
        public int[] FarmWins { get; set; }
        public int[] FarmLosses { get; set; }

        /// <summary>Consecutive seasons over the luxury tax line, which sets the rate.</summary>
        public int TaxYears { get; set; }

        /// <summary>The coaching staff. Absent in a save from before there was one.</summary>
        public int[] CoachIds { get; set; }
        public string[] CoachNames { get; set; }
        public int[] CoachRoles { get; set; }
        public int[] CoachSkills { get; set; }
        public int[] CoachSalaries { get; set; }
        public int[] CoachYears { get; set; }

        public int W { get; set; }
        public int L { get; set; }
        public int RS { get; set; }
        public int RA { get; set; }

        /// <summary>The club's money and gate.</summary>
        public int Budget { get; set; }
        public long Gate { get; set; }
        public int Dates { get; set; }
    }

    private sealed class AwardDto
    {
        public int Y { get; set; }
        public string A { get; set; }
        public int Pid { get; set; }
        public string Who { get; set; }
        public int T { get; set; }
        public string L { get; set; }
    }

    private sealed class HallDto
    {
        public int Y { get; set; }
        public string Who { get; set; }
        public string Pos { get; set; }
        public string Career { get; set; }
        public int Score { get; set; }
    }

    private sealed class RecordDto
    {
        public string S { get; set; }
        public float V { get; set; }
        public int Y { get; set; }
        public string Who { get; set; }
        public int T { get; set; }
    }

    private sealed class GameDto
    {
        public int D { get; set; }      // day
        public int A { get; set; }      // away team id
        public int H { get; set; }      // home team id
        public bool P { get; set; }     // played
        public int AR { get; set; }     // away runs
        public int HR { get; set; }     // home runs
        public int C { get; set; }      // crowd
    }

    private sealed class SeriesDto
    {
        public string R { get; set; }
        public int Hi { get; set; }
        public int Lo { get; set; }
        public int HW { get; set; }
        public int LW { get; set; }
        public int Best { get; set; }
    }

    private sealed class SaveDto
    {
        public int Seed { get; set; }
        public int GamesPlayed { get; set; }
        public int UserTeamId { get; set; }
        public int Innings { get; set; }
        public List<PlayerDto> Players { get; set; }
        public List<TeamDto> TeamList { get; set; }
        public List<GameDto> Schedule { get; set; }
        public List<SeriesDto> Playoffs { get; set; }
        public int ChampionId { get; set; } = -1;

        /// <summary>
        /// The calendar. Stored as day+1 so an older save, which has no field at all and so reads
        /// 0, is recognisable as "unknown" rather than being mistaken for opening day.
        /// </summary>
        public int DayPlusOne { get; set; }

        public int Year { get; set; }

        /// <summary>Players belonging to nobody, by id. They are stored in <c>Players</c> too.</summary>
        public int[] FreeAgentIds { get; set; }

        // The club's correspondence. A letter from the owner setting the year's expectations is
        // worthless if it evaporates when you close the game.
        public int[] MailFrom { get; set; }
        public string[] MailSubject { get; set; }
        public string[] MailBody { get; set; }
        public int[] MailDay { get; set; }
        public int[] MailYear { get; set; }
        public bool[] MailRead { get; set; }
        public string[] MailAbout { get; set; }

        public List<AwardDto> Awards { get; set; }
        public List<HallDto> Hall { get; set; }
        public List<RecordDto> Records { get; set; }
    }

    public static bool Exists() => FileAccess.FileExists(Path);

    public static void Delete()
    {
        if (Exists()) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(Path));
    }

    public static void Save(SeasonState season)
    {
        var dto = new SaveDto
        {
            Seed = season.LeagueSeed,
            GamesPlayed = season.GamesPlayed,
            DayPlusOne = season.CurrentDay + 1,
            Year = season.Year,
            UserTeamId = season.UserTeamId,
            Innings = season.Innings,
            Players = new List<PlayerDto>(),
            TeamList = new List<TeamDto>(),
            MailFrom = Inbox.Export().From,
            MailSubject = Inbox.Export().Subject,
            MailBody = Inbox.Export().Body,
            MailDay = Inbox.Export().Day,
            MailYear = Inbox.Export().Year,
            MailRead = Inbox.Export().Read,
            MailAbout = Inbox.Export().About,
            Schedule = season.Games.ConvertAll(g => new GameDto
            {
                D = g.Day, A = g.AwayId, H = g.HomeId, P = g.Played, AR = g.AwayRuns,
                HR = g.HomeRuns, C = g.Crowd,
            }),
            Playoffs = season.Playoffs.Series.ConvertAll(s => new SeriesDto
            {
                R = s.Round, Hi = s.HighSeedId, Lo = s.LowSeedId,
                HW = s.HighWins, LW = s.LowWins, Best = s.BestOf,
            }),
            ChampionId = season.Playoffs.ChampionId,
        };

        foreach (var team in Teams.All)
        {
            var roster = season.RosterFor(team.Id);
            var rec = season.Book.Record(team.Id);
            var books = season.Books(team.Id);
            var farm = Farm.Of(team.Id);

            dto.TeamList.Add(new TeamDto
            {
                Id = team.Id,
                PlayerIds = roster.Players.Select(p => p.Id).ToArray(),
                FarmIds = farm.Select(p => p.Id).ToArray(),

                // The lower two rungs were never written down. Farm.Of(id) means Triple-A, so a
                // save stored that one level and nothing else — and because loading skips
                // restocking when Triple-A comes back non-empty, Double-A and High-A came back
                // empty every time. Men developed down there all season and were deleted on load.
                DoubleAIds = Farm.Of(team.Id, Farm.Level.DoubleA).Select(p => p.Id).ToArray(),
                HighAIds = Farm.Of(team.Id, Farm.Level.HighA).Select(p => p.Id).ToArray(),
                FarmWins = FarmSeason.Export(team.Id).Wins,
                FarmLosses = FarmSeason.Export(team.Id).Losses,
                W = rec.Wins,
                L = rec.Losses,
                RS = rec.RunsScored,
                RA = rec.RunsAllowed,
                Budget = books.Budget,
                Gate = books.Attendance,
                Dates = books.HomeDates,
                TaxYears = books.TaxYears,
                CoachIds = Coaches.Export(team.Id).Ids,
                CoachNames = Coaches.Export(team.Id).Names,
                CoachRoles = Coaches.Export(team.Id).Roles_,
                CoachSkills = Coaches.Export(team.Id).Skills,
                CoachSalaries = Coaches.Export(team.Id).Salaries,
                CoachYears = Coaches.Export(team.Id).Years,
            });

            foreach (var p in roster.Players) dto.Players.Add(ToDto(p, season.Book));
            foreach (var p in farm) dto.Players.Add(ToDto(p, season.Book));
        }

        // Free agents belong to nobody, so they are written alongside the clubs' players rather
        // than under one of them.
        dto.FreeAgentIds = season.FreeAgents.Select(p => p.Id).ToArray();
        foreach (var p in season.FreeAgents) dto.Players.Add(ToDto(p, season.Book));

        dto.Awards = season.Annals.Awards.ConvertAll(a => new AwardDto
        {
            Y = a.Year, A = a.Award, Pid = a.PlayerId, Who = a.PlayerName, T = a.TeamId, L = a.Line,
        });
        dto.Hall = season.Annals.Hall.ConvertAll(h => new HallDto
        {
            Y = h.Year, Who = h.Name, Pos = h.Position, Career = h.Career, Score = h.Score,
        });
        dto.Records = season.Annals.Records
            .Select(kv => new RecordDto
            {
                S = kv.Key, V = kv.Value.Value, Y = kv.Value.Year,
                Who = kv.Value.PlayerName, T = kv.Value.TeamId,
            })
            .ToList();

        string json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = false });
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PushError($"Could not write {Path}: {FileAccess.GetOpenError()}");
            return;
        }
        file.StoreString(json);
    }

    public static SeasonState Load()
    {
        if (!Exists()) return null;

        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
        if (file == null) return null;

        SaveDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<SaveDto>(file.GetAsText());
        }
        catch (JsonException e)
        {
            GD.PushError($"Season save is corrupt, starting fresh: {e.Message}");
            return null;
        }
        if (dto?.Players == null || dto.TeamList == null) return null;

        var season = new SeasonState
        {
            LeagueSeed = dto.Seed,
            GamesPlayed = dto.GamesPlayed,
            Year = dto.Year <= 0 ? 1 : dto.Year,
            UserTeamId = dto.UserTeamId,
            Innings = dto.Innings <= 0 ? 9 : dto.Innings,
        };

        if (dto.Schedule != null)
            season.Games = dto.Schedule.ConvertAll(g => new ScheduledGame
            {
                Day = g.D, AwayId = g.A, HomeId = g.H, Played = g.P, AwayRuns = g.AR,
                HomeRuns = g.HR, Crowd = g.C,
            });

        if (dto.Playoffs != null)
        {
            foreach (var s in dto.Playoffs)
                season.Playoffs.Series.Add(new PlayoffSeries
                {
                    Round = s.R, HighSeedId = s.Hi, LowSeedId = s.Lo,
                    HighWins = s.HW, LowWins = s.LW, BestOf = s.Best <= 0 ? 7 : s.Best,
                });
            season.Playoffs.ChampionId = dto.ChampionId;
        }

        // Rebuild every player, then hand them to the club that owns them.
        var byId = new Dictionary<int, PlayerData>();
        foreach (var pd in dto.Players)
        {
            var p = FromDto(pd);
            byId[p.Id] = p;
            ApplyStats(season.Book, p, pd);
        }

        Farm.Clear();

        foreach (var td in dto.TeamList)
        {
            var roster = season.RosterFor(td.Id);
            roster.Players.Clear();
            roster.Pitchers.Clear();
            roster.Starters.Clear();
            roster.BattingOrder.Clear();

            foreach (int id in td.PlayerIds)
                if (byId.TryGetValue(id, out var p)) roster.Players.Add(p);

            TradeEngine.Rebuild(roster);

            var farm = Farm.Of(td.Id);
            foreach (int id in td.FarmIds ?? System.Array.Empty<int>())
                if (byId.TryGetValue(id, out var p)) farm.Add(p);

            var doubleA = Farm.Of(td.Id, Farm.Level.DoubleA);
            foreach (int id in td.DoubleAIds ?? System.Array.Empty<int>())
                if (byId.TryGetValue(id, out var p)) doubleA.Add(p);

            var highA = Farm.Of(td.Id, Farm.Level.HighA);
            foreach (int id in td.HighAIds ?? System.Array.Empty<int>())
                if (byId.TryGetValue(id, out var p)) highA.Add(p);

            FarmSeason.Import(td.Id, td.FarmWins, td.FarmLosses);

            var rec = season.Book.Record(td.Id);
            rec.Wins = td.W;
            rec.Losses = td.L;
            rec.RunsScored = td.RS;
            rec.RunsAllowed = td.RA;

            var books = season.Books(td.Id);
            books.Budget = td.Budget > 0 ? td.Budget : Finances.BaselineBudget;
            books.TaxYears = td.TaxYears;
            Coaches.Import(td.Id, td.CoachIds, td.CoachNames, td.CoachRoles, td.CoachSkills,
                td.CoachSalaries, td.CoachYears);
            books.Attendance = td.Gate;
            books.HomeDates = td.Dates;
        }

        foreach (int id in dto.FreeAgentIds ?? System.Array.Empty<int>())
            if (byId.TryGetValue(id, out var p))
            {
                p.IsFreeAgent = true;
                season.FreeAgents.Add(p);
            }

        foreach (var a in dto.Awards ?? new List<AwardDto>())
            season.Annals.Awards.Add(new AwardWin
            {
                Year = a.Y, Award = a.A, PlayerId = a.Pid, PlayerName = a.Who,
                TeamId = a.T, Line = a.L,
            });

        foreach (var h in dto.Hall ?? new List<HallDto>())
            season.Annals.Hall.Add(new HallOfFamer
            {
                Year = h.Y, Name = h.Who, Position = h.Pos, Career = h.Career, Score = h.Score,
            });

        foreach (var r in dto.Records ?? new List<RecordDto>())
            season.Annals.Records[r.S] = new RecordMark
            {
                Stat = r.S, Value = r.V, Year = r.Y, PlayerName = r.Who, TeamId = r.T,
            };

        // A league saved before there was an economy has nobody under contract, so everyone is
        // given the deal his age and ability imply rather than opening the books at zero.
        var money = new Core.Rng(dto.Seed * 2971 + 401);
        foreach (var r in season.AllRosters)
            foreach (var p in r.Players)
                Contracts.Establish(p, ref money);

        // Likewise a save from before the farm system exists has no affiliates at all.
        if (Teams.All.All(t => Farm.Of(t.Id).Count == 0))
        {
            Farm.Stock(season, dto.Seed);
            Farm.PlaySeason(season, season.GamesPerTeam, dto.Seed + 211);
        }
        else
        {
            // A save written while only Triple-A was being stored comes back with the lower two
            // rungs empty. Checking the whole organisation rather than one level of it means an
            // existing league repairs itself on load instead of showing two empty affiliates
            // forever — and any rung short of a fieldable side is topped back up, which matters
            // now that a farm game has to be able to put nine men out there.
            var refill = new Core.Rng(dto.Seed * 4409 + season.Year * 17 + 83);

            foreach (var t in Teams.All)
                foreach (var level in Farm.Levels)
                {
                    var farm = Farm.Of(t.Id, level);
                    int want = Farm.SizeOf(level);
                    if (farm.Count >= want) continue;

                    while (farm.Count < want)
                    {
                        bool needArm =
                            farm.Count(p => p.Position == Data.Position.P) < Farm.ArmsAt(level) - 2;

                        var p = RosterGenerator.Prospect(
                            400000 + t.Id * 1000 + (int)level * 91 + farm.Count,
                            ref refill, needArm ? Data.Position.P : Farm.Missing(farm));

                        p.Salary = Contracts.Minimum;
                        p.ContractYears = 1;
                        p.Age = level switch
                        {
                            Farm.Level.TripleA => refill.Range(22, 27),
                            Farm.Level.DoubleA => refill.Range(20, 25),
                            _ => refill.Range(18, 22),
                        };

                        farm.Add(p);
                    }
                }
        }

        Inbox.Import(dto.MailFrom, dto.MailSubject, dto.MailBody, dto.MailDay, dto.MailYear,
            dto.MailRead, dto.MailAbout);

        // A league that existed before there was an inbox has no correspondence, and would have
        // none until the calendar rolled into a new year — an empty screen for a whole season.
        // Seed it: the owner states his expectations and the scouts write up the best man in the
        // system, both of which are true of the league as it stands right now.
        if (Inbox.Messages.Count == 0)
        {
            Inbox.OpeningDay(season);
            Inbox.ScoutingReport(season);
        }

        // Restore the calendar. A save written before there was one has no day recorded, so the
        // date is inferred from how far the schedule has actually got — otherwise a season five
        // games deep would reopen on opening day.
        season.CurrentDay = dto.DayPlusOne > 0
            ? dto.DayPlusOne - 1
            : season.Games.Where(g => g.Played).Select(g => g.Day + 1).DefaultIfEmpty(0).Max();

        return season;
    }

    private static PlayerDto ToDto(PlayerData p, StatBook book)
    {
        var b = book.Batting(p);
        var t = book.Pitching(p);
        return new PlayerDto
        {
            Id = p.Id, First = p.FirstName, Last = p.LastName, Number = p.Number,
            Pos = (int)p.Position, Bats = (int)p.Bats, Throws = (int)p.Throws,
            Special = (int)p.Special, Arch = (int)p.Archetype,
            Age = p.Age, Pot = p.Potential, Rep = p.Repertoire, Legend = p.LegendId + 1,
            Con = p.Contact, Pow = p.Power, Spd = p.Speed, Arm = p.Arm, Fld = p.Fielding,
            PPow = p.PitchPower, PCon = p.PitchControl, Sta = p.Stamina, Look = p.LookSeed,
            Sal = p.Salary, CYr = p.ContractYears, Svc = p.ServiceYears, Role = (int)p.Role,
            Seasons = book.SeasonsPlayed(p),
            Car = Pack(book.CareerBatting(p), book.CareerPitching(p)),
            Min = book.HasMinorLine(p) ? Pack(book.MinorBatting(p), book.MinorPitching(p)) : null,
            Bat = PackBatting(b),
            Pit = PackPitching(t),
        };
    }

    // The stat arrays grew when the line did: the sacrifices, the hit-by-pitch, the caught
    // stealing and the bullpen's holds and blown saves all had to go somewhere. New fields are
    // appended and every reader takes a minimum length rather than an exact one, so a save
    // written before them still loads and simply carries zeroes in the new columns. An exact
    // length check here would have silently binned every stat in every existing league.
    private const int BattingFields = 18;
    private const int PitchingFields = 22;

    private static int[] PackBatting(Stats.BattingLine b) => new[]
    {
        b.Games, b.PlateAppearances, b.AtBats, b.Hits, b.Doubles, b.Triples,
        b.HomeRuns, b.Runs, b.RunsBattedIn, b.Walks, b.Strikeouts, b.StolenBases,
        b.HitByPitch, b.IntentionalWalks, b.CaughtStealing,
        b.SacrificeFlies, b.SacrificeBunts, b.GroundedIntoDoublePlay,
    };

    private static int[] PackPitching(Stats.PitchingLine t) => new[]
    {
        t.Games, t.GamesStarted, t.Outs, t.Hits, t.Runs, t.EarnedRuns, t.Walks,
        t.Strikeouts, t.HomeRunsAllowed, t.Wins, t.Losses, t.Saves, t.Pitches,
        t.HitBatters, t.IntentionalWalksIssued, t.WildPitches, t.BattersFaced,
        t.Holds, t.BlownSaves, t.CompleteGames, t.Shutouts, t.QualityStarts,
    };

    private static void UnpackBatting(int[] v, Stats.BattingLine b, int at = 0)
    {
        if (v == null || v.Length < at + 12) return;
        b.Games = v[at]; b.PlateAppearances = v[at + 1]; b.AtBats = v[at + 2]; b.Hits = v[at + 3];
        b.Doubles = v[at + 4]; b.Triples = v[at + 5]; b.HomeRuns = v[at + 6]; b.Runs = v[at + 7];
        b.RunsBattedIn = v[at + 8]; b.Walks = v[at + 9]; b.Strikeouts = v[at + 10];
        b.StolenBases = v[at + 11];

        if (v.Length < at + BattingFields) return;
        b.HitByPitch = v[at + 12]; b.IntentionalWalks = v[at + 13]; b.CaughtStealing = v[at + 14];
        b.SacrificeFlies = v[at + 15]; b.SacrificeBunts = v[at + 16];
        b.GroundedIntoDoublePlay = v[at + 17];
    }

    private static void UnpackPitching(int[] v, Stats.PitchingLine t, int at = 0)
    {
        if (v == null || v.Length < at + 13) return;
        t.Games = v[at]; t.GamesStarted = v[at + 1]; t.Outs = v[at + 2]; t.Hits = v[at + 3];
        t.Runs = v[at + 4]; t.EarnedRuns = v[at + 5]; t.Walks = v[at + 6];
        t.Strikeouts = v[at + 7]; t.HomeRunsAllowed = v[at + 8]; t.Wins = v[at + 9];
        t.Losses = v[at + 10]; t.Saves = v[at + 11]; t.Pitches = v[at + 12];

        if (v.Length < at + PitchingFields) return;
        t.HitBatters = v[at + 13]; t.IntentionalWalksIssued = v[at + 14];
        t.WildPitches = v[at + 15]; t.BattersFaced = v[at + 16];
        t.Holds = v[at + 17]; t.BlownSaves = v[at + 18]; t.CompleteGames = v[at + 19];
        t.Shutouts = v[at + 20]; t.QualityStarts = v[at + 21];
    }

    /// <summary>A batting line and a pitching line back to back, for the career and minor totals.</summary>
    private static int[] Pack(Stats.BattingLine b, Stats.PitchingLine t)
    {
        var joined = new int[BattingFields + PitchingFields];
        PackBatting(b).CopyTo(joined, 0);
        PackPitching(t).CopyTo(joined, BattingFields);
        return joined;
    }

    private static void Unpack(int[] v, Stats.BattingLine b, Stats.PitchingLine t)
    {
        if (v == null) return;

        // A save written before the line grew packs twelve batting fields then thirteen pitching
        // ones; a current save packs eighteen then twenty-two. The join moves, so it is read from
        // the length rather than assumed.
        int split = v.Length >= BattingFields + PitchingFields ? BattingFields : 12;
        UnpackBatting(v, b);
        UnpackPitching(v, t, split);
    }

    private static PlayerData FromDto(PlayerDto d) => new()
    {
        Id = d.Id, FirstName = d.First, LastName = d.Last, Number = d.Number,
        Position = (Data.Position)d.Pos, Bats = (Handedness)d.Bats, Throws = (Handedness)d.Throws,
        Special = (Special)d.Special, Archetype = (Archetype)d.Arch,
        Age = d.Age <= 0 ? 14 : d.Age, Potential = d.Pot <= 0 ? 5 : d.Pot,
        Repertoire = d.Rep == 0 ? 0b1111 : d.Rep,
        Contact = d.Con, Power = d.Pow, Speed = d.Spd, Arm = d.Arm, Fielding = d.Fld,
        PitchPower = d.PPow, PitchControl = d.PCon, Stamina = d.Sta, LookSeed = d.Look,
        LegendId = d.Legend - 1,
        Salary = d.Sal, ContractYears = d.CYr, ServiceYears = d.Svc,
        Role = (StaffRole)Mathf.Clamp(d.Role, 0, 4),
    };

    private static void ApplyStats(StatBook book, PlayerData p, PlayerDto d)
    {
        if (d.Bat is { Length: >= 12 }) UnpackBatting(d.Bat, book.Batting(p));
        if (d.Pit is { Length: >= 13 }) UnpackPitching(d.Pit, book.Pitching(p));

        // Career totals used to be thrown away on every reload: a ten-year veteran came back a
        // rookie, which also meant nobody could ever be judged for the hall of fame.
        Unpack(d.Car, book.CareerBatting(p), book.CareerPitching(p));
        if (d.Min != null) Unpack(d.Min, book.MinorBatting(p), book.MinorPitching(p));
        book.SetSeasonsPlayed(p, d.Seasons);
    }
}
