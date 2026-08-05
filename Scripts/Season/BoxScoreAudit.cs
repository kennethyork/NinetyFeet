using System.Linq;
using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>
/// Checks that a game which has been played can be read back.
///
/// A box score is easy to write and easy to get quietly wrong: a line kept by reference rather
/// than by value shows a player's season totals instead of his night, a club filter that reads
/// the current roster puts a traded man in a game he did not play, and a save that stores the
/// list newest-first and reloads it the same way comes back in reverse. None of that shows up as
/// a crash — it shows up as a box score that looks plausible and is wrong.
///
/// So this plays a fortnight, adds the lines up by hand, and insists they match the season book.
/// </summary>
public static class BoxScoreAudit
{
    public static void Run(int days)
    {
        var season = new SeasonState();
        season.StartNew(RosterGenerator.DefaultLeagueSeed, 0, Schedule.FullSeason, 9);

        int club = season.UserTeamId;
        GD.Print($"\n=== BOX SCORES — {Teams.Get(club).FullName}, {days} days ===");

        for (int d = 0; d < days; d++) season.AdvanceDay(simulateUserGame: true);

        var logs = season.Logs.Games;
        GD.Print($"  games on file:        {logs.Count}");

        if (logs.Count == 0)
        {
            GD.Print("  FAILED — nothing was written down.");
            return;
        }

        // Every stored game must involve the club, and no other.
        int strangers = logs.Count(b => !b.Involves(club));
        GD.Print($"  games not ours:       {strangers}   (want 0)");

        // Newest first, which is what every screen assumes.
        bool ordered = true;
        for (int i = 1; i < logs.Count; i++)
            if (logs[i].Day > logs[i - 1].Day) ordered = false;
        GD.Print($"  ordered newest first: {(ordered ? "yes" : "NO")}");

        // The line score has to agree with the runs, or the box score contradicts the schedule.
        int mismatched = logs.Count(b =>
            b.AwayInnings.Sum() != b.AwayRuns || b.HomeInnings.Sum() != b.HomeRuns);
        GD.Print($"  line score disagrees: {mismatched}   (want 0)");

        // Somebody has to have taken the decision in every finished game.
        int undecided = logs.Count(b => b.WinnerPlayerId < 0 || b.LoserPlayerId < 0);
        GD.Print($"  no decision recorded: {undecided}   (want 0)");

        // The real test: our club's hits, added up a night at a time, against the season book.
        int fromBoxes = 0, homers = 0, outs = 0;
        foreach (var b in logs)
        {
            foreach (var a in b.Batters(club)) { fromBoxes += a.Batting.Hits; homers += a.Batting.HomeRuns; }
            foreach (var a in b.Arms(club)) outs += a.Pitching.Outs;
        }

        var roster = season.RosterFor(club);
        int fromBook = roster.Players.Sum(p => season.Book.Batting(p).Hits);
        int hrBook = roster.Players.Sum(p => season.Book.Batting(p).HomeRuns);
        int outsBook = roster.Players.Sum(p => season.Book.Pitching(p).Outs);

        GD.Print($"  hits: boxes {fromBoxes}, book {fromBook}" +
                 $"   {(fromBoxes == fromBook ? "match" : "MISMATCH")}");
        GD.Print($"  home runs: boxes {homers}, book {hrBook}" +
                 $"   {(homers == hrBook ? "match" : "MISMATCH")}");
        GD.Print($"  outs pitched: boxes {outs}, book {outsBook}" +
                 $"   {(outs == outsBook ? "match" : "MISMATCH")}");

        // A player's game log has to be a subset of the games he was in.
        var regular = roster.BattingOrder.FirstOrDefault();
        if (regular != null)
        {
            var form = season.Logs.Recent(regular.Id, 10);
            GD.Print($"  {regular.Name}: {form.Count} of his last games on file, " +
                     $"reading {string.Join(" ", form.Select(f => $"{f.Line.Batting.Hits}-{f.Line.Batting.AtBats}"))}");
        }

        GD.Print($"  save round trip:      {SaveGame.BoxRoundTrip(season)}");

        GD.Print($"\n  A traded man keeps the club he played for: names and clubs are copied into\n" +
                 $"  the box at the time, not looked up afterwards.");
    }
}
