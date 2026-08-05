using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SandlotSlugfest.Data;

/// <summary>
/// Names, supplied by the person playing.
///
/// The clubs could already be renamed and recoloured; the men in them could not, and there was no
/// screen anywhere that wrote a player's name. So a league could be made to look like one you
/// follow while every man in it stayed invented, which is the half of the job that matters least.
///
/// This reads a plain text file — <c>user://rosters.txt</c> — of names by club, and hands them to
/// the generator in place of the ones it would have drawn. A file, rather than a screen, because
/// there are eight hundred and thirty-two men on the major-league rosters and nobody is going to
/// type them into a text box one at a time.
///
/// Three things it deliberately does not do.
///
/// It ships with nothing in it. The file lives in the user's own directory and no names are
/// included in the game — the template is written on request and is empty apart from the slot
/// labels.
///
/// It never touches a harness. Every verification run turns this off, for the same reason the club
/// editor is turned off: an audit that prints names should print the ones in the source, not
/// whatever this machine's owner has called his shortstop.
///
/// And it is not in the league fingerprint, so two people sharing a season can each use their own
/// file and see their own names for the same men without the leagues disagreeing. Identity is the
/// player id, and it always was.
/// </summary>
public static class Rosters
{
    public const string Path = "user://rosters.txt";

    /// <summary>Off for every audit. See the class note.</summary>
    public static bool Enabled = true;

    /// <summary>Names by club id, in the order the club is built.</summary>
    private static readonly Dictionary<int, List<(string First, string Last)>> _byClub = new();

    /// <summary>Sections in the file that matched no club, for the report to name.</summary>
    private static readonly List<string> _unmatched = new();

    private static bool _loaded;

    public static bool Any => Enabled && _byClub.Count > 0;

    /// <summary>How many clubs the file covers, and how many names it holds.</summary>
    public static int Clubs => _byClub.Count;
    public static int Count => _byClub.Values.Sum(v => v.Count);
    public static IReadOnlyList<string> Unmatched => _unmatched;

    // -----------------------------------------------------------------------
    // The slots a club is built in
    // -----------------------------------------------------------------------

    /// <summary>
    /// What each line of a club's section is for, in order.
    ///
    /// The generator builds thirteen arms, then the nine in the lineup, then four off the bench,
    /// and hands each man his place in that sequence as his identifier. Printing the sequence into
    /// the template is the difference between a file somebody can fill in and a list of twenty-six
    /// blanks — a name on line six is the closer whether or not anybody knew that.
    /// </summary>
    public static readonly string[] Slots =
    {
        "SP1", "SP2", "SP3", "SP4", "SP5",
        "CL", "SU1", "SU2", "RP1", "RP2", "RP3", "LR1", "LR2",
        "C", "1B", "2B", "3B", "SS", "LF", "CF", "RF", "DH",
        "bench C", "bench SS", "bench CF", "bench 1B",
    };

    // -----------------------------------------------------------------------
    // Reading
    // -----------------------------------------------------------------------

    public static bool Exists() => FileAccess.FileExists(Path);

    public static void Load()
    {
        _byClub.Clear();
        _unmatched.Clear();
        _loaded = true;

        if (!Enabled || !Exists()) return;

        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PushWarning($"Could not read {Path}: {FileAccess.GetOpenError()}");
            return;
        }

        int club = -1;

        foreach (string raw in file.GetAsText().Split('\n'))
        {
            // A comment can start anywhere on the line, not only at the beginning. The template
            // labels every club heading with the club's full name — "[BAL]   # Baltimore Blue
            // Crabs" — and a parser that only understood a whole-line comment read that as a
            // heading ending in "s", matched nothing, and silently skipped all thirty-two clubs
            // in the file the game had itself just written.
            int hash = raw.IndexOfAny(new[] { '#', ';' });
            string line = (hash >= 0 ? raw[..hash] : raw).Trim();
            if (line.Length == 0) continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                string key = line[1..^1].Trim();
                club = ClubFor(key);
                if (club < 0) _unmatched.Add(key);
                continue;
            }

            if (club < 0) continue;

            var name = Split(line);
            if (name == null) continue;

            if (!_byClub.TryGetValue(club, out var list))
                _byClub[club] = list = new List<(string, string)>();
            list.Add(name.Value);
        }
    }

    /// <summary>
    /// Which club a section heading means.
    ///
    /// Generous on purpose. Somebody who has renamed his clubs will write the new name, somebody
    /// who has not will write the shipped abbreviation, and a file that silently matches neither
    /// is indistinguishable from one the game never found. The shipped abbreviation is tried too,
    /// so a file survives its author renaming a club afterwards.
    /// </summary>
    private static int ClubFor(string key)
    {
        if (key.Length == 0) return -1;
        if (int.TryParse(key, out int id) && id >= 0 && id < Teams.All.Count) return id;

        bool Same(string a, string b) =>
            a != null && b != null && string.Equals(a.Trim(), b.Trim(),
                System.StringComparison.OrdinalIgnoreCase);

        foreach (var t in Teams.All)
            if (Same(key, t.Abbrev) || Same(key, t.Nickname) || Same(key, t.City)
                || Same(key, t.FullName))
                return t.Id;

        // And the names the club shipped with, which is what the template prints.
        foreach (var t in Teams.All)
            if (Same(key, Teams.OriginalAbbrev(t.Id)) || Same(key, Teams.OriginalName(t.Id)))
                return t.Id;

        return -1;
    }

    private static readonly string[] Suffixes = { "jr", "jr.", "sr", "sr.", "ii", "iii", "iv", "v" };

    /// <summary>
    /// One line into a first name and a surname.
    ///
    /// The surname is everything after the first word, so "Vladimir Guerrero Jr." keeps the suffix
    /// where it belongs rather than losing it — and a lone word is taken as a surname, with the
    /// generated given name left alone, because ShortName reads the first character of the first
    /// name and an empty one would take the screen down.
    /// </summary>
    private static (string First, string Last)? Split(string line)
    {
        var parts = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        if (parts.Length == 1) return (null, parts[0]);
        return (parts[0], string.Join(' ', parts[1..]));
    }

    // -----------------------------------------------------------------------
    // Handing them out
    // -----------------------------------------------------------------------

    /// <summary>
    /// The name for a club's nth man, or null to let the generator draw its own.
    ///
    /// A short section is not an error. Filling in one club, or the first nine men of one, has to
    /// work — a file that must be complete before it does anything is a file nobody finishes.
    /// </summary>
    public static (string First, string Last)? For(int teamId, int ordinal)
    {
        if (!Enabled) return null;
        if (!_loaded) Load();
        if (ordinal < 0 || !_byClub.TryGetValue(teamId, out var list)) return null;
        return ordinal < list.Count ? list[ordinal] : null;
    }

    // -----------------------------------------------------------------------
    // A league that already exists
    // -----------------------------------------------------------------------

    /// <summary>
    /// Renames the men in a league that has already been generated.
    ///
    /// Without this the file would only ever affect a brand new league, which is close to useless
    /// — somebody four seasons into a dynasty is exactly the person who wants his own names in it.
    /// A man is matched by the identifier he was built with rather than by the club he is on now,
    /// so a player who has been traded keeps the name he was given instead of taking the name of
    /// whoever is standing in his old slot.
    ///
    /// Written players are left alone. They are characters with faces and biographies rather than
    /// generated men, and quietly renaming them would be a worse surprise than not.
    /// </summary>
    public static int Apply(Season.SeasonState season)
    {
        if (!Enabled || season == null) return 0;
        if (!_loaded) Load();

        int changed = 0;

        foreach (var p in Everybody(season))
        {
            if (p.IsLegend || p.Id >= RosterGenerator.LegendIdBase) continue;

            int teamId = p.Id / 100;
            int ordinal = p.Id % 100;
            if (teamId < 0 || teamId >= Teams.All.Count) continue;

            if (For(teamId, ordinal) is not { } name) continue;
            if (name.First == p.FirstName && name.Last == p.LastName) continue;

            if (name.First != null) p.FirstName = name.First;
            p.LastName = name.Last;
            changed++;
        }

        return changed;
    }

    /// <summary>Everyone the league knows about: the clubs, the affiliates, and the unemployed.</summary>
    private static IEnumerable<PlayerData> Everybody(Season.SeasonState season)
    {
        foreach (var t in Teams.All)
        {
            foreach (var p in season.RosterFor(t.Id).Players) yield return p;
            foreach (var level in Season.Farm.Levels)
                foreach (var p in Season.Farm.Of(t.Id, level))
                    yield return p;
        }

        foreach (var p in season.FreeAgents) yield return p;
    }

    // -----------------------------------------------------------------------
    // Getting started
    // -----------------------------------------------------------------------

    /// <summary>
    /// Writes an empty file with every club and every slot labelled, ready to be filled in.
    ///
    /// It refuses to overwrite one that already exists. Somebody who has typed eight hundred names
    /// into this file must not be able to lose them by clicking the wrong button once.
    /// </summary>
    public static string WriteTemplate()
    {
        if (Exists()) return $"{Path} already exists and was left alone.";

        var text = new System.Text.StringBuilder();
        text.AppendLine("# Ninety Feet — your own names.");
        text.AppendLine("#");
        text.AppendLine("# One club per section, one man per line, in the order printed beside each");
        text.AppendLine("# line. Lines starting with # are ignored, and so is any club or any line");
        text.AppendLine("# you leave blank — a section you have not filled in simply keeps the names");
        text.AppendLine("# the game generated, so you can do one club and come back to the rest.");
        text.AppendLine("#");
        text.AppendLine("# A section heading can be the club's abbreviation, its nickname, its city");
        text.AppendLine("# or its full name. Rename the clubs first if you are going to: either the");
        text.AppendLine("# old heading or the new one will still find them.");
        text.AppendLine("#");
        text.AppendLine("# Surnames may have more than one word. \"Vladimir Guerrero Jr.\" is fine.");
        text.AppendLine("#");
        text.AppendLine("# The farm systems are not covered. Those are 2,112 more men across three");
        text.AppendLine("# levels, and they keep their generated names.");
        text.AppendLine();

        foreach (var t in Teams.All.OrderBy(t => t.Id))
        {
            text.AppendLine($"[{t.Abbrev}]      # {t.FullName}");
            foreach (string slot in Slots) text.AppendLine($"#   {slot}");
            text.AppendLine();
        }

        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        if (file == null) return $"Could not write {Path}: {FileAccess.GetOpenError()}";

        file.StoreString(text.ToString());
        return $"Wrote a blank {Path} — {Teams.All.Count} clubs, {Slots.Length} lines each.";
    }

    /// <summary>A line for a screen: whether a file was found and what came out of it.</summary>
    public static string Status()
    {
        if (!Enabled) return "off for this run";
        if (!_loaded) Load();
        if (!Exists()) return $"no {Path} — nobody has been renamed";
        if (_byClub.Count == 0) return $"{Path} was read and no club in it was recognised";

        string line = $"{Count} names across {_byClub.Count} of {Teams.All.Count} clubs";
        return _unmatched.Count == 0
            ? line
            : $"{line}; {_unmatched.Count} unrecognised: {string.Join(", ", _unmatched.Take(4))}";
    }
}
