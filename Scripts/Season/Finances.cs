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

    /// <summary>Consecutive seasons finished above the luxury tax line. Drives the rate.</summary>
    public int TaxYears;
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

    // -----------------------------------------------------------------------
    // The luxury tax
    // -----------------------------------------------------------------------

    /// <summary>
    /// The threshold above which a club pays for its payroll twice.
    ///
    /// A budget on its own is a wall: you either fit under it or you cannot sign the man. That is
    /// not how the money in baseball actually constrains anybody — a rich club can always outspend
    /// the rule, and what stops it is that going past this line costs increasingly silly amounts.
    /// A tax turns "can I afford him" into "is he worth what he costs plus the penalty", which is
    /// a far more interesting question and the one real front offices are actually asking.
    ///
    /// Set well above the average budget, so it binds on the clubs that spend rather than on
    /// everybody. The rate climbs with repeat offences the way the real one does.
    /// </summary>
    public const int TaxLine = 214_000;

    /// <summary>What a club is over by, or zero.</summary>
    public static int OverTaxLine(SeasonState season, int teamId) =>
        Mathf.Max(0, Contracts.Payroll(season.RosterFor(teamId)) - TaxLine);

    /// <summary>
    /// The rate a club pays on the excess. A first offence is cheap enough to be worth it for a
    /// club that thinks it can win; a fourth is punishing, which is what makes a run at a title a
    /// decision with a bill attached rather than a free choice.
    /// </summary>
    public static float TaxRate(int consecutiveYears) => consecutiveYears switch
    {
        <= 1 => 0.20f,
        2 => 0.32f,
        3 => 0.50f,
        _ => 0.62f,
    };

    /// <summary>What this club owes, given how long it has been over.</summary>
    public static int TaxBill(SeasonState season, int teamId)
    {
        int over = OverTaxLine(season, teamId);
        if (over <= 0) return 0;

        var book = season.Books(teamId);
        return Mathf.RoundToInt(over * TaxRate(book.TaxYears));
    }

    /// <summary>
    /// Settles the tax at the end of a season: the bill comes off next year's budget, and the
    /// clock of consecutive offences ticks up or resets.
    /// </summary>
    public static List<string> SettleTax(SeasonState season)
    {
        var lines = new List<string>();

        foreach (var t in Teams.All)
        {
            var book = season.Books(t.Id);
            int bill = TaxBill(season, t.Id);

            if (bill <= 0) { book.TaxYears = 0; continue; }

            book.TaxYears++;
            book.Budget = Mathf.Max(BaselineBudget / 2, book.Budget - bill);

            lines.Add($"{t.FullName} pay {Contracts.Text(bill)} in luxury tax — " +
                      $"{book.TaxYears} year(s) over the line.");
        }

        return lines;
    }
}
