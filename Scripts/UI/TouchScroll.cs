using Godot;

namespace SandlotSlugfest.UI;

/// <summary>
/// Where a phone's scroll gesture should go this frame.
///
/// The menu layer's scroll used to be quantised to mouse-wheel notches: a finger drag was
/// converted to WheelUp/WheelDown at 36 pixels per notch, and each notch moved 3 rows or 56/60
/// pixels — a mismatch that made the same drag scroll twice as fast on Cards as on League
/// Office, and made every list scroll in visible steps rather than sliding under the thumb.
/// Neither the sensitivity nor the stepping is a mobile-native feel.
///
/// The Game autoload delivers the raw finger delta here every frame instead. Screens with a
/// list register a handler in their _Ready and clear it in _ExitTree; on release a decaying
/// fling keeps pushing deltas until the momentum drops below a floor, so a flick from the
/// bottom of a long list carries the way it does in every other Android app.
///
/// The delegate is intentionally single-slotted rather than a bus. Only one menu is on screen
/// at a time — mixing scroll events across screens is not a case that ever arises in this game
/// — and a straight assignment keeps unregistration a one-liner in _ExitTree.
/// </summary>
public static class TouchScroll
{
    /// <summary>
    /// The screen currently listening for scroll deltas. Delta is in pixels of scroll offset:
    /// positive means the content underneath should move up (equivalent to Scroller.By or
    /// _scroll incrementing). Position is where the finger is on screen, so a screen with two
    /// panes (Trade, for example) can route the delta to whichever list is under the finger.
    /// </summary>
    public static System.Action<float, Vector2> Handler;

    public static bool Active => Handler != null;

    /// <summary>Passes a delta through to the active screen. A no-op if nobody registered.</summary>
    public static void Push(float pixels, Vector2 position) => Handler?.Invoke(pixels, position);
}
