using System;
using Godot;

namespace SandlotSlugfest.Data;

/// <summary>
/// One ballpark per club, all 32 of them distinct. Distances run left-field line, left gap,
/// centre, right gap, right-field line; heights match. No two parks share a footprint, and the
/// quirks are meant to change how a game plays there, not just how it looks.
/// </summary>
public static class Stadiums
{
    private static Stadium[] _all;

    public static Stadium For(int teamId) => All[teamId];
    public static Stadium For(TeamData team) => All[team.Id];

    public static Stadium[] All
    {
        get
        {
            if (_all != null) return _all;

            // Built first, then anything the player has rebuilt is laid over the top — the same
            // arrangement as the clubs, and for the same reason: the grounds in the source stay
            // the source of truth, so a park can always be put back.
            _all = Build();
            ParkEdits.ApplyAll();
            return _all;
        }
    }

    /// <summary>Throws the parks away so they are built again from source, edits and all.</summary>
    public static void Rebuild()
    {
        _all = null;
        _ = All;
    }

    private static Stadium Make(
        int id, string name, string quirk,
        float[] distances, float[] heights,
        string grass, string dirt, string wall, string trim,
        float air = 1f, float foul = 1f, bool covered = false) =>
        new()
        {
            TeamId = id,
            Name = name,
            Quirk = quirk,
            Distances = distances,
            Heights = heights,
            Grass = new Color(grass),
            GrassAlt = new Color(grass).Darkened(0.16f),
            Dirt = new Color(dirt),
            Wall = new Color(wall),
            WallTrim = new Color(trim),
            AirDensity = air,
            FoulTerritory = foul,
            Covered = covered,
        };

    private static Stadium[] Build()
    {
        var parks = new[]
        {
            // ---------------- AMERICAN LEAGUE EAST ----------------
            Make(0, "The Crab Pot", "Deep right field and a brick warehouse beyond it.",
                new[] { 333f, 371f, 402f, 385f, 318f }, new[] { 7f, 7f, 7f, 21f, 21f },
                "#2f7d43", "#b1793f", "#7d3f2a", "#e8641e"),

            Make(1, "The Boil", "A thirty-seven foot wall in left. Line drives die on it.",
                new[] { 310f, 379f, 390f, 380f, 302f }, new[] { 37f, 17f, 8f, 5f, 5f },
                "#2e8046", "#b5793c", "#1f6b3a", "#f2e4c9"),

            Make(2, "The Bomb Shelter", "A short porch in right that has ended a lot of games.",
                new[] { 318f, 399f, 408f, 385f, 314f }, new[] { 8f, 8f, 8f, 8f, 10f },
                "#2f7d43", "#ad7740", "#1b2a44", "#c9cdd4"),

            Make(3, "The Storm Dome", "Roofed. No wind, no rain, no excuses.",
                new[] { 315f, 370f, 404f, 370f, 322f }, new[] { 12f, 9f, 9f, 9f, 12f },
                "#3a8f52", "#a87c50", "#4a3f8c", "#f5d547", air: 0.99f, covered: true),

            Make(4, "Maple Yard", "Symmetrical and honest. The turf plays fast.",
                new[] { 328f, 375f, 400f, 375f, 328f }, new[] { 10f, 10f, 10f, 10f, 10f },
                "#35894b", "#b0793f", "#c7392f", "#f4f4f0"),

            Make(5, "Le Grand Parc", "Cavernous alleys. Triples happen here.",
                new[] { 325f, 390f, 415f, 390f, 325f }, new[] { 12f, 12f, 12f, 12f, 12f },
                "#2c7a41", "#a97440", "#1d4e89", "#e4572e", air: 1.02f),

            Make(6, "The Amplifier", "Short left, tall wall. Everything is loud.",
                new[] { 312f, 370f, 405f, 375f, 325f }, new[] { 19f, 9f, 9f, 9f, 9f },
                "#2f8145", "#b47c41", "#3b2c6b", "#f0a830"),

            Make(7, "The Assembly", "Deep left centre where fly balls go to die.",
                new[] { 342f, 395f, 420f, 365f, 330f }, new[] { 8f, 8f, 8f, 8f, 8f },
                "#2b7a40", "#ab7238", "#1f4e5f", "#d9531e"),

            // ---------------- AMERICAN LEAGUE WEST ----------------
            Make(8, "The Steelyard", "Small, symmetrical and unforgiving. A launching pad.",
                new[] { 322f, 362f, 396f, 362f, 322f }, new[] { 8f, 8f, 8f, 8f, 8f },
                "#31844a", "#b57c42", "#1a1a1a", "#b0b7bf"),

            Make(9, "The Smokehouse", "Enormous outfield. The gaps swallow line drives.",
                new[] { 330f, 392f, 410f, 392f, 330f }, new[] { 9f, 9f, 9f, 9f, 9f },
                "#2e7f44", "#b17a3e", "#4b3a2f", "#f2a65a"),

            Make(10, "The Loon's Nest", "An overhang in right that steals home runs.",
                new[] { 339f, 377f, 404f, 367f, 328f }, new[] { 8f, 8f, 8f, 23f, 23f },
                "#2d7b42", "#ae7a44", "#123f5e", "#e8edf2"),

            Make(11, "Launch Complex", "A hill in centre field and a flagpole in play.",
                new[] { 315f, 362f, 436f, 373f, 326f }, new[] { 21f, 10f, 10f, 10f, 7f },
                "#31854b", "#b47b3f", "#2b2d6e", "#e6952a", covered: true),

            Make(12, "The Reef", "Rock formations beyond the fence. Plays fair otherwise.",
                new[] { 330f, 370f, 400f, 370f, 330f }, new[] { 8f, 18f, 8f, 8f, 18f },
                "#348a4d", "#b67f45", "#147a8c", "#f2c14e"),

            Make(13, "The Grove", "Acres of foul ground. Pop-ups that should land, don't.",
                new[] { 330f, 388f, 400f, 388f, 330f }, new[] { 8f, 8f, 8f, 8f, 8f },
                "#2c7940", "#a9743c", "#2f5d3a", "#c9a227", foul: 1.7f),

            Make(14, "The Timber", "Marine air off the sound. Nothing carries.",
                new[] { 331f, 378f, 401f, 381f, 326f }, new[] { 8f, 17f, 8f, 8f, 8f },
                "#2a7b3f", "#a5733d", "#22483d", "#8fbf6b", air: 1.07f, covered: true),

            Make(15, "The Funnel", "Wind whips out to right. Fly balls keep going.",
                new[] { 329f, 374f, 407f, 377f, 326f }, new[] { 14f, 8f, 8f, 8f, 14f },
                "#31824a", "#bb8043", "#6b7a8f", "#d64545", air: 0.95f),

            // ---------------- NATIONAL LEAGUE EAST ----------------
            Make(16, "The Orchard", "Short porches both ways. Bring your bat.",
                new[] { 320f, 380f, 400f, 375f, 325f }, new[] { 8f, 8f, 8f, 8f, 8f },
                "#31874b", "#b67e42", "#e8874a", "#3e6b4a", air: 0.97f),

            Make(17, "The Rookery", "A deep, strange centre field with a sculpture in play.",
                new[] { 344f, 386f, 422f, 387f, 335f }, new[] { 11f, 11f, 11f, 11f, 11f },
                "#2d8146", "#ac7742", "#e85d9e", "#1fb3b3", covered: true),

            Make(18, "The Big Apple", "Fair and deep. Pitchers like it here.",
                new[] { 335f, 385f, 408f, 383f, 330f }, new[] { 8f, 8f, 8f, 15f, 15f },
                "#2c7c41", "#aa7540", "#2c6e49", "#d62828", air: 1.03f),

            Make(19, "The Bell Tower", "Cozy. The ball jumps out to left centre.",
                new[] { 329f, 369f, 401f, 369f, 330f }, new[] { 12f, 12f, 9f, 9f, 13f },
                "#328850", "#b87f44", "#7a5c2e", "#e8dcc0", air: 0.96f),

            Make(20, "The Monument", "Deep to right centre, tight down the lines.",
                new[] { 336f, 377f, 402f, 402f, 335f }, new[] { 8f, 8f, 8f, 14f, 14f },
                "#2e7d43", "#ae7940", "#2a3d66", "#d8d2c4"),

            Make(21, "The Foundry", "A wall of rolled steel in right. It rattles.",
                new[] { 325f, 389f, 399f, 375f, 320f }, new[] { 6f, 6f, 10f, 21f, 21f },
                "#2f8044", "#b07a3f", "#2b2b2b", "#f2b705"),

            Make(22, "The Landing", "River beyond right field. Splash hits count double, socially.",
                new[] { 328f, 379f, 404f, 370f, 325f }, new[] { 12f, 12f, 12f, 12f, 12f },
                "#307f46", "#b27b40", "#a32b2b", "#f0e6d2"),

            Make(23, "The Coop", "Brand new, built small on purpose. Expansion fireworks.",
                new[] { 318f, 366f, 395f, 366f, 318f }, new[] { 8f, 8f, 8f, 8f, 8f },
                "#33884c", "#b98044", "#d94e1f", "#f4c542", air: 0.96f),

            // ---------------- NATIONAL LEAGUE WEST ----------------
            Make(24, "The Trellis", "Ivy-covered brick. The wind decides everything.",
                new[] { 325f, 368f, 400f, 368f, 353f }, new[] { 11f, 11f, 11f, 11f, 11f },
                "#2f8244", "#ad763d", "#2e5e3a", "#c8352c", air: 0.98f),

            Make(25, "The Creamery", "Roofed and cozy. A slugger's living room.",
                new[] { 342f, 371f, 400f, 374f, 345f }, new[] { 8f, 8f, 8f, 8f, 8f },
                "#358a4e", "#b77e43", "#f2b231", "#2f4a7a", air: 0.97f, covered: true),

            Make(26, "The Gateway", "Big and fair. Doubles into the alleys.",
                new[] { 336f, 375f, 400f, 375f, 335f }, new[] { 8f, 8f, 8f, 8f, 8f },
                "#2d7e43", "#af7841", "#b03a2e", "#c9cdd1", air: 1.01f),

            Make(27, "The Arroyo", "A pool beyond right and desert air. It carries.",
                new[] { 330f, 374f, 407f, 374f, 335f }, new[] { 8f, 8f, 25f, 8f, 8f },
                "#31854a", "#c08a4e", "#7a3b8f", "#e9c46a", air: 0.93f, covered: true),

            Make(28, "The Summit", "A mile of thin air. Everything flies. Enormous outfield.",
                new[] { 347f, 390f, 415f, 375f, 350f }, new[] { 8f, 8f, 8f, 14f, 14f },
                "#2c8043", "#b57d42", "#3a4a6b", "#e0e5ec", air: 0.82f),

            Make(29, "The Backlot", "Perfect weather, deep power alleys, heavy night air.",
                new[] { 330f, 385f, 395f, 385f, 330f }, new[] { 8f, 8f, 8f, 8f, 8f },
                "#2f8347", "#b47c41", "#14141e", "#e8c547", air: 1.05f),

            Make(30, "The Break", "Deep and cool. Fly balls hang up forever.",
                new[] { 336f, 390f, 396f, 382f, 322f }, new[] { 8f, 8f, 8f, 8f, 18f },
                "#2b7c40", "#a97640", "#6b4226", "#f0c987", air: 1.06f),

            Make(31, "The Cove", "Triples alley in right centre and cold air off the bay.",
                new[] { 339f, 364f, 399f, 415f, 309f }, new[] { 8f, 8f, 8f, 8f, 25f },
                "#2a7940", "#a5733c", "#5b6b7a", "#e85d2a", air: 1.09f),
        };

        if (parks.Length != 32)
            throw new InvalidOperationException($"Expected 32 ballparks, found {parks.Length}.");

        for (int i = 0; i < parks.Length; i++)
        {
            var p = parks[i];
            if (p.TeamId != i)
                throw new InvalidOperationException($"Ballpark {i} has mismatched TeamId {p.TeamId}.");
            if (p.Distances.Length != 5 || p.Heights.Length != 5)
                throw new InvalidOperationException(
                    $"{p.Name} needs five distance and five height control points.");

            // A park that is absurdly small or huge would quietly wreck the balance numbers.
            foreach (float d in p.Distances)
                if (d is < 295f or > 450f)
                    throw new InvalidOperationException($"{p.Name} has an implausible fence at {d}ft.");
            foreach (float h in p.Heights)
                if (h is < 3f or > 40f)
                    throw new InvalidOperationException($"{p.Name} has an implausible wall at {h}ft.");
        }

        return parks;
    }
}
