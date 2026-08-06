using Godot;
using SandlotSlugfest.Audio;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.Season;

namespace SandlotSlugfest.UI;

public partial class MainMenu : Control
{
    private readonly string[] _items =
    {
        "Continue Saved League",
        // Two ways to run a club, named so the difference is obvious before you commit:
        // "Season" is the ball game, "Dynasty" is the management sim. Everything else — front
        // office, league office, trade desk, league browser — lives where you actually use it.
        "Season", "Dynasty", "Career", "Moments", "The Collection", "Exhibition Game",
        // Online was reachable only from the command line, by a headless self-test. All of it
        // worked and none of it was in the game.
        "Online", "Settings", "Controls", "Quit",
    };
    private int _selected;
    private float _time;

    // Two of the written players rather than random look seeds. The seeds happened to roll long
    // hair and a ponytail, which read as girls on the front of an all-male league — and using real
    // characters ties the title screen to the roster you actually play with.
    private static readonly PlayerData Batter = Legends.Make(1, 9001);    // Marcus Okafor, slugger
    private static readonly PlayerData Pitcher = Legends.Make(34, 9002);  // Dmitri Sokolov, buzz cut

    /// <summary>Where the painted sign ends, which is the ceiling the menu board must not cross.</summary>
    private static float SignBottom(Vector2 size) => size.Y * 0.235f + 71f;

    /// <summary>
    /// Vertical layout, derived from the viewport so every entry fits.
    ///
    /// This used to work from a starting position and correct downwards, with a floor of 34% of
    /// the height standing in for "below the sign". That floor was a guess, and adding a tenth
    /// entry proved it wrong at every window size — the board climbed until it was painted across
    /// the bottom half of the word FEET.
    ///
    /// Written the other way round now: the band between the bottom of the sign and the bottom of
    /// the window is measured, and the spacing is tightened until the entries fit inside it. The
    /// list can then grow without anybody having to remember to re-tune a constant.
    /// </summary>
    private (float Top, float Step) MenuLayout()
    {
        Vector2 size = GetViewportRect().Size;

        int gaps = Mathf.Max(1, _items.Length - 1);
        float step = Mathf.Clamp(size.Y * 0.058f, 20f, 42f);

        // The board is padded above the first entry and below the last by the same amount, so the
        // room the entries themselves get is the band less two paddings.
        float bandTop = SignBottom(size) + 6f;
        float bandBottom = size.Y - 40f;

        float padY = step * 0.72f;
        float available = bandBottom - bandTop - padY * 2f;

        if (gaps * step > available)
        {
            step = Mathf.Max(15f, available / gaps);
            padY = step * 0.72f;
            available = bandBottom - bandTop - padY * 2f;
        }

        // Anything left over goes above and below evenly, so a tall window centres the board in
        // the grass rather than jamming it under the sign.
        float slack = Mathf.Max(0f, available - gaps * step);
        return (bandTop + padY + slack * 0.5f, step);
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        // This screen paints and hit-tests its own links in _UnhandledInput.
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(true);
        Music.Instance?.Play(Tune.Menu);
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        QueueRedraw();
    }

    /// <summary>Screen rectangle of a menu entry, used for both hover and clicking.</summary>
    private Rect2 ItemRect(int index)
    {
        Vector2 size = GetViewportRect().Size;
        var (top, step) = MenuLayout();
        float y = top + index * step;
        return new Rect2(new Vector2(size.X * 0.5f - 210f, y - step * 0.5f), new Vector2(420f, step));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // The menu is clickable — hovering moves the selection, a click activates it.
        if (@event is InputEventMouseMotion motion)
        {
            for (int i = 0; i < _items.Length; i++)
                if (ItemRect(i).HasPoint(motion.Position))
                {
                    if (_selected != i) Sfx.Instance?.Play(Sound.UiMove, 0.5f);
                    _selected = i;
                    QueueRedraw();
                    break;
                }
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            for (int i = 0; i < _items.Length; i++)
                if (ItemRect(i).HasPoint(click.Position))
                {
                    _selected = i;
                    Sfx.Instance?.Play(Sound.UiSelect, 0.6f);
                    Activate();
                    return;
                }
            return;
        }

        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        switch (key.PhysicalKeycode)
        {
            case Key.Up or Key.W:
                _selected = (_selected - 1 + _items.Length) % _items.Length;
                Sfx.Instance?.Play(Sound.UiMove, 0.5f);
                break;
            case Key.Down or Key.S:
                _selected = (_selected + 1) % _items.Length;
                Sfx.Instance?.Play(Sound.UiMove, 0.5f);
                break;
            case Key.Enter or Key.KpEnter or Key.Space:
                Sfx.Instance?.Play(Sound.UiSelect, 0.6f);
                Activate();
                break;
            case Key.Escape:
                GetTree().Quit();
                break;
        }
        QueueRedraw();
    }

    private void Activate()
    {
        switch (_selected)
        {
            case 0:
                // The league was loaded by the Game autoload before this menu appeared. It was
                // previously impossible to reach: both league buttons went through ClubSelect,
                // whose confirmation deliberately starts a new season and overwrites the slot.
                if (!SaveGame.Occupied(SaveGame.Slot) || Game.Instance.League == null) return;
                Game.Instance.PendingSeasonGame = null;
                Game.Instance.CardClubRoster = null;
                Game.Instance.HomeTeamId = Game.Instance.League.UserTeamId;
                Game.Instance.GoTo("res://Scenes/Season.tscn");
                break;
            case 1:
                // Season: you play your club's games.
                Game.Instance.PendingSeasonGame = null;
                Game.Instance.ManagerOnly = false;
                Game.Instance.GoTo("res://Scenes/ClubSelect.tscn");
                break;
            case 2:
                // Dynasty: you run the club for years and never take the field.
                Game.Instance.PendingSeasonGame = null;
                Game.Instance.ManagerOnly = true;
                Game.Instance.GoTo("res://Scenes/ClubSelect.tscn");
                break;
            case 3:
                // One player, from the bottom of somebody's farm system to wherever he gets to.
                Game.Instance.PendingSeasonGame = null;
                Game.Instance.CardClubRoster = null;
                Game.Instance.GoTo("res://Scenes/Career.tscn");
                break;
            case 4:
                // One situation, ninety seconds.
                Game.Instance.PendingSeasonGame = null;
                Game.Instance.CardClubRoster = null;
                Game.Instance.GoTo("res://Scenes/Moments.tscn");
                break;
            case 5:
                // Collect players, build a side out of them, take it out.
                Game.Instance.PendingSeasonGame = null;
                Game.Instance.CardClubRoster = null;
                Game.Instance.GoTo("res://Scenes/Cards.tscn");
                break;
            case 6:
                Game.Instance.PendingSeasonGame = null;
                Game.Instance.CardClubRoster = null;
                Game.Instance.GoTo("res://Scenes/TeamSelect.tscn");
                break;
            case 7:
                // Host or join: one ballgame, or a whole season the two of you share.
                Game.Instance.PendingSeasonGame = null;
                Game.Instance.CardClubRoster = null;
                Game.Instance.GoTo("res://Scenes/Online.tscn");
                break;
            case 8:
                Game.Instance.GoTo("res://Scenes/Settings.tscn");
                break;
            case 9:
                Game.Instance.GoTo("res://Scenes/Controls.tscn");
                break;
            case 10:
                GetTree().Quit();
                break;
        }
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        float cx = size.X * 0.5f;
        float horizon = size.Y * 0.52f;

        DrawBackdrop(size, horizon);
        DrawKids(size, horizon);
        DrawSign(cx, size.Y * 0.235f, size.X);
        DrawMenu(size, cx);
    }

    /// <summary>Late-afternoon sky, the neighbourhood beyond the fence, and the sandlot itself.</summary>
    private void DrawBackdrop(Vector2 size, float horizon)
    {
        Scenery.SkyGradient(this, new Rect2(Vector2.Zero, new Vector2(size.X, horizon + 4f)),
            new Color("#4f8fc4"), new Color("#cfe6f2"));
        Scenery.Sun(this, new Vector2(size.X * 0.82f, size.Y * 0.15f), 26f);
        Scenery.Clouds(this, new Rect2(0f, size.Y * 0.05f, size.X, size.Y * 0.26f), 7, _time * 5f);
        Scenery.Neighbourhood(this, size.X, horizon, 12);

        // The grass, with mown stripes fanning out from home plate rather than straight bands —
        // parallel rectangles read as a bar chart, not a ball field.
        DrawRect(new Rect2(new Vector2(0f, horizon), new Vector2(size.X, size.Y - horizon)), Palette.GrassDark);
        var apex = new Vector2(size.X * 0.5f, size.Y * 1.45f);
        for (int i = 0; i < 9; i++)
        {
            if (i % 2 == 1) continue;
            float a0 = Mathf.Pi + Mathf.Pi * (i / 9f);
            float a1 = Mathf.Pi + Mathf.Pi * ((i + 1) / 9f);
            DrawColoredPolygon(new[]
            {
                apex,
                apex + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * size.Y * 2.2f,
                apex + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * size.Y * 2.2f,
            }, Palette.Grass);
        }

        // Re-cover the sky, since those wedges are drawn oversized to reach the corners.
        DrawRect(new Rect2(Vector2.Zero, new Vector2(size.X, horizon)), new Color(0, 0, 0, 0));
        Scenery.SkyGradient(this, new Rect2(Vector2.Zero, new Vector2(size.X, horizon)),
            new Color("#4f8fc4"), new Color("#cfe6f2"));
        Scenery.Sun(this, new Vector2(size.X * 0.82f, size.Y * 0.15f), 26f);
        Scenery.Clouds(this, new Rect2(0f, size.Y * 0.05f, size.X, size.Y * 0.26f), 7, _time * 5f);
        Scenery.Neighbourhood(this, size.X, horizon, 12);

        // A scuffed dirt infield arc across the foreground.
        var dirt = new Vector2[26];
        for (int i = 0; i < 24; i++)
        {
            float t = i / 23f;
            dirt[i] = new Vector2(t * size.X,
                size.Y * 0.80f - Mathf.Sin(t * Mathf.Pi) * size.Y * 0.10f);
        }
        dirt[24] = new Vector2(size.X, size.Y);
        dirt[25] = new Vector2(0f, size.Y);
        DrawColoredPolygon(dirt, Palette.Dirt);

        // Home plate, down where the player is standing.
        var plate = new Vector2(size.X * 0.235f, size.Y * 0.945f);
        Ink.Shape(this, new[]
        {
            plate + new Vector2(-26f, -8f), plate + new Vector2(26f, -8f),
            plate + new Vector2(30f, 4f), plate + new Vector2(0f, 13f), plate + new Vector2(-30f, 4f),
        }, Palette.Chalk, new Color("#c9cec4"), 2f, 91);
    }

    /// <summary>Two kids hanging around the plate, because a title screen with nobody on it is a wallpaper.</summary>
    private void DrawKids(Vector2 size, float horizon)
    {
        var teams = Teams.All;
        var home = teams[(int)(_time * 0.18f) % teams.Count];
        var away = teams[((int)(_time * 0.18f) + 13) % teams.Count];

        // The pitcher is further away, so he is drawn smaller and first.
        CartoonPlayer.Draw(this, new Vector2(size.X * 0.74f, horizon + size.Y * 0.115f),
            0.78f, -1f, Pose.Windup, away, Pitcher, _time);

        CartoonPlayer.Draw(this, new Vector2(size.X * 0.235f, size.Y * 0.93f),
            1.32f, 1f, Pose.Stance, home, Batter, _time, withBat: true);
    }

    /// <summary>A hand-painted plywood sign, the way a sandlot would actually be labelled.</summary>
    private void DrawSign(float cx, float cy, float width)
    {
        float w = Mathf.Min(width * 0.62f, 560f);
        float h = 132f;
        var board = new Vector2(cx, cy);

        // Two posts behind the board.
        foreach (float side in new[] { -1f, 1f })
            Ink.Shape(this, Ink.Capsule(
                board + new Vector2(side * w * 0.34f, -h * 0.35f),
                board + new Vector2(side * w * 0.34f, h * 0.95f), 7f),
                new Color("#8a6236"), new Color("#5d4022"), 2.4f, 300 + (int)side);

        // The board, tilted very slightly so it does not look machine-placed.
        var corners = new[]
        {
            board + new Vector2(-w * 0.5f, -h * 0.5f - 4f),
            board + new Vector2(w * 0.5f, -h * 0.5f + 3f),
            board + new Vector2(w * 0.5f, h * 0.5f + 5f),
            board + new Vector2(-w * 0.5f, h * 0.5f - 2f),
        };
        Ink.Shape(this, corners, new Color("#f0e2c4"), new Color("#6b4a26"), 4f, 77);

        // Plank seams.
        for (int i = 1; i < 3; i++)
        {
            float y = board.Y - h * 0.5f + h * (i / 3f);
            Ink.Line(this, new Vector2(board.X - w * 0.47f, y), new Vector2(board.X + w * 0.47f, y),
                new Color("#d8c6a2"), 2f, 400 + i, 1.2f);
        }

        Palette.TextCentered(this, new Vector2(cx, cy - 14f), "NINETY", 60, new Color("#20303f"));
        Palette.TextCentered(this, new Vector2(cx, cy + 42f), "FEET", 60, new Color("#d4552f"));

        // A ball wedged in the top corner of the sign.
        var ball = new Vector2(cx + w * 0.44f, cy - h * 0.46f);
        DrawCircle(ball, 15f, Palette.Ball);
        DrawArc(ball, 15f, 0f, Mathf.Tau, 20, new Color("#c9c2ad"), 2f);
        DrawArc(ball + new Vector2(-6f, 0f), 11f, -1.0f, 1.0f, 12, Palette.Warning, 2f);
        DrawArc(ball + new Vector2(6f, 0f), 11f, Mathf.Pi - 1.0f, Mathf.Pi + 1.0f, 12, Palette.Warning, 2f);
    }

    private void DrawMenu(Vector2 size, float cx)
    {
        var (top, step) = MenuLayout();

        // A second painted board rather than a flat dark slab — a modern UI panel dropped on a
        // sandlot looks like a dialog box someone forgot to remove.
        float padY = step * 0.72f;
        float halfW = 196f;
        float y0 = top - padY, y1 = top + (_items.Length - 1) * step + padY;
        Ink.Shape(this, new[]
        {
            new Vector2(cx - halfW, y0 - 3f), new Vector2(cx + halfW, y0 + 2f),
            new Vector2(cx + halfW + 3f, y1 + 3f), new Vector2(cx - halfW - 2f, y1),
        }, new Color("#f4e8ce", 0.94f), new Color("#6b4a26"), 3.5f, 55);

        for (int i = 0; i < _items.Length; i++)
        {
            bool on = i == _selected;
            float y = top + i * step;
            int fontSize = (int)Mathf.Clamp(step * 0.72f, 20f, 30f);
            var color = on ? new Color("#d4552f") : new Color("#2c3b4a");

            if (on)
            {
                float bob = Mathf.Sin(_time * 4f) * 3f;
                float w = Palette.TextWidth(_items[i], fontSize);
                var at = new Vector2(cx - w * 0.5f - 26f, y - fontSize * 0.30f + bob);
                DrawCircle(at, 8f, Palette.Ball);
                DrawArc(at, 8f, 0f, Mathf.Tau, 14, new Color("#c9c2ad"), 1.4f);
                DrawArc(at + new Vector2(-3f, 0f), 6f, -1.0f, 1.0f, 8, Palette.Warning, 1.4f);
                DrawArc(at + new Vector2(3f, 0f), 6f, Mathf.Pi - 1.0f, Mathf.Pi + 1.0f, 8, Palette.Warning, 1.4f);
            }

            Palette.TextCentered(this, new Vector2(cx, y), _items[i], fontSize, color);
        }

        // On grass, with a soft drop shadow — plain white on cream was unreadable where the
        // hint ran under the board.
        Palette.TextCentered(this, new Vector2(cx + 1f, size.Y - 19f),
            "Click an option, or use the arrow keys and Enter", 16, new Color(0f, 0f, 0f, 0.45f));
        Palette.TextCentered(this, new Vector2(cx, size.Y - 20f),
            "Click an option, or use the arrow keys and Enter", 16, new Color("#f7f2e4"));
    }
}
