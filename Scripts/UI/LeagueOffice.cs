using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.Season;
using SandlotSlugfest.Stats;

namespace SandlotSlugfest.UI;

/// <summary>
/// The front office: standings, league leaders and a club's own stat sheet. Tab across the top
/// with Left/Right, move down the list with Up/Down.
/// </summary>
public partial class LeagueOffice : Control
{
    private enum Tab { Standings, Hitting, Pitching, MyClub }

    private static readonly string[] TabNames = { "STANDINGS", "HITTING LEADERS", "PITCHING LEADERS", "CLUB STATS" };

    private Tab _tab = Tab.Standings;
    private int _teamCursor;
    private SeasonState _season;
    private readonly ClickMap _clicks = new();

    /// <summary>
    /// Set when the screen is opened on top of a paused game rather than as its own scene. It then
    /// closes back to the game instead of navigating to the main menu, so a look at the roster
    /// between innings does not throw the game away.
    /// </summary>
    public System.Action CloseOverlay;

    private void Leave()
    {
        if (CloseOverlay != null) { CloseOverlay(); return; }

        // Back to the season, not the title screen. This is reached from the season hub's office
        // links, and dumping somebody on the main menu for pressing Escape is not "back" — it
        // throws away where they were and makes them walk in again.
        Game.Instance.GoTo(Game.Instance.League != null
            ? "res://Scenes/Season.tscn"
            : "res://Scenes/MainMenu.tscn");
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        // Runs while the tree is paused, which is exactly when it is used as an overlay.
        ProcessMode = ProcessModeEnum.Always;

        // Without this the Control swallows every mouse event and _UnhandledInput never
        // sees a click, so nothing on the screen is clickable.
        MouseFilter = MouseFilterEnum.Ignore;

        _season = Game.Instance.League;
        _teamCursor = _season.UserTeamId;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion m) { if (_clicks.Hover(m.Position)) QueueRedraw(); return; }

        if (@event is InputEventMouseButton { Pressed: true } wheel &&
            wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            Scroll(wheel.ButtonIndex == MouseButton.WheelDown ? 60f : -60f);
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
            case Key.Escape or Key.Backspace:
                Leave();
                return;
            case Key.Pagedown: Scroll(220f); return;
            case Key.Pageup: Scroll(-220f); return;
            case Key.Home: Scroll(-99999f); return;
            case Key.Left or Key.A:
                _tab = (Tab)Mathf.PosMod((int)_tab - 1, 4);
                break;
            case Key.Right or Key.D:
                _tab = (Tab)Mathf.PosMod((int)_tab + 1, 4);
                break;
            case Key.Up or Key.W:
                _teamCursor = Mathf.PosMod(_teamCursor - 1, 32);
                break;
            case Key.Down or Key.S:
                _teamCursor = Mathf.PosMod(_teamCursor + 1, 32);
                break;
        }
        QueueRedraw();
    }

    /// <summary>
    /// How far the tab's contents are scrolled, in pixels.
    ///
    /// A club's roster is nine hitters and thirteen arms with a header apiece, which is taller
    /// than the screen, and there was no way to move — the men at the bottom of the staff simply
    /// could not be looked at.
    /// </summary>
    private float _scroll;

    /// <summary>How far past the bottom the current tab ran, measured while drawing it.</summary>
    private float _overflow;

    private void Scroll(float by)
    {
        _scroll = Mathf.Clamp(_scroll + by, 0f, Mathf.Max(0f, _overflow));
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);
        _clicks.Begin();

        Palette.BackButton(this, size, _clicks, Leave);
        Palette.Text(this, new Vector2(40f, 46f), "LEAGUE OFFICE", 26, Palette.Ink);
        Palette.Text(this, new Vector2(40f, 68f),
            $"{_season.GamesPlayed} game{(_season.GamesPlayed == 1 ? "" : "s")} played this season",
            14, Palette.InkDim);

        DrawTabs(size);

        // The tab's body scrolls; the title, tabs and footer do not. Anything that fits reports
        // no overflow, so switching to it puts the scroll back to the top by itself.
        _overflow = 0f;
        DrawSetTransform(new Vector2(0f, -_scroll), 0f, Vector2.One);
        switch (_tab)
        {
            case Tab.Standings: DrawStandings(size); break;
            case Tab.Hitting: DrawHittingLeaders(size); break;
            case Tab.Pitching: DrawPitchingLeaders(size); break;
            case Tab.MyClub: DrawClubStats(size); break;
        }
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        if (_scroll > _overflow) _scroll = _overflow;

        if (_overflow > 0f)
            Palette.Text(this, new Vector2(size.X - 250f, size.Y - 22f),
                "scroll wheel  ·  PgUp/PgDn  ·  Home", 13, Palette.InkDim);

        Palette.Text(this, new Vector2(40f, size.Y - 22f),
            "Left/Right to switch views  ·  Up/Down to pick a club  ·  Esc to go back",
            14, Palette.InkDim);
    }

    private void DrawTabs(Vector2 size)
    {
        float x = 40f;
        for (int i = 0; i < TabNames.Length; i++)
        {
            bool on = (int)_tab == i;
            float w = Palette.TextWidth(TabNames[i], 14) + 28f;
            var rect = new Rect2(new Vector2(x, 88f), new Vector2(w, 30f));
            Palette.Panel3D(this, rect, on ? Palette.Highlight.Darkened(0.2f) : Palette.Panel);
            Palette.TextCentered(this, rect.Position + rect.Size * 0.5f, TabNames[i], 14,
                on ? Palette.Night : Palette.InkDim);

            var picked = (Tab)i;
            _clicks.Add(rect, () => _tab = picked);
            x += w + 8f;
        }
    }

    // -----------------------------------------------------------------------

    private void DrawStandings(Vector2 size)
    {
        float y = 146f;
        float colW = (size.X - 80f) / 4f;

        int col = 0;
        foreach (var league in new[] { League.American, League.National })
        foreach (var division in new[] { Division.East, Division.West })
        {
            float x = 40f + col * colW;
            // Short form, so the heading never runs into the W column.
            string heading = $"{(league == League.American ? "AL" : "NL")} {division.ToString().ToUpperInvariant()}";
            Palette.Text(this, new Vector2(x, y), heading, 13, Palette.Highlight);

            Palette.Text(this, new Vector2(x + 150f, y), "W", 12, Palette.InkDim);
            Palette.Text(this, new Vector2(x + 176f, y), "L", 12, Palette.InkDim);
            Palette.Text(this, new Vector2(x + 202f, y), "PCT", 12, Palette.InkDim);
            Palette.Text(this, new Vector2(x + 244f, y), "DIFF", 12, Palette.InkDim);

            float ry = y + 22f;
            var rows = _season.Standings(league, division).ToList();
            var leader = rows.Count > 0 ? rows[0].Record : new TeamRecord();

            foreach (var (team, rec) in rows)
            {
                bool hovered = team.Id == _teamCursor;
                var rowRect = new Rect2(new Vector2(x - 6f, ry - 13f), new Vector2(colW - 16f, 19f));
                if (hovered) DrawRect(rowRect, Palette.PanelLight);

                int pick = team.Id;
                _clicks.Add(rowRect, () => { _teamCursor = pick; _tab = Tab.MyClub; },
                    () => _teamCursor = pick);

                DrawRect(new Rect2(new Vector2(x, ry - 11f), new Vector2(4f, 13f)), team.Primary);
                Palette.Text(this, new Vector2(x + 10f, ry), team.Abbrev, 12,
                    hovered ? Palette.Highlight : Palette.InkDim);
                Palette.Text(this, new Vector2(x + 44f, ry), team.Nickname, 13,
                    hovered ? Palette.Ink : Palette.InkDim);

                Palette.Text(this, new Vector2(x + 150f, ry), rec.Wins.ToString(), 13, Palette.Ink);
                Palette.Text(this, new Vector2(x + 176f, ry), rec.Losses.ToString(), 13, Palette.Ink);
                Palette.Text(this, new Vector2(x + 202f, ry), rec.WinPctText, 13, Palette.Ink);

                int diff = rec.RunDifferential;
                Palette.Text(this, new Vector2(x + 244f, ry), diff > 0 ? $"+{diff}" : diff.ToString(), 13,
                    diff > 0 ? new Color("#7ddb8a") : diff < 0 ? new Color("#c47b7b") : Palette.InkDim);

                _ = leader;
                ry += 19f;
            }
            col++;
        }
    }

    private void DrawHittingLeaders(Vector2 size)
    {
        var boards = new (string Title, System.Func<BattingLine, float> By, System.Func<BattingLine, string> Show, int Min)[]
        {
            ("BATTING AVERAGE", l => l.Average, l => BattingLine.Rate(l.Average), 10),
            ("HOME RUNS", l => l.HomeRuns, l => l.HomeRuns.ToString(), 1),
            ("RUNS BATTED IN", l => l.RunsBattedIn, l => l.RunsBattedIn.ToString(), 1),
            ("ON-BASE PLUS SLUGGING", l => l.Ops, l => l.Ops.ToString("F3"), 10),
            ("ON-BASE PERCENTAGE", l => l.OnBase, l => BattingLine.Rate(l.OnBase), 10),
            ("STOLEN BASES", l => l.StolenBases, l => $"{l.StolenBases}-{l.CaughtStealing}", 1),
        };

        DrawLeaderGrid(size, boards.Length, (i, x, y, w, depth) =>
        {
            var (title, by, show, min) = boards[i];
            Palette.Text(this, new Vector2(x, y), title, 13, Palette.Highlight);
            float ry = y + 24f;

            var rows = _season.HittingLeaders(by, min, depth);
            if (rows.Count == 0)
                Palette.Text(this, new Vector2(x, ry), "No qualifiers yet — play some games.", 12, Palette.InkDim);

            int rank = 1;
            foreach (var (player, line) in rows)
            {
                var team = _season.TeamOf(player);
                Palette.Text(this, new Vector2(x, ry), $"{rank}.", 12, Palette.InkDim);
                Palette.Text(this, new Vector2(x + 22f, ry), player.Name, 13, Palette.Ink);
                if (team != null)
                    Palette.Text(this, new Vector2(x + 150f, ry), team.Abbrev, 12, team.Secondary);
                Palette.Text(this, new Vector2(x + w - 60f, ry), show(line), 13, Palette.Highlight);
                ry += 19f;
                rank++;
            }
        });
    }

    private void DrawPitchingLeaders(Vector2 size)
    {
        var boards = new (string Title, System.Func<PitchingLine, float> By, bool Asc,
            System.Func<PitchingLine, string> Show, int Min)[]
        {
            ("EARNED RUN AVERAGE", l => l.Era, true, l => l.Era.ToString("F2"), 9),
            ("STRIKEOUTS", l => l.Strikeouts, false, l => l.Strikeouts.ToString(), 1),
            ("WINS", l => l.Wins, false, l => l.Wins.ToString(), 1),
            ("WALKS PLUS HITS PER INNING", l => l.Whip, true, l => l.Whip.ToString("F2"), 9),

            // The bullpen's own board. Saves alone flatter the one man who gets the ninth and
            // say nothing about the four who got the game to him.
            ("SAVES AND HOLDS", l => l.Saves + l.Holds, false,
                l => $"{l.Saves} sv, {l.Holds} hld", 1),
            ("FIELDING INDEPENDENT", l => l.Fip, true, l => l.Fip.ToString("F2"), 9),
        };

        DrawLeaderGrid(size, boards.Length, (i, x, y, w, depth) =>
        {
            var (title, by, asc, show, min) = boards[i];
            Palette.Text(this, new Vector2(x, y), title, 13, Palette.Highlight);
            float ry = y + 24f;

            var rows = _season.PitchingLeaders(by, asc, min, depth);
            if (rows.Count == 0)
                Palette.Text(this, new Vector2(x, ry), "No qualifiers yet — play some games.", 12, Palette.InkDim);

            int rank = 1;
            foreach (var (player, line) in rows)
            {
                var team = _season.TeamOf(player);
                Palette.Text(this, new Vector2(x, ry), $"{rank}.", 12, Palette.InkDim);
                Palette.Text(this, new Vector2(x + 22f, ry), player.Name, 13, Palette.Ink);
                if (team != null)
                    Palette.Text(this, new Vector2(x + 150f, ry), team.Abbrev, 12, team.Secondary);
                Palette.Text(this, new Vector2(x + w - 60f, ry), show(line), 13, Palette.Highlight);
                ry += 19f;
                rank++;
            }
        });
    }

    /// <summary>
    /// Lays the leaderboards out two across and as many down as there are.
    ///
    /// The row height used to be a fixed 250 and the depth a fixed nine, which quietly capped the
    /// screen at four boards: a fifth started below the bottom of the window and was drawn where
    /// nobody could see it. The band is now divided by how many rows there actually are, and each
    /// board is told how many names will fit rather than assuming.
    /// </summary>
    private void DrawLeaderGrid(Vector2 size, int count,
        System.Action<int, float, float, float, int> draw)
    {
        float w = (size.X - 100f) / 2f;
        const float Top = 152f;

        int rows = Mathf.Max(1, Mathf.CeilToInt(count / 2f));
        float band = (size.Y - Top - 40f) / rows;
        int depth = Mathf.Clamp(Mathf.FloorToInt((band - 30f) / 19f), 3, 9);

        for (int i = 0; i < count; i++)
        {
            float x = 40f + (i % 2) * (w + 20f);
            float y = Top + (i / 2) * band;
            draw(i, x, y, w, depth);
        }
    }

    private void DrawClubStats(Vector2 size)
    {
        var team = Teams.Get(_teamCursor);
        var roster = _season.RosterFor(team.Id);
        var rec = _season.Book.Record(team.Id);

        var header = new Rect2(new Vector2(40f, 140f), new Vector2(size.X - 80f, 54f));
        Palette.Panel3D(this, header, team.Primary);
        Palette.Text(this, header.Position + new Vector2(16f, 34f),
            $"{team.FullName}   {rec.Wins}-{rec.Losses}   ({rec.WinPctText})   " +
            $"RS {rec.RunsScored}  RA {rec.RunsAllowed}", 18, team.TextOnPrimary);

        // Hitters.
        float y = 224f;
        // The right-hand half of this table was empty, so the columns that make a line readable
        // now live there: on-base and slugging rather than only their sum, the stolen-base record
        // with the times he was caught, and the sacrifices and double plays that were being kept
        // but never shown.
        string[] cols =
        {
            "POS", "NAME", "AVG", "AB", "H", "2B", "3B", "HR", "RBI", "R", "BB", "K",
            "OBP", "SLG", "OPS", "SB-CS", "HBP", "SF", "GIDP",
        };
        float[] xs =
        {
            40f, 88f, 250f, 306f, 348f, 386f, 424f, 462f, 504f, 550f, 590f, 630f,
            676f, 730f, 784f, 838f, 902f, 946f, 986f,
        };

        for (int i = 0; i < cols.Length; i++)
            Palette.Text(this, new Vector2(xs[i], y), cols[i], 12, Palette.Highlight);
        y += 20f;

        foreach (var p in roster.BattingOrder)
        {
            var b = _season.Book.Batting(p);
            Palette.Text(this, new Vector2(xs[0], y), p.PositionText, 12, Palette.InkDim);
            Palette.Text(this, new Vector2(xs[1], y), p.Name, 13, Palette.Ink);
            Palette.Text(this, new Vector2(xs[2], y), BattingLine.Rate(b.Average), 13, Palette.Ink);
            Palette.Text(this, new Vector2(xs[3], y), b.AtBats.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(xs[4], y), b.Hits.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(xs[5], y), b.Doubles.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(xs[6], y), b.Triples.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(xs[7], y), b.HomeRuns.ToString(), 12, Palette.Ink);
            Palette.Text(this, new Vector2(xs[8], y), b.RunsBattedIn.ToString(), 12, Palette.Ink);
            Palette.Text(this, new Vector2(xs[9], y), b.Runs.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(xs[10], y), b.Walks.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(xs[11], y), b.Strikeouts.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(xs[12], y), BattingLine.Rate(b.OnBase), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(xs[13], y), BattingLine.Rate(b.Slugging), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(xs[14], y), b.Ops.ToString("F3"), 12, Palette.Ink);
            Palette.Text(this, new Vector2(xs[15], y),
                $"{b.StolenBases}-{b.CaughtStealing}", 12, Palette.InkDim);
            Palette.Text(this, new Vector2(xs[16], y), b.HitByPitch.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(xs[17], y), b.SacrificeFlies.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(xs[18], y),
                b.GroundedIntoDoublePlay.ToString(), 12, Palette.InkDim);
            y += 19f;
        }

        // Pitchers.
        y += 22f;
        string[] pcols =
        {
            "NAME", "W", "L", "ERA", "IP", "H", "ER", "BB", "K", "WHIP",
            "SV", "HLD", "BS", "QS", "HR", "HBP", "WP", "FIP",
        };
        float[] pxs =
        {
            88f, 250f, 288f, 326f, 386f, 434f, 472f, 514f, 552f, 596f,
            652f, 692f, 738f, 776f, 814f, 856f, 902f, 940f,
        };
        Palette.Text(this, new Vector2(40f, y), "STAFF", 12, Palette.Highlight);
        for (int i = 0; i < pcols.Length; i++)
            Palette.Text(this, new Vector2(pxs[i], y), pcols[i], 12, Palette.Highlight);
        y += 20f;

        foreach (var p in roster.Pitchers)
        {
            var t = _season.Book.Pitching(p);
            Palette.Text(this, new Vector2(pxs[0], y), p.Name, 13, Palette.Ink);
            Palette.Text(this, new Vector2(pxs[1], y), t.Wins.ToString(), 12, Palette.Ink);
            Palette.Text(this, new Vector2(pxs[2], y), t.Losses.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(pxs[3], y), t.Outs > 0 ? t.Era.ToString("F2") : "—", 13, Palette.Ink);
            Palette.Text(this, new Vector2(pxs[4], y), t.InningsText, 12, Palette.InkDim);
            Palette.Text(this, new Vector2(pxs[5], y), t.Hits.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(pxs[6], y), t.EarnedRuns.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(pxs[7], y), t.Walks.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(pxs[8], y), t.Strikeouts.ToString(), 12, Palette.Ink);
            Palette.Text(this, new Vector2(pxs[9], y), t.Outs > 0 ? t.Whip.ToString("F2") : "—", 12, Palette.InkDim);
            Palette.Text(this, new Vector2(pxs[10], y), t.Saves.ToString(), 12, Palette.Ink);
            Palette.Text(this, new Vector2(pxs[11], y), t.Holds.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(pxs[12], y), t.BlownSaves.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(pxs[13], y), t.QualityStarts.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(pxs[14], y), t.HomeRunsAllowed.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(pxs[15], y), t.HitBatters.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(pxs[16], y), t.WildPitches.ToString(), 12, Palette.InkDim);
            Palette.Text(this, new Vector2(pxs[17], y), t.Outs > 0 ? t.Fip.ToString("F2") : "—", 12, Palette.InkDim);
            y += 19f;
        }

        // How far past the bottom this ran, so the scroll knows where to stop.
        _overflow = Mathf.Max(0f, y - (GetViewportRect().Size.Y - 60f));
    }
}
