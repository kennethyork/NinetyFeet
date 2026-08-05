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

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            if (_clicks.Click(mb.Position)) QueueRedraw();
            return;
        }

        if (@event is InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape or Key.Backspace })
            Game.Instance.GoTo("res://Scenes/MainMenu.tscn");
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);
        _clicks.Begin();

        Palette.Text(this, new Vector2(40f, 46f), "SETTINGS", 26, Palette.Ink);
        Palette.Text(this, new Vector2(40f, 70f),
            "Changes apply to the next league you start, except sound, which is immediate.",
            13, Palette.InkDim);
        Palette.BackButton(this, size, _clicks, () => Game.Instance.GoTo("res://Scenes/MainMenu.tscn"));

        var g = Game.Instance;
        float y = 116f;

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

        // --- Your league ---
        y += 14f;
        Section("YOUR LEAGUE", ref y);

        int edited = 0;
        for (int i = 0; i < Data.Teams.All.Count; i++)
            if (Data.TeamEdits.For(i) != null) edited++;

        Row("Clubs", edited == 0 ? "As they shipped" : $"{edited} edited",
            "Rename and recolour any of the thirty-two. Nothing here changes how a club plays.",
            ref y, () => Game.Instance.GoTo("res://Scenes/TeamEditor.tscn"));

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

        Palette.Text(this, new Vector2(40f, size.Y - 28f),
            "Controls are listed on their own screen from the main menu.", 12, Palette.InkDim);
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

        if (!string.IsNullOrEmpty(note))
            Palette.Text(this, rect.Position + new Vector2(400f, 26f), note, 11, Palette.InkDim);

        _clicks.Add(rect, onClick);
        y += 48f;
    }
}
