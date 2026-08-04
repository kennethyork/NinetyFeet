using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

/// <summary>
/// The affiliates' own seasons: a fixture every day, a running record, and a game you can go to.
///
/// The farm used to produce statistics and nothing else — every prospect got a season line at the
/// end of the year out of a distribution, and there was no such thing as a Tuesday in Double-A. A
/// line in a table is not a season. A season is a club with a record, playing somebody tonight.
///
/// The affiliates travel with the parent club: on a day the big club is at Denver, its Triple-A
/// side plays Denver's Triple-A side, home and away the same way. Real minor-league schedules do
/// not work like this, but inventing three more full schedules would cost a great deal of memory
/// and save file to express something no one would ever look at, and this way the organisation
/// moves as one thing — which is how it feels to follow one.
///
/// The daily results are modelled from the two sides' strength rather than simulated pitch by
/// pitch. Ninety-six affiliates playing full games every day would triple the cost of advancing a
/// single day, and nobody is watching the other ninety-five. The one you go to is played for real,
/// and that result is the one that goes in the book.
/// </summary>
public static class FarmSeason
{
    public sealed class Standing
    {
        public int Wins;
        public int Losses;
        public int RunsFor;
        public int RunsAgainst;

        public int Played => Wins + Losses;
        public float Pct => Played == 0 ? 0f : Wins / (float)Played;
        public string Text => $"{Wins}-{Losses}";
    }

    private static readonly Dictionary<int, Standing> Records = new();

    private static int Key(int teamId, Farm.Level level) => teamId * 10 + (int)level;

    public static Standing Of(int teamId, Farm.Level level)
    {
        int key = Key(teamId, level);
        if (Records.TryGetValue(key, out var s)) return s;
        return Records[key] = new Standing();
    }

    public static void Clear() => Records.Clear();

    /// <summary>Every club at one rung, best record first — the affiliate standings.</summary>
    public static List<(TeamData Team, Standing Record)> Table(Farm.Level level) =>
        Teams.All.Select(t => (t, Of(t.Id, level)))
             .OrderByDescending(x => x.Item2.Pct)
             .ThenByDescending(x => x.Item2.Wins)
             .ToList();

    /// <summary>Where a club sits at a rung, counting from one.</summary>
    public static int RankOf(int teamId, Farm.Level level) =>
        Table(level).FindIndex(x => x.Team.Id == teamId) + 1;

    // -----------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------

    /// <summary>
    /// Who this club's affiliate plays on a given day, or null on an off day. Taken from the big
    /// club's schedule, because the organisation travels together.
    /// </summary>
    public static ScheduledGame FixtureFor(SeasonState season, int teamId, int day) =>
        season.Games.FirstOrDefault(g => g.Day == day && g.Involves(teamId));

    /// <summary>Today's fixture for the user's affiliate, if there is one.</summary>
    public static ScheduledGame Today(SeasonState season, int teamId) =>
        FixtureFor(season, teamId, season.CurrentDay);

    // -----------------------------------------------------------------------
    // Playing the day out
    // -----------------------------------------------------------------------

    /// <summary>
    /// Books one day at every rung. Called as the big-league day is advanced, so the whole
    /// organisation moves at once.
    /// </summary>
    public static void PlayDay(SeasonState season, int day)
    {
        foreach (var game in season.Games.Where(g => g.Day == day))
            foreach (var level in Farm.Levels)
                PlayOne(season, game.AwayId, game.HomeId, level, day);
    }

    /// <summary>One affiliate fixture, scored from the two sides' strength.</summary>
    private static void PlayOne(SeasonState season, int awayId, int homeId,
        Farm.Level level, int day)
    {
        // Deterministic in the fixture, so advancing the same day twice cannot invent a result and
        // reloading a save cannot change one that already happened.
        var rng = new Rng(season.Year * 7919 + day * 613 + awayId * 37 + homeId * 11
                          + (int)level * 101);

        int away = ScoreFor(Strength(awayId, level), Strength(homeId, level), ref rng);
        int home = ScoreFor(Strength(homeId, level), Strength(awayId, level), ref rng);

        // Somebody has to win. Extra innings, in effect.
        while (away == home) home += rng.Chance(0.5f) ? 1 : -1;
        if (home < 0) { home = away + 1; }

        Book(awayId, level, away, home);
        Book(homeId, level, home, away);
    }

    private static void Book(int teamId, Farm.Level level, int scored, int allowed)
    {
        var s = Of(teamId, level);
        s.RunsFor += scored;
        s.RunsAgainst += allowed;
        if (scored > allowed) s.Wins++; else s.Losses++;
    }

    /// <summary>How good an affiliate is, as the mean of the men on it.</summary>
    private static float Strength(int teamId, Farm.Level level)
    {
        var men = Farm.Of(teamId, level);
        return men.Count == 0 ? 5f : (float)men.Average(p => p.Overall);
    }

    /// <summary>
    /// Runs for one side. Centred near four and a half, which is roughly what a club scores, and
    /// pulled up or down by how it matches the opposition.
    /// </summary>
    private static int ScoreFor(float mine, float theirs, ref Rng rng)
    {
        float edge = Mathf.Clamp((mine - theirs) * 0.45f, -2.2f, 2.2f);
        float mean = 4.35f + edge;

        // Bell-shaped around the mean, so blowouts are rare and one-run games are not.
        float roll = (rng.Bell() - 0.5f) * 2f;
        return Mathf.Max(0, Mathf.RoundToInt(mean + roll * 4.4f));
    }

    /// <summary>
    /// Replaces a modelled result with one that was actually played.
    ///
    /// The day's fixture was already booked when the day advanced, so going to the game means
    /// taking that result back out and putting the real one in. Otherwise attending would quietly
    /// give your affiliate two games.
    /// </summary>
    public static void ReplaceResult(int teamId, Farm.Level level, int scored, int allowed,
        int wasScored, int wasAllowed)
    {
        var s = Of(teamId, level);

        s.RunsFor -= wasScored;
        s.RunsAgainst -= wasAllowed;
        if (wasScored > wasAllowed) s.Wins--; else s.Losses--;

        s.RunsFor += scored;
        s.RunsAgainst += allowed;
        if (scored > allowed) s.Wins++; else s.Losses++;
    }

    /// <summary>What the modelled result for a fixture was, so it can be taken back out.</summary>
    public static (int Mine, int Theirs) ModelledResult(SeasonState season, int teamId,
        int opponentId, Farm.Level level, int day, bool userIsHome)
    {
        var rng = new Rng(season.Year * 7919 + day * 613
                          + (userIsHome ? opponentId : teamId) * 37
                          + (userIsHome ? teamId : opponentId) * 11
                          + (int)level * 101);

        float mineStrength = Strength(teamId, level);
        float theirStrength = Strength(opponentId, level);

        // Drawn in the same order as PlayOne: away first, then home.
        int away = ScoreFor(userIsHome ? theirStrength : mineStrength,
                            userIsHome ? mineStrength : theirStrength, ref rng);
        int home = ScoreFor(userIsHome ? mineStrength : theirStrength,
                            userIsHome ? theirStrength : mineStrength, ref rng);

        while (away == home) home += rng.Chance(0.5f) ? 1 : -1;
        if (home < 0) home = away + 1;

        return userIsHome ? (home, away) : (away, home);
    }

    // -----------------------------------------------------------------------
    // Saving
    // -----------------------------------------------------------------------

    public static (int[] Wins, int[] Losses) Export(int teamId)
    {
        var w = new int[Farm.Levels.Length];
        var l = new int[Farm.Levels.Length];

        for (int i = 0; i < Farm.Levels.Length; i++)
        {
            var s = Of(teamId, Farm.Levels[i]);
            w[i] = s.Wins;
            l[i] = s.Losses;
        }

        return (w, l);
    }

    public static void Import(int teamId, int[] wins, int[] losses)
    {
        if (wins == null || losses == null) return;

        for (int i = 0; i < Farm.Levels.Length && i < wins.Length && i < losses.Length; i++)
        {
            var s = Of(teamId, Farm.Levels[i]);
            s.Wins = wins[i];
            s.Losses = losses[i];
        }
    }
}
