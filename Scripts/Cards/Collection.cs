using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Cards;

/// <summary>
/// What you own: the cards, the coins, and the club you have built out of them.
///
/// This is the mode other baseball games sell — you collect players from packs and the market and
/// assemble a side that could never exist in a real league, then take it out and play with it. It
/// works here because the league already has 1,152 written players and a generator that produces
/// men indistinguishable from them, so there is a genuine collection to chase rather than a
/// storefront with a few dozen faces in it.
///
/// It is deliberately a closed economy. Coins come from playing and from selling, and go on packs
/// and on buying. Nothing here touches real money and nothing ever will.
/// </summary>
public static class Collection
{
    private const string Path = "user://cards.json";

    /// <summary>Cards owned, keyed by player id so duplicates stack.</summary>
    private static readonly Dictionary<int, int> Owned = new();

    /// <summary>Every card the game knows how to make, built once from the league.</summary>
    private static List<Card> _catalogue;

    public static int Coins { get; private set; }

    /// <summary>The player ids in each lineup slot and rotation slot of the built club.</summary>
    public static readonly Dictionary<Position, int> Lineup = new();
    public static readonly List<int> Staff = new();

    /// <summary>
    /// Your own minor-league side.
    ///
    /// A collection is mostly cards that are not in your nine, and until now the only thing to do
    /// with those was sell them. That is a bad choice to force: a young card you believe in and a
    /// duplicate you will never use are not the same thing, and both were headed for the same
    /// button. The minors are where you keep the ones you are not playing but are not done with.
    /// </summary>
    public static readonly List<int> Minors = new();

    /// <summary>How many a card club can carry below the big side.</summary>
    public const int MinorsSize = 20;

    public static bool SendDown(int playerId)
    {
        if (!Has(playerId) || Minors.Contains(playerId)) return false;
        if (Minors.Count >= MinorsSize) return false;

        DropFromClub(playerId);
        Minors.Add(playerId);
        return true;
    }

    public static bool CallUp(int playerId) => Minors.Remove(playerId);

    /// <summary>Cards that are neither playing nor stashed — the ones actually spare.</summary>
    public static IEnumerable<Card> Spare => Mine
        .Where(c => !Lineup.ContainsValue(c.Player.Id)
                    && !Staff.Contains(c.Player.Id)
                    && !Minors.Contains(c.Player.Id));

    /// <summary>What a new collector starts with, so the first thing they can do is open a pack.</summary>
    public const int StartingCoins = 12000;

    // -----------------------------------------------------------------------
    // The catalogue
    // -----------------------------------------------------------------------

    /// <summary>
    /// Every player in the league, as a card. Built from the league itself rather than a separate
    /// list, so a card is always of somebody who really plays somewhere.
    /// </summary>
    public static List<Card> Catalogue
    {
        get
        {
            if (_catalogue != null) return _catalogue;

            _catalogue = new List<Card>();
            foreach (var team in Teams.All)
                foreach (var p in RosterGenerator.For(team).Players)
                    _catalogue.Add(new Card { Player = p, TeamId = team.Id });

            return _catalogue;
        }
    }

    public static Card Find(int playerId) => Catalogue.FirstOrDefault(c => c.Player.Id == playerId);

    public static int CountOf(int playerId) => Owned.GetValueOrDefault(playerId);

    public static bool Has(int playerId) => CountOf(playerId) > 0;

    /// <summary>Every card owned, best first.</summary>
    public static IEnumerable<Card> Mine =>
        Owned.Where(kv => kv.Value > 0)
             .Select(kv => Find(kv.Key))
             .Where(c => c != null)
             .OrderByDescending(c => c.Player.Overall)
             .ThenBy(c => c.Player.LastName);

    public static int Size => Owned.Values.Sum();

    /// <summary>What the whole collection would fetch if it were all sold.</summary>
    public static int Worth => Owned.Where(kv => kv.Value > 0)
        .Sum(kv => (Find(kv.Key)?.Value ?? 0) * kv.Value);

    // -----------------------------------------------------------------------
    // Owning and spending
    // -----------------------------------------------------------------------

    public static void Add(Card card, int count = 1)
    {
        if (card == null) return;
        Owned[card.Player.Id] = CountOf(card.Player.Id) + count;
    }

    public static bool Remove(int playerId, int count = 1)
    {
        int have = CountOf(playerId);
        if (have < count) return false;

        Owned[playerId] = have - count;
        if (Owned[playerId] <= 0)
        {
            Owned.Remove(playerId);
            DropFromClub(playerId);

            // The minors are not part of the club proper, so DropFromClub leaves them alone —
            // which meant selling your last copy of a man you had sent down left his number
            // sitting in the list. He kept showing up on the farm with a CALL UP button, and
            // calling him up produced somebody you did not own.
            ClearMinors(playerId);
        }
        return true;
    }

    public static void Earn(int coins) => Coins = Mathf.Max(0, Coins + coins);

    public static bool Spend(int coins)
    {
        if (coins > Coins) return false;
        Coins -= coins;
        return true;
    }

    // -----------------------------------------------------------------------
    // The club you build
    // -----------------------------------------------------------------------

    /// <summary>The nine spots a card can be put in, plus the staff.</summary>
    public static readonly Position[] Slots =
    {
        Position.C, Position.First, Position.Second, Position.Third, Position.Short,
        Position.Left, Position.Center, Position.Right, Position.DH,
    };

    /// <summary>How many arms a built club carries. Fewer than a real staff — this is your nine
    /// and a rotation, not an organisation.</summary>
    public const int StaffSize = 5;

    public static bool Assign(Position slot, int playerId)
    {
        if (!Has(playerId)) return false;

        // Nobody plays two positions at once.
        foreach (var s in Slots.ToList())
            if (Lineup.TryGetValue(s, out int who) && who == playerId) Lineup.Remove(s);

        ClearMinors(playerId);
        Lineup[slot] = playerId;
        return true;
    }

    public static bool AddToStaff(int playerId)
    {
        if (!Has(playerId) || Staff.Contains(playerId)) return false;
        if (Staff.Count >= StaffSize) return false;
        ClearMinors(playerId);
        Staff.Add(playerId);
        return true;
    }

    public static void DropFromClub(int playerId)
    {
        foreach (var s in Slots.ToList())
            if (Lineup.TryGetValue(s, out int who) && who == playerId) Lineup.Remove(s);
        Staff.Remove(playerId);
    }

    /// <summary>A man cannot be in the lineup and in the minors at the same time.</summary>
    private static void ClearMinors(int playerId) => Minors.Remove(playerId);

    /// <summary>True once there are nine men and at least one arm — enough to take the field.</summary>
    public static bool ClubIsReady => Lineup.Count >= Slots.Length && Staff.Count >= 1;

    /// <summary>How complete the built club is, for the screen.</summary>
    public static string ClubStatus =>
        $"{Lineup.Count}/{Slots.Length} in the lineup · {Staff.Count}/{StaffSize} on the staff";

    /// <summary>The combined rating of the built side, which is the number collectors chase.</summary>
    public static int Rating
    {
        get
        {
            var men = Lineup.Values.Concat(Staff).Select(Find).Where(c => c != null).ToList();
            return men.Count == 0 ? 0 : Mathf.RoundToInt((float)men.Average(c => c.Player.Overall) * 10);
        }
    }

    /// <summary>
    /// Turns the built club into a roster the game can actually play with. It borrows the colours
    /// of whichever club the best man on the side plays for, so the side has an identity on the
    /// field rather than being nine men in grey.
    /// </summary>
    public static Roster BuildRoster()
    {
        var best = Lineup.Values.Concat(Staff).Select(Find).Where(c => c != null)
            .OrderByDescending(c => c.Player.Overall).FirstOrDefault();

        var team = new TeamData
        {
            Id = 0,
            City = "My",
            Nickname = "Collection",
            Abbrev = "YOU",
            League = League.American,
            Division = Division.East,
            Primary = best != null ? Teams.Get(best.TeamId).Primary : new Color("#2e5a88"),
            Secondary = best != null ? Teams.Get(best.TeamId).Secondary : new Color("#e8c14a"),
            Motto = "Assembled a card at a time.",
        };

        var roster = new Roster { Team = team };

        foreach (var slot in Slots)
        {
            if (!Lineup.TryGetValue(slot, out int id)) continue;
            var card = Find(id);
            if (card == null) continue;
            roster.Starters[slot] = card.Player;
            roster.Players.Add(card.Player);
            roster.BattingOrder.Add(card.Player);
        }

        for (int i = 0; i < Staff.Count; i++)
        {
            var card = Find(Staff[i]);
            if (card == null) continue;
            card.Player.Role = StaffRole.Starter;
            roster.Pitchers.Add(card.Player);
            roster.Players.Add(card.Player);
        }

        if (roster.Pitchers.Count > 0) roster.SetPitcher(roster.Pitchers[0]);
        return roster;
    }

    // -----------------------------------------------------------------------
    // Saving
    // -----------------------------------------------------------------------

    private sealed class Dto
    {
        public int Coins { get; set; }
        public int[] Ids { get; set; }
        public int[] Counts { get; set; }
        public int[] LineupSlots { get; set; }
        public int[] LineupIds { get; set; }
        public int[] StaffIds { get; set; }
        public int[] MinorIds { get; set; }
        public bool Started { get; set; }

        // The reward program. Absent in an older save, which reads back as a fresh program rather
        // than an error — nobody should lose a collection to a new field.
        public int Xp { get; set; }
        public int[] VaultPacks { get; set; }
        public string[] CounterKeys { get; set; }
        public int[] CounterValues { get; set; }
        public string[] ClaimedMissions { get; set; }
        public int LastDaily { get; set; }
    }

    // -----------------------------------------------------------------------
    // The reward program's state
    // -----------------------------------------------------------------------

    /// <summary>Experience earned by playing, which drives the ladder in <see cref="Program"/>.</summary>
    public static int Xp { get; private set; }

    public static void AddXp(int xp) => Xp = Mathf.Max(0, Xp + xp);

    /// <summary>Packs earned but not yet opened, as indices into <see cref="Market.Packs"/>.</summary>
    public static readonly List<int> Vault = new();

    public static void Stash(int packIndex) => Vault.Add(packIndex);

    public static bool TakeFromVault(int packIndex) => Vault.Remove(packIndex);

    /// <summary>Running totals the missions are measured against.</summary>
    private static readonly Dictionary<string, int> Counters = new();

    public static int Counter(string key) => Counters.GetValueOrDefault(key);

    public static void Bump(string key, int by = 1) =>
        Counters[key] = Counter(key) + by;

    private static readonly HashSet<string> Claimed = new();

    public static bool MissionClaimed(string key) => Claimed.Contains(key);
    public static void ClaimMission(string key) => Claimed.Add(key);

    /// <summary>The day the free pack was last taken, so it comes once and comes back tomorrow.</summary>
    public static int LastDaily { get; private set; } = -1;

    public static void SetLastDaily(int day) => LastDaily = day;

    /// <summary>
    /// The previous save, kept alongside the current one.
    ///
    /// A collection is built up a pack at a time over weeks and there is no way to earn it back
    /// quickly. One bad write — or one careless delete — should not be the end of it.
    /// </summary>
    private const string Backup = "user://cards.backup.json";

    public static void Save()
    {
        // Roll the last good file aside before overwriting it.
        if (FileAccess.FileExists(Path))
        {
            using var previous = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
            string text = previous?.GetAsText();
            if (!string.IsNullOrEmpty(text))
            {
                using var backup = FileAccess.Open(Backup, FileAccess.ModeFlags.Write);
                backup?.StoreString(text);
            }
        }

        var dto = new Dto
        {
            Coins = Coins,
            Ids = Owned.Keys.ToArray(),
            Counts = Owned.Values.ToArray(),
            LineupSlots = Lineup.Keys.Select(k => (int)k).ToArray(),
            LineupIds = Lineup.Values.ToArray(),
            StaffIds = Staff.ToArray(),
            MinorIds = Minors.ToArray(),
            Started = true,
            Xp = Xp,
            VaultPacks = Vault.ToArray(),
            CounterKeys = Counters.Keys.ToArray(),
            CounterValues = Counters.Values.ToArray(),
            ClaimedMissions = Claimed.ToArray(),
            LastDaily = LastDaily,
        };

        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(dto));
    }

    public static void Load()
    {
        Owned.Clear();
        Lineup.Clear();
        Staff.Clear();
        Minors.Clear();
        Vault.Clear();
        Counters.Clear();
        Claimed.Clear();
        Xp = 0;
        LastDaily = -1;

        // Fall back to the backup if the live file has gone missing or will not open.
        string path = FileAccess.FileExists(Path) ? Path
            : FileAccess.FileExists(Backup) ? Backup
            : null;

        if (path == null)
        {
            Coins = StartingCoins;
            return;
        }

        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null) { Coins = StartingCoins; return; }

        Dto dto;
        try { dto = JsonSerializer.Deserialize<Dto>(file.GetAsText()); }
        catch (JsonException e)
        {
            GD.PushError($"Card collection is corrupt, starting fresh: {e.Message}");
            Coins = StartingCoins;
            return;
        }

        if (dto == null) { Coins = StartingCoins; return; }

        Coins = dto.Started ? dto.Coins : StartingCoins;

        if (dto.Ids != null && dto.Counts != null)
            for (int i = 0; i < dto.Ids.Length && i < dto.Counts.Length; i++)
                if (dto.Counts[i] > 0) Owned[dto.Ids[i]] = dto.Counts[i];

        if (dto.LineupSlots != null && dto.LineupIds != null)
            for (int i = 0; i < dto.LineupSlots.Length && i < dto.LineupIds.Length; i++)
                Lineup[(Position)dto.LineupSlots[i]] = dto.LineupIds[i];

        if (dto.StaffIds != null) Staff.AddRange(dto.StaffIds);
        if (dto.MinorIds != null) Minors.AddRange(dto.MinorIds);

        Xp = Mathf.Max(0, dto.Xp);
        LastDaily = dto.LastDaily == 0 ? -1 : dto.LastDaily;

        if (dto.VaultPacks != null)
            Vault.AddRange(dto.VaultPacks.Where(p => p >= 0 && p < Market.Packs.Length));

        if (dto.CounterKeys != null && dto.CounterValues != null)
            for (int i = 0; i < dto.CounterKeys.Length && i < dto.CounterValues.Length; i++)
                Counters[dto.CounterKeys[i]] = dto.CounterValues[i];

        if (dto.ClaimedMissions != null)
            foreach (string key in dto.ClaimedMissions) Claimed.Add(key);
    }
}
