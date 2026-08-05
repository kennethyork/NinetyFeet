using System.Linq;
using Godot;
using SandlotSlugfest.Data;
using SandlotSlugfest.Season;

namespace SandlotSlugfest.Net;

/// <summary>
/// Asks the question an online league stands or falls on: do two machines building the same
/// league from the same seed still agree a month later?
///
/// Netplay already proves it for one game. A season is the same idea over a far longer horizon,
/// and the failure mode is much worse — a desync inside a game costs you the game and you see it
/// at once, while a desync on the third of April costs you the season and you find out in August
/// when the two sides disagree about who is in first place.
///
/// So this builds two leagues side by side, advances them a day at a time, and fingerprints both
/// after every day. The day they stop matching is the day something in the simulation is reading
/// state that is not in the seed, and it names the clubs that differ rather than only saying so.
/// </summary>
public static class DeterminismAudit
{
    public static void Run(int days)
    {
        GD.Print($"\n=== DETERMINISM — two leagues, one seed, {days} days ===\n");

        var a = new SeasonState();
        var b = new SeasonState();
        a.StartNew(RosterGenerator.DefaultLeagueSeed, 0, Schedule.FullSeason, 9);
        b.StartNew(RosterGenerator.DefaultLeagueSeed, 0, Schedule.FullSeason, 9);

        ulong fa = LeagueFingerprint.Of(a);
        ulong fb = LeagueFingerprint.Of(b);
        GD.Print($"  before a ball is thrown:  {fa:X16}  {fb:X16}   " +
                 $"{(fa == fb ? "match" : "ALREADY DIFFERENT")}");

        if (fa != fb)
        {
            GD.Print("\n  The two leagues differ before any game is played, so the difference is in");
            GD.Print("  how a league is built rather than how it is simulated.");
            GD.Print(LeagueFingerprint.Diff(a, b));
            return;
        }

        int diverged = -1;

        for (int d = 1; d <= days; d++)
        {
            a.AdvanceDay(simulateUserGame: true);
            b.AdvanceDay(simulateUserGame: true);

            fa = LeagueFingerprint.Of(a);
            fb = LeagueFingerprint.Of(b);

            if (fa != fb) { diverged = d; break; }
            if (d % 10 == 0) GD.Print($"  day {d,3}:  {fa:X16}   match   ({a.GamesPlayed} games)");
        }

        if (diverged < 0)
        {
            GD.Print($"\n  {days} days, identical every single day.");
            GD.Print($"  final fingerprint {fa:X16} over {a.GamesPlayed} games played.");
            // Drift detection is tested first, deliberately. Loading a league rebuilds rosters
            // through the generator's cache, which both of these leagues are holding references
            // into — so a save round trip run beforehand leaves the two of them genuinely
            // different and the drift test would then be reporting that instead of its own nudge.
            NoticesADrift(a, b);
            SurvivesASave(a, fa);
            GD.Print("\n  The season is replayable from its seed, which is the floor an online");
            GD.Print("  league stands on: two machines can hold the same league and exchange only");
            GD.Print("  what the humans decide.");
            GD.Print("\n  This is the idle case, and it is the easy half. --league adds the two");
            GD.Print("  people: games each of them plays by hand, which the other machine cannot");
            GD.Print("  derive and has to be sent. --netleague runs that over a real socket.");
            return;
        }

        GD.Print($"\n  DIVERGED on day {diverged}.");
        GD.Print($"    {fa:X16}");
        GD.Print($"    {fb:X16}");
        GD.Print(LeagueFingerprint.Diff(a, b));
        GD.Print("\n  Something in the simulation is reading state that is not in the seed. Until");
        GD.Print("  that is found, an online league cannot work — the two sides would drift apart");
        GD.Print("  on their own with nobody touching anything.");
    }

    /// <summary>
    /// And it has to survive being put down and picked up again.
    ///
    /// Two machines agreeing while both stay running is not enough for a league: people quit, and
    /// they come back. If a league's fingerprint changes across a save and a load — a stat that
    /// is not written out, a roster that comes back in a different order, a field defaulted rather
    /// than restored — then the two sides diverge the moment one of them closes the game, and
    /// nothing in the netcode would ever catch it, because both machines would be behaving
    /// perfectly. It is the quietest way this feature could fail.
    /// </summary>
    private static void SurvivesASave(SeasonState league, ulong before)
    {
        const int Scratch = 3;      // the fourth slot, which the slot audit also leaves alone

        if (SaveGame.Occupied(Scratch))
        {
            GD.Print("\n  slot 4 is in use, so the save round trip was not tested.");
            return;
        }

        int was = SaveGame.Slot;
        SaveGame.Slot = Scratch;
        SaveGame.Save(league);

        var back = SaveGame.Load();
        ulong after = LeagueFingerprint.Of(back);

        string path = SaveGame.PathFor(Scratch);
        if (FileAccess.FileExists(path))
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        SaveGame.Slot = was;

        GD.Print($"\n  after a save and a load:  {after:X16}   " +
                 $"{(after == before ? "match" : "CHANGED")}");

        if (after == before)
        {
            GD.Print("  A league can be put down and picked up without moving, so a player");
            GD.Print("  quitting for the night does not silently split an online league in two.");
            return;
        }

        GD.Print("  The league is not the same after a round trip through its own save file.");
        GD.Print("  Two machines would drift apart the moment one of them closed the game, and");
        GD.Print("  the netcode would never see it — both sides would be behaving perfectly.");
        GD.Print(LeagueFingerprint.Diff(league, back));
    }

    /// <summary>
    /// And it has to notice when they do come apart.
    ///
    /// A checksum that never fires is not a safety net, it is a decoration, and the way to find
    /// out which one you have built is to break something on purpose. So one league is nudged —
    /// a single player, a single stat, the smallest change that can be made — and the fingerprint
    /// has to see it and the diff has to name the man.
    ///
    /// This matters more here than the matching case. Two leagues agreeing proves the simulation
    /// is tidy today; a detector that works proves the season is safe tomorrow, when somebody
    /// changes something that nobody thought of.
    /// </summary>
    private static void NoticesADrift(SeasonState a, SeasonState b)
    {
        var victim = b.RosterFor(0).Players.OrderBy(p => p.Id).FirstOrDefault();
        if (victim == null) { GD.Print("\n  nobody to nudge; drift detection untested."); return; }

        ulong before = LeagueFingerprint.Of(b);
        b.Book.Batting(victim).Hits += 1;
        ulong after = LeagueFingerprint.Of(b);

        GD.Print($"\n  one hit added to {victim.Name}, and nothing else:");
        GD.Print($"    {before:X16}  ->  {after:X16}   " +
                 $"{(before != after ? "seen" : "NOT SEEN")}");

        if (before == after)
        {
            GD.Print("  The fingerprint did not move. It cannot protect a league it cannot see.");
            b.Book.Batting(victim).Hits -= 1;
            return;
        }

        GD.Print("  and the diff says where:");
        GD.Print(LeagueFingerprint.Diff(a, b, 2));

        b.Book.Batting(victim).Hits -= 1;
        GD.Print($"\n  put back:  {LeagueFingerprint.Of(b):X16}   " +
                 $"{(LeagueFingerprint.Of(b) == before ? "matches again" : "DID NOT RECOVER")}");
    }
}
