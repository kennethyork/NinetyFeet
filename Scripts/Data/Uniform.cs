using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SandlotSlugfest.Data;

/// <summary>
/// Jersey numbers.
///
/// A number was handed out once, when a club's roster was first built, and never looked at again.
/// Every way a player can join a club afterwards — a trade, a callup, a waiver claim, a free-agent
/// signing, a draft pick — brought his old number with him and nothing checked whether somebody on
/// his new club was already wearing it. Thirteen clashes existed the day a league was created and
/// it reached a hundred and thirty-one by the fourth season, which is one player in six taking the
/// field in a team-mate's number.
///
/// It is not a cosmetic problem: the number is drawn on the back of the shirt and is how you tell
/// two men apart at a distance.
/// </summary>
public static class Uniform
{
    public const int Highest = 99;

    /// <summary>
    /// Settles every clash on a club in one pass, and returns how many men had to change.
    ///
    /// Seniority decides who keeps a contested number. A written player's number is part of who he
    /// is, so he never gives it up; after that it goes to whoever has been in the league longest,
    /// which is the same instinct a real clubhouse runs on.
    /// </summary>
    public static int Reconcile(Roster roster)
    {
        var taken = new HashSet<int>();
        int changed = 0;

        var order = roster.Players
            .OrderByDescending(p => p.IsLegend)
            .ThenByDescending(p => p.ServiceYears)
            .ThenBy(p => p.Id)
            .ToList();

        foreach (var p in order)
        {
            if (p.Number is > 0 and <= Highest && taken.Add(p.Number)) continue;

            p.Number = FreeNumber(taken, p.Id);
            taken.Add(p.Number);
            changed++;
        }

        return changed;
    }

    /// <summary>
    /// A free number that is not simply the lowest one going. Walking up from 1 every time put
    /// every reassigned player in the single digits, so a club that made three trades ended up
    /// with numbers 1, 2 and 3 on its bench and nothing above 60 anywhere.
    /// </summary>
    private static int FreeNumber(HashSet<int> taken, int id)
    {
        int start = Mathf.Abs(id * 37) % Highest + 1;

        for (int i = 0; i < Highest; i++)
        {
            int n = (start + i - 1) % Highest + 1;
            if (!taken.Contains(n)) return n;
        }

        return 0;
    }

    private static Dictionary<int, TeamData> _road;

    /// <summary>
    /// The kit a club wears in a given ballpark: its own at home, greys on the road.
    ///
    /// This is how baseball has always dressed, and it is not only tradition — it is what keeps
    /// the two sides apart. Both clubs wearing their primary colour is fine until Oakland's
    /// #2f5d3a meets North Side's #2e5e3a, which are the same green to the eye, and you can no
    /// longer tell who is fielding the ball. Eight pairs of clubs in this league sit close enough
    /// to be confusable; the road kit settles all of them at once instead of one at a time.
    ///
    /// The grey is blended with the club's own colour rather than being flat, so a road side still
    /// reads as itself, and the trim is left alone — that is where a road uniform carries its
    /// identity.
    /// </summary>
    public static TeamData Kit(TeamData team, bool home)
    {
        if (home || team == null) return team;

        _road ??= new Dictionary<int, TeamData>();
        if (_road.TryGetValue(team.Id, out var cached)) return cached;

        var greys = new TeamData
        {
            Id = team.Id,
            City = team.City,
            Nickname = team.Nickname,
            Abbrev = team.Abbrev,
            League = team.League,
            Division = team.Division,
            Motto = team.Motto,
            PowerBias = team.PowerBias,
            SpeedBias = team.SpeedBias,
            PitchingBias = team.PitchingBias,
            DefenseBias = team.DefenseBias,

            // Road grey, tinted toward the club so it is still recognisably them.
            Primary = team.Primary.Lerp(new Color(0.62f, 0.63f, 0.66f), 0.66f),
            Secondary = team.Secondary,
        };

        _road[team.Id] = greys;
        return greys;
    }
}
