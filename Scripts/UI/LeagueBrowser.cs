using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.UI;

/// <summary>An almanac of the whole league: pick a club on the left, read its full roster on the right.</summary>
public partial class LeagueBrowser : Control
{
    private int _selected;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        SetProcess(false);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        switch (key.PhysicalKeycode)
        {
            case Key.Escape or Key.Backspace:
                Game.Instance.GoTo("res://Scenes/MainMenu.tscn");
                return;
            case Key.Up or Key.W: _selected = Mathf.PosMod(_selected - 1, 32); break;
            case Key.Down or Key.S: _selected = Mathf.PosMod(_selected + 1, 32); break;
            case Key.Left or Key.A: _selected = Mathf.PosMod(_selected - 8, 32); break;
            case Key.Right or Key.D: _selected = Mathf.PosMod(_selected + 8, 32); break;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);

        Palette.Text(this, new Vector2(40f, 48f), "THE LEAGUE", 28, Palette.Ink);
        Palette.Text(this, new Vector2(40f, 72f),
            "32 clubs across the major-league map, plus Montreal and Nashville.", 15, Palette.InkDim);

        DrawTeamList(size);
        DrawRosterPanel(size, Teams.Get(_selected));

        Palette.Text(this, new Vector2(40f, size.Y - 24f),
            "Up/Down to browse  ·  Left/Right to jump a division  ·  Esc to go back", 15, Palette.InkDim);
    }

    private void DrawTeamList(Vector2 size)
    {
        float x = 40f;
        float y = 100f;
        Division lastDivision = (Division)(-1);
        League lastLeague = (League)(-1);

        for (int id = 0; id < 32; id++)
        {
            var team = Teams.Get(id);

            if (team.League != lastLeague || team.Division != lastDivision)
            {
                lastLeague = team.League;
                lastDivision = team.Division;
                y += 14f;
                Palette.Text(this, new Vector2(x, y),
                    Teams.DivisionName(team.League, team.Division).ToUpperInvariant(), 13, Palette.Highlight);
                y += 16f;
            }

            bool on = id == _selected;
            var row = new Rect2(new Vector2(x - 6f, y - 13f), new Vector2(330f, 19f));
            if (on) DrawRect(row, Palette.PanelLight);

            DrawRect(new Rect2(new Vector2(x, y - 11f), new Vector2(5f, 14f)), team.Primary);
            DrawRect(new Rect2(new Vector2(x + 6f, y - 11f), new Vector2(3f, 14f)), team.Secondary);

            Palette.Text(this, new Vector2(x + 16f, y), team.Abbrev, 13, on ? Palette.Highlight : Palette.InkDim);
            Palette.Text(this, new Vector2(x + 52f, y), team.FullName, 14, on ? Palette.Ink : Palette.InkDim);
            y += 19f;
        }
    }

    private void DrawRosterPanel(Vector2 size, TeamData team)
    {
        float x = 420f;
        var panel = new Rect2(new Vector2(x, 100f), new Vector2(size.X - x - 40f, size.Y - 150f));
        Palette.Panel3D(this, panel, Palette.Panel);

        DrawRect(new Rect2(panel.Position, new Vector2(panel.Size.X, 72f)), team.Primary);
        DrawRect(new Rect2(panel.Position + new Vector2(0f, 68f), new Vector2(panel.Size.X, 5f)), team.Secondary);

        Palette.Text(this, panel.Position + new Vector2(18f, 30f),
            $"{team.City}", 18, team.TextOnPrimary);
        Palette.Text(this, panel.Position + new Vector2(18f, 58f),
            team.Nickname.ToUpperInvariant(), 26, team.TextOnPrimary);
        Palette.Text(this, panel.Position + new Vector2(panel.Size.X - 90f, 44f),
            team.Abbrev, 22, team.TextOnPrimary);

        Palette.Text(this, panel.Position + new Vector2(18f, 96f), $"“{team.Motto}”", 14, Palette.Highlight);

        var roster = Game.Instance.League.RosterFor(team.Id);

        float y = panel.Position.Y + 128f;
        string[] headers = { "POS", "NAME", "#", "B/T", "CON", "POW", "SPD", "ARM", "FLD", "SIGNATURE" };
        float[] cols = { 18f, 60f, 190f, 224f, 274f, 320f, 366f, 412f, 458f, 510f };

        for (int i = 0; i < headers.Length; i++)
            Palette.Text(this, panel.Position + new Vector2(cols[i], y - panel.Position.Y), headers[i], 12,
                Palette.Highlight);
        y += 18f;

        foreach (var p in roster.Players.OrderBy(p => p.Position).ThenByDescending(p => p.Overall))
        {
            float ry = y - panel.Position.Y;
            Palette.Text(this, panel.Position + new Vector2(cols[0], ry), p.PositionText, 13, Palette.InkDim);
            Palette.Text(this, panel.Position + new Vector2(cols[1], ry), p.Name, 14, Palette.Ink);
            Palette.Text(this, panel.Position + new Vector2(cols[2], ry), p.Number.ToString(), 13, Palette.InkDim);
            Palette.Text(this, panel.Position + new Vector2(cols[3], ry),
                $"{(p.Bats == Handedness.Left ? "L" : "R")}/{(p.Throws == Handedness.Left ? "L" : "R")}",
                13, Palette.InkDim);

            // Qualified, because Control.Position shadows the enum inside this class.
            bool pitcher = p.Position == Data.Position.P;
            DrawStat(panel.Position + new Vector2(cols[4], ry), pitcher ? p.PitchPower : p.Contact, pitcher);
            DrawStat(panel.Position + new Vector2(cols[5], ry), pitcher ? p.PitchControl : p.Power, pitcher);
            DrawStat(panel.Position + new Vector2(cols[6], ry), pitcher ? p.Stamina : p.Speed, pitcher);
            DrawStat(panel.Position + new Vector2(cols[7], ry), p.Arm, false);
            DrawStat(panel.Position + new Vector2(cols[8], ry), p.Fielding, false);

            string trait = p.Special != Special.None
                ? $"{p.ArchetypeText} · {p.SpecialText}"
                : p.ArchetypeText;
            Palette.Text(this, panel.Position + new Vector2(cols[9], ry), trait, 12,
                p.Special != Special.None ? team.Secondary : Palette.InkDim);

            y += 19f;
        }

        Palette.Text(this, panel.Position + new Vector2(cols[4], y - panel.Position.Y + 12f),
            "Pitcher columns read VEL / CMD / STA", 12, Palette.InkDim);
    }

    private void DrawStat(Vector2 at, int value, bool pitcherColumn)
    {
        var color = value >= 8 ? new Color("#7ddb8a")
            : value >= 6 ? Palette.Ink
            : value >= 4 ? Palette.InkDim
            : new Color("#c47b7b");
        Palette.Text(this, at, value.ToString(), 13, color);
        if (pitcherColumn) { /* same layout, different meaning; noted under the table */ }
    }
}
