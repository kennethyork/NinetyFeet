using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Core;

namespace SandlotSlugfest.Data;

/// <summary>
/// Builds every club's roster from a seed. Generation is fully deterministic, so the
/// same league seed always yields the same players, ratings and jersey numbers.
/// </summary>
public static class RosterGenerator
{
    public const int DefaultLeagueSeed = 1994;

    /// <summary>
    /// Test hook: `--nolegends` builds the league without the written kids, so their effect on
    /// the league's run environment can be measured rather than guessed at.
    /// </summary>
    public static bool IncludeLegends = true;

    private static readonly Position[] FieldPositions =
    {
        Position.C, Position.First, Position.Second, Position.Third,
        Position.Short, Position.Left, Position.Center, Position.Right,
    };

    /// <summary>
    /// The nine men who bat. Under the designated hitter — universal since 2022, and the rule the
    /// league's reference statistics were measured under — the pitcher does not.
    /// </summary>
    private static readonly Position[] LineupPositions =
    {
        Position.C, Position.First, Position.Second, Position.Third,
        Position.Short, Position.Left, Position.Center, Position.Right, Position.DH,
    };

    /// <summary>
    /// The staff, in the order it is built: five to start and eight to relieve. A real club
    /// carries thirteen arms, and a manager with fewer than that has no bullpen to run.
    /// </summary>
    private static readonly StaffRole[] StaffShape =
    {
        StaffRole.Starter, StaffRole.Starter, StaffRole.Starter, StaffRole.Starter, StaffRole.Starter,
        StaffRole.Closer, StaffRole.Setup, StaffRole.Setup,
        StaffRole.Middle, StaffRole.Middle, StaffRole.Middle,
        StaffRole.Long, StaffRole.Long,
    };

    /// <summary>Four men off the bench: a second catcher, cover in the infield and the outfield.</summary>
    private static readonly Position[] BenchShape =
    {
        Position.C, Position.Short, Position.Center, Position.First,
    };

    private static readonly Special[] PitcherSpecials =
    {
        Special.Fireball, Special.CrazyCurve, Special.Corkscrew,
        Special.Knuckleball, Special.Heatseeker, Special.IceVeins,
    };

    private static readonly Special[] HitterSpecials =
    {
        Special.MoonShot, Special.ContactMaster, Special.BuntMaster, Special.TurboLegs,
        Special.SprayHitter, Special.GapPower, Special.PinchRunner,
    };

    private static readonly Special[] GloveSpecials =
    {
        Special.VacuumGlove, Special.CannonArm, Special.WallClimber, Special.Backstop,
    };

    private static Dictionary<int, Roster> _cache;

    /// <summary>Drops cached rosters so a new season regenerates everyone from scratch.</summary>
    public static void ResetCache() => _cache = null;

    /// <summary>All 32 rosters, generated once and reused.</summary>
    /// <summary>
    /// Puts a named kid on the club in place of a generated player at the same position, so the
    /// roster keeps its shape — nine starters, five arms, three on the bench.
    /// </summary>
    private static void InsertLegend(Roster roster, PlayerData legend, HashSet<int> usedNumbers)
    {
        // Never displace another written player. With three of them per club two can share a
        // position, and replacing whoever holds the spot silently deleted the first one — six of
        // ninety-six vanished that way.
        PlayerData replaced;
        int armSlot = legend.Position == Position.P ? roster.Pitchers.FindIndex(p => !p.IsLegend) : -1;

        if (legend.Position == Position.P && armSlot >= 0)
        {
            // A named arm takes the rotation slot his stuff deserves, if it is free.
            int wanted = legend.PitchPower + legend.PitchControl >= 15 ? 0 : roster.Pitchers.Count - 1;
            if (wanted >= 0 && wanted < roster.Pitchers.Count && !roster.Pitchers[wanted].IsLegend)
                armSlot = wanted;

            replaced = roster.Pitchers[armSlot];
            // He inherits the job of the man he displaces, or a written ace ends up as a sixth
            // starter on a five-man rotation.
            legend.Role = replaced.Role;
            roster.Pitchers[armSlot] = legend;
        }
        else if (legend.Position == Position.P)
        {
            // Every arm on the staff is already a written player, so he joins the roster rather
            // than being dropped — this path returned early and lost 44 of 512. There is no
            // rotation slot free for him, so he goes to the bullpen.
            legend.Role = StaffRole.Middle;

            // Whoever gives way must not himself be on the staff — swapping one arm out of the
            // roster list while leaving him on the pitching list is how five pitchers a club
            // ended up playing without being on the team.
            replaced = roster.Players.FirstOrDefault(p => !p.IsLegend && !roster.Pitchers.Contains(p));
            if (replaced == null)
            {
                roster.Players.Add(legend);
                roster.Pitchers.Add(legend);
                usedNumbers.Add(legend.Number);
                return;
            }
            roster.Pitchers.Add(legend);
        }
        else if (roster.Starters.TryGetValue(legend.Position, out replaced) && !replaced.IsLegend)
        {
            roster.Starters[legend.Position] = legend;
        }
        else
        {
            // His spot is taken by another written player, so he joins the club off the bench.
            //
            // The man he displaces must never be a pitcher. The old fallback took the first
            // non-legend on the roster, which is always an arm, and swapped him out of Players
            // while leaving him on Pitchers — so five arms a club were on the staff, took the
            // ball, recorded outs and won games, and were not on the roster at all. Their
            // appearances were never counted, because the record book walks Players.
            replaced = roster.Players.FirstOrDefault(p =>
                !p.IsLegend && !roster.Starters.ContainsValue(p) && !roster.Pitchers.Contains(p))
                ?? roster.Players.FirstOrDefault(p => !p.IsLegend && !roster.Pitchers.Contains(p));

            // Every written player gets a place. With several to a club the bench can fill up
            // with them, and returning here quietly dropped three of a hundred and ninety-two —
            // so if there is nobody to displace he simply joins, and the club carries an extra
            // man until the winter trims it.
            if (replaced == null)
            {
                roster.Players.Add(legend);
                usedNumbers.Add(legend.Number);
                return;
            }
        }

        int at = roster.Players.IndexOf(replaced);
        if (at >= 0) roster.Players[at] = legend;
        else roster.Players.Add(legend);

        // Whatever job the displaced man held, the written player inherits it. Without this a
        // legend could take a starter's roster spot while the lineup still pointed at the man he
        // replaced, so a club sent someone to the plate who was no longer on the team.
        foreach (var slot in roster.Starters.Where(kv => kv.Value == replaced)
                                            .Select(kv => kv.Key).ToList())
            roster.Starters[slot] = legend;

        int inOrder = roster.BattingOrder.IndexOf(replaced);
        if (inOrder >= 0) roster.BattingOrder[inOrder] = legend;

        // His number is part of who he is, so a generated team-mate gives way instead.
        usedNumbers.Remove(replaced.Number);
        foreach (var other in roster.Players)
            if (other != legend && other.Number == legend.Number)
            {
                int n = 1;
                while (usedNumbers.Contains(n) || n == legend.Number) n++;
                other.Number = n;
                usedNumbers.Add(n);
            }
        usedNumbers.Add(legend.Number);
    }

    public static Roster For(TeamData team, int leagueSeed = DefaultLeagueSeed)
    {
        _cache ??= new Dictionary<int, Roster>();
        int key = team.Id * 1000 + leagueSeed % 1000;
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var roster = Build(team, leagueSeed);
        _cache[key] = roster;
        return roster;
    }

    private static Roster Build(TeamData team, int leagueSeed)
    {
        var rng = new Rng(leagueSeed * 733 + team.Id * 8191);
        var roster = new Roster { Team = team };

        var usedNames = new HashSet<string>();
        var usedNumbers = new HashSet<int>();

        // Ages are assigned per player below; a club is a mix of young players still improving,
        // men in their prime, and veterans on the way down.

        // --- Thirteen pitchers: a five-man rotation and an eight-man bullpen. ---
        for (int i = 0; i < StaffShape.Length; i++)
        {
            var role = StaffShape[i];
            bool reliever = role != StaffRole.Starter;
            var p = NewPlayer(ref rng, team, Position.P, usedNames, usedNumbers);
            p.Role = role;

            // Aces get the best stuff. A closer is a one-inning ace — the best pure arm on the
            // staff after the front of the rotation — and it steps down from there through the
            // setup men to the long relievers, who are really failed starters.
            // A bullpen is not the back of the rotation. Relief innings used to be thrown by
            // whichever starter was rested — usually the ace — so pricing relievers below the
            // fifth starter moved a fifth of the league's innings from a 6.3 arm to a 4.1 one and
            // put run scoring up sixteen percent. A real pen out-pitches the back of a rotation.
            int tier = role switch
            {
                StaffRole.Closer => 0,
                StaffRole.Setup => 1,
                StaffRole.Middle => 2,
                StaffRole.Long => 3,
                _ => i,
            };

            // Arms come in flavours too — flamethrowers, surgeons, innings eaters, junkballers.
            p.Archetype = rng.NextFloat() switch
            {
                < 0.28f => Archetype.PowerArm,
                < 0.52f => Archetype.ControlArtist,
                < 0.72f => Archetype.Workhorse,
                < 0.88f => Archetype.Junkballer,
                _ => Archetype.Balanced,
            };

            // Order: velocity, command, stamina.
            var arm = p.Archetype switch
            {
                Archetype.PowerArm => new[] { 2.6f, -1.6f, -0.6f },
                Archetype.ControlArtist => new[] { -1.8f, 2.8f, 0.4f },
                Archetype.Workhorse => new[] { -0.5f, 0.6f, 2.6f },
                Archetype.Junkballer => new[] { -2.4f, 1.4f, -0.3f },
                _ => new[] { 0f, 0f, 0f },
            };

            // A man who only has to get three outs airs it out, so relief velocity runs above what
            // the same arm would show over six innings. Stamina is what he trades for it.
            float heat = role switch
            {
                StaffRole.Closer => 1.4f,
                StaffRole.Setup => 0.9f,
                StaffRole.Middle => 0.5f,
                StaffRole.Long => 0.2f,
                _ => 0f,
            };

            p.PitchPower = Clamp(Rate(ref rng, 6.3f - tier * 0.55f + arm[0] + heat, 2.2f)
                                 + team.PitchingBias);
            p.PitchControl = Clamp(Rate(ref rng, 6.0f - tier * 0.45f + arm[1], 2.2f) + team.PitchingBias);
            p.Stamina = Clamp(reliever
                ? Rate(ref rng, (role == StaffRole.Long ? 5.2f : 3.4f) + arm[2] * 0.4f, 1.6f)
                : Rate(ref rng, 6.8f - tier * 0.6f + arm[2], 2.0f));

            p.Contact = Clamp(Rate(ref rng, 2.6f, 1.6f));
            p.Power = Clamp(Rate(ref rng, 2.4f, 1.7f));
            p.Speed = Clamp(Rate(ref rng, 4.0f, 2.0f) + team.SpeedBias);
            p.Arm = Clamp(Rate(ref rng, 6.0f, 1.9f) + team.DefenseBias);
            p.Fielding = Clamp(Rate(ref rng, 5.5f, 1.9f) + team.DefenseBias);

            if (rng.Chance(i == 0 ? 0.75f : 0.35f))
                p.Special = rng.Pick(PitcherSpecials);

            AssignArsenal(p, ref rng);

            roster.Pitchers.Add(p);
            roster.Players.Add(p);
        }

        // --- Nine in the lineup: eight in the field and the designated hitter. ---
        foreach (var pos in LineupPositions)
        {
            var p = NewPlayer(ref rng, team, pos, usedNames, usedNumbers);
            ApplyPositionProfile(ref rng, p, team, starter: true);
            roster.Starters[pos] = p;
            roster.Players.Add(p);
        }

        // --- Four off the bench, covering behind the plate, the infield and the outfield. ---
        foreach (var pos in BenchShape)
        {
            var p = NewPlayer(ref rng, team, pos, usedNames, usedNumbers);
            ApplyPositionProfile(ref rng, p, team, starter: false);
            roster.Players.Add(p);
        }

        // --- The written kids, seeded onto their clubs. ---
        // Assignment is fixed by team id, so the same faces are always in the same places and you
        // learn who plays where the way you would with a real league.
        if (IncludeLegends)
        foreach (int legendId in Legends.ForTeam(team.Id))
            InsertLegend(roster, Legends.Make(legendId, team.Id * 100 + 90 + legendId), usedNumbers);


        // --- Batting order: best contact/power up top, nine real hitters. ---
        var hitters = LineupPositions.Select(pos => roster.Starters[pos]).ToList();
        var leadoff = hitters.OrderByDescending(h => h.Speed * 2 + h.Contact).First();
        hitters.Remove(leadoff);
        var ordered = hitters.OrderByDescending(h => h.Contact + h.Power * 1.3f).ToList();

        roster.BattingOrder.Add(leadoff);              // 1: table setter
        roster.BattingOrder.Add(ordered[2]);           // 2
        roster.BattingOrder.Add(ordered[0]);           // 3: best hitter
        roster.BattingOrder.Add(ordered[1]);           // 4: cleanup
        roster.BattingOrder.Add(ordered[3]);
        roster.BattingOrder.Add(ordered[4]);
        roster.BattingOrder.Add(ordered[5]);
        roster.BattingOrder.Add(ordered[6]);
        roster.BattingOrder.Add(ordered[7]);

        // A ceiling for everyone, not just draft prospects: young players have room to grow into,
        // players in their prime are close to theirs, and veterans are already past it.
        foreach (var p in roster.Players)
        {
            int room = p.Age < Season.Development.PeakAge
                ? Mathf.RoundToInt((Season.Development.PeakAge - p.Age) * rng.Range(0.20f, 0.60f))
                  + rng.Range(0, 2)
                : 0;
            p.Potential = Mathf.Clamp(p.Overall + room, 1, 10);
        }

        roster.SetPitcher(roster.Pitchers[0]);

        // Written players are seeded in after the generated squad is numbered, and each one brings
        // his own number with him, so a fresh league opened with thirteen clashes across it.
        Uniform.Reconcile(roster);
        return roster;
    }

    /// <summary>Picks the shape of a position player's game, so rosters are not full of clones.</summary>
    private static Archetype PickHitterArchetype(ref Rng rng, bool starter)
    {
        float roll = rng.NextFloat();
        // Five-tool players are rare and only ever start; scrappers fill out the bench.
        if (starter && roll < 0.06f) return Archetype.FiveTool;
        return roll switch
        {
            < 0.22f => Archetype.Slugger,
            < 0.38f => Archetype.ContactHitter,
            < 0.52f => Archetype.Speedster,
            < 0.66f => Archetype.GloveWizard,
            < 0.80f => Archetype.Scrapper,
            _ => Archetype.Balanced,
        };
    }

    private static void ApplyPositionProfile(ref Rng rng, PlayerData p, TeamData team, bool starter)
    {
        // Bench players are a notch below the starters across the board.
        float bump = starter ? 0f : -1.3f;

        p.Archetype = PickHitterArchetype(ref rng, starter);

        // Each archetype shifts the means, which is what makes players feel different to use.
        // Order: contact, power, speed, arm, fielding.
        var shift = p.Archetype switch
        {
            Archetype.Slugger => new[] { -1.4f, 3.0f, -1.6f, 0.3f, -0.8f },
            Archetype.ContactHitter => new[] { 2.6f, -1.4f, 0.5f, -0.3f, 0.2f },
            Archetype.Speedster => new[] { 0.7f, -2.0f, 3.2f, -0.4f, 0.9f },
            Archetype.GloveWizard => new[] { -1.0f, -1.5f, 0.8f, 2.4f, 2.8f },
            Archetype.FiveTool => new[] { 2.0f, 1.8f, 1.9f, 1.6f, 1.7f },
            Archetype.Scrapper => new[] { -0.6f, -1.8f, 0.9f, -0.9f, -0.4f },
            _ => new[] { 0f, 0f, 0f, 0f, 0f },
        };

        // A wider spread than before, so the league has genuine stars and genuine scrubs.
        p.Contact = Clamp(Rate(ref rng, 5.4f + bump + shift[0], 2.4f) + Mathf.RoundToInt(team.PowerBias * 0.4f));
        p.Power = Clamp(Rate(ref rng, 5.0f + bump + shift[1], 2.6f) + team.PowerBias);
        p.Speed = Clamp(Rate(ref rng, 5.2f + bump + shift[2], 2.5f) + team.SpeedBias);
        p.Arm = Clamp(Rate(ref rng, 5.1f + bump + shift[3], 2.3f) + team.DefenseBias);
        p.Fielding = Clamp(Rate(ref rng, 5.3f + bump + shift[4], 2.2f) + team.DefenseBias);
        p.PitchPower = Clamp(Rate(ref rng, 2.0f, 1.0f));
        p.PitchControl = Clamp(Rate(ref rng, 2.0f, 1.0f));
        p.Stamina = Clamp(Rate(ref rng, 5.0f, 2.0f));

        // Positional shape: up the middle is fast and slick, the corners hit for power.
        switch (p.Position)
        {
            case Position.C:
                p.Arm = Clamp(p.Arm + 2);
                p.Fielding = Clamp(p.Fielding + 1);
                p.Speed = Clamp(p.Speed - 2);
                break;
            case Position.First:
                p.Power = Clamp(p.Power + 2);
                p.Speed = Clamp(p.Speed - 2);
                p.Arm = Clamp(p.Arm - 1);
                break;
            case Position.Second:
                p.Fielding = Clamp(p.Fielding + 1);
                p.Speed = Clamp(p.Speed + 1);
                p.Power = Clamp(p.Power - 2);
                break;
            case Position.Third:
                p.Arm = Clamp(p.Arm + 2);
                p.Power = Clamp(p.Power + 1);
                p.Speed = Clamp(p.Speed - 1);
                break;
            case Position.Short:
                p.Fielding = Clamp(p.Fielding + 2);
                p.Arm = Clamp(p.Arm + 1);
                p.Speed = Clamp(p.Speed + 1);
                p.Power = Clamp(p.Power - 1);
                break;
            case Position.Left:
                p.Power = Clamp(p.Power + 1);
                p.Arm = Clamp(p.Arm - 1);
                break;
            case Position.Center:
                p.Speed = Clamp(p.Speed + 2);
                p.Fielding = Clamp(p.Fielding + 2);
                break;
            case Position.Right:
                p.Arm = Clamp(p.Arm + 2);
                p.Power = Clamp(p.Power + 1);
                break;
            case Position.DH:
                // He is in the lineup for one reason. Nobody designates a hitter for his glove.
                p.Power = Clamp(p.Power + 2);
                p.Contact = Clamp(p.Contact + 1);
                p.Speed = Clamp(p.Speed - 2);
                p.Fielding = Clamp(p.Fielding - 2);
                break;
        }

        if (rng.Chance(starter ? 0.45f : 0.15f))
        {
            bool glove = p.Fielding + p.Arm > p.Contact + p.Power;
            p.Special = rng.Pick(glove ? GloveSpecials : HitterSpecials);
        }
    }

    /// <summary>
    /// Maps a player's slot in the league to a unique name.
    ///
    /// There are 183 first names and 228 last names, so 41,724 combinations for roughly 550
    /// players. Multiplying the slot by a stride coprime with that total is a bijection, so no two
    /// slots can ever collide, and the result depends only on the slot — not on build order.
    /// </summary>
    /// <summary>
    /// How many name slots each club is given. Well above any roster, including the extra bodies
    /// a club carries when several written players land on it.
    /// </summary>
    private const int SlotsPerClub = 40;

    /// <summary>
    /// Where draft prospects and winter signings take their names from. Clubs use everything below
    /// this; prospects used to be squeezed into the same range with a padding trick, which meant a
    /// callup could arrive sharing a name with somebody already playing.
    /// </summary>
    private const int ProspectSlotBase = 32 * SlotsPerClub;

    /// <summary>
    /// The size of the shuffled space. Three things have to hold at once, and getting them wrong
    /// is what let duplicate names through three times running.
    ///
    /// It must be a multiple of the number of backgrounds, so splitting one off is exact. It must
    /// exceed the highest slot in use — clubs to 1279, prospects above that — so the shuffle stays
    /// a bijection. And what is left after the split, here 0..255, must fit inside the smallest
    /// background's supply of *unused* name pairs, or two men wrap onto the same one.
    /// </summary>
    private const long NameSpace = 9 * 256;

    /// <summary>
    /// For each background, the name pairs no hand-written player already owns.
    ///
    /// The generated names and the 1,152 written ones are drawn from the same cultural pools, on
    /// purpose — that is what makes a generated team-mate sit beside a written one without looking
    /// out of place. But it also means the generator can hand out a name that is already taken,
    /// and as clubs grew from sixteen men to twenty-six it started doing so half a dozen times a
    /// league: a second Ivan Pribylov, a second Amir Haddad. Removing the written pairs from the
    /// grid up front is cheaper than detecting the clash afterwards, and it cannot fail.
    /// </summary>
    private static int[][] _freeCells;

    private static int[][] FreeCells
    {
        get
        {
            if (_freeCells != null) return _freeCells;

            var taken = new HashSet<string>();
            foreach (var l in Legends.All) taken.Add($"{l.First} {l.Last}");

            var origins = NamePools.Origins;
            _freeCells = new int[origins.Length][];

            for (int o = 0; o < origins.Length; o++)
            {
                var origin = origins[o];
                int firstCount = origin.First.Length + NamePools.Nicknames.Length;
                var free = new List<int>(firstCount * origin.Last.Length);

                for (int cell = 0; cell < firstCount * origin.Last.Length; cell++)
                {
                    int fi = cell % firstCount;
                    int li = cell / firstCount;
                    string first = fi < origin.First.Length
                        ? origin.First[fi]
                        : NamePools.Nicknames[fi - origin.First.Length];

                    if (!taken.Contains($"{first} {origin.Last[li]}")) free.Add(cell);
                }

                _freeCells[o] = free.ToArray();
            }

            return _freeCells;
        }
    }

    private static (string First, string Last) SlotName(int teamId, int index) =>
        NameForSlot((long)teamId * SlotsPerClub + index);

    private static (string First, string Last) NameForSlot(long slot)
    {
        const long Stride = 10007;      // coprime with NameSpace, so the shuffle is a bijection

        // Pick a background first, then take both halves from inside it. Drawing the two pools
        // independently gave men called Mansa Elkington and Pim Kalinowski — each half plausible,
        // the pair obviously assembled.
        //
        // The mapping from slot to name has to be genuinely injective, not merely spread out. The
        // previous version derived the first and last indices from two unrelated divisions of the
        // same number, which is not injective at all — it simply had few enough collisions to look
        // right across 512 players. Growing clubs from sixteen to twenty-six put 832 men in the
        // league and four pairs of them shared a name.
        //
        // So: shuffle the slot across a fixed space, split off the background, and read the
        // remaining index as a two-digit number in (first names) x (surnames). Distinct slots give
        // distinct pairs, by construction, for as long as the pools are big enough to hold them.
        var origins = NamePools.Origins;
        long mixed = (slot * Stride % NameSpace + NameSpace) % NameSpace;

        int originIndex = (int)(mixed % origins.Length);
        var origin = origins[originIndex];
        long k = mixed / origins.Length;                 // 0 .. 255

        // A clubhouse nickname goes with any surname, so the nicknames extend each background's
        // list of given names rather than replacing a name already spoken for. Keeping them inside
        // the injective mapping is what stops "Lefty Kowalski" turning up on two clubs at once.
        var free = FreeCells[originIndex];
        int cell = free[(int)(k % free.Length)];

        int firstCount = origin.First.Length + NamePools.Nicknames.Length;
        int firstIndex = cell % firstCount;
        int lastIndex = cell / firstCount;

        string first = firstIndex < origin.First.Length
            ? origin.First[firstIndex]
            : NamePools.Nicknames[firstIndex - origin.First.Length];

        return (first, origin.Last[lastIndex]);
    }

    private static PlayerData NewPlayer(
        ref Rng rng, TeamData team, Position pos,
        HashSet<string> usedNames, HashSet<int> usedNumbers, long? slot = null)
    {
        // Names are handed out by slot rather than drawn at random. Rejection sampling against a
        // per-team set let the same man appear on two different clubs — three duplicates across
        // 512 players — and a league-wide set could not be used instead, because rosters are built
        // lazily and a shared set would make a club's names depend on which clubs were opened
        // first. A bijection from slot to name is unique league-wide *and* order-independent.
        var (first, last) = slot.HasValue
            ? NameForSlot(slot.Value)
            : SlotName(team.Id, usedNames.Count);
        string full = $"{first} {last}";
        usedNames.Add(full);

        int number;
        int guard = 0;
        do { number = rng.Range(1, 76); } while (!usedNumbers.Add(number) && ++guard < 100);

        return new PlayerData
        {
            // Unique across the league and stable for saves; a trade never changes it.
            Id = team.Id * 100 + usedNames.Count,
            FirstName = first,
            LastName = last,
            Number = number,
            Position = pos,
            // Lefties are the minority, and lefty throwers rarer still up the middle.
            Bats = rng.Chance(0.32f) ? Handedness.Left : Handedness.Right,
            Throws = rng.Chance(0.18f) ? Handedness.Left : Handedness.Right,
            LookSeed = (int)rng.NextUInt(),

            // A real age distribution: mostly mid-twenties to early thirties, with a tail of
            // rookies and veterans at either end.
            Age = rng.NextFloat() switch
            {
                < 0.18f => rng.Range(21, 25),
                < 0.62f => rng.Range(25, 30),
                < 0.88f => rng.Range(30, 34),
                _ => rng.Range(34, 39),
            },
        };
    }

    /// <summary>
    /// A draft prospect: young, raw, and defined more by where he might end up than by what he
    /// is now. Generated the same way as everyone else, then held back so there is real room
    /// between his current ability and his ceiling.
    /// </summary>
    /// <param name="want">
    /// Force a kind of player. A club that has released its way down to nine arms needs an arm,
    /// not another outfielder, and leaving it to chance left staffs permanently short.
    /// </param>
    public static PlayerData Prospect(int id, ref Rng rng, Position? want = null)
    {
        var filler = new TeamData
        {
            Id = 0, City = "", Nickname = "", Abbrev = "FA",
            Primary = new Color(0.4f, 0.4f, 0.45f), Secondary = new Color(0.7f, 0.7f, 0.75f),
        };

        var names = new HashSet<string>();
        var numbers = new HashSet<int>();

        // Roughly a fifth of any draft class is arms.
        var pos = want ?? (rng.Chance(0.22f)
            ? Position.P
            : FieldPositionsPool[rng.Range(0, FieldPositionsPool.Length)]);

        // Prospects take their names from above every club's range, so a callup can never arrive
        // sharing a name with somebody already playing. This used to be done by padding the
        // used-name set with dummies, which put prospects back inside the clubs' own slots as soon
        // as the identifier grew past forty.
        var p = NewPlayer(ref rng, filler, pos, names, numbers,
            slot: ProspectSlotBase + Mathf.Abs(id) % 900);
        p.Id = id;
        // A draft class is young men on the way up, not finished players.
        p.Age = rng.Range(Season.Development.RookieAge - 3, Season.Development.RookieAge + 2);

        if (pos == Position.P)
        {
            p.Archetype = rng.NextFloat() switch
            {
                < 0.30f => Archetype.PowerArm,
                < 0.55f => Archetype.ControlArtist,
                < 0.75f => Archetype.Workhorse,
                < 0.90f => Archetype.Junkballer,
                _ => Archetype.Balanced,
            };
            p.PitchPower = Clamp(Rate(ref rng, 4.6f, 2.3f));
            p.PitchControl = Clamp(Rate(ref rng, 4.3f, 2.3f));
            p.Stamina = Clamp(Rate(ref rng, 5.0f, 2.2f));
            p.Contact = Clamp(Rate(ref rng, 2.4f, 1.5f));
            p.Power = Clamp(Rate(ref rng, 2.3f, 1.6f));
            p.Speed = Clamp(Rate(ref rng, 4.2f, 2.2f));
            p.Arm = Clamp(Rate(ref rng, 5.4f, 2.0f));
            p.Fielding = Clamp(Rate(ref rng, 4.6f, 2.0f));
        }
        else
        {
            ApplyPositionProfile(ref rng, p, filler, starter: false);
        }

        if (rng.Chance(0.30f))
            p.Special = rng.Pick(pos == Position.P ? PitcherSpecials : HitterSpecials);

        // The younger and rawer he is, the further he might still travel before his peak.
        int room = Mathf.RoundToInt((Season.Development.PeakAge - p.Age) * rng.Range(0.25f, 0.65f))
                   + rng.Range(0, 3);
        p.Potential = Mathf.Clamp(p.Overall + room, 1, 10);

        AssignArsenal(p, ref rng);
        return p;
    }

    /// <summary>
    /// What this arm throws.
    ///
    /// A repertoire is most of a pitcher's identity, and the old one was three pitches drawn from
    /// a pool of three — so every arm in the league was a slightly different shuffle of the same
    /// hand. Real staffs are not like that. A power arm lives off a fastball and a slider; a
    /// sinkerballer wants ground balls and barely uses a breaking ball; a crafty veteran with no
    /// velocity survives on a cutter and a changeup and knowing where they go.
    ///
    /// So the kind of pitcher comes first and the pitches follow from it. Relievers carry fewer,
    /// which is the real reason a good reliever cannot start: two pitches will get you through a
    /// lineup once and not twice.
    /// </summary>
    private static void AssignArsenal(PlayerData p, ref Rng rng)
    {
        // Everybody has a fastball. That much is universal.
        p.Repertoire = 1 << (int)PitchType.Fastball;

        void Add(PitchType t) => p.Repertoire |= 1 << (int)t;

        bool hard = p.PitchPower >= 7;
        bool crafty = p.PitchControl >= 7 && p.PitchPower <= 6;

        // The knuckleballer is a genuine rarity, and he is his own thing entirely — soft, odd,
        // and able to go a long way on an arm that should not be able to.
        if (rng.Chance(0.015f))
        {
            Add(PitchType.Knuckler);
            Add(PitchType.Curveball);
            return;
        }

        if (hard && rng.Chance(0.55f))
        {
            // Power: the fastball sets it up and the slider finishes it.
            Add(PitchType.Slider);
            if (rng.Chance(0.45f)) Add(PitchType.Splitter);
            else if (rng.Chance(0.6f)) Add(PitchType.Curveball);
        }
        else if (crafty && rng.Chance(0.6f))
        {
            // Crafty: nothing arrives hard, so it had better not arrive straight.
            Add(PitchType.Cutter);
            Add(PitchType.Changeup);
            if (rng.Chance(0.5f)) Add(PitchType.Curveball);
        }
        else if (rng.Chance(0.30f))
        {
            // Sinkerballer: run and sink, and let them hit it into the ground.
            Add(PitchType.Sinker);
            Add(PitchType.Changeup);
            if (rng.Chance(0.45f)) Add(PitchType.Slider);
        }
        else
        {
            // Conventional: a breaking ball and something slow.
            Add(rng.Chance(0.5f) ? PitchType.Slider : PitchType.Curveball);
            if (rng.Chance(0.7f)) Add(PitchType.Changeup);
            if (p.PitchControl >= 8 && rng.Chance(0.5f)) Add(PitchType.Curveball);
        }

        // A signature move always brings its own pitch along — a curveball specialist who cannot
        // throw a curveball would be a joke at his own expense.
        switch (p.Special)
        {
            case Special.CrazyCurve: Add(PitchType.Curveball); break;
            case Special.Corkscrew: Add(PitchType.Slider); break;
            case Special.Knuckleball: Add(PitchType.Knuckler); break;
        }

        // Relievers work in short bursts off two or three pitches. Trim the least useful.
        if (p.Role != StaffRole.Starter && p.Role != StaffRole.Long)
        {
            foreach (var drop in new[] { PitchType.Changeup, PitchType.Curveball, PitchType.Cutter })
            {
                if (System.Numerics.BitOperations.PopCount((uint)p.Repertoire) <= 2) break;
                p.Repertoire &= ~(1 << (int)drop);
            }
        }
    }

    private static readonly Position[] FieldPositionsPool =
    {
        Position.C, Position.First, Position.Second, Position.Third,
        Position.Short, Position.Left, Position.Center, Position.Right,
    };

    /// <summary>A bell-curved rating around <paramref name="mean"/>, rounded to an integer.</summary>
    /// <summary>
    /// A rating drawn around a mean.
    ///
    /// The multiplier here sets how far apart clubs end up, and it was too wide: a measured season
    /// had a talent spread of 0.170 in win percentage against roughly 0.070 in the real majors, so
    /// the best club finished 31-2. Real baseball is a narrow game — the gap between the best and
    /// worst team is far smaller than it feels — and the ratings have to reflect that or every
    /// season is a procession.
    /// </summary>
    private static int Rate(ref Rng rng, float mean, float spread) =>
        Mathf.RoundToInt(mean + (rng.Bell() - 0.5f) * spread * TalentSpread);

    /// <summary>Set from the measured competitive balance; see the note on <see cref="Rate"/>.</summary>
    private const float TalentSpread = 2.1f;

    private static int Clamp(int value) => Mathf.Clamp(value, 1, 10);
}
