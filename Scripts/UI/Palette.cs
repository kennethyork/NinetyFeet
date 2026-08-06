using Godot;

namespace SandlotSlugfest.UI;

/// <summary>Shared colours and drawing helpers so every screen looks like the same game.</summary>
public static class Palette
{
    public static readonly Color Night = new("#0f151d");
    public static readonly Color Panel = new("#1a2431");
    public static readonly Color PanelLight = new("#243244");
    public static readonly Color Ink = new("#e9eef5");
    public static readonly Color InkDim = new("#93a2b5");
    public static readonly Color Accent = new("#4cc9f0");
    public static readonly Color Highlight = new("#ffd166");
    public static readonly Color Grass = new("#2f7d43");
    public static readonly Color GrassDark = new("#276b39");
    public static readonly Color Dirt = new("#b1793f");
    public static readonly Color DirtDark = new("#9a6733");
    public static readonly Color Chalk = new("#f2f4f0");
    public static readonly Color Ball = new("#fbf7ea");
    public static readonly Color Sky = new("#2a4a6b");
    public static readonly Color Warning = new("#e5544b");

    private static Font _font;
    public static Font Font => _font ??= ThemeDB.FallbackFont;

    public static void Text(CanvasItem canvas, Vector2 at, string text, int size, Color color,
        HorizontalAlignment align = HorizontalAlignment.Left, float width = -1f)
    {
        canvas.DrawString(Font, at, text, align, width, size, color);
    }

    /// <summary>Draws text centred on a point rather than aligned to a baseline box.</summary>
    public static void TextCentered(CanvasItem canvas, Vector2 center, string text, int size, Color color)
    {
        Vector2 measured = Font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        canvas.DrawString(Font, center - new Vector2(measured.X * 0.5f, -size * 0.35f),
            text, HorizontalAlignment.Left, -1, size, color);
    }

    /// <summary>
    /// A paragraph inside a given width, returning the y it finished on.
    ///
    /// Every screen in this game draws single lines at fixed positions, which is fine for a
    /// statistic and useless for prose — a biography written to a fixed point simply runs off the
    /// side of the card. Words are measured and broken between them; a word longer than the whole
    /// column is placed anyway rather than dropped, because losing a man's name is worse than
    /// overrunning by a few pixels.
    /// </summary>
    public static float Wrapped(CanvasItem canvas, Vector2 at, string text, int size,
        float width, Color color, float lineHeight = 0f)
    {
        if (string.IsNullOrEmpty(text)) return at.Y;

        float step = lineHeight > 0f ? lineHeight : size * 1.35f;
        float y = at.Y;
        var line = new System.Text.StringBuilder();

        foreach (string word in text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = line.Length == 0 ? word : $"{line} {word}";
            if (line.Length > 0 && TextWidth(candidate, size) > width)
            {
                Text(canvas, new Vector2(at.X, y), line.ToString(), size, color);
                y += step;
                line.Clear();
                line.Append(word);
                continue;
            }

            line.Clear();
            line.Append(candidate);
        }

        if (line.Length > 0)
        {
            Text(canvas, new Vector2(at.X, y), line.ToString(), size, color);
            y += step;
        }

        return y;
    }

    public static float TextWidth(string text, int size) =>
        Font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;

    /// <summary>
    /// A washed-out copy of a club's colours, for players who are out of the play.
    ///
    /// Keyed on the kit itself rather than the club's id. Since a club now has two kits — its own
    /// at home and greys on the road — an id is no longer unique to one set of colours, and the
    /// first one asked for was being handed back for the other. A visiting side could end up
    /// wearing the home club's washed-out colours, or the other way about.
    /// </summary>
    public static Data.TeamData GreyedOut(Data.TeamData team)
    {
        _greyed ??= new System.Collections.Generic.Dictionary<Data.TeamData, Data.TeamData>();
        if (_greyed.TryGetValue(team, out var cached)) return cached;

        var faded = new Data.TeamData
        {
            Id = team.Id,
            City = team.City,
            Nickname = team.Nickname,
            Abbrev = team.Abbrev,
            League = team.League,
            Division = team.Division,
            Primary = team.Primary.Lerp(new Color(0.35f, 0.35f, 0.38f), 0.75f),
            Secondary = team.Secondary.Lerp(new Color(0.45f, 0.45f, 0.48f), 0.75f),
            Motto = team.Motto,
        };
        _greyed[team] = faded;
        return faded;
    }

    private static System.Collections.Generic.Dictionary<Data.TeamData, Data.TeamData> _greyed;

    /// <summary>
    /// A clickable back button in the top-right of a screen, so every menu can be left with the
    /// mouse alone rather than requiring Esc.
    /// </summary>
    public static void BackButton(CanvasItem canvas, Vector2 viewport, ClickMap clicks,
        System.Action onBack)
    {
        var rect = new Rect2(new Vector2(viewport.X - 118f, 20f), new Vector2(96f, 32f));
        Panel3D(canvas, rect, PanelLight);
        TextCentered(canvas, rect.Position + rect.Size * 0.5f, "‹  BACK", 15, Ink);
        clicks.Add(rect, onBack);
    }

    public static void Panel3D(CanvasItem canvas, Rect2 rect, Color fill, float radius = 8f)
    {
        canvas.DrawRect(rect, fill);
        canvas.DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, 2f)), fill.Lightened(0.18f));
        canvas.DrawRect(new Rect2(rect.Position + new Vector2(0f, rect.Size.Y - 2f),
            new Vector2(rect.Size.X, 2f)), fill.Darkened(0.25f));
    }
}
