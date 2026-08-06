using System.Collections.Generic;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.UI;

namespace SandlotSlugfest.Gameplay;

/// <summary>
/// The game, played with a thumb.
///
/// Twenty-nine actions in this game are keys and there was not one touch handler anywhere, so on a
/// phone you could browse a roster and never throw a pitch. Godot turns a tap into a mouse click by
/// default, which carries the menus and nothing else: selecting a pitch is 1 to 4, bunting is B,
/// and stealing, the bullpen, the mound visit and the defensive alignment are all keys with no
/// on-screen equivalent.
///
/// Rather than special-case twenty-five call sites, this feeds the input system itself. A tap on a
/// button posts an InputEventAction, so <c>Input.IsActionJustPressed</c> answers exactly as it does
/// for a key and every reader in the game works untouched — including ones written years before
/// anybody thought about a phone.
///
/// The buttons change with the situation, because a control pad showing everything at once is how a
/// phone game ends up with nineteen tiny targets and no room for the ballfield. At the plate there
/// is a swing and the two ways to shade it; on the mound there is his own arsenal and nothing else.
/// </summary>
public static class TouchControls
{
    /// <summary>
    /// On when the device has a touchscreen. Forced on by <c>--touch</c> so the pad can be looked
    /// at, and posed for, without a phone in the room.
    /// </summary>
    public static bool Enabled;

    public static void Detect(string[] args)
    {
        Enabled = DisplayServer.IsTouchscreenAvailable()
               || System.Array.IndexOf(args, "--touch") >= 0;
    }

    /// <summary>A button on the pad: where it is, what it says, and what it presses.</summary>
    private readonly record struct Pad(Rect2 Where, string Label, string Action, bool Big, bool Lit);

    private static readonly List<Pad> Buttons = new();

    /// <summary>Which finger is aiming, so a thumb on a button never drags the bat as well.</summary>
    private static int _aimFinger = -1;
    private static Vector2 _aimFrom;
    private static Vector2 _aimStart;

    /// <summary>Actions posted this frame, released on the next one.</summary>
    private static readonly List<string> Held = new();

    // -----------------------------------------------------------------------
    // What is on the pad right now
    // -----------------------------------------------------------------------

    private static void Build(GameScene scene, Vector2 size)
    {
        Buttons.Clear();
        if (scene?.Situation == null) return;

        float m = 22f;                      // margin from the edge
        float r = 78f;                      // a comfortable thumb target
        float small = 62f;

        // HumanBatting and HumanPitching are already worked out per half inning and are never both
        // true, so the pad simply follows whichever side of the ball he is on.
        if (scene.HumanPitching)
        {
            // --- On the mound: his own repertoire, and nothing he cannot throw. ---
            var arm = scene.Situation.FieldingTeam.CurrentPitcher;
            var arsenal = arm == null ? System.Array.Empty<PitchType>()
                                      : new List<PitchType>(arm.Arsenal).ToArray();

            string[] actions =
            {
                InputActions.Pitch1, InputActions.Pitch2, InputActions.Pitch3, InputActions.Pitch4,
            };

            for (int i = 0; i < arsenal.Length && i < 4; i++)
                Buttons.Add(new Pad(
                    new Rect2(m + i * (small + 10f), size.Y - m - small, small, small),
                    SwingProfileNames.Short(arsenal[i]), actions[i], false,
                    scene.SelectedPitch == arsenal[i]));

            Buttons.Add(new Pad(new Rect2(size.X - m - r, size.Y - m - r, r, r),
                "DEAL", InputActions.Action, true, false));

            Buttons.Add(new Pad(new Rect2(size.X - m - r - small - 10f, size.Y - m - small, small, small),
                "PEN", InputActions.ChangePitcher, false, false));
            return;
        }

        // --- At the plate. ---
        Buttons.Add(new Pad(new Rect2(size.X - m - r, size.Y - m - r, r, r),
            "SWING", InputActions.Action, true, false));

        Buttons.Add(new Pad(new Rect2(size.X - m - r - small - 10f, size.Y - m - small, small, small),
            "POW", InputActions.SwingPower, false, false));

        Buttons.Add(new Pad(new Rect2(size.X - m - r - (small + 10f) * 2f, size.Y - m - small, small, small),
            "CON", InputActions.SwingContact, false, false));

        Buttons.Add(new Pad(new Rect2(m, size.Y - m - small, small, small),
            "BUNT", InputActions.Bunt, false, false));

        Buttons.Add(new Pad(new Rect2(m + small + 10f, size.Y - m - small, small, small),
            "GO", InputActions.Steal, false, false));
    }

    // -----------------------------------------------------------------------
    // Touch
    // -----------------------------------------------------------------------

    /// <summary>Returns true if the pad consumed the event.</summary>
    public static bool Handle(InputEvent e, GameScene scene, Vector2 size)
    {
        if (!Enabled || scene == null) return false;
        Build(scene, size);

        switch (e)
        {
            case InputEventScreenTouch touch:
                if (touch.Pressed)
                {
                    foreach (var b in Buttons)
                        if (b.Where.HasPoint(touch.Position)) { Press(b.Action); return true; }

                    // Not on a button, so this finger is the one aiming.
                    _aimFinger = touch.Index;
                    _aimFrom = touch.Position;
                    _aimStart = scene.BatCursor;
                    return true;
                }

                if (touch.Index == _aimFinger) _aimFinger = -1;
                return true;

            case InputEventScreenDrag drag when drag.Index == _aimFinger:
                Aim(scene, drag.Position);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Moves the bat from where the finger started rather than to where it is.
    ///
    /// Aiming absolutely would put the target underneath the thumb, which is the one place on a
    /// phone you cannot see. Relative dragging keeps the hand out of the strike zone, and it means
    /// the reach of the gesture is set by the screen rather than by where the finger landed.
    /// </summary>
    private static void Aim(GameScene scene, Vector2 to)
    {
        const float FeetPerPixel = 1f / 104f;     // matches BattingView.PixelsPerFoot
        var moved = (to - _aimFrom) * FeetPerPixel;

        scene.BatCursor = new Vector2(
            Mathf.Clamp(_aimStart.X + moved.X, -2.2f, 2.2f),
            Mathf.Clamp(_aimStart.Y - moved.Y, 0.4f, 5.0f));
    }

    private static void Press(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        Held.Add(action);
    }

    /// <summary>
    /// Releases last frame's presses. Without this a tap is held down for ever and the game reads
    /// one touch of the swing button as a bat that never comes back.
    /// </summary>
    public static void Release()
    {
        if (Held.Count == 0) return;

        foreach (string action in Held)
            Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });

        Held.Clear();
    }

    // -----------------------------------------------------------------------
    // Drawing
    // -----------------------------------------------------------------------

    public static void Draw(CanvasItem c, GameScene scene, Vector2 size)
    {
        if (!Enabled || scene?.Situation == null) return;
        Build(scene, size);

        foreach (var b in Buttons)
        {
            var fill = b.Lit ? Palette.Highlight
                     : b.Big ? new Color(0.86f, 0.69f, 0.29f, 0.80f)
                             : new Color(1f, 1f, 1f, 0.16f);

            c.DrawCircle(b.Where.Position + b.Where.Size * 0.5f, b.Where.Size.X * 0.5f, fill);
            c.DrawArc(b.Where.Position + b.Where.Size * 0.5f, b.Where.Size.X * 0.5f,
                0f, Mathf.Tau, 28, new Color(0f, 0f, 0f, 0.45f), 3f);

            Palette.TextCentered(c, b.Where.Position + b.Where.Size * 0.5f, b.Label,
                b.Big ? 19 : 15, b.Big || b.Lit ? new Color(0.09f, 0.12f, 0.16f) : Palette.Ink);
        }

        // A hint at the gesture, only while nobody is using it, and clear of the ballpark strip
        // that already occupies the bottom centre.
        if (_aimFinger < 0 && scene.HumanBatting)
            Palette.TextCentered(c, new Vector2(size.X * 0.5f, size.Y - 112f),
                "drag anywhere to aim", 13, new Color(1f, 1f, 1f, 0.42f));
    }
}
