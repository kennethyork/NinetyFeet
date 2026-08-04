using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Season;

public enum Sky { Clear, Overcast, Rain }

/// <summary>Conditions at first pitch.</summary>
public readonly struct Conditions
{
    public readonly Sky Kind;
    public readonly int TemperatureF;

    /// <summary>Feet per second, positive blowing out to centre field.</summary>
    public readonly float Wind;

    public Conditions(Sky kind, int temperature, float wind)
    {
        Kind = kind; TemperatureF = temperature; Wind = wind;
    }

    public string Text => Kind switch
    {
        Sky.Rain => $"Rain, {TemperatureF}°",
        Sky.Overcast => $"Overcast, {TemperatureF}°",
        _ => $"Clear, {TemperatureF}°",
    };

    /// <summary>"12 mph out to left" — the line a broadcast reads before a home run.</summary>
    public string WindText
    {
        get
        {
            int mph = Mathf.RoundToInt(Mathf.Abs(Wind) * 0.682f);
            if (mph < 4) return "Calm";
            return Wind > 0 ? $"{mph} mph out" : $"{mph} mph in";
        }
    }
}

/// <summary>
/// The weather. It is worth having for its own sake — a cold grey Tuesday in April should not
/// look like a July evening — but it also does real work: cold air is dead air, a wind blowing
/// out turns fly balls into home runs, and rain keeps people at home.
/// </summary>
public static class Weather
{
    /// <summary>
    /// Conditions for a scheduled game. Deterministic in the date and the park, so the same game
    /// always plays under the same sky.
    /// </summary>
    public static Conditions For(SeasonState season, ScheduledGame game)
    {
        var park = Stadiums.For(Teams.Get(game.HomeId));
        if (park is { Covered: true }) return new Conditions(Sky.Clear, 72, 0f);

        var rng = new Rng(game.HomeId * 104729 + game.Day * 313 + season.Year * 7919);
        var date = Calendar.DateOf(game.Day);

        // Baseball is played from spring into autumn, so the temperature curve peaks in July.
        float summer = Mathf.Cos((date.DayOfYear - 196) / 365f * Mathf.Tau) * 0.5f + 0.5f;
        int temperature = Mathf.RoundToInt(Mathf.Lerp(48f, 88f, summer) + rng.Range(-9f, 9f));

        float roll = rng.NextFloat();
        var kind = roll switch
        {
            < 0.09f => Sky.Rain,
            < 0.34f => Sky.Overcast,
            _ => Sky.Clear,
        };

        // Wind is mostly gentle and occasionally decisive, and it blows in as often as out.
        float wind = (rng.Bell() - 0.5f) * 44f;

        return new Conditions(kind, temperature, wind);
    }
}
