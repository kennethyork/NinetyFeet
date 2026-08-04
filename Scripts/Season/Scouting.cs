using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>
/// What your scouts think a player will become, which is not the same as what he will become.
///
/// A ceiling you can simply read is not a decision. If the number on the card is the truth, then
/// picking between two prospects is arithmetic, there is no such thing as a bust or a sleeper, and
/// a farm system is a spreadsheet you sort. Every game worth playing hides this, and the hiding is
/// most of what makes the draft and the farm interesting.
///
/// The error here is deterministic in the player, so a man does not flicker between readings — the
/// scouts have an opinion of him and they hold it. And it narrows as he climbs: a nineteen-year-old
/// in High-A could be anything, and by the time he is in Triple-A you mostly know. That shrinking
/// is the whole shape of the thing. It rewards waiting, and it punishes trading on a number.
/// </summary>
public static class Scouting
{
    /// <summary>How well a player is known, which depends on how close to the majors he is.</summary>
    public enum Confidence { Raw, Developing, Polished, Known }

    /// <summary>How many rating points the scouts can be out by, at each remove.</summary>
    private static int Spread(Confidence c) => c switch
    {
        Confidence.Raw => 3,
        Confidence.Developing => 2,
        Confidence.Polished => 1,
        _ => 0,
    };

    public static string Label(Confidence c) => c switch
    {
        Confidence.Raw => "raw",
        Confidence.Developing => "developing",
        Confidence.Polished => "close",
        _ => "known",
    };

    /// <summary>
    /// How well this club knows one of its own. A man on the big club has nothing left to guess
    /// at — you watch him every day.
    /// </summary>
    public static Confidence For(int teamId, PlayerData p)
    {
        var level = Farm.LevelOf(teamId, p);
        if (level == null) return Confidence.Known;

        return level switch
        {
            Farm.Level.TripleA => Confidence.Polished,
            Farm.Level.DoubleA => Confidence.Developing,
            _ => Confidence.Raw,
        };
    }

    /// <summary>A draft prospect nobody has seen play a professional game.</summary>
    public const Confidence Undrafted = Confidence.Raw;

    /// <summary>
    /// The scouts' own opinion of a ceiling — the true one, moved by an error that belongs to this
    /// player and never changes. Some men are consistently overrated and some are missed entirely,
    /// which is exactly what a draft needs to be worth holding.
    /// </summary>
    public static int Estimate(PlayerData p, Confidence c)
    {
        int spread = Spread(c);
        if (spread == 0) return p.Ceiling;

        // Bell-shaped, so most reads are close and the howler is rare.
        var rng = new Rng(p.Id * 2749 + 8191);
        float roll = (rng.Bell() - 0.5f) * 2f;                 // -1 .. +1, middle-heavy
        return Mathf.Clamp(p.Ceiling + Mathf.RoundToInt(roll * spread), 1, 10);
    }

    /// <summary>The band a scout will actually commit to, rather than a single number.</summary>
    public static (int Low, int High) Band(PlayerData p, Confidence c)
    {
        int estimate = Estimate(p, c);
        int half = Mathf.Max(0, Spread(c) - 1);
        return (Mathf.Clamp(estimate - half, 1, 10), Mathf.Clamp(estimate + half, 1, 10));
    }

    /// <summary>Scout's shorthand for a ceiling, as a grade rather than a number.</summary>
    private static string GradeOf(int ceiling) => ceiling switch
    {
        >= 9 => "Superstar",
        >= 8 => "All-Star",
        >= 7 => "Everyday starter",
        >= 6 => "Solid regular",
        >= 5 => "Bench piece",
        _ => "Organisational",
    };

    /// <summary>
    /// What the farm screen and the draft board print. A known player gets a flat grade; anyone
    /// further out gets the range his scouts will stand behind, which is the honest answer.
    /// </summary>
    public static string Report(PlayerData p, Confidence c)
    {
        if (c == Confidence.Known) return GradeOf(p.Ceiling);

        var (low, high) = Band(p, c);
        string lowGrade = GradeOf(low);
        string highGrade = GradeOf(high);

        return lowGrade == highGrade ? $"{lowGrade}?" : $"{lowGrade} – {highGrade}";
    }

    /// <summary>Convenience for a club looking at its own organisation.</summary>
    public static string Report(int teamId, PlayerData p) => Report(p, For(teamId, p));
}
