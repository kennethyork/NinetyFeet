using System.Collections.Generic;
using System.Linq;
using SandlotSlugfest.Data;
using SandlotSlugfest.Season;
using SandlotSlugfest.Stats;

namespace SandlotSlugfest.Net;

/// <summary>
/// One finished ballgame, packed small enough to send.
///
/// This is the one thing an online league cannot do the way an online game does it. A game keeps
/// two machines together by exchanging decisions and letting both simulations arrive at the same
/// answer, which works because both are running the same code over the same seed. A season breaks
/// that in exactly one place: the games each owner plays by hand. Nothing on the other machine can
/// re-derive a result a person produced with a bat in their hands, so that result has to travel.
///
/// Which makes this the most dangerous file in the feature, and the reason for how it is written.
/// Anything a played game changes that is not in here does not travel, and a difference that does
/// not travel is permanent — it will not heal, it will not be noticed by either player, and every
/// day afterwards is built on top of it. So the rule is: send the whole effect of the game, not
/// the interesting parts of it.
///
/// The effect is wider than the box score, and the parts that bite are the quiet ones:
///
///   · the numbers, obviously, for every man who batted or threw;
///   · the club's line, since a win is not derivable from a score once the game is not simulated;
///   · every arm's rest and recent workload, which decide who is available tomorrow;
///   · and the lineup card itself. A pinch hitter is permanent — Substitute writes the new man
///     into the order and the fielding chart and nothing puts the old one back — so an owner who
///     hits for his catcher in the eighth has changed his club until he changes it again. Miss
///     this one and the two leagues hold identical players, identical statistics and identical
///     standings while quietly sending out different nines.
///
/// It is verified by <c>--league</c>, which plays a game on one league, sends the packet to a
/// second, and requires the two fingerprints to match afterwards. That check is only worth as much
/// as the fingerprint is wide, which is why the lineup card was folded into it at the same time.
/// </summary>
public static class GameResult
{
    /// <summary>Bumped if the layout ever changes, so a mismatch is refused rather than misread.</summary>
    public const int Version = 1;

    /// <summary>Which day a packet belongs to, without unpacking the rest of it.</summary>
    public static int DayOf(int[] v) => v is { Length: >= 2 } ? v[1] : -1;

    private const int BattingFields = 18;
    private const int PitchingFields = 22;

    // -----------------------------------------------------------------------
    // Packing
    // -----------------------------------------------------------------------

    /// <summary>Everything the given game did to the league, as a flat array of numbers.</summary>
    public static int[] Pack(SeasonState season, ScheduledGame game, Core.GameSituation sit)
    {
        var w = new List<int>(2600)
        {
            Version, game.Day, game.AwayId, game.HomeId,
            sit.AwayScore, sit.HomeScore, game.Crowd,
        };

        // The clubs' lines. A simulated game derives the win from the score; a played one has to
        // carry it, because the book it came out of is the only place it exists.
        var records = sit.Stats.TeamLines.OrderBy(kv => kv.Key).ToList();
        w.Add(records.Count);
        foreach (var (teamId, rec) in records)
        {
            w.Add(teamId);
            w.Add(rec.Wins); w.Add(rec.Losses);
            w.Add(rec.RunsScored); w.Add(rec.RunsAllowed);
        }

        WriteClub(w, season.RosterFor(game.AwayId), game.AwayId);
        WriteClub(w, season.RosterFor(game.HomeId), game.HomeId);

        // Every man on both clubs, whether or not he got in. The ones who did not play still
        // carry rest and workload, and those are what decide who is available tomorrow.
        var men = season.RosterFor(game.AwayId).Players
            .Concat(season.RosterFor(game.HomeId).Players)
            .Distinct()
            .OrderBy(p => p.Id)
            .ToList();

        w.Add(men.Count);
        foreach (var p in men)
        {
            bool batted = sit.Stats.HasBatted(p);
            bool pitched = sit.Stats.HasPitched(p);

            w.Add(p.Id);
            w.Add((batted ? 1 : 0) | (pitched ? 2 : 0));
            w.Add(p.DaysOut); w.Add(p.RestDays); w.Add(p.RecentPitches);

            if (batted) WriteBatting(w, sit.Stats.Batting(p));
            if (pitched) WritePitching(w, sit.Stats.Pitching(p));
        }

        return w.ToArray();
    }

    private static void WriteClub(List<int> w, Roster club, int teamId)
    {
        w.Add(teamId);

        w.Add(club.BattingOrder.Count);
        foreach (var p in club.BattingOrder) w.Add(p?.Id ?? -1);

        var spots = club.Starters.Keys.OrderBy(k => (int)k).ToList();
        w.Add(spots.Count);
        foreach (var spot in spots) { w.Add((int)spot); w.Add(club.Starters[spot]?.Id ?? -1); }

        w.Add(club.Pitchers.Count);
        foreach (var p in club.Pitchers) w.Add(p?.Id ?? -1);

        w.Add(club.CurrentPitcher?.Id ?? -1);
    }

    private static void WriteBatting(List<int> w, BattingLine b)
    {
        w.Add(b.Games); w.Add(b.PlateAppearances); w.Add(b.AtBats); w.Add(b.Hits);
        w.Add(b.Doubles); w.Add(b.Triples); w.Add(b.HomeRuns); w.Add(b.Runs);
        w.Add(b.RunsBattedIn); w.Add(b.Walks); w.Add(b.Strikeouts); w.Add(b.StolenBases);
        w.Add(b.HitByPitch); w.Add(b.IntentionalWalks); w.Add(b.CaughtStealing);
        w.Add(b.SacrificeFlies); w.Add(b.SacrificeBunts); w.Add(b.GroundedIntoDoublePlay);
    }

    private static void WritePitching(List<int> w, PitchingLine t)
    {
        w.Add(t.Games); w.Add(t.GamesStarted); w.Add(t.Outs); w.Add(t.Hits);
        w.Add(t.Runs); w.Add(t.EarnedRuns); w.Add(t.Walks); w.Add(t.Strikeouts);
        w.Add(t.HomeRunsAllowed); w.Add(t.Wins); w.Add(t.Losses); w.Add(t.Saves);
        w.Add(t.Pitches); w.Add(t.HitBatters); w.Add(t.IntentionalWalksIssued);
        w.Add(t.WildPitches); w.Add(t.BattersFaced); w.Add(t.Holds); w.Add(t.BlownSaves);
        w.Add(t.CompleteGames); w.Add(t.Shutouts); w.Add(t.QualityStarts);
    }

    // -----------------------------------------------------------------------
    // Applying
    // -----------------------------------------------------------------------

    /// <summary>
    /// Writes a game somebody else played into this league. Returns null if it went in, or a
    /// sentence saying why not — a packet that cannot be applied has to be loud, because carrying
    /// on without it is how a season quietly splits in two.
    /// </summary>
    public static string Apply(SeasonState season, int[] v)
    {
        if (v == null || v.Length < 8) return "the packet is empty or truncated";
        if (v[0] != Version) return $"packet version {v[0]}, and this game speaks {Version}";

        int at = 1;
        int day = v[at++], awayId = v[at++], homeId = v[at++];
        int awayRuns = v[at++], homeRuns = v[at++], crowd = v[at++];

        var game = season.Games.FirstOrDefault(g =>
            g.Day == day && g.AwayId == awayId && g.HomeId == homeId);

        if (game == null)
            return $"no game on day {day} between {awayId} and {homeId} on this calendar";
        if (game.Played)
            return $"the game on day {day} between {awayId} and {homeId} is already in the books";

        // Every read past this point is bounds-checked, because a packet that runs out halfway
        // would otherwise leave the league half-updated, which is worse than not applying it.
        try
        {
            var book = new StatBook();

            int clubCount = v[at++];
            for (int i = 0; i < clubCount; i++)
            {
                int teamId = v[at++];
                var rec = book.Record(teamId);
                rec.Wins = v[at++]; rec.Losses = v[at++];
                rec.RunsScored = v[at++]; rec.RunsAllowed = v[at++];
            }

            var away = season.RosterFor(awayId);
            var home = season.RosterFor(homeId);

            var byId = away.Players.Concat(home.Players)
                .GroupBy(p => p.Id)
                .ToDictionary(g => g.Key, g => g.First());

            at = ReadClub(v, at, away, byId);
            at = ReadClub(v, at, home, byId);

            int men = v[at++];
            for (int i = 0; i < men; i++)
            {
                int id = v[at++];
                int flags = v[at++];
                int daysOut = v[at++], rest = v[at++], recent = v[at++];

                byId.TryGetValue(id, out var p);

                if (p != null)
                {
                    p.DaysOut = daysOut;
                    p.RestDays = rest;
                    p.RecentPitches = recent;
                }

                // The line is read whether or not the man was found, so the cursor stays in step.
                if ((flags & 1) != 0) at = ReadBatting(v, at, p == null ? null : book.Batting(p));
                if ((flags & 2) != 0) at = ReadPitching(v, at, p == null ? null : book.Pitching(p));
            }

            game.AwayRuns = awayRuns;
            game.HomeRuns = homeRuns;
            game.Played = true;
            game.Crowd = crowd > 0 ? crowd : Attendance.For(season, game);

            season.Book.Absorb(book);
            Attendance.Record(season, game, game.Crowd);
            season.GamesPlayed++;
            return null;
        }
        catch (System.IndexOutOfRangeException)
        {
            return "the packet ran out partway through; the game was not applied";
        }
    }

    private static int ReadClub(int[] v, int at, Roster club, Dictionary<int, PlayerData> byId)
    {
        at++;                                   // the club id, already known from the header

        var order = new List<PlayerData>();
        int orderCount = v[at++];
        for (int i = 0; i < orderCount; i++)
            if (byId.TryGetValue(v[at++], out var p)) order.Add(p);

        var starters = new List<(Data.Position Spot, PlayerData Man)>();
        int spotCount = v[at++];
        for (int i = 0; i < spotCount; i++)
        {
            var spot = (Data.Position)v[at++];
            if (byId.TryGetValue(v[at++], out var p)) starters.Add((spot, p));
        }

        var staff = new List<PlayerData>();
        int armCount = v[at++];
        for (int i = 0; i < armCount; i++)
            if (byId.TryGetValue(v[at++], out var p)) staff.Add(p);

        byId.TryGetValue(v[at++], out var onTheMound);

        // The mound goes first and the card second, deliberately. SetPitcher hands the incoming
        // arm the outgoing one's place in the batting order — which is right when a manager makes
        // a change, and wrong here, where the order that came over the wire is already the answer.
        if (onTheMound != null) club.SetPitcher(onTheMound);

        if (order.Count == orderCount)
        {
            club.BattingOrder.Clear();
            club.BattingOrder.AddRange(order);
        }

        if (starters.Count == spotCount)
        {
            club.Starters.Clear();
            foreach (var (spot, man) in starters) club.Starters[spot] = man;
        }

        if (staff.Count == armCount)
        {
            club.Pitchers.Clear();
            club.Pitchers.AddRange(staff);
        }

        return at;
    }

    private static int ReadBatting(int[] v, int at, BattingLine b)
    {
        if (b == null) return at + BattingFields;
        b.Games = v[at]; b.PlateAppearances = v[at + 1]; b.AtBats = v[at + 2]; b.Hits = v[at + 3];
        b.Doubles = v[at + 4]; b.Triples = v[at + 5]; b.HomeRuns = v[at + 6]; b.Runs = v[at + 7];
        b.RunsBattedIn = v[at + 8]; b.Walks = v[at + 9]; b.Strikeouts = v[at + 10];
        b.StolenBases = v[at + 11]; b.HitByPitch = v[at + 12]; b.IntentionalWalks = v[at + 13];
        b.CaughtStealing = v[at + 14]; b.SacrificeFlies = v[at + 15];
        b.SacrificeBunts = v[at + 16]; b.GroundedIntoDoublePlay = v[at + 17];
        return at + BattingFields;
    }

    private static int ReadPitching(int[] v, int at, PitchingLine t)
    {
        if (t == null) return at + PitchingFields;
        t.Games = v[at]; t.GamesStarted = v[at + 1]; t.Outs = v[at + 2]; t.Hits = v[at + 3];
        t.Runs = v[at + 4]; t.EarnedRuns = v[at + 5]; t.Walks = v[at + 6]; t.Strikeouts = v[at + 7];
        t.HomeRunsAllowed = v[at + 8]; t.Wins = v[at + 9]; t.Losses = v[at + 10];
        t.Saves = v[at + 11]; t.Pitches = v[at + 12]; t.HitBatters = v[at + 13];
        t.IntentionalWalksIssued = v[at + 14]; t.WildPitches = v[at + 15];
        t.BattersFaced = v[at + 16]; t.Holds = v[at + 17]; t.BlownSaves = v[at + 18];
        t.CompleteGames = v[at + 19]; t.Shutouts = v[at + 20]; t.QualityStarts = v[at + 21];
        return at + PitchingFields;
    }
}
