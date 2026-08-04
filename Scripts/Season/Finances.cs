using System.Collections.Generic;
using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>A club's money for the season, and where it came from.</summary>
public sealed class ClubBook
{
    /// <summary>What the club may spend on salaries this year, in thousands.</summary>
    public int Budget;

    /// <summary>Tickets sold so far this season, and how many home dates they came over.</summary>
    public long Attendance;
    public int HomeDates;

    public int AverageCrowd => HomeDates == 0 ? 0 : (int)(Attendance / HomeDates);
}

/// <summary>
/// The league's economy: what each club can afford, and how that changes.
///
/// A budget is not a salary cap — nobody stops a club overspending — but it is what the computer
/// clubs will commit to, and what the user is measured against. It comes from the market the club
/// plays in and from how many people came through the gate last year, which is how a real club's
/// money actually behaves: winning pays for winning.
/// </summary>
public static class Finances
{
    /// <summary>
    /// Roughly how many people a market will support, in thousands of season-ticket equivalents.
    /// These follow the real metropolitan areas — a club in Hollywood or the Bronx can carry a
    /// payroll that Kansas City cannot, and that asymmetry is most of what makes running a small
    /// club a different game from running a big one.
    /// </summary>
    private static readonly Dictionary<string, float> MarketSize = new()
    {
        ["Bronx"] = 1.55f, ["Queens"] = 1.42f, ["Hollywood"] = 1.50f, ["Anaheim"] = 1.16f,
        ["North Side"] = 1.30f, ["South Side"] = 1.08f, ["Boston"] = 1.24f,
        ["Philadelphia"] = 1.20f, ["San Francisco"] = 1.22f, ["Oakland"] = 0.82f,
        ["Texas"] = 1.18f, ["Houston"] = 1.16f, ["Washington"] = 1.14f, ["Atlanta"] = 1.14f,
        ["Toronto"] = 1.18f, ["Seattle"] = 1.08f, ["Denver"] = 1.02f, ["Phoenix"] = 1.02f,
        ["St. Louis"] = 1.00f, ["Baltimore"] = 0.94f, ["San Diego"] = 1.00f,
        ["Detroit"] = 0.94f, ["Minnesota"] = 0.96f, ["Tampa Bay"] = 0.86f,
        ["Miami"] = 0.96f, ["Pittsburgh"] = 0.82f, ["Cleveland"] = 0.84f,
        ["Cincinnati"] = 0.84f, ["Kansas City"] = 0.80f, ["Milwaukee"] = 0.82f,
        ["Montreal"] = 0.92f, ["Nashville"] = 0.88f,
    };

    /// <summary>The league-average payroll a club of average market and average results carries.</summary>
    public const int BaselineBudget = 148_000;

    public static float Market(TeamData team) =>
        MarketSize.TryGetValue(team.City, out float m) ? m : 1f;

    /// <summary>
    /// Sets every club's opening budget. Called when a league is created, before anyone has a
    /// record to be judged on, so it is market alone.
    /// </summary>
    public static void OpenBooks(SeasonState season)
    {
        foreach (var t in Teams.All)
        {
            var book = season.Books(t.Id);
            book.Budget = Mathf.RoundToInt(BaselineBudget * Market(t) / 100f) * 100;
            book.Attendance = 0;
            book.HomeDates = 0;
        }
    }

    /// <summary>
    /// Rolls the books over between seasons. A club that drew well and won can spend more next
    /// year; one that emptied its park cannot. The swing is deliberately gentle — a bad year
    /// should hurt without putting a club into a spiral it can never climb out of.
    /// </summary>
    public static void CloseBooks(SeasonState season)
    {
        foreach (var t in Teams.All)
        {
            var book = season.Books(t.Id);
            var rec = season.Book.Record(t.Id);

            float onField = rec.Games == 0 ? 1f : 0.80f + rec.WinPct * 0.40f;
            float gate = book.HomeDates == 0
                ? 1f
                : Mathf.Clamp(book.AverageCrowd / (Attendance.Capacity * 0.62f), 0.85f, 1.15f);

            int target = Mathf.RoundToInt(BaselineBudget * Market(t) * onField * gate);

            // Move most of the way toward the target rather than jumping to it, so a single
            // hundred-loss season does not halve a club's payroll overnight.
            book.Budget = Mathf.RoundToInt(Mathf.Lerp(book.Budget, target, 0.55f) / 100f) * 100;
            book.Attendance = 0;
            book.HomeDates = 0;
        }
    }

    /// <summary>What a club has left to spend, which may be negative if it is over the books.</summary>
    public static int SpaceFor(SeasonState season, int teamId) =>
        season.Books(teamId).Budget - Contracts.Payroll(season.RosterFor(teamId));
}
