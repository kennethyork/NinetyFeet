using Godot;
using SandlotSlugfest.Core;

namespace SandlotSlugfest.UI;

public partial class ControlsScreen : Control
{
    private static readonly (string Section, string Key, string What)[] Rows =
    {
        ("AT THE PLATE", "Mouse or left stick", "Aim the hitting reticle — either, your choice"),
        ("", "Left click / Space / A", "Normal swing — balanced"),
        ("", "Right click / F / Y", "Power swing — smaller barrel, more damage"),
        ("", "Middle click / C / X", "Contact swing — bigger barrel, less power"),
        ("", "B / left bumper", "Bunt"),
        ("", "WASD", "Aim without a mouse"),
        ("", "(either device)", "Whichever you touch takes the reticle and keeps it"),
        ("", "R", "Challenge the call (2026 automated ball-strike rule)"),
        ("", "Click the diamond", "Throw over — two per hitter, a third is a balk"),
        ("", "(be ready)", "Set in the box with 8 seconds left or it is a strike"),

        ("ON THE MOUND", "1 2 3 4 / A B X Y", "This pitcher's own repertoire, in order"),
        ("", "Mouse or left stick", "Aim the pitch"),
        ("", "Left click / Space / A", "Deal"),
        ("", "P", "Go to the pen — the manager walks out and takes the ball"),
        ("", "V", "Mound visit — settle him. Five a game; the sixth is a change"),
        ("", "I", "Put him on intentionally"),
        ("", "U", "Get somebody up — press again to walk down the pen"),
        ("", "(about an inning)", "Bring him in cold and he cannot find it for ten pitches"),

        ("MANAGING — THE ARROW KEYS", "Left / G", "STEAL — send the runner"),
        ("", "Right / H", "Pinch hit — only before the first pitch of an at-bat"),
        ("", "Up / E", "Send the runners on a ball in play"),
        ("", "Down / Q", "Hold them at the bag"),

        ("IN THE FIELD", "1 2 3 4", "Throw to first, second, third, home"),
        ("", "(wait)", "The fielder makes the play himself"),
        ("", "Y / back button", "Move the defence: DP depth, in, no doubles, shift"),
        ("", "(it is real)", "They stand where you put them; nothing else changes"),

        ("ANYWHERE", "Esc", "Back out to the menu"),
        ("", "M", "Mute all sound"),
        ("", "N", "Turn the commentary booth on or off"),
        ("", "- / =", "Volume down / up"),
    };

    private static readonly (string Section, string Key, string What)[] MobileRows =
    {
        ("AT THE PLATE", "Left aim pad", "Drag to move the hitting reticle"),
        ("", "SWING", "Normal swing — balanced"),
        ("", "POW / CON", "Trade contact for damage, or damage for contact"),
        ("", "BUNT", "Square around before the pitch arrives"),
        ("", "GO", "Send a runner"),

        ("ON THE MOUND", "Left aim pad", "Drag to locate the pitch"),
        ("", "Pitch buttons", "Choose from this pitcher's actual repertoire"),
        ("", "DEAL", "Start or complete the delivery"),
        ("", "PEN", "Open the bullpen"),

        ("BALL IN PLAY", "GO / HOLD", "Advance or stop the runners"),
        ("", "Base diamond", "Throw spatially to first, second, third or home"),
        ("", "Open grass", "Tap or drag to steer with manual fielding"),

        ("MENUS", "Tap", "Activate a row or button when your finger lifts"),
        ("", "Swipe", "Scroll rosters, settings and league screens"),
        ("", "Android Back", "Close the top layer, leave a screen or pause play"),
        ("", "Settings", "Adjust size, opacity, sensitivity, handedness and zoom"),
    };

    private readonly ClickMap _clicks = new();
    private readonly Scroller _scroll = new();

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        // Without this the Control swallows every mouse event and the back button is a picture.
        MouseFilter = MouseFilterEnum.Ignore;

        TouchScroll.Handler = (px, _) => { _scroll.By(px); QueueRedraw(); };
    }

    public override void _ExitTree() => TouchScroll.Handler = null;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_scroll.Wheel(@event)) { QueueRedraw(); return; }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            if (_clicks.Click(mb.Position)) QueueRedraw();
            return;
        }

        if (!ControllerNav.TryPressedKey(@event, out Key pressed)) return;

        if (pressed is Key.Escape or Key.Enter or Key.Space or Key.Backspace)
        {
            Game.Instance.GoTo("res://Scenes/MainMenu.tscn");
            return;
        }

        if (_scroll.Key(pressed)) QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);
        _clicks.Begin();

        float x = size.X * 0.5f - 300f;

        // Twenty-odd rows at thirty pixels apiece is taller than a short window, and the ones
        // past the bottom were simply unreachable — on the one screen whose entire job is telling
        // you which key does what.
        const float Top = 138f;
        float bottom = size.Y - 56f;
        float y = _scroll.Begin(Top, bottom);

        var shownRows = Gameplay.TouchControls.MobileLayout ? MobileRows : Rows;
        foreach (var (section, key, what) in shownRows)
        {
            if (!string.IsNullOrEmpty(section))
            {
                y += 14f;
                Palette.Text(this, new Vector2(x, y), section, 15, Palette.Highlight);
                y += 26f;
            }

            var box = new Rect2(new Vector2(x, y - 16f), new Vector2(190f, 24f));
            Palette.Panel3D(this, box, Palette.PanelLight);
            Palette.Text(this, new Vector2(x + 12f, y + 1f), key, 14, Palette.Ink);
            Palette.Text(this, new Vector2(x + 210f, y + 1f), what, 15, Palette.InkDim);
            y += 30f;
        }

        _scroll.End(y);

        // Header and footer over the top, since nothing here clips.
        DrawRect(new Rect2(0f, 0f, size.X, Top - 10f), Palette.Night);
        DrawRect(new Rect2(0f, bottom, size.X, size.Y - bottom), Palette.Night);

        Palette.Text(this, new Vector2(x, 108f), "CONTROLS", 30, Palette.Ink);
        Palette.BackButton(this, size, _clicks, () => Game.Instance.GoTo("res://Scenes/MainMenu.tscn"));
        _scroll.Draw(this, x + 600f, Top, bottom);

        Palette.Text(this, new Vector2(x, size.Y - 26f),
            Gameplay.TouchControls.MobileLayout
                ? (_scroll.Overflows ? "Swipe for the rest  ·  Android Back to leave"
                                     : "Android Back to leave")
                : (_scroll.Overflows ? "Scroll for the rest  ·  Esc or Space to go back"
                                     : "Press Esc or Space to go back"), 15, Palette.InkDim);
    }
}
