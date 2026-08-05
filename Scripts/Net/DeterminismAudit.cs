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
}
