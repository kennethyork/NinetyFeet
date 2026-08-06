using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SandlotSlugfest.Data;

/// <summary>
/// The 32-club league. Every club is an original creation placed in a real
/// major-league market: all 30 current markets plus two expansion cities
/// (Montreal and Nashville). Names, colours and logos are deliberately our own —
/// no real club marks are used.
/// </summary>
public static class Teams
{
    private static TeamData[] _all;
    private static TeamData[] _active;
    private static (string Abbrev, string FullName)[] _shipped;

    /// <summary>
    /// How many clubs the league has, of the thirty-two that exist.
    ///
    /// A league is two leagues of two divisions, so this has to divide by four — the subset is
    /// taken evenly from each division rather than off the top of the list, or a sixteen-club
    /// league would be the whole American League playing itself.
    ///
    /// Clubs keep the identifiers they shipped with. A sixteen-club league is literally sixteen of
    /// these thirty-two, with the same ids, the same ballparks and the same written players they
    /// would have had in a full one — so nothing keyed by club id means something different at a
    /// different size: not a save, not the club editor, not a roster file, not a rebuilt ground.
    /// Renumbering the survivors 0 to 15 would have been fractionally simpler here and would have
    /// quietly changed which club every one of those files was talking about.
    /// </summary>
    public const int ShippedCount = 32;

    public static readonly int[] Sizes = { 8, 12, 16, 20, 24, 28, 32 };

    private static int _size = ShippedCount;

    public static int ActiveCount
    {
        get => _size;
        set
        {
            int wanted = Mathf.Clamp(value - value % 4, 8, ShippedCount);
            if (wanted == _size) return;
            _size = wanted;
            _active = null;
            _byId = null;
        }
    }

    /// <summary>Every club that exists, whether or not it is in the league this season.</summary>
    public static IReadOnlyList<TeamData> Shipped
    {
        get
        {
            if (_all != null) return _all;

            // Built first, then anything the player has renamed or recoloured is laid over the
            // top. The built-in list stays the source of truth, so a club can always be put back.
            _all = Build();

            // What each club shipped as, kept before the overrides go on. A file written against
            // the original names — the roster template prints them — has to keep working after its
            // author renames the clubs, which is the order most people will do the two jobs in.
            _shipped = System.Array.ConvertAll(_all, t => (t.Abbrev, t.FullName));

            TeamEdits.ApplyAll();
            return _all;
        }
    }

    /// <summary>The clubs actually playing this season.</summary>
    public static IReadOnlyList<TeamData> All
    {
        get
        {
            if (_active != null) return _active;

            var shipped = Shipped;
            if (_size >= shipped.Count) return _active = _all;

            // Evenly from each division, in the order they ship, so the four divisions stay the
            // same size as one another and every one of them keeps its own markets.
            int per = _size / 4;
            var picked = new List<TeamData>();

            foreach (var league in new[] { League.American, League.National })
                foreach (var division in new[] { Division.East, Division.West })
                    picked.AddRange(shipped
                        .Where(t => t.League == league && t.Division == division)
                        .OrderBy(t => t.Id)
                        .Take(per));

            return _active = picked.OrderBy(t => t.Id).ToArray();
        }
    }

    /// <summary>
    /// Throws the clubs away so they are built again from source. Used when an edit is undone —
    /// the overrides are applied over the originals rather than into them, so the only way back
    /// is to start from the originals.
    /// </summary>
    public static void Rebuild()
    {
        _all = null;
        _active = null;
        _byId = null;
        _ = All;
    }

    private static Dictionary<int, TeamData> _byId;

    /// <summary>
    /// A club by its identifier, which is no longer its position in the list.
    ///
    /// It was, while every league had all thirty-two. A smaller league holds the same ids it
    /// shipped with — 0 to 3 and 8 to 11 and so on — so indexing the list by id would hand back
    /// the wrong club, or walk off the end of it. The map is built from the shipped list rather
    /// than the active one, because plenty of things legitimately ask about a club that is not in
    /// this season's league: an old save, a written player's home, a stadium file.
    /// </summary>
    public static TeamData Get(int id)
    {
        _byId ??= Shipped.ToDictionary(t => t.Id);
        return _byId.TryGetValue(id, out var t) ? t : Shipped[Mathf.Clamp(id, 0, Shipped.Count - 1)];
    }

    /// <summary>Whether a club is in this season's league at all.</summary>
    public static bool InLeague(int id) => All.Any(t => t.Id == id);

    /// <summary>Where a club sits in this season's league, for screens that page through it.</summary>
    public static int IndexOf(int id)
    {
        for (int i = 0; i < All.Count; i++) if (All[i].Id == id) return i;
        return -1;
    }

    /// <summary>The club this many places along from the given one, wrapping.</summary>
    public static TeamData Step(int fromId, int by)
    {
        int at = IndexOf(fromId);
        if (at < 0) return All[0];
        return All[Mathf.PosMod(at + by, All.Count)];
    }

    /// <summary>The abbreviation and name a club shipped with, whatever it has since been called.</summary>
    public static string OriginalAbbrev(int id)
    {
        _ = All;
        return id >= 0 && _shipped != null && id < _shipped.Length ? _shipped[id].Abbrev : null;
    }

    public static string OriginalName(int id)
    {
        _ = All;
        return id >= 0 && _shipped != null && id < _shipped.Length ? _shipped[id].FullName : null;
    }

    public static IEnumerable<TeamData> In(League league, Division division) =>
        All.Where(t => t.League == league && t.Division == division);

    public static IEnumerable<TeamData> In(League league) => All.Where(t => t.League == league);

    private static TeamData Make(
        int id, string city, string nickname, string abbrev,
        League league, Division division,
        string primary, string secondary, string motto,
        int power = 0, int speed = 0, int pitching = 0, int defense = 0) =>
        new()
        {
            Id = id,
            City = city,
            Nickname = nickname,
            Abbrev = abbrev,
            League = league,
            Division = division,
            Primary = new Color(primary),
            Secondary = new Color(secondary),
            Motto = motto,
            PowerBias = power,
            SpeedBias = speed,
            PitchingBias = pitching,
            DefenseBias = defense,
        };

    private static TeamData[] Build()
    {
        const League AL = League.American;
        const League NL = League.National;
        const Division E = Division.East;
        const Division W = Division.West;

        var teams = new[]
        {
            // ---------------------------------------------------------------
            // AMERICAN LEAGUE — EAST
            // ---------------------------------------------------------------
            Make(0,  "Baltimore",   "Blue Crabs",    "BAL", AL, E, "#2e5a88", "#e8641e",
                 "Pinch 'em before they pinch you.",          power: 0, speed: 1, pitching: 0, defense: 1),
            Make(1,  "Boston",      "Lobsters",      "BOS", AL, E, "#b8322b", "#f2e4c9",
                 "Claws up, bats hot.",                        power: 1, speed: 0, pitching: 1, defense: 0),
            Make(2,  "Bronx",       "Bombardiers",   "BRX", AL, E, "#1b2a44", "#c9cdd4",
                 "Everything they hit lands in another zip code.", power: 2, speed: -1, pitching: 1, defense: 0),
            Make(3,  "Tampa Bay",   "Thunderheads",  "TAM", AL, E, "#4a3f8c", "#f5d547",
                 "You hear them before you see them.",         power: 0, speed: 1, pitching: 2, defense: 1),
            Make(4,  "Toronto",     "Maple Bats",    "TOR", AL, E, "#c7392f", "#f4f4f0",
                 "Sweet swings, sticky finishes.",             power: 1, speed: 0, pitching: 0, defense: 1),
            Make(5,  "Montreal",    "Voyageurs",     "MTL", AL, E, "#1d4e89", "#e4572e",
                 "Paddling back into the league after all these years.", power: 0, speed: 2, pitching: 0, defense: 0),
            Make(6,  "Cleveland",   "Rockers",       "CLE", AL, E, "#3b2c6b", "#f0a830",
                 "Turn the fastball up to eleven.",            power: 0, speed: 0, pitching: 2, defense: 1),
            Make(7,  "Detroit",     "Motorheads",    "DET", AL, E, "#1f4e5f", "#d9531e",
                 "Built in the garage, tuned for the gap.",    power: 1, speed: 0, pitching: 1, defense: 0),

            // ---------------------------------------------------------------
            // AMERICAN LEAGUE — WEST
            // ---------------------------------------------------------------
            Make(8,  "South Side",  "Sluggers",      "SSS", AL, W, "#1a1a1a", "#b0b7bf",
                 "No frills. Just damage.",                    power: 2, speed: 0, pitching: 0, defense: -1),
            Make(9,  "Kansas City", "Smoke",         "KCS", AL, W, "#4b3a2f", "#f2a65a",
                 "Low and slow, then heat at the knees.",      power: 0, speed: 1, pitching: 1, defense: 1),
            Make(10, "Minnesota",   "Loons",         "MIN", AL, W, "#123f5e", "#e8edf2",
                 "Weird birds, wicked gloves.",                power: 0, speed: 0, pitching: 0, defense: 2),
            Make(11, "Houston",     "Moonshots",     "HOU", AL, W, "#2b2d6e", "#e6952a",
                 "We have liftoff, and it's still climbing.",  power: 2, speed: 0, pitching: 1, defense: 0),
            Make(12, "Anaheim",     "Angelfish",     "ANA", AL, W, "#147a8c", "#f2c14e",
                 "Slippery in the outfield, deadly in the box.", power: 1, speed: 1, pitching: 0, defense: 0),
            Make(13, "Oakland",     "Oaks",          "OAK", AL, W, "#2f5d3a", "#c9a227",
                 "Deep roots, deeper counts.",                 power: 0, speed: 0, pitching: 1, defense: 1),
            Make(14, "Seattle",     "Sasquatch",     "SEA", AL, W, "#22483d", "#8fbf6b",
                 "Big feet, bigger range.",                    power: 1, speed: -1, pitching: 2, defense: 1),
            Make(15, "Texas",       "Twisters",      "TEX", AL, W, "#6b7a8f", "#d64545",
                 "The wind does half the hitting.",            power: 2, speed: 1, pitching: -1, defense: 0),

            // ---------------------------------------------------------------
            // NATIONAL LEAGUE — EAST
            // ---------------------------------------------------------------
            Make(16, "Atlanta",     "Peaches",       "ATL", NL, E, "#e8874a", "#3e6b4a",
                 "Sweet now, bruising later.",                 power: 1, speed: 1, pitching: 1, defense: 0),
            Make(17, "Miami",       "Flamingos",     "MIA", NL, E, "#e85d9e", "#1fb3b3",
                 "One leg, no fear.",                          power: 0, speed: 2, pitching: 0, defense: 1),
            Make(18, "Queens",      "Apples",        "QNS", NL, E, "#2c6e49", "#d62828",
                 "Crisp swings from the big orchard.",         power: 1, speed: 0, pitching: 1, defense: 0),
            Make(19, "Philadelphia","Liberty Bells", "PHI", NL, E, "#7a5c2e", "#e8dcc0",
                 "Cracked, loud, and impossible to ignore.",   power: 2, speed: 0, pitching: 0, defense: 0),
            Make(20, "Washington",  "Monuments",     "WAS", NL, E, "#2a3d66", "#d8d2c4",
                 "Immovable at first, unmissable at the plate.", power: 1, speed: -1, pitching: 1, defense: 1),
            Make(21, "Pittsburgh",  "Ironmen",       "PIT", NL, E, "#2b2b2b", "#f2b705",
                 "Forged for the late innings.",               power: 1, speed: 0, pitching: 1, defense: 1),
            Make(22, "Cincinnati",  "Riverboats",    "CIN", NL, E, "#a32b2b", "#f0e6d2",
                 "All aboard, next stop: home plate.",         power: 0, speed: 1, pitching: 0, defense: 1),
            Make(23, "Nashville",   "Hot Chickens",  "NSH", NL, E, "#d94e1f", "#f4c542",
                 "Spicy bats, expansion-year swagger.",        power: 1, speed: 1, pitching: -1, defense: 0),

            // ---------------------------------------------------------------
            // NATIONAL LEAGUE — WEST
            // ---------------------------------------------------------------
            Make(24, "North Side",  "Ivy",           "NSI", NL, W, "#2e5e3a", "#c8352c",
                 "They grow on the wall and on you.",          power: 0, speed: 1, pitching: 1, defense: 1),
            Make(25, "Milwaukee",   "Cheeseheads",   "MIL", NL, W, "#f2b231", "#2f4a7a",
                 "Sharp, aged, and always melting the pitcher.", power: 2, speed: 0, pitching: 0, defense: 0),
            Make(26, "St. Louis",   "Archers",       "STL", NL, W, "#b03a2e", "#c9cdd1",
                 "Every throw hits the gateway.",              power: 0, speed: 0, pitching: 1, defense: 2),
            Make(27, "Phoenix",     "Roadrunners",   "PHX", NL, W, "#7a3b8f", "#e9c46a",
                 "Beep beep. Triple.",                         power: 0, speed: 2, pitching: 0, defense: 1),
            Make(28, "Denver",      "Mountaineers",  "DEN", NL, W, "#3a4a6b", "#e0e5ec",
                 "The thin air does the rest.",                power: 2, speed: 1, pitching: -2, defense: 0),
            Make(29, "Hollywood",   "Stars",         "HOL", NL, W, "#14141e", "#e8c547",
                 "Every play is the highlight reel.",          power: 1, speed: 1, pitching: 2, defense: 0),
            Make(30, "San Diego",   "Surfers",       "SD",  NL, W, "#6b4226", "#f0c987",
                 "Catch the wave, ride it home.",              power: 1, speed: 1, pitching: 0, defense: 1),
            Make(31, "San Francisco","Fog",          "SF",  NL, W, "#5b6b7a", "#e85d2a",
                 "You cannot hit what you cannot see.",        power: 0, speed: 0, pitching: 2, defense: 1),
        };

        if (teams.Length != 32)
            throw new InvalidOperationException($"League must have 32 clubs, found {teams.Length}.");

        for (int i = 0; i < teams.Length; i++)
        {
            if (teams[i].Id != i)
                throw new InvalidOperationException($"Team at index {i} has mismatched Id {teams[i].Id}.");
        }

        foreach (var group in teams.GroupBy(t => (t.League, t.Division)))
        {
            if (group.Count() != 8)
                throw new InvalidOperationException(
                    $"{group.Key.League} {group.Key.Division} has {group.Count()} clubs, expected 8.");
        }

        return teams;
    }

    public static string DivisionName(League league, Division division) =>
        $"{(league == League.American ? "American" : "National")} League {division}";
}
