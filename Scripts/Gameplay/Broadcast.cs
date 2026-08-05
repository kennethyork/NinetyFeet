using System.Linq;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.Season;
using SandlotSlugfest.Stats;

namespace SandlotSlugfest.Gameplay;

/// <summary>
/// The graphics a broadcast puts up, built from what the game already knows.
///
/// A televised game tells you who is coming up and why it matters before he swings, and the
/// telling is most of what makes it feel like an event rather than a simulation. This one told
/// you nothing: a name in the corner and a count.
///
/// Every line here is read from the season book, including the splits — so the card can say that
/// this hitter is .310 against right-handers and the man on the mound is a right-hander, which is
/// the single most useful sentence anybody could put on the screen at that moment. None of it is
/// invented for effect; if the number is not known yet, the line is left off.
/// </summary>
public static class Broadcast
{
    /// <summary>How long a matchup card stays up when a new man comes to the plate.</summary>
    public const float CardSeconds = 3.2f;

    public readonly struct Card
    {
        public readonly string Title;
        public readonly string Subtitle;
        public readonly string[] Lines;

        public Card(string title, string subtitle, params string[] lines)
        {
            Title = title; Subtitle = subtitle; Lines = lines ?? System.Array.Empty<string>();
        }

        public bool IsEmpty => Title == null;
    }

    /// <summary>The hitter, what he has done, and how he does against this arm in particular.</summary>
    public static Card Matchup(PlayerData batter, PlayerData pitcher, SeasonState league)
    {
        if (batter == null) return default;

        string hand = $"Bats {Platoon.Letter(batter.Bats)}";
        string sub = $"{PlayerData.PositionLabel(batter.Position)}  ·  {hand}  ·  age {batter.Age}";

        // Without a league behind it — a friendly, a moment — there are no numbers to show, and
        // making some up would be worse than saying nothing.
        if (league == null) return new Card(batter.Name, sub);

        var line = league.Book.Batting(batter);
        var lines = new System.Collections.Generic.List<string>();

        if (line.AtBats > 0)
            lines.Add($"{BattingLine.Rate(line.Average)} avg   {line.HomeRuns} HR   " +
                      $"{line.RunsBattedIn} RBI   {BattingLine.Rate(line.OnBase)} obp");

        // The split against the hand he is looking at, which is the reason the card is worth
        // putting up at all. Only shown once there are enough at-bats to mean anything.
        if (pitcher != null && league.Book.Splits.HasBatting(batter))
        {
            var slice = pitcher.Throws == Handedness.Left ? Split.VsLeft : Split.VsRight;
            var against = league.Book.Splits.Batting(batter).Peek(slice);
            if (against != null && against.AtBats >= 20)
                lines.Add($"{SplitBook.Label(slice)}:  {BattingLine.Rate(against.Average)}   " +
                          $"{against.HomeRuns} HR in {against.AtBats} at-bats");
        }

        // How he has been lately, from the games actually on file.
        var form = league.Logs.Recent(batter.Id, 5);
        if (form.Count > 0)
        {
            int h = form.Sum(f => f.Line.Batting?.Hits ?? 0);
            int ab = form.Sum(f => f.Line.Batting?.AtBats ?? 0);
            if (ab > 0) lines.Add($"Last {form.Count}: {h} for {ab}");
        }

        return new Card(batter.Name, sub, lines.ToArray());
    }

    /// <summary>The man on the mound, and what tonight has cost him so far.</summary>
    public static Card OnTheMound(PlayerData pitcher, SeasonState league, GameSituation sit,
        int pitchesToday)
    {
        if (pitcher == null) return default;

        string sub = $"{PlayerData.RoleLabel(pitcher.Role)}  ·  " +
                     $"Throws {Platoon.Letter(pitcher.Throws)}  ·  {pitcher.ArsenalText}";

        var lines = new System.Collections.Generic.List<string>();

        if (sit != null)
        {
            var today = sit.Stats.Pitching(pitcher);
            if (today.BattersFaced > 0)
                lines.Add($"Tonight: {today.InningsText} ip, {today.Hits} h, " +
                          $"{today.EarnedRuns} er, {today.Strikeouts} k   ·   {pitchesToday} pitches");
        }

        if (league != null)
        {
            var year = league.Book.Pitching(pitcher);
            if (year.Outs > 0)
                lines.Add($"Season: {year.Wins}-{year.Losses}, {year.Era:F2} era, " +
                          $"{year.Whip:F2} whip" + (year.Saves > 0 ? $", {year.Saves} sv" : ""));
        }

        return new Card(pitcher.Name, sub, lines.ToArray());
    }

    /// <summary>
    /// The card between innings: the line score so far, and the top of the order coming up.
    /// </summary>
    public static Card BetweenInnings(GameSituation sit)
    {
        if (sit == null) return default;

        string title = $"{sit.Away.Team.Abbrev} {sit.AwayScore}   " +
                       $"{sit.Home.Team.Abbrev} {sit.HomeScore}";

        var due = sit.BattingTeam;
        string coming = due?.DueUp != null ? $"Due up: {due.DueUp.Name}" : "";

        return new Card(title, sit.InningText,
            $"{sit.Away.Team.Abbrev}  {sit.AwayHits} hits, {sit.AwayErrors} errors",
            $"{sit.Home.Team.Abbrev}  {sit.HomeHits} hits, {sit.HomeErrors} errors",
            coming);
    }
}
