using Godot;

namespace SandlotSlugfest.Data;

/// <summary>
/// What each club's three farm sides are called, and where they play.
///
/// Affiliates were drawn as "Blue Crabs (Triple-A)" — the parent's name with the rung in brackets,
/// which is a label rather than a club. That is fine while the farm is a table of numbers and
/// wrong the moment you can watch one play, take the dugout for a night, or spend a career
/// climbing through them. A prospect grinding three years in the Wilmington Blue Rocks is a story;
/// three years in "Blue Crabs (Double-A)" is a spreadsheet cell.
///
/// Every name here is original, the same as the thirty-two parent clubs. The towns are real
/// places in each parent's region — that is geography, not a trademark — but the nicknames are
/// invented, and they lean the way minor-league names actually lean: local industry, local food,
/// local weather, local jokes. A Milwaukee farmhand comes up through Madison, Green Bay and
/// Appleton playing for the Curds, the Cheddar and the Whey.
/// </summary>
public static class Affiliates
{
    /// <summary>City and nickname for one affiliate.</summary>
    public readonly record struct Club(string City, string Nickname)
    {
        public string FullName => $"{City} {Nickname}";
    }

    /// <summary>Triple-A, Double-A, High-A, for each of the 32 clubs in team-id order.</summary>
    private static readonly Club[][] All =
    {
        // 0 Baltimore Blue Crabs
        Row("Chesapeake", "Skipjacks", "Annapolis", "Watermen", "Ocean City", "Sandpipers"),
        // 1 Boston Lobsters
        Row("Worcester", "Bay Staters", "Portland", "Lightkeepers", "Lowell", "Millhands"),
        // 2 Bronx Bombardiers
        Row("Scranton", "Anthracite", "Trenton", "Ironworks", "Poughkeepsie", "Riverfolk"),
        // 3 Tampa Bay Thunderheads
        Row("Sarasota", "Squalls", "Ocala", "Thunderclaps", "Fort Myers", "Gale"),
        // 4 Toronto Maple Bats
        Row("Hamilton", "Steel Cats", "London", "Timberjacks", "Sudbury", "Sap Runners"),
        // 5 Montreal Voyageurs
        Row("Quebec", "Portagers", "Sherbrooke", "Trappers", "Gatineau", "Canoemen"),
        // 6 Cleveland Rockers
        Row("Akron", "Amplifiers", "Youngstown", "Backbeat", "Sandusky", "Breakers"),
        // 7 Detroit Motorheads
        Row("Toledo", "Gearheads", "Flint", "Pistons", "Kalamazoo", "Sparkplugs"),
        // 8 South Side Sluggers
        Row("Joliet", "Stockyards", "Rockford", "Foundrymen", "Peoria", "Haymakers"),
        // 9 Kansas City Smoke
        Row("Wichita", "Brisket", "Springfield", "Embers", "Topeka", "Kindling"),
        // 10 Minnesota Loons
        Row("Duluth", "Ore Boats", "Rochester", "Northern Lights", "Mankato", "Goslings"),
        // 11 Houston Moonshots
        Row("Galveston", "Gantry", "Beaumont", "Booster Stage", "Waco", "Countdown"),
        // 12 Anaheim Angelfish
        Row("Riverside", "Tide Pools", "Bakersfield", "Kelp", "Ventura", "Anemones"),
        // 13 Oakland Oaks
        Row("Modesto", "Acorns", "Fresno", "Saplings", "Stockton", "Grove Hands"),
        // 14 Seattle Sasquatch
        Row("Tacoma", "Timberline", "Spokane", "Trailblazers", "Olympia", "Footprints"),
        // 15 Texas Twisters
        Row("Amarillo", "Dust Devils", "Lubbock", "Funnel", "Abilene", "Windrows"),

        // 16 Atlanta Peaches
        Row("Macon", "Preserves", "Augusta", "Orchardmen", "Columbus", "Cobblers"),
        // 17 Miami Flamingos
        Row("Fort Lauderdale", "Wading Birds", "Naples", "Spoonbills", "Key West", "Fledglings"),
        // 18 Queens Apples
        Row("Syracuse", "Orchard", "Binghamton", "Cider Press", "Coney Island", "Crabapples"),
        // 19 Philadelphia Liberty Bells
        Row("Allentown", "Foundry Bells", "Reading", "Clappers", "Camden", "Chimes"),
        // 20 Washington Monuments
        Row("Richmond", "Obelisks", "Harrisburg", "Cornerstones", "Norfolk", "Pediments"),
        // 21 Pittsburgh Ironmen
        Row("Altoona", "Blast Furnace", "Erie", "Puddlers", "Wheeling", "Rivetheads"),
        // 22 Cincinnati Riverboats
        Row("Louisville", "Paddlewheels", "Dayton", "Deckhands", "Evansville", "Steamers"),
        // 23 Nashville Hot Chickens
        Row("Knoxville", "Cayenne", "Chattanooga", "Skillets", "Jackson", "Brine"),
        // 24 North Side Ivy
        Row("Des Moines", "Trellis", "Springfield", "Creepers", "South Bend", "Tendrils"),
        // 25 Milwaukee Cheeseheads
        Row("Madison", "Curds", "Green Bay", "Cheddar", "Appleton", "Whey"),
        // 26 St. Louis Archers
        Row("Memphis", "Fletchers", "Columbia", "Quivers", "Cape Girardeau", "Bowstrings"),
        // 27 Phoenix Roadrunners
        Row("Tucson", "Coyotes", "Yuma", "Ocotillo", "Flagstaff", "Chaparral"),
        // 28 Denver Mountaineers
        Row("Colorado Springs", "Switchbacks", "Pueblo", "Timberline", "Grand Junction", "Cairns"),
        // 29 Hollywood Stars
        Row("Pasadena", "Klieg Lights", "Bakersfield", "Second Unit", "Long Beach", "Extras"),
        // 30 San Diego Surfers
        Row("Chula Vista", "Longboards", "Escondido", "Undertow", "Oceanside", "Shorebreak"),
        // 31 San Francisco Fog
        Row("Sacramento", "Delta Mist", "Stockton", "Marine Layer", "Santa Rosa", "Haar"),
    };

    private static Club[] Row(string c1, string n1, string c2, string n2, string c3, string n3) =>
        new[] { new Club(c1, n1), new Club(c2, n2), new Club(c3, n3) };

    /// <summary>
    /// The affiliate for a club at a rung. <paramref name="level"/> is 0 for Triple-A, 1 for
    /// Double-A, 2 for High-A, matching <c>Farm.Level</c>.
    /// </summary>
    public static Club For(int teamId, int level)
    {
        if (teamId < 0 || teamId >= All.Length) return new Club("", "Affiliate");
        return All[teamId][Mathf.Clamp(level, 0, 2)];
    }
}
