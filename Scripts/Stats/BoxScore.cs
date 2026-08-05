using System.Collections.Generic;
using System.Linq;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Stats;

/// <summary>
/// One man's night, kept by identity rather than by reference.
///
/// The name and club are copied in on purpose. A box score is a record of what happened on a
/// date, and a player who is traded in July must not retroactively appear to have played that
/// game for his new club.
/// </summary>
public sealed class Appearance
{
    public int PlayerId;
    public string Name = "";
    public string Slot = "";
    public int TeamId;
    public bool Pitched;

    /// <summary>The packed line — batting or pitching depending on <see cref="Pitched"/>.</summary>
    public BattingLine Batting;
    public PitchingLine Pitching;
}

/// <summary>
/// A finished game, written down.
///
/// The simulation played thousands of games and kept none of them. A result was a pair of
/// numbers on a schedule row: you could see that you lost 5-2 on the fourteenth of June and
/// there was no way, ever, to find out who had pitched. Season totals answer what a player has
/// done; only a box score answers what he did on a night, and only a run of box scores makes a
/// hot streak something you can look at rather than something you feel.
/// </summary>
public sealed class BoxScore
{
    public int Day;
    public int Year;
    public int AwayId;
    public int HomeId;

    /// <summary>Runs by inning, the way a line score reads.</summary>
    public int[] AwayInnings = System.Array.Empty<int>();
    public int[] HomeInnings = System.Array.Empty<int>();

    public int AwayRuns, HomeRuns, AwayHits, HomeHits, AwayErrors, HomeErrors;

    public readonly List<Appearance> Lines = new();

    /// <summary>Who took the decision. -1 when nobody did.</summary>
    public int WinnerPlayerId = -1;
    public int LoserPlayerId = -1;
    public int SavePlayerId = -1;

    public string Note = "";

    public int WinningTeamId => AwayRuns > HomeRuns ? AwayId : HomeId;
    public bool Involves(int teamId) => AwayId == teamId || HomeId == teamId;

    public IEnumerable<Appearance> Batters(int teamId) =>
        Lines.Where(a => a.TeamId == teamId && !a.Pitched);

    public IEnumerable<Appearance> Arms(int teamId) =>
        Lines.Where(a => a.TeamId == teamId && a.Pitched);

    public Appearance Of(int playerId) => Lines.FirstOrDefault(a => a.PlayerId == playerId);

    /// <summary>
    /// Builds the record from a finished game's own book.
    ///
    /// Only players who actually appeared are written down — the book only holds lines for men
    /// who came to the plate or took the ball, so an unused bench is simply absent, which is how
    /// a real box score reads too.
    /// </summary>
    public static BoxScore From(int day, int year, Roster away, Roster home,
        StatBook game, int[] awayInnings, int[] homeInnings,
        int awayHits, int homeHits, int awayErrors, int homeErrors, string note)
    {
        var box = new BoxScore
        {
            Day = day,
            Year = year,
            AwayId = away.Team.Id,
            HomeId = home.Team.Id,
            AwayInnings = awayInnings ?? System.Array.Empty<int>(),
            HomeInnings = homeInnings ?? System.Array.Empty<int>(),
            AwayRuns = awayInnings?.Sum() ?? 0,
            HomeRuns = homeInnings?.Sum() ?? 0,
            AwayHits = awayHits,
            HomeHits = homeHits,
            AwayErrors = awayErrors,
            HomeErrors = homeErrors,
            Note = note ?? "",
        };

        int ClubOf(PlayerData p) =>
            away.Players.Contains(p) ? away.Team.Id
            : home.Players.Contains(p) ? home.Team.Id
            : -1;

        foreach (var (player, line) in game.AllBatting)
        {
            if (line.PlateAppearances == 0) continue;
            int club = ClubOf(player);
            if (club < 0) continue;

            var copy = new BattingLine();
            copy.Absorb(line);
            box.Lines.Add(new Appearance
            {
                PlayerId = player.Id, Name = player.Name, TeamId = club,
                Slot = player.PositionText, Pitched = false, Batting = copy,
            });
        }

        foreach (var (player, line) in game.AllPitching)
        {
            if (line.Outs == 0 && line.BattersFaced == 0) continue;
            int club = ClubOf(player);
            if (club < 0) continue;

            var copy = new PitchingLine();
            copy.Absorb(line);
            box.Lines.Add(new Appearance
            {
                PlayerId = player.Id, Name = player.Name, TeamId = club,
                Slot = PlayerData.RoleLabel(player.Role), Pitched = true, Pitching = copy,
            });

            if (line.Wins > 0) box.WinnerPlayerId = player.Id;
            if (line.Losses > 0) box.LoserPlayerId = player.Id;
            if (line.Saves > 0) box.SavePlayerId = player.Id;
        }

        return box;
    }
}

/// <summary>
/// The club's book of finished games, and every player's game log inside it.
///
/// Kept for the user's club only, and only for a couple of seasons. Writing a full box score for
/// all sixteen games on every date would be some fifty thousand lines a year for games nobody is
/// ever going to open, and the save has to hold it all.
/// </summary>
public sealed class GameLogs
{
    /// <summary>Newest first.</summary>
    public readonly List<BoxScore> Games = new();

    /// <summary>Two seasons of one club's games. Enough to look back at last year.</summary>
    public const int Keep = 340;

    public void Add(BoxScore box)
    {
        if (box == null) return;
        Games.Insert(0, box);
        if (Games.Count > Keep) Games.RemoveRange(Keep, Games.Count - Keep);
    }

    /// <summary>One player's night-by-night, newest first.</summary>
    public IEnumerable<(BoxScore Game, Appearance Line)> For(int playerId) =>
        Games.Select(g => (Game: g, Line: g.Of(playerId)))
             .Where(x => x.Line != null);

    /// <summary>His last few games, which is what a slump or a streak actually looks like.</summary>
    public List<(BoxScore Game, Appearance Line)> Recent(int playerId, int count) =>
        For(playerId).Take(count).ToList();

    public void Clear() => Games.Clear();
}
