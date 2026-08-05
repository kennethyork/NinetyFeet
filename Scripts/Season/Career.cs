using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.Stats;

namespace SandlotSlugfest.Season;

/// <summary>
/// One player's career, from the bottom of somebody's farm system to wherever he gets to.
///
/// The whole appeal of this mode elsewhere is that you are not the club — you are one man in it,
/// and the organisation makes decisions about you rather than the other way round. You do not pick
/// where you play. You are told, and the only argument you get to make is what you do at the plate.
///
/// Almost none of this is new machinery. The three-rung farm already plays real seasons, the
/// development curve already moves men toward their ceilings, promotions and call-ups already
/// exist, and affiliates can already field a side and play a game. A career is those parts
/// pointed at one player instead of at a club.
/// </summary>
public sealed class CareerState
{
    private const string Path = "user://career.json";

    public string FirstName = "";
    public string LastName = "";
    public Position Position = Position.Center;
    public Handedness Bats = Handedness.Right;

    /// <summary>The organisation that drafted him, and where he currently is.</summary>
    public int TeamId;

    /// <summary>Null once he is on the big club.</summary>
    public Farm.Level? Level = Farm.Level.HighA;

    public int Year = 1;
    public int Age = 19;

    /// <summary>Games played this season, out of a short career-mode season.</summary>
    public int GamesThisYear;

    /// <summary>How long a career season is. Shorter than the league's — this is one man's year.</summary>
    public const int SeasonLength = 40;

    /// <summary>The player himself, kept in the farm system like anybody else.</summary>
    public PlayerData Player;

    /// <summary>Career totals, and this season's.</summary>
    public readonly BattingLine Season = new();
    public readonly BattingLine Career = new();
    public readonly PitchingLine SeasonArm = new();
    public readonly PitchingLine CareerArm = new();

    /// <summary>What the organisation has said about him.</summary>
    public readonly List<string> Journal = new();

    public bool Retired;

    public string Name => $"{FirstName} {LastName}";

    public string Where => Level == null
        ? $"{Teams.Get(TeamId).FullName}"
        : $"{Teams.Get(TeamId).Nickname} ({Farm.Name(Level.Value)})";

    public bool InTheMajors => Level == null;

    public bool IsPitcher => Position == Position.P;

    public void Note(string line)
    {
        Journal.Insert(0, $"Year {Year}: {line}");
        if (Journal.Count > 40) Journal.RemoveAt(Journal.Count - 1);
    }

    // -----------------------------------------------------------------------

    private sealed class Dto
    {
        public string First { get; set; }
        public string Last { get; set; }
        public int Pos { get; set; }
        public int Bats { get; set; }
        public int TeamId { get; set; }
        public int LevelPlusOne { get; set; }   // 0 = the big club
        public int Year { get; set; }
        public int Age { get; set; }
        public int Games { get; set; }
        public bool Retired { get; set; }
        public int[] Ratings { get; set; }
        public int Potential { get; set; }
        public int PlayerId { get; set; }
        public int LookSeed { get; set; }
        public int Repertoire { get; set; }
        public int[] SeasonBat { get; set; }
        public int[] CareerBat { get; set; }
        public string[] Journal { get; set; }
    }

    public static bool Exists() => FileAccess.FileExists(Path);

    public static void Delete()
    {
        if (Exists()) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(Path));
    }

    public void Save()
    {
        var dto = new Dto
        {
            First = FirstName, Last = LastName, Pos = (int)Position, Bats = (int)Bats,
            TeamId = TeamId, LevelPlusOne = Level == null ? 0 : (int)Level.Value + 1,
            Year = Year, Age = Age, Games = GamesThisYear, Retired = Retired,
            Potential = Player?.Potential ?? 6,
            PlayerId = Player?.Id ?? 0,
            LookSeed = Player?.LookSeed ?? 0,
            Repertoire = Player?.Repertoire ?? 0b1111,
            Ratings = Player == null ? null : new[]
            {
                Player.Contact, Player.Power, Player.Speed, Player.Arm, Player.Fielding,
                Player.PitchPower, Player.PitchControl, Player.Stamina,
            },
            SeasonBat = Pack(Season),
            CareerBat = Pack(Career),
            Journal = Journal.ToArray(),
        };

        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(dto));
    }

    public static CareerState Load()
    {
        if (!Exists()) return null;

        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
        if (file == null) return null;

        Dto dto;
        try { dto = JsonSerializer.Deserialize<Dto>(file.GetAsText()); }
        catch (JsonException e)
        {
            GD.PushError($"Career file is corrupt, starting fresh: {e.Message}");
            return null;
        }

        if (dto == null) return null;

        var c = new CareerState
        {
            FirstName = dto.First ?? "",
            LastName = dto.Last ?? "",
            Position = (Position)Mathf.Clamp(dto.Pos, 0, 9),
            Bats = (Handedness)Mathf.Clamp(dto.Bats, 0, 2),
            TeamId = Mathf.Clamp(dto.TeamId, 0, Teams.All.Count - 1),
            Level = dto.LevelPlusOne == 0 ? null : (Farm.Level)(dto.LevelPlusOne - 1),
            Year = Mathf.Max(1, dto.Year),
            Age = Mathf.Max(16, dto.Age),
            GamesThisYear = dto.Games,
            Retired = dto.Retired,
        };

        c.Player = new PlayerData
        {
            Id = dto.PlayerId == 0 ? 990000 : dto.PlayerId,
            FirstName = c.FirstName,
            LastName = c.LastName,
            Position = c.Position,
            Bats = c.Bats,
            Throws = c.Bats == Handedness.Left ? Handedness.Left : Handedness.Right,
            LookSeed = dto.LookSeed,
            Potential = Mathf.Clamp(dto.Potential, 1, 10),
            Age = c.Age,
            Number = 1,
            Repertoire = dto.Repertoire == 0 ? 0b1111 : dto.Repertoire,
            Salary = Contracts.Minimum,
            ContractYears = 1,
        };

        if (dto.Ratings is { Length: >= 8 })
        {
            var r = dto.Ratings;
            c.Player.Contact = r[0]; c.Player.Power = r[1]; c.Player.Speed = r[2];
            c.Player.Arm = r[3]; c.Player.Fielding = r[4]; c.Player.PitchPower = r[5];
            c.Player.PitchControl = r[6]; c.Player.Stamina = r[7];
        }

        Unpack(c.Season, dto.SeasonBat);
        Unpack(c.Career, dto.CareerBat);
        if (dto.Journal != null) c.Journal.AddRange(dto.Journal);

        return c;
    }

    private static int[] Pack(BattingLine b) => new[]
    {
        b.Games, b.PlateAppearances, b.AtBats, b.Hits, b.Doubles, b.Triples, b.HomeRuns,
        b.RunsBattedIn, b.Runs, b.Walks, b.Strikeouts, b.StolenBases,
    };

    private static void Unpack(BattingLine b, int[] v)
    {
        if (v is not { Length: >= 12 }) return;
        b.Games = v[0]; b.PlateAppearances = v[1]; b.AtBats = v[2]; b.Hits = v[3];
        b.Doubles = v[4]; b.Triples = v[5]; b.HomeRuns = v[6]; b.RunsBattedIn = v[7];
        b.Runs = v[8]; b.Walks = v[9]; b.Strikeouts = v[10]; b.StolenBases = v[11];
    }
}
