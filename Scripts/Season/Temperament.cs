using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>
/// What a player is like, as distinct from how good he is.
///
/// Every man in this league was the same person. Two twenty-three-year-olds with identical
/// ratings developed at the same rate, wanted the same contract, and felt exactly the same about
/// sitting on the bench for four months — which meant there was never a reason to prefer one of
/// them, and a club was a spreadsheet of numbers rather than a room with people in it.
///
/// Three things, and only three, because each one has to actually do something:
///
///   Work ethic  — how much he improves in an off-season. The difference between a prospect who
///                 arrives and one who was always going to be what he already was.
///   Loyalty     — how much of a discount he will take to stay. A loyal man re-signs; a mercenary
///                 goes to the highest bidder and it is not personal.
///   Temperament — how hard he takes things. It moves his morale, it does not move his bat.
///
/// Morale deliberately does not touch on-field performance. It would be the easiest thing in the
/// world to add and it would silently wreck a calibration that took a long time to earn: a league
/// where morale swings hitting is a league whose run environment depends on how happy everybody
/// is. It decides whether he re-signs, how fast he develops, and what he says to you.
/// </summary>
public static class Temperament
{
    /// <summary>Neutral morale. Everything moves relative to this.</summary>
    public const int Settled = 5;

    /// <summary>
    /// Rolls a personality for a new player. Independent of his ratings on purpose — the whole
    /// point is that you cannot tell from the back of the card.
    /// </summary>
    public static void Assign(PlayerData p, ref Rng rng)
    {
        // Bell-shaped, so most men are ordinary and the extremes are worth noticing.
        p.WorkEthic = Mathf.Clamp(Mathf.RoundToInt(rng.Bell() * 10f), 1, 10);
        p.Loyalty = Mathf.Clamp(Mathf.RoundToInt(rng.Bell() * 10f), 1, 10);
        p.Poise = Mathf.Clamp(Mathf.RoundToInt(rng.Bell() * 10f), 1, 10);
        p.Morale = Settled;
    }

    /// <summary>
    /// How much faster or slower than ordinary this man improves. Sized modestly: work ethic is
    /// worth roughly a fifth either way, which over five off-seasons is the difference between a
    /// prospect reaching his ceiling and stalling a grade short of it.
    /// </summary>
    public static float GrowthFactor(PlayerData p)
    {
        if (p == null) return 1f;

        float ethic = 0.82f + p.WorkEthic / 10f * 0.36f;

        // A man who is miserable does not put the work in either.
        float mood = 0.94f + Mathf.Clamp(p.Morale, 0, 10) / 10f * 0.12f;
        return ethic * mood;
    }

    /// <summary>
    /// What he will re-sign for, as a share of his market value. A loyal, happy man takes less to
    /// stay; an unhappy one wants paying to put up with it.
    /// </summary>
    public static float AskingFactor(PlayerData p)
    {
        if (p == null) return 1f;

        float loyal = 1.10f - p.Loyalty / 10f * 0.20f;
        float mood = 1.14f - Mathf.Clamp(p.Morale, 0, 10) / 10f * 0.22f;
        return Mathf.Clamp(loyal * mood, 0.80f, 1.30f);
    }

    // -----------------------------------------------------------------------
    // What moves it
    // -----------------------------------------------------------------------

    /// <summary>
    /// Settles everybody's mood at the end of a season, from things that actually happened to
    /// them: whether they played, whether the club won, and how near the end of the contract is.
    ///
    /// A man with poise takes all of it more evenly, in both directions.
    /// </summary>
    public static void EndOfSeason(SeasonState season)
    {
        foreach (var team in Teams.All)
        {
            var rec = season.Book.Record(team.Id);
            var roster = season.RosterFor(team.Id);

            // Winning is worth something to everybody in the room.
            float club = rec.Games > 0 ? (rec.WinPct - 0.500f) * 6f : 0f;

            foreach (var p in roster.Players)
            {
                float move = club;

                // Playing time, which is what a player actually cares about.
                var line = season.Book.Batting(p);
                var arm = season.Book.Pitching(p);
                bool played = p.Position == Data.Position.P
                    ? arm.Outs >= 60
                    : line.PlateAppearances >= 180;
                bool buried = p.Position == Data.Position.P
                    ? arm.Outs < 15
                    : line.PlateAppearances < 40;

                if (played) move += 1.2f;
                else if (buried) move -= 1.8f;

                // The last year of a deal is unsettling, whatever else is going on.
                if (p.ContractYears <= 1) move -= 0.6f;

                // Poise damps it, both ways. A steady man is not elated either.
                move *= 1.25f - p.Poise / 10f * 0.5f;

                // And everybody drifts back toward level over a winter.
                float toward = (Settled - p.Morale) * 0.25f;

                p.Morale = Mathf.Clamp(Mathf.RoundToInt(p.Morale + move + toward), 0, 10);
            }
        }
    }

    /// <summary>Words for it, since a number out of ten tells a manager nothing.</summary>
    public static string MoraleText(int morale) => morale switch
    {
        >= 9 => "delighted to be here",
        >= 7 => "happy",
        >= 4 => "content",
        >= 2 => "unsettled",
        _ => "wants out",
    };

    public static string EthicText(int ethic) => ethic switch
    {
        >= 9 => "first in, last out",
        >= 7 => "works at it",
        >= 4 => "does what is asked",
        >= 2 => "coasts",
        _ => "has to be dragged",
    };

    public static string LoyaltyText(int loyalty) => loyalty switch
    {
        >= 9 => "would stay for nothing",
        >= 7 => "likes it here",
        >= 4 => "will listen",
        >= 2 => "follows the money",
        _ => "a mercenary",
    };

    /// <summary>The one-line read a scout would give you.</summary>
    public static string Summary(PlayerData p) =>
        p == null ? "" : $"{EthicText(p.WorkEthic)} · {LoyaltyText(p.Loyalty)} · {MoraleText(p.Morale)}";
}
