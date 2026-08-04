namespace SandlotSlugfest.Data;

/// <summary>
/// Names for generated players, grouped so the two halves belong together.
///
/// The pools used to be drawn from independently, which produced men called Mansa Elkington and
/// Pim Kalinowski — every name technically valid and obviously assembled. A written player reads
/// as a person because Marcus Okafor and Kenji Morikawa are each of a piece, so a given name is
/// now chosen inside a background and the surname comes from the same one.
///
/// Men only, to match the real major-league game. Nicknames like Lefty and Slugger are the sort a
/// clubhouse hands out, so they sit apart and go with any surname.
/// </summary>
public static class NamePools
{
    /// <summary>One background: given names and surnames that sit together.</summary>
    public readonly struct Origin
    {
        public readonly string[] First;
        public readonly string[] Last;

        public Origin(string[] first, string[] last) { First = first; Last = last; }
    }

    public static readonly Origin[] Origins =
    {
        // --- American and British Isles ---
        new(new[]
        {
            "Ace", "Ash", "Bennett", "Bo", "Cal", "Cass", "Charlie", "Cody", "Dandy", "Dex",
            "Duke", "Eddie", "Flip", "Frankie", "Gray", "Gus", "Hank", "Hollis", "Jax", "Nash",
            "Odie", "Ozzy", "Quincy", "Rex", "Roscoe", "Shep", "Sky", "Trill", "Wes", "Wilder",
            "Amos", "Caleb", "Nelson", "Silas", "Wendell", "Griffin", "Rowan", "Cormac", "Ronan",
            "Fionn", "Lorcan", "Callum", "Alistair", "Barnaby", "Winston", "Ellis", "Rafferty",
            "Basil", "Toby", "Teddy", "Vic", "Percy", "Hopper",
        }, new[]
        {
            "Ackley", "Albright", "Ashworth", "Barlow", "Bishop", "Boone", "Brannigan", "Calloway",
            "Clemens", "Crandall", "Danvers", "Devine", "Doyle", "Drummond", "Eastman", "Ellsworth",
            "Fairbanks", "Whitfield", "Whitlock", "Kingsley", "Ashcroft", "Aldridge", "Warrington",
            "Sinclair", "Yates", "Pryor", "Crane", "Fletcher", "Marsh", "Cross", "Vale", "Wren",
            "Shaw", "Moss", "Hale", "Godfrey", "Pearce", "Reed", "Blackwood", "Winslow", "Ainsworth",
            "Halloran", "Gallagher", "Byrne", "Murphy", "Whelan", "Doherty", "Hughes", "Larkspur",
        }),

        // --- Hispanic and Latin American ---
        new(new[]
        {
            "Angel", "Diego", "Javi", "Mateo", "Pablo", "Rafa", "Cirilo", "Fico", "Tadeo", "Tico",
            "Ernesto", "Julio", "Lorenzo", "Miguel", "Oscar", "Pedro", "Ricardo", "Santiago",
            "Vicente", "Alonso", "Andres", "Renzo", "Emilio", "Ignacio", "Rafael", "Marcelo",
            "Teodoro", "Abdiel", "Ismael", "Hugo", "Enzo", "Nico", "Iker", "Alfonso", "Lupe",
        }, new[]
        {
            "Aguilar", "Blanco", "Cabral", "Cardoso", "Castille", "Cortez", "Cruz", "Dacosta",
            "Almeida", "Salazar", "Reina", "Guerrero", "Quintana", "Salcedo", "Duarte", "Herrera",
            "Vega", "Rojas", "Cordero", "Santana", "Bermudez", "Vasquez", "Ortega", "Duran",
            "Recinos", "Iglesias", "Villalobos", "Silva", "Pacheco", "Ruiz", "Barrientos",
            "Trujillo", "Nascimento", "Escobar", "Delgado",
        }),

        // --- Japanese ---
        new(new[]
        {
            "Kenji", "Shiro", "Kaito", "Yuto", "Ronin", "Hiroshi", "Kenta", "Takeshi", "Kenzo",
            "Rui", "Sora", "Jae",
        }, new[]
        {
            "Ando", "Tanaka", "Yamashita", "Morikawa", "Takahashi", "Shimizu", "Fujimoto",
            "Kobayashi", "Watanabe", "Arai", "Ito", "Nakashima", "Hamasaki", "Nakagawa", "Terada",
        }),

        // --- West African ---
        new(new[]
        {
            "Chidi", "Emeka", "Kwame", "Malik", "Jelani", "Idris", "Ugo", "Kofi", "Mansa",
            "Kwesi", "Kwabena", "Omari", "Tapiwa", "Solomon", "Onyeka", "Nnamdi", "Chike",
            "Kelechi", "Obi", "Desmond", "Amare", "Amari", "Zaya",
        }, new[]
        {
            "Abara", "Chudi", "Diallo", "Okafor", "Adeyemi", "Oduya", "Boateng", "Nwosu", "Osei",
            "Mbeki", "Eze", "Asante", "Dube", "Achebe", "Boadu", "Okonjo", "Okonkwo", "Balogun",
            "Kamau", "Bakare", "Anozie", "Mensah", "Nwankwo", "Toure", "Moyo", "Bello", "Mbaye",
            "Dlamini", "Kimathi", "Sowande",
        }),

        // --- Slavic and Eastern European ---
        new(new[]
        {
            "Ivan", "Dmitri", "Sergei", "Nikolai", "Zoltan", "Aleks", "Milos", "Piotr", "Lukas",
            "Andrei", "Pavel", "Nikita", "Damir", "Ilya", "Emil", "Aleksy", "Lucian", "Radu",
            "Tomasz", "Adam", "Lazar", "Ziggy", "Kasper",
        }, new[]
        {
            "Pribylov", "Sokolov", "Kowalczyk", "Nowak", "Petrov", "Novak", "Petrovic", "Volkov",
            "Radovic", "Jovanovic", "Bortnik", "Zielinski", "Kaminski", "Lewandowski", "Orlov",
            "Popov", "Kozlov", "Sorokin", "Milic", "Szabo", "Novotny", "Barbu", "Farkas",
            "Zeleny", "Ostrowski",
        }),

        // --- Nordic and Germanic ---
        new(new[]
        {
            "Lars", "Magnus", "Soren", "Mikko", "Eero", "Otto", "Anders", "Bjorn", "Stellan",
            "Gustav", "Nils", "Klaus", "Wilhelm", "Fabian", "Anton", "Ove", "Bram", "Xander",
            "Jonas", "Linus", "Erik", "Isaac", "Freddie", "Karsten", "Dov",
        }, new[]
        {
            "Halvorsen", "Lindqvist", "Thorvaldsen", "Bergstrom", "Vandermeer", "Kaufman",
            "Lindstrom", "Aaberg", "Berg", "Holm", "Brandt", "Vinter", "Hoffman", "Weiss",
            "Meyer", "Roth", "Blau", "Grimm", "Kirchner", "Bergman", "Lind", "Karlsson",
            "Eriksson", "Visser", "Vos", "Dahl", "Brenner", "Ericsson", "Kristiansen", "Gebhardt",
        }),

        // --- Italian, French, Iberian and Greek ---
        new(new[]
        {
            "Bruno", "Vito", "Rocco", "Dante", "Matteo", "Elio", "Blaise", "Felix", "Remy",
            "Didier", "Pascal", "Cato", "Fenwick", "Osgood", "Auden", "Corbin", "Sil",
        }, new[]
        {
            "Bellini", "Bracco", "Marchetti", "Ricci", "Bianchi", "Ferrari", "Bertani", "Corsetti",
            "Castellanos", "Ferreira", "Delacroix", "Marchand", "Moreau", "Duval", "Baptiste",
            "Fontana", "Riggio", "Kanellis", "Papadakis", "Stavros", "Salgado", "Bianchini",
            "Pellegrino", "Gallardo", "Camarillo",
        }),

        // --- Middle Eastern, Turkish and Persian ---
        new(new[]
        {
            "Aziz", "Omar", "Salim", "Tariq", "Yosef", "Zaid", "Nabil", "Samir", "Rashid",
            "Ibrahim", "Hakeem", "Yusuf", "Kian", "Cyrus", "Amir", "Hassan", "Osman", "Ari",
            "Zeke", "Ilias", "Uriel", "Zeb",
        }, new[]
        {
            "Bektas", "Haddad", "Qureshi", "Nasser", "Zand", "Demir", "Ozturk", "Farouk",
            "Mansour", "Rahim", "Karim", "Bouhali", "Mizrahi", "Adler", "Yilmaz", "Halberstam",
            "Alavi",
        }),

        // --- South and Southeast Asian ---
        new(new[]
        {
            "Arjun", "Ravi", "Sanjay", "Rohan", "Vikram", "Naveen", "Mohan", "Devraj", "Sunil",
            "Priyansh", "Sanjit", "Kiran", "Bao", "Lek", "Pim", "Jin", "Ravi", "Kavi",
        }, new[]
        {
            "Bhatt", "Raghavan", "Chandrasekar", "Deshmukh", "Sundaram", "Iyer", "Menon", "Naidu",
            "Pillai", "Sethi", "Kapoor", "Malhotra", "Rao", "Bose", "Chatterjee", "Dalisay",
            "Trinh", "Chen", "Bhandari", "Laghari",
        }),
    };

    /// <summary>Clubhouse nicknames. They belong to no background and go with any surname.</summary>
    public static readonly string[] Nicknames =
    {
        "Boots", "Chip", "Doodle", "Gizmo", "Jitters", "Lefty", "Moose", "Noodle",
        "Peanut", "Pogo", "Rocket", "Skip", "Slugger", "Tank", "Turbo", "Waffles", "Whiz", "Zip",
    };

    /// <summary>Every given name, for code that just needs the flat list.</summary>
    public static readonly string[] First = Flatten(true);

    /// <summary>Every surname, likewise.</summary>
    public static readonly string[] Last = Flatten(false);

    private static string[] Flatten(bool first)
    {
        var list = new System.Collections.Generic.List<string>();
        foreach (var o in Origins) list.AddRange(first ? o.First : o.Last);
        if (first) list.AddRange(Nicknames);
        return list.ToArray();
    }
}
