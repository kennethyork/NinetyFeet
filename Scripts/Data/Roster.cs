using System.Collections.Generic;
using System.Linq;

namespace SandlotSlugfest.Data;

/// <summary>A club's full complement of players plus its starting nine and pitching staff.</summary>
public sealed class Roster
{
    public TeamData Team;

    /// <summary>Everyone on the club, starters first.</summary>
    public readonly List<PlayerData> Players = new();

    /// <summary>The nine lineup regulars keyed by the spot they play, the designated hitter
    /// included. The pitcher is put in here too so the fielding code can look him up.</summary>
    public readonly Dictionary<Position, PlayerData> Starters = new();

    /// <summary>The whole staff, rotation first and then the bullpen.</summary>
    public readonly List<PlayerData> Pitchers = new();

    /// <summary>Nine hitters in batting order. Under the designated hitter rule the pitcher is
    /// not one of them.</summary>
    public readonly List<PlayerData> BattingOrder = new();

    public PlayerData CurrentPitcher { get; private set; }

    /// <summary>Everyone who has taken the mound in the current game, in order.</summary>
    public readonly List<PlayerData> UsedArms = new();

    /// <summary>Index into <see cref="BattingOrder"/> of the next hitter due up.</summary>
    public int LineupSpot;

    /// <summary>The five who take the ball every fifth day.</summary>
    public IEnumerable<PlayerData> Rotation => Pitchers.Where(p => p.Role == StaffRole.Starter);

    /// <summary>Everyone else on the staff.</summary>
    public IEnumerable<PlayerData> Bullpen => Pitchers.Where(p => p.Role != StaffRole.Starter);

    /// <summary>The man for the ninth, or the best arm left if the job is vacant.</summary>
    public PlayerData Closer =>
        Bullpen.FirstOrDefault(p => p.Role == StaffRole.Closer)
        ?? Bullpen.OrderByDescending(p => p.Overall).FirstOrDefault();

    public PlayerData NextHitter()
    {
        var hitter = BattingOrder[LineupSpot];
        LineupSpot = (LineupSpot + 1) % BattingOrder.Count;
        return hitter;
    }

    public PlayerData DueUp => BattingOrder[LineupSpot];

    public void SetPitcher(PlayerData pitcher)
    {
        // Leagues that predate the designated hitter still have the pitcher batting ninth, so if
        // he is in the order the new arm inherits his spot.
        int spot = BattingOrder.IndexOf(CurrentPitcher);
        if (spot >= 0) BattingOrder[spot] = pitcher;

        CurrentPitcher = pitcher;
        Starters[Position.P] = pitcher;
        if (pitcher != null && !UsedArms.Contains(pitcher)) UsedArms.Add(pitcher);
    }

    /// <summary>
    /// Wipes the record of who has pitched, at the start of a game.
    ///
    /// The man already holding the ball has to go straight back on the list. A season names its
    /// starter before handing the game over, so clearing the list without him meant the first
    /// reliever of the night was recorded as the starting pitcher — the rotation showed 0.1 starts
    /// a man and the long relievers showed fifteen.
    /// </summary>
    public void StartGame()
    {
        UsedArms.Clear();
        if (CurrentPitcher != null) UsedArms.Add(CurrentPitcher);
    }

    /// <summary>
    /// Who the manager goes to now.
    ///
    /// This used to hand back whichever arm had the highest overall, which on a five-man staff
    /// meant the ace came out of the bullpen in the third inning of a blowout. A real manager
    /// picks for the situation: the long man when the starter is knocked out early, the closer
    /// only with a lead to protect in the ninth, and never the same three arms every night.
    /// </summary>
    /// <param name="wanted">The job the situation calls for; see CpuBrain.RoleFor.</param>
    /// <param name="save">Whether there is a small lead to protect.</param>
    /// <summary>
    /// The next arm out of the pen, with the man due up taken into account.
    ///
    /// <paramref name="dueUp"/> is who the other club has coming, if it is worth playing the
    /// matchup. Bringing a left-hander in to face a left-handed hitter is one of the oldest
    /// decisions in the game and the whole reason a club carries one, and with no platoon in the
    /// simulation there had never been a reason to do it.
    /// </summary>
    public PlayerData NextArm(StaffRole wanted, bool save, PlayerData dueUp = null)
    {
        var pool = Bullpen
            .Where(p => p != CurrentPitcher && !UsedArms.Contains(p) && !p.IsInjured)
            .ToList();

        // Everybody is spent or already used. Rather than play without a pitcher, go back to the
        // tired list — a real club empties its pen before it forfeits.
        var rested = pool.Where(p => p.IsRested).ToList();
        if (rested.Count > 0) pool = rested;
        if (pool.Count == 0)
            pool = Pitchers.Where(p => p != CurrentPitcher && !p.IsInjured).ToList();
        if (pool.Count == 0) return null;

        return pool
            .OrderByDescending(p => (p.Role == wanted ? 40f : 0f)
                                    + (p.Role == StaffRole.Closer && !save ? -25f : 0f)
                                    + p.Overall * 2f
                                    + p.RestDays
                                    + Matchup(p))
            .First();

        // Worth something, but not worth more than having the right man for the inning: a lefty
        // in the seventh does not get to close.
        float Matchup(PlayerData arm)
        {
            if (dueUp == null || dueUp.Bats == Handedness.Switch) return 0f;
            return dueUp.Bats == arm.Throws ? 14f : -6f;
        }
    }

    /// <summary>Bring in the freshest arm that is not already on the mound.</summary>
    public PlayerData BestAvailableReliever() => NextArm(StaffRole.Middle, save: false);

    /// <summary>The player covering a given spot on the field right now.</summary>
    public PlayerData Fielder(Position position) =>
        Starters.TryGetValue(position, out var p) ? p : CurrentPitcher;

    /// <summary>
    /// The spot this player is actually playing, which after a trade may differ from the
    /// position he came up at. Falls back to his natural position if he is not in the lineup.
    /// </summary>
    public Position SlotOf(PlayerData player)
    {
        foreach (var (spot, occupant) in Starters)
            if (occupant == player) return spot;
        return player.Position;
    }

    /// <summary>Everyone not in the lineup and not on the staff — the bench.</summary>
    public IEnumerable<PlayerData> Bench => Players.Where(p =>
        p.Position != Position.P && !Starters.ContainsValue(p));

    /// <summary>
    /// Sends a substitute in for a man already in the batting order, keeping his spot. Used for
    /// pinch hitters and for covering an injury.
    /// </summary>
    public bool Substitute(PlayerData outgoing, PlayerData incoming)
    {
        if (outgoing == null || incoming == null || outgoing == incoming) return false;

        int at = BattingOrder.IndexOf(outgoing);
        if (at < 0) return false;
        BattingOrder[at] = incoming;

        foreach (var slot in Starters.Where(kv => kv.Value == outgoing).Select(kv => kv.Key).ToList())
            Starters[slot] = incoming;
        return true;
    }
}
