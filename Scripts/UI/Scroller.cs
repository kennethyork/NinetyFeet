using Godot;

namespace SandlotSlugfest.UI;

/// <summary>
/// A scrolling column of content, for the screens that grew past the window.
///
/// Every screen in this game composes itself against a 1280x720 viewport and draws where it likes.
/// That works right up until a screen gains a row — Settings had eleven and now has thirteen, and
/// the last three of them were simply off the bottom of the world with the eleventh printed
/// through the footer. Nothing warned about it, because nothing measures how tall a screen has
/// become; it is just drawn, and whatever falls off the edge is gone.
///
/// This is deliberately not a Godot ScrollContainer. The screens do their own drawing and their own
/// hit testing, so a container would need every one of them rebuilt as a node tree. What they
/// actually need is one number to subtract from y, an honest measurement of how far the content
/// ran, and something on screen saying there is more — which is all this is.
///
/// The one rule for a screen using it: draw the scrolling content first and the header and footer
/// afterwards. There is no clipping here, so content scrolled above the top would otherwise be
/// printed straight through the title.
/// </summary>
public sealed class Scroller
{
    /// <summary>How far down the content the view has been moved, in pixels.</summary>
    public float Offset { get; private set; }

    private float _content;
    private float _view;
    private float _top;

    /// <summary>Whether there is anything below the fold worth telling the player about.</summary>
    public bool Overflows => _content > _view + 1f;

    public float Max => Mathf.Max(0f, _content - _view);

    /// <summary>
    /// Opens a frame. The band the content lives in runs from <paramref name="top"/> to
    /// <paramref name="bottom"/>, and the y a screen should start drawing at is returned.
    /// </summary>
    public float Begin(float top, float bottom)
    {
        _top = top;
        _view = Mathf.Max(1f, bottom - top);
        Offset = Mathf.Clamp(Offset, 0f, Max);
        return top - Offset;
    }

    /// <summary>
    /// Closes a frame, told where the content actually finished. Measured rather than declared,
    /// so a screen that gains a row scrolls further without anybody remembering to say so.
    /// </summary>
    public void End(float finishedAt) => _content = Mathf.Max(0f, finishedAt + Offset - _top);

    public void By(float pixels) => Offset = Mathf.Clamp(Offset + pixels, 0f, Max);
    public void Home() => Offset = 0f;

    /// <summary>Moves only far enough to keep a controller-focused drawn row in view.</summary>
    public void Reveal(Rect2 rect, float top, float bottom)
    {
        if (rect.Position.Y < top) By(rect.Position.Y - top);
        else if (rect.End.Y > bottom) By(rect.End.Y - bottom);
    }

    /// <summary>Handles a wheel event. Returns true if it was one, so the caller can redraw.</summary>
    public bool Wheel(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true } mb) return false;

        switch (mb.ButtonIndex)
        {
            case MouseButton.WheelUp: By(-56f); return true;
            case MouseButton.WheelDown: By(56f); return true;
            default: return false;
        }
    }

    /// <summary>Handles the keys that ought to scroll. Returns true if it was one of them.</summary>
    public bool Key(Godot.Key key)
    {
        switch (key)
        {
            case Godot.Key.Up or Godot.Key.W: By(-56f); return true;
            case Godot.Key.Down or Godot.Key.S: By(56f); return true;
            case Godot.Key.Pageup: By(-_view * 0.8f); return true;
            case Godot.Key.Pagedown: By(_view * 0.8f); return true;
            case Godot.Key.Home: Home(); return true;
            case Godot.Key.End: Offset = Max; return true;
            default: return false;
        }
    }

    /// <summary>
    /// The bar, drawn only when there is something to scroll.
    ///
    /// A screen that scrolls and does not say so is a screen with content nobody finds. The thumb
    /// is sized to the share of the content on show, which is the only honest way to say how much
    /// more there is.
    /// </summary>
    public void Draw(CanvasItem c, float x, float top, float bottom)
    {
        if (!Overflows) return;

        float height = bottom - top;
        var track = new Rect2(x, top, 5f, height);
        c.DrawRect(track, new Color(1f, 1f, 1f, 0.06f));

        float share = Mathf.Clamp(_view / _content, 0.08f, 1f);
        float thumb = height * share;
        float travel = Max <= 0f ? 0f : Offset / Max;

        c.DrawRect(new Rect2(x, top + (height - thumb) * travel, 5f, thumb),
            new Color(1f, 1f, 1f, 0.30f));
    }
}
