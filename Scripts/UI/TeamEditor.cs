using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.UI;

/// <summary>
/// Rename and recolour a club.
///
/// The thirty-two were written into the source and none of them could be changed, which is fine
/// for a game somebody plays once and wrong for one they keep. A league you have run for ten
/// seasons ought to be able to have your own club in it.
///
/// What can be edited is deliberately limited to the name, the abbreviation and the two colours.
/// A club's league, division and playing biases are what make the thirty-two different from one
/// another as opponents; letting somebody hand his own club a pitching bias would turn this
/// screen into a cheat menu.
/// </summary>
public partial class TeamEditor : Control
{
    private enum Field { City, Nickname, Abbrev }

    private int _team;
    private Field _editing = Field.City;
    private bool _pickingSecondary;
    private readonly ClickMap _clicks = new();

    /// <summary>What has been typed but not yet saved. Null means "leave this one alone".</summary>
    private string _city, _nickname, _abbrev;
    private Color? _primary, _secondary;

    private string _note = "";
    private float _noteTimer;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(true);

        _team = Game.Instance.League?.UserTeamId ?? 0;
        LoadTeam();
    }

    public override void _Process(double delta)
    {
        if (_noteTimer <= 0f) return;
        _noteTimer -= (float)delta;
        if (_noteTimer <= 0f) { _note = ""; QueueRedraw(); }
    }

    private void LoadTeam()
    {
        var t = Teams.Get(_team);
        _city = t.City;
        _nickname = t.Nickname;
        _abbrev = t.Abbrev;
        _primary = t.Primary;
        _secondary = t.Secondary;
    }

    private void Leave() => Game.Instance.GoTo(Game.Instance.League != null
        ? "res://Scenes/Season.tscn"
        : "res://Scenes/MainMenu.tscn");

    private void Say(string what)
    {
        _note = what;
        _noteTimer = 2.6f;
    }

    // -----------------------------------------------------------------------
    // Input
    // -----------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion m) { if (_clicks.Hover(m.Position)) QueueRedraw(); return; }
        if (@event is InputEventJoypadButton && _clicks.Controller(@event, Leave))
        { QueueRedraw(); return; }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            if (_clicks.Click(mb.Position)) QueueRedraw();
            return;
        }

        if (@event is not InputEventKey { Pressed: true } key) return;

        switch (key.PhysicalKeycode)
        {
            case Key.Escape: Leave(); return;
            case Key.Tab:
                _editing = (Field)Mathf.PosMod((int)_editing + 1, 3);
                break;
            case Key.Backspace:
                Typed(null);
                break;
            case Key.Enter or Key.KpEnter:
                Commit();
                break;
            default:
                // Ordinary printable characters go into whichever field has focus. Godot hands
                // the character over as a unicode value, which is the only way to get letters out
                // of a key event without hand-writing a keymap.
                long u = key.Unicode;
                if (u >= 32 && u < 127) Typed(((char)u).ToString());
                else return;
                break;
        }

        QueueRedraw();
    }

    /// <summary>Adds a character to the focused field, or removes one when given null.</summary>
    private void Typed(string ch)
    {
        string current = _editing switch
        {
            Field.Nickname => _nickname,
            Field.Abbrev => _abbrev,
            _ => _city,
        } ?? "";

        string next;
        if (ch == null) next = current.Length > 0 ? current[..^1] : "";
        else
        {
            // An abbreviation is three characters on a scoreboard and nowhere near enough room
            // for more; the names are capped at something a panel can actually draw.
            int cap = _editing == Field.Abbrev ? 3 : 18;
            if (current.Length >= cap) return;
            next = current + (_editing == Field.Abbrev ? ch.ToUpperInvariant() : ch);
        }

        switch (_editing)
        {
            case Field.Nickname: _nickname = next; break;
            case Field.Abbrev: _abbrev = next; break;
            default: _city = next; break;
        }
    }

    private void Commit()
    {
        if (string.IsNullOrWhiteSpace(_city) || string.IsNullOrWhiteSpace(_nickname) ||
            string.IsNullOrWhiteSpace(_abbrev))
        {
            Say("A club needs a city, a nickname and an abbreviation.");
            return;
        }

        var original = Teams.Get(_team);

        // Only what actually differs is stored. A club edited back to what it was carries no
        // override at all, rather than an override that happens to match.
        var edit = new TeamEdits.Edit
        {
            City = _city == original.City ? null : _city.Trim(),
            Nickname = _nickname == original.Nickname ? null : _nickname.Trim(),
            Abbrev = _abbrev == original.Abbrev ? null : _abbrev.Trim(),
            Primary = _primary == original.Primary ? null : _primary,
            Secondary = _secondary == original.Secondary ? null : _secondary,
        };

        TeamEdits.Set(_team, edit);
        LoadTeam();
        Say(edit.IsEmpty ? "Back to how it shipped." : $"Saved. They are the {Teams.Get(_team).FullName}.");
    }

    private void Revert()
    {
        TeamEdits.Clear(_team);
        LoadTeam();
        Say("Put back as it shipped.");
    }

    private void Pick(int by)
    {
        _team = Mathf.PosMod(_team + by, Teams.All.Count);
        LoadTeam();
    }

    // -----------------------------------------------------------------------
    // Drawing
    // -----------------------------------------------------------------------

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);
        _clicks.Begin();

        Palette.BackButton(this, size, _clicks, Leave);
        Palette.Text(this, new Vector2(40f, 46f), "CLUB EDITOR", 26, Palette.Ink);
        Palette.Text(this, new Vector2(40f, 68f),
            "Tab moves between the fields, Enter saves. Nothing here changes how a club plays.",
            14, Palette.InkDim);

        DrawPicker(size);
        DrawPreview(size);
        DrawFields(size);
        DrawSwatches(size);
        DrawButtons(size);

        if (_note != "")
            Palette.Text(this, new Vector2(40f, size.Y - 48f), _note, 15, Palette.Highlight);

        Palette.Text(this, new Vector2(40f, size.Y - 22f),
            "Esc to go back  ·  edits are kept in their own file, so the originals are never lost",
            13, Palette.InkDim);
        _clicks.DrawFocus(this, Palette.Highlight);
    }

    private void DrawPicker(Vector2 size)
    {
        var left = new Rect2(new Vector2(40f, 100f), new Vector2(34f, 32f));
        var right = new Rect2(new Vector2(320f, 100f), new Vector2(34f, 32f));

        Palette.Panel3D(this, left, Palette.PanelLight);
        Palette.Panel3D(this, right, Palette.PanelLight);
        Palette.TextCentered(this, left.Position + left.Size * 0.5f, "<", 16, Palette.Ink);
        Palette.TextCentered(this, right.Position + right.Size * 0.5f, ">", 16, Palette.Ink);
        _clicks.Add(left, () => { Pick(-1); QueueRedraw(); });
        _clicks.Add(right, () => { Pick(1); QueueRedraw(); });

        var t = Teams.Get(_team);
        Palette.TextCentered(this, new Vector2(197f, 122f),
            $"{_team + 1} of {Teams.All.Count}", 13, Palette.InkDim);
        Palette.Text(this, new Vector2(370f, 122f),
            Teams.DivisionName(t.League, t.Division),
            14, Palette.InkDim);

        if (TeamEdits.For(_team) != null)
            Palette.Text(this, new Vector2(620f, 122f), "· edited", 13, Palette.Highlight);
    }

    /// <summary>The club as it will look, drawn from what is currently typed rather than saved.</summary>
    private void DrawPreview(Vector2 size)
    {
        var panel = new Rect2(new Vector2(40f, 148f), new Vector2(size.X - 80f, 96f));
        Palette.Panel3D(this, panel, _primary ?? Palette.Panel);

        DrawRect(new Rect2(panel.Position + new Vector2(0f, panel.Size.Y - 8f),
            new Vector2(panel.Size.X, 8f)), _secondary ?? Palette.Highlight);

        var ink = (_primary ?? Palette.Panel).Luminance > 0.5f ? Palette.Night : Colors.White;

        Palette.Text(this, panel.Position + new Vector2(24f, 46f),
            $"{_city} {_nickname}".Trim(), 30, ink);
        Palette.Text(this, panel.Position + new Vector2(24f, 72f), _abbrev ?? "", 18,
            _secondary ?? ink);
    }

    private void DrawFields(Vector2 size)
    {
        var rows = new (Field Which, string Label, string Value)[]
        {
            (Field.City, "CITY", _city),
            (Field.Nickname, "NICKNAME", _nickname),
            (Field.Abbrev, "ABBREVIATION", _abbrev),
        };

        float y = 268f;
        foreach (var (which, label, value) in rows)
        {
            bool on = _editing == which;
            var box = new Rect2(new Vector2(180f, y), new Vector2(300f, 30f));

            Palette.Text(this, new Vector2(40f, y + 21f), label, 12, Palette.InkDim);
            Palette.Panel3D(this, box, on ? Palette.PanelLight : Palette.Panel);
            Palette.Text(this, box.Position + new Vector2(10f, 21f),
                (value ?? "") + (on ? "_" : ""), 16, Palette.Ink);

            var target = which;
            _clicks.Add(box, () => { _editing = target; QueueRedraw(); });
            y += 40f;
        }
    }

    private void DrawSwatches(Vector2 size)
    {
        Palette.Text(this, new Vector2(40f, 410f), "COLOURS", 12, Palette.InkDim);

        // Which of the two the swatches are currently setting.
        var pri = new Rect2(new Vector2(180f, 392f), new Vector2(140f, 28f));
        var sec = new Rect2(new Vector2(330f, 392f), new Vector2(140f, 28f));

        Palette.Panel3D(this, pri, _pickingSecondary ? Palette.Panel : Palette.PanelLight);
        Palette.Panel3D(this, sec, _pickingSecondary ? Palette.PanelLight : Palette.Panel);
        Palette.TextCentered(this, pri.Position + pri.Size * 0.5f, "PRIMARY", 12, Palette.Ink);
        Palette.TextCentered(this, sec.Position + sec.Size * 0.5f, "TRIM", 12, Palette.Ink);
        _clicks.Add(pri, () => { _pickingSecondary = false; QueueRedraw(); });
        _clicks.Add(sec, () => { _pickingSecondary = true; QueueRedraw(); });

        // A short palette rather than a colour wheel: every one of these reads on a uniform and
        // against the field, which is not true of an arbitrary colour.
        var swatches = TeamEdits.Swatches;
        for (int i = 0; i < swatches.Length; i++)
        {
            var at = new Rect2(new Vector2(180f + i % 10 * 34f, 434f + i / 10 * 34f),
                new Vector2(30f, 30f));
            DrawRect(at, swatches[i]);

            bool chosen = (_pickingSecondary ? _secondary : _primary) == swatches[i];
            if (chosen)
                DrawRect(new Rect2(at.Position - new Vector2(2f, 2f), at.Size + new Vector2(4f, 4f)),
                    Palette.Highlight, filled: false, width: 2f);

            var colour = swatches[i];
            _clicks.Add(at, () =>
            {
                if (_pickingSecondary) _secondary = colour; else _primary = colour;
                QueueRedraw();
            });
        }
    }

    private void DrawButtons(Vector2 size)
    {
        var save = new Rect2(new Vector2(40f, 528f), new Vector2(140f, 34f));
        Palette.Panel3D(this, save, Palette.Highlight.Darkened(0.25f));
        Palette.TextCentered(this, save.Position + save.Size * 0.5f, "SAVE", 14, Palette.Ink);
        _clicks.Add(save, () => { Commit(); QueueRedraw(); });

        var revert = new Rect2(new Vector2(192f, 528f), new Vector2(180f, 34f));
        bool edited = TeamEdits.For(_team) != null;
        Palette.Panel3D(this, revert, edited ? Palette.Panel : Palette.Panel.Darkened(0.3f));
        Palette.TextCentered(this, revert.Position + revert.Size * 0.5f, "PUT IT BACK", 13,
            edited ? Palette.Ink : Palette.InkDim);
        if (edited) _clicks.Add(revert, () => { Revert(); QueueRedraw(); });

        DrawNames(size);
    }

    /// <summary>
    /// The players' names, which are a file rather than a screen.
    ///
    /// Eight hundred and thirty-two men is not something anybody is going to type into a text box
    /// one at a time, so the work happens in a text editor and this offers the two things that
    /// cannot: somewhere to get a blank file with every slot labelled, and a way to put it onto a
    /// league that already exists. Reading the file only affects leagues built afterwards, which
    /// on its own would be useless to anybody four seasons into a dynasty.
    /// </summary>
    private void DrawNames(Vector2 size)
    {
        float y = 578f;
        Palette.Text(this, new Vector2(40f, y), "PLAYER NAMES", 13, Palette.Highlight);
        Palette.Text(this, new Vector2(160f, y), Rosters.Status(), 13, Palette.InkDim);

        var write = new Rect2(new Vector2(40f, y + 12f), new Vector2(190f, 34f));
        Palette.Panel3D(this, write, Palette.Panel);
        Palette.TextCentered(this, write.Position + write.Size * 0.5f,
            Rosters.Exists() ? "FILE ALREADY THERE" : "WRITE A BLANK FILE", 12,
            Rosters.Exists() ? Palette.InkDim : Palette.Ink);

        if (!Rosters.Exists())
            _clicks.Add(write, () =>
            {
                Say(Rosters.WriteTemplate());
                Rosters.Load();
                QueueRedraw();
            });

        var apply = new Rect2(new Vector2(242f, y + 12f), new Vector2(210f, 34f));
        bool can = Rosters.Any && Game.Instance.League != null;
        Palette.Panel3D(this, apply, can ? Palette.Panel : Palette.Panel.Darkened(0.3f));
        Palette.TextCentered(this, apply.Position + apply.Size * 0.5f, "APPLY TO THIS LEAGUE", 12,
            can ? Palette.Ink : Palette.InkDim);

        if (can)
            _clicks.Add(apply, () =>
            {
                Rosters.Load();
                int n = Rosters.Apply(Game.Instance.League);
                Game.Instance.SaveLeague();
                Say(n == 0
                    ? "Nobody was renamed — every man already has the name in the file."
                    : $"{n} players renamed. Box scores already written keep the old names.");
                QueueRedraw();
            });

        Palette.Text(this, new Vector2(464f, y + 34f),
            ProjectSettings.GlobalizePath(Rosters.Path), 11, Palette.InkDim);

        DrawParks(size, y + 58f);
    }

    /// <summary>
    /// The ballparks, which are also a file and for a stronger reason than the names.
    ///
    /// Five fence distances and five wall heights is not something anybody types into a screen one
    /// number at a time, and unlike a name it is not something anybody invents from nothing either
    /// — so the file is written out with every ground as it currently stands, and the work is
    /// moving a wall rather than building a park from an empty bracket.
    ///
    /// The warning is not decoration. A fence distance goes into the physics: pull one in far
    /// enough and the league's home run rate follows it, which the audit demonstrates by turning
    /// 106 home runs into 528.
    /// </summary>
    private void DrawParks(Vector2 size, float y)
    {
        Palette.Text(this, new Vector2(40f, y), "BALLPARKS", 13, Palette.Highlight);
        Palette.Text(this, new Vector2(160f, y), ParkEdits.Status(), 13, Palette.InkDim);

        var write = new Rect2(new Vector2(40f, y + 12f), new Vector2(190f, 30f));
        bool there = FileAccess.FileExists(ParkEdits.Path);
        Palette.Panel3D(this, write, Palette.Panel);
        Palette.TextCentered(this, write.Position + write.Size * 0.5f,
            there ? "FILE ALREADY THERE" : "WRITE THE GROUNDS OUT", 12,
            there ? Palette.InkDim : Palette.Ink);

        if (!there)
            _clicks.Add(write, () =>
            {
                Say(ParkEdits.WriteTemplate());
                ParkEdits.Load();
                Stadiums.Rebuild();
                QueueRedraw();
            });

        var reload = new Rect2(new Vector2(242f, y + 12f), new Vector2(150f, 30f));
        Palette.Panel3D(this, reload, there ? Palette.Panel : Palette.Panel.Darkened(0.3f));
        Palette.TextCentered(this, reload.Position + reload.Size * 0.5f, "READ IT AGAIN", 12,
            there ? Palette.Ink : Palette.InkDim);

        if (there)
            _clicks.Add(reload, () =>
            {
                ParkEdits.Load();
                Stadiums.Rebuild();
                Say(ParkEdits.Status());
                QueueRedraw();
            });

        Palette.Text(this, new Vector2(404f, y + 32f),
            "Fences change how the game plays, not just how it looks.", 11, Palette.Warning);
    }

}
