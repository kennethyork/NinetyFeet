using Godot;

namespace SandlotSlugfest.Core;

public enum Difficulty { Rookie, Pro, AllStar, Legend }

/// <summary>
/// What a difficulty level actually changes.
///
/// Every knob here applies only where a human is involved: the hitting assists when you bat, the
/// command spread when you pitch, and the CPU's skill when it is facing you. Nothing here touches
/// a CPU-versus-CPU at-bat, so simulated games and the league's statistics stay on the calibration
/// they were measured against no matter what level you play at.
/// </summary>
public readonly struct DifficultyTuning
{
    public readonly string Name;
    public readonly string Blurb;

    /// <summary>Multiplier on the bat's sweet spot when a human swings.</summary>
    public readonly float BatAssist;

    /// <summary>Multiplier on the timing window when a human swings.</summary>
    public readonly float TimingAssist;

    /// <summary>Scatter on a human's own pitches. Lower lands nearer the reticle.</summary>
    public readonly float HumanCommand;

    /// <summary>Scatter on the CPU's pitches to a human hitter. Lower means it paints corners.</summary>
    public readonly float CpuCommand;

    /// <summary>Multiplier on the CPU hitter's read error when a human is pitching.</summary>
    public readonly float CpuRead;

    /// <summary>
    /// How hard the CPU throws at you. Above 1 the ball arrives sooner and you have less time to
    /// decide — which is what a step up in difficulty should actually feel like, rather than only
    /// a narrower bat.
    /// </summary>
    public readonly float PitchSpeed;

    private DifficultyTuning(string name, string blurb, float bat, float timing,
        float humanCommand, float cpuCommand, float cpuRead, float pitchSpeed = 1f)
    {
        PitchSpeed = pitchSpeed;
        Name = name;
        Blurb = blurb;
        BatAssist = bat;
        TimingAssist = timing;
        HumanCommand = humanCommand;
        CpuCommand = cpuCommand;
        CpuRead = cpuRead;
    }

    public static DifficultyTuning For(Difficulty d) => d switch
    {
        Difficulty.Rookie => new DifficultyTuning("Rookie",
            "Wide bat, forgiving timing. The CPU is wild and swings at junk.",
            1.92f, 2.80f, 0.18f, 1.45f, 1.35f, pitchSpeed: 0.88f),

        Difficulty.AllStar => new DifficultyTuning("All-Star",
            "Tight window. The CPU paints corners and lays off your mistakes.",
            1.16f, 1.44f, 0.42f, 0.80f, 0.84f, pitchSpeed: 1.09f),

        Difficulty.Legend => new DifficultyTuning("Legend",
            "Almost no help. You are hitting on your own eyes.",
            1.02f, 1.14f, 0.58f, 0.62f, 0.70f, pitchSpeed: 1.16f),

        // Pro sat where an average player never missed at all — three quarters of every swing in
        // play — so it was tightened. That went too far the other way: four swings in ten came
        // back as fouls and at-bats stopped resolving. This sits between the two.
        _ => new DifficultyTuning("Pro",
            "The intended balance. A fair fight.",
            1.46f, 2.05f, 0.30f, 1f, 1f, pitchSpeed: 1f),
    };

    public static Difficulty Next(Difficulty d) => (Difficulty)(((int)d + 1) % 4);
}

/// <summary>Difficulty is a preference, not season state, so it lives in its own settings file.</summary>
public static class Settings
{
    private const string Path = "user://settings.cfg";

    public static Difficulty LoadDifficulty()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return Difficulty.Pro;
        int v = (int)cfg.GetValue("game", "difficulty", (int)Difficulty.Pro);
        return (Difficulty)Mathf.Clamp(v, 0, 3);
    }

    public static bool LoadManagerOnly()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return false;
        return (bool)cfg.GetValue("game", "manageronly", false);
    }

    public static void SaveManagerOnly(bool on)
    {
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue("game", "manageronly", on);
        cfg.Save(Path);
    }

    /// <summary>Innings per game. Nine by default, like the real thing.</summary>
    public static int LoadInnings()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return 9;
        return Mathf.Clamp((int)cfg.GetValue("game", "innings", 9), 1, 12);
    }

    public static void SaveInnings(int innings)
    {
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue("game", "innings", Mathf.Clamp(innings, 1, 12));
        cfg.Save(Path);
    }

    /// <summary>Games per club in a new league.</summary>
    public static int LoadSeasonLength()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return 162;
        return (int)cfg.GetValue("game", "seasonlength", 162);
    }

    public static void SaveSeasonLength(int games)
    {
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue("game", "seasonlength", games);
        cfg.Save(Path);
    }

    public static bool LoadAutoFielding()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return true;
        return (bool)cfg.GetValue("game", "autofielding", true);
    }

    public static void SaveAutoFielding(bool on)
    {
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue("game", "autofielding", on);
        cfg.Save(Path);
    }

    public static void SaveDifficulty(Difficulty d)
    {
        var cfg = new ConfigFile();
        cfg.Load(Path);   // keep anything else already in the file
        cfg.SetValue("game", "difficulty", (int)d);
        cfg.Save(Path);
    }
}
