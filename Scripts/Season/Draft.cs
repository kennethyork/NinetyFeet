using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>One selection made in the draft.</summary>
public sealed class DraftPick
{
    public int Round;
    public int Overall;
    public int TeamId;
    public PlayerData Player;
}

/// <summary>
/// The amateur draft. A procedurally generated class of young prospects, taken in reverse
/// order of the standings so the worst clubs pick first. The player drafts for their own club;
/// every other pick is made by a CPU scout.
/// </summary>
public sealed class Draft
{
    public const int Rounds = 2;

    public readonly List<PlayerData> Available = new();
    public readonly List<DraftPick> Picks = new();
    public readonly List<int> Order = new();

    public int Current;             // index into Order
    public bool Complete => Current >= Order.Count;

    public int OnTheClock => Complete ? -1 : Order[Current];
    public int CurrentRound => Complete ? Rounds : Current / 32 + 1;

    /// <summary>Builds the class and the order. Worst record picks first.</summary>
    public void Begin(SeasonState season, int seed)
    {
        Available.Clear();
        Picks.Clear();
        Order.Clear();
        Current = 0;

        var rng = new Rng(seed * 7717 + 91);

        // A class a little larger than the number of picks, so there is always a choice left.
        int classSize = Rounds * 32 + 24;
        // Written prospects first, so a draft class in year five is still made of named people.
        for (int i = 0; i < classSize; i++)
        {
            var written = Legends.DrawFromReserve(season.ReserveUsed, 90000 + seed % 1000 * 500 + i);
            Available.Add(written ?? RosterGenerator.Prospect(90000 + seed % 1000 * 500 + i, ref rng));
        }

        var byRecord = Teams.All
            .OrderBy(t => season.Book.Record(t.Id).WinPct)
            .ThenBy(t => season.Book.Record(t.Id).RunDifferential)
            .Select(t => t.Id)
            .ToList();

        for (int round = 0; round < Rounds; round++) Order.AddRange(byRecord);
    }

    /// <summary>What a CPU scout thinks a prospect is worth: what he is, plus what he might be.</summary>
    public static float ScoutValue(PlayerData p, Roster roster)
    {
        // Ceiling matters more than current ability for a teenager.
        float raw = p.Overall * 0.85f + p.Potential * 1.35f;

        // A club short at a position reaches for one who plays it.
        int atSpot = roster.Players.Count(x => x.Position == p.Position);
        float need = atSpot switch { 0 => 1.30f, 1 => 1.12f, 2 => 1.0f, _ => 0.90f };

        // Arms are always in demand.
        if (p.Position == Data.Position.P && roster.Pitchers.Count < 5) need *= 1.15f;

        return raw * need + (p.Special != Special.None ? 2.5f : 0f);
    }

    /// <summary>Makes the pick that is currently on the clock for a given player.</summary>
    public bool Take(SeasonState season, PlayerData player)
    {
        if (Complete || player == null || !Available.Remove(player)) return false;

        int teamId = Order[Current];
        var roster = season.RosterFor(teamId);
        roster.Players.Add(player);
        if (player.Position == Data.Position.P) roster.Pitchers.Add(player);
        TradeEngine.Rebuild(roster);

        Picks.Add(new DraftPick
        {
            Round = Current / 32 + 1,
            Overall = Current + 1,
            TeamId = teamId,
            Player = player,
        });

        Current++;
        return true;
    }

    /// <summary>Runs the CPU's pick for whoever is on the clock.</summary>
    public DraftPick AutoPick(SeasonState season)
    {
        if (Complete) return null;

        int teamId = Order[Current];
        var roster = season.RosterFor(teamId);

        var best = Available
            .OrderByDescending(p => ScoutValue(p, roster))
            .FirstOrDefault();

        if (best == null) { Current++; return null; }
        Take(season, best);
        return Picks[^1];
    }

    /// <summary>Advances the CPU through every pick until the user is on the clock, or it ends.</summary>
    public List<DraftPick> RunToUser(SeasonState season)
    {
        var made = new List<DraftPick>();
        int guard = 0;
        while (!Complete && OnTheClock != season.UserTeamId && guard++ < 200)
        {
            var pick = AutoPick(season);
            if (pick != null) made.Add(pick);
        }
        return made;
    }
}
