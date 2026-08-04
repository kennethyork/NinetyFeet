namespace SandlotSlugfest.Core;

/// <summary>
/// Real league rates, pulled from the official MLB Stats API rather than remembered.
///
/// Everything here is per game with both clubs combined, which is how the simulation reports, so
/// the two can be compared directly. Fetched from
/// https://statsapi.mlb.com/api/v1/teams/stats?season=2024&amp;group=hitting&amp;stats=season
/// (sportId 1 for the majors, 11 for Triple-A) on 3 August 2026.
///
/// These were refetched and recomputed on 3 August 2026 because the previous set was wrong, and
/// wrong in a way that mattered: it listed 13.30 strikeouts a game when the real figure is 16.96,
/// and a comment here asserted that 16.60 was the error and 13.30 the correction — exactly
/// backwards. Every calibration pass since was aiming a quarter of the way off on the single
/// statistic that governs how many balls reach play.
///
/// The totals are summed across all thirty clubs and divided by the league's summed gamesPlayed,
/// which gives a per-team-game rate; doubling that gives the both-clubs game total the simulation
/// reports. The earlier numbers appear to have mixed the two conventions.
///
/// Cross-checked against an independent Statcast sample of 87,799 pitches over 300 games, which
/// agrees to within about 2% on every line.
/// </summary>
public static class RealBaseball
{
    public readonly struct League
    {
        public readonly string Name;
        public readonly float Runs, Hits, Doubles, Triples, HomeRuns, Walks, Strikeouts, StolenBases;
        public readonly float Average, OnBase, Slugging;

        public League(string name, float runs, float hits, float doubles, float triples,
            float homeRuns, float walks, float strikeouts, float steals,
            float average, float onBase, float slugging)
        {
            Name = name; Runs = runs; Hits = hits; Doubles = doubles; Triples = triples;
            HomeRuns = homeRuns; Walks = walks; Strikeouts = strikeouts; StolenBases = steals;
            Average = average; OnBase = onBase; Slugging = slugging;
        }

        public float Ops => OnBase + Slugging;
    }

    /// <summary>The majors, 2024. Per-team figures doubled to give a combined game total.</summary>
    public static readonly League Mlb = new(
        "MLB 2024",
        runs: 8.79f, hits: 16.39f, doubles: 3.20f, triples: 0.29f, homeRuns: 2.24f,
        walks: 6.15f, strikeouts: 16.96f, steals: 1.49f,
        average: 0.243f, onBase: 0.312f, slugging: 0.399f);

    /// <summary>
    /// Triple-A, 2024 — a noticeably livelier run environment than the majors, which is what a
    /// minor-league level in this game should be calibrated against rather than the MLB numbers.
    /// </summary>
    public static readonly League TripleA = new(
        "AAA 2024",
        runs: 10.63f, hits: 17.36f, doubles: 3.56f, triples: 0.38f, homeRuns: 2.31f,
        walks: 8.44f, strikeouts: 17.68f, steals: 2.07f,
        average: 0.259f, onBase: 0.343f, slugging: 0.416f);

    /// <summary>
    /// Balls in play that fall for a hit, 2024: (H - HR) / (AB - K - HR + SF). Works out at .294
    /// for the majors.
    /// </summary>
    public const float MlbBabip = 0.294f;

    /// <summary>Earned run average and walks-plus-hits-per-inning, majors 2024.</summary>
    public const float MlbEra = 3.49f;
    public const float MlbWhip = 1.20f;
}
