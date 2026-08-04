using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

public struct TradeVerdict
{
    public bool Accepted;
    public string Reason;
    public float OfferedValue;
    public float RequestedValue;
}

/// <summary>
/// Values players and judges trade offers. The other club weighs raw talent, how badly it
/// needs the position, and whether the deal leaves it able to field a team.
/// </summary>
public static class TradeEngine
{
    /// <summary>What a player is worth in trade, on an open-ended points scale.</summary>
    public static float Value(PlayerData p)
    {
        float core = p.Position == Data.Position.P
            ? p.PitchPower * 1.25f + p.PitchControl * 1.15f + p.Stamina * 0.7f + p.Fielding * 0.25f
            : p.Contact * 1.30f + p.Power * 1.15f + p.Speed * 0.75f +
              p.Fielding * 0.70f + p.Arm * 0.50f;

        // Up-the-middle defenders and front-line arms carry a premium.
        float positional = p.Position switch
        {
            Data.Position.C => 1.10f,
            Data.Position.Short => 1.12f,
            Data.Position.Center => 1.08f,
            Data.Position.Second => 1.04f,
            Data.Position.P => 1.10f,
            _ => 1.0f,
        };

        // A signature move is worth real money.
        float special = p.Special == Special.None ? 1.0f : 1.12f;

        return core * positional * special;
    }

    /// <summary>How thin a club is at a position, used to price need into an offer.</summary>
    private static float NeedFactor(Roster roster, Data.Position pos)
    {
        var atSpot = roster.Players.Where(p => p.Position == pos).ToList();
        if (atSpot.Count == 0) return 1.45f;

        float best = atSpot.Max(Value);
        float leagueTypical = pos == Data.Position.P ? 26f : 30f;

        // Weak there means a bigger appetite; already strong means less interest.
        return Mathf.Clamp(1.35f - (best / leagueTypical) * 0.45f, 0.80f, 1.45f);
    }

    /// <summary>
    /// Judges an offer from <paramref name="fromTeam"/>'s point of view: they give up
    /// <paramref name="requested"/> and receive <paramref name="offered"/>.
    /// </summary>
    public static TradeVerdict Evaluate(
        SeasonState season, int fromTeamId,
        IReadOnlyList<PlayerData> offered, IReadOnlyList<PlayerData> requested)
    {
        var roster = season.RosterFor(fromTeamId);

        if (offered.Count == 0 || requested.Count == 0)
            return new TradeVerdict { Accepted = false, Reason = "Put some names on both sides first." };

        // Checked here rather than only in the screen, so nothing can slip a deal through late.
        if (!season.TradesOpen)
            return new TradeVerdict
            {
                Accepted = false,
                Reason = "The deadline has passed — we can't make a deal until the offseason.",
            };

        // They must keep enough bodies to field a team.
        int after = roster.Players.Count - requested.Count + offered.Count;
        if (after < 12)
            return new TradeVerdict { Accepted = false, Reason = "That would leave us short of a full roster." };

        int pitchersAfter = roster.Pitchers.Count(p => !requested.Contains(p)) +
                            offered.Count(p => p.Position == Data.Position.P);
        if (pitchersAfter < 3)
            return new TradeVerdict { Accepted = false, Reason = "We can't gut our pitching staff." };

        float incoming = offered.Sum(p => Value(p) * NeedFactor(roster, p.Position));
        float outgoing = requested.Sum(Value);

        // Clubs value what they already have, and want a margin to bother doing a deal.
        outgoing *= 1.08f;

        var verdict = new TradeVerdict { OfferedValue = incoming, RequestedValue = outgoing };

        if (incoming >= outgoing)
        {
            verdict.Accepted = true;
            verdict.Reason = incoming > outgoing * 1.25f
                ? "We'll take that deal before you change your mind."
                : "That works for us. Deal.";
        }
        else
        {
            verdict.Accepted = false;
            float shortfall = (outgoing - incoming) / Mathf.Max(outgoing, 1f);
            verdict.Reason = shortfall switch
            {
                < 0.10f => "You're close. Sweeten it a little.",
                < 0.30f => "Not enough coming back. Try again.",
                _ => "That's nowhere near fair value.",
            };
        }

        return verdict;
    }

    /// <summary>Carries out an accepted trade, moving players between the two rosters.</summary>
    public static void Execute(
        SeasonState season, int teamA, int teamB,
        IReadOnlyList<PlayerData> fromA, IReadOnlyList<PlayerData> fromB)
    {
        var a = season.RosterFor(teamA);
        var b = season.RosterFor(teamB);

        foreach (var p in fromA) Move(a, b, p);
        foreach (var p in fromB) Move(b, a, p);

        Rebuild(a);
        Rebuild(b);
    }

    private static void Move(Roster from, Roster to, PlayerData player)
    {
        from.Players.Remove(player);
        from.Pitchers.Remove(player);
        from.BattingOrder.Remove(player);
        foreach (var spot in from.Starters.Where(kv => kv.Value == player).Select(kv => kv.Key).ToList())
            from.Starters.Remove(spot);

        to.Players.Add(player);
        if (player.Position == Data.Position.P) to.Pitchers.Add(player);
    }

    /// <summary>The eight spots someone has to stand in, plus the designated hitter.</summary>
    private static readonly Data.Position[] LineupSlots =
    {
        Data.Position.C, Data.Position.First, Data.Position.Second, Data.Position.Third,
        Data.Position.Short, Data.Position.Left, Data.Position.Center, Data.Position.Right,
        Data.Position.DH,
    };

    /// <summary>
    /// Re-picks the lineup, the rotation, the bullpen roles and the batting order.
    ///
    /// Roles are reassigned from scratch rather than carried across, because a trade brings in a
    /// man wearing another club's job title — take two closers in a deal and one of them has to
    /// become a setup man.
    /// </summary>
    public static void Rebuild(Roster roster)
    {
        var arms = roster.Players.Where(p => p.Position == Data.Position.P).ToList();

        // Starting is an endurance job: a man with great stuff and no stamina is a reliever, and
        // ranking the whole staff on raw stuff alone put closers at the top of the rotation.
        var rotation = arms
            .OrderByDescending(p => p.PitchPower + p.PitchControl + p.Stamina * 1.6f)
            .Take(5)
            .ToList();
        foreach (var p in rotation) p.Role = StaffRole.Starter;

        // The rest sort themselves out by pure stuff — best arm closes, next two set up.
        var pen = arms.Except(rotation)
            .OrderByDescending(p => p.PitchPower * 1.3f + p.PitchControl * 1.2f)
            .ToList();
        for (int i = 0; i < pen.Count; i++)
            pen[i].Role = i switch
            {
                0 => StaffRole.Closer,
                1 or 2 => StaffRole.Setup,
                3 or 4 or 5 => StaffRole.Middle,
                _ => StaffRole.Long,
            };

        roster.Pitchers.Clear();
        roster.Pitchers.AddRange(rotation);
        roster.Pitchers.AddRange(pen);

        roster.Starters.Clear();
        var used = new HashSet<PlayerData>();

        foreach (var spot in LineupSlots)
        {
            // Prefer a natural fit; fall back to the best bat left over. The designated hitter is
            // picked last and on his bat alone, which is the whole point of the job.
            var pick = roster.Players
                .Where(p => !used.Contains(p) && p.Position != Data.Position.P)
                .OrderByDescending(p => spot == Data.Position.DH
                    ? p.Contact * 1.3f + p.Power * 1.5f
                    : (p.Position == spot ? 12f : 0f) + Value(p))
                .FirstOrDefault();

            if (pick == null) continue;
            used.Add(pick);
            roster.Starters[spot] = pick;
        }

        if (roster.Pitchers.Count > 0) roster.SetPitcher(roster.Pitchers[0]);

        roster.BattingOrder.Clear();
        // SetPitcher above puts the pitcher into Starters, so he has to be filtered out here or
        // he lands in the batting order, which under the designated hitter rule he never does.
        var hitters = roster.Starters
            .Where(kv => kv.Key != Data.Position.P)
            .Select(kv => kv.Value)
            .Distinct()
            .ToList();
        var leadoff = hitters.OrderByDescending(h => h.Speed * 2 + h.Contact).FirstOrDefault();
        if (leadoff != null) hitters.Remove(leadoff);
        var rest = hitters.OrderByDescending(h => h.Contact + h.Power * 1.3f).ToList();

        if (leadoff != null) roster.BattingOrder.Add(leadoff);
        foreach (var h in rest) roster.BattingOrder.Add(h);

        // A club stripped so bare it has no nine hitters still has to send someone up.
        if (roster.BattingOrder.Count == 0 && roster.Pitchers.Count > 0)
            roster.BattingOrder.Add(roster.Pitchers[0]);

        roster.LineupSpot = 0;

        // Anyone who has just arrived gets a number nobody else on the club is wearing. This is
        // the single hook every join goes through — trades, callups, claims, signings and the
        // draft all rebuild the roster — so putting it here covers all of them at once.
        Uniform.Reconcile(roster);
    }
}
