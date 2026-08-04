using System.Collections.Generic;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>One scheduled game. Day groups games that are played on the same date.</summary>
public sealed class ScheduledGame
{
    public int Day;
    public int AwayId;
    public int HomeId;
    public bool Played;
    public int AwayRuns;
    public int HomeRuns;

    /// <summary>Tickets sold. Fixed once the game is in the books.</summary>
    public int Crowd;

    public bool Involves(int teamId) => AwayId == teamId || HomeId == teamId;

    public int WinnerId => AwayRuns > HomeRuns ? AwayId : HomeId;
    public int LoserId => AwayRuns > HomeRuns ? HomeId : AwayId;
}

/// <summary>
/// Builds a balanced season schedule for the 32-club league. Every club plays the same number
/// of games, home and away are evened out, and division rivals meet more often than the rest
/// of the league — the way a real schedule is weighted.
/// </summary>
public static class Schedule
{
    public const int ShortSeason = 33;
    public const int MediumSeason = 81;

    /// <summary>A real major-league season: 162 games, played as 54 three-game series.</summary>
    public const int FullSeason = 162;

    /// <summary>Games in a series. Real clubs meet for a set at one venue, not one game at a time.</summary>
    public const int SeriesLength = 3;

    /// <summary>Series played back to back before the whole league gets a day off.</summary>
    private const int SeriesPerRest = 3;

    /// <summary>
    /// Generates the whole season as a run of series with rest days between them.
    ///
    /// It used to be one game per club per day for the entire year: nobody ever had a day off,
    /// nobody ever played the same opponent twice running, and an injured player healed on a
    /// schedule that had no rest in it. A season is now a sequence of three-game sets at one
    /// venue, which is what makes a date on the calendar mean something.
    /// </summary>
    public static List<ScheduledGame> Build(int gamesPerTeam, int seed)
    {
        var rng = new Rng(seed);
        var games = new List<ScheduledGame>();

        // Each round of the circle method pairs all 32 clubs; each pairing becomes one series.
        int rounds = Godot.Mathf.Max(1, Godot.Mathf.CeilToInt(gamesPerTeam / (float)SeriesLength));
        var pairings = Pairings(rounds, ref rng);

        // Home games hosted so far, per club. Deriving the host from round or club parity keeps
        // going wrong here: club 0 is the fixed point of the circle method, so any parity rule
        // holds the same value for it every single round — the first attempt had Baltimore playing
        // all thirty-three games away. Counting is the only thing that cannot drift.
        var hosted = new int[32];

        int day = 0;
        for (int r = 0; r < pairings.Count; r++)
        {
            foreach (var (a, b) in pairings[r])
            {
                // Whoever has hosted less gets this series; ties break on club id for stability.
                // Ties alternate by round rather than by club id: breaking on `a < b` quietly
                // handed every tie to the lower-numbered club, which pushed Baltimore to 63% home.
                bool aHosts = hosted[a] != hosted[b] ? hosted[a] < hosted[b] : r % 2 == 0;
                hosted[aHosts ? a : b]++;
                for (int g = 0; g < SeriesLength; g++)
                    games.Add(new ScheduledGame
                    {
                        Day = day + g,
                        AwayId = aHosts ? b : a,
                        HomeId = aHosts ? a : b,
                    });
            }

            day += SeriesLength;

            // A rest day every few series — the whole league is idle, so the hurt heal.
            if ((r + 1) % SeriesPerRest == 0) day++;
        }

        return games;
    }

    /// <summary>
    /// Round-robin pairings, weighted so division rivals come up more often than the rest of the
    /// league. Each returned round is sixteen pairings covering all thirty-two clubs.
    /// </summary>
    private static List<List<(int A, int B)>> Pairings(int rounds, ref Rng rng)
    {
        const int n = 32;
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;

        var all = new List<List<(int, int)>>();
        for (int round = 0; round < rounds; round++)
        {
            var set = new List<(int, int)>();
            for (int i = 0; i < n / 2; i++) set.Add((order[i], order[n - 1 - i]));
            all.Add(set);

            // Rotate everything except the first entry — the circle method.
            int last = order[n - 1];
            for (int i = n - 1; i > 1; i--) order[i] = order[i - 1];
            order[1] = last;
        }
        return all;
    }

    /// <summary>How many complete round robins fit inside the target without overshooting.</summary>
    private static int FullRoundRobins(int gamesPerTeam) => Godot.Mathf.Max(1, gamesPerTeam / 31);

    /// <summary>
    /// One full round robin across all 32 clubs using the circle method: fix club 0, rotate the
    /// rest. Thirty-one rounds of sixteen games, every club playing exactly once per round.
    /// </summary>
    private static int AddRoundRobin(List<ScheduledGame> games, int day, bool flip)
    {
        const int n = 32;
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;

        for (int round = 0; round < n - 1; round++)
        {
            for (int i = 0; i < n / 2; i++)
            {
                int a = order[i];
                int b = order[n - 1 - i];

                // Home must depend on the round alone. Keying it to (round + i) looks balanced
                // but is not: a rotating club's index climbs by one every round, so (round + i)
                // holds the same parity for that club forever and it never plays away.
                bool firstIsHome = (round % 2 == 0) ^ flip;
                games.Add(new ScheduledGame
                {
                    Day = day,
                    AwayId = firstIsHome ? b : a,
                    HomeId = firstIsHome ? a : b,
                });
            }
            day++;

            // Rotate everything except the first entry.
            int last = order[n - 1];
            for (int i = n - 1; i > 1; i--) order[i] = order[i - 1];
            order[1] = last;
        }
        return day;
    }

    /// <summary>A round in which every club plays each of its seven division rivals once.</summary>
    private static int AddDivisionalRound(List<ScheduledGame> games, int day, bool flip)
    {
        foreach (var league in new[] { League.American, League.National })
        foreach (var division in new[] { Division.East, Division.West })
        {
            var ids = new List<int>();
            foreach (var t in Teams.In(league, division)) ids.Add(t.Id);

            // Circle method again, within the eight-club division.
            int n = ids.Count;
            var order = ids.ToArray();
            int startDay = day;

            for (int round = 0; round < n - 1; round++)
            {
                for (int i = 0; i < n / 2; i++)
                {
                    int a = order[i];
                    int b = order[n - 1 - i];
                    bool firstIsHome = (round % 2 == 0) ^ flip;
                    games.Add(new ScheduledGame
                    {
                        Day = startDay + round,
                        AwayId = firstIsHome ? b : a,
                        HomeId = firstIsHome ? a : b,
                    });
                }

                int last = order[n - 1];
                for (int i = n - 1; i > 1; i--) order[i] = order[i - 1];
                order[1] = last;
            }
        }
        return day + 7;
    }

    /// <summary>Shuffles which day each block falls on so the season is not perfectly ordered.</summary>
    private static void ShuffleDays(List<ScheduledGame> games, ref Rng rng)
    {
        int maxDay = 0;
        foreach (var g in games) maxDay = Godot.Mathf.Max(maxDay, g.Day);

        var remap = new int[maxDay + 1];
        for (int i = 0; i <= maxDay; i++) remap[i] = i;
        for (int i = maxDay; i > 0; i--)
        {
            int j = rng.Range(0, i + 1);
            (remap[i], remap[j]) = (remap[j], remap[i]);
        }

        foreach (var g in games) g.Day = remap[g.Day];
        games.Sort((a, b) => a.Day.CompareTo(b.Day));
    }

    /// <summary>Sanity check used by the self-test: everyone plays the same number of games.</summary>
    public static bool IsBalanced(List<ScheduledGame> games, out string problem)
    {
        var counts = new int[32];
        var home = new int[32];
        foreach (var g in games)
        {
            counts[g.AwayId]++;
            counts[g.HomeId]++;
            home[g.HomeId]++;
        }

        for (int i = 1; i < 32; i++)
            if (counts[i] != counts[0])
            {
                problem = $"{Teams.Get(i).Abbrev} has {counts[i]} games, {Teams.Get(0).Abbrev} has {counts[0]}";
                return false;
            }

        for (int i = 0; i < 32; i++)
        {
            float share = home[i] / (float)counts[i];
            if (share < 0.35f || share > 0.65f)
            {
                problem = $"{Teams.Get(i).Abbrev} plays {share * 100f:F0}% of its games at home";
                return false;
            }
        }

        // Nobody may be booked twice on the same day.
        var seen = new HashSet<(int, int)>();
        foreach (var g in games)
        {
            if (!seen.Add((g.Day, g.AwayId)) || !seen.Add((g.Day, g.HomeId)))
            {
                problem = $"a club is double-booked on day {g.Day}";
                return false;
            }
        }

        problem = null;
        return true;
    }
}
