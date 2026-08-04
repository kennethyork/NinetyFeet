using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.Season;

namespace SandlotSlugfest.UI;

/// <summary>
/// The draft board. Prospects on the left ranked by the scouts, your club's needs on the right,
/// and a running log of who went where. Worst record picks first.
/// </summary>
public partial class DraftScreen : Control
{
    private readonly ClickMap _clicks = new();
    private SeasonState _season;
    private Draft _draft;
    private int _scroll;
    private string _notice = "";

    private const int Rows = 16;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _season = Game.Instance.League;
        _draft = _season.Draft;

        if (_draft.Order.Count == 0) _draft.Begin(_season, _season.LeagueSeed);

        // Let the clubs ahead of us pick before the board is first shown.
        var made = _draft.RunToUser(_season);
        if (made.Count > 0) _notice = $"{made.Count} picks made before you were on the clock.";
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true } mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp) { _scroll = Mathf.Max(0, _scroll - 3); QueueRedraw(); return; }
            if (mb.ButtonIndex == MouseButton.WheelDown) { _scroll += 3; QueueRedraw(); return; }
            if (mb.ButtonIndex == MouseButton.Left && _clicks.Click(mb.Position)) QueueRedraw();
            return;
        }

        if (@event is InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape })
            Game.Instance.GoTo("res://Scenes/Season.tscn");
    }

    private void Pick(PlayerData p)
    {
        if (_draft.Complete || _draft.OnTheClock != _season.UserTeamId) return;

        _draft.Take(_season, p);
        _notice = $"You take {p.Name} — {p.PositionText}, {p.PotentialGrade.ToLowerInvariant()}.";

        var made = _draft.RunToUser(_season);
        if (made.Count > 0) _notice += $"  ({made.Count} picks followed.)";

        Game.Instance.SaveLeague();
    }

    private void SimRest()
    {
        int guard = 0;
        while (!_draft.Complete && guard++ < 200) _draft.AutoPick(_season);
        Game.Instance.SaveLeague();
        _notice = "Draft complete.";
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);
        _clicks.Begin();

        bool ours = !_draft.Complete && _draft.OnTheClock == _season.UserTeamId;
        var club = Teams.Get(_season.UserTeamId);

        Palette.Text(this, new Vector2(40f, 46f), "AMATEUR DRAFT", 26, Palette.Ink);
        Palette.Text(this, new Vector2(40f, 70f),
            _draft.Complete
                ? "Every pick is in."
                : $"Round {_draft.CurrentRound} of {Draft.Rounds}   ·   pick " +
                  $"{_draft.Current + 1} of {_draft.Order.Count}   ·   " +
                  $"on the clock: {Teams.Get(_draft.OnTheClock).FullName}",
            15, ours ? Palette.Highlight : Palette.InkDim);

        Palette.BackButton(this, size, _clicks, () => Game.Instance.GoTo("res://Scenes/Season.tscn"));

        if (!_draft.Complete)
        {
            var sim = new Rect2(new Vector2(size.X - 260f, 20f), new Vector2(130f, 32f));
            Palette.Panel3D(this, sim, Palette.PanelLight);
            Palette.TextCentered(this, sim.Position + sim.Size * 0.5f, "SIM REST", 14, Palette.Ink);
            _clicks.Add(sim, SimRest);
        }

        DrawBoard(size, ours, club);
        DrawLog(size);

        if (!string.IsNullOrEmpty(_notice))
            Palette.Text(this, new Vector2(40f, size.Y - 24f), _notice, 14, Palette.Highlight);
    }

    private void DrawBoard(Vector2 size, bool ours, TeamData club)
    {
        var roster = _season.RosterFor(club.Id);
        var ranked = _draft.Available
            .OrderByDescending(p => Draft.ScoutValue(p, roster))
            .ToList();

        _scroll = Mathf.Clamp(_scroll, 0, Mathf.Max(0, ranked.Count - Rows));

        var panel = new Rect2(new Vector2(40f, 104f), new Vector2(size.X - 400f, size.Y - 160f));
        Palette.Panel3D(this, panel, Palette.Panel);

        float y = panel.Position.Y + 28f;
        string[] head = { "POS", "NAME", "AGE", "NOW", "CEIL", "OUTLOOK", "SIGNATURE" };
        float[] cols = { 16f, 60f, 220f, 262f, 306f, 352f, 520f };
        for (int i = 0; i < head.Length; i++)
            Palette.Text(this, panel.Position + new Vector2(cols[i], y - panel.Position.Y), head[i],
                12, Palette.Highlight);
        y += 20f;

        for (int i = _scroll; i < Mathf.Min(_scroll + Rows, ranked.Count); i++)
        {
            var p = ranked[i];
            var row = new Rect2(new Vector2(panel.Position.X + 8f, y - 13f),
                new Vector2(panel.Size.X - 16f, 21f));

            if (ours)
            {
                var target = p;
                _clicks.Add(row, () => Pick(target));
            }

            Palette.Text(this, new Vector2(panel.Position.X + cols[0], y), p.PositionText, 12, Palette.InkDim);
            Palette.Text(this, new Vector2(panel.Position.X + cols[1], y), p.Name, 14, Palette.Ink);
            Palette.Text(this, new Vector2(panel.Position.X + cols[2], y), p.Age.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(panel.Position.X + cols[3], y), p.Overall.ToString(), 13, Palette.Ink);

            // Ceiling in green when there is real growth left in him.
            var ceilTint = p.Upside >= 3 ? new Color("#7ddb8a") : p.Upside >= 1 ? Palette.Ink : Palette.InkDim;
            Palette.Text(this, new Vector2(panel.Position.X + cols[4], y),
                $"{p.Potential}{(p.Upside > 0 ? $" (+{p.Upside})" : "")}", 13, ceilTint);

            Palette.Text(this, new Vector2(panel.Position.X + cols[5], y), p.PotentialGrade, 12, Palette.InkDim);
            if (p.Special != Special.None)
                Palette.Text(this, new Vector2(panel.Position.X + cols[6], y), p.SpecialText, 12, club.Secondary);

            y += 21f;
        }

        Palette.Text(this, new Vector2(panel.Position.X + 16f, panel.End.Y - 12f),
            ours ? "Click a prospect to draft him  ·  scroll for more"
                 : "Waiting on the clubs ahead of you  ·  scroll to scout the board",
            12, ours ? Palette.Highlight : Palette.InkDim);
    }

    private void DrawLog(Vector2 size)
    {
        var panel = new Rect2(new Vector2(size.X - 340f, 104f), new Vector2(300f, size.Y - 160f));
        Palette.Panel3D(this, panel, Palette.Panel);
        Palette.Text(this, panel.Position + new Vector2(14f, 26f), "PICKS", 13, Palette.Highlight);

        float y = panel.Position.Y + 50f;
        int shown = 0;
        for (int i = _draft.Picks.Count - 1; i >= 0 && shown < 22; i--, shown++)
        {
            var pick = _draft.Picks[i];
            var team = Teams.Get(pick.TeamId);
            bool mine = pick.TeamId == _season.UserTeamId;

            DrawRect(new Rect2(new Vector2(panel.Position.X + 14f, y - 10f), new Vector2(4f, 13f)),
                team.Primary);
            Palette.Text(this, new Vector2(panel.Position.X + 24f, y),
                $"{pick.Overall}. {team.Abbrev}", 12, mine ? Palette.Highlight : Palette.InkDim);
            Palette.Text(this, new Vector2(panel.Position.X + 96f, y),
                $"{pick.Player.PositionText} {pick.Player.LastName}", 12,
                mine ? Palette.Ink : Palette.InkDim);
            y += 18f;
        }

        if (_draft.Picks.Count == 0)
            Palette.Text(this, panel.Position + new Vector2(14f, 52f), "No picks yet.", 12, Palette.InkDim);
    }
}
