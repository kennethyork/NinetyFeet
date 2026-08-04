using Godot;
using SandlotSlugfest.Core;

namespace SandlotSlugfest.UI;

public partial class ControlsScreen : Control
{
    private static readonly (string Section, string Key, string What)[] Rows =
    {
        ("AT THE PLATE", "Move the mouse", "Aim the hitting reticle"),
        ("", "Left click / Space", "Normal swing — balanced"),
        ("", "Right click / F", "Power swing — smaller barrel, more damage"),
        ("", "Middle click / C", "Contact swing — bigger barrel, less power"),
        ("", "B", "Bunt"),
        ("", "Arrow keys / WASD", "Aim without a mouse"),
        ("", "Q / E", "Hold runners / send runners"),
        ("", "R", "Challenge the call (2026 automated ball-strike rule)"),
        ("", "Click the diamond", "Throw over — two per hitter, a third is a balk"),
        ("", "(be ready)", "Set in the box with 8 seconds left or it is a strike"),

        ("ON THE MOUND", "1 2 3 4", "Fastball, curveball, changeup, slider"),
        ("", "Move the mouse", "Aim the pitch"),
        ("", "Left click / Space", "Deal"),

        ("IN THE FIELD", "1 2 3 4", "Throw to first, second, third, home"),
        ("", "(wait)", "The fielder makes the play himself"),

        ("ANYWHERE", "Esc", "Back out to the menu"),
        ("", "M", "Mute all sound"),
        ("", "N", "Turn the commentary booth on or off"),
        ("", "- / =", "Volume down / up"),
    };

    public override void _Ready() => SetAnchorsPreset(LayoutPreset.FullRect);

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true } key &&
            key.PhysicalKeycode is Key.Escape or Key.Enter or Key.Space or Key.Backspace)
            Game.Instance.GoTo("res://Scenes/MainMenu.tscn");
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);

        float x = size.X * 0.5f - 300f;
        float y = 90f;

        Palette.Text(this, new Vector2(x, y), "CONTROLS", 30, Palette.Ink);
        y += 48f;

        foreach (var (section, key, what) in Rows)
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

        Palette.Text(this, new Vector2(x, size.Y - 50f), "Press Esc or Space to go back", 15, Palette.InkDim);
    }
}
