using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Season;

namespace SandlotSlugfest.UI;

/// <summary>
/// The club's correspondence: who wrote, what they said, and what you have not read yet.
///
/// The news feed on the season screen says what happened. This says what somebody thinks about it,
/// which is the part that makes a franchise feel like a job rather than a table.
/// </summary>
public partial class InboxScreen : Control
{
    private int _selected;
    private int _scroll;
    private readonly ClickMap _clicks = new();

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Opening the inbox reads the one you land on.
        if (Inbox.Messages.Count > 0) Inbox.Messages[0].Read = true;
    }

    private void Leave() => Game.Instance.GoTo(Game.Instance.League != null
        ? "res://Scenes/Season.tscn"
        : "res://Scenes/MainMenu.tscn");

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion m) { if (_clicks.Hover(m.Position)) QueueRedraw(); return; }

        if (@event is InputEventMouseButton { Pressed: true } wheel &&
            wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            _scroll = Mathf.Max(0, _scroll + (wheel.ButtonIndex == MouseButton.WheelDown ? 3 : -3));
            QueueRedraw();
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            if (_clicks.Click(mb.Position)) QueueRedraw();
            return;
        }

        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        switch (key.PhysicalKeycode)
        {
            case Key.Escape or Key.Backspace: Leave(); return;
            case Key.Up or Key.W: Select(_selected - 1); break;
            case Key.Down or Key.S: Select(_selected + 1); break;
            case Key.R: Inbox.MarkAllRead(); break;
        }
        QueueRedraw();
    }

    private void Select(int index)
    {
        if (Inbox.Messages.Count == 0) return;
        _selected = Mathf.Clamp(index, 0, Inbox.Messages.Count - 1);
        Inbox.Messages[_selected].Read = true;
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);
        _clicks.Begin();

        Palette.BackButton(this, size, _clicks, Leave);
        Palette.Text(this, new Vector2(40f, 46f), "INBOX", 26, Palette.Ink);
        Palette.Text(this, new Vector2(40f, 68f),
            Inbox.Unread > 0
                ? $"{Inbox.Unread} unread of {Inbox.Messages.Count}  ·  R to mark all read"
                : $"{Inbox.Messages.Count} message{(Inbox.Messages.Count == 1 ? "" : "s")}",
            14, Inbox.Unread > 0 ? Palette.Highlight : Palette.InkDim);

        if (Inbox.Messages.Count == 0)
        {
            Palette.Text(this, new Vector2(40f, 130f),
                "Nothing yet. Your staff will write when they have something to say.",
                15, Palette.InkDim);
            return;
        }

        DrawList(size);
        DrawMessage(size);

        Palette.Text(this, new Vector2(40f, size.Y - 22f),
            "Up/Down to read  ·  click a message  ·  R marks all read  ·  Esc to go back",
            14, Palette.InkDim);
    }

    private void DrawList(Vector2 size)
    {
        var panel = new Rect2(new Vector2(40f, 100f), new Vector2(420f, size.Y - 150f));
        Palette.Panel3D(this, panel, Palette.Panel);

        float rowH = 44f;
        int fits = Mathf.Max(3, (int)((panel.Size.Y - 16f) / rowH));
        _scroll = Mathf.Clamp(_scroll, 0, Mathf.Max(0, Inbox.Messages.Count - fits));

        // Keep the one being read in view.
        if (_selected < _scroll) _scroll = _selected;
        else if (_selected >= _scroll + fits) _scroll = _selected - fits + 1;

        float y = panel.Position.Y + 10f;

        for (int i = _scroll; i < Inbox.Messages.Count && i < _scroll + fits; i++)
        {
            var msg = Inbox.Messages[i];
            bool on = i == _selected;
            var row = new Rect2(new Vector2(panel.Position.X + 6f, y), new Vector2(408f, rowH - 4f));

            if (on) DrawRect(row, Palette.PanelLight);

            // Unread carries a mark, which is the whole point of an inbox over a feed.
            if (!msg.Read)
                DrawRect(new Rect2(row.Position + new Vector2(4f, 8f), new Vector2(4f, 24f)),
                    Palette.Highlight);

            Palette.Text(this, row.Position + new Vector2(18f, 17f), msg.FromName, 12,
                TintFor(msg.From));
            Palette.Text(this, row.Position + new Vector2(18f, 33f), Trim(msg.Subject, 44), 13,
                msg.Read ? Palette.InkDim : Palette.Ink);
            Palette.Text(this, row.Position + new Vector2(row.Size.X - 60f, 17f),
                $"Yr {msg.Year}", 11, Palette.InkDim);

            int pick = i;
            _clicks.Add(row, () => { Select(pick); QueueRedraw(); });
            y += rowH;
        }

        if (Inbox.Messages.Count > fits)
            Palette.Text(this, new Vector2(panel.Position.X + 8f, panel.End.Y + 16f),
                $"{_scroll + 1}–{Mathf.Min(_scroll + fits, Inbox.Messages.Count)} " +
                $"of {Inbox.Messages.Count}  ·  scroll", 12, Palette.InkDim);
    }

    private void DrawMessage(Vector2 size)
    {
        var msg = Inbox.Messages[Mathf.Clamp(_selected, 0, Inbox.Messages.Count - 1)];
        var panel = new Rect2(new Vector2(480f, 100f),
            new Vector2(size.X - 520f, size.Y - 150f));
        Palette.Panel3D(this, panel, Palette.Panel);

        DrawRect(new Rect2(panel.Position, new Vector2(panel.Size.X, 4f)), TintFor(msg.From));

        Palette.Text(this, panel.Position + new Vector2(20f, 32f), msg.FromName, 13,
            TintFor(msg.From));
        Palette.Text(this, panel.Position + new Vector2(20f, 58f), msg.Subject, 19, Palette.Ink);

        if (msg.About != "")
            Palette.Text(this, panel.Position + new Vector2(panel.Size.X - 240f, 32f),
                $"re: {msg.About}", 12, Palette.InkDim);

        float y = panel.Position.Y + 96f;
        foreach (string para in msg.Body.Split('\n'))
        {
            if (para.Trim() == "") { y += 12f; continue; }

            foreach (string line in Wrap(para, panel.Size.X - 44f, 15))
            {
                Palette.Text(this, new Vector2(panel.Position.X + 20f, y), line, 15, Palette.Ink);
                y += 22f;
            }
        }
    }

    /// <summary>Breaks a paragraph to the panel's width, since nothing else here wraps text.</summary>
    private static string[] Wrap(string text, float width, int fontSize)
    {
        var lines = new System.Collections.Generic.List<string>();
        string line = "";

        foreach (string word in text.Split(' '))
        {
            string candidate = line == "" ? word : $"{line} {word}";
            if (Palette.TextWidth(candidate, fontSize) > width && line != "")
            {
                lines.Add(line);
                line = word;
            }
            else line = candidate;
        }

        if (line != "") lines.Add(line);
        return lines.ToArray();
    }

    private static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    private static Color TintFor(Sender from) => from switch
    {
        Sender.Owner => new Color("#e8c14a"),
        Sender.Scouting => new Color("#5fb3d0"),
        Sender.Pitching => new Color("#e07a3a"),
        Sender.Hitting => new Color("#6bbf6b"),
        Sender.Bench => new Color("#c48fd0"),
        Sender.Press => new Color("#cfd3da"),
        _ => Palette.InkDim,
    };
}
