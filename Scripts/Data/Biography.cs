using System.Collections.Generic;
using System.Linq;
using SandlotSlugfest.Core;

namespace SandlotSlugfest.Data;

/// <summary>
/// A few sentences about a ballplayer, composed from what he actually is.
///
/// Every man in the league already had one line, drawn from a pool keyed to his look seed. That is
/// enough to stop a roster reading as a spreadsheet and not enough to make anybody memorable, and
/// it has a worse problem than its length: a line pulled from a pool describes a *type*. Two
/// different sluggers get the same sentence, and the sentence is true of neither of them in
/// particular.
///
/// This is put together rather than picked. Where his name comes from, what he does on the field,
/// the one thing he is known for, how he is regarded in the room, and where he is in a career —
/// each clause drawn from that man's own numbers, so a biography is as unique as the player is,
/// which the uniqueness audit says is completely. The written kids keep the lines they were given;
/// nothing generated should be allowed to talk over an authored character.
/// </summary>
public static class Biography
{
    /// <summary>
    /// Where a man's name comes from.
    ///
    /// Not where he was born — the game has no map and inventing one would be writing fiction the
    /// rest of it cannot support. The name pools are grouped by background, so this says the one
    /// true thing available: which set of names he came out of.
    /// </summary>
    private static readonly string[] Roots =
    {
        "out of the old sandlot leagues",
        "from a long line of ballplayers",
        "the first in his family to play",
        "signed off a diamond nobody scouts",
        "found at a tryout camp",
        "raised on a field behind a grain store",
        "came up through the winter leagues",
        "played three sports and picked this one",
        "turned down a scholarship to sign",
    };

    /// <summary>
    /// The thing he is known for, taken from whichever of his tools is genuinely his.
    ///
    /// Whole sentences rather than clauses to be hung off a pronoun. The first version returned
    /// fragments and pasted "He " in front of every one of them, which produced "He not quick, and
    /// has never once pretended otherwise" and "He nothing gets through him that a man could
    /// reach" — a template that is only grammatical for the phrasings its author happened to
    /// think of while writing it.
    /// </summary>
    private static string Calling(PlayerData p)
    {
        if (p.Position == Data.Position.P)
        {
            if (p.PitchPower >= 8 && p.PitchControl >= 7)
                return "He throws hard and knows where it is going, which is the rarest pair in the game.";
            if (p.PitchPower >= 8) return "He throws harder than anybody wants to stand in against.";
            if (p.PitchControl >= 8) return "He will put it wherever the catcher sets up, all night.";
            if (p.Stamina >= 8) return "He takes the ball and keeps it, deep into games nobody else finishes.";
            if (p.Stamina <= 3) return "One inning, all of it, and then he is done.";
            if (p.PitchPower <= 3 && p.PitchControl <= 3) return "Neither the arm nor the aim, but he is here.";
            return "He gets outs without ever making it look like much.";
        }

        if (p.Power >= 8 && p.Contact >= 7) return "He hits it hard and hits it often, which is the whole job.";
        if (p.Power >= 8) return "One swing changes a scoreboard, and he misses a lot in between.";
        if (p.Contact >= 8) return "Very difficult to strike out, and harder still to fool twice.";
        if (p.Speed >= 8) return "He turns a walk into a double all by himself.";
        if (p.Fielding >= 8 && p.Arm >= 8) return "A glove and an arm both, and the runners have all worked it out.";
        if (p.Fielding >= 8) return "Nothing gets through him that a man could reach.";
        if (p.Arm >= 9) return "The arm has ended a lot of arguments about third base.";
        if (p.Speed <= 3) return "Not quick, and he has never once pretended otherwise.";
        if (p.Contact <= 3 && p.Power <= 3) return "The bat is not why he is in the lineup.";
        return "He does a bit of everything and none of it badly.";
    }

    /// <summary>How he is regarded in the room, which is the personality doing real work.</summary>
    private static string Room(PlayerData p)
    {
        bool works = p.WorkEthic >= 8;
        bool idles = p.WorkEthic <= 3;
        bool calm = p.Poise >= 8;
        bool rattles = p.Poise <= 3;
        bool loyal = p.Loyalty >= 8;
        bool restless = p.Loyalty <= 3;

        if (works && calm) return "First to the park, and the last man on the club you would want to rattle.";
        if (works && restless) return "Outworks everybody in the room and would still listen to an offer.";
        if (works) return "Puts the work in without being asked and without mentioning it.";
        if (idles && calm) return "Coasts, and is maddeningly good anyway when it matters.";
        if (idles) return "The coaching staff have had the conversation more than once.";
        if (calm) return "Nothing that happens on a ballfield appears to reach him.";
        if (rattles) return "Wears every at-bat on his face, which the other dugout has noticed.";
        if (loyal) return "Has never given anybody a reason to think he wants to be anywhere else.";
        if (restless) return "His name comes up every winter, and he does not deny it.";
        return "Gets on with it.";
    }

    /// <summary>Where he is in a career, which is age and service time rather than ability.</summary>
    private static string Chapter(PlayerData p)
    {
        if (p.Age <= 21) return $"He is {p.Age} and has not finished growing into any of it.";
        if (p.Age <= 24 && p.Potential >= 8)
            return $"He is {p.Age}, and the people who saw him at nineteen have not stopped talking.";
        if (p.Age <= 24) return $"He is {p.Age}, which is most of the reason to be patient.";
        if (p.Age >= 35) return $"He is {p.Age}, and every winter somebody asks him about it.";
        if (p.Age >= 32) return $"At {p.Age} he has stopped getting quicker and started getting smarter.";
        if (p.ServiceYears >= 8) return $"Nine seasons in, and {p.ServiceYears} of them here.";
        return $"He is {p.Age} and in the middle of the good years.";
    }

    /// <summary>The signature ability, when he has one worth a sentence.</summary>
    private static string Signature(PlayerData p) => p.Special switch
    {
        Special.Fireball => "The heater, on the nights it is there, cannot be hit.",
        Special.CrazyCurve => "The curveball falls off a table and takes the hitter's front foot with it.",
        Special.Corkscrew => "The slider changes direction twice, which should not be legal.",
        Special.Knuckleball => "Throws a knuckleball. Nobody, catcher included, knows where it goes.",
        Special.Heatseeker => "The fastball climbs, and hitters swing under it all evening.",
        Special.IceVeins => "His command does not fade when he is tired, which is a kind of cheating.",
        Special.MoonShot => "When he squares one up it does not come down in the ballpark.",
        Special.ContactMaster => "The sweet spot on his bat appears to be the whole bat.",
        Special.BuntMaster => "Can bunt one dead on a line and has done it in October.",
        Special.TurboLegs => "Second gear. Nobody has thrown him out going first to third.",
        Special.SprayHitter => "Goes with the pitch wherever it is, so there is no shift for him.",
        Special.GapPower => "Line drives that keep rising until they reach the alley.",
        Special.PinchRunner => "The jump he gets is the reason he is on the roster in September.",
        Special.VacuumGlove => "The glove closes on anything inside a postcode of him.",
        Special.CannonArm => "The arm is a genuine weapon and the third-base coach respects it.",
        Special.WallClimber => "Has taken more than one home run back over the fence.",
        Special.Backstop => "Nothing gets past him to the screen. Nothing.",
        _ => null,
    };

    /// <summary>
    /// The whole thing.
    ///
    /// Seeded from the look seed so it never changes between viewings — a biography that reworded
    /// itself every time the screen redrew would be worse than no biography at all.
    /// </summary>
    public static string For(PlayerData p)
    {
        if (p == null) return "";

        // A written kid keeps what he was written with — if it is actually his. Most of the cast
        // share a line with fifteen other men, and a sentence handed round the clubhouse is not a
        // biography. Those get one composed from their own numbers like anybody else; the ones
        // whose line is theirs alone keep it, because a composed paragraph would be talking over
        // somebody who already has a voice.
        if (p.IsLegend && !Legends.BioIsShared(p.LegendId))
            return Legends.Bio(p.LegendId);

        var rng = new Rng(p.LookSeed ^ 0x2B17);
        var lines = new List<string>
        {
            $"{p.Name}, {Job(p)}, {Roots[rng.Range(0, Roots.Length)]}.",
            Calling(p),
            Chapter(p),
        };

        // A signature only gets a sentence if the man's tools bear it out.
        //
        // The generator picks a special without reference to the ratings, so a three-contact
        // hitter can carry ContactMaster and a five-arm outfielder a cannon. Both are true of the
        // simulation — his barrel really is a third wider — and both read as a contradiction two
        // sentences after "the bat is not why he is in the lineup". The ability still works; it
        // just does not get to be the headline when nothing else about him agrees.
        if (Fits(p) && Signature(p) is { } signature) lines.Add(signature);
        lines.Add(Room(p));

        return string.Join(" ", lines);
    }

    /// <summary>Whether his signature ability is borne out by the rest of him.</summary>
    private static bool Fits(PlayerData p) => p.Special switch
    {
        Special.ContactMaster or Special.SprayHitter or Special.BuntMaster => p.Contact >= 5,
        Special.MoonShot or Special.GapPower => p.Power >= 5,
        Special.TurboLegs or Special.PinchRunner => p.Speed >= 5,
        Special.CannonArm => p.Arm >= 6,
        Special.VacuumGlove or Special.Backstop or Special.WallClimber => p.Fielding >= 5,
        Special.Fireball or Special.Heatseeker => p.PitchPower >= 5,
        Special.IceVeins => p.PitchControl >= 5,
        Special.CrazyCurve or Special.Corkscrew or Special.Knuckleball => true,
        _ => false,
    };

    /// <summary>The one-line version, for a row that has no room for a paragraph.</summary>
    public static string Short(PlayerData p) =>
        p == null ? "" : p.IsLegend ? Legends.Bio(p.LegendId) : Flavour.For(p);

    private static string Job(PlayerData p) => p.Position switch
    {
        Data.Position.P => p.Role == StaffRole.Starter ? "starting pitcher"
                         : p.Role == StaffRole.Closer ? "closer" : "reliever",
        Data.Position.C => "catcher",
        Data.Position.First => "first baseman",
        Data.Position.Second => "second baseman",
        Data.Position.Third => "third baseman",
        Data.Position.Short => "shortstop",
        Data.Position.Left => "left fielder",
        Data.Position.Center => "centre fielder",
        Data.Position.Right => "right fielder",
        _ => "designated hitter",
    };
}
