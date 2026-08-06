using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SandlotSlugfest.Data;

/// <summary>
/// Ballparks, rebuilt by the person playing.
///
/// The clubs could be renamed and recoloured and the men in them could be renamed, and the one
/// thing left fixed was the ground they play on — thirty-two footprints written into the source
/// with no way to change any of them.
///
/// This one is different from the other two overlays in a way worth stating plainly. A club's name
/// and a player's name are labels: change them and nothing about the baseball moves. A fence
/// distance is not a label. It goes straight into the physics — the ball is either over it or off
/// it — so a three-hundred-foot porch in right will show up in the league's home run rate by the
/// end of April. That is the point, and it is also the risk: a park nobody measured is a
/// calibration nobody measured. Every audit therefore ignores this file, exactly as it ignores the
/// club editor, so a measurement of the game is always a measurement of the game as it shipped.
///
/// Stored as <c>user://stadiums.cfg</c>, laid over the built-in parks rather than into them, so a
/// ground can always be put back the way it was.
/// </summary>
public static class ParkEdits
{
    public static string Path = "user://stadiums.cfg";

    /// <summary>Off for every audit. See the class note — a park changes the baseball.</summary>
    public static bool Enabled = true;

    public sealed class Edit
    {
        public string Name;
        public string Quirk;

        /// <summary>Five fence distances and five wall heights, or null to leave them alone.</summary>
        public float[] Distances;
        public float[] Heights;

        public float? Air;
        public float? Foul;
        public bool? Covered;

        public Color? Grass;
        public Color? Dirt;
        public Color? Wall;
        public Color? Trim;

        public bool IsEmpty => Name == null && Quirk == null && Distances == null && Heights == null
                            && !Air.HasValue && !Foul.HasValue && !Covered.HasValue
                            && !Grass.HasValue && !Dirt.HasValue && !Wall.HasValue && !Trim.HasValue;
    }

    private static readonly Dictionary<int, Edit> Edits = new();

    public static bool Any => Enabled && Edits.Count > 0;
    public static int Count => Enabled ? Edits.Count : 0;

    public static Edit For(int teamId) => Edits.GetValueOrDefault(teamId);

    // -----------------------------------------------------------------------
    // What a park may be given
    // -----------------------------------------------------------------------

    /// <summary>
    /// The limits a hand-written file is held to.
    ///
    /// Not taste, arithmetic. A fence at fifty feet would put every fly ball in the seats and a
    /// fence at nine hundred would end home runs altogether; either turns the league into
    /// something the calibration has nothing to say about. These are wide enough to build any
    /// ground that has ever existed and narrow enough that the game remains baseball.
    /// </summary>
    public const float MinDistance = 250f, MaxDistance = 500f;
    public const float MinHeight = 2f, MaxHeight = 60f;
    public const float MinAir = 0.85f, MaxAir = 1.15f;
    public const float MinFoul = 0.4f, MaxFoul = 2.5f;

    // -----------------------------------------------------------------------
    // Applying
    // -----------------------------------------------------------------------

    public static void ApplyAll()
    {
        if (!Enabled) return;
        foreach (int id in Edits.Keys) Apply(id);
    }

    private static void Apply(int teamId)
    {
        if (!Enabled) return;
        if (teamId < 0 || teamId >= Stadiums.All.Length) return;

        var e = For(teamId);
        if (e == null) return;

        var p = Stadiums.All[teamId];

        if (!string.IsNullOrWhiteSpace(e.Name)) p.Name = e.Name;
        if (!string.IsNullOrWhiteSpace(e.Quirk)) p.Quirk = e.Quirk;

        if (e.Distances is { Length: 5 })
            p.Distances = e.Distances.Select(d => Mathf.Clamp(d, MinDistance, MaxDistance)).ToArray();
        if (e.Heights is { Length: 5 })
            p.Heights = e.Heights.Select(h => Mathf.Clamp(h, MinHeight, MaxHeight)).ToArray();

        if (e.Air.HasValue) p.AirDensity = Mathf.Clamp(e.Air.Value, MinAir, MaxAir);
        if (e.Foul.HasValue) p.FoulTerritory = Mathf.Clamp(e.Foul.Value, MinFoul, MaxFoul);
        if (e.Covered.HasValue) p.Covered = e.Covered.Value;

        if (e.Grass.HasValue)
        {
            p.Grass = e.Grass.Value;

            // The mown stripe is a shade of the grass rather than its own colour, so it follows
            // whatever the ground is turfed with instead of staying green over a blue field.
            p.GrassAlt = e.Grass.Value.Darkened(0.16f);
        }
        if (e.Dirt.HasValue) p.Dirt = e.Dirt.Value;
        if (e.Wall.HasValue) p.Wall = e.Wall.Value;
        if (e.Trim.HasValue) p.WallTrim = e.Trim.Value;
    }

    // -----------------------------------------------------------------------
    // Storage
    // -----------------------------------------------------------------------

    public static void Set(int teamId, Edit edit)
    {
        if (edit == null || edit.IsEmpty) Edits.Remove(teamId);
        else Edits[teamId] = edit;

        Save();
        Stadiums.Rebuild();
    }

    public static void Clear(int teamId)
    {
        Edits.Remove(teamId);
        Save();
        Stadiums.Rebuild();
    }

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
                Name = Text(cfg, section, "name"),
                Quirk = Text(cfg, section, "quirk"),
                Distances = Fives(cfg, section, "distances"),
                Heights = Fives(cfg, section, "heights"),
                Air = Number(cfg, section, "air"),
                Foul = Number(cfg, section, "foul"),
                Covered = Flag(cfg, section, "covered"),
                Grass = Colour(cfg, section, "grass"),
                Dirt = Colour(cfg, section, "dirt"),
                Wall = Colour(cfg, section, "wall"),
                Trim = Colour(cfg, section, "trim"),
            };

            if (!e.IsEmpty) Edits[id] = e;
        }
    }

    private static void Save()
    {
        var cfg = new ConfigFile();

        foreach (var (id, e) in Edits)
        {
            string s = id.ToString();
            if (e.Name != null) cfg.SetValue(s, "name", e.Name);
            if (e.Quirk != null) cfg.SetValue(s, "quirk", e.Quirk);
            if (e.Distances != null) cfg.SetValue(s, "distances", e.Distances);
            if (e.Heights != null) cfg.SetValue(s, "heights", e.Heights);
            if (e.Air.HasValue) cfg.SetValue(s, "air", e.Air.Value);
            if (e.Foul.HasValue) cfg.SetValue(s, "foul", e.Foul.Value);
            if (e.Covered.HasValue) cfg.SetValue(s, "covered", e.Covered.Value);
            if (e.Grass.HasValue) cfg.SetValue(s, "grass", e.Grass.Value.ToHtml(false));
            if (e.Dirt.HasValue) cfg.SetValue(s, "dirt", e.Dirt.Value.ToHtml(false));
            if (e.Wall.HasValue) cfg.SetValue(s, "wall", e.Wall.Value.ToHtml(false));
            if (e.Trim.HasValue) cfg.SetValue(s, "trim", e.Trim.Value.ToHtml(false));
        }

        cfg.Save(Path);
    }

    private static string Text(ConfigFile cfg, string section, string key) =>
        cfg.HasSectionKey(section, key) ? cfg.GetValue(section, key).AsString() : null;

    private static float? Number(ConfigFile cfg, string section, string key) =>
        cfg.HasSectionKey(section, key) ? cfg.GetValue(section, key).AsSingle() : null;

    private static bool? Flag(ConfigFile cfg, string section, string key) =>
        cfg.HasSectionKey(section, key) ? cfg.GetValue(section, key).AsBool() : null;

    private static Color? Colour(ConfigFile cfg, string section, string key) =>
        cfg.HasSectionKey(section, key)
            ? new Color(cfg.GetValue(section, key).AsString())
            : null;

    /// <summary>
    /// Five numbers, or nothing at all.
    ///
    /// A partial row is refused rather than padded. Four distances and a missing gap is a typo,
    /// and guessing what the fifth should have been would build a ground its author never
    /// described and cannot see is wrong.
    /// </summary>
    private static float[] Fives(ConfigFile cfg, string section, string key)
    {
        if (!cfg.HasSectionKey(section, key)) return null;

        var raw = cfg.GetValue(section, key).AsFloat32Array();
        return raw is { Length: 5 } ? raw : null;
    }

    /// <summary>
    /// Writes a file describing every park as it currently stands, ready to be edited.
    ///
    /// The parks are written out in full rather than as a blank form, because a ballpark is five
    /// distances and five heights and nobody can invent those from nothing — starting from Fenway
    /// and moving the wall is a job somebody can do, and starting from an empty bracket is not.
    /// Refuses to overwrite an existing file.
    /// </summary>
    public static string WriteTemplate()
    {
        if (FileAccess.FileExists(Path)) return $"{Path} already exists and was left alone.";

        var cfg = new ConfigFile();

        foreach (var park in Stadiums.All)
        {
            string s = park.TeamId.ToString();
            cfg.SetValue(s, "name", park.Name);
            cfg.SetValue(s, "quirk", park.Quirk);
            cfg.SetValue(s, "distances", park.Distances);
            cfg.SetValue(s, "heights", park.Heights);
            cfg.SetValue(s, "air", park.AirDensity);
            cfg.SetValue(s, "foul", park.FoulTerritory);
            cfg.SetValue(s, "covered", park.Covered);
            cfg.SetValue(s, "grass", park.Grass.ToHtml(false));
            cfg.SetValue(s, "dirt", park.Dirt.ToHtml(false));
            cfg.SetValue(s, "wall", park.Wall.ToHtml(false));
            cfg.SetValue(s, "trim", park.WallTrim.ToHtml(false));
        }

        var error = cfg.Save(Path);
        if (error != Error.Ok) return $"Could not write {Path}: {error}";

        return $"Wrote {Path} — all {Stadiums.All.Length} grounds as they stand. " +
               "Distances run left line, left gap, centre, right gap, right line.";
    }

    /// <summary>A line for a screen.</summary>
    public static string Status()
    {
        if (!Enabled) return "off for this run";
        if (!FileAccess.FileExists(Path)) return $"no {Path} — every ground as it shipped";
        return Edits.Count == 0
            ? $"{Path} was read and nothing in it changed a ground"
            : $"{Edits.Count} of {Stadiums.All.Length} grounds rebuilt";
    }
}
