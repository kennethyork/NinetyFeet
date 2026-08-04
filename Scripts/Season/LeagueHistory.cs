using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.Stats;

namespace SandlotSlugfest.Season;

/// <summary>One trophy, handed to one player, in one year.</summary>
public sealed class AwardWin
{
    public int Year;
    public string Award;         // "AL Most Valuable Player"
    public int PlayerId;
    public string PlayerName;
    public int TeamId;
    public string Line;          // the season line that won it

    public override string ToString() => $"{Year}  {Award}: {PlayerName} ({Teams.Get(TeamId).Abbrev})";
}

/// <summary>A single-season mark that stood at the time it was set.</summary>
public sealed class RecordMark
{
    public string Stat;
    public float Value;
    public int Year;
    public string PlayerName;
    public int TeamId;
}

/// <summary>A player whose career is over and who is now judged on all of it.</summary>
public sealed class HallOfFamer
{
    public int Year;
    public string Name;
    public string Position;
    public string Career;
    public int Score;
}

/// <summary>
/// What the league remembers.
///
/// A franchise's tenth season should not feel like its first. Before this the only thing carried
/// forward was a list of champions — no MVP, no Cy Young, no leaderboard that survived the winter,
/// nobody in a hall of fame, no record to chase. The numbers were all already being kept; nothing
/// was ever asked of them.
/// </summary>
public sealed class LeagueHistory
{
    public readonly List<AwardWin> Awards = new();
    public readonly List<HallOfFamer> Hall = new();
    public readonly Dictionary<string, RecordMark> Records = new();

    public IEnumerable<AwardWin> In(int year) => Awards.Where(a => a.Year == year);

    public IEnumerable<AwardWin> For(PlayerData p) => Awards.Where(a => a.PlayerId == p.Id);

    /// <summary>Trophies this player has won, most recent first, for his card.</summary>
    public string HonoursText(PlayerData p)
    {
        var mine = For(p).OrderByDescending(a => a.Year).ToList();
        if (mine.Count == 0) return "";
        return string.Join("  ", mine.Take(4).Select(a => $"{a.Year} {Short(a.Award)}"));
    }

    private static string Short(string award) => award
        .Replace("Most Valuable Player", "MVP")
        .Replace("Pitcher of the Year", "CY")
        .Replace("Rookie of the Year", "ROY")
        .Replace("American League ", "AL ")
        .Replace("National League ", "NL ");

    /// <summary>Notes a new single-season best, if it beats what is on the books.</summary>
    public bool Consider(string stat, float value, int year, PlayerData p, int teamId,
        bool lowerIsBetter = false)
    {
        if (Records.TryGetValue(stat, out var held))
        {
            bool better = lowerIsBetter ? value < held.Value : value > held.Value;
            if (!better) return false;
        }

        Records[stat] = new RecordMark
        {
            Stat = stat, Value = value, Year = year, PlayerName = p.Name, TeamId = teamId,
        };
        return true;
    }
}

/// <summary>
/// Picks the league's trophies at the end of a season, and votes on the hall of fame.
///
/// The voting is deliberately explainable — one number per candidate, built from the things
/// people actually argue about — because an award nobody can account for reads as random.
/// </summary>
public static class Awards
{
    /// <summary>A hitter needs this many plate appearances to be eligible, a pitcher this many outs.</summary>
    public static int MinPlateAppearances(int games) => Mathf.RoundToInt(games * 3.1f);
    public static int MinOuts(int games) => Mathf.RoundToInt(games * 2.4f);

    /// <summary>
    /// A hitter's case, on the shape of a real ballot: getting on base and hitting for power,
    /// weighted by how much he played and helped by playing for a winner.
    /// </summary>
    private static float HitterScore(SeasonState season, PlayerData p, BattingLine b)
    {
        var team = season.TeamOf(p);
        float winning = team == null ? 1f : 0.86f + season.Book.Record(team.Id).WinPct * 0.28f;

        float rate = b.OnBase * 1.8f + b.Slugging;
        float volume = Mathf.Min(1f, b.PlateAppearances / (season.GamesPerTeam * 4.0f));
        return rate * (0.55f + volume * 0.45f) * winning * 100f;
    }

    private static float PitcherScore(SeasonState season, PlayerData p, PitchingLine t)
    {
        var team = season.TeamOf(p);
        float winning = team == null ? 1f : 0.90f + season.Book.Record(team.Id).WinPct * 0.20f;

        // Runs prevented against a league-average arm, scaled by how many innings he did it over.
        float era = t.Era <= 0.01f ? 9f : t.Era;
        float prevented = Mathf.Max(0f, (RealBaseball.MlbEra + 0.9f) - era);
        float innings = t.InningsPitched;
        float strikeouts = t.Strikeouts / Mathf.Max(innings, 1f) * 0.9f;

        return (prevented * innings * 0.36f + strikeouts * 5.5f + t.Saves * 1.1f) * winning;
    }

    /// <summary>
    /// Hands out the year's trophies. Called once the postseason is settled and before the book
    /// is closed, because it reads the season that has just finished.
    /// </summary>
    public static List<AwardWin> Decide(SeasonState season)
    {
        var handed = new List<AwardWin>();
        int minPa = MinPlateAppearances(season.GamesPerTeam);
        int minOuts = MinOuts(season.GamesPerTeam);

        foreach (var league in new[] { League.American, League.National })
        {
            string prefix = league == League.American ? "American League" : "National League";
            var clubs = Teams.In(league).Select(t => t.Id).ToHashSet();

            bool Mine(PlayerData p)
            {
                var t = season.TeamOf(p);
                return t != null && clubs.Contains(t.Id);
            }

            var hitters = season.Book.QualifiedHitters(minPa).Where(x => Mine(x.Player)).ToList();
            var arms = season.Book.QualifiedPitchers(minOuts).Where(x => Mine(x.Player)).ToList();

            Give(season, handed, $"{prefix} Most Valuable Player", hitters
                .OrderByDescending(x => HitterScore(season, x.Player, x.Line))
                .Select(x => (x.Player, Line: BattingText(x.Line)))
                .FirstOrDefault());

            Give(season, handed, $"{prefix} Pitcher of the Year", arms
                .OrderByDescending(x => PitcherScore(season, x.Player, x.Line))
                .Select(x => (x.Player, Line: PitchingText(x.Line)))
                .FirstOrDefault());

            // A rookie is a player in his first season of real playing time.
            var rookieBats = hitters
                .Where(x => season.Book.SeasonsPlayed(x.Player) == 0 && x.Player.ServiceYears <= 1)
                .Select(x => (x.Player, Score: HitterScore(season, x.Player, x.Line),
                    Line: BattingText(x.Line)));
            var rookieArms = arms
                .Where(x => season.Book.SeasonsPlayed(x.Player) == 0 && x.Player.ServiceYears <= 1)
                .Select(x => (x.Player, Score: PitcherScore(season, x.Player, x.Line) * 0.85f,
                    Line: PitchingText(x.Line)));

            var rookie = rookieBats.Concat(rookieArms).OrderByDescending(x => x.Score).FirstOrDefault();
            if (rookie.Player != null)
                Give(season, handed, $"{prefix} Rookie of the Year", (rookie.Player, rookie.Line));

            // A glove award for the best defender who played every day.
            var glove = hitters
                .Where(x => x.Line.Games >= season.GamesPerTeam * 0.6f)
                .OrderByDescending(x => x.Player.Fielding * 2 + x.Player.Arm +
                                        (x.Player.Special == Special.VacuumGlove ? 4 : 0))
                .Select(x => (x.Player, Line: $"{PlayerData.PositionLabel(x.Player.Position)}, " +
                                              $"fielding {x.Player.Fielding}/10"))
                .FirstOrDefault();
            Give(season, handed, $"{prefix} Defender of the Year", glove);
        }

        NoteRecords(season);
        season.Annals.Awards.AddRange(handed);
        return handed;
    }

    private static void Give(SeasonState season, List<AwardWin> into, string award,
        (PlayerData Player, string Line) winner)
    {
        if (winner.Player == null) return;
        var team = season.TeamOf(winner.Player);

        into.Add(new AwardWin
        {
            Year = season.Year,
            Award = award,
            PlayerId = winner.Player.Id,
            PlayerName = winner.Player.Name,
            TeamId = team?.Id ?? 0,
            Line = winner.Line,
        });
    }

    private static string BattingText(BattingLine b) =>
        $"{BattingLine.Rate(b.Average)}/{BattingLine.Rate(b.OnBase)}/{BattingLine.Rate(b.Slugging)}, " +
        $"{b.HomeRuns} HR, {b.RunsBattedIn} RBI";

    private static string PitchingText(PitchingLine t) =>
        $"{t.Wins}-{t.Losses}, {t.Era:F2} ERA, {t.Strikeouts} K in {t.InningsText} IP";

    /// <summary>Checks the season's leaders against the all-time single-season marks.</summary>
    private static void NoteRecords(SeasonState season)
    {
        int minPa = MinPlateAppearances(season.GamesPerTeam);
        int minOuts = MinOuts(season.GamesPerTeam);
        var annals = season.Annals;

        foreach (var (p, b) in season.Book.QualifiedHitters(1))
        {
            int teamId = season.TeamOf(p)?.Id ?? 0;
            annals.Consider("Home runs", b.HomeRuns, season.Year, p, teamId);
            annals.Consider("Runs batted in", b.RunsBattedIn, season.Year, p, teamId);
            annals.Consider("Hits", b.Hits, season.Year, p, teamId);
            annals.Consider("Stolen bases", b.StolenBases, season.Year, p, teamId);
            if (b.PlateAppearances >= minPa)
                annals.Consider("Batting average", b.Average, season.Year, p, teamId);
        }

        foreach (var (p, t) in season.Book.QualifiedPitchers(1))
        {
            int teamId = season.TeamOf(p)?.Id ?? 0;
            annals.Consider("Strikeouts", t.Strikeouts, season.Year, p, teamId);
            annals.Consider("Wins", t.Wins, season.Year, p, teamId);
            annals.Consider("Saves", t.Saves, season.Year, p, teamId);
            if (t.Outs >= minOuts)
                annals.Consider("Earned run average", t.Era, season.Year, p, teamId, lowerIsBetter: true);
        }
    }

    /// <summary>
    /// Weighs a finished career for the hall of fame. Longevity alone is not enough and one huge
    /// year is not enough; it takes both, and trophies count for something the numbers miss.
    /// </summary>
    public static int HallScore(SeasonState season, PlayerData p)
    {
        var b = season.Book.CareerBatting(p);
        var t = season.Book.CareerPitching(p);
        int trophies = season.Annals.For(p).Count();

        float score = trophies * 26f;

        if (t.Outs > b.AtBats)
        {
            score += t.InningsPitched * 0.10f;
            score += t.Strikeouts * 0.030f;
            score += t.Wins * 0.85f;
            score += t.Saves * 0.55f;
            if (t.Outs > 2000 && t.Era < 3.60f) score += (3.60f - t.Era) * 34f;
        }
        else
        {
            score += b.Hits * 0.055f;
            score += b.HomeRuns * 0.20f;
            score += b.RunsBattedIn * 0.045f;
            score += b.StolenBases * 0.045f;
            if (b.AtBats > 3000) score += (b.Average - 0.260f) * 340f;
        }

        return Mathf.RoundToInt(score);
    }

    /// <summary>The bar for induction. Set so a good long career falls short and a great one clears.</summary>
    public const int HallThreshold = 118;

    /// <summary>Considers a retiring player, and enshrines him if his career earned it.</summary>
    public static HallOfFamer ConsiderForHall(SeasonState season, PlayerData p)
    {
        int score = HallScore(season, p);
        if (score < HallThreshold) return null;

        var b = season.Book.CareerBatting(p);
        var t = season.Book.CareerPitching(p);
        bool arm = t.Outs > b.AtBats;

        var entry = new HallOfFamer
        {
            Year = season.Year,
            Name = p.Name,
            Position = PlayerData.PositionLabel(p.Position),
            Score = score,
            Career = arm
                ? $"{t.Wins}-{t.Losses}, {t.Era:F2} ERA, {t.Strikeouts} K over {t.InningsPitched:F0} innings"
                : $"{b.Hits} hits, {b.HomeRuns} HR, {BattingLine.Rate(b.Average)} average",
        };

        season.Annals.Hall.Add(entry);
        return entry;
    }
}
