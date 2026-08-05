using Godot;

namespace SandlotSlugfest.Stats;

/// <summary>A hitter's accumulated line.</summary>
public sealed class BattingLine
{
    public int Games, PlateAppearances, AtBats, Hits, Doubles, Triples, HomeRuns;
    public int Runs, RunsBattedIn, Walks, Strikeouts, StolenBases;

    // The rest of the line. These are the counting stats that separate a box score from a
    // scoreboard: without the sacrifices and the hit-by-pitch, on-base percentage is wrong, and
    // without caught stealing a base thief looks free.
    public int HitByPitch, IntentionalWalks, CaughtStealing;
    public int SacrificeFlies, SacrificeBunts, GroundedIntoDoublePlay;

    public int Singles => Hits - Doubles - Triples - HomeRuns;
    public int TotalBases => Singles + Doubles * 2 + Triples * 3 + HomeRuns * 4;

    public float Average => AtBats > 0 ? Hits / (float)AtBats : 0f;

    /// <summary>
    /// The real formula. This used to be (H + BB) / PA, which is close but not the same thing —
    /// it charged the hitter for sacrifice bunts and gave him nothing for being hit.
    /// </summary>
    public float OnBase
    {
        get
        {
            float chances = AtBats + Walks + HitByPitch + SacrificeFlies;
            return chances > 0 ? (Hits + Walks + HitByPitch) / chances : 0f;
        }
    }

    public float Slugging => AtBats > 0 ? TotalBases / (float)AtBats : 0f;
    public float Ops => OnBase + Slugging;

    /// <summary>Isolated power: slugging with the singles taken out.</summary>
    public float Iso => Slugging - Average;

    /// <summary>How often a ball put in play fell in. Separates luck from contact.</summary>
    public float Babip
    {
        get
        {
            float inPlay = AtBats - Strikeouts - HomeRuns + SacrificeFlies;
            return inPlay > 0 ? (Hits - HomeRuns) / inPlay : 0f;
        }
    }

    public float StrikeoutRate => PlateAppearances > 0 ? Strikeouts / (float)PlateAppearances : 0f;
    public float WalkRate => PlateAppearances > 0 ? Walks / (float)PlateAppearances : 0f;

    /// <summary>Steals as a share of attempts. Below about .700 and he is costing runs.</summary>
    public float StealRate
    {
        get
        {
            int tries = StolenBases + CaughtStealing;
            return tries > 0 ? StolenBases / (float)tries : 0f;
        }
    }

    public void Absorb(BattingLine o)
    {
        Games += o.Games; PlateAppearances += o.PlateAppearances; AtBats += o.AtBats;
        Hits += o.Hits; Doubles += o.Doubles; Triples += o.Triples; HomeRuns += o.HomeRuns;
        Runs += o.Runs; RunsBattedIn += o.RunsBattedIn; Walks += o.Walks;
        Strikeouts += o.Strikeouts; StolenBases += o.StolenBases;
        HitByPitch += o.HitByPitch; IntentionalWalks += o.IntentionalWalks;
        CaughtStealing += o.CaughtStealing; SacrificeFlies += o.SacrificeFlies;
        SacrificeBunts += o.SacrificeBunts; GroundedIntoDoublePlay += o.GroundedIntoDoublePlay;
    }

    /// <summary>Batting averages are shown without the leading zero, the way a scoreboard does.</summary>
    public static string Rate(float value) => value.ToString("F3").TrimStart('0');
}

/// <summary>A pitcher's accumulated line. Innings are stored as outs so thirds stay exact.</summary>
public sealed class PitchingLine
{
    public int Games, GamesStarted, Outs, Hits, Runs, EarnedRuns;
    public int Walks, Strikeouts, HomeRunsAllowed, Wins, Losses, Saves, Pitches;

    // The rest of a staff's ledger. Holds and blown saves are how a bullpen is actually judged,
    // and neither was being kept.
    public int HitBatters, IntentionalWalksIssued, WildPitches, BattersFaced;
    public int Holds, BlownSaves, CompleteGames, Shutouts, QualityStarts;

    public float InningsPitched => Outs / 3f;
    public float Era => Outs > 0 ? EarnedRuns * 27f / Outs : 0f;
    public float Whip => Outs > 0 ? (Walks + Hits) * 3f / Outs : 0f;

    /// <summary>Innings the traditional way: 6.2 means six and two thirds.</summary>
    public string InningsText => $"{Outs / 3}.{Outs % 3}";

    public float StrikeoutsPer9 => Outs > 0 ? Strikeouts * 27f / Outs : 0f;
    public float WalksPer9 => Outs > 0 ? Walks * 27f / Outs : 0f;
    public float HomeRunsPer9 => Outs > 0 ? HomeRunsAllowed * 27f / Outs : 0f;

    /// <summary>Strikeouts per walk. The one number that survives a change of defence behind him.</summary>
    public float StrikeoutToWalk => Walks > 0 ? Strikeouts / (float)Walks : Strikeouts;

    /// <summary>
    /// Fielding independent pitching: what his ERA would be if every ball in play were average.
    /// The 3.10 constant is the usual scaling that puts FIP on the same scale as ERA.
    /// </summary>
    public float Fip => Outs > 0
        ? (13f * HomeRunsAllowed + 3f * (Walks + HitBatters) - 2f * Strikeouts) * 3f / Outs + 3.10f
        : 0f;

    /// <summary>How often the opposition reached. Needs batters faced, so it is new here.</summary>
    public float OpponentAverage
    {
        get
        {
            int atBats = BattersFaced - Walks - HitBatters;
            return atBats > 0 ? Hits / (float)atBats : 0f;
        }
    }

    public void Absorb(PitchingLine o)
    {
        Games += o.Games; GamesStarted += o.GamesStarted; Outs += o.Outs; Hits += o.Hits;
        Runs += o.Runs; EarnedRuns += o.EarnedRuns; Walks += o.Walks; Strikeouts += o.Strikeouts;
        HomeRunsAllowed += o.HomeRunsAllowed; Wins += o.Wins; Losses += o.Losses;
        Saves += o.Saves; Pitches += o.Pitches;
        HitBatters += o.HitBatters; IntentionalWalksIssued += o.IntentionalWalksIssued;
        WildPitches += o.WildPitches; BattersFaced += o.BattersFaced;
        Holds += o.Holds; BlownSaves += o.BlownSaves; CompleteGames += o.CompleteGames;
        Shutouts += o.Shutouts; QualityStarts += o.QualityStarts;
    }
}

/// <summary>A club's won-lost record.</summary>
public sealed class TeamRecord
{
    public int Wins, Losses, RunsScored, RunsAllowed;

    public int Games => Wins + Losses;
    public float WinPct => Games > 0 ? Wins / (float)Games : 0f;
    public int RunDifferential => RunsScored - RunsAllowed;

    public string WinPctText => WinPct.ToString("F3").TrimStart('0');

    /// <summary>
    /// The record this club's run differential says it deserved. A club well above it has been
    /// lucky in close games, and is the one that falls back in September.
    /// </summary>
    public float ExpectedWinPct
    {
        get
        {
            float rs = Mathf.Pow(RunsScored, 1.83f);
            float ra = Mathf.Pow(RunsAllowed, 1.83f);
            return rs + ra > 0f ? rs / (rs + ra) : 0f;
        }
    }

    public int ExpectedWins => Mathf.RoundToInt(ExpectedWinPct * Games);

    /// <summary>Games behind the given leader.</summary>
    public float GamesBehind(TeamRecord leader) =>
        ((leader.Wins - Wins) + (Losses - leader.Losses)) / 2f;
}
