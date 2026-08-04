using Godot;

namespace SandlotSlugfest.Core;

/// <summary>
/// Registers the control scheme at startup instead of storing it in project.godot, so the
/// bindings live next to the code that reads them and stay readable in version control.
/// </summary>
public static class InputActions
{
    public const string AimUp = "aim_up";
    public const string AimDown = "aim_down";
    public const string AimLeft = "aim_left";
    public const string AimRight = "aim_right";
    public const string Action = "action";        // normal swing / pitch / confirm
    public const string SwingPower = "swing_power";
    public const string SwingContact = "swing_contact";
    public const string Bunt = "bunt";
    public const string Pitch1 = "pitch_1";
    public const string Pitch2 = "pitch_2";
    public const string Pitch3 = "pitch_3";
    public const string Pitch4 = "pitch_4";
    public const string ThrowHome = "throw_home";
    public const string ThrowFirst = "throw_first";
    public const string ThrowSecond = "throw_second";
    public const string ThrowThird = "throw_third";
    public const string SendRunners = "send_runners";
    public const string HoldRunners = "hold_runners";
    public const string Pause = "pause";

    /// <summary>Arms the current player's signature move — the Backyard power-up.</summary>
    public const string PowerUp = "power_up";

    // --- Managing. A human used to have no way to do any of this: the computer stole bases
    // against him at a rate he could not answer, and he could neither change a pitcher nor send
    // up a bat for one. These are the decisions a manager actually makes during a game.

    /// <summary>Send the lead runner. Held down, it is the steal sign.</summary>
    public const string Steal = "steal";

    /// <summary>Bring in a bat for the man due up.</summary>
    public const string PinchHit = "pinch_hit";

    /// <summary>Go to the bullpen.</summary>
    public const string ChangePitcher = "change_pitcher";

    /// <summary>A trip to the mound to settle him rather than to take him out. Five a game.</summary>
    public const string MoundVisit = "mound_visit";

    /// <summary>Put him on. A pitchout when there is a runner going.</summary>
    public const string IntentionalWalk = "intentional_walk";

    /// <summary>Also accepted as "confirm" on menus, alongside the action button.</summary>
    public const string Confirm = "confirm";
    public const string Back = "back";

    public static void Register()
    {
        Bind(AimUp, Key.W, Key.Up);
        Bind(AimDown, Key.S, Key.Down);
        Bind(AimLeft, Key.A, Key.Left);
        Bind(AimRight, Key.D, Key.Right);
        Bind(Action, Key.Space);
        Bind(SwingPower, Key.F);
        Bind(SwingContact, Key.C);
        Bind(Bunt, Key.B);
        Bind(Confirm, Key.Enter, Key.KpEnter, Key.Space);
        Bind(Back, Key.Escape, Key.Backspace);

        // The mouse is the natural way to play this: point where you want to swing and click.
        // Left is a normal cut, right is a power hack, middle shortens up for contact.
        BindMouse(Action, MouseButton.Left);
        BindMouse(SwingPower, MouseButton.Right);
        BindMouse(SwingContact, MouseButton.Middle);

        // --- Gamepad. Left stick and the d-pad both aim; the face buttons do the work. ---
        BindStick(AimLeft, JoyAxis.LeftX, -1f);
        BindStick(AimRight, JoyAxis.LeftX, 1f);
        // Godot's Y axis points down, so "up" on the stick is a negative value.
        BindStick(AimUp, JoyAxis.LeftY, -1f);
        BindStick(AimDown, JoyAxis.LeftY, 1f);

        BindPad(AimLeft, JoyButton.DpadLeft);
        BindPad(AimRight, JoyButton.DpadRight);
        BindPad(AimUp, JoyButton.DpadUp);
        BindPad(AimDown, JoyButton.DpadDown);

        BindPad(Action, JoyButton.A);
        BindPad(Confirm, JoyButton.A);
        BindPad(SwingPower, JoyButton.Y);
        BindPad(SwingContact, JoyButton.X);
        BindPad(Bunt, JoyButton.LeftShoulder);
        BindPad(Back, JoyButton.B);
        BindPad(Pause, JoyButton.Start);

        BindPad(SendRunners, JoyButton.RightShoulder);
        BindPad(HoldRunners, JoyButton.LeftShoulder);

        Bind(Pitch1, Key.Key1);
        Bind(Pitch2, Key.Key2);
        Bind(Pitch3, Key.Key3);
        Bind(Pitch4, Key.Key4);

        // Throwing reuses the number row: the same key that picks a pitch picks a base.
        Bind(ThrowHome, Key.Key4);
        Bind(ThrowFirst, Key.Key1);
        Bind(ThrowSecond, Key.Key2);
        Bind(ThrowThird, Key.Key3);

        Bind(SendRunners, Key.E);
        Bind(HoldRunners, Key.Q);
        Bind(Pause, Key.Escape);
        Bind(PowerUp, Key.Shift, Key.Tab);
        BindPad(PowerUp, JoyButton.RightShoulder);

        // The manager's keys sit under the left hand, away from the swing.
        Bind(Steal, Key.G);
        Bind(PinchHit, Key.H);
        Bind(ChangePitcher, Key.P);
        Bind(MoundVisit, Key.V);
        Bind(IntentionalWalk, Key.I);
        BindPad(Steal, JoyButton.LeftStick);
        BindPad(PinchHit, JoyButton.RightStick);

        // Pitch types and throw targets share the face buttons and shoulders on a pad.
        BindPad(Pitch1, JoyButton.A);
        BindPad(Pitch2, JoyButton.B);
        BindPad(Pitch3, JoyButton.X);
        BindPad(Pitch4, JoyButton.Y);

        BindPad(ThrowFirst, JoyButton.A);
        BindPad(ThrowSecond, JoyButton.B);
        BindPad(ThrowThird, JoyButton.X);
        BindPad(ThrowHome, JoyButton.Y);
    }

    /// <summary>True when any gamepad is plugged in, used to pick which prompts to show.</summary>
    public static bool GamepadConnected => Input.GetConnectedJoypads().Count > 0;

    private static void Bind(string action, params Key[] keys)
    {
        if (InputMap.HasAction(action)) InputMap.EraseAction(action);
        InputMap.AddAction(action, 0.25f);
        foreach (var key in keys)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }

    /// <summary>Adds mouse buttons to an action that has already been bound to keys.</summary>
    private static void BindMouse(string action, params MouseButton[] buttons)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action, 0.25f);
        foreach (var button in buttons)
            InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
    }

    private static void BindPad(string action, JoyButton button)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action, 0.25f);
        InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = button });
    }

    /// <summary>Binds one direction of an analog stick. <paramref name="value"/> is -1 or +1.</summary>
    private static void BindStick(string action, JoyAxis axis, float value)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action, 0.25f);
        InputMap.ActionAddEvent(action, new InputEventJoypadMotion { Axis = axis, AxisValue = value });
    }
}
