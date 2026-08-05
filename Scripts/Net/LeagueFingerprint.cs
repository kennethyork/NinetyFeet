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

    private static string Ratings(Data.PlayerData p) =>
        $"pos{(int)p.Position} role{(int)p.Role} con{p.Contact} pow{p.Power} spd{p.Speed} " +
        $"fld{p.Fielding} arm{p.Arm} pp{p.PitchPower} pc{p.PitchControl} sta{p.Stamina}";

    /// <summary>One man's numbers, for saying what differs rather than that something does.</summary>
    private static string Line(SeasonState s, Data.PlayerData p)
    {
        var b = s.Book.Batting(p);
        var t = s.Book.Pitching(p);
        return $"{b.AtBats}/{b.Hits}/{b.HomeRuns}/{b.Walks}/{b.Strikeouts}" +
               $" {t.Outs}/{t.EarnedRuns}/{t.Strikeouts}";
    }

    /// <summary>The nine, by name, for saying which club is sending out somebody else.</summary>
    private static string Card(SeasonState s, int teamId) =>
        string.Join(" ", s.RosterFor(teamId).BattingOrder.Select(p => p?.ShortName ?? "—"));

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

            // The card, not just the names on it.
            //
            // A pinch hitter is permanent — Substitute writes the new man into the batting order
            // and the fielding chart and nothing ever puts the old one back — so two machines
            // whose rosters hold exactly the same men can still send out different nines tomorrow.
            // Without this the fingerprint would call those two leagues identical right up until
            // the moment they produced different results, which is the worst possible place to
            // find out. Order matters here and is the roster's own, so it is folded as it stands.
            var club = season.RosterFor(team.Id);
            foreach (var p in club.BattingOrder) h = Fold(h, p?.Id ?? -1);
            foreach (var spot in club.Starters.Keys.OrderBy(k => (int)k))
                h = Fold(h, (int)spot * 1_000_003L + (club.Starters[spot]?.Id ?? -1));
            foreach (var p in club.Pitchers) h = Fold(h, p?.Id ?? -1);

            // The roster itself, in id order: who is on it and what he has done. A trade that
            // happened on one machine and not the other shows up here on the day it happens.
            foreach (var p in club.Players.OrderBy(p => p.Id))
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

        if (found.Length > 0) return found.ToString().TrimEnd();

        // Standings agree, so walk the rosters and name the first man who does not.
        foreach (var team in Teams.All.OrderBy(t => t.Id))
        {
            if (shown >= limit) break;

            // The lineup card first, because a club can hold the same twenty-six men and still be
            // sending out a different nine — and that difference decides tomorrow's game rather
            // than describing yesterday's.
            string ca = Card(a, team.Id);
            string cb = Card(b, team.Id);
            if (ca != cb)
            {
                found.AppendLine($"    {team.Abbrev} lineup  {ca}");
                found.AppendLine($"    {team.Abbrev} against  {cb}");
                shown++;
            }

            var ra = a.RosterFor(team.Id).Players.OrderBy(p => p.Id).ToList();
            var rb = b.RosterFor(team.Id).Players.OrderBy(p => p.Id).ToList();

            if (ra.Count != rb.Count)
            {
                found.AppendLine($"    {team.Abbrev} carries {ra.Count} men against {rb.Count}");
                if (++shown >= limit) break;
                continue;
            }

            for (int i = 0; i < ra.Count && shown < limit; i++)
            {
                var pa = ra[i];
                var pb = rb[i];

                string why =
                    pa.Id != pb.Id ? $"different man ({pa.Id} vs {pb.Id})"
                    : pa.Overall != pb.Overall ? $"overall {pa.Overall} vs {pb.Overall}  " +
                        $"[{Ratings(pa)}] vs [{Ratings(pb)}]"
                    : pa.DaysOut != pb.DaysOut ? $"days out {pa.DaysOut} vs {pb.DaysOut}"
                    : Line(a, pa) != Line(b, pb) ? $"line {Line(a, pa)} vs {Line(b, pb)}"
                    : null;

                if (why == null) continue;
                found.AppendLine($"    {team.Abbrev} {pa.Name}: {why}");
                shown++;
            }
        }

        return found.Length == 0 ? "    no difference found, which should not be possible"
                                 : found.ToString().TrimEnd();
    }
}
