using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Cards;

/// <summary>What came out of a pack, so the screen can show it being opened.</summary>
public sealed class PackResult
{
    public readonly List<Card> Cards = new();
    public Card Best => Cards.OrderByDescending(c => c.Value).FirstOrDefault();
}

/// <summary>
/// Packs and the trading post.
///
/// The pull rates are stated openly on the screen, because a collection mode where the odds are
/// hidden is a slot machine wearing a baseball cap. A diamond is rare enough to be an event and
/// common enough that chasing one is not hopeless.
///
/// Selling pays less than buying costs. That spread is the only thing stopping a collection from
/// growing forever out of its own duplicates, and it is what makes a card you actually want worth
/// keeping rather than flipping.
/// </summary>
public static class Market
{
    public sealed class PackKind
    {
        public string Name;
        public string Blurb;
        public int Price;
        public int Cards;

        /// <summary>Chance of the best card in the pack being at least each tier, richest first.</summary>
        public float DiamondChance;
        public float GoldChance;
        public float SilverChance;

        /// <summary>Draw only from the league's handwritten players.</summary>
        public bool LegendsOnly;
    }

    public static readonly PackKind[] Packs =
    {
        new()
        {
            Name = "STANDARD PACK", Price = 1500, Cards = 5,
            Blurb = "Five cards. Something silver or better, usually.",
            DiamondChance = 0.012f, GoldChance = 0.09f, SilverChance = 0.55f,
        },
        new()
        {
            Name = "PREMIUM PACK", Price = 6000, Cards = 5,
            Blurb = "Five cards, at least one gold, and a real shot at a diamond.",
            DiamondChance = 0.075f, GoldChance = 1f, SilverChance = 1f,
        },
        new()
        {
            Name = "DIAMOND HUNT", Price = 22000, Cards = 3,
            Blurb = "Three cards. One of them is a diamond one time in three.",
            DiamondChance = 0.34f, GoldChance = 1f, SilverChance = 1f,
        },
        new()
        {
            Name = "PROSPECT PACK", Price = 600, Cards = 3,
            Blurb = "Three cards, cheap. How a collection starts.",
            DiamondChance = 0.004f, GoldChance = 0.04f, SilverChance = 0.30f,
        },
        new()
        {
            // The written players are the ones with names you recognise, and a pack that hunts
            // only them is a different chase from a pack that hunts ratings.
            Name = "LEGENDS PACK", Price = 14000, Cards = 4,
            Blurb = "Four of the league's written names. Nobody generated.",
            DiamondChance = 0.12f, GoldChance = 1f, SilverChance = 1f,
            LegendsOnly = true,
        },
    };

    /// <summary>Seeded from the clock, because a pack that always contains the same five cards
    /// is not a pack.</summary>
    private static Rng _rng = new(System.Environment.TickCount);

    /// <summary>
    /// Opens a pack. The best card is rolled first against the stated odds and the rest are filled
    /// in beneath it, which is what makes the odds on the screen mean what they say — rolling each
    /// card independently would quietly make a five-card pack five times likelier to hit.
    /// </summary>
    public static PackResult Open(PackKind pack)
    {
        var result = new PackResult();

        Tier best =
            _rng.Chance(pack.DiamondChance) ? Tier.Diamond
            : _rng.Chance(pack.GoldChance) ? Tier.Gold
            : _rng.Chance(pack.SilverChance) ? Tier.Silver
            : Tier.Bronze;

        var headline = Draw(best, pack.LegendsOnly);
        if (headline != null) result.Cards.Add(headline);

        for (int i = result.Cards.Count; i < pack.Cards; i++)
        {
            // The rest of the pack is filler, weighted low. A pack is one card you wanted and
            // four you will sell, which is the shape of every pack anyone has ever opened.
            Tier tier = _rng.NextFloat() switch
            {
                < 0.55f => Tier.Bronze,
                < 0.88f => Tier.Silver,
                < 0.985f => Tier.Gold,
                _ => Tier.Diamond,
            };

            var filler = Draw(tier, pack.LegendsOnly);
            if (filler != null) result.Cards.Add(filler);
        }

        foreach (var c in result.Cards) Collection.Add(c);
        return result;
    }

    /// <summary>Opens a pack that was earned rather than bought, spending it from the vault.</summary>
    public static PackResult OpenEarned(int packIndex)
    {
        if (packIndex < 0 || packIndex >= Packs.Length) return null;
        if (!Collection.TakeFromVault(packIndex)) return null;

        return Open(Packs[packIndex]);
    }

    /// <summary>One random card of a given tier, or the nearest tier that has anyone in it.</summary>
    private static Card Draw(Tier tier, bool legendsOnly = false)
    {
        for (int step = 0; step < 5; step++)
        {
            // Walk down if the league happens to have nobody at that tier.
            var at = (Tier)Mathf.Max(0, (int)tier - step);
            var pool = Collection.Catalogue
                .Where(c => c.Tier == at && (!legendsOnly || c.Player.IsLegend))
                .ToList();
            if (pool.Count > 0) return pool[_rng.Range(0, pool.Count)];
        }

        // A legends pack that has run the written players dry still owes you a card.
        return legendsOnly ? Draw(tier) : null;
    }

    /// <summary>What the market pays for a card. Less than it charges, and that gap is the point.</summary>
    public static int SellPrice(Card card) => Mathf.RoundToInt(card.Value * 0.62f / 5f) * 5;

    /// <summary>What the market charges. A premium over the card's worth, so packs stay worth opening.</summary>
    public static int BuyPrice(Card card) => Mathf.RoundToInt(card.Value * 1.18f / 5f) * 5;

    public static bool Sell(Card card)
    {
        if (card == null || !Collection.Has(card.Player.Id)) return false;
        if (!Collection.Remove(card.Player.Id)) return false;

        Collection.Earn(SellPrice(card));
        return true;
    }

    public static string Buy(Card card)
    {
        if (card == null) return "No card selected.";

        int price = BuyPrice(card);
        if (!Collection.Spend(price))
            return $"That costs {Coins(price)} and you have {Coins(Collection.Coins)}.";

        Collection.Add(card);
        return null;
    }

    /// <summary>
    /// What is on the block. Not the whole league — a handful of names that change with the day,
    /// so the market is something you check rather than a catalogue you shop.
    /// </summary>
    public static List<Card> Listings(int seed, int count = 18)
    {
        var rng = new Rng(seed * 7919 + 13);
        var pool = Collection.Catalogue.Where(c => c.Tier >= Tier.Silver).ToList();
        var listed = new List<Card>();

        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            int at = rng.Range(0, pool.Count);
            listed.Add(pool[at]);
            pool.RemoveAt(at);
        }

        return listed.OrderByDescending(c => c.Value).ToList();
    }

    /// <summary>Coins, written the way the screen shows them.</summary>
    public static string Coins(int amount) => $"{amount:N0}c";

    /// <summary>
    /// What a game is worth. Winning pays properly, losing still pays something — a collection
    /// mode that only rewards winning stops being playable the moment you are outmatched.
    /// </summary>
    public static int Purse(bool won, int runsFor, int runsAgainst) =>
        (won ? 900 : 350) + runsFor * 45 + Mathf.Max(0, 10 - runsAgainst) * 20;
}
