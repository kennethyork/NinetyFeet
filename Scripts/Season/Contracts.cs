using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>
/// What players cost.
///
/// A franchise mode without money is a spreadsheet that only goes up: every trade is free, keeping
/// a thirty-eight-year-old costs nothing, and there is never a reason to choose. Scarcity is what
/// turns a roster into a set of decisions.
///
/// Everything here is in thousands of dollars, so a salary fits comfortably in an int and a save
/// file stays readable. $760 is the league minimum; $30,000 is thirty million.
///
/// The service-time rules are the real ones in miniature, because they are what make the economy
/// interesting rather than merely present: a young player is cheap however good he is, which is
/// why a club develops one instead of buying one.
/// </summary>
public static class Contracts
{
    /// <summary>The least a club may pay anyone on the roster.</summary>
    public const int Minimum = 760;

    /// <summary>Years of service before a player may argue for a raise, and before he may leave.</summary>
    public const int ArbitrationService = 3;
    public const int FreeAgentService = 6;

    /// <summary>
    /// What an established player is worth a year on the open market, before service time is
    /// taken into account. Steep at the top on purpose — the difference between a good regular
    /// and a star is most of a payroll.
    /// </summary>
    public static int MarketValue(PlayerData p)
    {
        int overall = p.Overall;
        float baseValue = overall switch
        {
            >= 10 => 40000f,
            9 => 32000f,
            8 => 24000f,
            7 => 15500f,
            6 => 8500f,
            5 => 3600f,
            4 => 1400f,
            _ => Minimum,
        };

        // A club pays for the years ahead, not the ones behind. A twenty-six-year-old at a given
        // ability is worth more than a thirty-four-year-old at the same ability, and the market
        // knows it.
        float age = p.Age switch
        {
            <= 24 => 1.12f,
            <= 28 => 1.06f,
            <= 31 => 1.0f,
            <= 33 => 0.88f,
            <= 35 => 0.72f,
            <= 37 => 0.55f,
            _ => 0.40f,
        };

        // A closer is paid like one. His overall is already role-adjusted, but the job itself
        // carries a premium that a middle reliever's does not.
        float role = p.Position == Data.Position.P && p.Role == StaffRole.Closer ? 1.15f : 1f;

        return Mathf.Max(Minimum, Mathf.RoundToInt(baseValue * age * role / 10f) * 10);
    }

    /// <summary>
    /// What he actually earns, given how long he has been up. A player with no service has no
    /// leverage at all; one with three years can argue for a slice of his market value; one with
    /// six can walk, so he is paid what he is worth.
    /// </summary>
    public static int SalaryFor(PlayerData p)
    {
        int market = MarketValue(p);
        if (p.ServiceYears >= FreeAgentService) return market;
        if (p.ServiceYears < ArbitrationService) return Minimum;

        // Arbitration awards climb across the three years a player is eligible.
        float share = p.ServiceYears switch { 3 => 0.32f, 4 => 0.52f, _ => 0.72f };
        return Mathf.Max(Minimum, Mathf.RoundToInt(market * share / 10f) * 10);
    }

    /// <summary>How long a deal a free agent wants. Good young players get years; old ones do not.</summary>
    public static int DesiredYears(PlayerData p, ref Rng rng)
    {
        if (p.Age >= 35) return 1;
        if (p.Overall >= 8) return rng.Range(3, 6);
        if (p.Overall >= 6) return rng.Range(2, 5);
        return rng.Range(1, 3);
    }

    /// <summary>
    /// Puts an opening-day contract on a player who has never had one, inferring his service time
    /// from his age. Without this every club would open the books at the league minimum and the
    /// economy would mean nothing in its first season.
    /// </summary>
    public static void Establish(PlayerData p, ref Rng rng)
    {
        if (p.Salary > 0) return;

        // Debut around twenty-one, so service time is roughly age minus the rookie age, with some
        // spread for the late bloomers and the men who came up at nineteen.
        p.ServiceYears = Mathf.Clamp(p.Age - Development.RookieAge + rng.Range(-1, 2), 0, 18);
        p.Salary = SalaryFor(p);
        p.ContractYears = p.ServiceYears >= FreeAgentService
            ? rng.Range(1, 5)
            : 1;
    }

    /// <summary>A club's total commitment for the coming season.</summary>
    public static int Payroll(Roster roster)
    {
        int total = 0;
        foreach (var p in roster.Players) total += p.Salary;
        return total;
    }

    /// <summary>Money as a person would say it: "$4.2M", "$760K".</summary>
    public static string Text(int thousands)
    {
        if (thousands >= 100000) return $"${thousands / 1000}M";
        if (thousands >= 1000) return $"${thousands / 1000f:0.#}M";
        return $"${thousands}K";
    }
}
