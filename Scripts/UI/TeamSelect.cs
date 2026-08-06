using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.UI;

/// <summary>
/// The matchup screen: pick the visitors, pick the home club, set the length and who is
/// holding the controller. Teams are laid out four divisions across, eight clubs deep.
/// </summary>
public partial class TeamSelect : Control
{
    private const string SelectionPath = "user://settings.cfg";
    private enum Stage { Away, Home, Settings }

    private Stage _stage = Stage.Away;
    private int _cursor;
    private int _awayPick = -1;
    private int _homePick = -1;
    private int _settingRow;
    private float _time;

    private static readonly int[] InningOptions = { 3, 6, 9 };
    private int _inningIndex = 1;
    private ControlMode _mode = ControlMode.PlayerVsCpu;

    private const int Columns = 4;
    private const int Rows = 8;

    private readonly ClickMap _clicks = new();

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        // The Control swallows every mouse event at its default filter, so _UnhandledInput never
        // saw a click. This screen registers click targets for every club and none of them worked:
        // it was keyboard-only without anybody deciding it should be.
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(true);
        var cfg = new ConfigFile();
        if (cfg.Load(SelectionPath) == Error.Ok && (bool)cfg.GetValue("exhibition", "saved", false))
        {
            _awayPick = Mathf.Clamp((int)cfg.GetValue("exhibition", "away", Game.Instance.AwayTeamId), 0, Teams.All.Count - 1);
            _homePick = Mathf.Clamp((int)cfg.GetValue("exhibition", "home", Game.Instance.HomeTeamId), 0, Teams.All.Count - 1);
            if (_homePick == _awayPick) _homePick = (_awayPick + 1) % Teams.All.Count;
            int innings = (int)cfg.GetValue("exhibition", "innings", 6);
            _inningIndex = Mathf.Max(0, System.Array.IndexOf(InningOptions, innings));
            _mode = (ControlMode)Mathf.Clamp((int)cfg.GetValue("exhibition", "mode", 0), 0, 5);
            Game.Instance.AutoFielding = (bool)cfg.GetValue("exhibition", "auto_fielding", true);
            _cursor = _homePick; _stage = Stage.Settings; _settingRow = 3;
        }
        else _cursor = Game.Instance.AwayTeamId;
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Fully mouse-operable: hover highlights, click commits.
        if (@event is InputEventMouseMotion motion)
        {
            if (_clicks.Hover(motion.Position)) QueueRedraw();
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            if (_clicks.Click(mb.Position)) QueueRedraw();
            return;
        }

        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        if (key.PhysicalKeycode == Key.Escape)
        {
            if (_stage == Stage.Away) Game.Instance.GoTo("res://Scenes/MainMenu.tscn");
            else if (_stage == Stage.Home) { _stage = Stage.Away; _cursor = _awayPick; }
            else { _stage = Stage.Home; _cursor = _homePick; }
            QueueRedraw();
            return;
        }

        if (_stage == Stage.Settings) HandleSettingsKey(key);
        else HandleGridKey(key);

        QueueRedraw();
    }

    private void HandleGridKey(InputEventKey key)
    {
        int col = _cursor / Rows;
        int row = _cursor % Rows;

        switch (key.PhysicalKeycode)
        {
            case Key.Left or Key.A: col = (col - 1 + Columns) % Columns; break;
            case Key.Right or Key.D: col = (col + 1) % Columns; break;
            case Key.Up or Key.W: row = (row - 1 + Rows) % Rows; break;
            case Key.Down or Key.S: row = (row + 1) % Rows; break;
            case Key.Enter or Key.KpEnter or Key.Space:
                Confirm();
                return;
        }
        _cursor = col * Rows + row;
    }

    private void Confirm()
    {
        if (_stage == Stage.Away)
        {
            _awayPick = _cursor;
            _stage = Stage.Home;
            // Default the home cursor somewhere other than the club just chosen.
            _cursor = _awayPick == 31 ? 0 : _awayPick + 1;
        }
        else if (_stage == Stage.Home)
        {
            if (_cursor == _awayPick) return;    // a club cannot play itself
            _homePick = _cursor;
            _stage = Stage.Settings;
            _settingRow = 3;      // land on PLAY BALL, so Enter is the obvious next press
        }
    }

    private void HandleSettingsKey(InputEventKey key)
    {
        switch (key.PhysicalKeycode)
        {
            case Key.Up or Key.W: _settingRow = (_settingRow - 1 + 4) % 4; break;
            case Key.Down or Key.S: _settingRow = (_settingRow + 1) % 4; break;
            case Key.Left or Key.A: NudgeSetting(-1); break;
            case Key.Right or Key.D: NudgeSetting(1); break;
            case Key.Enter or Key.KpEnter or Key.Space:
                // Starting the game is the only action on this screen, so Enter always does it.
                // Requiring the cursor to be parked on PLAY BALL first just looked frozen.
                StartGame();
                break;
        }
    }

    private void NudgeSetting(int delta)
    {
        if (_settingRow == 0)
        {
            _inningIndex = Mathf.PosMod(_inningIndex + delta, InningOptions.Length);
        }
        else if (_settingRow == 2)
        {
            Game.Instance.AutoFielding = !Game.Instance.AutoFielding;
        }
        else if (_settingRow == 1)
        {
            var modes = new[]
            {
                ControlMode.PlayerVsCpu, ControlMode.CpuVsPlayer,
                ControlMode.BatOnlyAway, ControlMode.BatOnlyHome,
                ControlMode.PlayerVsPlayer, ControlMode.CpuVsCpu,
            };
            int i = System.Array.IndexOf(modes, _mode);
            _mode = modes[Mathf.PosMod(i + delta, modes.Length)];
        }
    }

    private void StartGame()
    {
        var g = Game.Instance;
        g.AwayTeamId = _awayPick;
        g.HomeTeamId = _homePick;
        g.Innings = InningOptions[_inningIndex];
        g.Mode = _mode;
        var cfg = new ConfigFile(); cfg.Load(SelectionPath);
        cfg.SetValue("exhibition", "saved", true); cfg.SetValue("exhibition", "away", _awayPick);
        cfg.SetValue("exhibition", "home", _homePick); cfg.SetValue("exhibition", "innings", InningOptions[_inningIndex]);
        cfg.SetValue("exhibition", "mode", (int)_mode); cfg.SetValue("exhibition", "auto_fielding", g.AutoFielding);
        cfg.Save(SelectionPath);
        g.GoTo("res://Scenes/Game.tscn");
    }

    // -----------------------------------------------------------------------
    // Drawing
    // -----------------------------------------------------------------------

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);
        _clicks.Begin();

        // Escape has always worked here and nothing on screen said so. A way out you have to
        // already know about is not a way out.
        Palette.BackButton(this, size, _clicks, () =>
        {
            if (_stage == Stage.Away) Game.Instance.GoTo("res://Scenes/MainMenu.tscn");
            else { _stage = Stage.Away; QueueRedraw(); }
        });

        string title = _stage switch
        {
            Stage.Away => "CHOOSE THE VISITORS",
            Stage.Home => "CHOOSE THE HOME CLUB",
            _ => "SET THE MATCHUP",
        };
        Palette.Text(this, new Vector2(40f, 52f), title, 30, Palette.Ink);

        if (_stage == Stage.Settings)
        {
            DrawSettings(size);
            return;
        }

        DrawGrid(size);
        DrawPreview(size, Teams.Get(_cursor));

        string help = _stage == Stage.Away
            ? "Arrows to move  ·  Enter to lock in the visitors  ·  Esc to go back"
            : "Arrows to move  ·  Enter to lock in the home club  ·  Esc to go back";
        Palette.Text(this, new Vector2(40f, size.Y - 24f), help, 16, Palette.InkDim);

        if (_awayPick >= 0)
        {
            var away = Teams.Get(_awayPick);
            Palette.Text(this, new Vector2(size.X - 360f, 52f),
                $"Visitors: {away.FullName}", 18, away.Primary.Lightened(0.35f));
        }
    }

    private void DrawGrid(Vector2 size)
    {
        const float cellW = 176f;
        const float cellH = 52f;
        const float gapX = 10f;
        const float gapY = 6f;
        float startX = 40f;
        float startY = 108f;

        for (int col = 0; col < Columns; col++)
        {
            var first = Teams.Get(col * Rows);
            string header = $"{(first.League == League.American ? "AL" : "NL")} {first.Division.ToString().ToUpperInvariant()}";
            Palette.Text(this, new Vector2(startX + col * (cellW + gapX), startY - 12f),
                header, 15, Palette.InkDim);

            for (int row = 0; row < Rows; row++)
            {
                int id = col * Rows + row;
                var team = Teams.Get(id);
                var rect = new Rect2(
                    new Vector2(startX + col * (cellW + gapX), startY + row * (cellH + gapY)),
                    new Vector2(cellW, cellH));

                bool hovered = id == _cursor;
                bool takenByAway = _stage == Stage.Home && id == _awayPick;

                int pick = id;
                _clicks.Add(rect,
                    onClick: () => { _cursor = pick; Confirm(); },
                    onHover: () => _cursor = pick);

                Color fill = team.Primary;
                if (takenByAway) fill = fill.Darkened(0.6f);
                Palette.Panel3D(this, rect, fill);

                // Trim stripe in the club's secondary colour.
                DrawRect(new Rect2(rect.Position + new Vector2(0f, rect.Size.Y - 5f),
                    new Vector2(rect.Size.X, 5f)), team.Secondary);

                if (hovered)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(_time * 6f);
                    DrawRect(rect, Palette.Highlight.Lerp(Palette.Ink, pulse), false, 3f);
                }

                var textColor = takenByAway ? Palette.InkDim : team.TextOnPrimary;
                Palette.Text(this, rect.Position + new Vector2(10f, 21f), team.Abbrev, 17, textColor);
                Palette.Text(this, rect.Position + new Vector2(52f, 21f), team.City, 14, textColor);
                Palette.Text(this, rect.Position + new Vector2(52f, 38f), team.Nickname, 16, textColor);
            }
        }
    }

    private void DrawPreview(Vector2 size, TeamData team)
    {
        float x = size.X - 340f;
        var panel = new Rect2(new Vector2(x, 100f), new Vector2(300f, size.Y - 160f));
        Palette.Panel3D(this, panel, Palette.Panel);

        DrawRect(new Rect2(panel.Position, new Vector2(panel.Size.X, 64f)), team.Primary);
        DrawRect(new Rect2(panel.Position + new Vector2(0f, 60f), new Vector2(panel.Size.X, 4f)), team.Secondary);

        Palette.Text(this, panel.Position + new Vector2(14f, 26f), team.City, 16, team.TextOnPrimary);
        Palette.Text(this, panel.Position + new Vector2(14f, 50f), team.Nickname.ToUpperInvariant(), 22,
            team.TextOnPrimary);

        // Wrapped, not clipped — several mottos are longer than the panel is wide.
        DrawMultilineString(Palette.Font, panel.Position + new Vector2(14f, 88f), team.Motto,
            HorizontalAlignment.Left, panel.Size.X - 28f, 13, 2, Palette.InkDim);

        // Read from the live league so any completed trades show up here.
        var roster = Game.Instance.League.RosterFor(team.Id);

        float y = panel.Position.Y + 124f;
        Palette.Text(this, new Vector2(panel.Position.X + 14f, y), "STARTING NINE", 13, Palette.Highlight);
        y += 20f;

        foreach (var p in roster.BattingOrder)
        {
            // Show the spot he is playing, not the one he came up at.
            Palette.Text(this, new Vector2(panel.Position.X + 14f, y),
                PlayerData.PositionLabel(roster.SlotOf(p)), 13, Palette.InkDim);
            Palette.Text(this, new Vector2(panel.Position.X + 44f, y), p.ShortName, 14, Palette.Ink);
            DrawPips(new Vector2(panel.Position.X + 196f, y - 5f), p.Overall, team.Secondary);
            Palette.Text(this, new Vector2(panel.Position.X + 44f, y + 12f), p.ArchetypeText, 10,
                Palette.InkDim);
            y += 30f;
        }

        y += 12f;
        Palette.Text(this, new Vector2(panel.Position.X + 14f, y), "ON THE MOUND", 13, Palette.Highlight);
        y += 20f;
        var ace = roster.Pitchers[0];
        Palette.Text(this, new Vector2(panel.Position.X + 14f, y), ace.ShortName, 14, Palette.Ink);
        DrawPips(new Vector2(panel.Position.X + 196f, y - 5f), ace.Overall, team.Secondary);
        y += 19f;
        Palette.Text(this, new Vector2(panel.Position.X + 14f, y),
            $"Velocity {ace.PitchPower}  ·  Command {ace.PitchControl}", 12, Palette.InkDim);

        // Signature moves are the fun part; list whoever has one.
        y += 30f;
        var specials = roster.Players.Where(p => p.Special != Special.None).Take(4).ToList();
        if (specials.Count > 0)
        {
            Palette.Text(this, new Vector2(panel.Position.X + 14f, y), "SIGNATURE MOVES", 13, Palette.Highlight);
            y += 20f;
            foreach (var p in specials)
            {
                Palette.Text(this, new Vector2(panel.Position.X + 14f, y),
                    $"{p.ShortName} — {p.SpecialText}", 12, Palette.Ink);
                y += 17f;
            }
        }
    }

    private void DrawPips(Vector2 at, int value, Color color)
    {
        for (int i = 0; i < 10; i++)
        {
            var rect = new Rect2(at + new Vector2(i * 9f, 0f), new Vector2(6f, 10f));
            DrawRect(rect, i < value ? color : Palette.PanelLight);
        }
    }

    private void DrawSettings(Vector2 size)
    {
        var away = Teams.Get(_awayPick);
        var home = Teams.Get(_homePick);

        float cx = size.X * 0.5f;
        float top = 130f;

        DrawMatchupCard(new Vector2(cx - 330f, top), away, "VISITORS");
        DrawMatchupCard(new Vector2(cx + 50f, top), home, "HOME");
        Palette.TextCentered(this, new Vector2(cx, top + 90f), "at", 26, Palette.InkDim);

        string[] labels = { "Innings", "Controls", "Fielding", "" };
        string[] values =
        {
            InningOptions[_inningIndex].ToString(),
            ModeLabel(_mode),
            Game.Instance.AutoFielding ? "Automatic" : "You control fielders",
            "PLAY BALL",
        };

        float y = top + 240f;
        for (int i = 0; i < 4; i++)
        {
            bool on = i == _settingRow;
            var color = on ? Palette.Highlight : Palette.Ink;

            if (i == 3)
            {
                var rect = new Rect2(new Vector2(cx - 140f, y - 4f), new Vector2(280f, 50f));
                Palette.Panel3D(this, rect, on ? Palette.Highlight : Palette.PanelLight);
                if (on) DrawRect(rect, Palette.Ink, false, 3f);
                Palette.TextCentered(this, new Vector2(cx, y + 28f), values[i], 26,
                    on ? Palette.Night : Palette.Ink);

                _clicks.Add(rect, StartGame, () => _settingRow = 3);
            }
            else
            {
                // A filled bar behind the active row, so which one has focus is never in doubt.
                if (on)
                    DrawRect(new Rect2(new Vector2(cx - 250f, y + 2f), new Vector2(500f, 32f)),
                        Palette.PanelLight);

                Palette.Text(this, new Vector2(cx - 240f, y + 24f), labels[i], 20,
                    on ? Palette.Ink : Palette.InkDim);

                // Clickable arrows either side of the value, so the mouse can change settings.
                var left = new Rect2(new Vector2(cx + 8f, y + 2f), new Vector2(34f, 32f));
                var right = new Rect2(new Vector2(cx + 210f, y + 2f), new Vector2(34f, 32f));
                Palette.Panel3D(this, left, on ? Palette.PanelLight : Palette.Panel);
                Palette.Panel3D(this, right, on ? Palette.PanelLight : Palette.Panel);
                Palette.TextCentered(this, left.Position + left.Size * 0.5f, "‹", 20, Palette.Ink);
                Palette.TextCentered(this, right.Position + right.Size * 0.5f, "›", 20, Palette.Ink);
                Palette.Text(this, new Vector2(cx + 54f, y + 24f), values[i], 20, color);

                int row = i;
                _clicks.Add(left, () => { _settingRow = row; NudgeSetting(-1); }, () => _settingRow = row);
                _clicks.Add(right, () => { _settingRow = row; NudgeSetting(1); }, () => _settingRow = row);
            }
            y += 62f;
        }

        Palette.TextCentered(this, new Vector2(cx, y + 16f),
            "Press ENTER to start the game", 18, Palette.Highlight);

        Palette.Text(this, new Vector2(40f, size.Y - 24f),
            "Up/Down to move  ·  Left/Right to change  ·  Enter to start  ·  Esc to go back",
            16, Palette.InkDim);
    }

    private void DrawMatchupCard(Vector2 at, TeamData team, string caption)
    {
        var rect = new Rect2(at, new Vector2(280f, 150f));
        Palette.Panel3D(this, rect, team.Primary);
        DrawRect(new Rect2(at + new Vector2(0f, 144f), new Vector2(280f, 6f)), team.Secondary);

        Palette.Text(this, at + new Vector2(14f, 26f), caption, 13, team.TextOnPrimary);
        Palette.Text(this, at + new Vector2(14f, 62f), team.City, 20, team.TextOnPrimary);
        Palette.Text(this, at + new Vector2(14f, 94f), team.Nickname.ToUpperInvariant(), 26, team.TextOnPrimary);
        Palette.Text(this, at + new Vector2(14f, 126f), team.Abbrev, 15, team.TextOnPrimary);
    }

    private static string ModeLabel(ControlMode mode) => mode switch
    {
        ControlMode.PlayerVsCpu => "You run the visitors (bat + pitch)",
        ControlMode.CpuVsPlayer => "You run the home club (bat + pitch)",
        ControlMode.BatOnlyAway => "You bat only (visitors)",
        ControlMode.BatOnlyHome => "You bat only (home)",
        ControlMode.PlayerVsPlayer => "Two players",
        _ => "Watch the CPUs",
    };
}
