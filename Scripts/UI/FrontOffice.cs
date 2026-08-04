using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.Season;

namespace SandlotSlugfest.UI;

/// <summary>
/// The business end of running a club: the payroll, the winter market, the farm system, and what
/// the league remembers.
///
/// These four things share a screen because they are one decision seen from four sides. What you
/// can afford decides who you can sign; who you can sign decides whether you develop instead; and
/// the record book is the only thing that says whether any of it worked.
/// </summary>
public partial class FrontOffice : Control
{
    private enum Tab { Money, Market, Farm, History }

    private static readonly string[] TabNames = { "PAYROLL", "FREE AGENTS", "FARM SYSTEM", "RECORD BOOK" };

    private Tab _tab = Tab.Money;

    /// <summary>Which rung of the organisation the farm view is showing.</summary>
    private Farm.Level _farmLevel = Farm.Level.TripleA;
    private int _cursor;
    private string _notice = "";
    private float _noticeTimer;
    private SeasonState _season;
    private readonly ClickMap _clicks = new();

    public System.Action CloseOverlay;

    private void Leave()
    {
        if (CloseOverlay != null) { CloseOverlay(); return; }
        Game.Instance.GoTo("res://Scenes/Season.tscn");
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Ignore;
        _season = Game.Instance.League;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        // An armed signing does not stay armed. Coming back to the screen later and clicking once
        // should never be the click that commits four years of payroll.
        if (_armedTimer > 0f)
        {
            _armedTimer -= (float)delta;
            if (_armedTimer <= 0f) { _armed = null; QueueRedraw(); }
        }

        if (_noticeTimer <= 0f) return;
        _noticeTimer -= (float)delta;
        if (_noticeTimer <= 0f) { _notice = ""; QueueRedraw(); }
    }

    private void Say(string message)
    {
        _notice = message;
        _noticeTimer = 4.5f;
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion m) { if (_clicks.Hover(m.Position)) QueueRedraw(); return; }

        if (@event is InputEventMouseButton { Pressed: true } wheel &&
            wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            _scroll = Mathf.Max(0, _scroll + (wheel.ButtonIndex == MouseButton.WheelDown ? 3 : -3));
            _armed = null;      // the row under the cursor has moved; what was armed is not it now
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
            case Key.Left or Key.A:
                _tab = (Tab)Mathf.PosMod((int)_tab - 1, 4); _cursor = 0; _scroll = 0; break;
            case Key.Right or Key.D:
                _tab = (Tab)Mathf.PosMod((int)_tab + 1, 4); _cursor = 0; _scroll = 0; break;
            case Key.Up or Key.W: _scroll = Mathf.Max(0, _scroll - 1); break;
            case Key.Down or Key.S: _scroll++; break;
            case Key.Home: _scroll = 0; break;
        }
        QueueRedraw();
    }

    /// <summary>
    /// How far down a long list we have scrolled.
    ///
    /// None of these lists could scroll, and every one of them was longer than the screen. A club
    /// carries twenty-six men and the payroll tab drew twenty-four of them — starting from the
    /// best paid, so the two you could not see were the two on the minimum. The farm was worse:
    /// eighteen men in High-A pushed the SEND DOWN block off the bottom entirely.
    /// </summary>
    private int _scroll;

    /// <summary>
    /// What a second click would actually do. Signing a free agent commits real payroll for real
    /// years against a season you cannot roll back, and it happened on one click on a row a
    /// thousand pixels wide.
    /// </summary>
    private string _armed;
    private float _armedTimer;

    private bool Confirm(string key, string prompt)
    {
        if (_armed == key) { _armed = null; _armedTimer = 0f; return true; }

        _armed = key;
        _armedTimer = 4f;
        Say($"{prompt}  ·  click again to confirm.");
        return false;
    }

    /// <summary>
    /// Clamps the scroll to a list that has just been measured and reports where you are in it.
    /// Deriving the row count from the space actually left below <paramref name="y"/> means a list
    /// can never run off the bottom again, whatever else is drawn above or below it.
    /// </summary>
    private int Window(float y, float height, int total, float reserve = 0f)
    {
        int rows = Mathf.Max(4, (int)((height - 70f - reserve - y) / 19f));
        _scroll = Mathf.Clamp(_scroll, 0, Mathf.Max(0, total - rows));

        if (total > rows)
            Palette.Text(this, new Vector2(760f, y - 22f),
                $"{_scroll + 1}–{Mathf.Min(_scroll + rows, total)} of {total}  ·  scroll or Up/Down",
                12, Palette.InkDim);

        return rows;
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);
        _clicks.Begin();

        Palette.BackButton(this, size, _clicks, Leave);

        var club = Teams.Get(_season.UserTeamId);
        Palette.Text(this, new Vector2(40f, 46f), "FRONT OFFICE", 26, Palette.Ink);
        Palette.Text(this, new Vector2(40f, 68f),
            $"{club.FullName} · year {_season.Year}", 14, Palette.InkDim);

        DrawTabs(size);

        switch (_tab)
        {
            case Tab.Money: DrawMoney(size); break;
            case Tab.Market: DrawMarket(size); break;
            case Tab.Farm: DrawFarm(size); break;
            case Tab.History: DrawHistory(size); break;
        }

        if (_notice != "")
            Palette.Text(this, new Vector2(40f, size.Y - 44f), _notice, 14, Palette.Highlight);

        Palette.Text(this, new Vector2(40f, size.Y - 22f),
            "Left/Right to switch views  ·  click a name to act  ·  Esc to go back",
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
            _clicks.Add(rect, () => { _tab = picked; _cursor = 0; _scroll = 0; _armed = null; });
            x += w + 8f;
        }
    }

    // -----------------------------------------------------------------------
    // Payroll
    // -----------------------------------------------------------------------

    private void DrawMoney(Vector2 size)
    {
        var roster = _season.RosterFor(_season.UserTeamId);
        var books = _season.Books(_season.UserTeamId);
        int payroll = Contracts.Payroll(roster);
        int space = books.Budget - payroll;

        float y = 150f;
        Palette.Text(this, new Vector2(40f, y), "BUDGET", 13, Palette.InkDim);
        Palette.Text(this, new Vector2(150f, y), Contracts.Text(books.Budget), 16, Palette.Ink);

        Palette.Text(this, new Vector2(320f, y), "PAYROLL", 13, Palette.InkDim);
        Palette.Text(this, new Vector2(430f, y), Contracts.Text(payroll), 16, Palette.Ink);

        Palette.Text(this, new Vector2(600f, y), "ROOM", 13, Palette.InkDim);
        Palette.Text(this, new Vector2(700f, y), Contracts.Text(space), 16,
            space < 0 ? Palette.Warning : Palette.Highlight);

        Palette.Text(this, new Vector2(880f, y), "AVERAGE CROWD", 13, Palette.InkDim);
        Palette.Text(this, new Vector2(1030f, y), Attendance.Text(books.AverageCrowd), 16, Palette.Ink);

        // The bar makes the relationship between the two numbers immediate in a way the numbers
        // themselves do not.
        var bar = new Rect2(new Vector2(40f, y + 20f), new Vector2(size.X - 80f, 12f));
        DrawRect(bar, Palette.Panel);
        float fill = books.Budget <= 0 ? 0f : Mathf.Clamp(payroll / (float)books.Budget, 0f, 1.35f);
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * Mathf.Min(fill, 1f), bar.Size.Y)),
            fill > 1f ? Palette.Warning : Palette.Highlight);

        y += 56f;
        Header(y, "PLAYER", "JOB", "AGE", "OVR", "SALARY", "YEARS", "SERVICE");
        y += 22f;

        var paid = roster.Players.OrderByDescending(p => p.Salary).ToList();
        foreach (var p in paid.Skip(_scroll).Take(Window(y, size.Y, paid.Count)))
        {
            Row(y, p, p.RoleText, $"{p.Age}", $"{p.Overall}", Contracts.Text(p.Salary),
                p.ContractYears <= 1 ? "final" : $"{p.ContractYears}",
                ServiceText(p));
            y += 19f;
        }
    }

    /// <summary>Where a player is on the clock, which is the whole of his negotiating position.</summary>
    private static string ServiceText(PlayerData p)
    {
        if (p.ServiceYears >= Contracts.FreeAgentService) return "free agent eligible";
        if (p.ServiceYears >= Contracts.ArbitrationService) return $"arbitration ({p.ServiceYears}y)";
        return $"club control ({Contracts.ArbitrationService - p.ServiceYears}y to arb)";
    }

    // -----------------------------------------------------------------------
    // The winter market
    // -----------------------------------------------------------------------

    private void DrawMarket(Vector2 size)
    {
        float y = 150f;

        if (_season.FreeAgents.Count == 0)
        {
            Palette.Text(this, new Vector2(40f, y),
                "Nobody is on the market. Contracts run out at the end of the season.",
                15, Palette.InkDim);
            return;
        }

        int space = Finances.SpaceFor(_season, _season.UserTeamId);
        Palette.Text(this, new Vector2(40f, y),
            $"{_season.FreeAgents.Count} available  ·  you have {Contracts.Text(space)} of room  " +
            $"·  roster {_season.RosterFor(_season.UserTeamId).Players.Count}/{Development.RosterLimit}",
            14, Palette.InkDim);

        y += 30f;
        Header(y, "PLAYER", "POS", "AGE", "OVR", "ASKING", "CEILING", "");
        y += 22f;

        var ranked = _season.FreeAgents.OrderByDescending(Contracts.MarketValue).ToList();
        var market = ranked.Skip(_scroll).Take(Window(y, size.Y, ranked.Count)).ToList();

        foreach (var p in market)
        {
            int asking = Contracts.MarketValue(p);
            var rect = new Rect2(new Vector2(34f, y - 13f), new Vector2(size.X - 68f, 19f));
            string key = $"sign:{p.Id}";

            if (_armed == key) DrawRect(rect, Palette.Highlight.Darkened(0.62f));

            Row(y, p, PlayerData.PositionLabel(p.Position), $"{p.Age}", $"{p.Overall}",
                Contracts.Text(asking), p.PotentialGrade, _armed == key ? "CONFIRM" : "SIGN");

            var target = p;
            int offer = asking;
            _clicks.Add(rect, () =>
            {
                var rng = new Rng(target.Id * 31 + _season.Year);
                int years = Contracts.DesiredYears(target, ref rng);

                // A multi-year contract against your payroll is not something to do by accident.
                if (!Confirm(key, $"Sign {target.Name} for {years} year(s) at " +
                                  $"{Contracts.Text(offer)} a year?")) return;

                string refused = FreeAgency.UserSign(_season, target, offer, years);
                Say(refused ?? $"Signed {target.Name} — {years} year(s) at " +
                               $"{Contracts.Text(offer)} a year.");
            });
            y += 19f;
        }
    }

    // -----------------------------------------------------------------------
    // The farm
    // -----------------------------------------------------------------------

    private void DrawFarm(Vector2 size)
    {
        var roster = _season.RosterFor(_season.UserTeamId);
        float y = 150f;

        // A rung selector. The lower two levels have been simulating and developing all along;
        // there was simply no way to look at them, which is the worst state for a system to be in.
        float bx = 40f;
        foreach (var level in Farm.Levels)
        {
            bool on = level == _farmLevel;
            // Every rung carries its own occupancy, so which affiliate has room is readable
            // without clicking through all three.
            string label = $"{Farm.Name(level).ToUpperInvariant()}  " +
                           $"{Farm.SpotsText(_season.UserTeamId, level)}";
            float bw = Palette.TextWidth(label, 12) + 26f;
            var brect = new Rect2(new Vector2(bx, y - 14f), new Vector2(bw, 26f));

            bool full = Farm.Free(_season.UserTeamId, level) <= 0;
            Palette.Panel3D(this, brect, on ? Palette.Highlight.Darkened(0.2f) : Palette.Panel);
            Palette.TextCentered(this, brect.Position + brect.Size * 0.5f, label, 12,
                on ? Palette.Night : full ? Palette.Warning : Palette.InkDim);

            var picked = level;
            _clicks.Add(brect, () => { _farmLevel = picked; _scroll = 0; });
            bx += bw + 8f;
        }

        var farm = Farm.Of(_season.UserTeamId, _farmLevel);

        // The affiliate's own season: a record, a place in the table, and who it plays tonight.
        var standing = FarmSeason.Of(_season.UserTeamId, _farmLevel);
        var fixture = FarmSeason.Today(_season, _season.UserTeamId);

        Palette.Text(this, new Vector2(bx + 16f, y),
            $"{standing.Text}  ·  {Ordinal(FarmSeason.RankOf(_season.UserTeamId, _farmLevel))} " +
            $"at this level  ·  {Farm.Free(_season.UserTeamId, _farmLevel)} spots free",
            13, Palette.InkDim);

        y += 22f;

        if (fixture == null)
        {
            Palette.Text(this, new Vector2(40f, y), "No game today — the affiliate is off too.",
                13, Palette.InkDim);
        }
        else
        {
            bool home = fixture.HomeId == _season.UserTeamId;
            var other = Teams.Get(home ? fixture.AwayId : fixture.HomeId);
            Palette.Text(this, new Vector2(40f, y),
                $"TONIGHT: {(home ? "vs" : "at")} {other.FullName} ({Farm.Name(_farmLevel)})",
                13, Palette.Highlight);
        }

        // Take the dugout at this level, or just go and watch.
        bool playable = fixture != null && Farm.BuildRoster(_season.UserTeamId, _farmLevel) != null;
        DrawFarmGameButtons(size, y, playable);

        y += 26f;
        Header(y, "PROSPECT", "POS", "AGE", "OVR", "SCOUTS SAY",
            $"{Farm.Name(_farmLevel).ToUpperInvariant()} LINE", "");
        y += 22f;

        var prospects = farm.OrderByDescending(p => p.Ceiling)
                            .ThenByDescending(p => p.Overall).ToList();

        // Leave room for the SEND DOWN block below, which eighteen High-A men used to push clean
        // off the bottom of the screen.
        int rows = Window(y, size.Y, prospects.Count, reserve: 160f);

        foreach (var p in prospects.Skip(_scroll).Take(rows))
        {
            var rect = new Rect2(new Vector2(34f, y - 13f), new Vector2(520f, 19f));

            Row(y, p, PlayerData.PositionLabel(p.Position), $"{p.Age}", $"{p.Overall}",
                Scouting.Report(_season.UserTeamId, p), MinorLine(p), "CALL UP");

            var target = p;
            _clicks.Add(rect, () =>
            {
                if (roster.Players.Count >= Development.RosterLimit)
                {
                    Say($"The roster is full at {Development.RosterLimit}. Send someone down first.");
                    return;
                }
                Say(Farm.CallUp(_season, _season.UserTeamId, target)
                    ? $"{target.Name} is called up."
                    : $"{target.Name} could not be promoted.");
            });

            // A man can also be moved a rung rather than all the way to the big club — pushing a
            // young arm up to Double-A is most of what running a farm system actually is.
            DrawRungButtons(y, p);
            y += 19f;
        }

        // And the other direction: the big club's least useful man can be optioned out — to the
        // rung being looked at, rather than always to Triple-A.
        y += 14f;
        Palette.Text(this, new Vector2(40f, y),
            $"SEND DOWN TO {Farm.Name(_farmLevel).ToUpperInvariant()}", 13, Palette.Highlight);
        y += 22f;

        foreach (var p in roster.Players.OrderBy(p => p.Overall).Take(5))
        {
            var rect = new Rect2(new Vector2(34f, y - 13f), new Vector2(size.X - 68f, 19f));
            Row(y, p, p.RoleText, $"{p.Age}", $"{p.Overall}", Contracts.Text(p.Salary),
                p.PotentialGrade, "OPTION");

            var target = p;
            var to = _farmLevel;
            _clicks.Add(rect, () =>
            {
                if (Farm.Free(_season.UserTeamId, to) <= 0)
                {
                    Say($"{Farm.Name(to)} is full at {Farm.Spots(to)}. " +
                        "Move somebody up or pick another level.");
                    return;
                }
                Say(Farm.SendDown(_season, _season.UserTeamId, target, to)
                    ? $"{target.Name} is sent to {Farm.Name(to)}."
                    : "You can't go any thinner than that.");
            });
            y += 19f;
        }
    }

    /// <summary>
    /// PLAY and WATCH, for a game at the rung being looked at.
    ///
    /// Both do the same thing to the same two sides; the only difference is whether a human is in
    /// the dugout. Watching matters as much as playing — in a dynasty the point of a farm system
    /// is the kid you drafted three years ago, and being able to go and see him is the difference
    /// between a prospect and a row in a table.
    /// </summary>
    private void DrawFarmGameButtons(Vector2 size, float y, bool playable)
    {
        Button("PLAY A GAME", size.X - 420f, false);
        Button("WATCH A GAME", size.X - 240f, true);

        void Button(string label, float x, bool watch)
        {
            var rect = new Rect2(new Vector2(x, y - 17f), new Vector2(170f, 30f));
            Palette.Panel3D(this, rect, playable ? Palette.Highlight.Darkened(0.2f) : Palette.Panel);
            Palette.TextCentered(this, rect.Position + rect.Size * 0.5f, label, 12,
                playable ? Palette.Night : Palette.InkDim);

            if (!playable) return;
            _clicks.Add(rect, () => StartFarmGame(watch));
        }
    }

    /// <summary>
    /// Puts a farm fixture on and goes to it.
    ///
    /// The result is deliberately not booked against the season. The affiliate's year is
    /// simulated as a whole at the end of the season, and folding one hand-played game into that
    /// would double-count it — a prospect would get credit for a night you were there and for the
    /// same night in the simulation. This is a game you go to, not a game that rewrites the books.
    /// </summary>
    private void StartFarmGame(bool watch)
    {
        var g = Game.Instance;

        // Tonight's actual fixture, not an exhibition. The affiliate travels with the big club, so
        // the opponent and the home field are the ones on the schedule.
        var fixture = FarmSeason.Today(_season, _season.UserTeamId);
        if (fixture == null) { Say("There is no game today."); return; }

        bool home = fixture.HomeId == _season.UserTeamId;
        int opponentId = home ? fixture.AwayId : fixture.HomeId;

        var mine = Farm.BuildRoster(_season.UserTeamId, _farmLevel);
        var theirs = Farm.BuildRoster(opponentId, _farmLevel);

        if (mine == null || theirs == null)
        {
            Say($"One of the two {Farm.Name(_farmLevel)} sides cannot field nine tonight.");
            return;
        }

        var away = home ? theirs : mine;
        var homeSide = home ? mine : theirs;
        int opponent = opponentId;

        // The day's result was modelled when the day advanced. Going to the game means taking that
        // one back out, so attending does not quietly give the affiliate two games.
        g.FarmReplacing = (_season.UserTeamId, opponentId, (int)_farmLevel,
            FarmSeason.ModelledResult(_season, _season.UserTeamId, opponentId, _farmLevel,
                _season.CurrentDay, home));

        g.PendingSeasonGame = null;
        g.CardClubRoster = null;
        g.ReturnTo = "res://Scenes/FrontOffice.tscn";
        g.FarmAwayRoster = away;
        g.FarmHomeRoster = homeSide;
        g.FarmLevelName = Farm.Name(_farmLevel);
        g.AwayTeamId = home ? opponent : _season.UserTeamId;
        g.HomeTeamId = home ? _season.UserTeamId : opponent;

        // Watching is a computer game with nobody's hands on it; playing puts you in your own
        // dugout whichever side of the fixture your affiliate is on.
        g.Mode = watch ? ControlMode.CpuVsCpu
               : home ? ControlMode.CpuVsPlayer     // your affiliate bats in the bottom
               : ControlMode.PlayerVsCpu;           // and in the top when it is on the road

        g.GoTo("res://Scenes/Game.tscn");
    }

    /// <summary>
    /// The two small buttons that move a prospect one rung, in either direction.
    ///
    /// Calling a man straight up from High-A is not how an organisation is run — you push him to
    /// the next level and see whether he holds. Without this the three rungs were somewhere to
    /// look rather than something to manage.
    /// </summary>
    private void DrawRungButtons(float y, PlayerData p)
    {
        int at = System.Array.IndexOf(Farm.Levels, _farmLevel);

        // Levels run best-first, so the rung above is the earlier index.
        Rung(760f, "▲", at - 1);
        Rung(792f, "▼", at + 1);

        void Rung(float x, string glyph, int index)
        {
            if (index < 0 || index >= Farm.Levels.Length) return;

            var to = Farm.Levels[index];
            bool room = Farm.Free(_season.UserTeamId, to) > 0;
            var rect = new Rect2(new Vector2(x, y - 13f), new Vector2(26f, 18f));

            Palette.Panel3D(this, rect, room ? Palette.Panel : Palette.Panel.Darkened(0.3f));
            Palette.TextCentered(this, rect.Position + rect.Size * 0.5f, glyph, 11,
                room ? Palette.Highlight : Palette.InkDim);

            var target = p;
            _clicks.Add(rect, () =>
            {
                if (!room) { Say($"{Farm.Name(to)} is full at {Farm.Spots(to)}."); return; }
                Say(Farm.Move(_season.UserTeamId, target, to)
                    ? $"{target.Name} moves to {Farm.Name(to)}."
                    : $"{target.Name} could not be moved.");
            });
        }
    }

    /// <summary>"1st", "2nd", "23rd" — for where an affiliate sits in its table.</summary>
    private static string Ordinal(int n)
    {
        if (n % 100 is >= 11 and <= 13) return $"{n}th";
        return (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" };
    }

    private string MinorLine(PlayerData p)
    {
        if (!_season.Book.HasMinorLine(p)) return "no record";

        if (p.Position == Data.Position.P)
        {
            var t = _season.Book.MinorPitching(p);
            if (t.Outs == 0) return "no record";
            return $"{t.Wins}-{t.Losses}, {t.Era:F2}, {t.Strikeouts}K in {t.InningsText}";
        }

        var b = _season.Book.MinorBatting(p);
        if (b.AtBats == 0) return "no record";
        return $"{Stats.BattingLine.Rate(b.Average)}, {b.HomeRuns} HR, {b.RunsBattedIn} RBI";
    }

    // -----------------------------------------------------------------------
    // The record book
    // -----------------------------------------------------------------------

    private void DrawHistory(Vector2 size)
    {
        float y = 150f;
        float half = size.X * 0.5f;

        Palette.Text(this, new Vector2(40f, y), "LAST SEASON'S AWARDS", 13, Palette.Highlight);
        float ay = y + 24f;

        var recent = _season.Annals.Awards
            .OrderByDescending(a => a.Year)
            .Take(10)
            .ToList();

        if (recent.Count == 0)
            Palette.Text(this, new Vector2(40f, ay), "Nothing handed out yet — see the season through.",
                14, Palette.InkDim);

        foreach (var a in recent)
        {
            Palette.Text(this, new Vector2(40f, ay), $"{a.Year}", 12, Palette.InkDim);
            Palette.Text(this, new Vector2(76f, ay), a.Award, 12, Palette.InkDim);
            Palette.Text(this, new Vector2(320f, ay), a.PlayerName, 13, Palette.Ink);
            Palette.Text(this, new Vector2(470f, ay), Teams.Get(a.TeamId).Abbrev, 12, Palette.InkDim);
            ay += 19f;
        }

        // Single-season records.
        Palette.Text(this, new Vector2(half, y), "SINGLE-SEASON RECORDS", 13, Palette.Highlight);
        float ry = y + 24f;

        foreach (var (stat, mark) in _season.Annals.Records.OrderBy(kv => kv.Key))
        {
            Palette.Text(this, new Vector2(half, ry), stat, 12, Palette.InkDim);
            Palette.Text(this, new Vector2(half + 170f, ry),
                stat is "Batting average" or "Earned run average" ? $"{mark.Value:F3}" : $"{mark.Value:F0}",
                13, Palette.Ink);
            Palette.Text(this, new Vector2(half + 240f, ry), mark.PlayerName, 12, Palette.Ink);
            Palette.Text(this, new Vector2(half + 400f, ry), $"{mark.Year}", 12, Palette.InkDim);
            ry += 19f;
        }

        // The hall.
        float hy = Mathf.Max(ay, ry) + 26f;
        Palette.Text(this, new Vector2(40f, hy), "HALL OF FAME", 13, Palette.Highlight);
        hy += 24f;

        if (_season.Annals.Hall.Count == 0)
            Palette.Text(this, new Vector2(40f, hy),
                "Empty. It takes a whole career, and nobody has finished one yet.", 14, Palette.InkDim);

        foreach (var h in _season.Annals.Hall.OrderByDescending(h => h.Year).Take(8))
        {
            Palette.Text(this, new Vector2(40f, hy), $"{h.Year}", 12, Palette.InkDim);
            Palette.Text(this, new Vector2(76f, hy), h.Name, 13, Palette.Ink);
            Palette.Text(this, new Vector2(240f, hy), h.Position, 12, Palette.InkDim);
            Palette.Text(this, new Vector2(290f, hy), h.Career, 12, Palette.InkDim);
            hy += 19f;
        }
    }

    // -----------------------------------------------------------------------

    private void Header(float y, params string[] columns)
    {
        float[] x = { 40f, 250f, 360f, 410f, 460f, 580f, 760f };
        for (int i = 0; i < columns.Length && i < x.Length; i++)
            Palette.Text(this, new Vector2(x[i], y), columns[i], 12, Palette.InkDim);
    }

    private void Row(float y, PlayerData p, params string[] cells)
    {
        float[] x = { 250f, 360f, 410f, 460f, 580f, 760f };

        Palette.Text(this, new Vector2(40f, y), p.Name, 13, Palette.Ink);
        for (int i = 0; i < cells.Length && i < x.Length; i++)
            Palette.Text(this, new Vector2(x[i], y), cells[i], 12,
                i == cells.Length - 1 && cells[i] is "SIGN" or "CALL UP" or "OPTION"
                    ? Palette.Highlight
                    : Palette.InkDim);
    }
}
