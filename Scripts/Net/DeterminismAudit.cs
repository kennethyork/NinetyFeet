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
            SurvivesASave(a, fa);
            GD.Print("\n  The season is replayable from its seed, which is what an online league");
            GD.Print("  needs: two machines can hold the same league and exchange only what the");
            GD.Print("  humans decide. The remaining work is routing those decisions through the");
            GD.Print("  sequencer netplay already has, and comparing this number every day so a");
            GD.Print("  drift is caught on the day it happens rather than in August.");
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
}
