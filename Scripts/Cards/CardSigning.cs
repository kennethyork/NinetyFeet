using System.Linq;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.Season;

namespace SandlotSlugfest.Cards;

/// <summary>
/// Playing a card into a running franchise.
///
/// The obvious implementation is to copy the player onto your club, and it is wrong: every card is
/// of somebody who really plays somewhere in your league, so a copy puts the same man on two
/// rosters at once. The league's whole promise is that there is exactly one of everybody — it is
/// why names and numbers are unique and why a trade means something.
///
/// So a card is a transfer, not a copy. Play it and he leaves the club he was on and joins yours;
/// that club signs a replacement so it is not left a man short; and the card is spent. He arrives
/// on his real contract, so the front office has to be able to afford him, which is what stops the
/// collection being a way to buy a superteam for nothing.
/// </summary>
public static class CardSigning
{
    /// <summary>Whether a card can be played into the user's club, and why not if it cannot.</summary>
    public static string Refusal(SeasonState season, Card card)
    {
        if (season == null) return "No season in progress. Start one first.";
        if (card == null || !Collection.Has(card.Player.Id)) return "You don't own that card.";

        var mine = season.RosterFor(season.UserTeamId);
        if (mine.Players.Contains(card.Player))
            return $"{card.Player.Name} already plays for you.";

        if (mine.Players.Count >= Development.RosterLimit)
            return $"Your roster is full at {Development.RosterLimit}. Release or option somebody first.";

        int space = Finances.SpaceFor(season, season.UserTeamId);
        if (card.Player.Salary > space)
            return $"{card.Player.Name} earns {Contracts.Text(card.Player.Salary)} and you have " +
                   $"{Contracts.Text(space)} of room.";

        return null;
    }

    /// <summary>
    /// Carries out the transfer. Returns the headline for the news feed, or null if it was refused.
    /// </summary>
    public static string Play(SeasonState season, Card card)
    {
        if (Refusal(season, card) != null) return null;

        var player = card.Player;
        var from = season.TeamOf(player);
        var mine = season.RosterFor(season.UserTeamId);

        if (from != null)
        {
            var old = season.RosterFor(from.Id);
            old.Players.Remove(player);
            old.Pitchers.Remove(player);
            old.BattingOrder.Remove(player);
            foreach (var slot in old.Starters.Where(s => s.Value == player)
                                             .Select(s => s.Key).ToList())
                old.Starters.Remove(slot);

            // His old club does not simply play a man short. It reaches into its own farm first,
            // which is what a farm system is for, and signs somebody only if it has to.
            bool wasArm = player.Position == Data.Position.P;
            var promoted = Farm.Of(from.Id)
                .Where(p => (p.Position == Data.Position.P) == wasArm)
                .OrderByDescending(p => p.Overall)
                .FirstOrDefault();

            if (promoted == null || !Farm.CallUp(season, from.Id, promoted))
            {
                var rng = new Rng(player.Id * 977 + season.Year);
                var replacement = RosterGenerator.Prospect(
                    500000 + player.Id, ref rng, wasArm ? Data.Position.P : null);
                replacement.Salary = Contracts.Minimum;
                replacement.ContractYears = 1;
                old.Players.Add(replacement);
                if (wasArm) old.Pitchers.Add(replacement);
            }

            TradeEngine.Rebuild(old);
        }

        mine.Players.Add(player);
        if (player.Position == Data.Position.P) mine.Pitchers.Add(player);
        TradeEngine.Rebuild(mine);

        // The card is spent. It bought him once.
        Collection.Remove(player.Id);
        Collection.Save();

        string where = from == null ? "free agency" : from.FullName;
        return $"{player.Name} joins {Teams.Get(season.UserTeamId).Abbrev} from {where} " +
               $"at {Contracts.Text(player.Salary)}.";
    }
}
