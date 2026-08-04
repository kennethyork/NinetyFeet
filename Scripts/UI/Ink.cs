using Godot;
using SandlotSlugfest.Core;

namespace SandlotSlugfest.UI;

/// <summary>
/// Drawing helpers that take the hard vector edge off everything. Perfectly straight lines and
/// flat fills are what make procedural art read as a diagram rather than as something drawn, so
/// outlines here wobble slightly and fills carry a gradient and a little grain.
/// </summary>
public static class Ink
{
    /// <summary>
    /// A line with a slight wobble, as if drawn by hand.
    /// <paramref name="seed"/> must identify the *edge*, not its screen position. Seeding from
    /// the endpoints looks stable but is not: anything that moves — and every player bobs — gets
    /// a new seed each frame, so its outline crawls and the shape appears to breathe.
    /// </summary>
    public static void Line(CanvasItem c, Vector2 a, Vector2 b, Color colour, float width,
        int seed, float wobble = 1.6f)
    {
        Vector2 d = b - a;
        float len = d.Length();
        if (len < 0.001f) return;

        Vector2 n = new Vector2(-d.Y, d.X) / len;
        int segs = Mathf.Clamp(Mathf.RoundToInt(len / 26f), 1, 5);
        if (segs == 1) { c.DrawLine(a, b, colour, width); return; }

        var rng = new Rng(seed);

        Vector2 prev = a;
        for (int i = 1; i <= segs; i++)
        {
            float t = i / (float)segs;
            Vector2 point = a + d * t;
            if (i < segs) point += n * (rng.NextFloat() - 0.5f) * wobble * Mathf.Min(len * 0.06f, 3.2f);
            c.DrawLine(prev, point, colour, width);
            prev = point;
        }
    }

    /// <summary>
    /// A filled polygon with a hand-drawn outline. Each edge takes its wobble from the shape's
    /// seed plus its own index, so the whole outline holds still while the figure moves.
    /// </summary>
    public static void Shape(CanvasItem c, Vector2[] points, Color fill, Color outline, float width,
        int seed)
    {
        c.DrawColoredPolygon(points, fill);
        for (int i = 0; i < points.Length; i++)
            Line(c, points[i], points[(i + 1) % points.Length], outline, width, seed * 131 + i * 17);
    }

    /// <summary>A vertical gradient fill across a rectangle.</summary>
    public static void GradientRect(CanvasItem c, Rect2 rect, Color top, Color bottom)
    {
        var pts = new[]
        {
            rect.Position,
            rect.Position + new Vector2(rect.Size.X, 0f),
            rect.End,
            rect.Position + new Vector2(0f, rect.Size.Y),
        };
        c.DrawPolygon(pts, new[] { top, top, bottom, bottom });
    }

    /// <summary>
    /// A rounded blob built from a circle whose radius varies per vertex. Bodies and heads drawn
    /// as four-point quads look like cardboard; this gives them some softness.
    /// </summary>
    public static Vector2[] Blob(Vector2 centre, float rx, float ry, int points = 18, float squash = 0f)
    {
        var pts = new Vector2[points];
        for (int i = 0; i < points; i++)
        {
            float a = i / (float)points * Mathf.Tau;
            float r = 1f + Mathf.Sin(a * 2f) * squash;
            pts[i] = centre + new Vector2(Mathf.Cos(a) * rx * r, Mathf.Sin(a) * ry * r);
        }
        return pts;
    }

    /// <summary>A capsule between two points — the basis for arms, legs and torsos.</summary>
    public static Vector2[] Capsule(Vector2 a, Vector2 b, float radius, int arc = 7)
    {
        Vector2 d = b - a;
        float len = d.Length();
        if (len < 0.001f) return Blob(a, radius, radius);

        Vector2 dir = d / len;
        Vector2 n = new(-dir.Y, dir.X);
        var pts = new Vector2[arc * 2];

        for (int i = 0; i < arc; i++)
        {
            float t = Mathf.Pi * i / (arc - 1);
            pts[i] = b + n * Mathf.Cos(t) * radius + dir * Mathf.Sin(t) * radius;
        }
        for (int i = 0; i < arc; i++)
        {
            float t = Mathf.Pi * i / (arc - 1);
            pts[arc + i] = a - n * Mathf.Cos(t) * radius - dir * Mathf.Sin(t) * radius;
        }
        return pts;
    }

    /// <summary>Scatters faint speckles over an area so large flat fills have some grain.</summary>
    public static void Grain(CanvasItem c, Rect2 area, int seed, int count, Color tint, float size = 2.4f)
    {
        var rng = new Rng(seed);
        for (int i = 0; i < count; i++)
        {
            var at = new Vector2(
                area.Position.X + rng.NextFloat() * area.Size.X,
                area.Position.Y + rng.NextFloat() * area.Size.Y);
            c.DrawCircle(at, size * rng.Range(0.5f, 1.4f), tint);
        }
    }
}
