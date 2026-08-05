using System.Linq;
using System.Text;
using SandlotSlugfest.Data;
using SandlotSlugfest.Season;

namespace SandlotSlugfest.Net;

/// <summary>
/// A checksum of a whole league, and the first thing an online league needs.
///
/// Netplay works because two machines build the identical game from a shared seed and exchange
/// only what each human decides. That trick generalises from one game to a whole season — every
/// simulated game is deterministic from the league seed — but it generalises with one nasty
/// difference. A desync inside a single game costs you that game and you find out at once. A
/// desync on the third of April costs you the season, and you find out in August when the two
/// machines disagree about who is in first place.
///
/// So before any of the rest of it: a fingerprint the two sides can compare every day, and a
/// harness that proves the season is replayable at all. If two leagues built from one seed do not
/// match, an online league is impossible until they do, and it is far better to know that from a
/// checksum than from a player's ruined dynasty.
///
/// Everything folded in here is iterated in a fixed order — clubs by id, players by id — because
/// a hash over a dictionary's natural order is a hash of the runtime's mood.
/// </summary>
public static class LeagueFingerprint
{
    private const ulong Offset = 14695981039346656037;
    private const ulong Prime = 1099511628211;

    private static ulong Fold(ulong hash, long value)
    {
        unchecked
        {
            for (int i = 0; i < 8; i++)
            {
                hash ^= (ulong)((value >> (i * 8)) & 0xFF);
                hash *= Prime;
            }
        }
        return hash;
    }

    /// <summary>The whole league in one number.</summary>
    public static ulong Of(SeasonState season)
    {
        if (season == null) return 0;

        ulong h = Offset;
        h = Fold(h, season.CurrentDay);
        h = Fold(h, season.Year);
        h = Fold(h, season.GamesPlayed);
        h = Fold(h, season.LeagueSeed);

        foreach (var team in Teams.All.OrderBy(t => t.Id))
        {
            var rec = season.Book.Record(team.Id);
            h = Fold(h, team.Id);
            h = Fold(h, rec.Wins);
            h = Fold(h, rec.Losses);
            h = Fold(h, rec.RunsScored);
            h = Fold(h, rec.RunsAllowed);

            // The roster itself, in id order: who is on it and what he has done. A trade that
            // happened on one machine and not the other shows up here on the day it happens.
            foreach (var p in season.RosterFor(team.Id).Players.OrderBy(p => p.Id))
            {
                var b = season.Book.Batting(p);
                var t = season.Book.Pitching(p);
                h = Fold(h, p.Id);
                h = Fold(h, p.Overall);
                h = Fold(h, p.DaysOut);
                h = Fold(h, b.AtBats); h = Fold(h, b.Hits); h = Fold(h, b.HomeRuns);
                h = Fold(h, b.RunsBattedIn); h = Fold(h, b.Walks); h = Fold(h, b.Strikeouts);
                h = Fold(h, t.Outs); h = Fold(h, t.EarnedRuns); h = Fold(h, t.Strikeouts);
            }
        }

        return h;
    }

    /// <summary>
    /// Where two leagues actually differ, once the numbers say they do. A checksum tells you
    /// something is wrong and nothing else; this says what, which is the difference between a
    /// bug you can fix and a bug you can only observe.
    /// </summary>
    public static string Diff(SeasonState a, SeasonState b, int limit = 12)
    {
        if (a == null || b == null) return "one of the two leagues is missing";

        var found = new StringBuilder();
        int shown = 0;

        if (a.CurrentDay != b.CurrentDay)
            found.AppendLine($"    day {a.CurrentDay} vs {b.CurrentDay}");
        if (a.GamesPlayed != b.GamesPlayed)
            found.AppendLine($"    games played {a.GamesPlayed} vs {b.GamesPlayed}");

        foreach (var team in Teams.All.OrderBy(t => t.Id))
        {
            if (shown >= limit) break;

            var ra = a.Book.Record(team.Id);
            var rb = b.Book.Record(team.Id);
            if (ra.Wins == rb.Wins && ra.Losses == rb.Losses &&
                ra.RunsScored == rb.RunsScored) continue;

            found.AppendLine($"    {team.Abbrev}  {ra.Wins}-{ra.Losses} ({ra.RunsScored} rs)" +
                             $"  vs  {rb.Wins}-{rb.Losses} ({rb.RunsScored} rs)");
            shown++;
        }

        return found.Length == 0 ? "    identical on standings; the difference is in a player line"
                                 : found.ToString().TrimEnd();
    }
}
