using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.Season;

namespace SandlotSlugfest.UI;

/// <summary>
/// Pick the club you run for the season. This was genuinely missing — the user's club was only
/// ever set inside StartNew, so you were handed whichever team the defaults happened to name.
/// </summary>
public partial class ClubSelect : Control
{
    private readonly ClickMap _clicks = new();
    private int _cursor;
    private int _length;         // index into SeasonLengths; a full 162 by default
    private float _time;

    private static readonly (string Label, int Games)[] SeasonLengths =
    {
        ("Full (162 games) — a real season", Schedule.FullSeason),
        ("Half (81 games)", Schedule.MediumSeason),
        ("Short (33 games)", Schedule.ShortSeason),
    };

    private const int Columns = 4;
    private const int Rows = 8;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(true);
        _cursor = Game.Instance.League?.UserTeamId ?? 0;
    }

    public override void _Process(double delta) { _time += (float)delta; QueueRedraw(); }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion m) { _clicks.Hover(m.Position); return; }
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            _clicks.Click(mb.Position);
            return;
        }

        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        int col = _cursor / Rows, row = _cursor % Rows;
        switch (key.PhysicalKeycode)
        {
            case Key.Escape: Game.Instance.GoTo("res://Scenes/MainMenu.tscn"); return;
            case Key.Left or Key.A: col = Mathf.PosMod(col - 1, Columns); break;
            case Key.Right or Key.D: col = Mathf.PosMod(col + 1, Columns); break;
            case Key.Up or Key.W: row = Mathf.PosMod(row - 1, Rows); break;
            case Key.Down or Key.S: row = Mathf.PosMod(row + 1, Rows); break;
            case Key.Enter or Key.KpEnter or Key.Space: Start(); return;
        }
        _cursor = col * Rows + row;
    }

    private void Start()
    {
        var g = Game.Instance;
        g.SeasonLength = SeasonLengths[_length].Games;
        g.NewSeason(_cursor, SeasonLengths[_length].Games);
        Settings.SaveResumeMode(Settings.ResumeMode.League);
        g.HomeTeamId = _cursor;
        g.GoTo("res://Scenes/Season.tscn");
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);
        _clicks.Begin();

        Palette.Text(this, new Vector2(40f, 48f), "CHOOSE YOUR CLUB", 28, Palette.Ink);
        Palette.Text(this, new Vector2(40f, 72f),
            "You will run this club for the whole season — batting, pitching, trades and the draft.",
            14, Palette.InkDim);

        Palette.BackButton(this, size, _clicks, () => Game.Instance.GoTo("res://Scenes/MainMenu.tscn"));

        DrawGrid(size);
        DrawPreview(size, Teams.Get(_cursor));
    }

    private void DrawGrid(Vector2 size)
    {
        const float cellW = 176f, cellH = 52f, gapX = 10f, gapY = 6f;
        float startX = 40f, startY = 118f;

        for (int col = 0; col < Columns; col++)
        {
            var first = Teams.Get(col * Rows);
            Palette.Text(this, new Vector2(startX + col * (cellW + gapX), startY - 12f),
                $"{(first.League == League.American ? "AL" : "NL")} " +
                $"{first.Division.ToString().ToUpperInvariant()}", 13, Palette.Highlight);

            for (int row = 0; row < Rows; row++)
            {
                int id = col * Rows + row;
                var team = Teams.Get(id);
                var rect = new Rect2(
                    new Vector2(startX + col * (cellW + gapX), startY + row * (cellH + gapY)),
                    new Vector2(cellW, cellH));

                Palette.Panel3D(this, rect, team.Primary);
                DrawRect(new Rect2(rect.Position + new Vector2(0f, rect.Size.Y - 5f),
                    new Vector2(rect.Size.X, 5f)), team.Secondary);

                if (id == _cursor)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 6f);
                    DrawRect(rect, Palette.Highlight.Lerp(Palette.Ink, pulse), false, 3f);
                }

                var ink = team.TextOnPrimary;
                Palette.Text(this, rect.Position + new Vector2(10f, 21f), team.Abbrev, 17, ink);
                Palette.Text(this, rect.Position + new Vector2(52f, 21f), team.City, 14, ink);
                Palette.Text(this, rect.Position + new Vector2(52f, 38f), team.Nickname, 16, ink);

                int pick = id;
                _clicks.Add(rect, () => { _cursor = pick; Start(); }, () => _cursor = pick);
            }
        }
    }

    private void DrawPreview(Vector2 size, TeamData team)
    {
        float x = size.X - 340f;
        var panel = new Rect2(new Vector2(x, 118f), new Vector2(300f, size.Y - 180f));
        Palette.Panel3D(this, panel, Palette.Panel);

        DrawRect(new Rect2(panel.Position, new Vector2(panel.Size.X, 64f)), team.Primary);
        DrawRect(new Rect2(panel.Position + new Vector2(0f, 60f), new Vector2(panel.Size.X, 4f)),
            team.Secondary);
        Palette.Text(this, panel.Position + new Vector2(14f, 26f), team.City, 16, team.TextOnPrimary);
        Palette.Text(this, panel.Position + new Vector2(14f, 50f), team.Nickname.ToUpperInvariant(),
            22, team.TextOnPrimary);

        var park = Stadiums.For(team.Id);
        float y = panel.Position.Y + 86f;
        Palette.Text(this, new Vector2(panel.Position.X + 14f, y), "HOME PARK", 12, Palette.Highlight);
        Palette.Text(this, new Vector2(panel.Position.X + 14f, y + 18f), park.Name, 15, Palette.Ink);
        DrawMultilineString(Palette.Font, new Vector2(panel.Position.X + 14f, y + 38f), park.Quirk,
            HorizontalAlignment.Left, panel.Size.X - 28f, 12, 3, Palette.InkDim);

        Palette.Text(this, new Vector2(panel.Position.X + 14f, y + 92f),
            $"{(int)park.ShortestFence}–{(int)park.DeepestFence} ft   ·   " +
            $"tallest wall {(int)park.TallestWall} ft", 12, Palette.InkDim);

        // Season length, chosen here so the club and the commitment are picked together.
        y = panel.Position.Y + 240f;
        Palette.Text(this, new Vector2(panel.Position.X + 14f, y), "SEASON LENGTH", 12, Palette.Highlight);
        for (int i = 0; i < SeasonLengths.Length; i++)
        {
            var rect = new Rect2(new Vector2(panel.Position.X + 14f, y + 12f + i * 34f),
                new Vector2(panel.Size.X - 28f, 28f));
            bool on = i == _length;
            Palette.Panel3D(this, rect, on ? Palette.Highlight.Darkened(0.2f) : Palette.PanelLight);
            Palette.TextCentered(this, rect.Position + rect.Size * 0.5f, SeasonLengths[i].Label, 13,
                on ? Palette.Night : Palette.Ink);
            int pick = i;
            _clicks.Add(rect, () => _length = pick);
        }

        // Which mode this is, so it is never ambiguous what you are about to start.
        y += 12f + SeasonLengths.Length * 34f + 10f;
        bool dynasty = Game.Instance.ManagerOnly;
        Palette.Text(this, new Vector2(panel.Position.X + 14f, y),
            dynasty ? "DYNASTY" : "SEASON", 12, Palette.Highlight);
        Palette.Text(this, new Vector2(panel.Position.X + 14f, y + 20f),
            dynasty
                ? "You run the club. Every game is simulated,\nand the franchise runs year after year."
                : "You play your club's games yourself,\none season at a time.",
            11, Palette.InkDim);

        var go = new Rect2(new Vector2(panel.Position.X + 14f, panel.End.Y - 58f),
            new Vector2(panel.Size.X - 28f, 44f));
        Palette.Panel3D(this, go, Palette.Highlight);
        Palette.TextCentered(this, go.Position + go.Size * 0.5f,
            $"RUN THE {team.Nickname.ToUpperInvariant()}", 16, Palette.Night);
        _clicks.Add(go, Start);
    }
}
