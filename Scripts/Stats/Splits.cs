using System.Collections.Generic;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Stats;

/// <summary>
/// The slices a line gets cut into.
///
/// The simulation has always modelled the platoon advantage — <see cref="Core.Platoon"/> has been
/// deciding at-bats by handedness for a long time — but nothing ever wrote down which hand the
/// pitcher had, so the one thing the sim did well was invisible. These are the slices worth
/// keeping: the ones a manager makes a decision from.
/// </summary>
public enum Split
{
    VsRight,
    VsLeft,
    AtHome,
    OnRoad,

    /// <summary>April through to the end. Seven buckets covers a real calendar with room to spare.</summary>
    Month1, Month2, Month3, Month4, Month5, Month6, Month7,

    /// <summary>With men on base and first base not the only one occupied — the at-bats that decide games.</summary>
    ScoringPosition,

    Count,
}

/// <summary>Where a single plate appearance belongs: which slices it counts toward.</summary>
public readonly struct SplitContext
{
    public readonly bool BatterAtHome;

    /// <summary>0-based month of the season, April being 0. Clamped into the seven buckets.</summary>
    public readonly int Month;

    public readonly bool RunnerInScoringPosition;

    public SplitContext(bool batterAtHome, int month, bool runnerInScoringPosition)
    {
        BatterAtHome = batterAtHome;
        Month = month < 0 ? 0 : month > 6 ? 6 : month;
        RunnerInScoringPosition = runnerInScoringPosition;
    }

    /// <summary>The default when nothing knows any better — a neutral, first-month, empty-bases at-bat.</summary>
    public static readonly SplitContext None = new(false, 0, false);

    public static Split MonthSlot(int month) => (Split)((int)Split.Month1 + (month < 0 ? 0 : month > 6 ? 6 : month));
}

/// <summary>
/// One player's line, cut every way. Lines are made lazily: a hitter who never faced a left-hander
/// carries no left-handed line rather than an empty one, which keeps a 3,000-player league cheap.
/// </summary>
public sealed class SplitSet<T> where T : new()
{
    private readonly Dictionary<Split, T> _slices = new();

    public T Of(Split slice)
    {
        if (!_slices.TryGetValue(slice, out var line)) _slices[slice] = line = new T();
        return line;
    }

    /// <summary>Null when nothing has ever been recorded here — so a screen can say "no data".</summary>
    public T Peek(Split slice) => _slices.GetValueOrDefault(slice);

    public bool Has(Split slice) => _slices.ContainsKey(slice);

    public IEnumerable<KeyValuePair<Split, T>> All => _slices;

    public void Clear() => _slices.Clear();
}

/// <summary>Every player's splits, for one season or for a career.</summary>
public sealed class SplitBook
{
    private readonly Dictionary<PlayerData, SplitSet<BattingLine>> _batting = new();
    private readonly Dictionary<PlayerData, SplitSet<PitchingLine>> _pitching = new();

    public SplitSet<BattingLine> Batting(PlayerData p)
    {
        if (!_batting.TryGetValue(p, out var set)) _batting[p] = set = new SplitSet<BattingLine>();
        return set;
    }

    public SplitSet<PitchingLine> Pitching(PlayerData p)
    {
        if (!_pitching.TryGetValue(p, out var set)) _pitching[p] = set = new SplitSet<PitchingLine>();
        return set;
    }

    public bool HasBatting(PlayerData p) => _batting.ContainsKey(p);
    public bool HasPitching(PlayerData p) => _pitching.ContainsKey(p);

    /// <summary>Rolls another book's slices into this one, slice by slice.</summary>
    public void Absorb(SplitBook other)
    {
        foreach (var (player, set) in other._batting)
            foreach (var (slice, line) in set.All)
                Batting(player).Of(slice).Absorb(line);

        foreach (var (player, set) in other._pitching)
            foreach (var (slice, line) in set.All)
                Pitching(player).Of(slice).Absorb(line);
    }

    public void Clear()
    {
        _batting.Clear();
        _pitching.Clear();
    }

    /// <summary>The label a screen puts above a column.</summary>
    public static string Label(Split slice) => slice switch
    {
        Split.VsRight => "vs RHP",
        Split.VsLeft => "vs LHP",
        Split.AtHome => "Home",
        Split.OnRoad => "Road",
        Split.ScoringPosition => "RISP",
        _ => MonthName(slice),
    };

    /// <summary>The same label from a pitcher's side, where the hand belongs to the hitter.</summary>
    public static string PitcherLabel(Split slice) => slice switch
    {
        Split.VsRight => "vs RHB",
        Split.VsLeft => "vs LHB",
        _ => Label(slice),
    };

    private static string MonthName(Split slice) =>
        slice >= Split.Month1 && slice <= Split.Month7
            ? MonthNames[(int)slice - (int)Split.Month1]
            : slice.ToString();

    private static readonly string[] MonthNames =
        { "April", "May", "June", "July", "August", "September", "October" };
}
