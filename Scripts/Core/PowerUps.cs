using System.Collections.Generic;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Core;

/// <summary>
/// Tracks each player's signature move as a limited charge for the current game, in the
/// Backyard Baseball tradition: a kid's special is something you spend at the right moment,
/// not a passive bonus that quietly applies all game.
/// </summary>
public sealed class PowerUpLedger
{
    private readonly Dictionary<PlayerData, int> _used = new();

    public void Reset() => _used.Clear();

    public int Remaining(PlayerData p)
    {
        if (p == null || p.Special == Special.None) return 0;
        _used.TryGetValue(p, out int spent);
        return Godot.Mathf.Max(0, p.PowerUpsPerGame - spent);
    }

    public bool Available(PlayerData p) => Remaining(p) > 0;

    /// <summary>Spends a charge. Returns false when the player has none left.</summary>
    public bool Spend(PlayerData p)
    {
        if (!Available(p)) return false;
        _used.TryGetValue(p, out int spent);
        _used[p] = spent + 1;
        return true;
    }

    /// <summary>What using it does, in words, for the on-screen prompt.</summary>
    public static string Describe(Special special) => special switch
    {
        Special.Fireball => "Unhittable heat",
        Special.CrazyCurve => "Curve falls off the table",
        Special.Corkscrew => "Slider changes direction twice",
        Special.Knuckleball => "Nobody knows where it goes",
        Special.Heatseeker => "Fastball that keeps rising",
        Special.IceVeins => "Paints the corner",
        Special.MoonShot => "Swing for the moon",
        Special.ContactMaster => "Cannot be missed",
        Special.BuntMaster => "Perfect bunt",
        Special.SprayHitter => "Finds the gap",
        Special.GapPower => "Scorched into the alley",
        Special.TurboLegs => "Turbo out of the box",
        Special.PinchRunner => "Huge jump",
        Special.VacuumGlove => "Vacuum glove",
        Special.CannonArm => "Cannon arm",
        Special.WallClimber => "Robs it at the wall",
        Special.Backstop => "Nothing gets past",
        _ => "",
    };

    /// <summary>True for specials that do something at the plate.</summary>
    public static bool IsHitting(Special s) => s is Special.MoonShot or Special.ContactMaster
        or Special.BuntMaster or Special.SprayHitter or Special.GapPower;

    /// <summary>True for specials that do something on the mound.</summary>
    public static bool IsPitching(Special s) => s is Special.Fireball or Special.CrazyCurve
        or Special.Corkscrew or Special.Knuckleball or Special.Heatseeker or Special.IceVeins;
}
