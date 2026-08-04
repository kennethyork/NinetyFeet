using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Cards;

/// <summary>How good a card is, at a glance. The whole collection is read through this.</summary>
public enum Tier { Common, Bronze, Silver, Gold, Diamond }

/// <summary>
/// A player as a collectable.
///
/// A card is not a copy of a player — it is a reference to one, plus the things that only matter
/// because he is a card: what tier he sits in, what he is worth, and how many of him you own. The
/// player himself still comes from the league, so a card of Marcus Okafor is the same Marcus
/// Okafor you would face in a season, with the same ratings, the same face and the same bio.
/// </summary>
public sealed class Card
{
    /// <summary>The player this card is of. Stable across saves through his id.</summary>
    public PlayerData Player;

    /// <summary>The club he is pictured with, which is where his colours come from.</summary>
    public int TeamId;

    public Tier Tier => TierOf(Player);

    public static Tier TierOf(PlayerData p) => p.Overall switch
    {
        >= 9 => Tier.Diamond,
        >= 8 => Tier.Gold,
        >= 6 => Tier.Silver,
        >= 4 => Tier.Bronze,
        _ => Tier.Common,
    };

    /// <summary>
    /// What he sells for. Steep at the top on purpose — the gap between a gold and a diamond is
    /// most of a collection, which is what makes pulling one out of a pack matter.
    /// </summary>
    public static int ValueOf(PlayerData p)
    {
        int baseValue = TierOf(p) switch
        {
            Tier.Diamond => 14000,
            Tier.Gold => 3800,
            Tier.Silver => 700,
            Tier.Bronze => 140,
            _ => 40,
        };

        // Inside a tier, the difference between a nine and a ten is real money, and a written
        // player is worth more than a generated one of the same rating — people want the names.
        float within = 1f + (p.Overall - TierFloor(TierOf(p))) * 0.34f;
        float named = p.IsLegend ? 1.45f : 1f;

        // A young player with room to grow is worth more than an old one who is what he is.
        float age = p.Age <= 24 ? 1.20f : p.Age <= 29 ? 1.05f : p.Age <= 33 ? 0.92f : 0.76f;

        return Mathf.Max(25, Mathf.RoundToInt(baseValue * within * named * age / 5f) * 5);
    }

    private static int TierFloor(Tier tier) => tier switch
    {
        Tier.Diamond => 9, Tier.Gold => 8, Tier.Silver => 6, Tier.Bronze => 4, _ => 1,
    };

    public int Value => ValueOf(Player);

    public static Color ColourOf(Tier tier) => tier switch
    {
        Tier.Diamond => new Color("#7fe3ff"),
        Tier.Gold => new Color("#e8c14a"),
        Tier.Silver => new Color("#c6ccd4"),
        Tier.Bronze => new Color("#c07a42"),
        _ => new Color("#8a8f96"),
    };

    public Color Colour => ColourOf(Tier);

    public static string Label(Tier tier) => tier switch
    {
        Tier.Diamond => "DIAMOND",
        Tier.Gold => "GOLD",
        Tier.Silver => "SILVER",
        Tier.Bronze => "BRONZE",
        _ => "COMMON",
    };

    public string TierText => Label(Tier);
}
