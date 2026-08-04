using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>What a club offered a player, and what he made of it.</summary>
public readonly struct Offer
{
    public readonly int TeamId;
    public readonly int Salary;
    public readonly int Years;

    public Offer(int teamId, int salary, int years)
    {
        TeamId = teamId; Salary = salary; Years = years;
    }

    public int Total => Salary * Mathf.Max(1, Years);
}

/// <summary>
/// The winter market: contracts run out, players go on the block, and clubs spend what they have.
///
/// This is the part that makes a roster a set of choices rather than a list. Every player is now
/// on a clock — three years to arbitration, six to free agency — so a club that develops a star
/// gets him cheap for a while and then has to decide whether it can afford to keep him. That
/// decision is the whole of a franchise mode.
/// </summary>
public static class FreeAgency
{
    /// <summary>
    /// Runs the offseason's business, in the order it happens: options and raises first, then
    /// everyone whose deal is up hits the market, then clubs sign until the money runs out.
    /// Returns the headlines.
    /// </summary>
    public static List<string> Run(SeasonState season, int seed)
    {
        var rng = new Rng(seed * 4231 + 89);
        var news = new List<string>();

        Expire(season, news);
        Waivers(season, news);
        Sign(season, ref rng, news);
        FillShortRosters(season, ref rng, news);

        return news;
    }

    /// <summary>
    /// Ticks every contract down a year. Players still under contract get their service time and,
    /// if they are arbitration-eligible, a raise. Everyone else becomes a free agent.
    /// </summary>
    private static void Expire(SeasonState season, List<string> news)
    {
        foreach (var t in Teams.All)
        {
            var roster = season.RosterFor(t.Id);

            foreach (var p in roster.Players.ToList())
            {
                p.ServiceYears++;
                p.ContractYears--;

                if (p.ContractYears > 0)
                {
                    // Still signed, but arbitration re-prices him every winter.
                    if (p.ServiceYears is >= Contracts.ArbitrationService and < Contracts.FreeAgentService)
                    {
                        int was = p.Salary;
                        p.Salary = Contracts.SalaryFor(p);
                        if (p.Salary > was * 1.6f && p.Salary > 4000)
                            news.Add($"{p.Name} wins arbitration with {t.Abbrev}: " +
                                     $"{Contracts.Text(was)} to {Contracts.Text(p.Salary)}.");
                    }
                    continue;
                }

                // Not yet free: a club simply renews a player with no leverage.
                if (p.ServiceYears < Contracts.FreeAgentService)
                {
                    p.Salary = Contracts.SalaryFor(p);
                    p.ContractYears = 1;
                    continue;
                }

                Cut(season, t.Id, p);
                p.IsFreeAgent = true;
                season.FreeAgents.Add(p);
            }
        }

        var headline = season.FreeAgents
            .OrderByDescending(p => Contracts.MarketValue(p))
            .FirstOrDefault();
        if (headline != null)
            news.Add($"{season.FreeAgents.Count} players hit free agency, led by {headline.Name} " +
                     $"(overall {headline.Overall}, asking about " +
                     $"{Contracts.Text(Contracts.MarketValue(headline))} a year).");
    }

    /// <summary>
    /// Clubs over their budget put their most expensive surplus on waivers, and clubs with room
    /// claim. A claimed player takes his contract with him, which is what makes it a real risk.
    /// </summary>
    private static void Waivers(SeasonState season, List<string> news)
    {
        var exposed = new List<(int From, PlayerData Player)>();

        foreach (var t in Teams.All)
        {
            var roster = season.RosterFor(t.Id);
            int guard = 0;

            while (Finances.SpaceFor(season, t.Id) < 0 && guard++ < 12)
            {
                // The man whose salary is furthest ahead of what he is worth goes first.
                var cut = roster.Players
                    .Where(p => p.Salary > Contracts.Minimum * 3)
                    .OrderByDescending(p => p.Salary - Contracts.MarketValue(p))
                    .FirstOrDefault();
                if (cut == null) break;

                Cut(season, t.Id, cut);
                exposed.Add((t.Id, cut));
            }
        }

        foreach (var (from, player) in exposed)
        {
            // In reverse order of last year's finish, the way a real waiver claim is ordered.
            var claimant = Teams.All
                .Where(t => t.Id != from)
                .Where(t => Finances.SpaceFor(season, t.Id) > player.Salary)
                .Where(t => season.RosterFor(t.Id).Players.Count < Development.RosterLimit)
                .OrderBy(t => season.Book.Record(t.Id).WinPct)
                .FirstOrDefault(t => Wants(season, t.Id, player));

            if (claimant == null)
            {
                player.IsFreeAgent = true;
                season.FreeAgents.Add(player);
                news.Add($"{Teams.Get(from).Abbrev} release {player.Name} " +
                         $"({Contracts.Text(player.Salary)}); he clears waivers.");
                continue;
            }

            Add(season, claimant.Id, player);
            news.Add($"{claimant.Abbrev} claim {player.Name} off waivers from " +
                     $"{Teams.Get(from).Abbrev}.");
        }
    }

    /// <summary>Whether a club has a use for this player, given who it already has.</summary>
    private static bool Wants(SeasonState season, int teamId, PlayerData p)
    {
        var roster = season.RosterFor(teamId);

        if (p.Position == Data.Position.P)
        {
            var arms = roster.Players.Where(x => x.Position == Data.Position.P).ToList();
            if (arms.Count < Development.StaffSize) return true;
            return p.Overall > arms.Min(a => a.Overall);
        }

        int bats = roster.Players.Count(x => x.Position != Data.Position.P);
        if (bats < Development.RosterLimit - Development.StaffSize) return true;

        var weakest = roster.Players
            .Where(x => x.Position != Data.Position.P)
            .OrderBy(x => x.Overall)
            .First();
        return p.Overall > weakest.Overall;
    }

    /// <summary>
    /// The computer clubs work the market. Each pass, every club with money and a need makes its
    /// best offer on the player it wants most, and the player takes the best deal on the table.
    /// </summary>
    private static void Sign(SeasonState season, ref Rng rng, List<string> news)
    {
        // Best players sign first, which is how a real market clears.
        var market = season.FreeAgents
            .OrderByDescending(Contracts.MarketValue)
            .ToList();

        foreach (var player in market)
        {
            int asking = Contracts.MarketValue(player);
            int years = Contracts.DesiredYears(player, ref rng);

            Offer best = default;
            foreach (var t in Teams.All)
            {
                if (t.Id == season.UserTeamId) continue;         // the user does his own business
                var roster = season.RosterFor(t.Id);
                if (roster.Players.Count >= Development.RosterLimit) continue;
                if (!Wants(season, t.Id, player)) continue;

                int space = Finances.SpaceFor(season, t.Id);
                if (space < asking * 0.75f) continue;

                // A club that needs him badly pays over the odds; one that merely likes him does
                // not. Without that spread every free agent signs for exactly his market value and
                // the winter has no texture at all.
                float appetite = Need(season, t.Id, player);
                int bid = Mathf.RoundToInt(asking * appetite);
                if (bid > space) bid = space;
                if (bid < asking * 0.75f) continue;

                var offer = new Offer(t.Id, bid, years);
                if (offer.Total > best.Total) best = offer;
            }

            if (best.Salary <= 0) continue;

            player.Salary = best.Salary;
            player.ContractYears = best.Years;
            player.IsFreeAgent = false;
            Add(season, best.TeamId, player);
            season.FreeAgents.Remove(player);

            if (best.Salary >= 12000)
                news.Add($"{Teams.Get(best.TeamId).Abbrev} sign {player.Name} — " +
                         $"{best.Years} years, {Contracts.Text(best.Salary)} a year.");
        }
    }

    /// <summary>How much over the odds a club will go for a player it needs.</summary>
    private static float Need(SeasonState season, int teamId, PlayerData p)
    {
        var roster = season.RosterFor(teamId);
        var atSpot = roster.Players
            .Where(x => x.Position == p.Position && x.Position != Data.Position.P)
            .ToList();

        if (p.Position == Data.Position.P)
        {
            int arms = roster.Players.Count(x => x.Position == Data.Position.P);
            if (arms < Development.StaffSize - 1) return 1.22f;
            return 0.98f;
        }

        if (atSpot.Count == 0) return 1.25f;
        return atSpot.Max(x => x.Overall) < p.Overall ? 1.12f : 0.94f;
    }

    /// <summary>
    /// Nobody opens the season a man short. Any club still under the limit signs whoever is left,
    /// and reaches into its own farm system before it settles for a stranger.
    /// </summary>
    private static void FillShortRosters(SeasonState season, ref Rng rng, List<string> news)
    {
        foreach (var t in Teams.All)
        {
            var roster = season.RosterFor(t.Id);
            int guard = 0;

            while (roster.Players.Count < Development.RosterLimit && guard++ < 40)
            {
                bool needArm = roster.Players.Count(p => p.Position == Data.Position.P)
                               < Development.StaffSize;

                var pick = season.FreeAgents
                    .Where(p => (p.Position == Data.Position.P) == needArm)
                    .OrderByDescending(p => p.Overall)
                    .FirstOrDefault(p => p.Salary <= Mathf.Max(Contracts.Minimum,
                        Finances.SpaceFor(season, t.Id)));

                if (pick != null)
                {
                    pick.IsFreeAgent = false;
                    pick.ContractYears = 1;
                    pick.Salary = Contracts.SalaryFor(pick);
                    season.FreeAgents.Remove(pick);
                    Add(season, t.Id, pick);
                    continue;
                }

                // Reach into the farm rather than conjuring a stranger out of nowhere.
                var prospect = Farm.Of(t.Id)
                    .Where(p => (p.Position == Data.Position.P) == needArm)
                    .OrderByDescending(p => p.Overall)
                    .FirstOrDefault();

                if (prospect != null && Farm.CallUp(season, t.Id, prospect))
                {
                    news.Add($"{t.Abbrev} call up {prospect.Name} to fill out the roster.");
                    continue;
                }

                var signing = RosterGenerator.Prospect(
                    400000 + t.Id * 1000 + season.Year * 53 + guard, ref rng,
                    needArm ? Data.Position.P : null);
                signing.Salary = Contracts.Minimum;
                signing.ContractYears = 1;
                Add(season, t.Id, signing);
            }
        }
    }

    private static void Cut(SeasonState season, int teamId, PlayerData p)
    {
        var roster = season.RosterFor(teamId);
        roster.Players.Remove(p);
        roster.Pitchers.Remove(p);
        roster.BattingOrder.Remove(p);
        foreach (var spot in roster.Starters.Where(s => s.Value == p).Select(s => s.Key).ToList())
            roster.Starters.Remove(spot);
        TradeEngine.Rebuild(roster);
    }

    private static void Add(SeasonState season, int teamId, PlayerData p)
    {
        var roster = season.RosterFor(teamId);
        roster.Players.Add(p);
        if (p.Position == Data.Position.P) roster.Pitchers.Add(p);
        p.IsFreeAgent = false;
        TradeEngine.Rebuild(roster);
    }

    /// <summary>
    /// The user signing a free agent himself. Returns why not, or null if the deal is done.
    /// </summary>
    public static string UserSign(SeasonState season, PlayerData p, int salary, int years)
    {
        if (!season.FreeAgents.Contains(p)) return "He's already signed somewhere.";

        var roster = season.RosterFor(season.UserTeamId);
        if (roster.Players.Count >= Development.RosterLimit)
            return $"Your roster is full at {Development.RosterLimit}. Someone has to go first.";

        int asking = Contracts.MarketValue(p);
        if (salary < asking * 0.82f)
            return $"He wants about {Contracts.Text(asking)} a year. That offer won't get a meeting.";

        if (salary > Finances.SpaceFor(season, season.UserTeamId))
            return $"You only have {Contracts.Text(Finances.SpaceFor(season, season.UserTeamId))} " +
                   "of room under the budget.";

        p.Salary = salary;
        p.ContractYears = Mathf.Clamp(years, 1, 6);
        season.FreeAgents.Remove(p);
        Add(season, season.UserTeamId, p);
        return null;
    }
}
