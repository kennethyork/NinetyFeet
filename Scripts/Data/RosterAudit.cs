using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Season;

namespace SandlotSlugfest.Data;

/// <summary>
/// What a supplied names file actually did.
///
/// Every other way of finding out is bad. Starting a league and reading the roster screen tells
/// you whether the first club worked and nothing about the other thirty-one; a section heading
/// that matches no club is invisible, and so is a name that landed on the wrong man because the
/// lines were a slot out. So: read the file, build a league from it, and print what came back —
/// including, and especially, the parts that did not.
///
/// This is the one audit that deliberately reads the player's own file. Every other harness turns
/// it off. It is not a measurement of the game, it is a check on something the player wrote.
/// </summary>
public static class RosterAudit
{
    public static void Run()
    {
        GD.Print("\n=== YOUR NAMES ===\n");

        // The flag is off for verification runs, and this run is one. Turned back on by hand,
        // because reading the file is the entire point of this particular harness.
        Rosters.Enabled = true;
        Rosters.Load();

        GD.Print($"  file:  {Rosters.Path}");

        if (!Rosters.Exists())
        {
            GD.Print("  There is no file there yet.\n");
            GD.Print("  Write one with --names-template, or from the club editor in the game.");
            GD.Print("  It is a plain text file: a club per section, a man per line.");
            return;
        }

        GD.Print($"  read:  {Rosters.Status()}\n");

        foreach (string bad in Rosters.Unmatched)
            GD.Print($"  no club called \"{bad}\" — that section was skipped entirely");

        if (Rosters.Clubs == 0)
        {
            GD.Print("\n  Nothing in the file matched a club, so no name in it will ever be used.");
            GD.Print("  A section heading has to be a club's abbreviation, nickname, city or full");
            GD.Print("  name, in square brackets: [BAL], [Blue Crabs], [Baltimore].");
            return;
        }

        // Now build a league with the file on and see who is actually called what.
        RosterGenerator.ResetCache();
        var league = new SeasonState();
        league.StartNew(RosterGenerator.DefaultLeagueSeed, 0, Schedule.FullSeason, 9);

        int covered = 0, partial = 0, legendHeld = 0;
        var duplicates = new Dictionary<string, List<string>>();

        foreach (var team in Teams.All.OrderBy(t => t.Id))
        {
            var roster = league.RosterFor(team.Id);

            // Generated men only. The written kids keep their own names and are not the file's
            // business, so counting them as gaps would report every club as short.
            var mine = roster.Players
                .Where(p => !p.IsLegend && p.Id / 100 == team.Id)
                .OrderBy(p => p.Id)
                .ToList();

            int wanted = mine.Count;
            int got = 0;
            int supplied = SuppliedFor(team.Id);

            foreach (var p in mine)
            {
                if (Rosters.For(team.Id, p.Id % 100) is not { } name) continue;
                got++;

                // The name in the file must be the name on the man. If these ever disagree the
                // file is being read and then thrown away somewhere, which is worse than it not
                // being read at all.
                string expected = name.First == null ? p.FirstName + " " + name.Last
                                                     : name.First + " " + name.Last;
                if (p.Name != expected)
                    GD.Print($"  {team.Abbrev} slot {p.Id % 100}: file says \"{expected}\", " +
                             $"the man is \"{p.Name}\"  — NOT APPLIED");
            }

            // A written player takes a generated man's slot outright, so a name aimed at that slot
            // is never used and nothing on any screen says so. Three a club, and they are drawn to
            // the good slots — a written ace takes the top of the rotation — which is exactly
            // where somebody typing in a real roster puts the name he cares most about.
            int held = roster.Players.Count(p => p.IsLegend);
            if (held > 0) legendHeld += held;

            if (got == 0)
            {
                if (supplied > 0)
                    GD.Print($"  {team.Abbrev,-4} {supplied} names supplied and none of them used" +
                             $" — all {supplied} slots are held by written players");
                continue;
            }

            if (got >= wanted) covered++;
            else
            {
                partial++;
                GD.Print($"  {team.Abbrev,-4} {got} of {wanted} generated men named" +
                         (supplied > got
                             ? $"; {supplied - got} names went unused, their slots held by " +
                               "written players or past the end of the club"
                             : "; the rest keep the names the game gave them"));
            }
        }

        // Two men with one name is legal and probably a typo, so it is reported rather than
        // refused — it is the player's league and he may have meant it.
        foreach (var p in Teams.All.SelectMany(t => league.RosterFor(t.Id).Players))
        {
            if (!duplicates.TryGetValue(p.Name, out var who))
                duplicates[p.Name] = who = new List<string>();
            who.Add(league.TeamOf(p)?.Abbrev ?? "??");
        }

        var clashes = duplicates.Where(kv => kv.Value.Count > 1).Take(8).ToList();

        GD.Print($"\n  {covered} clubs fully named, {partial} partly, " +
                 $"{Teams.All.Count - covered - partial} untouched.");

        if (legendHeld > 0)
            GD.Print($"  {legendHeld} slots across the league are held by written players, who keep" +
                     "\n  their own names. Settings -> Written players turns them off, and then a" +
                     "\n  new league has a generated man in every slot for a file to name.");

        if (clashes.Count == 0)
        {
            GD.Print("  no two men in the league share a name.");
        }
        else
        {
            GD.Print($"  {clashes.Count} names are held by more than one man:");
            foreach (var (name, who) in clashes)
                GD.Print($"    {name} — {string.Join(", ", who)}");
            GD.Print("  Legal, and usually a line typed twice.");
        }

        OntoALeagueThatExists();

        GD.Print("\n  A file only ever renames men the generator made. The written kids keep their");
        GD.Print("  own names, and so do the farm systems.");
    }

    /// <summary>
    /// The other half, and the half that matters to anybody already playing.
    ///
    /// Reading the file only affects leagues built afterwards, which is no use at all to somebody
    /// four seasons into a dynasty — so the club editor can put the names onto a league that is
    /// already running. That path is easy to get subtly wrong: a man is matched by the identifier
    /// he was built with rather than by the club he is on now, so a player who has been traded
    /// keeps his own name instead of taking the name of whoever now stands in his old slot.
    ///
    /// So: build a league with the file switched off, turn it on, apply, and check every man.
    /// </summary>
    private static void OntoALeagueThatExists()
    {
        GD.Print("\n  onto a league that already exists:");

        Rosters.Enabled = false;
        RosterGenerator.ResetCache();
        var league = new SeasonState();
        league.StartNew(RosterGenerator.DefaultLeagueSeed, 0, Schedule.FullSeason, 9);

        // A trade first, so the check covers the case the matching rule exists for.
        var a = league.RosterFor(0).Players.FirstOrDefault(p => !p.IsLegend);
        var b = league.RosterFor(1).Players.FirstOrDefault(p => !p.IsLegend);
        if (a != null && b != null)
        {
            league.RosterFor(0).Players.Remove(a);
            league.RosterFor(1).Players.Remove(b);
            league.RosterFor(0).Players.Add(b);
            league.RosterFor(1).Players.Add(a);
        }

        Rosters.Enabled = true;
        Rosters.Load();
        int changed = Rosters.Apply(league);

        int wrong = 0;
        foreach (var t in Teams.All)
            foreach (var p in league.RosterFor(t.Id).Players)
            {
                if (p.IsLegend) continue;
                if (Rosters.For(p.Id / 100, p.Id % 100) is not { } want) continue;

                string expected = want.First == null ? $"{p.FirstName} {want.Last}"
                                                     : $"{want.First} {want.Last}";
                if (p.Name == expected) continue;

                wrong++;
                if (wrong <= 4) GD.Print($"    {p.Name} should be {expected}");
            }

        GD.Print($"    {changed} renamed, {wrong} wrong.");
        if (a != null && b != null)
            GD.Print($"    the traded man is on {league.TeamOf(a)?.Abbrev}, still called {a.Name}" +
                     " — his name went with him.");
    }

    /// <summary>How many names the file holds for a club, whether or not they can all land.</summary>
    private static int SuppliedFor(int teamId)
    {
        int n = 0;
        while (Rosters.For(teamId, n) != null) n++;
        return n;
    }

    /// <summary>Writes the blank file, so there is something to fill in.</summary>
    public static void Template()
    {
        Rosters.Enabled = true;
        GD.Print("\n=== YOUR NAMES — template ===\n");
        GD.Print("  " + Rosters.WriteTemplate());
        GD.Print($"  On this machine that is {ProjectSettings.GlobalizePath(Rosters.Path)}");
    }
}
