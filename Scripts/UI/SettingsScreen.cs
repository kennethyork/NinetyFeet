using Godot;
using SandlotSlugfest.Audio;
using SandlotSlugfest.Core;
using SandlotSlugfest.Season;

namespace SandlotSlugfest.UI;

/// <summary>
/// Everything that was previously either hard-coded or hidden behind a keyboard shortcut.
///
/// Innings lived as a constant in three different files and disagreed with itself; difficulty
/// cycled on the main menu; sound could only be changed with unlabelled keys. They belong in one
/// place you can find.
/// </summary>
public partial class SettingsScreen : Control
{
    private static readonly int[] InningChoices = { 3, 6, 7, 9 };

    private static readonly (string Label, int Games)[] LengthChoices =
    {
        ("162 — a real season", Schedule.FullSeason),
        ("81 — half", Schedule.MediumSeason),
        ("33 — short", Schedule.ShortSeason),
    };

    private readonly ClickMap _clicks = new();
    private readonly Scroller _scroll = new();
    private bool _deleteLeagueArmed;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(true);
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion m) { if (_clicks.Hover(m.Position)) QueueRedraw(); return; }

        if (_clicks.Controller(@event, () => Game.Instance.GoTo("res://Scenes/MainMenu.tscn")))
        { QueueRedraw(); return; }

        if (_scroll.Wheel(@event)) { QueueRedraw(); return; }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            if (_clicks.Click(mb.Position)) QueueRedraw();
            return;
        }

        if (@event is not InputEventKey { Pressed: true } key) return;

        if (key.PhysicalKeycode is Key.Escape or Key.Backspace)
        {
            Game.Instance.GoTo("res://Scenes/MainMenu.tscn");
            return;
        }

        if (_scroll.Key(key.PhysicalKeycode)) QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);
        _clicks.Begin();

        // The rows are drawn first and the header over them afterwards, because nothing here
        // clips: a row scrolled above the top would otherwise print straight through the title.
        const float Top = 116f;
        float bottom = size.Y - 40f;

        var g = Game.Instance;
        float y = _scroll.Begin(Top, bottom);

        // --- The game itself ---
        Section("THE GAME", ref y);

        Row("Difficulty", g.Tuning.Name, g.Tuning.Blurb, ref y,
            () => g.Difficulty = DifficultyTuning.Next(g.Difficulty));

        Row("Innings per game", g.Innings.ToString(),
            g.Innings == 9 ? "Regulation." : "Shorter games finish sooner but score less.", ref y,
            () =>
            {
                int at = System.Array.IndexOf(InningChoices, g.Innings);
                g.Innings = InningChoices[(at + 1 + InningChoices.Length) % InningChoices.Length];
            });

        int lengthAt = System.Array.FindIndex(LengthChoices, c => c.Games == g.SeasonLength);
        Row("Season length", LengthChoices[Mathf.Max(0, lengthAt)].Games.ToString(),
            "Games per club in a new league. 162 is a real one.", ref y,
            () => g.SeasonLength = LengthChoices[(Mathf.Max(0, lengthAt) + 1) % LengthChoices.Length].Games);

        Row("Fielding", g.AutoFielding ? "Automatic" : "Manual",
            g.AutoFielding
                ? "Your fielders chase and throw on their own."
                : "You steer the nearest fielder and pick the throw.", ref y,
            () => { g.AutoFielding = !g.AutoFielding; Settings.SaveAutoFielding(g.AutoFielding); });

        Row("Mode", g.ManagerOnly ? "Dynasty" : "Season",
            g.ManagerOnly ? "You manage; games are simulated." : "You play your club's games.", ref y,
            () => g.ManagerOnly = !g.ManagerOnly);

        if (!OS.HasFeature("mobile"))
        {
            bool fullscreen = Settings.LoadFullscreen();
            Row("Display", fullscreen ? "Fullscreen" : "Windowed",
                "Switches immediately. Your choice is restored next time.", ref y,
                () => Settings.SaveFullscreen(!fullscreen));
        }

        // --- Your league ---
        y += 14f;
        Section("YOUR LEAGUE", ref y);

        int edited = 0;
        for (int i = 0; i < Data.Teams.All.Count; i++)
            if (Data.TeamEdits.For(i) != null) edited++;

        var ps = Settings.LoadPitching();
        Row("Pitching", ps == PitchingStyle.Meter ? "Meter" : "Classic",
            ps == PitchingStyle.Meter
                ? "Stop it at the top for power, on the mark for command."
                : "Pick the pitch, aim it, throw it.",
            ref y, () => Settings.SavePitching(
                ps == PitchingStyle.Meter ? PitchingStyle.Classic : PitchingStyle.Meter));

        var hs = Settings.LoadHitting();
        Row("Hitting", hs switch
            {
                HittingStyle.Timing => "Timing",
                HittingStyle.Directional => "Directional",
                _ => "Zone",
            },
            hs switch
            {
                HittingStyle.Timing => "No aiming. Swing at the right moment and the bat finds it.",
                HittingStyle.Directional => "Nudge up, down, in or away. A guess, not a hand on it.",
                _ => "Aim the bat anywhere. The most control, and the most to do.",
            },
            ref y, () => Settings.SaveHitting(hs switch
            {
                HittingStyle.Zone => HittingStyle.Directional,
                HittingStyle.Directional => HittingStyle.Timing,
                _ => HittingStyle.Zone,
            }));

        bool dh = Settings.UseDesignatedHitter();
        Row("Designated hitter", dh ? "On" : "Off",
            dh ? "Nine hitters; the pitcher does not bat."
               : "The pitcher bats ninth.",
            ref y, () => Settings.SaveDesignatedHitter(!dh));

        int clubs = Data.Teams.ActiveCount;
        Row("Clubs in the league", $"{clubs}",
            clubs == Data.Teams.ShippedCount
                ? "All of them. Fewer means a tighter league."
                : $"{clubs} of 32, evenly from all four divisions.  ·  new leagues only",
            ref y, () =>
            {
                var sizes = Data.Teams.Sizes;
                int at = System.Array.IndexOf(sizes, clubs);
                int next = sizes[(at < 0 ? sizes.Length - 1 : at + 1) % sizes.Length];
                Settings.SaveLeagueSize(next);
                Data.Teams.ActiveCount = next;
            });

        bool written = Settings.UseWrittenPlayers();
        Row("Written players", written ? "In the league" : "Left out",
            written
                ? "Sixteen a club, each taking a generated man's slot."
                : "Every man generated, so a roster file fills every slot.",
            ref y, () => Settings.SaveWrittenPlayers(!written));

        Row("Player names", Data.Rosters.Any ? $"{Data.Rosters.Count} supplied" : "As generated",
            Data.Rosters.Status(),
            ref y, () => Game.Instance.GoTo("res://Scenes/TeamEditor.tscn"));

        int po = Settings.PlayoffLength();
        Row("Playoffs", $"Best of {po}, then {Mathf.Min(7, po + 2)}",
            "How long October is. A seven favours the better club.",
            ref y,
            () => Settings.SavePlayoffLength(po == 3 ? 5 : po == 5 ? 7 : 3));

        Row("League", $"Slot {Season.SaveGame.Slot + 1} of {Season.SaveGame.Slots}",
            Season.SaveGame.Describe(Season.SaveGame.Slot) + "  ·  the one you leave is saved",
            ref y,
            () => Game.Instance.SwitchLeague(
                (Season.SaveGame.Slot + 1) % Season.SaveGame.Slots));

        bool hasLeague = SaveGame.Occupied(SaveGame.Slot);
        Row("Delete saved league",
            !hasLeague ? "No save in this slot" : _deleteLeagueArmed ? "TAP AGAIN TO DELETE" : "Delete",
            !hasLeague ? "Start a season or dynasty to create one."
                : _deleteLeagueArmed ? "This permanently removes the current slot."
                : "Removes it from Season and Dynasty. Tap twice so this cannot happen by accident.",
            ref y,
            () =>
            {
                if (!SaveGame.Occupied(SaveGame.Slot)) { _deleteLeagueArmed = false; return; }
                if (!_deleteLeagueArmed) { _deleteLeagueArmed = true; return; }
                SaveGame.Delete();
                _deleteLeagueArmed = false;
            });

        Row("Clubs", edited == 0 ? "As they shipped" : $"{edited} edited",
            "Rename and recolour. Nothing here changes how a club plays.",
            ref y, () => Game.Instance.GoTo("res://Scenes/TeamEditor.tscn"));

        y += 14f;
        Section("ACCESSIBILITY", ref y);
        AccessibilityRow("Large interface text", "large_text", g.LargeText,
            "Increases menu and gameplay labels.", v => g.LargeText = v, ref y);
        AccessibilityRow("High contrast", "high_contrast", g.HighContrast,
            "Uses bright text instead of muted grey.", v => g.HighContrast = v, ref y);
        AccessibilityRow("Reduced motion", "reduced_motion", g.ReducedMotion,
            "Stops decorative movement and pulsing.", v => g.ReducedMotion = v, ref y);
        AccessibilityRow("Controller vibration", "vibration", g.Vibration,
            "Physical feedback for contact and misses.", v => g.Vibration = v, ref y);

        // --- Sound ---
        y += 14f;
        Section("SOUND", ref y);

        var sfx = Sfx.Instance;
        Row("Volume", sfx == null ? "—" : $"{Mathf.RoundToInt(sfx.Volume * 100f)}%",
            "Click to step up; wraps around at the top.", ref y,
            () => sfx?.SetVolume(sfx.Volume >= 0.99f ? 0f : Mathf.Min(1f, sfx.Volume + 0.1f)));

        Row("All sound", sfx is { Muted: true } ? "Muted" : "On", "Key: M", ref y,
            () => sfx?.ToggleMute());

        Row("Commentary", Narrator.Instance is { Enabled: true } ? "On" : "Off",
            "Play-by-play and colour, one crew per ballpark. Key: N", ref y,
            () => Narrator.Instance?.SetEnabled(!(Narrator.Instance?.Enabled ?? false)));

        Row("Music", Music.Instance is { Enabled: true } ? "On" : "Off", "", ref y,
            () => Music.Instance?.SetEnabled(!(Music.Instance?.Enabled ?? false)));

        _scroll.End(y);

        // Header and footer last, over the top of anything that scrolled up behind them.
        DrawRect(new Rect2(0f, 0f, size.X, Top - 8f), Palette.Night);
        DrawRect(new Rect2(0f, bottom, size.X, size.Y - bottom), Palette.Night);

        Palette.Text(this, new Vector2(40f, 46f), "SETTINGS", 26, Palette.Ink);
        Palette.Text(this, new Vector2(40f, 70f),
            "Changes apply to your next league. Sound is immediate.",
            13, Palette.InkDim);
        Palette.BackButton(this, size, _clicks, () => Game.Instance.GoTo("res://Scenes/MainMenu.tscn"));
        _clicks.DrawFocus(this, Palette.Highlight);

        _scroll.Draw(this, Mathf.Min(872f, size.X - 32f), Top, bottom);

        Palette.Text(this, new Vector2(40f, size.Y - 14f),
            _scroll.Overflows
                ? "Controls are on their own screen  ·  scroll for more"
                : "Controls are listed on their own screen from the main menu.", 12, Palette.InkDim);
    }

    private void AccessibilityRow(string label, string key, bool value, string note,
        System.Action<bool> apply, ref float y)
    {
        Row(label, value ? "On" : "Off", note, ref y, () =>
        {
            bool next = !value;
            Settings.SaveAccessibility(key, next);
            apply(next);
            QueueRedraw();
        });
    }

    private void Section(string title, ref float y)
    {
        Palette.Text(this, new Vector2(40f, y), title, 11, Palette.Highlight);
        y += 22f;
    }

    /// <summary>One setting: a label, its current value, and a line saying what it does.</summary>
    private void Row(string label, string value, string note, ref float y, System.Action onClick)
    {
        Vector2 size = GetViewportRect().Size;
        var rect = new Rect2(new Vector2(40f, y), new Vector2(Mathf.Min(820f, size.X - 80f), 42f));
        Palette.Panel3D(this, rect, Palette.Panel);

        Palette.Text(this, rect.Position + new Vector2(16f, 26f), label, 15, Palette.Ink);
        Palette.Text(this, rect.Position + new Vector2(250f, 26f), value, 15, Palette.Highlight);

        // The note starts after the value rather than at a fixed column. A value longer than the
        // gap — "no user://rosters.txt — nobody has been renamed" is one — was printed straight
        // through its own explanation, and neither half could be read.
        if (!string.IsNullOrEmpty(note))
        {
            float noteX = Mathf.Max(400f, 250f + Palette.TextWidth(value, 15) + 18f);
            float room = rect.Size.X - noteX - 12f;
            Palette.Text(this, rect.Position + new Vector2(noteX, 26f),
                Palette.Fit(note, 11, room), 11, Palette.InkDim);
        }

        _clicks.Add(rect, onClick);
        y += 48f;
    }
}
