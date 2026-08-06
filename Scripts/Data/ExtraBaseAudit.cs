using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Core;

namespace SandlotSlugfest.Data;

/// <summary>
/// Why a league without the written players hits for less extra base, settled by experiment
/// rather than by argument.
///
/// The symptom: turn the written players off and doubles fall about eleven percent and triples
/// about a third, while runs, hits, home runs, carry and spray all stay put. Balls leave the bat
/// the same way and land in the same places; the same number of them are hits. What changes is
/// how many bases the hitter takes — doubles and triples are turning into singles.
///
/// That already rules out most of the candidates. It is not the hitters' ratings, not the park,
/// not the pitching. It leaves the things that decide whether a man keeps running: his speed, the
/// specials that multiply it, and the specials on the other side that take it away again.
///
/// Reasoning further from league averages is where the last attempt went wrong — a change made on
/// that basis improved triples, made doubles worse, and moved runs not at all. So this changes one
/// thing at a time in a league that is otherwise identical, over the same matchups and the same
/// seeds, and reads the answer off. An intervention, not a correlation.
/// </summary>
public static class ExtraBaseAudit
{
    /// <summary>Specials that make a runner faster or bolder.</summary>
    private static readonly Special[] Legs = { Special.TurboLegs, Special.PinchRunner };

    /// <summary>Specials that carry a ball further into the gap.</summary>
    private static readonly Special[] Gap = { Special.GapPower, Special.MoonShot };

    private static readonly Special[] Glove =
    {
        Special.VacuumGlove, Special.CannonArm, Special.WallClimber, Special.Backstop,
    };

    public static void Run(int games, int only)
    {
        GD.Print($"\n=== EXTRA BASES — one change at a time, {games} games each ===\n");

        var configs = new (string Name, System.Action Setup)[]
        {
            ("written players in, untouched", () => Build(true)),
            ("written players out", () => Build(false)),
            ("out, and no glove specials anywhere", () => { Build(false); StripGloves(); }),
            ("out, and every lineup man given legs", () => { Build(false); GiveLegs(); }),
            ("out, and every lineup man given gap power", () => { Build(false); GiveGap(); }),
            ("out, and the gloves brought down to the written cast",
                () => { Build(false); Deglove(0.32f, 0.17f); }),
            ("out, and only the gloves brought down", () => { Build(false); Deglove(0.32f, 0f); }),
            ("out, and only the arms brought down", () => { Build(false); Deglove(0f, 0.17f); }),

            // Both defensive findings at once. Each recovered about a third on its own, and a
            // third plus a third is only a fix if they add — two measurements of the same
            // underlying thing would recover a third between them.
            ("out, gloves down AND no glove specials",
                () => { Build(false); Deglove(0.32f, 0.17f); StripGloves(); }),

            // The mirror, as a control: how much of the written cast's extra-base advantage is
            // its specials rather than the men.
            ("written players in, but no specials at all", () => { Build(true); StripAll(); }),

            // The generator picks a man's special uniformly from seven, so a three-speed slugger
            // can be handed TurboLegs — where it is worth nothing — while the fastest man in the
            // league gets BuntMaster. Same number of specials, matched to the men who can use
            // them. Tested before changing anything, because the last change made on reasoning
            // alone had to be reverted.
            ("out, and specials matched to the man", () => { Build(false); MatchSpecials(); }),

            // Both leagues are short of the real 3.20, not only the one without written players —
            // 3.03 and 2.63. So the last lever tried is the one that moves both: how boldly the
            // batter-runner takes second. It is the number that decides doubles and nothing else,
            // and if it is simply set a little low then every other experiment above was looking
            // for a difference where the problem is a level.
            ("written in, bolder to second", () => { Build(true); Stretch(0.86f); }),
            ("out, bolder to second", () => { Build(false); Stretch(0.86f); }),
        };

        for (int i = 0; i < configs.Length; i++)
        {
            if (only >= 0 && i != only) continue;

            configs[i].Setup();
            var (singles, doubles, triples, homers) = HeadlessSim.HitShape(games);

            GD.Print($"  [{i}] {configs[i].Name}");
            GD.Print($"      1B {singles / (float)games,5:0.00}   " +
                     $"2B {doubles / (float)games,5:0.00}   " +
                     $"3B {triples / (float)games,5:0.00}   " +
                     $"HR {homers / (float)games,5:0.00}   " +
                     $"(real 2B 3.20, 3B 0.29)\n");
        }

        if (only < 0)
        {
            GD.Print("  [0] against [1] is the shortfall. Whichever of [2], [3] and [4] closes it");
            GD.Print("  is the cause; if none of them does, it is not the specials at all and the");
            GD.Print("  next place to look is the baserunning agent itself.");
        }
    }

    // -----------------------------------------------------------------------
    // Building a league to experiment on
    // -----------------------------------------------------------------------

    /// <summary>How boldly the batter-runner goes for second. Put back after every run.</summary>
    private static void Stretch(float to) => Core.PlaySimulation.StretchToSecond = to;

    private static void Build(bool withWritten)
    {
        Core.PlaySimulation.StretchToSecond = 0.84f;
        Core.PlaySimulation.StretchToThird = 0.565f;

        RosterGenerator.IncludeLegends = withWritten;
        RosterGenerator.ResetCache();
        foreach (var t in Teams.All) RosterGenerator.For(t);
    }

    /// <summary>Everyone who bats, which is the only population that can take an extra base.</summary>
    private static IEnumerable<PlayerData> Lineups() =>
        Teams.All.SelectMany(t => RosterGenerator.For(t).BattingOrder);

    private static void StripAll()
    {
        foreach (var p in Lineups()) p.Special = Special.None;
    }

    private static void StripGloves()
    {
        foreach (var p in Lineups())
            if (Glove.Contains(p.Special)) p.Special = Special.None;
    }

    /// <summary>
    /// Hands every man in every order a running special.
    ///
    /// Deliberately far beyond anything the generator would produce — the point is not to propose
    /// this as a setting but to put an upper bound on what running specials can be worth. If the
    /// whole league running like Rickey Henderson does not recover eleven percent of doubles, then
    /// the missing doubles were never about running specials and no adjustment to their rate will
    /// ever find them.
    /// </summary>
    private static void GiveLegs()
    {
        var rng = new Rng(90210);
        foreach (var p in Lineups()) p.Special = Legs[rng.Range(0, Legs.Length)];
    }

    private static void GiveGap()
    {
        var rng = new Rng(90211);
        foreach (var p in Lineups()) p.Special = Gap[rng.Range(0, Gap.Length)];
    }

    /// <summary>
    /// Reassigns each man's bat special to the thing he is actually good at, keeping the number
    /// of them exactly as it was.
    ///
    /// The generator picks uniformly from seven, which is a fair coin and the wrong question. A
    /// special is a signature, and TurboLegs on a three-speed first baseman is not a signature, it
    /// is a wasted one — while the fastest man on the club draws BuntMaster and his legs go
    /// unremarked. Prevalence is untouched here on purpose: if matching them to the men moves the
    /// extra-base hits, the fault was never how many there were.
    /// </summary>
    private static void MatchSpecials()
    {
        var rng = new Rng(90213);

        foreach (var p in Lineups())
        {
            if (p.Special == Special.None || Glove.Contains(p.Special)) continue;

            // Which of the three he is most notable for, measured against the centre each rating
            // is generated around rather than against each other.
            float legs = p.Speed - 5.2f;
            float pop = p.Power - 5.0f;
            float bat = p.Contact - 5.4f;

            p.Special = legs >= pop && legs >= bat ? Legs[rng.Range(0, Legs.Length)]
                      : pop >= bat ? Gap[rng.Range(0, Gap.Length)]
                      : Hands[rng.Range(0, Hands.Length)];
        }
    }

    /// <summary>What a contact hitter is known for.</summary>
    private static readonly Special[] Hands =
        { Special.ContactMaster, Special.SprayHitter, Special.BuntMaster };

    /// <summary>
    /// Takes the given fraction of a point off every lineup's glove and arm.
    ///
    /// Ratings are whole numbers, so a mean cannot be shifted by a third of a point directly; the
    /// same fraction of the men are docked a full point instead, which moves the average by the
    /// amount asked for and leaves the spread alone. Seeded, so the experiment repeats.
    ///
    /// The numbers passed in are the gaps --talent measured between the two leagues' lineups:
    /// 6.36 against 6.68 with the glove, 6.29 against 6.45 with the arm. If bringing the generated
    /// men down to the written cast's level puts the doubles back, then that gap is the cause and
    /// nothing else needs looking at.
    /// </summary>
    private static void Deglove(float fielding, float arm)
    {
        var rng = new Rng(90212);
        foreach (var p in Lineups())
        {
            if (rng.NextFloat() < fielding) p.Fielding = Mathf.Max(1, p.Fielding - 1);
            if (rng.NextFloat() < arm) p.Arm = Mathf.Max(1, p.Arm - 1);
        }
    }
}
