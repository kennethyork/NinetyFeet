using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>What a coach is for.</summary>
public enum CoachRole { Hitting, Pitching, Bench, Scouting }

/// <summary>
/// A man on the staff, hired for what he is good at.
/// </summary>
public sealed class Coach
{
    public int Id;
    public string Name;
    public CoachRole Role;

    /// <summary>1 to 10. What he is actually worth at the job.</summary>
    public int Skill;

    /// <summary>What he costs a year, in thousands, out of the same budget as the players.</summary>
    public int Salary;

    public int Years;

    public string Grade => Skill switch
    {
        >= 9 => "outstanding",
        >= 7 => "strong",
        >= 5 => "solid",
        >= 3 => "adequate",
        _ => "weak",
    };

    public string What => Role switch
    {
        CoachRole.Hitting => "develops bats",
        CoachRole.Pitching => "develops arms",
        CoachRole.Bench => "keeps men healthy and rested",
        _ => "sharpens the scouting reports",
    };
}

/// <summary>
/// The coaching staff, and what it is worth.
///
/// A front office that only signs players is missing half of what a front office does. In OOTP the
/// staff is one of the quieter levers and one of the most consequential over a dynasty: a good
/// hitting coach compounds across every young bat in the system for as long as you keep him, and
/// paying for one costs money you could have spent on a shortstop this year. That trade — spend on
/// the club you have or on the club you are building — is the whole of running an organisation, and
/// without a staff there was no way to express it.
///
/// Four jobs, deliberately different in what they touch, so hiring is a choice rather than a
/// ranking: bats, arms, health, and how much your scouts actually know.
/// </summary>
public static class Coaches
{
    /// <summary>How many a club carries. One of each job.</summary>
    public static readonly CoachRole[] Roles =
    {
        CoachRole.Hitting, CoachRole.Pitching, CoachRole.Bench, CoachRole.Scouting,
    };

    private static readonly Dictionary<int, List<Coach>> Staffs = new();

    /// <summary>Coaches without a club, available to hire.</summary>
    public static readonly List<Coach> Available = new();

    public static List<Coach> Of(int teamId)
    {
        if (Staffs.TryGetValue(teamId, out var s)) return s;
        return Staffs[teamId] = new List<Coach>();
    }

    public static Coach Get(int teamId, CoachRole role) =>
        Of(teamId).FirstOrDefault(c => c.Role == role);

    /// <summary>What a club's coach at a job is worth, 0 when the post is empty.</summary>
    public static int SkillAt(int teamId, CoachRole role) => Get(teamId, role)?.Skill ?? 0;

    public static void Clear()
    {
        Staffs.Clear();
        Available.Clear();
    }

    public static int Payroll(int teamId) => Of(teamId).Sum(c => c.Salary);

    // -----------------------------------------------------------------------
    // What they actually do
    // -----------------------------------------------------------------------

    /// <summary>
    /// The multiplier a coach puts on development. A club with nobody in the job is not neutral —
    /// it is worse than one with an ordinary coach, which is what makes the post worth filling.
    /// </summary>
    public static float DevelopmentFactor(int teamId, PlayerData p)
    {
        var role = p.Position == Position.P ? CoachRole.Pitching : CoachRole.Hitting;
        int skill = SkillAt(teamId, role);

        // Empty post is 0.90; an ordinary coach is about 1.0; the best in the game is 1.18.
        return skill == 0 ? 0.90f : 0.90f + skill * 0.028f;
    }

    /// <summary>How much a bench coach shortens an injury. Never to nothing.</summary>
    public static float HealingFactor(int teamId)
    {
        int skill = SkillAt(teamId, CoachRole.Bench);
        return skill == 0 ? 1.06f : 1.06f - skill * 0.018f;
    }

    /// <summary>
    /// How much of the scouts' error a good scouting director removes. A club that pays for
    /// scouting genuinely knows more about its own prospects than one that does not — which is the
    /// only honest way to make the department worth money.
    /// </summary>
    public static float ScoutingSharpness(int teamId)
    {
        int skill = SkillAt(teamId, CoachRole.Scouting);
        return skill == 0 ? 1.15f : 1.15f - skill * 0.035f;
    }

    // -----------------------------------------------------------------------
    // Hiring
    // -----------------------------------------------------------------------

    public static int AskingPrice(Coach c) => 300 + c.Skill * c.Skill * 34;

    /// <summary>Fills every club's staff and stocks the hiring pool when a league begins.</summary>
    public static void Stock(int seed)
    {
        Clear();
        var rng = new Rng(seed * 8171 + 337);
        int id = 0;

        foreach (var t in Teams.All)
            foreach (var role in Roles)
            {
                var c = Make(ref rng, role, ref id);
                c.Salary = AskingPrice(c);
                c.Years = rng.Range(1, 4);
                Of(t.Id).Add(c);
            }

        // A market to hire out of, or the only way to improve a staff would be waiting for
        // somebody else's man to be sacked.
        for (int i = 0; i < 14; i++)
        {
            var c = Make(ref rng, Roles[i % Roles.Length], ref id);
            c.Salary = AskingPrice(c);
            Available.Add(c);
        }
    }

    private static Coach Make(ref Rng rng, CoachRole role, ref int id)
    {
        // Bell-shaped, so most coaches are ordinary and a real one is worth paying for.
        int skill = Mathf.Clamp(Mathf.RoundToInt(rng.Bell() * 11f) + 1, 1, 10);

        return new Coach
        {
            Id = 900000 + id++,
            Name = CoachNames.Pick(ref rng),
            Role = role,
            Skill = skill,
        };
    }

    /// <summary>Hires a coach, sacking whoever held the job. Returns why not, or null.</summary>
    public static string Hire(SeasonState season, int teamId, Coach c)
    {
        if (c == null) return "Nobody selected.";
        if (!Available.Contains(c)) return $"{c.Name} has taken another job.";

        int space = Finances.SpaceFor(season, teamId) - Payroll(teamId);
        if (c.Salary > space)
            return $"{c.Name} wants {Contracts.Text(c.Salary)} and you have " +
                   $"{Contracts.Text(Mathf.Max(0, space))} left.";

        var staff = Of(teamId);
        var outgoing = staff.FirstOrDefault(x => x.Role == c.Role);
        if (outgoing != null)
        {
            staff.Remove(outgoing);
            Available.Add(outgoing);
        }

        Available.Remove(c);
        c.Years = 2;
        staff.Add(c);
        return null;
    }

    /// <summary>The winter: contracts run down, some men move on, the pool refreshes.</summary>
    public static List<string> RunOffseason(int seed)
    {
        var lines = new List<string>();
        var rng = new Rng(seed * 3313 + 71);
        int id = 700000 + (seed & 0xFFFF);

        foreach (var t in Teams.All)
        {
            var staff = Of(t.Id);
            foreach (var c in staff.ToList())
            {
                if (--c.Years > 0) continue;

                // An outstanding coach is likelier to be poached than to re-sign.
                if (rng.Chance(0.30f + c.Skill * 0.03f))
                {
                    staff.Remove(c);
                    Available.Add(c);
                    lines.Add($"{t.FullName} lose {c.Name}, their {Label(c.Role)} coach.");
                }
                else c.Years = rng.Range(2, 4);
            }

            // Never leave a post empty for long — the AI clubs fill from the pool.
            foreach (var role in Roles)
            {
                if (staff.Any(x => x.Role == role)) continue;

                var pick = Available.Where(x => x.Role == role)
                                    .OrderByDescending(x => x.Skill)
                                    .FirstOrDefault();
                if (pick == null)
                {
                    pick = Make(ref rng, role, ref id);
                    pick.Salary = AskingPrice(pick);
                }
                else Available.Remove(pick);

                pick.Years = rng.Range(2, 4);
                staff.Add(pick);
            }
        }

        // Top the market back up.
        while (Available.Count < 12)
        {
            var c = Make(ref rng, Roles[Available.Count % Roles.Length], ref id);
            c.Salary = AskingPrice(c);
            Available.Add(c);
        }

        return lines;
    }

    public static string Label(CoachRole role) => role switch
    {
        CoachRole.Hitting => "hitting",
        CoachRole.Pitching => "pitching",
        CoachRole.Bench => "bench",
        _ => "scouting",
    };

    // -----------------------------------------------------------------------

    public static (int[] Ids, string[] Names, int[] Roles_, int[] Skills, int[] Salaries,
        int[] Years) Export(int teamId)
    {
        var s = Of(teamId);
        return (s.Select(c => c.Id).ToArray(), s.Select(c => c.Name).ToArray(),
                s.Select(c => (int)c.Role).ToArray(), s.Select(c => c.Skill).ToArray(),
                s.Select(c => c.Salary).ToArray(), s.Select(c => c.Years).ToArray());
    }

    public static void Import(int teamId, int[] ids, string[] names, int[] roles, int[] skills,
        int[] salaries, int[] years)
    {
        if (ids == null || names == null || roles == null || skills == null) return;

        var staff = Of(teamId);
        staff.Clear();

        for (int i = 0; i < ids.Length && i < names.Length && i < roles.Length
                        && i < skills.Length; i++)
            staff.Add(new Coach
            {
                Id = ids[i],
                Name = names[i],
                Role = (CoachRole)Mathf.Clamp(roles[i], 0, 3),
                Skill = Mathf.Clamp(skills[i], 1, 10),
                Salary = salaries != null && i < salaries.Length ? salaries[i] : 500,
                Years = years != null && i < years.Length ? years[i] : 2,
            });
    }
}

/// <summary>Names for the staff, kept apart from the players' so nobody is both.</summary>
internal static class CoachNames
{
    private static readonly string[] First =
    {
        "Bud", "Cal", "Dusty", "Earl", "Gene", "Hank", "Jim", "Lou", "Mel", "Ozzie",
        "Pete", "Ray", "Sparky", "Tony", "Walt", "Whitey", "Bobby", "Clint", "Doc", "Rube",
    };

    private static readonly string[] Last =
    {
        "Ashford", "Brannigan", "Corbin", "Delahanty", "Eastwick", "Fairbanks", "Gormley",
        "Hollis", "Iverson", "Jessup", "Kestrel", "Lomax", "Mundy", "Norquist", "Oakes",
        "Prentiss", "Quill", "Rademacher", "Sandoval", "Tillman", "Underhill", "Vance",
        "Whitlock", "Yarborough", "Zeller",
    };

    public static string Pick(ref Rng rng) => $"{rng.Pick(First)} {rng.Pick(Last)}";
}
