using Godot;

namespace SandlotSlugfest.Core;

/// <summary>
/// Appended rather than inserted: the value is written to settings, so putting Simulation in the
/// middle would silently move everybody who had chosen Legend.
/// </summary>
public enum Difficulty { Rookie, Pro, AllStar, Legend, Simulation }

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

        // No help whatsoever: the same bat, the same timing window and the same command the
        // simulation itself swings with. Every rate in this game is calibrated at assist 1, so
        // this is the only setting where a human plays on exactly the terms the league does — an
        // average hitter misses about a quarter of his swings, fouls off about four in ten, and
        // squaring one up has to be earned. It is what somebody means when they ask for hitting
        // like The Show's.
        //
        // The whiff rescue in the resolver is skipped entirely at assist 1, so a bad swing is a
        // miss rather than a foul. That is the whole difference in feel.
        // The hardest level, and it has to be hardest on every axis rather than only the two that
        // were thought about when it was added. As written it had the bat and the timing window at
        // their floor and then quietly handed back four advantages: your own pitches landed nearer
        // the target than on Legend, the opposition pitched worse, its hitters read worse, and the
        // ball arrived slower. A setting that sits at the bottom of the list calling itself "no
        // help at all" while being easier than the one above it in four ways out of six is simply
        // lying about what it is.
        //
        // Bat and timing sit at 1.00 — the floor, and not a coincidence: that is the exact bat the
        // simulation itself swings, so a human here plays on the terms every calibrated rate in
        // this game was measured against. Everything else goes one step past Legend.
        Difficulty.Simulation => new DifficultyTuning("Simulation",
            "No assists at all, and the sharpest opposition. The game the simulation plays.",
            1f, 1f, 0.66f, 0.55f, 0.62f, pitchSpeed: 1.22f),

        // Pro sat where an average player never missed at all — three quarters of every swing in
        // play — so it was tightened. That went too far the other way: four swings in ten came
        // back as fouls and at-bats stopped resolving. This sits between the two.
        _ => new DifficultyTuning("Pro",
            "The intended balance. A fair fight.",
            1.46f, 2.05f, 0.30f, 1f, 1f, pitchSpeed: 1f),
    };

    public static Difficulty Next(Difficulty d) =>
        (Difficulty)(((int)d + 1) % System.Enum.GetValues<Difficulty>().Length);
}

/// <summary>Difficulty is a preference, not season state, so it lives in its own settings file.</summary>
/// <summary>
/// How a hitter is asked to hit.
///
/// The Show's real idea is not any one of its interfaces, it is that it ships several and lets you
/// choose — and the choice is honest in a way a difficulty slider is not. Timing is easier because
/// it asks less of you, not because the game has quietly widened your bat behind your back.
///
/// Every one of these produces the same two numbers the swing resolver has always taken: where the
/// bat was and when it came through. Nothing below the interface changes, which is also why none
/// of it can move the league's calibration — the computer's at-bats do not pass through here.
/// </summary>
public enum HittingStyle
{
    /// <summary>Aim the bat anywhere in the plate plane. The most control and the most to do.</summary>
    Zone,

    /// <summary>Nudge up, down, in or away. Coarse placement, and far less to manage.</summary>
    Directional,

    /// <summary>No aiming at all. Swing at the right moment and the bat goes where the ball is.</summary>
    Timing,
}

/// <summary>How a pitcher is asked to pitch. See <see cref="HittingStyle"/> for the reasoning.</summary>
public enum PitchingStyle
{
    /// <summary>Pick the pitch, aim it, throw it. What this game has always had.</summary>
    Classic,

    /// <summary>A meter: start it, stop it for power, stop it again for accuracy.</summary>
    Meter,
}

public static class Settings
{
    private const string Path = "user://settings.cfg";

    public static Difficulty LoadDifficulty()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return Difficulty.Pro;
        int v = (int)cfg.GetValue("game", "difficulty", (int)Difficulty.Pro);
        return (Difficulty)Mathf.Clamp(v, 0, 4);
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

    /// <summary>
    /// Whether the designated hitter is in force. On by default, as it is everywhere in the real
    /// game now, but a league that wants the pitcher to bat can have it.
    /// </summary>
    public static HittingStyle LoadHitting()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return HittingStyle.Zone;
        return (HittingStyle)Mathf.Clamp((int)cfg.GetValue("game", "hitstyle", 0), 0, 2);
    }

    public static void SaveHitting(HittingStyle style)
    {
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue("game", "hitstyle", (int)style);
        cfg.Save(Path);
    }

    public static PitchingStyle LoadPitching()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return PitchingStyle.Classic;
        return (PitchingStyle)Mathf.Clamp((int)cfg.GetValue("game", "pitchstyle", 0), 0, 1);
    }

    public static void SavePitching(PitchingStyle style)
    {
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue("game", "pitchstyle", (int)style);
        cfg.Save(Path);
    }

    public static bool UseDesignatedHitter()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return true;
        return (bool)cfg.GetValue("game", "dh", true);
    }

    public static void SaveDesignatedHitter(bool on)
    {
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue("game", "dh", on);
        cfg.Save(Path);
    }

    /// <summary>
    /// Whether the written players are seeded onto the clubs.
    ///
    /// On by default: ninety-six hand-written kids with faces and biographies, three to a club,
    /// and they are a good part of what the league is. Off matters to one person in particular —
    /// somebody supplying his own names. A written player displaces the generated man in his slot,
    /// so three names in every section of a roster file are quietly spent on men who keep their
    /// own names, and the ace you typed on line one may simply not be there.
    ///
    /// Only read when a league is built, so turning it over does nothing to one already running.
    /// </summary>
    public static bool UseWrittenPlayers()
    {
        var cfg = new ConfigFile();
        return cfg.Load(Path) != Error.Ok || (bool)cfg.GetValue("game", "legends", true);
    }

    public static void SaveWrittenPlayers(bool on)
    {
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue("game", "legends", on);
        cfg.Save(Path);
    }

    /// <summary>
    /// Games in the first playoff round. The later rounds run two longer, capped at seven, which
    /// is how a real bracket is shaped — the further you go the more the format asks of you.
    /// </summary>
    public static int PlayoffLength()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return 5;
        int v = (int)cfg.GetValue("game", "playofflength", 5);
        return v is 3 or 5 or 7 ? v : 5;
    }

    public static void SavePlayoffLength(int games)
    {
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue("game", "playofflength", games is 3 or 5 or 7 ? games : 5);
        cfg.Save(Path);
    }

    /// <summary>Whether the first-game help card has been dismissed.</summary>
    public static bool LoadSeenHelp()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return false;
        return (bool)cfg.GetValue("game", "seenhelp", false);
    }

    public static void SaveSeenHelp()
    {
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue("game", "seenhelp", true);
        cfg.Save(Path);
    }

    /// <summary>Which league slot was last open.</summary>
    public static int LoadSlot()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return 0;
        return (int)cfg.GetValue("game", "slot", 0);
    }

    public static void SaveSlot(int slot)
    {
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue("game", "slot", slot);
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
