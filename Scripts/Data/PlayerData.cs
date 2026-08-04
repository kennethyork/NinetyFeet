using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SandlotSlugfest.Data;

/// <summary>
/// Where a player lines up. The designated hitter is appended rather than slotted in beside the
/// other spots because a save stores the raw enum value — inserting him in the middle would have
/// turned every catcher in an existing league into a first baseman.
/// </summary>
public enum Position { P, C, First, Second, Third, Short, Left, Center, Right, DH }

public enum Handedness { Right, Left }

/// <summary>
/// A pitcher's job. A five-man staff had no room for one — the last arm was "the closer" and the
/// rest were the rotation, so there was no seventh-inning man to burn, no lefty to bring in for
/// one hitter, and no decision to get wrong. Roles are what make a bullpen a thing you manage
/// rather than a list you read from the top.
/// </summary>
public enum StaffRole { Starter, Long, Middle, Setup, Closer }

/// <summary>The shape of a player's game. Drives rating generation and is shown in the UI.</summary>
public enum Archetype
{
    Balanced, Slugger, ContactHitter, Speedster, GloveWizard, FiveTool, Scrapper,
    PowerArm, ControlArtist, Workhorse, Junkballer,
}

/// <summary>Backyard-style signature moves. Each player gets at most one.</summary>
public enum Special
{
    None,
    Fireball,      // pitcher: occasional unhittable heater
    CrazyCurve,    // pitcher: exaggerated late break
    Corkscrew,     // pitcher: pitch that changes direction twice
    MoonShot,      // hitter: big launch-angle bonus on perfect timing
    ContactMaster, // hitter: much wider sweet spot
    BuntMaster,    // hitter: bunts never go foul
    TurboLegs,     // runner: big speed burst between bases
    VacuumGlove,   // fielder: greatly expanded catch radius
    CannonArm,     // fielder: much faster throws
    WallClimber,   // fielder: can rob home runs at the fence
    Knuckleball,   // pitcher: drifts unpredictably, murder on timing
    Heatseeker,    // pitcher: fastball that keeps rising
    IceVeins,      // pitcher: command does not fade with fatigue
    SprayHitter,   // hitter: goes with the pitch, rarely pulls it foul
    GapPower,      // hitter: line drives carry into the alleys
    PinchRunner,   // runner: enormous jump, takes the extra base almost always
    Backstop,      // fielder: never lets a ball get past him
}

public sealed class PlayerData
{
    /// <summary>Stable identity that survives trades and save files.</summary>
    public int Id;

    public string FirstName;
    public string LastName;
    public int Number;
    public Position Position;
    public Handedness Bats;
    public Handedness Throws;
    public Special Special = Special.None;
    public Archetype Archetype = Archetype.Balanced;

    // --- Ratings, all on a 1..10 scale so they can be shown as pips in the UI. ---
    public int Contact;
    public int Power;
    public int Speed;
    public int Arm;
    public int Fielding;
    public int PitchPower;
    public int PitchControl;
    public int Stamina;

    /// <summary>Drives the procedural portrait (skin tone, hair, build) so it stays stable.</summary>
    public int LookSeed;

    /// <summary>
    /// Index into <see cref="Legends"/> for one of the handcrafted kids, or -1 for a generated
    /// player. A named kid uses a written appearance and biography rather than a rolled one.
    /// </summary>
    public int LegendId = -1;

    public bool IsLegend => LegendId >= 0;

    /// <summary>
    /// The pitches this player actually knows. Not every kid has a curveball — in Backyard
    /// Baseball your repertoire is part of who you are, so the picker only offers what this
    /// pitcher can throw. Stored as a bit set over <see cref="PitchTypeBit"/>.
    /// </summary>
    /// <summary>
    /// Every arm knew every pitch, which meant no arm was distinguishable from another on the
    /// mound. A real repertoire is three or four pitches and it is most of a pitcher's identity —
    /// a sinkerballer and a curveball specialist are not the same job. Assigned in
    /// <see cref="RosterGenerator"/>; the fastball is the one thing everybody has.
    /// </summary>
    public int Repertoire = 0b1111;

    public bool Knows(int pitchTypeIndex) => (Repertoire & (1 << pitchTypeIndex)) != 0;

    /// <summary>The pitches this arm actually throws, in enum order.</summary>
    public IEnumerable<Core.PitchType> Arsenal
    {
        get
        {
            foreach (Core.PitchType t in System.Enum.GetValues<Core.PitchType>())
                if (Knows((int)t)) yield return t;
        }
    }

    /// <summary>His repertoire written out, for the scouting line and the mound overlay.</summary>
    public string ArsenalText =>
        string.Join(" · ", Arsenal.Select(Core.SwingProfileNames.Short));

    /// <summary>Signature moves are used, not merely possessed: a limited charge per game.</summary>
    public int PowerUpsPerGame = 1;

    /// <summary>What this arm is for. Meaningless on a position player.</summary>
    public StaffRole Role = StaffRole.Starter;

    /// <summary>
    /// Days since he last pitched. A manager who can call on the same arm every night has no
    /// bullpen to run out of, and the closer would throw a hundred and sixty games a year.
    /// </summary>
    public int RestDays = 3;

    /// <summary>Pitches thrown over the last few days, decayed daily. Drives who is available.</summary>
    public int RecentPitches;

    /// <summary>Whether he can be brought in tonight. A long man can go back-to-back; nobody
    /// goes three hard days running.</summary>
    public bool IsRested => !IsInjured && (RestDays >= 2 || RecentPitches < 22);

    /// <summary>Years old. Young players are raw but have room to grow into their ceiling.</summary>
    public int Age = 26;

    // --- The contract. Money is in thousands of dollars; see Season.Contracts. ---

    /// <summary>What he is paid this season.</summary>
    public int Salary;

    /// <summary>Seasons still owed on the deal, this one included. Zero means it is up.</summary>
    public int ContractYears;

    /// <summary>
    /// Years in the majors. Three earns him arbitration, six earns him the right to leave — which
    /// is what makes a cheap young star worth more to a club than an expensive proven one.
    /// </summary>
    public int ServiceYears;

    /// <summary>Nobody's player: a free agent waiting to be signed.</summary>
    public bool IsFreeAgent;

    /// <summary>Set when a player has retired; he is removed from his club that offseason.</summary>
    public bool Retired;

    /// <summary>Games still to miss through injury. Zero means available.</summary>
    public int DaysOut;

    /// <summary>What he is hurt with, for the injury report.</summary>
    public string Injury = "";

    public bool IsInjured => DaysOut > 0;

    /// <summary>
    /// The overall this player could reach if he develops. A draft is only interesting when a
    /// raw kid with a high ceiling is a real alternative to a polished one who is already there.
    /// </summary>
    public int Potential = 5;

    /// <summary>
    /// A ceiling can never be below what a player already is. Saves written before potential meant
    /// anything stored a flat 5 for everyone, which displayed as "Overall 9 · ceiling 5".
    /// </summary>
    public int Ceiling => Mathf.Max(Potential, Overall);

    /// <summary>How much growth is still ahead of him, for the scouting column.</summary>
    public int Upside => Mathf.Max(0, Ceiling - Overall);

    /// <summary>Scout's shorthand for a prospect's ceiling.</summary>
    public string PotentialGrade => Ceiling switch
    {
        >= 9 => "Superstar",
        >= 8 => "All-Star",
        >= 7 => "Everyday starter",
        >= 6 => "Solid regular",
        >= 5 => "Bench piece",
        _ => "Organisational",
    };

    public string Name => $"{FirstName} {LastName}";
    public string ShortName => $"{FirstName[0]}. {LastName}";

    /// <summary>
    /// Overall rating used for lineup sorting and the team-select screen.
    ///
    /// A reliever is not judged on how long he can go. Weighting stamina the same for everyone
    /// made every closer in the league look like a fringe arm — nine-and-nine stuff with a three
    /// stamina came out at 6.7, below a replacement-level innings eater — which meant the trade
    /// engine gave them away and the offseason released them.
    /// </summary>
    public int Overall => Position == Position.P
        ? Role == StaffRole.Starter
            ? Mathf.RoundToInt((PitchPower + PitchControl + Stamina + Fielding * 0.5f) / 3.5f)
            : Mathf.RoundToInt((PitchPower * 1.35f + PitchControl * 1.2f + Stamina * 0.25f
                                + Fielding * 0.2f) / 3.0f)
        : Mathf.RoundToInt((Contact * 1.2f + Power + Speed + Fielding + Arm * 0.8f) / 5.0f);

    public static string PositionLabel(Position p) => p switch
    {
        Position.P => "P",
        Position.C => "C",
        Position.First => "1B",
        Position.Second => "2B",
        Position.Third => "3B",
        Position.Short => "SS",
        Position.Left => "LF",
        Position.Center => "CF",
        Position.Right => "RF",
        Position.DH => "DH",
        _ => "?",
    };

    public static string RoleLabel(StaffRole r) => r switch
    {
        StaffRole.Long => "Long relief",
        StaffRole.Middle => "Middle relief",
        StaffRole.Setup => "Setup",
        StaffRole.Closer => "Closer",
        _ => "Starter",
    };

    /// <summary>His job, for the roster screen: a slot for hitters, a bullpen role for arms.</summary>
    public string RoleText => Position == Position.P ? RoleLabel(Role) : PositionText;

    public string PositionText => PositionLabel(Position);

    public static string SpecialLabel(Special s) => s switch
    {
        Special.Fireball => "Fireball",
        Special.CrazyCurve => "Crazy Curve",
        Special.Corkscrew => "Corkscrew",
        Special.MoonShot => "Moon Shot",
        Special.ContactMaster => "Contact Master",
        Special.BuntMaster => "Bunt Master",
        Special.TurboLegs => "Turbo Legs",
        Special.VacuumGlove => "Vacuum Glove",
        Special.CannonArm => "Cannon Arm",
        Special.WallClimber => "Wall Climber",
        Special.Knuckleball => "Knuckleball",
        Special.Heatseeker => "Heatseeker",
        Special.IceVeins => "Ice Veins",
        Special.SprayHitter => "Spray Hitter",
        Special.GapPower => "Gap Power",
        Special.PinchRunner => "Pinch Runner",
        Special.Backstop => "Backstop",
        _ => "",
    };

    public string SpecialText => SpecialLabel(Special);

    public static string ArchetypeLabel(Archetype a) => a switch
    {
        Archetype.Slugger => "Slugger",
        Archetype.ContactHitter => "Contact Hitter",
        Archetype.Speedster => "Speedster",
        Archetype.GloveWizard => "Glove Wizard",
        Archetype.FiveTool => "Five-Tool Star",
        Archetype.Scrapper => "Scrapper",
        Archetype.PowerArm => "Power Arm",
        Archetype.ControlArtist => "Control Artist",
        Archetype.Workhorse => "Workhorse",
        Archetype.Junkballer => "Junkballer",
        _ => "All-Rounder",
    };

    public string ArchetypeText => ArchetypeLabel(Archetype);
}
