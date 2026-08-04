using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.UI;

/// <summary>
/// The world beyond the outfield wall: sky, sun, clouds and a neighbourhood of houses and
/// trees. Backyard Baseball is played in somebody's neighbourhood rather than a stadium, and a
/// flat blue rectangle behind the fence is what made every park read as the same empty box.
/// Everything is seeded from the ballpark, so all 32 have their own skyline.
/// </summary>
public static class Scenery
{
    private static readonly Color[] RoofColours =
    {
        new("#8c3b32"), new("#4a5d7e"), new("#6b4a2f"), new("#3f6b52"),
        new("#7a4a6b"), new("#a05c2c"), new("#54606e"), new("#8a6d3b"),
    };

    private static readonly Color[] WallColours =
    {
        new("#e8d9bd"), new("#d9c4a5"), new("#cbd6dd"), new("#e3c9b0"),
        new("#d5ddc8"), new("#efe2cc"), new("#c9bfae"), new("#e6cfc0"),
    };

    /// <summary>A soft vertical gradient, drawn as a single polygon with per-vertex colours.</summary>
    public static void SkyGradient(CanvasItem c, Rect2 rect, Color top, Color bottom)
    {
        var points = new[]
        {
            rect.Position,
            rect.Position + new Vector2(rect.Size.X, 0f),
            rect.End,
            rect.Position + new Vector2(0f, rect.Size.Y),
        };
        c.DrawPolygon(points, new[] { top, top, bottom, bottom });
    }

    /// <summary>Sun with a soft halo. Skipped for covered parks by the caller.</summary>
    public static void Sun(CanvasItem c, Vector2 at, float radius)
    {
        for (int i = 4; i >= 1; i--)
            c.DrawCircle(at, radius * (1f + i * 0.45f), new Color(1f, 0.95f, 0.72f, 0.05f));
        c.DrawCircle(at, radius, new Color("#ffe9a3"));
        c.DrawCircle(at, radius * 0.82f, new Color("#fff6d0"));
    }

    /// <summary>Fat cartoon clouds built from overlapping circles.</summary>
    public static void Clouds(CanvasItem c, Rect2 band, int seed, float drift)
    {
        var rng = new Rng(seed * 977 + 13);
        int count = rng.Range(3, 6);

        for (int i = 0; i < count; i++)
        {
            float baseX = rng.Range(-0.1f, 1.1f) * band.Size.X;
            float x = Mathf.PosMod(baseX + drift * rng.Range(3f, 9f), band.Size.X * 1.3f) - band.Size.X * 0.15f;
            float y = band.Position.Y + rng.Range(0.12f, 0.78f) * band.Size.Y;
            float s = rng.Range(0.7f, 1.5f);

            var tint = new Color(1f, 1f, 1f, rng.Range(0.62f, 0.85f));
            var at = new Vector2(band.Position.X + x, y);

            c.DrawCircle(at, 22f * s, tint);
            c.DrawCircle(at + new Vector2(20f * s, 5f * s), 17f * s, tint);
            c.DrawCircle(at + new Vector2(-19f * s, 6f * s), 15f * s, tint);
            c.DrawCircle(at + new Vector2(8f * s, -11f * s), 15f * s, tint);
        }
    }

    /// <summary>
    /// Houses, trees and a distant hill line sitting behind the outfield wall. The wall's own
    /// silhouette is passed in so the buildings tuck in behind it rather than floating.
    /// </summary>
    public static void Neighbourhood(CanvasItem c, float width, float horizonY, int seed)
    {
        var rng = new Rng(seed * 5107 + 41);

        // Distant hills first, so everything else overlaps them.
        var hill = new Color("#5c7f5e");
        for (int pass = 0; pass < 2; pass++)
        {
            float amp = 26f - pass * 9f;
            float baseY = horizonY - 10f + pass * 8f;
            const int steps = 24;
            var pts = new Vector2[steps + 3];
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float x = t * width;
                float y = baseY - Mathf.Sin(t * 6.3f + pass * 2.1f + seed) * amp
                                - Mathf.Sin(t * 13.1f + seed) * amp * 0.35f;
                pts[i] = new Vector2(x, y);
            }
            pts[steps + 1] = new Vector2(width, horizonY + 40f);
            pts[steps + 2] = new Vector2(0f, horizonY + 40f);
            c.DrawColoredPolygon(pts, pass == 0 ? hill.Darkened(0.25f) : hill);
        }

        // Then the houses along the street behind the fence.
        float x2 = rng.Range(-30f, 10f);
        while (x2 < width + 40f)
        {
            float w = rng.Range(52f, 96f);
            float h = rng.Range(38f, 70f);
            float top = horizonY - h;

            var wall = WallColours[rng.Range(0, WallColours.Length)];
            var roof = RoofColours[rng.Range(0, RoofColours.Length)];

            // Body.
            var body = new Rect2(new Vector2(x2, top), new Vector2(w, h + 14f));
            c.DrawRect(body, wall);
            c.DrawRect(body, new Color(0f, 0f, 0f, 0.35f), false, 2f);

            // Pitched roof.
            c.DrawColoredPolygon(new[]
            {
                new Vector2(x2 - 6f, top),
                new Vector2(x2 + w + 6f, top),
                new Vector2(x2 + w * 0.5f, top - rng.Range(16f, 30f)),
            }, roof);

            // A couple of lit windows.
            int cols = Mathf.Max(1, (int)(w / 30f));
            for (int i = 0; i < cols; i++)
            {
                if (rng.Chance(0.25f)) continue;
                var win = new Rect2(
                    new Vector2(x2 + 10f + i * (w - 16f) / cols, top + 12f),
                    new Vector2(13f, 15f));
                c.DrawRect(win, rng.Chance(0.4f) ? new Color("#ffe9a8") : new Color("#7f93a8"));
                c.DrawRect(win, new Color(0f, 0f, 0f, 0.3f), false, 1.5f);
            }

            x2 += w + rng.Range(10f, 34f);

            // A tree in the gap now and then.
            if (rng.Chance(0.45f))
            {
                float tx = x2 - rng.Range(6f, 20f);
                float th = rng.Range(30f, 56f);
                c.DrawLine(new Vector2(tx, horizonY + 12f), new Vector2(tx, horizonY - th),
                    new Color("#6b4a2f"), 7f);
                var leaf = new Color("#3f7a45").Lightened(rng.Range(0f, 0.22f));
                c.DrawCircle(new Vector2(tx, horizonY - th - 6f), 20f, leaf);
                c.DrawCircle(new Vector2(tx - 15f, horizonY - th + 6f), 15f, leaf.Darkened(0.12f));
                c.DrawCircle(new Vector2(tx + 15f, horizonY - th + 4f), 16f, leaf.Lightened(0.08f));
            }
        }
    }

    /// <summary>Spectators along the top of the wall — a few dozen bobbing heads.</summary>
    public static void Crowd(CanvasItem c, float width, float wallTopY, int seed, float time)
    {
        var rng = new Rng(seed * 31 + 7);
        var skins = new[]
        {
            new Color("#f6d3b3"), new Color("#dda172"), new Color("#a9663c"),
            new Color("#8a4e2c"), new Color("#eebd94"),
        };

        for (float x = 6f; x < width; x += rng.Range(16f, 30f))
        {
            float bob = Mathf.Sin(time * rng.Range(1.5f, 3.5f) + x * 0.11f) * 2.2f;
            var head = new Vector2(x, wallTopY - 7f + bob);
            var shirt = new Color(rng.NextFloat(), rng.NextFloat(), rng.NextFloat()).Lightened(0.25f);

            c.DrawRect(new Rect2(head + new Vector2(-6f, 2f), new Vector2(12f, 10f)), shirt);
            c.DrawCircle(head, 5f, skins[rng.Range(0, skins.Length)]);
        }
    }
}
