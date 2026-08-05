using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Season;

namespace SandlotSlugfest.Data;

/// <summary>
/// The written players against the generated ones, rating by rating.
///
/// Asked because turning the written players off — which somebody supplying his own names has to
/// do, since a written player takes a generated man's slot outright — moved the league's run
/// scoring from four percent under the majors to twelve percent under. That is not a small
/// difference and it was not predicted by anything.
///
/// It should not have been a surprise either. Sixteen of every club's twenty-seven men are hand
/// written: 512 of 869. The calibration has therefore never measured the generated players on
/// their own — it has always measured a league that is three-fifths authored, with the generator
/// filling in around them. If the two populations are not the same strength, then the run scoring
/// that was tuned to match the majors is partly a property of the written cast, and the generator
/// underneath it has been quietly carrying a bias nobody could see.
///
/// So this prints both populations side by side. It is a diagnosis, not a check: there is no
/// pass or fail here, only a number that explains the eight percent or fails to.
/// </summary>
public static class TalentAudit
{
    public static void Run()
    {
        GD.Print("\n=== WRITTEN AGAINST GENERATED ===\n");

        // Two leagues, not one.
        //
        // The first version of this took both populations from a single league, which is a trap.
        // A written player displaces a generated one, so the generated men left in that league are
        // the survivors — and whoever survives is not a fair sample of what the generator makes.
        // It reported generated hitters as *better* than written ones, which is the opposite of
        // the truth and would have sent the whole diagnosis the wrong way.
        //
        // The population that matters is the one a league without written players actually gets,
        // so that league is built on purpose and measured whole.
        RosterGenerator.IncludeLegends = true;
        RosterGenerator.ResetCache();
        var withThem = new SeasonState();
        withThem.StartNew(RosterGenerator.DefaultLeagueSeed, 0, Schedule.FullSeason, 9);
        var written = Teams.All.SelectMany(t => withThem.RosterFor(t.Id).Players)
            .Where(p => p.IsLegend).ToList();

        RosterGenerator.IncludeLegends = false;
        RosterGenerator.ResetCache();
        var without = new SeasonState();
        without.StartNew(RosterGenerator.DefaultLeagueSeed, 0, Schedule.FullSeason, 9);
        var made = Teams.All.SelectMany(t => without.RosterFor(t.Id).Players).ToList();

        var writtenBats = written.Where(p => p.Position != Position.P).ToList();
        var madeBats = made.Where(p => p.Position != Position.P).ToList();
        var writtenArms = written.Where(p => p.Position == Position.P).ToList();
        var madeArms = made.Where(p => p.Position == Position.P).ToList();

        GD.Print($"  {written.Count} written players, against the {made.Count} men a league gets");
        GD.Print("  when they are turned off — which is the comparison that decides whether the");
        GD.Print("  calibration survives being asked to do without them.\n");

        GD.Print("  HITTERS                written   generated      gap");
        Line("contact", writtenBats, madeBats, p => p.Contact);
        Line("power", writtenBats, madeBats, p => p.Power);
        Line("speed", writtenBats, madeBats, p => p.Speed);
        Line("fielding", writtenBats, madeBats, p => p.Fielding);
        Line("arm", writtenBats, madeBats, p => p.Arm);
        Line("overall", writtenBats, madeBats, p => p.Overall);

        GD.Print("\n  ARMS                   written   generated      gap");
        Line("velocity", writtenArms, madeArms, p => p.PitchPower);
        Line("command", writtenArms, madeArms, p => p.PitchControl);
        Line("stamina", writtenArms, madeArms, p => p.Stamina);
        Line("overall", writtenArms, madeArms, p => p.Overall);

        // The number that actually decides run scoring is not either population's strength, it is
        // the difference between them — a league of uniformly better players scores the same as a
        // league of uniformly worse ones.
        float batGap = Mean(writtenBats, p => p.Contact + p.Power) - Mean(madeBats, p => p.Contact + p.Power);
        float armGap = Mean(writtenArms, p => p.PitchPower + p.PitchControl)
                     - Mean(madeArms, p => p.PitchPower + p.PitchControl);

        GD.Print($"\n  bat advantage {batGap:+0.00;-0.00} points of contact+power,");
        GD.Print($"  arm advantage {armGap:+0.00;-0.00} points of velocity+command.");
        GD.Print($"  net, in the hitter's favour: {batGap - armGap:+0.00;-0.00}\n");

        // The ratings were the obvious suspect and they were innocent, which is worth saying out
        // loud rather than leaving for the next person to re-derive. Every reading on this page
        // pointed the wrong way: without the written players the lineups that play are *better*
        // and the rotations *worse*, so run scoring should have gone up. It went down eight
        // percent. Whatever moves a league that far is not on the ratings card, and the section
        // below is where it turned out to be.
        GD.Print(batGap > armGap
            ? "  On ratings alone, taking the written players out should lower run scoring."
            : "  On ratings alone, taking the written players out should raise run scoring.");
        GD.Print("  It is worth checking that against --sim 400 --nolegends before believing it:");
        GD.Print("  ratings are the obvious explanation for a change in run environment and they");
        GD.Print("  were, here, entirely the wrong one.");

        WhoActuallyPlays(withThem, without);

        // Where in the order the difference lands matters as much as its size: a written cast that
        // is stronger only at the top of the rotation moves fewer runs than one that is stronger
        // across the lineup.
        GD.Print("\n  by lineup slot, contact+power, written minus generated:");
        foreach (var pos in new[]
                 {
                     Position.C, Position.First, Position.Second, Position.Third,
                     Position.Short, Position.Left, Position.Center, Position.Right, Position.DH,
                 })
        {
            var w = writtenBats.Where(p => p.Position == pos).ToList();
            var m = madeBats.Where(p => p.Position == pos).ToList();
            if (w.Count == 0 || m.Count == 0) continue;

            float gap = Mean(w, p => p.Contact + p.Power) - Mean(m, p => p.Contact + p.Power);
            GD.Print($"    {pos,-8} {gap,6:+0.00;-0.00}   ({w.Count} written, {m.Count} generated)");
        }
    }

    /// <summary>
    /// The nine who bat and the five who start, which is the only comparison that decides a game.
    ///
    /// A roster is twenty-seven men and a ballgame is played by fourteen of them. Comparing whole
    /// populations answers a question nobody asked: the bench and the back of the bullpen are in
    /// that average and are hardly ever on the field. If the two leagues' *lineups* are the same
    /// strength then the ratings are not the cause of anything, whatever the roster means say.
    /// </summary>
    private static void WhoActuallyPlays(SeasonState withThem, SeasonState without)
    {
        GD.Print("\n  WHO ACTUALLY PLAYS       with written   without         gap");

        var lineupA = Teams.All.SelectMany(t => withThem.RosterFor(t.Id).BattingOrder).ToList();
        var lineupB = Teams.All.SelectMany(t => without.RosterFor(t.Id).BattingOrder).ToList();

        Line("lineup contact", lineupA, lineupB, p => p.Contact);
        Line("lineup power", lineupA, lineupB, p => p.Power);
        Line("lineup speed", lineupA, lineupB, p => p.Speed);
        Line("lineup overall", lineupA, lineupB, p => p.Overall);

        var rotA = Teams.All.SelectMany(t => withThem.RosterFor(t.Id).Rotation).ToList();
        var rotB = Teams.All.SelectMany(t => without.RosterFor(t.Id).Rotation).ToList();

        Line("rotation velocity", rotA, rotB, p => p.PitchPower);
        Line("rotation command", rotA, rotB, p => p.PitchControl);
        Line("rotation stamina", rotA, rotB, p => p.Stamina);
        Line("rotation overall", rotA, rotB, p => p.Overall);

        var penA = Teams.All.SelectMany(t => withThem.RosterFor(t.Id).Bullpen).ToList();
        var penB = Teams.All.SelectMany(t => without.RosterFor(t.Id).Bullpen).ToList();

        Line("bullpen velocity", penA, penB, p => p.PitchPower);
        Line("bullpen command", penA, penB, p => p.PitchControl);
        Line("bullpen overall", penA, penB, p => p.Overall);

        GD.Print($"\n    ({lineupA.Count} against {lineupB.Count} in the orders, " +
                 $"{rotA.Count} against {rotB.Count} in the rotations, " +
                 $"{penA.Count} against {penB.Count} in the pens)");

        float bats = Mean(lineupA, p => p.Contact + p.Power + p.Speed)
                   - Mean(lineupB, p => p.Contact + p.Power + p.Speed);
        float arms = Mean(rotA.Concat(penA).ToList(), p => p.PitchPower + p.PitchControl)
                   - Mean(rotB.Concat(penB).ToList(), p => p.PitchPower + p.PitchControl);

        GD.Print($"\n    the lineups that play are {bats:+0.00;-0.00} points of contact+power+speed" +
                 " apart,");
        GD.Print($"    the staffs that pitch are {arms:+0.00;-0.00} points of velocity+command apart.");

        // If the ratings say one thing and the run scoring says the opposite, the cause is
        // something the ratings do not describe. There are only three such things a player
        // carries, so all three are printed rather than guessed at one at a time.
        GD.Print("\n  WHAT THE RATINGS DO NOT SAY");

        Hands("lineups", lineupA, lineupB);
        Hands("rotations", rotA, rotB);
        Hands("bullpens", penA, penB);

        Share("lineup specials", lineupA, lineupB, p => p.Special != Special.None);
        Share("staff specials", rotA.Concat(penA).ToList(), rotB.Concat(penB).ToList(),
            p => p.Special != Special.None);

        // And which sort. A special is not a decoration: ContactMaster widens the sweet spot by a
        // third, VacuumGlove more than doubles a fielder's catch radius. Whether the men in the
        // lineup carry bat specials or glove ones is worth more runs than any rating on this page.
        Share("  of those, bat", lineupA, lineupB, p => Bat.Contains(p.Special));
        Share("  of those, glove", lineupA, lineupB, p => Glove.Contains(p.Special));

        // The platoon is the one that would do it. A hitter facing the same hand is measurably
        // worse off, and if one league sets more of those matchups than the other then the run
        // scoring moves without a single rating changing.
        GD.Print($"\n    left-handed hitting, lineups:   " +
                 $"{Pct(lineupA, p => p.Bats != Handedness.Right)} with, " +
                 $"{Pct(lineupB, p => p.Bats != Handedness.Right)} without");
        GD.Print($"    left-handed pitching, staffs:   " +
                 $"{Pct(rotA.Concat(penA).ToList(), p => p.Throws == Handedness.Left)} with, " +
                 $"{Pct(rotB.Concat(penB).ToList(), p => p.Throws == Handedness.Left)} without");
    }

    private static readonly Special[] Bat =
    {
        Special.MoonShot, Special.ContactMaster, Special.BuntMaster, Special.TurboLegs,
        Special.SprayHitter, Special.GapPower, Special.PinchRunner,
    };

    private static readonly Special[] Glove =
    {
        Special.VacuumGlove, Special.CannonArm, Special.WallClimber, Special.Backstop,
    };

    private static string Pct(List<PlayerData> who, System.Func<PlayerData, bool> is_) =>
        who.Count == 0 ? "—" : $"{100f * who.Count(is_) / who.Count:0.0}%";

    private static void Hands(string label, List<PlayerData> a, List<PlayerData> b)
    {
        string Shape(List<PlayerData> who) =>
            $"L {Pct(who, p => p.Bats == Handedness.Left)}  " +
            $"R {Pct(who, p => p.Bats == Handedness.Right)}  " +
            $"S {Pct(who, p => p.Bats == Handedness.Switch)}  " +
            $"throws L {Pct(who, p => p.Throws == Handedness.Left)}";

        GD.Print($"    {label,-12} with     {Shape(a)}");
        GD.Print($"    {label,-12} without  {Shape(b)}");
    }

    private static void Share(string label, List<PlayerData> a, List<PlayerData> b,
        System.Func<PlayerData, bool> is_)
    {
        GD.Print($"    {label,-18} {Pct(a, is_),8} with, {Pct(b, is_),8} without");
    }

    private static float Mean(List<PlayerData> who, System.Func<PlayerData, float> of) =>
        who.Count == 0 ? 0f : who.Sum(of) / who.Count;

    private static void Line(string label, List<PlayerData> written, List<PlayerData> made,
        System.Func<PlayerData, float> of)
    {
        float w = Mean(written, of);
        float m = Mean(made, of);
        GD.Print($"  {label,-18} {w,9:0.00} {m,11:0.00} {w - m,8:+0.00;-0.00}");
    }
}
