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

    /// <summary>
    /// True when phone-sized presentation should be used. A touchscreen laptop still wants the
    /// desktop camera; <c>--touch</c> opts into the mobile layout for development captures.
    /// </summary>
    public static bool MobileLayout;

    public static void Detect(string[] args)
    {
        bool preview = System.Array.IndexOf(args, "--touch") >= 0;
        Enabled = DisplayServer.IsTouchscreenAvailable()
               || OS.HasFeature("mobile")
               || preview;
        MobileLayout = OS.HasFeature("mobile") || preview;
    }

    /// <summary>A button on the pad: where it is, what it says, and what it presses.</summary>
    private readonly record struct Pad(Rect2 Where, string Label, string Action, bool Big, bool Lit);

    private static readonly List<Pad> Buttons = new();

    /// <summary>Which finger is aiming, so a thumb on a button never drags the bat as well.</summary>
    private static int _aimFinger = -1;
    private static Vector2 _aimFrom;
    private static Vector2 _aimStart;
    private static Vector2 _aimAt;

    /// <summary>Actions posted this frame, released on the next one.</summary>
    private static readonly List<string> Held = new();
    private static readonly Dictionary<string, ulong> FlashUntil = new();

    // -----------------------------------------------------------------------
    // What is on the pad right now
    // -----------------------------------------------------------------------

    private static void Build(GameScene scene, Vector2 size)
    {
        Buttons.Clear();
        if (scene?.Situation == null) return;

        Vector4 safe = SafeInsets(size);
        float left = MobileLayout ? Mathf.Max(28f, safe.X + 16f) : 22f;
        float top = MobileLayout ? Mathf.Max(28f, safe.Y + 16f) : 22f;
        float right = MobileLayout ? Mathf.Max(28f, safe.Z + 16f) : 22f;
        float bottom = MobileLayout ? Mathf.Max(28f, safe.W + 16f) : 22f;
        float r = MobileLayout ? 104f : 78f; // primary action belongs under a thumb
        float small = MobileLayout ? 68f : 62f;
        float primaryX = size.X - right - r;
        float primaryY = size.Y - bottom - r;
        float lowerY = size.Y - bottom - small;

        Buttons.Add(new Pad(new Rect2(size.X - right - 48f, top, 48f, 48f),
            "II", InputActions.Pause, false, false));

        if (scene.Phase == AtBatPhase.InPlay)
        {
            if (scene.HumanPitching && !Game.Instance.AutoFielding)
            {
                // Throws live where the bases live. A spatial diamond is much faster to read in
                // the half-second after contact than four text buttons in an arbitrary row.
                float d = small + 8f;
                Vector2 c = new(size.X - right - small - d, size.Y - bottom - small - d);
                Buttons.Add(new Pad(new Rect2(c.X, c.Y - d, small, small), "2", InputActions.ThrowSecond, false, false));
                Buttons.Add(new Pad(new Rect2(c.X + d, c.Y, small, small), "1", InputActions.ThrowFirst, false, false));
                Buttons.Add(new Pad(new Rect2(c.X, c.Y + d, small, small), "H", InputActions.ThrowHome, true, false));
                Buttons.Add(new Pad(new Rect2(c.X - d, c.Y, small, small), "3", InputActions.ThrowThird, false, false));
            }
            else if (scene.HumanBatting)
            {
                Buttons.Add(new Pad(new Rect2(primaryX, primaryY, r, r),
                    "GO", InputActions.SendRunners, true, false));
                Buttons.Add(new Pad(new Rect2(primaryX - small - 12f, lowerY,
                    small, small), "HOLD", InputActions.HoldRunners, false, false));
            }
            return;
        }

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

            float pitchesX = left + (MobileLayout ? 196f : 0f);
            for (int i = 0; i < arsenal.Length && i < 4; i++)
                Buttons.Add(new Pad(
                    new Rect2(pitchesX + i * (small + 10f), lowerY, small, small),
                    SwingProfileNames.Short(arsenal[i]), actions[i], false,
                    scene.SelectedPitch == arsenal[i]));

            Buttons.Add(new Pad(new Rect2(primaryX, primaryY, r, r),
                "DEAL", InputActions.Action, true, false));

            Buttons.Add(new Pad(new Rect2(primaryX - small - 12f, lowerY - small - 10f, small, small),
                "PEN", InputActions.ChangePitcher, false, false));
            return;
        }

        // --- At the plate. ---
        Buttons.Add(new Pad(new Rect2(primaryX, primaryY, r, r),
            "SWING", InputActions.Action, true, false));

        Buttons.Add(new Pad(new Rect2(primaryX - small - 12f, lowerY - small - 10f, small, small),
            "POW", InputActions.SwingPower, false, false));

        Buttons.Add(new Pad(new Rect2(primaryX - small - 12f, lowerY, small, small),
            "CON", InputActions.SwingContact, false, false));

        float utilityX = left + (MobileLayout ? 196f : 0f);
        Buttons.Add(new Pad(new Rect2(utilityX, lowerY - small - 10f, small, small),
            "BUNT", InputActions.Bunt, false, false));

        Buttons.Add(new Pad(new Rect2(utilityX, lowerY, small, small),
            "GO", InputActions.Steal, false, false));
    }

    /// <summary>
    /// Android cutout and gesture insets converted from display pixels to canvas units.
    /// Components are left, top, right and bottom. They must remain separate: a camera notch on
    /// the left edge should not lift the entire control pad away from the bottom edge.
    /// </summary>
    public static Vector4 SafeInsets(Vector2 viewport)
    {
        if (!MobileLayout) return Vector4.Zero;
        Vector2I screen = DisplayServer.ScreenGetSize();
        Rect2I safe = DisplayServer.GetDisplaySafeArea();
        if (screen.X <= 0 || screen.Y <= 0 || safe.Size.X <= 0 || safe.Size.Y <= 0)
            return Vector4.Zero;

        float sx = viewport.X / screen.X;
        float sy = viewport.Y / screen.Y;
        int right = screen.X - safe.End.X;
        int bottom = screen.Y - safe.End.Y;
        return new Vector4(safe.Position.X * sx, safe.Position.Y * sy, right * sx, bottom * sy);
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
                    _aimAt = touch.Position;
                    _aimStart = scene.HumanPitching ? scene.PitchAim : scene.BatCursor;
                    if (scene.Phase == AtBatPhase.InPlay && scene.HumanPitching)
                        scene.SetTouchFieldTarget(touch.Position);
                    return true;
                }

                if (touch.Index == _aimFinger) _aimFinger = -1;
                return true;

            case InputEventScreenDrag drag when drag.Index == _aimFinger:
                _aimAt = drag.Position;
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
        if (scene.Phase == AtBatPhase.InPlay && scene.HumanPitching)
        {
            scene.SetTouchFieldTarget(to);
            return;
        }

        const float FeetPerPixel = 1f / 104f;     // matches BattingView.PixelsPerFoot
        var moved = (to - _aimFrom) * FeetPerPixel;

        scene.SetTouchAim(new Vector2(_aimStart.X + moved.X, _aimStart.Y - moved.Y));
    }

    private static void Press(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        Held.Add(action);
        FlashUntil[action] = Time.GetTicksMsec() + 140;
        if (Game.Instance?.Vibration == true)
            Input.VibrateHandheld(action == InputActions.Action ? 24 : 12);
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

        // Give the left thumb an obvious home. Aiming still accepts a drag anywhere that is not a
        // button, so players can start a gesture without pixel hunting; the pad teaches the control
        // and provides live feedback without putting a reticle underneath the player's thumb.
        if (MobileLayout && scene.Phase != AtBatPhase.InPlay &&
            (scene.HumanBatting || scene.HumanPitching))
        {
            Vector4 safe = SafeInsets(size);
            float left = Mathf.Max(28f, safe.X + 16f);
            float bottom = Mathf.Max(28f, safe.W + 16f);
            Vector2 center = new(left + 78f, size.Y - bottom - 78f);
            c.DrawCircle(center, 76f, new Color(0.02f, 0.04f, 0.06f, 0.28f));
            c.DrawArc(center, 76f, 0f, Mathf.Tau, 40, new Color(1f, 1f, 1f, 0.24f), 3f);

            Vector2 nub = center;
            if (_aimFinger >= 0)
                nub += (_aimAt - _aimFrom).LimitLength(44f);
            c.DrawCircle(nub, 30f, new Color(1f, 1f, 1f, _aimFinger >= 0 ? 0.34f : 0.18f));
            c.DrawArc(nub, 30f, 0f, Mathf.Tau, 24, new Color(1f, 1f, 1f, 0.32f), 2f);
            Palette.TextCentered(c, center + new Vector2(0f, -96f), "AIM", 12,
                new Color(1f, 1f, 1f, 0.52f));
        }

        foreach (var b in Buttons)
        {
            bool pressed = FlashUntil.TryGetValue(b.Action, out ulong until)
                && Time.GetTicksMsec() < until;
            var fill = b.Lit ? Palette.Highlight
                     : pressed ? new Color(1f, 0.82f, 0.38f, 0.95f)
                     : b.Big ? new Color(0.86f, 0.69f, 0.29f, 0.80f)
                             : new Color(1f, 1f, 1f, 0.16f);

            c.DrawCircle(b.Where.Position + b.Where.Size * 0.5f, b.Where.Size.X * 0.5f, fill);
            c.DrawArc(b.Where.Position + b.Where.Size * 0.5f, b.Where.Size.X * 0.5f,
                0f, Mathf.Tau, 28, new Color(0f, 0f, 0f, 0.45f), 3f);

            Palette.TextCentered(c, b.Where.Position + b.Where.Size * 0.5f +
                (pressed ? new Vector2(0f, 2f) : Vector2.Zero), b.Label,
                b.Big ? 19 : 15, b.Big || b.Lit || pressed
                    ? new Color(0.09f, 0.12f, 0.16f) : Palette.Ink);
        }

        // Desktop touch previews do not use the visible thumb pad, so retain the old hint there.
        if (!MobileLayout && _aimFinger < 0 && scene.HumanBatting)
            Palette.TextCentered(c, new Vector2(size.X * 0.5f, size.Y - 112f),
                "drag anywhere to aim", 13, new Color(1f, 1f, 1f, 0.42f));
    }
}
