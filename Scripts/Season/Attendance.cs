using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>
/// How many people came, and what the ballpark sounds like because of it.
///
/// A crowd is not decoration. It is the visible form of everything the club has done: a contender
/// in a big market in September plays in front of a full house, and a hundred-loss club in April
/// plays in front of nobody. It also feeds the money — the gate is what a club's budget is built
/// out of — so the same number the broadcast shows is the one the front office lives on.
/// </summary>
public static class Attendance
{
    /// <summary>A typical major-league park. Real capacities run 25,000 to 56,000.</summary>
    public const float Capacity = 41500f;

    /// <summary>
    /// Works out the gate for one game. Deterministic in the schedule day and the clubs, so the
    /// same game always draws the same crowd however many times it is replayed.
    /// </summary>
    public static int For(SeasonState season, ScheduledGame game)
    {
        var home = Teams.Get(game.HomeId);
        var rng = new Rng(game.HomeId * 7919 + game.Day * 131 + season.Year * 17);

        float draw = Finances.Market(home);

        // A club people want to watch. Early in the year they are buying last season; by summer
        // they are buying this one.
        var rec = season.Book.Record(game.HomeId);
        if (rec.Games >= 10) draw *= 0.72f + rec.WinPct * 0.58f;

        // A pennant race fills a park. So does a good visiting club.
        float through = season.FinalDay <= 0 ? 0f : Mathf.Clamp(game.Day / (float)season.FinalDay, 0f, 1f);
        var visitor = season.Book.Record(game.AwayId);
        if (rec.Games >= 20 && InTheRace(season, game.HomeId))
            draw *= 1f + through * 0.30f;
        else if (rec.Games >= 20)
            draw *= 1f - through * 0.22f;                 // out of it, and everyone knows

        if (visitor.Games >= 20 && visitor.WinPct > 0.560f) draw *= 1.07f;

        // Opening day, weekends and the weather.
        if (game.Day == 0) draw *= 1.45f;
        else if (season.Today.DayOfWeek is System.DayOfWeek.Friday or System.DayOfWeek.Saturday)
            draw *= 1.14f;
        else if (season.Today.DayOfWeek is System.DayOfWeek.Sunday) draw *= 1.09f;

        var sky = Weather.For(season, game);
        draw *= sky.Kind switch
        {
            Sky.Rain => 0.74f,
            Sky.Overcast => 0.94f,
            _ => 1f,
        };
        if (sky.TemperatureF < 45) draw *= 0.86f;

        // Nobody sells out every night, and nobody plays to a genuinely empty park.
        float crowd = Capacity * 0.58f * draw * rng.Range(0.90f, 1.10f);
        return Mathf.RoundToInt(Mathf.Clamp(crowd, Capacity * 0.16f, Capacity));
    }

    /// <summary>Within striking distance of the best record in the club's own league.</summary>
    private static bool InTheRace(SeasonState season, int teamId)
    {
        var mine = season.Book.Record(teamId);
        var league = Teams.Get(teamId).League;
        float best = Teams.In(league)
            .Select(t => season.Book.Record(t.Id).WinPct)
            .DefaultIfEmpty(0f)
            .Max();
        return mine.WinPct >= best - 0.060f;
    }

    /// <summary>Books a night's gate against the home club.</summary>
    public static void Record(SeasonState season, ScheduledGame game, int crowd)
    {
        var book = season.Books(game.HomeId);
        book.Attendance += crowd;
        book.HomeDates++;
    }

    /// <summary>"38,214" — how a scoreboard writes it.</summary>
    public static string Text(int crowd) => crowd.ToString("N0");
}
