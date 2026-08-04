using Godot;

namespace SandlotSlugfest.Stats;

/// <summary>A hitter's accumulated line.</summary>
public sealed class BattingLine
{
    public int Games, PlateAppearances, AtBats, Hits, Doubles, Triples, HomeRuns;
    public int Runs, RunsBattedIn, Walks, Strikeouts, StolenBases;

    public int Singles => Hits - Doubles - Triples - HomeRuns;
    public int TotalBases => Singles + Doubles * 2 + Triples * 3 + HomeRuns * 4;

    public float Average => AtBats > 0 ? Hits / (float)AtBats : 0f;
    public float OnBase => PlateAppearances > 0 ? (Hits + Walks) / (float)PlateAppearances : 0f;
    public float Slugging => AtBats > 0 ? TotalBases / (float)AtBats : 0f;
    public float Ops => OnBase + Slugging;

    public void Absorb(BattingLine o)
    {
        Games += o.Games; PlateAppearances += o.PlateAppearances; AtBats += o.AtBats;
        Hits += o.Hits; Doubles += o.Doubles; Triples += o.Triples; HomeRuns += o.HomeRuns;
        Runs += o.Runs; RunsBattedIn += o.RunsBattedIn; Walks += o.Walks;
        Strikeouts += o.Strikeouts; StolenBases += o.StolenBases;
    }

    /// <summary>Batting averages are shown without the leading zero, the way a scoreboard does.</summary>
    public static string Rate(float value) => value.ToString("F3").TrimStart('0');
}

/// <summary>A pitcher's accumulated line. Innings are stored as outs so thirds stay exact.</summary>
public sealed class PitchingLine
{
    public int Games, GamesStarted, Outs, Hits, Runs, EarnedRuns;
    public int Walks, Strikeouts, HomeRunsAllowed, Wins, Losses, Saves, Pitches;

    public float InningsPitched => Outs / 3f;
    public float Era => Outs > 0 ? EarnedRuns * 27f / Outs : 0f;
    public float Whip => Outs > 0 ? (Walks + Hits) * 3f / Outs : 0f;

    /// <summary>Innings the traditional way: 6.2 means six and two thirds.</summary>
    public string InningsText => $"{Outs / 3}.{Outs % 3}";

    public void Absorb(PitchingLine o)
    {
        Games += o.Games; GamesStarted += o.GamesStarted; Outs += o.Outs; Hits += o.Hits;
        Runs += o.Runs; EarnedRuns += o.EarnedRuns; Walks += o.Walks; Strikeouts += o.Strikeouts;
        HomeRunsAllowed += o.HomeRunsAllowed; Wins += o.Wins; Losses += o.Losses;
        Saves += o.Saves; Pitches += o.Pitches;
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

    /// <summary>Games behind the given leader.</summary>
    public float GamesBehind(TeamRecord leader) =>
        ((leader.Wins - Wins) + (Losses - leader.Losses)) / 2f;
}
