using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.Season;
using SandlotSlugfest.Stats;

namespace SandlotSlugfest.UI;

/// <summary>
/// One player's career: where he is, what he has done, and what the club thinks of him.
///
/// You do not run the club here. You are told where you are playing and you go and play, which is
/// the entire difference between this and every other mode in the game.
/// </summary>
public partial class CareerScreen : Control
{
    private CareerState _career;
    private readonly ClickMap _clicks = new();

    private bool _creating;
    private int _build;
    private int _bats;
    private string _first = "";
    private string _last = "";
    private int _nameField;          // 0 = first, 1 = last
    private string _notice = "";
    private float _noticeTimer;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _career = CareerState.Load();
        _creating = _career == null;
        if (_creating) { _first = "Ace"; _last = "Ackley"; }
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (_noticeTimer <= 0f) return;
        _noticeTimer -= (float)delta;
        if (_noticeTimer <= 0f) { _notice = ""; QueueRedraw(); }
    }

    private void Say(string m) { _notice = m; _noticeTimer = 4f; QueueRedraw(); }

    private void Leave()
    {
        Palette.HideSoftKeyboard();
        Game.Instance.GoTo("res://Scenes/MainMenu.tscn");
    }

    /// <summary>Opens Android's on-screen keyboard on the currently-focused name field.</summary>
    private void OpenKeyboard()
    {
        string existing = _nameField == 0 ? _first : _last;
        int cap = _nameField == 0 ? 14 : 16;
        Palette.ShowSoftKeyboard(existing, cap);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mm) { if (_clicks.Hover(mm.Position)) QueueRedraw(); return; }
        if (@event is InputEventJoypadButton && _clicks.Controller(@event, Leave))
        { QueueRedraw(); return; }
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            if (_clicks.Click(mb.Position)) QueueRedraw();
            return;
        }

        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        if (_creating && TypeName(key)) { QueueRedraw(); return; }

        switch (key.PhysicalKeycode)
        {
            case Key.Escape: Leave(); return;
            case Key.Up or Key.W:
                if (_creating) _build = Mathf.PosMod(_build - 1, CareerEngine.Builds.Length);
                break;
            case Key.Down or Key.S:
                if (_creating) _build = Mathf.PosMod(_build + 1, CareerEngine.Builds.Length);
                break;
        }
        QueueRedraw();
    }

    /// <summary>Typing a name. Backspace deletes, Tab moves between the two fields.</summary>
    private bool TypeName(InputEventKey key)
    {
        if (key.PhysicalKeycode == Key.Tab) { _nameField = 1 - _nameField; return true; }

        if (key.PhysicalKeycode == Key.Backspace)
        {
            if (_nameField == 0 && _first.Length > 0) _first = _first[..^1];
            else if (_nameField == 1 && _last.Length > 0) _last = _last[..^1];
            return true;
        }

        long unicode = key.Unicode;
        if (unicode < 32 || unicode > 126) return false;

        char ch = (char)unicode;
        if (!char.IsLetter(ch) && ch != '\'' && ch != '-' && ch != ' ') return false;

        if (_nameField == 0 && _first.Length < 14) _first += ch;
        else if (_nameField == 1 && _last.Length < 16) _last += ch;
        return true;
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);
        _clicks.Begin();

        Palette.BackButton(this, size, _clicks, Leave);

        if (_creating) DrawCreate(size);
        else DrawCareer(size);

        if (_notice != "")
            Palette.Text(this, new Vector2(Palette.SafeLeft(size), Palette.SafeBottom(size, 44f)), _notice, 14, Palette.Highlight);

        _clicks.DrawFocus(this, Palette.Highlight);
    }

    // -----------------------------------------------------------------------

    private void DrawCreate(Vector2 size)
    {
        Palette.Text(this, new Vector2(Palette.SafeLeft(size), Palette.SafeTop(size)), "START A CAREER", 26, Palette.Ink);
        Palette.Text(this, new Vector2(Palette.SafeLeft(size), Palette.SafeTop(size) + 22f),
            "One player. You do not pick where you play — you are drafted, and you earn the rest.",
            14, Palette.InkDim);

        float y = 118f;
        Palette.Text(this, new Vector2(40f, y), "NAME", 13, Palette.Highlight);
        y += 26f;

        DrawField(new Rect2(new Vector2(40f, y), new Vector2(230f, 34f)), _first, "First",
            _nameField == 0, () => _nameField = 0);
        DrawField(new Rect2(new Vector2(282f, y), new Vector2(280f, 34f)), _last, "Last",
            _nameField == 1, () => _nameField = 1);

        // Which side he stands on.
        float bx = 590f;
        for (int i = 0; i < 3; i++)
        {
            string label = i switch { 0 => "BATS R", 1 => "BATS L", _ => "SWITCH" };
            var rect = new Rect2(new Vector2(bx, y), new Vector2(96f, 34f));
            Palette.Panel3D(this, rect, _bats == i ? Palette.Highlight.Darkened(0.2f) : Palette.Panel);
            Palette.TextCentered(this, rect.Position + rect.Size * 0.5f, label, 12,
                _bats == i ? Palette.Night : Palette.InkDim);

            int pick = i;
            _clicks.Add(rect, () => { _bats = pick; QueueRedraw(); });
            bx += 102f;
        }

        // Clear of the name row: the fields are 34 tall, so anything less than this sits on them.
        y += 68f;
        Palette.Text(this, new Vector2(40f, y), "WHAT KIND OF PLAYER", 13, Palette.Highlight);
        y += 26f;

        for (int i = 0; i < CareerEngine.Builds.Length; i++)
        {
            var b = CareerEngine.Builds[i];
            bool on = i == _build;
            var rect = new Rect2(new Vector2(40f, y), new Vector2(size.X - 80f, 56f));

            Palette.Panel3D(this, rect, on ? Palette.PanelLight : Palette.Panel);
            Palette.Text(this, rect.Position + new Vector2(18f, 24f), b.Name, 16,
                on ? Palette.Ink : Palette.InkDim);
            Palette.Text(this, rect.Position + new Vector2(18f, 44f), b.Blurb, 12, Palette.InkDim);
            Palette.Text(this, rect.Position + new Vector2(rect.Size.X - 300f, 24f),
                $"{PlayerData.PositionLabel(b.Position)}  ·  ceiling {b.Ceiling}", 13,
                Palette.Highlight);

            int pick = i;
            _clicks.Add(rect, () => { _build = pick; QueueRedraw(); });
            y += 62f;
        }

        var start = new Rect2(new Vector2(size.X - 280f, size.Y - 96f), new Vector2(240f, 44f));
        Palette.Panel3D(this, start, Palette.Highlight.Darkened(0.2f));
        Palette.TextCentered(this, start.Position + start.Size * 0.5f, "ENTER THE DRAFT", 16,
            Palette.Night);
        _clicks.Add(start, Begin);

        Palette.Text(this, new Vector2(Palette.SafeLeft(size), Palette.SafeBottom(size, 22f)),
            "Click a field and type  ·  Tab switches fields  ·  Up/Down picks a build", 13,
            Palette.InkDim);
    }

    private void DrawField(Rect2 rect, string value, string placeholder, bool active,
        System.Action focus)
    {
        Palette.Panel3D(this, rect, active ? Palette.PanelLight : Palette.Panel);
        Palette.Text(this, rect.Position + new Vector2(12f, 23f),
            value == "" ? placeholder : value, 15,
            value == "" ? Palette.InkDim : Palette.Ink);

        if (active)
            DrawRect(new Rect2(rect.Position + new Vector2(12f + Palette.TextWidth(value, 15), 9f),
                new Vector2(2f, 18f)), Palette.Highlight);

        _clicks.Add(rect, () => { focus(); OpenKeyboard(); QueueRedraw(); });
    }

    private void Begin()
    {
        if (_first.Trim() == "" || _last.Trim() == "") { Say("He needs a name."); return; }

        var g = Game.Instance;
        var league = g.League;
        if (league == null) { Say("No league loaded."); return; }

        // Drafted by somebody, not chosen. That is the point.
        var rng = new Rng((int)Time.GetTicksMsec());
        int club = rng.Range(0, Teams.All.Count);

        _career = CareerEngine.Create(_first.Trim(), _last.Trim(), CareerEngine.Builds[_build],
            (Handedness)_bats, club, (int)Time.GetTicksMsec());
        _career.Save();
        _creating = false;
        Say($"Drafted by {Teams.Get(club).FullName}.");
    }

    // -----------------------------------------------------------------------

    private void DrawCareer(Vector2 size)
    {
        var c = _career;
        var p = c.Player;

        Palette.Text(this, new Vector2(Palette.SafeLeft(size), Palette.SafeTop(size)), c.Name.ToUpperInvariant(), 26, Palette.Ink);
        Palette.Text(this, new Vector2(Palette.SafeLeft(size), Palette.SafeTop(size) + 22f),
            $"{PlayerData.PositionLabel(c.Position)} · bats {Platoon.Letter(c.Bats)} · " +
            $"age {c.Age} · year {c.Year} · {c.Where}", 14, Palette.InkDim);

        float y = 118f;
        Palette.Text(this, new Vector2(40f, y), "RATINGS", 13, Palette.Highlight);
        y += 24f;

        string ratings = c.IsPitcher
            ? $"VEL {p.PitchPower}   CMD {p.PitchControl}   STA {p.Stamina}   FLD {p.Fielding}"
            : $"CON {p.Contact}   POW {p.Power}   SPD {p.Speed}   ARM {p.Arm}   FLD {p.Fielding}";
        Palette.Text(this, new Vector2(44f, y), ratings, 15, Palette.Ink);
        Palette.Text(this, new Vector2(560f, y),
            $"OVERALL {p.Overall}   CEILING {p.Potential}", 15, Palette.Highlight);

        y += 34f;
        Palette.Text(this, new Vector2(44f, y), CareerEngine.Standing(c), 13,
            c.Retired ? Palette.Warning : Palette.InkDim);

        // The lines.
        y += 40f;
        Palette.Text(this, new Vector2(40f, y), "THIS SEASON", 13, Palette.Highlight);
        Palette.Text(this, new Vector2(520f, y), "CAREER", 13, Palette.Highlight);
        y += 24f;

        Line(40f, y, c.Season);
        Line(520f, y, c.Career);

        y += 96f;

        // Play, or end the year.
        if (!c.Retired)
        {
            bool seasonOver = c.GamesThisYear >= CareerState.SeasonLength;

            var play = new Rect2(new Vector2(40f, y), new Vector2(240f, 44f));
            Palette.Panel3D(this, play, seasonOver ? Palette.Panel : Palette.Highlight.Darkened(0.2f));
            Palette.TextCentered(this, play.Position + play.Size * 0.5f,
                seasonOver ? "SEASON OVER" : "PLAY A GAME", 15,
                seasonOver ? Palette.InkDim : Palette.Night);
            if (!seasonOver) _clicks.Add(play, PlayGame);

            var sim = new Rect2(new Vector2(292f, y), new Vector2(200f, 44f));
            Palette.Panel3D(this, sim, Palette.PanelLight);
            Palette.TextCentered(this, sim.Position + sim.Size * 0.5f,
                seasonOver ? "END THE SEASON" : "SIT THIS ONE OUT", 14, Palette.Ink);
            _clicks.Add(sim, seasonOver ? EndSeason : SimGame);

            Palette.Text(this, new Vector2(510f, y + 26f),
                $"{c.GamesThisYear}/{CareerState.SeasonLength} games this year", 13,
                Palette.InkDim);
        }

        // The journal.
        y += 66f;
        Palette.Text(this, new Vector2(40f, y), "THE ORGANISATION SAYS", 13, Palette.Highlight);
        y += 24f;

        foreach (string line in c.Journal.Take(9))
        {
            Palette.Text(this, new Vector2(44f, y), line, 13, Palette.InkDim);
            y += 19f;
        }

        var scrap = new Rect2(new Vector2(size.X - 200f, size.Y - 60f), new Vector2(160f, 30f));
        Palette.Panel3D(this, scrap, Palette.Panel);
        Palette.TextCentered(this, scrap.Position + scrap.Size * 0.5f, "START OVER", 12,
            Palette.Warning);
        _clicks.Add(scrap, () =>
        {
            CareerState.Delete();
            _career = null;
            _creating = true;
            _first = _last = "";
            QueueRedraw();
        });
    }

    private void Line(float x, float y, BattingLine b)
    {
        Palette.Text(this, new Vector2(x + 4f, y),
            $"G {b.Games}   AB {b.AtBats}   H {b.Hits}   HR {b.HomeRuns}   RBI {b.RunsBattedIn}",
            14, Palette.Ink);
        Palette.Text(this, new Vector2(x + 4f, y + 22f),
            $"AVG {BattingLine.Rate(b.Average)}   OBP {BattingLine.Rate(b.OnBase)}   " +
            $"SLG {BattingLine.Rate(b.Slugging)}   OPS {BattingLine.Rate(b.Ops)}",
            14, Palette.InkDim);
        Palette.Text(this, new Vector2(x + 4f, y + 44f),
            $"BB {b.Walks}   K {b.Strikeouts}   SB {b.StolenBases}", 14, Palette.InkDim);
    }

    // -----------------------------------------------------------------------

    private void PlayGame()
    {
        var g = Game.Instance;
        var mine = CareerEngine.SideFor(_career, g.League);
        var theirs = CareerEngine.OpponentFor(_career, g.League,
            _career.Year * 733 + _career.GamesThisYear * 29 + _career.TeamId);

        if (mine == null || theirs == null) { Say("No side could be put together."); return; }

        _career.Save();

        // He is always the visitor, so his half of the inning comes first and a short session
        // still contains his at-bats.
        g.PendingSeasonGame = null;
        g.CardClubRoster = null;
        g.PendingMoment = null;
        g.FarmAwayRoster = mine;
        g.FarmHomeRoster = theirs;
        g.FarmLevelName = _career.Level == null ? "The majors" : Farm.Name(_career.Level.Value);
        g.FarmReplacing = null;
        g.CareerPlayer = _career.Player;
        g.ReturnTo = "res://Scenes/Career.tscn";
        g.AwayTeamId = _career.TeamId;
        g.HomeTeamId = theirs.Team.Id;
        g.Mode = ControlMode.BatOnlyAway;

        g.GoTo("res://Scenes/Game.tscn");
    }

    /// <summary>A day off: the game happens without him and the line is modelled.</summary>
    private void SimGame()
    {
        var rng = new Rng(_career.Year * 977 + _career.GamesThisYear * 41 + _career.TeamId);
        var line = new BattingLine { Games = 1 };

        int pa = rng.Range(3, 6);
        for (int i = 0; i < pa; i++)
        {
            line.PlateAppearances++;
            if (rng.Chance(0.09f)) { line.Walks++; continue; }

            line.AtBats++;
            float skill = (_career.Player.Contact + _career.Player.Power) / 20f;
            if (rng.Chance(0.16f + (1f - skill) * 0.12f)) { line.Strikeouts++; continue; }

            if (!rng.Chance(0.20f + skill * 0.16f)) continue;

            line.Hits++;
            if (rng.Chance(0.09f + _career.Player.Power / 10f * 0.10f)) { line.HomeRuns++; line.Runs++; }
            else if (rng.Chance(0.22f)) line.Doubles++;
            if (rng.Chance(0.36f)) line.RunsBattedIn++;
        }

        CareerEngine.BookGame(_career, line);
        _career.Save();
        Say($"Sat it out — {line.Hits} for {line.AtBats} from the modelled line.");
    }

    private void EndSeason()
    {
        var news = CareerEngine.EndSeason(_career, Game.Instance.League,
            _career.Year * 61 + _career.TeamId);
        _career.Save();
        Say(news.Count > 0 ? news[^1] : "Season over.");
    }
}
