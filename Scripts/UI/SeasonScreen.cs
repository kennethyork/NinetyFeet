using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.Season;

namespace SandlotSlugfest.UI;

/// <summary>
/// The season hub: your club's record and division race, the next game on the schedule, and
/// once the regular season is out, the postseason bracket.
/// </summary>
public partial class SeasonScreen : Control
{
    private readonly ClickMap _clicks = new();
    private SeasonState _season;
    private string _notice = "";

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        // A Control swallows mouse events in the GUI phase by default, so they never reach
        // _UnhandledInput. These screens do their own hit testing, so let the events through.
        MouseFilter = MouseFilterEnum.Ignore;
        _season = Game.Instance.League;

        // A league loaded from before schedules existed needs one built.
        if (!_season.HasSchedule)
        {
            _season.Games = Schedule.Build(Schedule.ShortSeason, _season.LeagueSeed);
            Game.Instance.SaveLeague();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            if (_clicks.Click(mb.Position)) QueueRedraw();
            return;
        }
        if (@event is InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape })
            Game.Instance.GoTo("res://Scenes/MainMenu.tscn");
    }

    // -----------------------------------------------------------------------
    // Actions
    // -----------------------------------------------------------------------

    private void PlayNext() => GoToGame(spectate: false);

    /// <summary>
    /// Watches a game rather than playing it. Both sides are managed by the computer and you sit
    /// in the stands — which is what a manager wants when a series matters but he does not want to
    /// take the bat himself. The result books exactly as a played game does.
    /// </summary>
    private void WatchNext() => GoToGame(spectate: true);

    private void GoToGame(bool spectate)
    {
        // Roll the calendar forward to the date of the game, so the rest of the league is caught
        // up before the user takes the field.
        var next = _season.AdvanceToNextUserGame();
        if (next == null) return;

        _season.SimulateRestOfDay(next);

        var g = Game.Instance;
        g.AwayTeamId = next.AwayId;
        g.HomeTeamId = next.HomeId;
        g.Innings = _season.Innings;
        g.Mode = spectate
            ? ControlMode.CpuVsCpu
            : next.HomeId == _season.UserTeamId ? ControlMode.CpuVsPlayer : ControlMode.PlayerVsCpu;
        g.PendingSeasonGame = next;
        g.GoTo("res://Scenes/Game.tscn");
    }

    private void SimNext()
    {
        var next = _season.AdvanceToNextUserGame();
        if (next == null) return;
        _season.AdvanceDay(simulateUserGame: true);
        _season.BeginPlayoffsIfReady();
        Game.Instance.SaveLeague();
        _notice = $"{Teams.Get(next.AwayId).Abbrev} {next.AwayRuns} — " +
                  $"{next.HomeRuns} {Teams.Get(next.HomeId).Abbrev}";
    }

    /// <summary>Pushes the calendar a day, playing every game on it.</summary>
    private void SimDay()
    {
        _season.AdvanceDay(simulateUserGame: true);
        _season.BeginPlayoffsIfReady();
        Game.Instance.SaveLeague();
        _notice = $"Now {Calendar.Format(_season.Today)}.";
    }

    /// <summary>A week of the calendar, stopping the moment the user's club is due to play.</summary>
    private void SimWeek()
    {
        for (int i = 0; i < 7; i++)
        {
            if (_season.UserGameToday() != null) break;
            if (_season.CurrentDay > _season.FinalDay) break;
            _season.AdvanceDay(simulateUserGame: true);
        }
        _season.BeginPlayoffsIfReady();
        Game.Instance.SaveLeague();
        _notice = "Simulated a week of the schedule.";
    }

    private void SimToEnd()
    {
        int guard = 0;
        while (_season.CurrentDay <= _season.FinalDay && guard++ < 500)
            _season.AdvanceDay(simulateUserGame: true);
        foreach (var g in _season.Games.Where(g => !g.Played).ToList()) _season.SimulateGame(g);
        _season.BeginPlayoffsIfReady();
        Game.Instance.SaveLeague();
        _notice = "Regular season complete.";
    }

    private void PlayoffGame()
    {
        var series = _season.SimulateNextPlayoffGame();
        Game.Instance.SaveLeague();
        _notice = series == null ? "The postseason is over." : series.Line;
    }

    /// <summary>
    /// Rolls the franchise into next year: everyone ages, develops or declines, the old and the
    /// finished retire, clubs restock, and a fresh schedule is drawn. Career totals and the record
    /// book carry over.
    ///
    /// This used to throw the whole league away and generate a new one, which meant the entire
    /// offseason — aging, development, retirements, history — was unreachable from the game.
    /// </summary>
    private void NextSeason()
    {
        int year = _season.Year;
        _season.AdvanceToNextSeason();
        Game.Instance.SaveLeague();

        int retired = _season.LastOffseason.Count(r => r.Graduated);
        int improved = _season.LastOffseason.Count(r => r.After > r.Before);
        _notice = $"Year {year} is in the books. {retired} players moved on, {improved} improved. " +
                  $"Welcome to year {_season.Year}.";
    }

    /// <summary>Throws this league away and generates a brand new one. Asks first.</summary>
    private void StartOver()
    {
        if (!_confirmWipe) { _confirmWipe = true; _notice = "That wipes this league. Click again to confirm."; return; }

        _confirmWipe = false;
        Game.Instance.NewSeason(_season.UserTeamId);
        _season = Game.Instance.League;
        _notice = "New league generated.";
    }

    private bool _confirmWipe;

    // -----------------------------------------------------------------------
    // Drawing
    // -----------------------------------------------------------------------

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);
        _clicks.Begin();

        var club = Teams.Get(_season.UserTeamId);
        var rec = _season.Book.Record(club.Id);

        Palette.Text(this, new Vector2(40f, 46f), "SEASON", 26, Palette.Ink);
        Palette.Text(this, new Vector2(40f, 70f),
            $"{club.FullName}   {rec.Wins}-{rec.Losses}  ({rec.WinPctText})   " +
            $"{_season.UserGamesPlayed()} of {_season.UserGamesPlayed() + _season.UserGamesRemaining()} played",
            15, Palette.InkDim);

        // The date is the thing you advance, so it leads.
        Palette.Text(this, new Vector2(size.X - 300f, 46f),
            Calendar.Format(_season.Today), 20, Palette.Highlight);
        Palette.Text(this, new Vector2(size.X - 300f, 68f),
            $"Year {_season.Year}  ·  {Calendar.PhaseLabel(_season.Phase)}" +
            (Game.Instance.ManagerOnly ? "  ·  Manager" : ""), 13, Palette.InkDim);

        // The deadline is a clock you have to manage. It used to sit under the date, which is
        // where it belongs conceptually and where there is no room: the right column is full at 46
        // and 68, and the office links own everything from 72 down. Under the club's record on the
        // left instead — that column is empty and the deadline is a fact about your club anyway.
        if (_season.Phase == SeasonPhase.RegularSeason)
        {
            bool open = _season.TradesOpen;
            Palette.Text(this, new Vector2(40f, 90f),
                open
                    ? $"Trade deadline: {Calendar.FormatShort(Calendar.DateOf(_season.TradeDeadlineDay))}" +
                      $"  ({_season.DaysToDeadline} game days)"
                    : "Trade deadline has passed",
                12, open && _season.DaysToDeadline <= 4 ? Palette.Warning
                    : open ? Palette.InkDim : Palette.Warning);
        }

        Palette.BackButton(this, size, _clicks, () => Game.Instance.GoTo("res://Scenes/MainMenu.tscn"));

        // Between games is exactly when a manager wants the roster and the league — having to go
        // back out to the main menu for them made them feel like a separate application.
        if (_season.Playoffs.Started) DrawPlayoffs(size);
        else DrawRegularSeason(size);

        DrawDivision(size, club);
        DrawNews(size);

        // Last, on top of everything. A navigation bar that something else can paint over is not
        // navigation.
        DrawOfficeShortcuts(size);

        if (!string.IsNullOrEmpty(_notice))
            Palette.Text(this, new Vector2(40f, size.Y - 26f), _notice, 14, Palette.Highlight);
    }

    /// <summary>
    /// The league news feed: injuries and transactions as they happen. A management sim without
    /// one is a spreadsheet — this is how the league tells you something changed while you were
    /// advancing the calendar.
    /// </summary>
    private void DrawNews(Vector2 size)
    {
        if (_season.News.Count == 0) return;

        var panel = new Rect2(new Vector2(40f, PanelTop + 188f), new Vector2(520f, size.Y - PanelTop - 228f));
        if (panel.Size.Y < 90f) return;
        Palette.Panel3D(this, panel, Palette.Panel);

        Palette.Text(this, panel.Position + new Vector2(18f, 28f), "AROUND THE LEAGUE", 13, Palette.Highlight);

        int rows = Mathf.FloorToInt((panel.Size.Y - 44f) / 34f);
        float y = 56f;

        foreach (var n in _season.News.Take(rows))
        {
            var when = Calendar.FormatShort(Calendar.DateOf(n.Day));
            Palette.Text(this, panel.Position + new Vector2(18f, y), when, 11, Palette.InkDim);
            Palette.Text(this, panel.Position + new Vector2(72f, y), n.Headline, 14, Palette.Ink);

            if (!string.IsNullOrEmpty(n.Detail))
                Palette.Text(this, panel.Position + new Vector2(72f, y + 15f), n.Detail, 11, Palette.InkDim);

            // A stripe in the club's colour, so your own team's news is findable at a glance.
            if (n.TeamId >= 0)
                DrawRect(new Rect2(panel.Position + new Vector2(60f, y - 10f), new Vector2(4f, 24f)),
                    n.TeamId == _season.UserTeamId ? Palette.Highlight : Teams.Get(n.TeamId).Primary);

            y += 34f;
        }
    }

    /// <summary>Quick links to the management screens, alongside the calendar controls.</summary>
    /// <summary>
    /// Where the office links sit.
    ///
    /// They were at 114, which is inside both panels — the next-game card starts at 104 and so
    /// does the division table — and they were drawn before those panels, so the whole navigation
    /// bar was painted over. All that showed was a sliver of one word in the forty-pixel gap
    /// between the two panels and one letter past the right-hand edge.
    ///
    /// Their own row above the panels, and drawn last so nothing can bury them again.
    /// </summary>
    private const float NavY = 74f;

    /// <summary>
    /// Where the panels start. The header runs to 90 and the office links occupy 74 to 104, so
    /// anything above this is standing on something.
    /// </summary>
    private const float PanelTop = 120f;

    private void DrawOfficeShortcuts(Vector2 size)
    {
        var links = new (string Label, string Scene)[]
        {
            ("CLUBHOUSE", "res://Scenes/Franchise.tscn"),
            ("FRONT OFFICE", "res://Scenes/FrontOffice.tscn"),
            ("LEAGUE OFFICE", "res://Scenes/LeagueOffice.tscn"),
            ("TRADE DESK", "res://Scenes/TradeScreen.tscn"),
            ("THE LEAGUE", "res://Scenes/LeagueBrowser.tscn"),
        };

        // Starting over used to require finishing a season first — there was no way out of a
        // league you had lost interest in without deleting the save file by hand.
        var wipe = new Rect2(new Vector2(size.X - 128f - links.Length * 134f, NavY),
            new Vector2(124f, 30f));
        Palette.Panel3D(this, wipe, _confirmWipe ? Palette.Warning.Darkened(0.25f) : Palette.Panel);
        Palette.TextCentered(this, wipe.Position + wipe.Size * 0.5f,
            _confirmWipe ? "SURE?" : "NEW LEAGUE", 10, _confirmWipe ? Palette.Ink : Palette.InkDim);
        _clicks.Add(wipe, StartOver);

        for (int i = 0; i < links.Length; i++)
        {
            bool shut = links[i].Label == "TRADE DESK" && !_season.TradesOpen;

            var rect = new Rect2(new Vector2(size.X - 128f - i * 134f, NavY), new Vector2(124f, 30f));
            Palette.Panel3D(this, rect, shut ? Palette.Panel : Palette.PanelLight);
            Palette.TextCentered(this, rect.Position + rect.Size * 0.5f, links[i].Label, 10,
                shut ? Palette.InkDim : Palette.Ink);

            string target = links[i].Scene;
            _clicks.Add(rect, () => Game.Instance.GoTo(target));
        }
    }

    private void DrawRegularSeason(Vector2 size)
    {
        var next = _season.NextUserGame();
        var panel = new Rect2(new Vector2(40f, PanelTop), new Vector2(520f, 168f));
        Palette.Panel3D(this, panel, Palette.Panel);

        if (next == null)
        {
            Palette.Text(this, panel.Position + new Vector2(18f, 40f),
                "Your schedule is complete.", 20, Palette.Ink);
            Button(new Rect2(panel.Position + new Vector2(18f, 70f), new Vector2(240f, 40f)),
                "SIM REST OF LEAGUE", SimToEnd);
            if (_season.RegularSeasonComplete)
                Button(new Rect2(panel.Position + new Vector2(272f, 70f), new Vector2(220f, 40f)),
                    "START PLAYOFFS", () => { _season.BeginPlayoffsIfReady(); Game.Instance.SaveLeague(); });
            return;
        }

        bool home = next.HomeId == _season.UserTeamId;
        var foe = Teams.Get(home ? next.AwayId : next.HomeId);
        var park = Stadiums.For(next.HomeId);

        Palette.Text(this, panel.Position + new Vector2(18f, 32f), "NEXT GAME", 13, Palette.Highlight);
        Palette.Text(this, panel.Position + new Vector2(18f, 62f),
            home ? $"vs {foe.FullName}" : $"at {foe.FullName}", 22, Palette.Ink);
        Palette.Text(this, panel.Position + new Vector2(18f, 84f),
            $"{park.Name} — {park.Quirk}", 13, Palette.InkDim);

        bool today = _season.UserGameToday() != null;

        // Dynasty defaults to simulating, but never locks you out of a game you want to see —
        // a big series is exactly when a manager wants to watch rather than read a box score.
        if (Game.Instance.ManagerOnly)
        {
            Button(new Rect2(panel.Position + new Vector2(18f, 104f), new Vector2(126f, 44f)),
                "NEXT GAME", SimNext, Palette.Highlight);
            Button(new Rect2(panel.Position + new Vector2(154f, 104f), new Vector2(104f, 44f)),
                today ? "WATCH" : "GO WATCH", WatchNext);
            Button(new Rect2(panel.Position + new Vector2(268f, 104f), new Vector2(70f, 44f)), "DAY", SimDay);
            Button(new Rect2(panel.Position + new Vector2(348f, 104f), new Vector2(70f, 44f)), "WEEK", SimWeek);
            Button(new Rect2(panel.Position + new Vector2(428f, 104f), new Vector2(74f, 44f)), "YEAR", SimToEnd);
            return;
        }

        Button(new Rect2(panel.Position + new Vector2(18f, 104f), new Vector2(150f, 44f)),
            today ? "PLAY" : "GO TO GAME", PlayNext, Palette.Highlight);
        Button(new Rect2(panel.Position + new Vector2(178f, 104f), new Vector2(96f, 44f)), "SIM", SimNext);
        Button(new Rect2(panel.Position + new Vector2(284f, 104f), new Vector2(84f, 44f)), "DAY", SimDay);
        Button(new Rect2(panel.Position + new Vector2(378f, 104f), new Vector2(60f, 44f)), "WEEK", SimWeek);
        Button(new Rect2(panel.Position + new Vector2(448f, 104f), new Vector2(54f, 44f)), "YEAR", SimToEnd);
    }

    private void DrawPlayoffs(Vector2 size)
    {
        var panel = new Rect2(new Vector2(40f, PanelTop), new Vector2(520f, 300f));
        Palette.Panel3D(this, panel, Palette.Panel);
        Palette.Text(this, panel.Position + new Vector2(18f, 30f), "POSTSEASON", 13, Palette.Highlight);

        float y = panel.Position.Y + 56f;
        foreach (var s in _season.Playoffs.Series)
        {
            bool live = s.Ready && !s.Complete;
            var ink = s.Complete ? Palette.InkDim : live ? Palette.Ink : Palette.InkDim;
            Palette.Text(this, new Vector2(panel.Position.X + 18f, y), s.Line, 15, ink);

            if (s.Complete && s.WinnerId >= 0)
                Palette.Text(this, new Vector2(panel.Position.X + 380f, y),
                    $"{Teams.Get(s.WinnerId).Abbrev} advance", 12, new Color("#7ddb8a"));
            y += 26f;
        }

        if (_season.Playoffs.Finished)
        {
            var champ = Teams.Get(_season.Playoffs.ChampionId);
            var box = new Rect2(panel.Position + new Vector2(18f, y + 12f), new Vector2(480f, 56f));
            Palette.Panel3D(this, box, champ.Primary);
            Palette.TextCentered(this, box.Position + box.Size * 0.5f,
                $"{champ.FullName.ToUpperInvariant()} — CHAMPIONS", 20, champ.TextOnPrimary);

            // The offseason: draft first, then roll the league over.
            bool draftDone = _season.Draft.Order.Count > 0 && _season.Draft.Complete;
            Button(new Rect2(panel.Position + new Vector2(18f, y + 80f), new Vector2(230f, 40f)),
                draftDone ? "DRAFT COMPLETE" : "GO TO THE DRAFT",
                () => Game.Instance.GoTo("res://Scenes/Draft.tscn"),
                draftDone ? null : Palette.Highlight);

            Button(new Rect2(panel.Position + new Vector2(262f, y + 80f), new Vector2(230f, 40f)),
                $"PLAY YEAR {_season.Year + 1}", NextSeason, Palette.Highlight);
        }
        else
        {
            Button(new Rect2(panel.Position + new Vector2(18f, y + 16f), new Vector2(240f, 42f)),
                "PLAY NEXT PLAYOFF GAME", PlayoffGame, Palette.Highlight);
        }
    }

    private void DrawDivision(Vector2 size, TeamData club)
    {
        float x = 600f;
        var panel = new Rect2(new Vector2(x, PanelTop), new Vector2(size.X - x - 40f, size.Y - PanelTop - 40f));
        Palette.Panel3D(this, panel, Palette.Panel);

        Palette.Text(this, panel.Position + new Vector2(16f, 28f),
            Teams.DivisionName(club.League, club.Division).ToUpperInvariant(), 13, Palette.Highlight);

        float y = panel.Position.Y + 52f;
        Palette.Text(this, new Vector2(panel.Position.X + 200f, y), "W", 12, Palette.InkDim);
        Palette.Text(this, new Vector2(panel.Position.X + 228f, y), "L", 12, Palette.InkDim);
        Palette.Text(this, new Vector2(panel.Position.X + 256f, y), "PCT", 12, Palette.InkDim);
        Palette.Text(this, new Vector2(panel.Position.X + 300f, y), "GB", 12, Palette.InkDim);
        y += 20f;

        var rows = _season.Standings(club.League, club.Division).ToList();
        var leader = rows.Count > 0 ? rows[0].Record : null;

        foreach (var (team, rec) in rows)
        {
            bool mine = team.Id == club.Id;
            if (mine)
                DrawRect(new Rect2(new Vector2(panel.Position.X + 10f, y - 13f),
                    new Vector2(panel.Size.X - 20f, 20f)), Palette.PanelLight);

            DrawRect(new Rect2(new Vector2(panel.Position.X + 16f, y - 11f), new Vector2(5f, 14f)),
                team.Primary);
            Palette.Text(this, new Vector2(panel.Position.X + 30f, y), team.Abbrev, 12,
                mine ? Palette.Highlight : Palette.InkDim);
            Palette.Text(this, new Vector2(panel.Position.X + 66f, y), team.Nickname, 14,
                mine ? Palette.Ink : Palette.InkDim);

            Palette.Text(this, new Vector2(panel.Position.X + 200f, y), rec.Wins.ToString(), 13, Palette.Ink);
            Palette.Text(this, new Vector2(panel.Position.X + 228f, y), rec.Losses.ToString(), 13, Palette.Ink);
            Palette.Text(this, new Vector2(panel.Position.X + 256f, y), rec.WinPctText, 13, Palette.Ink);

            float gb = leader == null ? 0f : rec.GamesBehind(leader);
            Palette.Text(this, new Vector2(panel.Position.X + 300f, y),
                gb <= 0f ? "—" : gb.ToString("0.0"), 13, Palette.InkDim);
            y += 20f;
        }
    }

    private void Button(Rect2 rect, string label, System.Action onClick, Color? fill = null)
    {
        var colour = fill ?? Palette.PanelLight;
        Palette.Panel3D(this, rect, colour);
        Palette.TextCentered(this, rect.Position + rect.Size * 0.5f, label,
            rect.Size.X > 150f ? 15 : 14, fill.HasValue ? Palette.Night : Palette.Ink);
        _clicks.Add(rect, onClick);
    }
}
