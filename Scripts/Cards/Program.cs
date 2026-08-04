using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SandlotSlugfest.Cards;

/// <summary>
/// The reward program: packs you earn rather than buy.
///
/// A collection mode where the only way to get a pack is to pay for it out of coins is a shop, and
/// a shop stops being interesting the moment you can afford everything in it. The games that do
/// this well — The Show's programs, OOTP's Perfect Team — pay you in packs for turning up and for
/// doing specific things, so there is always a reason to play one more.
///
/// Three ways to be paid, deliberately different in shape:
///
///   The ladder rewards volume. Every game adds experience and the rungs come further apart, so it
///   is a slow, dependable drip that never dries up.
///
///   The missions reward doing something particular — winning a shutout, scoring ten, building a
///   full nine. They are one-offs, and they are the reason to try something other than what you
///   were already doing.
///
///   The daily rewards coming back tomorrow, which is the only one of the three that cannot be
///   rushed.
///
/// Everything here pays in packs and coins that already exist. Nothing costs real money and
/// nothing ever will.
/// </summary>
public static class Program
{
    // -----------------------------------------------------------------------
    // The ladder
    // -----------------------------------------------------------------------

    public sealed class Rung
    {
        public int Xp;
        public string Name;

        /// <summary>Index into <see cref="Market.Packs"/>, or -1 for a coin reward.</summary>
        public int Pack = -1;
        public int Coins;
    }

    /// <summary>
    /// The rungs, and what each one pays.
    ///
    /// The gaps widen as you climb and the rewards climb with them, so the early ones arrive fast
    /// enough to feel like the program is real and the late ones are worth actually chasing.
    /// </summary>
    public static readonly Rung[] Ladder =
    {
        new() { Xp =    250, Name = "CALLED UP",      Pack = 0 },
        new() { Xp =    650, Name = "EVERYDAY",       Coins = 1200 },
        new() { Xp =  1_200, Name = "REGULAR",        Pack = 0 },
        new() { Xp =  2_000, Name = "ALL-STAR BREAK", Pack = 1 },
        new() { Xp =  3_100, Name = "SECOND HALF",    Coins = 3500 },
        new() { Xp =  4_600, Name = "PENNANT RACE",   Pack = 1 },
        new() { Xp =  6_600, Name = "OCTOBER",        Pack = 3 },
        new() { Xp =  9_200, Name = "PENNANT",        Coins = 9000 },
        new() { Xp = 12_500, Name = "THE SERIES",     Pack = 2 },
        new() { Xp = 17_000, Name = "COOPERSTOWN",    Pack = 4 },
    };

    /// <summary>Where the ladder ends, for the bar on the screen.</summary>
    public static int Summit => Ladder[^1].Xp;

    /// <summary>The next rung not yet reached, or null once the ladder is finished.</summary>
    public static Rung Next => Ladder.FirstOrDefault(r => r.Xp > Collection.Xp);

    /// <summary>
    /// What a finished game is worth in experience.
    ///
    /// Losing still pays. A program that only advances when you win stalls exactly when a player
    /// most needs a reason to keep going, and the whole point of the ladder is that it never stops
    /// moving. Runs scored pay a little on top so a blowout feels different from a squeaker.
    /// </summary>
    public static int XpForGame(bool won, int runsFor, int runsAgainst) =>
        (won ? 120 : 55) + runsFor * 6 + Mathf.Max(0, 6 - runsAgainst) * 4;

    /// <summary>
    /// Adds experience and hands over everything the new total has earned.
    ///
    /// Rungs are paid out here rather than claimed on the screen, because a reward you have to
    /// remember to collect is a reward that sits there uncollected.
    /// </summary>
    public static List<string> AddXp(int xp)
    {
        var earned = new List<string>();
        if (xp <= 0) return earned;

        int before = Collection.Xp;
        Collection.AddXp(xp);

        foreach (var rung in Ladder.Where(r => r.Xp > before && r.Xp <= Collection.Xp))
            earned.Add(Pay(rung.Pack, rung.Coins, rung.Name));

        return earned;
    }

    // -----------------------------------------------------------------------
    // Missions
    // -----------------------------------------------------------------------

    public sealed class Mission
    {
        public string Key;
        public string Name;
        public string Detail;
        public int Target;
        public int Pack = -1;
        public int Coins;

        /// <summary>How far along this is. Some count games; some read the collection directly.</summary>
        public System.Func<int> Progress;
    }

    public static readonly Mission[] Missions =
    {
        new()
        {
            Key = "games", Name = "GET SOME INNINGS IN", Target = 5, Pack = 0,
            Detail = "Play five games with your collected club.",
            Progress = () => Collection.Counter("games"),
        },
        new()
        {
            Key = "wins", Name = "A WINNING SIDE", Target = 10, Pack = 1,
            Detail = "Win ten games.",
            Progress = () => Collection.Counter("wins"),
        },
        new()
        {
            Key = "ten", Name = "TOUCH 'EM ALL", Target = 1, Coins = 2500,
            Detail = "Score ten runs in a game.",
            Progress = () => Collection.Counter("ten"),
        },
        new()
        {
            Key = "shutout", Name = "NOTHING DOING", Target = 1, Pack = 1,
            Detail = "Win one without letting them score.",
            Progress = () => Collection.Counter("shutout"),
        },
        new()
        {
            Key = "nine", Name = "A FULL NINE", Target = 1, Coins = 1500,
            Detail = "Fill every lineup slot and sign at least one arm.",
            Progress = () => Collection.ClubIsReady ? 1 : 0,
        },
        new()
        {
            Key = "fifty", Name = "A REAL COLLECTION", Target = 50, Pack = 1,
            Detail = "Own fifty cards.",
            Progress = () => Collection.Size,
        },
        new()
        {
            Key = "diamond", Name = "FOUND ONE", Target = 1, Pack = 3,
            Detail = "Own a diamond card.",
            Progress = () => Collection.Mine.Any(c => c.Tier == Tier.Diamond) ? 1 : 0,
        },
        new()
        {
            Key = "legends", Name = "THE NAMES YOU KNOW", Target = 8, Pack = 2,
            Detail = "Own eight of the league's written players.",
            Progress = () => Collection.Mine.Count(c => c.Player.IsLegend),
        },
    };

    public static bool Complete(Mission m) => m.Progress() >= m.Target;
    public static bool Claimed(Mission m) => Collection.MissionClaimed(m.Key);

    /// <summary>Hands over a finished mission's reward, once.</summary>
    public static string Claim(Mission m)
    {
        if (!Complete(m)) return null;
        if (Claimed(m)) return null;

        Collection.ClaimMission(m.Key);
        return Pay(m.Pack, m.Coins, m.Name);
    }

    /// <summary>Missions finished but not yet collected, for the badge on the tab.</summary>
    public static int Unclaimed => Missions.Count(m => Complete(m) && !Claimed(m));

    // -----------------------------------------------------------------------
    // Turning up
    // -----------------------------------------------------------------------

    /// <summary>Days since the epoch, so "today" means the same thing all day.</summary>
    private static int Today => (int)(Time.GetUnixTimeFromSystem() / 86400.0);

    public static bool DailyReady => Collection.LastDaily != Today;

    /// <summary>The free pack for coming back. One a day, and it does not stockpile.</summary>
    public static string ClaimDaily()
    {
        if (!DailyReady) return null;

        Collection.SetLastDaily(Today);
        return Pay(0, 250, "TODAY'S PACK");
    }

    // -----------------------------------------------------------------------

    /// <summary>Puts a reward where it belongs and says what it was.</summary>
    private static string Pay(int pack, int coins, string what)
    {
        if (pack >= 0 && pack < Market.Packs.Length)
        {
            Collection.Stash(pack);
            return $"{what} — {Market.Packs[pack].Name} earned. It's waiting in PACKS.";
        }

        Collection.Earn(coins);
        return $"{what} — {Market.Coins(coins)} earned.";
    }

    /// <summary>
    /// Books a finished game against the program. Called for a collection game and for a season or
    /// dynasty game alike — playing the game is playing the game, and a mode that only counted one
    /// of them would quietly punish you for running a franchise.
    /// </summary>
    public static List<string> BookGame(bool won, int runsFor, int runsAgainst)
    {
        Collection.Bump("games");
        if (won) Collection.Bump("wins");
        if (runsFor >= 10) Collection.Bump("ten");
        if (won && runsAgainst == 0) Collection.Bump("shutout");

        return AddXp(XpForGame(won, runsFor, runsAgainst));
    }
}
