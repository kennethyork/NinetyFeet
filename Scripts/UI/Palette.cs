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
        size = Scaled(size);
        if (Core.Game.Instance?.HighContrast == true && color == InkDim) color = Ink;
        canvas.DrawString(Font, at, text, align, width, size, color);
        Note(at, text, size);
    }

    /// <summary>Draws text centred on a point rather than aligned to a baseline box.</summary>
    public static void TextCentered(CanvasItem canvas, Vector2 center, string text, int size, Color color)
    {
        size = Scaled(size);
        if (Core.Game.Instance?.HighContrast == true && color == InkDim) color = Ink;
        Vector2 measured = Font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        var at = center - new Vector2(measured.X * 0.5f, -size * 0.35f);
        canvas.DrawString(Font, at, text, HorizontalAlignment.Left, -1, size, color);
        Note(at, text, size);
    }

    // -----------------------------------------------------------------------
    // Does the text fit?
    // -----------------------------------------------------------------------

    /// <summary>
    /// Watches where every string is actually drawn, so "does it all fit" can be measured.
    ///
    /// Screens here draw at fixed positions with no clipping and no layout engine, so a label
    /// that outgrows its column does not wrap or truncate — it prints straight across whatever is
    /// beside it, and both become unreadable. Nothing warns. It is found by somebody looking at
    /// the screen, which means it is found on the screens people look at and never on the rest.
    ///
    /// Two faults are worth catching and both are objective: a string drawn off the edge of the
    /// viewport, and two strings whose boxes overlap. The second has one honest exception — this
    /// game draws drop shadows by printing the same words twice a pixel apart — so identical text
    /// overlapping itself is not reported.
    /// </summary>
    public static bool Watching;

    private static readonly System.Collections.Generic.List<(Rect2 Box, string What, int Size)> Seen = new();

    private static void Note(Vector2 at, string text, int size)
    {
        if (!Watching || string.IsNullOrWhiteSpace(text)) return;

        // DrawString takes a baseline, so the box sits above the point it was given.
        float w = TextWidth(text, size);
        Seen.Add((new Rect2(at.X, at.Y - size * 0.82f, w, size * 1.05f), text, size));
    }

    public static void BeginWatch()
    {
        Seen.Clear();
        Watching = true;
    }

    /// <summary>What did not fit, worst first. Empty when everything did.</summary>
    public static System.Collections.Generic.List<string> Report(Vector2 viewport)
    {
        Watching = false;
        var faults = new System.Collections.Generic.List<string>();

        foreach (var (box, what, _) in Seen)
        {
            if (box.End.X > viewport.X + 1f)
                faults.Add($"off the right edge by {box.End.X - viewport.X:0} px: \"{Short(what)}\"");
            else if (box.Position.X < -1f)
                faults.Add($"off the left edge by {-box.Position.X:0} px: \"{Short(what)}\"");
            else if (box.End.Y > viewport.Y + 1f)
                faults.Add($"below the bottom by {box.End.Y - viewport.Y:0} px: \"{Short(what)}\"");
        }

        for (int i = 0; i < Seen.Count; i++)
            for (int j = i + 1; j < Seen.Count; j++)
            {
                var a = Seen[i];
                var b = Seen[j];

                // A drop shadow is the same words printed twice a pixel apart, on purpose.
                if (a.What == b.What) continue;
                if (!a.Box.Intersects(b.Box)) continue;

                var over = a.Box.Intersection(b.Box);
                if (over.Size.X < 3f || over.Size.Y < 3f) continue;

                faults.Add($"{over.Size.X:0} px of overlap: \"{Short(a.What)}\" across \"{Short(b.What)}\"");
            }

        Seen.Clear();
        return faults;
    }

    private static string Short(string s) => s.Length <= 42 ? s : s[..40] + "…";

    /// <summary>
    /// A string trimmed to fit a pixel width, with an ellipsis if anything was lost.
    ///
    /// Screens here draw at fixed positions with no clipping, so a string that is too long does
    /// not overflow tidily — it prints across whatever is beside it and both become unreadable.
    /// </summary>
    public static string Fit(string text, int size, float width)
    {
        if (string.IsNullOrEmpty(text) || width <= 8f) return "";
        if (TextWidth(text, size) <= width) return text;

        for (int keep = text.Length - 1; keep > 1; keep--)
        {
            string cut = text[..keep].TrimEnd() + "…";
            if (TextWidth(cut, size) <= width) return cut;
        }

        return "…";
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
        Font.GetStringSize(text, HorizontalAlignment.Left, -1, Scaled(size)).X;

    private static int Scaled(int size) => Core.Game.Instance?.LargeText == true
        ? Mathf.Max(size + 1, Mathf.RoundToInt(size * 1.12f)) : size;

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
        // Anchored to the window, like the panels it sits above.
        //
        // Moving it onto the centred stage was tried and was wrong: the menus are not on the
        // stage. Their panels are laid out as "forty in from each edge of the window", so a back
        // button pinned to a centred design box sat forty pixels inside them horizontally and,
        // on a tall window, two hundred and eighty pixels below the title it belongs beside. The
        // stage is for the ballfield, whose proportions are the game. A menu simply fills.
        Vector4 safe = Gameplay.TouchControls.SafeInsets(viewport);
        Vector2 buttonSize = Gameplay.TouchControls.MobileLayout
            ? new Vector2(112f, 48f) : new Vector2(104f, 34f);
        var rect = new Rect2(
            new Vector2(viewport.X - safe.Z - buttonSize.X - 28f, safe.Y + 22f), buttonSize);

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

    // -----------------------------------------------------------------------
    // Safe insets and virtual keyboard
    // -----------------------------------------------------------------------

    /// <summary>
    /// The x-coordinate for a screen title or header text. On desktop this is the caller's exact
    /// <paramref name="baseInset"/> (the historical 40 px, unchanged); on a phone with a camera
    /// cutout on the left edge, it shifts inward so the title clears the hole. Every menu passes
    /// this through instead of a bare 40f literal so a title never lands behind a system
    /// decoration on a device that has one, and never drifts from its old position on a desktop
    /// window that does not.
    /// </summary>
    public static float SafeLeft(Vector2 viewport, float baseInset = 40f)
    {
        if (!Gameplay.TouchControls.MobileLayout) return baseInset;
        Vector4 safe = Gameplay.TouchControls.SafeInsets(viewport);
        return Mathf.Max(baseInset, safe.X + 24f);
    }

    /// <summary>Right-edge inset paired with <see cref="SafeLeft"/> for footer text and buttons.</summary>
    public static float SafeRight(Vector2 viewport, float baseInset = 40f)
    {
        if (!Gameplay.TouchControls.MobileLayout) return baseInset;
        Vector4 safe = Gameplay.TouchControls.SafeInsets(viewport);
        return Mathf.Max(baseInset, safe.Z + 24f);
    }

    /// <summary>
    /// The distance from the top edge a title's baseline should sit at. On desktop the caller's
    /// exact <paramref name="baseInset"/> is returned; on phones a floor of (cutout + 40) applies
    /// so the title clears any camera hole-punch.
    /// </summary>
    public static float SafeTop(Vector2 viewport, float baseInset = 46f)
    {
        if (!Gameplay.TouchControls.MobileLayout) return baseInset;
        Vector4 safe = Gameplay.TouchControls.SafeInsets(viewport);
        return Mathf.Max(baseInset, safe.Y + 40f);
    }

    /// <summary>
    /// The y-coordinate a footer line of text should sit at, measured from the top. Given as
    /// (viewport.Y - offset) so the caller reads like <c>SafeBottom(size, 22f)</c> where 22 is the
    /// historical baseline distance from the bottom. Rises on phones only so the hint clears the
    /// system gesture bar; on desktop the caller's exact inset is honoured.
    /// </summary>
    public static float SafeBottom(Vector2 viewport, float baseInset = 22f)
    {
        if (!Gameplay.TouchControls.MobileLayout) return viewport.Y - baseInset;
        Vector4 safe = Gameplay.TouchControls.SafeInsets(viewport);
        return viewport.Y - Mathf.Max(baseInset, safe.W + 22f);
    }

    /// <summary>
    /// Opens Android's on-screen keyboard on top of the game, feeding key events into whatever
    /// screen registered a text field. A no-op on desktop and on devices where the platform does
    /// not provide one. Every screen with a canvas-drawn text field should call this the moment
    /// the field takes focus, or the field is inert on a phone.
    ///
    /// The soft keyboard delivers taps as ordinary InputEventKey events (both unicode for
    /// printable characters and Key.Backspace / Key.Enter for the specials), so the existing
    /// per-screen typing code in <c>_UnhandledInput</c> reads it identically to a physical
    /// keyboard. This exists only to trigger the overlay.
    /// </summary>
    public static void ShowSoftKeyboard(string existing, int maxLength = -1,
        DisplayServer.VirtualKeyboardType type = DisplayServer.VirtualKeyboardType.Default)
    {
        if (!Gameplay.TouchControls.MobileLayout) return;
        if (!DisplayServer.HasFeature(DisplayServer.Feature.VirtualKeyboard)) return;
        DisplayServer.VirtualKeyboardShow(existing ?? "", new Rect2(),
            type, maxLength, existing?.Length ?? 0, existing?.Length ?? 0);
    }

    public static void HideSoftKeyboard()
    {
        if (!Gameplay.TouchControls.MobileLayout) return;
        if (!DisplayServer.HasFeature(DisplayServer.Feature.VirtualKeyboard)) return;
        DisplayServer.VirtualKeyboardHide();
    }
}
