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

        // Without this the Control swallows every mouse event and _UnhandledInput never sees a
        // click, so nothing on the screen is clickable — including the back button.
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(false);

        TouchScroll.Handler = (px, _) =>
        {
            _touchAccum += px;
            const float row = 16f;
            int step = (int)(_touchAccum / row);
            if (step == 0) return;
            _touchAccum -= step * row;
            _rosterScroll = Mathf.Max(0, _rosterScroll + step);
            QueueRedraw();
        };
    }

    public override void _ExitTree() => TouchScroll.Handler = null;

    private float _touchAccum;

    public override void _UnhandledInput(InputEvent @event)
    {
        // A back button nothing can click is decoration.
        if (@event is InputEventMouseMotion m) { if (_clicks.Hover(m.Position)) QueueRedraw(); return; }
        if (@event is InputEventJoypadButton && _clicks.Controller(@event, Leave))
        { QueueRedraw(); return; }
        if (@event is InputEventMouseButton { Pressed: true } wheel &&
            wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            _rosterScroll = Mathf.Max(0,
                _rosterScroll + (wheel.ButtonIndex == MouseButton.WheelDown ? 3 : -3));
            QueueRedraw();
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            if (_clicks.Click(mb.Position)) QueueRedraw();
            return;
        }

        if (!ControllerNav.TryPressedKey(@event, out Key pressed)) return;

        switch (pressed)
        {
            case Key.Escape or Key.Backspace:
                Leave();
                return;
            // Stepping through the league rather than counting to thirty-two: in a smaller
            // league the ids are not contiguous, so arithmetic on one lands on a club that is
            // not playing this season.
            case Key.Up or Key.W: _selected = Teams.Step(_selected, -1).Id; _rosterScroll = 0; break;
            case Key.Down or Key.S: _selected = Teams.Step(_selected, 1).Id; _rosterScroll = 0; break;
            case Key.Left or Key.A: _selected = Teams.Step(_selected, -4).Id; _rosterScroll = 0; break;
            case Key.Right or Key.D: _selected = Teams.Step(_selected, 4).Id; _rosterScroll = 0; break;
        }
        QueueRedraw();
    }

    /// <summary>
    /// Back to the season, not the title screen — and there is a button for it now.
    ///
    /// This screen had no back button at all and Escape went to the main menu, so reaching it from
    /// the season hub was a one-way trip: the only way back to your club was through the front
    /// door again.
    /// </summary>
    private void Leave() => Game.Instance.GoTo(Game.Instance.League != null
        ? "res://Scenes/Season.tscn"
        : "res://Scenes/MainMenu.tscn");

    private readonly ClickMap _clicks = new();

    /// <summary>How far down the selected club's roster we have scrolled.</summary>
    private int _rosterScroll;

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);
        _clicks.Begin();
        Palette.BackButton(this, size, _clicks, Leave);

        Palette.Text(this, new Vector2(Palette.SafeLeft(size), Palette.SafeTop(size, 48f)), "THE LEAGUE", 28, Palette.Ink);
        Palette.Text(this, new Vector2(Palette.SafeLeft(size), Palette.SafeTop(size) + 26f),
            $"{Teams.All.Count} clubs across the major-league map, plus Montreal and Nashville.",
            15, Palette.InkDim);

        DrawTeamList(size);
        DrawRosterPanel(size, Teams.Get(_selected));

        Palette.Text(this, new Vector2(Palette.SafeLeft(size), Palette.SafeBottom(size, 24f)),
            "Up/Down to browse  ·  Left/Right to jump a division  ·  Esc to go back", 15, Palette.InkDim);
        _clicks.DrawFocus(this, Palette.Highlight);
    }

    private void DrawTeamList(Vector2 size)
    {
        float x = 40f;
        float y = 100f;
        Division lastDivision = (Division)(-1);
        League lastLeague = (League)(-1);

        // Mobile bumps the row height to a comfortable tap target. Thirty-two clubs at 30 px each
        // is 960 px which will run past the bottom on a phone — the kinetic touch scroller
        // registered above lets the finger drag the list normally.
        bool touch = Gameplay.TouchControls.MobileLayout;
        float rowStep = touch ? 30f : 15f;
        float rowH = touch ? 28f : 16f;

        foreach (var team in Teams.All)
        {

            if (team.League != lastLeague || team.Division != lastDivision)
            {
                lastLeague = team.League;
                lastDivision = team.Division;
                y += 10f;
                Palette.Text(this, new Vector2(x, y),
                    Teams.DivisionName(team.League, team.Division).ToUpperInvariant(), 12, Palette.Highlight);
                y += 14f;
            }

            bool on = team.Id == _selected;
            var row = new Rect2(new Vector2(x - 6f, y - 12f), new Vector2(330f, rowH));
            if (on) DrawRect(row, Palette.PanelLight);

            DrawRect(new Rect2(new Vector2(x, y - 10f), new Vector2(5f, 12f)), team.Primary);
            DrawRect(new Rect2(new Vector2(x + 6f, y - 10f), new Vector2(3f, 12f)), team.Secondary);

            Palette.Text(this, new Vector2(x + 16f, y), team.Abbrev, 12, on ? Palette.Highlight : Palette.InkDim);
            Palette.Text(this, new Vector2(x + 52f, y), team.FullName, 13, on ? Palette.Ink : Palette.InkDim);

            // Tapping a row picks that team. Was keyboard-only, which meant a phone player could
            // never see any club except the one the screen loaded on.
            int picked = team.Id;
            _clicks.Add(row, () => { _selected = picked; _rosterScroll = 0; QueueRedraw(); });

            // Thirty-two clubs and four division headings at the old spacing needed 828 pixels in
            // a 720-pixel screen, so the bottom of the National League West was printed straight
            // through the footer. Desktop still fits; mobile scrolls.
            y += rowStep;
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

        // A club carries twenty-six to twenty-nine men and about twenty-three rows fit, so the
        // bottom of every roster in the league was simply unreachable.
        var men = roster.Players.OrderBy(p => p.Position).ThenByDescending(p => p.Overall).ToList();
        int fits = Mathf.Max(4, (int)((panel.End.Y - 16f - y) / 19f));
        _rosterScroll = Mathf.Clamp(_rosterScroll, 0, Mathf.Max(0, men.Count - fits));

        if (men.Count > fits)
            Palette.Text(this, panel.Position + new Vector2(panel.Size.X - 210f, 96f),
                $"{_rosterScroll + 1}–{Mathf.Min(_rosterScroll + fits, men.Count)} of {men.Count}" +
                "  ·  scroll", 12, Palette.InkDim);

        foreach (var p in men.Skip(_rosterScroll).Take(fits))
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
