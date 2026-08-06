using System;
using Godot;

namespace SandlotSlugfest.UI;

/// <summary>Small, shared translations for custom-drawn menus that do not use Godot Controls.</summary>
public static class ControllerNav
{
    public static bool TryKey(InputEvent e, out Key key)
    {
        key = Key.None;
        if (e is not InputEventJoypadButton { Pressed: true } pad) return false;
        key = pad.ButtonIndex switch
        {
            JoyButton.DpadUp => Key.Up,
            JoyButton.DpadDown => Key.Down,
            JoyButton.DpadLeft => Key.Left,
            JoyButton.DpadRight => Key.Right,
            JoyButton.A or JoyButton.Start => Key.Enter,
            JoyButton.B => Key.Escape,
            _ => Key.None,
        };
        return key != Key.None;
    }
}
