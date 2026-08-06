using Godot;

namespace SandlotSlugfest.UI;

/// <summary>
/// Where the composition sits inside whatever window the game has been given.
///
/// The project stretches in canvas_items mode with the aspect set to expand, which has one very
/// convenient property: the viewport is measured in canvas units, and those units never shrink
/// below the 1280x720 everything was composed against. A wider window gains width, a taller one
/// gains height, and neither ever takes any away. So responsiveness here is not a scaling problem
/// — nothing has to be made smaller — it is a question of what to do with the extra room.
///
/// Two answers, and which one applies depends on what is being drawn.
///
/// Backdrops fill it. Sky, grass, the neighbourhood beyond the fence, the panel behind a menu —
/// these should reach the edges of the screen whatever shape it is, and stretching them is
/// invisible because there is nothing in them whose proportions anybody could check.
///
/// Compositions are centred in it. The strike zone, the plate, the mound, the batter: the
/// distances between those are the game. PixelsPerFoot is a constant for exactly that reason, so
/// a hitter's timing cannot depend on the shape of his window — and the anchors those things hang
/// off have to be just as invariant, or the zone drifts away from the plate as the window changes
/// and the field comes apart. Which it did: the aspect was pinned to "keep" and the game
/// letterboxed rather than face this.
/// </summary>
public static class Layout
{
    /// <summary>The size every screen in this game is composed against.</summary>
    public static readonly Vector2 Design = new(1280f, 720f);

    /// <summary>
    /// The centred design-sized box inside the viewport. Never smaller than the design size, and
    /// its position is the margin the extra room has been split into.
    /// </summary>
    public static Rect2 Stage(Vector2 size)
    {
        var extra = size - Design;
        var origin = new Vector2(Mathf.Max(0f, extra.X) * 0.5f, Mathf.Max(0f, extra.Y) * 0.5f);
        return new Rect2(origin, new Vector2(Mathf.Max(Design.X, size.X - origin.X * 2f),
                                             Mathf.Max(Design.Y, size.Y - origin.Y * 2f)));
    }

    /// <summary>Where the design box starts. Zero on a 16:9 window.</summary>
    public static Vector2 Origin(Vector2 size) => Stage(size).Position;

    /// <summary>A point given in design coordinates, placed on this viewport.</summary>
    public static Vector2 At(Vector2 size, float x, float y) => Origin(size) + new Vector2(x, y);

    /// <summary>A fraction of the design box rather than of the window.</summary>
    public static float Down(Vector2 size, float fraction) => Origin(size).Y + Design.Y * fraction;
    public static float Across(Vector2 size, float fraction) => Origin(size).X + Design.X * fraction;
}
