using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Data;
using SandlotSlugfest.Season;

namespace SandlotSlugfest.Net;

/// <summary>
/// Two owners, one league, and the question the whole feature turns on: does a game one of them
/// played by hand land on the other machine as the same game?
///
/// <see cref="DeterminismAudit"/> proved the easy half — two leagues built from one seed and left
/// alone stay identical for a season. That is necessary and it is not sufficient, because the
/// point of a shared dynasty is that the two people do things, and the thing they mostly do is
/// play ballgames the other machine has no way to re-derive. Those results travel as a packet, and
/// a packet that leaves anything out breaks the league permanently and silently.
///
/// So this runs the real arrangement. Two leagues from one seed. Club A's games are played on the
/// first and posted to the second; club B's are played on the second and posted to the first; every
/// other game in the league is simulated by both. Then both fingerprints, every single day.
///
/// The "played" games are deliberately simulated from a seed the league itself would never use.
/// That is the property under test: a result neither machine could work out for itself, that has to
/// arrive over the wire intact or not at all. Playing them from the league's own seed would make
/// the whole audit agree with itself for the wrong reason.
/// </summary>
public static class LeagueAudit
{
    /// <summary>
    /// The two clubs with a person behind them.
    ///
    /// The second is not a fixed number. It is whoever the first plays on opening day, so the run
    /// is guaranteed to contain the fixture the two owners are both in — the one neither of them
    /// posts and both of them settle. Picking club 1 out of the air ran sixty days without the two
    /// ever meeting, and passed while saying nothing about the one case that is different.
    /// </summary>
    private const int OwnerA = 0;
    private static int OwnerB = 1;

    /// <summary>Whoever a club plays first, which is the same on both machines.</summary>
    public static int FirstOpponentOf(int clubId, int seed, int gamesPerTeam)
    {
        var fixtures = Schedule.Build(gamesPerTeam, seed);
        var first = fixtures.FirstOrDefault(g => g.Involves(clubId));
        return first == null ? (clubId + 1) % Teams.All.Count
             : first.AwayId == clubId ? first.HomeId : first.AwayId;
    }

    public static void Run(int days)
    {
        OwnerB = FirstOpponentOf(OwnerA, RosterGenerator.DefaultLeagueSeed, Schedule.FullSeason);

        GD.Print($"\n=== ONLINE LEAGUE — two owners, one league, {days} days ===\n");
        GD.Print($"  {Teams.Get(OwnerA).FullName} is run from the first machine,");
        GD.Print($"  {Teams.Get(OwnerB).FullName} from the second. Everything else is simulated");
        GD.Print("  by both. Games their owners play are posted across as packets.\n");

        var a = new SeasonState();
        var b = new SeasonState();
        a.StartNew(RosterGenerator.DefaultLeagueSeed, OwnerA, Schedule.FullSeason, 9);
        b.StartNew(RosterGenerator.DefaultLeagueSeed, OwnerB, Schedule.FullSeason, 9);

        if (LeagueFingerprint.Of(a) != LeagueFingerprint.Of(b))
        {
            GD.Print("  The two leagues differ before a ball is thrown, so nothing below means");
            GD.Print("  anything. Run --determinism.");
            GD.Print(LeagueFingerprint.Diff(a, b));
            return;
        }

        SeesALineup(a, b);

        int posted = 0, cardsChanged = 0, refused = 0, derbies = 0;
        int diverged = -1;
        string why = "";

        for (int d = 1; d <= days && diverged < 0; d++)
        {
            // Each owner plays whatever his club has on today, on his own machine, and posts it.
            // The order the two packets are applied in does not matter — the book adds, the two
            // games touch different clubs — but it is fixed anyway, because "does not matter" is
            // a claim and a fixed order is a guarantee.
            foreach (int owner in new[] { OwnerA, OwnerB })
            {
                var mine = owner == OwnerA ? a : b;
                var theirs = owner == OwnerA ? b : a;

                foreach (var fixture in mine.Games
                             .Where(g => g.Day == mine.CurrentDay && !g.Played && g.Involves(owner))
                             .ToList())
                {
                    // Both owners in the same ballgame is the one case with no packet in it.
                    // Neither can send the other a result he was also in, so both settle it from
                    // the fixture's own seed — the only answer the two machines can both arrive
                    // at — and nothing crosses the wire. Each league does it on its own pass,
                    // which is exactly what the two machines do.
                    if (fixture.Involves(OwnerA) && fixture.Involves(OwnerB))
                    {
                        if (owner == OwnerA) derbies++;
                        mine.RecordPlayedGame(fixture, mine.Resolve(fixture));
                        continue;
                    }

                    var before = Card(mine, owner);
                    var sit = PlayByHand(mine, fixture);
                    if (Card(mine, owner) != before) cardsChanged++;

                    int[] packet = GameResult.Pack(mine, fixture, sit);
                    mine.RecordPlayedGame(fixture, sit);

                    string trouble = GameResult.Apply(theirs, packet);
                    if (trouble != null)
                    {
                        refused++;
                        GD.Print($"  day {d}: the packet was refused — {trouble}");
                        continue;
                    }
                    posted++;
                }
            }

            // Now the day turns over on both sides, and every game neither owner played is
            // simulated by both. The games above are already in the books, so this steps over them.
            a.AdvanceDay(simulateUserGame: true);
            b.AdvanceDay(simulateUserGame: true);

            ulong fa = LeagueFingerprint.Of(a);
            ulong fb = LeagueFingerprint.Of(b);

            if (fa != fb)
            {
                diverged = d;
                why = $"{fa:X16} against {fb:X16}";
                break;
            }

            if (d % 10 == 0)
                GD.Print($"  day {d,3}:  {fa:X16}   match   " +
                         $"({a.GamesPlayed} games, {posted} posted across)");
        }

        GD.Print("");

        if (diverged >= 0)
        {
            GD.Print($"  DIVERGED on day {diverged}:  {why}");
            GD.Print(LeagueFingerprint.Diff(a, b));
            GD.Print("\n  Something a played game changes is not in the packet. Whatever the diff");
            GD.Print("  names above is the field that is missing from GameResult.Pack.");
            return;
        }

        GD.Print($"  {days} days, identical every day.");
        GD.Print($"  {posted} games posted across, {a.GamesPlayed} played in the league.");
        GD.Print($"  the two owners met {derbies} times" +
                 (derbies == 0
                     ? " — THAT FIXTURE WENT UNTESTED"
                     : ", settled by both machines and posted by neither."));
        GD.Print($"  the lineup card changed during {cardsChanged} of those {posted} games" +
                 (cardsChanged == 0
                     ? " — see below" : ", and travelled."));
        if (refused > 0) GD.Print($"  {refused} packets were refused outright.");

        if (cardsChanged == 0 && posted > 0)
        {
            GD.Print("\n  No pinch hitter appeared in any of them, so the hardest part of the");
            GD.Print("  packet went untested by the run above. Testing it deliberately:");
            PinchHitTravels(a, b);
        }

        GD.Print("\n  A season can be shared: each owner plays his own club, the results cross,");
        GD.Print("  and the two leagues stay the same league. What remains is the wire, not the");
        GD.Print("  model.");
    }

    /// <summary>
    /// A ballgame the league could not have worked out for itself.
    ///
    /// A human at the plate is not reproducible, which is the entire reason the packet exists, so
    /// the audit must not use a result the other machine could arrive at independently. Simulating
    /// from a seed well outside the league's own gives exactly that: a legitimate ballgame, played
    /// with the right men under the right conditions, that only one of the two machines can know.
    /// </summary>
    private static Core.GameSituation PlayByHand(SeasonState season, ScheduledGame game) =>
        season.Resolve(game, unchecked(
            season.LeagueSeed * 15_485_863 + game.Day * 2_654_435 + game.HomeId * 97 + 41));

    private static string Card(SeasonState s, int teamId) =>
        string.Join(",", s.RosterFor(teamId).BattingOrder.Select(p => p?.Id ?? -1));

    /// <summary>
    /// Proves the fingerprint can see a lineup card at all.
    ///
    /// It could not until this feature was built: two leagues holding identical men with identical
    /// statistics and identical standings hashed the same however differently they were lined up,
    /// which meant the daily check would have called an already-broken league healthy right up to
    /// the moment it produced different results. A detector that cannot see the thing it is
    /// guarding is a decoration, so it gets broken on purpose here.
    /// </summary>
    private static void SeesALineup(SeasonState a, SeasonState b)
    {
        var club = b.RosterFor(OwnerA);
        if (club.BattingOrder.Count < 2) { GD.Print("  no lineup to shuffle."); return; }

        ulong before = LeagueFingerprint.Of(b);
        (club.BattingOrder[0], club.BattingOrder[1]) = (club.BattingOrder[1], club.BattingOrder[0]);
        ulong after = LeagueFingerprint.Of(b);

        GD.Print($"  two men swapped in one batting order, nothing else:");
        GD.Print($"    {before:X16}  ->  {after:X16}   {(before != after ? "seen" : "NOT SEEN")}");

        if (before == after)
            GD.Print("    The fingerprint is blind to the lineup. It cannot guard a shared league.");

        (club.BattingOrder[0], club.BattingOrder[1]) = (club.BattingOrder[1], club.BattingOrder[0]);
        GD.Print($"    put back:  {LeagueFingerprint.Of(b):X16}   " +
                 $"{(LeagueFingerprint.Of(b) == before ? "matches again" : "DID NOT RECOVER")}\n");
    }

    /// <summary>
    /// The pinch hitter, tested on purpose in case no game in the run above produced one.
    ///
    /// A substitution is the quietest thing a played game can leave behind: it changes nobody's
    /// numbers and nobody's record, only who is in the order — so it is invisible today and
    /// decides everything tomorrow. If the audit above happens not to exercise it, it is not
    /// allowed to pass by saying nothing.
    /// </summary>
    private static void PinchHitTravels(SeasonState a, SeasonState b)
    {
        var club = a.RosterFor(OwnerA);
        var starter = club.BattingOrder.FirstOrDefault();
        var bench = club.Bench.FirstOrDefault(p => !club.BattingOrder.Contains(p));

        if (starter == null || bench == null) { GD.Print("    nobody on the bench to send up."); return; }

        var fixture = a.Games.FirstOrDefault(g => !g.Played && g.Involves(OwnerA)
                                                  && !g.Involves(OwnerB));
        if (fixture == null) { GD.Print("    no fixture left to play."); return; }

        var sit = PlayByHand(a, fixture);
        club.Substitute(starter, bench);            // he hits for him in the eighth
        GD.Print($"    {bench.Name} hits for {starter.Name} and stays in.");

        int[] packet = GameResult.Pack(a, fixture, sit);
        a.RecordPlayedGame(fixture, sit);

        string trouble = GameResult.Apply(b, packet);
        if (trouble != null) { GD.Print($"    the packet was refused — {trouble}"); return; }

        bool same = Card(a, OwnerA) == Card(b, OwnerA);
        GD.Print($"    the other machine now bats: {(same ? "the same nine" : "A DIFFERENT NINE")}");
        if (!same)
        {
            GD.Print($"      here:  {Card(a, OwnerA)}");
            GD.Print($"      there: {Card(b, OwnerA)}");
        }
    }
}
