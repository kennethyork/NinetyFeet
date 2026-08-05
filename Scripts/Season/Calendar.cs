using System;

namespace SandlotSlugfest.Season;

/// <summary>Where the league is in its year.</summary>
public enum SeasonPhase { Preseason, RegularSeason, Playoffs, Draft, Offseason }

/// <summary>
/// The league year as dates rather than an index.
///
/// A schedule of numbered days is enough to simulate with, but it is not a franchise: you cannot
/// be "three days from a series in Boston" if there are no days. This maps each schedule day onto
/// a real date so the season can be advanced a day, a week, or to your next game, the way a
/// management sim runs.
/// </summary>
public static class Calendar
{
    /// <summary>Opening day. Seasons here are short, so games run every other day or so.</summary>
    public static readonly DateTime OpeningDay = new(2026, 4, 2);

    /// <summary>
    /// Real days per scheduled game day. One, so a full 162-game season runs April to late
    /// September the way a real one does: 54 three-game series plus rest days is about 180 dates.
    /// At two days per game the same season would have spilled almost a year past opening day.
    /// </summary>
    public const int DaysPerGameDay = 1;

    public static DateTime DateOf(int gameDay) => OpeningDay.AddDays(gameDay * DaysPerGameDay);

    /// <summary>
    /// Which month of the season a game day falls in, April being 0. This is what buckets a
    /// player's monthly splits, so a hot August is something you can point at rather than
    /// something you half remember.
    /// </summary>
    public static int MonthIndex(int gameDay)
    {
        var d = DateOf(gameDay);
        int months = (d.Year - OpeningDay.Year) * 12 + d.Month - OpeningDay.Month;
        return months < 0 ? 0 : months > 6 ? 6 : months;
    }

    /// <summary>The schedule day on or before a calendar date.</summary>
    public static int GameDayOf(DateTime date) =>
        (int)Math.Floor((date - OpeningDay).TotalDays / DaysPerGameDay);

    /// <summary>
    /// How far through the schedule the deadline falls. Real baseball puts it around two thirds
    /// of the way in, which is late enough to know what your club is and early enough that the
    /// decision still costs you something.
    /// </summary>
    public const float TradeDeadlineFraction = 0.66f;

    public static string Format(DateTime d) => d.ToString("ddd d MMM yyyy");

    public static string FormatShort(DateTime d) => d.ToString("d MMM");

    /// <summary>A readable label for where the league is in its year.</summary>
    public static string PhaseLabel(SeasonPhase phase) => phase switch
    {
        SeasonPhase.Preseason => "Spring training",
        SeasonPhase.RegularSeason => "Regular season",
        SeasonPhase.Playoffs => "Postseason",
        SeasonPhase.Draft => "Draft",
        _ => "Offseason",
    };
}
