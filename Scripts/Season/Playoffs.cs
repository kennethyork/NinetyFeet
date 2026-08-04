using System.Collections.Generic;
using System.Linq;
using SandlotSlugfest.Data;
using SandlotSlugfest.Stats;

namespace SandlotSlugfest.Season;

/// <summary>A best-of series between two seeded clubs.</summary>
public sealed class PlayoffSeries
{
    public string Round;
    public int HighSeedId = -1;
    public int LowSeedId = -1;
    public int HighWins;
    public int LowWins;
    public int BestOf = 7;

    public bool Ready => HighSeedId >= 0 && LowSeedId >= 0;
    public int NeededToWin => BestOf / 2 + 1;
    public bool Complete => HighWins >= NeededToWin || LowWins >= NeededToWin;

    public int WinnerId => !Complete ? -1 : HighWins > LowWins ? HighSeedId : LowSeedId;
    public int LoserId => !Complete ? -1 : HighWins > LowWins ? LowSeedId : HighSeedId;

    public bool Involves(int teamId) => HighSeedId == teamId || LowSeedId == teamId;

    /// <summary>The higher seed hosts games 1, 2, 5 and 7.</summary>
    public bool HighSeedHosts(int gameNumber) => gameNumber is 1 or 2 or 5 or 7;

    public int GamesPlayed => HighWins + LowWins;

    public string Line
    {
        get
        {
            if (!Ready) return $"{Round}: to be decided";
            string hi = Teams.Get(HighSeedId).Abbrev;
            string lo = Teams.Get(LowSeedId).Abbrev;
            return $"{Round}: {hi} {HighWins} — {LowWins} {lo}";
        }
    }
}

/// <summary>
/// The postseason bracket. Four clubs qualify from each league: the two division winners, then
/// the two best records left over as wild cards. Seeds are one to four, one hosts four.
/// </summary>
public sealed class PlayoffBracket
{
    public readonly List<PlayoffSeries> Series = new();
    public int ChampionId = -1;

    public bool Started => Series.Count > 0;
    public bool Finished => ChampionId >= 0;

    /// <summary>Seeds a league's four qualifiers: division winners first, then best records.</summary>
    public static List<int> Seed(SeasonState season, League league)
    {
        var winners = new List<int>();
        foreach (var division in new[] { Division.East, Division.West })
        {
            var top = season.Standings(league, division).FirstOrDefault();
            if (top.Team != null) winners.Add(top.Team.Id);
        }

        // Division winners are seeded above everyone, ordered by record between themselves.
        winners = winners
            .OrderByDescending(id => season.Book.Record(id).WinPct)
            .ThenByDescending(id => season.Book.Record(id).RunDifferential)
            .ToList();

        var wildCards = Teams.In(league)
            .Where(t => !winners.Contains(t.Id))
            .OrderByDescending(t => season.Book.Record(t.Id).WinPct)
            .ThenByDescending(t => season.Book.Record(t.Id).RunDifferential)
            .Take(2)
            .Select(t => t.Id)
            .ToList();

        winners.AddRange(wildCards);
        return winners;
    }

    public void Build(SeasonState season)
    {
        Series.Clear();
        ChampionId = -1;

        var al = Seed(season, League.American);
        var nl = Seed(season, League.National);
        if (al.Count < 4 || nl.Count < 4) return;

        // Round one: one hosts four, two hosts three, in each league.
        Series.Add(new PlayoffSeries { Round = "AL Semifinal", HighSeedId = al[0], LowSeedId = al[3], BestOf = 5 });
        Series.Add(new PlayoffSeries { Round = "AL Semifinal", HighSeedId = al[1], LowSeedId = al[2], BestOf = 5 });
        Series.Add(new PlayoffSeries { Round = "NL Semifinal", HighSeedId = nl[0], LowSeedId = nl[3], BestOf = 5 });
        Series.Add(new PlayoffSeries { Round = "NL Semifinal", HighSeedId = nl[1], LowSeedId = nl[2], BestOf = 5 });

        Series.Add(new PlayoffSeries { Round = "AL Championship", BestOf = 7 });
        Series.Add(new PlayoffSeries { Round = "NL Championship", BestOf = 7 });
        Series.Add(new PlayoffSeries { Round = "Sandlot Series", BestOf = 7 });
    }

    /// <summary>Feeds completed series into the next round and crowns a champion at the end.</summary>
    public void Advance(SeasonState season)
    {
        if (Series.Count < 7) return;

        // Semifinal winners meet in their league championship, better seed hosting.
        FillFrom(4, Series[0], Series[1], season);
        FillFrom(5, Series[2], Series[3], season);

        if (Series[4].Complete && Series[5].Complete && !Series[6].Ready)
        {
            int a = Series[4].WinnerId;
            int b = Series[5].WinnerId;
            bool aHigher = season.Book.Record(a).WinPct >= season.Book.Record(b).WinPct;
            Series[6].HighSeedId = aHigher ? a : b;
            Series[6].LowSeedId = aHigher ? b : a;
        }

        if (Series[6].Complete) ChampionId = Series[6].WinnerId;
    }

    private void FillFrom(int index, PlayoffSeries a, PlayoffSeries b, SeasonState season)
    {
        if (Series[index].Ready || !a.Complete || !b.Complete) return;
        int x = a.WinnerId, y = b.WinnerId;
        bool xHigher = season.Book.Record(x).WinPct >= season.Book.Record(y).WinPct;
        Series[index].HighSeedId = xHigher ? x : y;
        Series[index].LowSeedId = xHigher ? y : x;
    }

    /// <summary>The next series that still needs games, or null when the postseason is done.</summary>
    public PlayoffSeries NextLive() =>
        Series.FirstOrDefault(s => s.Ready && !s.Complete);

    /// <summary>Records one game's result into the series the two clubs are playing.</summary>
    public void RecordGame(PlayoffSeries series, int winnerId)
    {
        if (series == null || series.Complete) return;
        if (winnerId == series.HighSeedId) series.HighWins++;
        else series.LowWins++;
    }
}
