using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.Season;

namespace SandlotSlugfest.UI;

/// <summary>
/// The front office.
///
/// Aging, development, career totals, injuries and a champion history all existed and none of it
/// was ever shown: a franchise engine with no franchise screens. This is where you look at your
/// roster, open a player and see what he has actually done, check who is hurt, and read the record
/// books.
/// </summary>
public partial class FranchiseScreen : Control
{
    private enum Tab { Roster, Lineup, Injuries, History }

    private static readonly string[] TabNames =
        { "ROSTER", "LINEUP & BULLPEN", "INJURY REPORT", "RECORD BOOK" };

    /// <summary>Selected for a swap. Click a second player to exchange their places.</summary>
    private PlayerData _swapFrom;

    private Tab _tab = Tab.Roster;
    private SeasonState _season;
    private PlayerData _selected;
    private int _teamCursor;
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
        // Back to the season hub this was opened from. The clubhouse is a season screen; the
        // title screen is not "back" from it.
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

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            if (_clicks.Click(mb.Position)) QueueRedraw();
            return;
        }

        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        switch (key.PhysicalKeycode)
        {
            case Key.Escape or Key.Backspace:
                if (_selected != null) { _selected = null; break; }
                Leave();
                return;
            case Key.Left or Key.A:
                _tab = (Tab)Mathf.PosMod((int)_tab - 1, 4); _selected = null; _swapFrom = null; break;
            case Key.Right or Key.D:
                _tab = (Tab)Mathf.PosMod((int)_tab + 1, 4); _selected = null; _swapFrom = null; break;
            default:
                return;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);
        _clicks.Begin();

        var club = Teams.Get(_teamCursor);
        // This is the clubhouse — the roster, the lineup, the injuries. It called itself FRONT
        // OFFICE, which is a different screen reached from a different link on the same nav bar.
        // Two screens with one name is a good way to be sure nobody finds either.
        Palette.Text(this, new Vector2(40f, 46f), "CLUBHOUSE", 26, Palette.Ink);
        Palette.Text(this, new Vector2(40f, 70f),
            $"{club.FullName}   ·   Year {_season.Year}   ·   {Calendar.Format(_season.Today)}",
            15, Palette.InkDim);

        Palette.BackButton(this, size, _clicks, Leave);
        DrawTabs(size);

        switch (_tab)
        {
            case Tab.Lineup: DrawLineup(size); break;
            case Tab.Injuries: DrawInjuries(size); break;
            case Tab.History: DrawHistory(size); break;
            default: DrawRoster(size); break;
        }

        if (_selected != null) DrawPlayerCard(size);
    }

    private void DrawTabs(Vector2 size)
    {
        for (int i = 0; i < TabNames.Length; i++)
        {
            var rect = new Rect2(new Vector2(40f + i * 172f, 96f), new Vector2(164f, 34f));
            bool on = (int)_tab == i;
            Palette.Panel3D(this, rect, on ? Palette.Highlight.Darkened(0.2f) : Palette.Panel);
            Palette.TextCentered(this, rect.Position + rect.Size * 0.5f, TabNames[i], 13,
                on ? Palette.Night : Palette.InkDim);

            int picked = i;
            _clicks.Add(rect, () => { _tab = (Tab)picked; _selected = null; _swapFrom = null; });
        }
    }

    // -----------------------------------------------------------------------
    // Roster
    // -----------------------------------------------------------------------

    private void DrawRoster(Vector2 size)
    {
        var roster = _season.RosterFor(_teamCursor);
        var panel = new Rect2(new Vector2(40f, 146f), new Vector2(size.X - 80f, size.Y - 196f));
        Palette.Panel3D(this, panel, Palette.Panel);

        // CEILING had ninety pixels and prints things like "Everyday starter", which needs a
        // hundred — so every scouting grade ran into the batting average beside it.
        var cols = new[] { 20f, 220f, 290f, 340f, 400f, 470f, 612f, 674f, 732f, 800f };
        var heads = new[] { "PLAYER", "POS", "AGE", "OVR", "POT", "CEILING", "AVG", "HR", "RBI", "STATUS" };
        for (int i = 0; i < heads.Length; i++)
            Palette.Text(this, panel.Position + new Vector2(cols[i], 30f), heads[i], 11, Palette.InkDim);

        // Regulars first, then arms, then the rest — the order a manager thinks in.
        var ordered = roster.Players
            .OrderByDescending(p => roster.Starters.ContainsValue(p) ? 2 : roster.Pitchers.Contains(p) ? 1 : 0)
            .ThenByDescending(p => p.Overall)
            .ToList();

        float y = 56f;
        int rows = Mathf.FloorToInt((panel.Size.Y - 70f) / 26f);

        foreach (var p in ordered.Take(rows))
        {
            var row = new Rect2(panel.Position + new Vector2(12f, y - 14f), new Vector2(panel.Size.X - 24f, 24f));
            if (p == _selected) DrawRect(row, Palette.PanelLight);

            var bat = _season.Book.Batting(p);
            var tone = p.IsInjured ? Palette.Warning : Palette.Ink;

            Palette.Text(this, panel.Position + new Vector2(cols[0], y), $"#{p.Number,-3} {p.Name}", 14, tone);
            Palette.Text(this, panel.Position + new Vector2(cols[1], y), PlayerData.PositionLabel(p.Position), 13, Palette.InkDim);
            Palette.Text(this, panel.Position + new Vector2(cols[2], y), p.Age.ToString(), 13, Palette.InkDim);
            Palette.Text(this, panel.Position + new Vector2(cols[3], y), p.Overall.ToString(), 13, Palette.Ink);

            // Upside is the whole reason potential exists — show what is still ahead of him.
            var potColour = p.Upside >= 2 ? new Color(0.45f, 1f, 0.55f) : Palette.InkDim;
            Palette.Text(this, panel.Position + new Vector2(cols[4], y),
                p.Upside > 0 ? $"{p.Ceiling} (+{p.Upside})" : p.Ceiling.ToString(), 13, potColour);
            Palette.Text(this, panel.Position + new Vector2(cols[5], y), p.PotentialGrade, 12, Palette.InkDim);

            if (p.Position == Data.Position.P)
            {
                var pit = _season.Book.Pitching(p);
                Palette.Text(this, panel.Position + new Vector2(cols[6], y),
                    pit.Outs > 0 ? $"{pit.Era:F2} ERA" : "—", 13, Palette.InkDim);
                Palette.Text(this, panel.Position + new Vector2(cols[7], y), $"{pit.Wins}-{pit.Losses}", 13, Palette.InkDim);
                Palette.Text(this, panel.Position + new Vector2(cols[8], y), $"{pit.Strikeouts} K", 13, Palette.InkDim);
            }
            else
            {
                Palette.Text(this, panel.Position + new Vector2(cols[6], y),
                    bat.AtBats > 0 ? $"{bat.Average:.000}" : "—", 13, Palette.InkDim);
                Palette.Text(this, panel.Position + new Vector2(cols[7], y), bat.HomeRuns.ToString(), 13, Palette.InkDim);
                Palette.Text(this, panel.Position + new Vector2(cols[8], y), bat.RunsBattedIn.ToString(), 13, Palette.InkDim);
            }

            Palette.Text(this, panel.Position + new Vector2(cols[9], y),
                p.IsInjured ? $"OUT {p.DaysOut}" : roster.Starters.ContainsValue(p) ? "starter"
                    : roster.Pitchers.Contains(p) ? "rotation" : "bench",
                12, p.IsInjured ? Palette.Warning : Palette.InkDim);

            var pick = p;
            _clicks.Add(row, () => _selected = pick);
            y += 26f;
        }

        Palette.Text(this, panel.Position + new Vector2(20f, panel.Size.Y - 16f),
            "Click a player for his card.", 12, Palette.InkDim);
    }

    // -----------------------------------------------------------------------
    // Lineup and bullpen
    // -----------------------------------------------------------------------

    /// <summary>
    /// Setting the batting order, who plays where, and who is on the mound. A manager who cannot
    /// move his own lineup around is a spectator.
    /// </summary>
    private void DrawLineup(Vector2 size)
    {
        var roster = _season.RosterFor(_teamCursor);

        // --- Batting order ---
        var left = new Rect2(new Vector2(40f, 146f), new Vector2(size.X * 0.52f - 60f, size.Y - 196f));
        Palette.Panel3D(this, left, Palette.Panel);
        Palette.Text(this, left.Position + new Vector2(20f, 28f), "BATTING ORDER", 11, Palette.Highlight);
        Palette.Text(this, left.Position + new Vector2(150f, 28f),
            _swapFrom == null ? "click two players to swap them"
                              : $"swapping {_swapFrom.LastName} — pick his partner", 11,
            _swapFrom == null ? Palette.InkDim : new Color(0.45f, 1f, 0.55f));

        float y = 58f;
        for (int i = 0; i < roster.BattingOrder.Count; i++)
        {
            var p = roster.BattingOrder[i];
            var row = new Rect2(left.Position + new Vector2(12f, y - 14f), new Vector2(left.Size.X - 24f, 26f));
            if (p == _swapFrom) DrawRect(row, Palette.PanelLight);

            // Which position he is filling, so a swap shows its consequence.
            string spot = roster.Starters.FirstOrDefault(kv => kv.Value == p).Value == p
                ? PlayerData.PositionLabel(roster.Starters.First(kv => kv.Value == p).Key)
                : "DH";

            Palette.Text(this, left.Position + new Vector2(20f, y), $"{i + 1}.", 13, Palette.InkDim);
            Palette.Text(this, left.Position + new Vector2(48f, y), p.Name, 14,
                p.IsInjured ? Palette.Warning : Palette.Ink);
            Palette.Text(this, left.Position + new Vector2(250f, y), spot, 12, Palette.InkDim);
            Palette.Text(this, left.Position + new Vector2(300f, y),
                p.IsInjured ? "INJURED" : $"OVR {p.Overall}", 12,
                p.IsInjured ? Palette.Warning : Palette.InkDim);

            var pick = p;
            _clicks.Add(row, () => Swap(roster, pick));

            // Arrows to move a hitter up or down the card.
            for (int dir = -1; dir <= 1; dir += 2)
            {
                int to = i + dir;
                if (to < 0 || to >= roster.BattingOrder.Count) continue;

                var arrow = new Rect2(left.Position + new Vector2(left.Size.X - 60f + (dir > 0 ? 26f : 0f), y - 12f),
                    new Vector2(22f, 20f));
                Palette.TextCentered(this, arrow.Position + arrow.Size * 0.5f, dir < 0 ? "^" : "v", 14, Palette.InkDim);

                int from = i, dest = to;
                _clicks.Add(arrow, () =>
                {
                    (roster.BattingOrder[from], roster.BattingOrder[dest]) =
                        (roster.BattingOrder[dest], roster.BattingOrder[from]);
                    Game.Instance.SaveLeague();
                });
            }
            y += 26f;
        }

        // --- Pitching staff ---
        var right = new Rect2(new Vector2(size.X * 0.52f + 20f, 146f),
            new Vector2(size.X * 0.48f - 60f, size.Y - 196f));
        Palette.Panel3D(this, right, Palette.Panel);
        Palette.Text(this, right.Position + new Vector2(20f, 28f), "PITCHING STAFF", 11, Palette.Highlight);
        Palette.Text(this, right.Position + new Vector2(150f, 28f), "click to bring him in", 11, Palette.InkDim);

        y = 58f;
        foreach (var arm in roster.Pitchers)
        {
            bool onMound = arm == roster.CurrentPitcher;
            var row = new Rect2(right.Position + new Vector2(12f, y - 14f), new Vector2(right.Size.X - 24f, 26f));
            if (onMound) DrawRect(row, Palette.PanelLight);

            Palette.Text(this, right.Position + new Vector2(20f, y), arm.Name, 14,
                arm.IsInjured ? Palette.Warning : Palette.Ink);
            Palette.Text(this, right.Position + new Vector2(230f, y),
                $"VEL {arm.PitchPower}  CMD {arm.PitchControl}  STA {arm.Stamina}", 11, Palette.InkDim);
            Palette.Text(this, right.Position + new Vector2(right.Size.X - 90f, y),
                arm.IsInjured ? "INJURED" : onMound ? "ON THE MOUND" : "available", 11,
                arm.IsInjured ? Palette.Warning : onMound ? Palette.Highlight : Palette.InkDim);

            var pick = arm;
            _clicks.Add(row, () =>
            {
                if (pick.IsInjured) return;          // a hurt arm cannot take the ball
                roster.SetPitcher(pick);
                Game.Instance.SaveLeague();
            });
            y += 26f;
        }

        Palette.Text(this, right.Position + new Vector2(20f, right.Size.Y - 16f),
            "Changes stick for the next game you play.", 11, Palette.InkDim);
    }

    /// <summary>
    /// Exchanges two players' places — both their spot in the order and the position they field,
    /// so moving your shortstop to leadoff does not quietly leave nobody at short.
    /// </summary>
    private void Swap(Roster roster, PlayerData p)
    {
        if (_swapFrom == null) { _swapFrom = p; return; }
        if (_swapFrom == p) { _swapFrom = null; return; }

        int a = roster.BattingOrder.IndexOf(_swapFrom);
        int b = roster.BattingOrder.IndexOf(p);
        if (a >= 0 && b >= 0) (roster.BattingOrder[a], roster.BattingOrder[b]) =
            (roster.BattingOrder[b], roster.BattingOrder[a]);

        var spotA = roster.Starters.FirstOrDefault(kv => kv.Value == _swapFrom);
        var spotB = roster.Starters.FirstOrDefault(kv => kv.Value == p);
        if (spotA.Value != null && spotB.Value != null)
        {
            roster.Starters[spotA.Key] = p;
            roster.Starters[spotB.Key] = _swapFrom;
        }

        _swapFrom = null;
        Game.Instance.SaveLeague();
    }

    // -----------------------------------------------------------------------
    // The player card — the screen that makes a career visible
    // -----------------------------------------------------------------------

    private void DrawPlayerCard(Vector2 size)
    {
        var p = _selected;
        var card = new Rect2(new Vector2(size.X * 0.5f - 320f, 120f), new Vector2(640f, size.Y - 200f));

        DrawRect(new Rect2(Vector2.Zero, size), new Color(0f, 0f, 0f, 0.55f));
        Palette.Panel3D(this, card, Palette.Panel);

        var club = _season.TeamOf(p) ?? Teams.Get(_teamCursor);
        Palette.Panel3D(this, new Rect2(card.Position, new Vector2(card.Size.X, 62f)), club.Primary);
        Palette.Text(this, card.Position + new Vector2(20f, 38f), $"#{p.Number}  {p.Name}", 24, Colors.White);
        Palette.Text(this, card.Position + new Vector2(card.Size.X - 210f, 26f),
            $"{PlayerData.PositionLabel(p.Position)}  ·  age {p.Age}", 13, Colors.White);
        Palette.Text(this, card.Position + new Vector2(card.Size.X - 210f, 46f),
            $"{club.FullName}", 12, new Color(1f, 1f, 1f, 0.75f));

        float y = 92f;

        // A written player has a reputation; a generated one has his archetype.
        // Every player has a line, written or not — a blank biography was the last thing that
        // marked a generated player out as filler.
        Palette.Text(this, card.Position + new Vector2(20f, y), Flavour.For(p), 14,
            p.IsLegend ? Palette.Highlight : new Color("#c8d4e2"));
        y += 18f;
        Palette.Text(this, card.Position + new Vector2(20f, y),
            $"{p.Archetype} · {p.PotentialGrade}", 11, Palette.InkDim);
        y += 30f;

        if (p.IsInjured)
        {
            Palette.Text(this, card.Position + new Vector2(20f, y),
                $"OUT — {p.Injury}, about {p.DaysOut} games", 14, Palette.Warning);
            y += 26f;
        }

        // --- Ratings ---
        y += 6f;
        Palette.Text(this, card.Position + new Vector2(20f, y), "RATINGS", 11, Palette.InkDim);
        y += 20f;

        var ratings = p.Position == Data.Position.P
            ? new (string, int)[] { ("VEL", p.PitchPower), ("CMD", p.PitchControl), ("STA", p.Stamina), ("FLD", p.Fielding) }
            : new (string, int)[] { ("CON", p.Contact), ("POW", p.Power), ("SPD", p.Speed), ("FLD", p.Fielding), ("ARM", p.Arm) };

        for (int i = 0; i < ratings.Length; i++)
        {
            float x = 20f + i * 118f;
            Palette.Text(this, card.Position + new Vector2(x, y), ratings[i].Item1, 11, Palette.InkDim);
            for (int pip = 0; pip < 10; pip++)
                DrawRect(new Rect2(card.Position + new Vector2(x + 34f + pip * 8f, y - 9f), new Vector2(6f, 10f)),
                    pip < ratings[i].Item2 ? club.Secondary : Palette.PanelLight);
        }
        y += 34f;

        Palette.Text(this, card.Position + new Vector2(20f, y),
            $"Overall {p.Overall}   ·   ceiling {p.Ceiling} ({p.PotentialGrade})" +
            (p.Upside > 0 ? $"   ·   {p.Upside} still to come" : "   ·   at his ceiling"),
            13, Palette.InkDim);
        y += 32f;

        // --- This season, and the career behind it ---
        int seasons = _season.Book.SeasonsPlayed(p);
        Palette.Text(this, card.Position + new Vector2(20f, y),
            seasons > 0 ? $"CAREER — {seasons} previous season{(seasons == 1 ? "" : "s")}" : "CAREER — rookie",
            11, Palette.InkDim);
        y += 22f;

        if (p.Position == Data.Position.P)
        {
            var now = _season.Book.Pitching(p);
            var all = _season.Book.CareerPitching(p);
            StatRow(card, ref y, new[] { "", "W", "L", "SV", "IP", "ERA", "WHIP", "K", "BB" }, header: true);
            StatRow(card, ref y, new[] { "this year", $"{now.Wins}", $"{now.Losses}", $"{now.Saves}",
                now.InningsText, now.Outs > 0 ? $"{now.Era:F2}" : "—", now.Outs > 0 ? $"{now.Whip:F2}" : "—",
                $"{now.Strikeouts}", $"{now.Walks}" });
            StatRow(card, ref y, new[] { "career", $"{all.Wins}", $"{all.Losses}", $"{all.Saves}",
                all.InningsText, all.Outs > 0 ? $"{all.Era:F2}" : "—", all.Outs > 0 ? $"{all.Whip:F2}" : "—",
                $"{all.Strikeouts}", $"{all.Walks}" });
        }
        else
        {
            var now = _season.Book.Batting(p);
            var all = _season.Book.CareerBatting(p);
            StatRow(card, ref y, new[] { "", "G", "AB", "H", "2B", "HR", "RBI", "BB", "AVG", "OPS" }, header: true);
            StatRow(card, ref y, new[] { "this year", $"{now.Games}", $"{now.AtBats}", $"{now.Hits}",
                $"{now.Doubles}", $"{now.HomeRuns}", $"{now.RunsBattedIn}", $"{now.Walks}",
                now.AtBats > 0 ? $"{now.Average:.000}" : "—", now.AtBats > 0 ? $"{now.Ops:.000}" : "—" });
            StatRow(card, ref y, new[] { "career", $"{all.Games}", $"{all.AtBats}", $"{all.Hits}",
                $"{all.Doubles}", $"{all.HomeRuns}", $"{all.RunsBattedIn}", $"{all.Walks}",
                all.AtBats > 0 ? $"{all.Average:.000}" : "—", all.AtBats > 0 ? $"{all.Ops:.000}" : "—" });
        }

        DrawSplits(card, ref y, p);
        DrawRecentForm(card, ref y, p);

        var close = new Rect2(card.Position + new Vector2(card.Size.X - 96f, card.Size.Y - 52f), new Vector2(76f, 34f));
        Palette.Panel3D(this, close, Palette.PanelLight);
        Palette.TextCentered(this, close.Position + close.Size * 0.5f, "CLOSE", 13, Palette.Ink);
        _clicks.Add(close, () => _selected = null);
    }

    /// <summary>
    /// The same season cut by hand, by ground and by the men on base.
    ///
    /// The simulation has always played the platoon matchup — a left-hander really does have a
    /// harder time against left-handed pitching here — but nothing wrote down which hand was on
    /// the mound, so the one thing the engine did best was the one thing you could not see. This
    /// is where a bench decision comes from: it is not interesting that a man is hitting .260, it
    /// is interesting that he is hitting .300 against right-handers and .190 against left.
    /// </summary>
    private void DrawSplits(Rect2 card, ref float y, PlayerData p)
    {
        bool arm = p.Position == Data.Position.P;
        var slices = new[]
        {
            Stats.Split.VsRight, Stats.Split.VsLeft,
            Stats.Split.AtHome, Stats.Split.OnRoad, Stats.Split.ScoringPosition,
        };

        y += 14f;
        Palette.Text(this, card.Position + new Vector2(20f, y), "THIS SEASON, BROKEN DOWN", 11,
            Palette.InkDim);
        y += 22f;

        StatRow(card, ref y, arm
            ? new[] { "", "IP", "ERA", "WHIP", "K", "BB", "H" }
            : new[] { "", "AB", "H", "HR", "RBI", "AVG", "OPS" }, header: true);

        var book = _season.Book.Splits;
        bool anything = false;

        foreach (var slice in slices)
        {
            string label = arm ? Stats.SplitBook.PitcherLabel(slice) : Stats.SplitBook.Label(slice);

            if (arm)
            {
                var line = book.HasPitching(p) ? book.Pitching(p).Peek(slice) : null;
                if (line == null || line.Outs == 0) continue;
                anything = true;
                StatRow(card, ref y, new[]
                {
                    label, line.InningsText, $"{line.Era:F2}", $"{line.Whip:F2}",
                    $"{line.Strikeouts}", $"{line.Walks}", $"{line.Hits}",
                });
            }
            else
            {
                var line = book.HasBatting(p) ? book.Batting(p).Peek(slice) : null;
                if (line == null || line.AtBats == 0) continue;
                anything = true;
                StatRow(card, ref y, new[]
                {
                    label, $"{line.AtBats}", $"{line.Hits}", $"{line.HomeRuns}",
                    $"{line.RunsBattedIn}", $"{line.Average:.000}", $"{line.Ops:.000}",
                });
            }
        }

        if (!anything)
            Palette.Text(this, card.Position + new Vector2(20f, y),
                "Nothing yet this season.", 12, Palette.InkDim);
    }

    /// <summary>
    /// His last ten nights, oldest to newest, on one line.
    ///
    /// A season total says a man is hitting .240. It cannot say whether he arrived there by
    /// hitting .240 every week or by going 2-for-40 in September, and those are different players
    /// to put in a lineup. This is the smallest thing that answers it.
    /// </summary>
    private void DrawRecentForm(Rect2 card, ref float y, PlayerData p)
    {
        var recent = _season.Logs.Recent(p.Id, 10);
        if (recent.Count == 0) return;

        y += 16f;
        Palette.Text(this, card.Position + new Vector2(20f, y),
            $"LAST {recent.Count} — most recent on the right", 11, Palette.InkDim);
        y += 20f;

        // Recent() hands them back newest first; a form line reads the other way.
        recent.Reverse();

        float x = 20f;
        foreach (var (game, line) in recent)
        {
            string cell;
            Color tint;

            if (line.Pitched)
            {
                var t = line.Pitching;
                cell = $"{t.InningsText}/{t.EarnedRuns}";
                tint = t.EarnedRuns == 0 ? Palette.Highlight
                     : t.EarnedRuns >= 4 ? Palette.Warning : Palette.Ink;
            }
            else
            {
                var b = line.Batting;
                cell = $"{b.Hits}-{b.AtBats}" + (b.HomeRuns > 0 ? "*" : "");
                tint = b.HomeRuns > 0 ? Palette.Highlight
                     : b.Hits == 0 && b.AtBats > 0 ? Palette.InkDim : Palette.Ink;
            }

            Palette.Text(this, card.Position + new Vector2(x, y), cell, 12, tint);
            x += 58f;
        }
    }

    private void StatRow(Rect2 card, ref float y, string[] cells, bool header = false)
    {
        float x = 20f;
        for (int i = 0; i < cells.Length; i++)
        {
            Palette.Text(this, card.Position + new Vector2(x, y), cells[i],
                header ? 11 : 13, header || i == 0 ? Palette.InkDim : Palette.Ink);
            x += i == 0 ? 84f : 58f;
        }
        y += header ? 20f : 24f;
    }

    // -----------------------------------------------------------------------
    // Injuries and history
    // -----------------------------------------------------------------------

    private void DrawInjuries(Vector2 size)
    {
        var panel = new Rect2(new Vector2(40f, 146f), new Vector2(size.X - 80f, size.Y - 196f));
        Palette.Panel3D(this, panel, Palette.Panel);

        var hurt = new List<(TeamData Club, PlayerData P)>();
        foreach (var t in Teams.All)
            foreach (var p in _season.RosterFor(t.Id).Players)
                if (p.IsInjured) hurt.Add((t, p));

        Palette.Text(this, panel.Position + new Vector2(20f, 30f),
            hurt.Count == 0 ? "Nobody in the league is hurt." : $"{hurt.Count} players on the shelf",
            13, Palette.InkDim);

        float y = 60f;
        int rows = Mathf.FloorToInt((panel.Size.Y - 76f) / 26f);

        foreach (var (club, p) in hurt.OrderByDescending(h => h.P.DaysOut).Take(rows))
        {
            bool mine = club.Id == _season.UserTeamId;
            DrawRect(new Rect2(panel.Position + new Vector2(12f, y - 12f), new Vector2(5f, 20f)),
                mine ? Palette.Highlight : club.Primary);

            Palette.Text(this, panel.Position + new Vector2(28f, y), club.Abbrev, 12, Palette.InkDim);
            Palette.Text(this, panel.Position + new Vector2(80f, y), p.Name, 14, mine ? Palette.Ink : Palette.InkDim);
            Palette.Text(this, panel.Position + new Vector2(300f, y), p.Injury, 13, Palette.InkDim);
            Palette.Text(this, panel.Position + new Vector2(520f, y), $"about {p.DaysOut} games", 13, Palette.Warning);

            var pick = p;
            _clicks.Add(new Rect2(panel.Position + new Vector2(12f, y - 14f), new Vector2(panel.Size.X - 24f, 24f)),
                () => _selected = pick);
            y += 26f;
        }
    }

    private void DrawHistory(Vector2 size)
    {
        var panel = new Rect2(new Vector2(40f, 146f), new Vector2(size.X - 80f, size.Y - 196f));
        Palette.Panel3D(this, panel, Palette.Panel);

        if (_season.History.Count == 0)
        {
            Palette.Text(this, panel.Position + new Vector2(20f, 40f),
                "No champions yet — the record book opens when you finish a season.", 15, Palette.InkDim);
            return;
        }

        Palette.Text(this, panel.Position + new Vector2(20f, 30f), "CHAMPIONS", 11, Palette.InkDim);

        // Bounded. A dynasty adds a champion every year for ever, and this drew all of them at
        // 28 pixels apiece — by year twenty it was writing off the bottom of the panel and out of
        // the window. The most recent are the ones anybody looks at.
        float y = 62f;
        int fits = Mathf.Max(3, (int)((panel.Size.Y - 80f) / 28f));
        var flags = _season.History.OrderByDescending(h => h.Year).ToList();

        if (flags.Count > fits)
            Palette.Text(this, panel.Position + new Vector2(panel.Size.X - 150f, 30f),
                $"most recent {fits} of {flags.Count}", 11, Palette.InkDim);

        foreach (var (year, teamId) in flags.Take(fits))
        {
            var club = Teams.Get(teamId);
            bool mine = teamId == _season.UserTeamId;

            DrawRect(new Rect2(panel.Position + new Vector2(12f, y - 14f), new Vector2(5f, 22f)), club.Primary);
            Palette.Text(this, panel.Position + new Vector2(30f, y), $"Year {year}", 14, Palette.InkDim);
            Palette.Text(this, panel.Position + new Vector2(120f, y), club.FullName, 16,
                mine ? Palette.Highlight : Palette.Ink);
            y += 28f;
        }

        // A club's own count, which is the number anyone actually cares about.
        int mineCount = _season.History.Count(h => h.TeamId == _season.UserTeamId);
        Palette.Text(this, panel.Position + new Vector2(20f, panel.Size.Y - 20f),
            $"{Teams.Get(_season.UserTeamId).FullName}: {mineCount} title{(mineCount == 1 ? "" : "s")} " +
            $"in {_season.History.Count} season{(_season.History.Count == 1 ? "" : "s")}",
            13, Palette.Highlight);
    }
}
