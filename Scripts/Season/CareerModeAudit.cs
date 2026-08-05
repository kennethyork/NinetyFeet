using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.Stats;

namespace SandlotSlugfest.Season;

/// <summary>
/// Plays whole careers out, so the mode can be judged rather than described.
///
/// A career is a decade or two of promotions, development and eventually retirement, and every
/// part of that was machinery nobody had ever run end to end. It compiled and the screens drew.
/// Whether a man actually climbs the ladder, reaches the majors, or simply sits in High-A until
/// he is forty was an open question.
///
/// Not the same thing as <see cref="CareerAudit"/>, which follows the whole league's development
/// through an offseason. This follows one man through the career mode's own machinery: the builds,
/// the promotion bar, the call-up, and the retirement check.
/// </summary>
public static class CareerModeAudit
{
    public static void Run(int careers)
    {
        var season = new SeasonState();
        season.StartNew(RosterGenerator.DefaultLeagueSeed, 0, Schedule.FullSeason, 9);

        GD.Print($"\n=== CAREER MODE — {careers} men, played to the end ===");

        int reached = 0, retiredBelow = 0, neverFinished = 0, yearsToMajors = 0, peakSum = 0;

        for (int n = 0; n < careers; n++)
        {
            var build = CareerEngine.Builds[n % CareerEngine.Builds.Length];
            var c = CareerEngine.Create($"Test{n}", build.Name.Split(' ')[0], build,
                Handedness.Right, n % Teams.All.Count, 90210 + n * 37);

            int draftedAt = c.Player.Overall;
            int arrived = -1;

            // Twenty-five years is longer than any real career. If he is still going after that,
            // the retirement check is not firing and the mode never ends.
            for (int year = 0; year < 25 && !c.Retired; year++)
            {
                for (int g = 0; g < CareerState.SeasonLength; g++)
                    CareerEngine.BookGame(c, ModelGame(c, year * 1000 + g + n * 77));

                CareerEngine.EndSeason(c, season, 7717 + n * 13 + year);
                if (arrived < 0 && c.InTheMajors) arrived = c.Year;
            }

            if (arrived > 0) { reached++; yearsToMajors += arrived; }
            else if (c.Retired) retiredBelow++;
            else neverFinished++;

            peakSum += c.PeakOverall;

            if (n < 3) Story(c, build, draftedAt);
        }

        GD.Print($"\n  reached the majors:      {reached} of {careers}" +
                 (reached > 0 ? $"  (after {yearsToMajors / (float)reached:F1} years on average)" : ""));
        GD.Print($"  retired without arriving: {retiredBelow}");
        GD.Print($"  still going after 25 yrs: {neverFinished}");
        GD.Print($"  average peak rating:      {peakSum / (float)careers:F1}");

        GD.Print(reached > 0 && neverFinished == 0
            ? "\n  VERDICT: the ladder works. Men climb it, arrive, and eventually finish."
            : "\n  VERDICT: FAILED — nobody arrives, or nobody ever stops.");
    }

    private static void Story(CareerState c, CareerEngine.Build build, int draftedAt)
    {
        GD.Print($"\n  {build.Name}  ·  drafted at {draftedAt}, ceiling {c.DraftCeiling}, " +
                 $"peaked at {c.PeakOverall}");
        GD.Print($"    finished rated {c.Player.Overall} at {c.Age}, " +
                 $"{(c.EndedBecause == "" ? "still playing" : c.EndedBecause)}, " +
                 $"{(c.InTheMajors ? "in the majors" : Farm.Name(c.Level ?? Farm.Level.HighA))}");
        GD.Print($"    career: {c.Career.Games} games, {BattingLine.Rate(c.Career.Average)}, " +
                 $"{c.Career.HomeRuns} home runs, {c.Career.RunsBattedIn} driven in");

        foreach (string line in c.Journal.AsEnumerable().Reverse().Take(6))
            GD.Print($"      {line}");
    }

    /// <summary>One game's line, modelled exactly as the career screen models a day off.</summary>
    private static BattingLine ModelGame(CareerState c, int seed)
    {
        var rng = new Rng(seed * 31 + c.TeamId + 5);
        var line = new BattingLine { Games = 1 };

        int pa = rng.Range(3, 6);
        for (int i = 0; i < pa; i++)
        {
            line.PlateAppearances++;
            if (rng.Chance(0.09f)) { line.Walks++; continue; }

            line.AtBats++;
            float skill = (c.Player.Contact + c.Player.Power) / 20f;
            if (rng.Chance(0.16f + (1f - skill) * 0.12f)) { line.Strikeouts++; continue; }
            if (!rng.Chance(0.20f + skill * 0.16f)) continue;

            line.Hits++;
            if (rng.Chance(0.09f + c.Player.Power / 10f * 0.10f)) { line.HomeRuns++; line.Runs++; }
            else if (rng.Chance(0.22f)) line.Doubles++;
            if (rng.Chance(0.36f)) line.RunsBattedIn++;
        }

        return line;
    }
}
