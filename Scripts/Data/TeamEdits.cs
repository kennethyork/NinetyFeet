using System.Collections.Generic;
using Godot;

namespace SandlotSlugfest.Data;

/// <summary>
/// Renamed and recoloured clubs, kept apart from the clubs themselves.
///
/// The thirty-two are written into the source and there was no way to change any of them. That is
/// fine for a game you play once and wrong for one somebody keeps: a league you have run for ten
/// seasons ought to be able to have your club in it, and the club you actually support is almost
/// certainly not the Bronx Bombardiers.
///
/// Overrides live in their own file rather than being written back over the built-in list, so
/// nothing is ever lost — a club can always be put back exactly as it shipped, and a save from
/// before any of this loads unchanged.
///
/// Only the name, the abbreviation and the two colours can be edited. The league, the division
/// and the club's playing biases are deliberately not: they are what makes the thirty-two
/// different from one another as opponents, and letting a player quietly hand his own club a
/// pitching bias would turn a customisation screen into a cheat menu.
/// </summary>
public static class TeamEdits
{
    /// <summary>
    /// Where the overrides live. Settable only so the audit can write somewhere else — a harness
    /// that saved to the real file would quietly overwrite whatever the player had renamed.
    /// </summary>
    public static string Path = "user://teams.cfg";

    public sealed class Edit
    {
        public string City;
        public string Nickname;
        public string Abbrev;
        public Color? Primary;
        public Color? Secondary;

        public bool IsEmpty => City == null && Nickname == null && Abbrev == null
                            && Primary == null && Secondary == null;
    }

    private static readonly Dictionary<int, Edit> Edits = new();

    /// <summary>Set false by a harness, so a measurement never describes a renamed league.</summary>
    public static bool Enabled = true;

    public static bool Any => Edits.Count > 0;

    public static Edit For(int teamId) =>
        Edits.TryGetValue(teamId, out var e) ? e : null;

    /// <summary>Puts a club's changes in place and writes them to disk.</summary>
    public static void Set(int teamId, Edit edit)
    {
        if (edit == null || edit.IsEmpty) Edits.Remove(teamId);
        else Edits[teamId] = edit;

        Save();
        Apply(teamId);
    }

    /// <summary>Puts one club back exactly as it shipped.</summary>
    public static void Clear(int teamId)
    {
        if (!Edits.Remove(teamId)) return;
        Save();

        // The built-in values are the source of truth, so the club is simply rebuilt.
        Teams.Rebuild();
    }

    public static void ClearAll()
    {
        Edits.Clear();
        Save();
        Teams.Rebuild();
    }

    // -----------------------------------------------------------------------
    // Applying
    // -----------------------------------------------------------------------

    /// <summary>Lays every stored change over the built-in clubs. Called once they are built.</summary>
    public static void ApplyAll()
    {
        if (!Enabled) return;
        foreach (int id in Edits.Keys) Apply(id);
    }

    private static void Apply(int teamId)
    {
        if (!Enabled) return;
        if (teamId < 0 || teamId >= Teams.All.Count) return;

        var e = For(teamId);
        if (e == null) return;

        var t = Teams.All[teamId];
        if (!string.IsNullOrWhiteSpace(e.City)) t.City = e.City;
        if (!string.IsNullOrWhiteSpace(e.Nickname)) t.Nickname = e.Nickname;
        if (!string.IsNullOrWhiteSpace(e.Abbrev)) t.Abbrev = e.Abbrev;
        if (e.Primary.HasValue) t.Primary = e.Primary.Value;
        if (e.Secondary.HasValue) t.Secondary = e.Secondary.Value;
    }

    // -----------------------------------------------------------------------
    // Storage
    // -----------------------------------------------------------------------

    public static void Load()
    {
        Edits.Clear();
        if (!Enabled) return;

        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;

        foreach (string section in cfg.GetSections())
        {
            if (!int.TryParse(section, out int id)) continue;

            var e = new Edit
            {
                City = Text(cfg, section, "city"),
                Nickname = Text(cfg, section, "nickname"),
                Abbrev = Text(cfg, section, "abbrev"),
                Primary = Colour(cfg, section, "primary"),
                Secondary = Colour(cfg, section, "secondary"),
            };

            if (!e.IsEmpty) Edits[id] = e;
        }
    }

    private static string Text(ConfigFile cfg, string section, string key)
    {
        if (!cfg.HasSectionKey(section, key)) return null;
        string v = (string)cfg.GetValue(section, key, "");
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static Color? Colour(ConfigFile cfg, string section, string key)
    {
        if (!cfg.HasSectionKey(section, key)) return null;
        string v = (string)cfg.GetValue(section, key, "");
        return string.IsNullOrWhiteSpace(v) ? null : new Color(v);
    }

    private static void Save()
    {
        var cfg = new ConfigFile();

        foreach (var (id, e) in Edits)
        {
            string s = id.ToString();
            if (e.City != null) cfg.SetValue(s, "city", e.City);
            if (e.Nickname != null) cfg.SetValue(s, "nickname", e.Nickname);
            if (e.Abbrev != null) cfg.SetValue(s, "abbrev", e.Abbrev);
            if (e.Primary.HasValue) cfg.SetValue(s, "primary", e.Primary.Value.ToHtml(false));
            if (e.Secondary.HasValue) cfg.SetValue(s, "secondary", e.Secondary.Value.ToHtml(false));
        }

        cfg.Save(Path);
    }

    /// <summary>A short palette to pick from, rather than a colour wheel nobody wants to use.</summary>
    public static readonly Color[] Swatches =
    {
        new("#c2352b"), new("#d4552f"), new("#e08a26"), new("#e8c14a"),
        new("#5f9e4a"), new("#2f7d43"), new("#3f9c94"), new("#4f8fc4"),
        new("#2c4c8a"), new("#3a3f7a"), new("#7a4f9c"), new("#b0508f"),
        new("#8a6236"), new("#5d4022"), new("#2c3b4a"), new("#101820"),
        new("#8b98a6"), new("#cfd3da"), new("#f0e2c4"), new("#f7f2e4"),
    };
}
