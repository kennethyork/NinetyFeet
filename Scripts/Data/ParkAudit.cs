using System.Linq;
using Godot;

namespace SandlotSlugfest.Data;

/// <summary>
/// What rebuilding a ballpark actually does.
///
/// Two things have to be true and they pull against each other. A rebuilt park must change the
/// baseball — a three-hundred-foot porch that produces the same number of home runs is a picture
/// of a fence, not a fence. And it must change nothing else: not the club, not the other
/// thirty-one grounds, not the file it was read from, and above all not what a harness measures.
///
/// So this moves one wall and counts the balls that clear it, and then goes looking for anything
/// else that moved.
/// </summary>
public static class ParkAudit
{
    private const int Victim = 0;

    public static void Run(int swings)
    {
        GD.Print($"\n=== BALLPARKS — one wall moved, {swings} balls in the air ===\n");

        // On for this run only. Every other harness leaves it off, which is the property being
        // checked at the end.
        ParkEdits.Enabled = true;
        ParkEdits.Load();

        string was = ParkEdits.Path;
        ParkEdits.Path = "user://stadiums.audit.cfg";      // never the player's own file
        ParkEdits.Load();
        Stadiums.Rebuild();

        var before = Stadiums.For(Victim);
        var original = (Name: before.Name, D: before.Distances.ToArray(), H: before.Heights.ToArray());

        GD.Print($"  {Teams.Get(Victim).FullName} play at {original.Name}");
        GD.Print($"    fences  {string.Join(" ", original.D.Select(d => $"{d,3:0}"))}");
        GD.Print($"    walls   {string.Join(" ", original.H.Select(h => $"{h,3:0}"))}");

        int outBefore = OverTheFence(swings);
        GD.Print($"    {outBefore} home runs in {swings} games there\n");

        // Now pull the fences in and drop the wall to nothing. A change nobody could miss, because
        // a check that only fires on a large change is still a check that fires.
        ParkEdits.Set(Victim, new ParkEdits.Edit
        {
            Name = "The Bandbox",
            Distances = new[] { 280f, 300f, 320f, 300f, 280f },
            Heights = new[] { 4f, 4f, 4f, 4f, 4f },
        });

        var after = Stadiums.For(Victim);
        GD.Print($"  rebuilt as {after.Name}");
        GD.Print($"    fences  {string.Join(" ", after.Distances.Select(d => $"{d,3:0}"))}");
        GD.Print($"    walls   {string.Join(" ", after.Heights.Select(h => $"{h,3:0}"))}");

        int outAfter = OverTheFence(swings);
        GD.Print($"    {outAfter} home runs in the same {swings} games\n");

        GD.Print(outAfter > outBefore
            ? $"  ok    the ground changed the baseball: {outAfter - outBefore} more balls out"
            : "  FAIL  the fences moved and nothing happened; the park is a picture, not a park");

        // The clamps. A file is hand-written and will contain a typo eventually, and a fence at
        // fifty feet has to be refused rather than played on.
        ParkEdits.Set(Victim, new ParkEdits.Edit
        {
            Distances = new[] { 10f, 10f, 10f, 10f, 10f },
            Heights = new[] { 900f, 900f, 900f, 900f, 900f },
            Air = 40f,
        });

        var silly = Stadiums.For(Victim);
        bool held = silly.Distances.All(d => d >= ParkEdits.MinDistance)
                 && silly.Heights.All(h => h <= ParkEdits.MaxHeight)
                 && silly.AirDensity <= ParkEdits.MaxAir;

        GD.Print(held
            ? $"  ok    absurd numbers were clamped: fences {silly.Distances[0]:0}, " +
              $"walls {silly.Heights[0]:0}, air {silly.AirDensity:0.00}"
            : "  FAIL  a typo in the file was played on as written");

        // A partial row is refused outright rather than padded out with guesses.
        ParkEdits.Set(Victim, new ParkEdits.Edit { Distances = new[] { 300f, 320f } });
        bool refused = Stadiums.For(Victim).Distances.Length == 5
                    && Stadiums.For(Victim).Distances[0] != 300f;
        GD.Print(refused
            ? "  ok    a row of four distances was refused rather than padded"
            : "  FAIL  a partial row was accepted and the ground is a shape nobody described");

        // Nobody else moved.
        bool others = Stadiums.All.Where(p => p.TeamId != Victim)
            .All(p => p.Distances.Length == 5 && p.Distances[2] > ParkEdits.MinDistance);
        GD.Print(others ? "  ok    the other grounds were left alone"
                        : "  FAIL  editing one park changed another");

        // And putting it back has to actually put it back.
        ParkEdits.Clear(Victim);
        var restored = Stadiums.For(Victim);
        bool back = restored.Name == original.Name
                 && restored.Distances.SequenceEqual(original.D)
                 && restored.Heights.SequenceEqual(original.H);
        GD.Print(back ? "  ok    reverts to the ground as it shipped"
                      : "  FAIL  a park cannot be put back");

        // A harness must never see any of it.
        ParkEdits.Enabled = false;
        ParkEdits.Load();
        Stadiums.Rebuild();
        GD.Print(Stadiums.For(Victim).Name == original.Name
            ? "  ok    a verification run ignores the file entirely"
            : "  FAIL  an audit would be measuring somebody's own ballpark");

        string scratch = ProjectSettings.GlobalizePath(ParkEdits.Path);
        if (FileAccess.FileExists(ParkEdits.Path)) DirAccess.RemoveAbsolute(scratch);
        ParkEdits.Path = was;
        ParkEdits.Enabled = false;
        ParkEdits.Load();
        Stadiums.Rebuild();

        GD.Print($"\n  the player's own file at {was} was never written to.");
    }

    /// <summary>
    /// How many balls leave this yard over a fixed set of ballgames.
    ///
    /// Played through the real simulation rather than a formula of this file's own. A closed-form
    /// carry estimate would only ever prove that this audit's arithmetic responds to a fence, and
    /// the question is whether the *game* does. Same opponents, same seeds and rosters rebuilt from
    /// scratch each time, so the ground is the only thing that differs between the two counts.
    /// </summary>
    private static int OverTheFence(int games)
    {
        RosterGenerator.ResetCache();
        var home = RosterGenerator.For(Teams.Get(Victim));

        int hr = 0;
        for (int i = 0; i < games; i++)
        {
            var away = RosterGenerator.For(Teams.Get((i * 7 + 3) % Teams.All.Count));
            var sit = Core.QuickGame.Simulate(away, home, 9, seed: 7000 + i);
            foreach (var (_, line) in sit.Stats.BattingLines) hr += line.HomeRuns;
        }

        return hr;
    }
}
