using SandlotSlugfest.Core;

namespace SandlotSlugfest.Data;

/// <summary>
/// Gives a generated player a reputation that fits him.
///
/// A hand-written player reads as written because everything about him agrees: Marcus Okafor is a
/// slugger, is built like one, and his line says he hits it a mile or not at all. A generated
/// player used to get a line pulled at random off a shelf, and that mismatch is what made him feel
/// assembled — the words said one thing and the ratings said another.
///
/// So the line is chosen from what the player actually is. A man who cannot make contact but hits
/// the ball a long way is told he hits it a long way. A shortstop with a cannon gets a line about
/// his arm. It reads as though somebody wrote him because, in the only sense that shows on screen,
/// somebody did.
/// </summary>
public static class Flavour
{
    private static readonly string[] Slugger =
    {
        "Hits it a long way or not at all, and is at peace with that.",
        "Swings hard in case he connects. He connects often enough.",
        "Three outfielders and none of them play shallow.",
        "Every ball he squares up leaves in a hurry.",
        "Has cleared everything in this league at least once.",
        "The strikeouts are the price and nobody is arguing about it.",
        "Turns a mistake pitch into a souvenir.",
        "Pitchers work around him and the numbers say they should.",
    };

    private static readonly string[] Contact =
    {
        "Puts the bat on the ball whatever you throw him.",
        "Fouls off anything close until he gets something to hit.",
        "Has not struck out looking since anyone can remember.",
        "Short swing, quick hands, no interest in the fences.",
        "Works a count like a man being paid by the pitch.",
        "Goes the other way on purpose and does it well.",
        "The at-bat is never over as far as he is concerned.",
        "Hard to strike out and harder to talk about.",
    };

    private static readonly string[] Speed =
    {
        "Turns a single into a double on outfielder hesitation.",
        "Down the line quicker than anyone expects.",
        "Runs the bases like the outfield owes him money.",
        "Scores from second on a ball nobody thought would get through.",
        "Beats out infield hits that ought to be routine.",
        "The pitcher looks over twice before every delivery.",
        "Takes the extra base the moment you blink.",
        "Fast enough that a hit means two and a walk means trouble.",
    };

    private static readonly string[] Glove =
    {
        "Catches what he reaches and reaches most of it.",
        "Makes the hard play look ordinary and the ordinary look boring.",
        "The pitchers on this staff love seeing him behind them.",
        "Never takes a bad route. Not once, not ever.",
        "Has saved more runs than his bat has driven in.",
        "First to the ball and calm when he gets there.",
        "Nobody covers more ground and says less about it.",
        "Would be famous if defence made anybody famous.",
    };

    private static readonly string[] Arm =
    {
        "Runners stopped testing him two seasons ago.",
        "Throws from the corner on a line and on the bag.",
        "Has an arm the whole division talks about.",
        "One throw a game reminds you why he plays out there.",
        "Guns down anybody who forgets who is fielding it.",
    };

    private static readonly string[] Heat =
    {
        "Throws hard, throws often, explains nothing.",
        "The fastball is the plan and it is usually enough.",
        "Hitters foul it back and shake out their hands.",
        "Radar gun material, and he knows where the gun is.",
        "Strikes out the side and walks off as though it was owed.",
        "Overpowers the bottom of an order without changing speeds.",
    };

    private static readonly string[] Command =
    {
        "Paints the corner all afternoon. The corner never moves.",
        "Throws strikes and trusts the men behind him.",
        "Same delivery, same spot, first inning or ninth.",
        "Walks nobody and dares you to do something about it.",
        "Puts it where the catcher set up, pitch after pitch.",
        "Not much on the gun and it has never once mattered.",
    };

    private static readonly string[] Workhorse =
    {
        "Takes the ball every fifth day and finishes what he starts.",
        "Eats innings the way other men eat sunflower seeds.",
        "Would pitch both ends of a doubleheader if anyone let him.",
        "Never asks out. The manager stopped offering.",
        "Still throwing in the eighth with the delivery he opened with.",
    };

    private static readonly string[] Veteran =
    {
        "Has outlasted three managers and a rebuild.",
        "Older than the box score suggests and sharper than it shows.",
        "Knows every pitcher in this league and most of their tells.",
        "Twelve years in and still first out for infield practice.",
        "Not what he was, and still better than what would replace him.",
        "Plays like a man who has counted how many he has left.",
    };

    private static readonly string[] Rookie =
    {
        "Young enough that nobody knows what he is yet.",
        "Came up quicker than the club planned and has not gone back.",
        "Raw, quick, and a season away from figuring it out.",
        "Everything ahead of him and none of it settled.",
        "The scouts disagree about him, which is usually a good sign.",
        "First full season and already unafraid of the moment.",
    };

    /// <summary>A useful regular who is nobody's star. Genuinely complimentary, and it should be.</summary>
    private static readonly string[] Journeyman =
    {
        "Steady enough that his manager sleeps at night.",
        "Never the story, often the reason.",
        "Does one thing well and knows exactly what it is.",
        "Plays hard on a cold Tuesday in April.",
        "Fields his position well enough that nobody mentions it.",
        "Would be a starter on half the clubs in this league.",
    };

    /// <summary>
    /// The end of the bench and the end of the staff.
    ///
    /// Every player below eight in everything used to fall into <see cref="Journeyman"/>, whose
    /// lines run from dismissive to flattering — so a pitcher with a four velocity and a five
    /// command was told he would start for half the league. A card that praises a bad player is
    /// worse than no card, because the reader learns not to trust any of them.
    /// </summary>
    private static readonly string[] Fringe =
    {
        "The last man on the roster and the first one out to stretch.",
        "Nobody's first choice and nobody's problem.",
        "Here because somebody has to be, and he does not complain about it.",
        "A useful body on a long road trip.",
        "Out of options and out of excuses.",
        "Holds the spot until the club finds someone better.",
        "Every league needs men who take the ball in a lost cause.",
        "Plays like he knows the bus to Triple-A runs both ways.",
    };

    /// <summary>Not good enough yet, not old enough to worry. A prospect's card.</summary>
    private static readonly string[] Raw =
    {
        "All arms and legs and nothing settled.",
        "A long way from ready and further from finished.",
        "The tools are real. The results are not, yet.",
        "Somebody in the organisation still believes, and he is not wrong to.",
        "Needs a full year somewhere quiet before anyone judges him.",
    };

    /// <summary>
    /// The line for a player. Written players keep the one they were given; everyone else gets one
    /// drawn from what he is, keyed to his look seed so it never changes between viewings.
    /// </summary>
    public static string For(PlayerData p)
    {
        if (p == null) return "";
        if (p.IsLegend) return Legends.Bio(p.LegendId);

        var pool = PoolFor(p);
        var rng = new Rng(p.LookSeed ^ 0x5F3A);
        return pool[rng.Range(0, pool.Length)];
    }

    /// <summary>
    /// Picks the set of lines that actually describes this player. The most striking thing about
    /// him wins, so the line says something true and specific rather than something safe.
    /// </summary>
    private static string[] PoolFor(PlayerData p)
    {
        if (p.Position == Position.P)
        {
            if (p.Stamina >= 8) return Workhorse;
            if (p.PitchPower >= 8) return Heat;
            if (p.PitchControl >= 8) return Command;
            if (p.Age >= 33) return Veteran;

            // A young player who is bad is a prospect; an old one who is bad is a spare part.
            // Sorting on age alone told a 22-year-old with a four fastball that the scouts
            // disagreed about him, which reads as praise it has not earned.
            if (p.Overall <= 4) return p.Age <= 23 ? Raw : Fringe;
            if (p.Age <= 23) return Rookie;
            return Journeyman;
        }

        if (p.Power >= 8 && p.Contact <= 6) return Slugger;
        if (p.Contact >= 8) return Contact;
        if (p.Speed >= 8) return Speed;
        if (p.Fielding >= 8) return Glove;
        if (p.Arm >= 9) return Arm;
        if (p.Age >= 33) return Veteran;
        if (p.Overall <= 4) return p.Age <= 23 ? Raw : Fringe;
        if (p.Age <= 23) return Rookie;
        return Journeyman;
    }
}
