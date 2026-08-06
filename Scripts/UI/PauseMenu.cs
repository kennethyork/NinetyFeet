using Godot;
using SandlotSlugfest.Core;

namespace SandlotSlugfest.UI;

/// <summary>
/// The in-game pause menu.
///
/// Escape used to abandon the game outright, and the front office was only reachable from the main
/// menu — so checking who was hurt or how a prospect was developing meant throwing away whatever
/// you were playing. This freezes the game and opens those screens on top of it.
/// </summary>
public partial class PauseMenu : Control
{
    private static readonly string[] Items =
    {
        "Resume", "Front Office", "League Office", "Controls", "Quit to menu",
    };

    private int _selected;
    private readonly ClickMap _clicks = new();

    /// <summary>The screen currently laid over the game, if any.</summary>
    private Control _open;

    /// <summary>Raised when the player chooses to leave the game entirely.</summary>
    public System.Action OnQuit;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Everything else is frozen; this and whatever it opens must keep running.
        ProcessMode = ProcessModeEnum.Always;
        GetTree().Paused = true;
    }

    /// <summary>Closes the pause menu and lets play continue.</summary>
    private void Resume()
    {
        GetTree().Paused = false;
        QueueFree();
    }

    /// <summary>Lays one of the management screens over the paused game.</summary>
    private void Open(string scenePath)
    {
        if (_open != null) return;

        var scene = GD.Load<PackedScene>(scenePath);
        if (scene == null) return;

        var node = scene.Instantiate<Control>();
        node.ProcessMode = ProcessModeEnum.Always;

        // Both management screens expose this; it is how they know to close rather than navigate.
        switch (node)
        {
            case FranchiseScreen f: f.CloseOverlay = CloseOpen; break;
            case LeagueOffice l: l.CloseOverlay = CloseOpen; break;
        }

        _open = node;
        AddChild(node);
        QueueRedraw();
    }

    private void CloseOpen()
    {
        _open?.QueueFree();
        _open = null;
        QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // While a screen is up it owns the input; it closes itself through CloseOverlay.
        if (_open != null) return;

        if (@event is InputEventMouseMotion mm) { if (_clicks.Hover(mm.Position)) QueueRedraw(); return; }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            if (_clicks.Click(mb.Position)) QueueRedraw();
            return;
        }
        if (@event is InputEventJoypadButton { Pressed: true } pad)
        {
            switch (pad.ButtonIndex)
            {
                case JoyButton.B or JoyButton.Start: Resume(); return;
                case JoyButton.DpadUp: _selected = Mathf.PosMod(_selected - 1, Items.Length); break;
                case JoyButton.DpadDown: _selected = Mathf.PosMod(_selected + 1, Items.Length); break;
                case JoyButton.A: Activate(_selected); return;
                default: return;
            }
            QueueRedraw(); return;
        }

        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        switch (key.PhysicalKeycode)
        {
            case Key.Escape:
                Resume();
                return;
            case Key.Up or Key.W:
                _selected = Mathf.PosMod(_selected - 1, Items.Length);
                Audio.Sfx.Instance?.Play(Audio.Sound.UiMove, 0.5f);
                break;
            case Key.Down or Key.S:
                _selected = Mathf.PosMod(_selected + 1, Items.Length);
                Audio.Sfx.Instance?.Play(Audio.Sound.UiMove, 0.5f);
                break;
            case Key.Enter or Key.KpEnter or Key.Space:
                Activate(_selected);
                break;
            default:
                return;
        }
        QueueRedraw();
    }

    private void Activate(int index)
    {
        Audio.Sfx.Instance?.Play(Audio.Sound.UiSelect, 0.6f);

        switch (index)
        {
            case 0: Resume(); break;
            case 1: Open("res://Scenes/Franchise.tscn"); break;
            case 2: Open("res://Scenes/LeagueOffice.tscn"); break;
            case 3: Open("res://Scenes/Controls.tscn"); break;
            case 4:
                GetTree().Paused = false;
                OnQuit?.Invoke();
                break;
        }
    }

    public override void _Draw()
    {
        if (_open != null) return;      // a screen is covering us

        Vector2 size = GetViewportRect().Size;
        _clicks.Begin();

        // Dim the frozen game behind, rather than hiding it — you are still in a ball game.
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0f, 0f, 0f, 0.62f));

        float w = 320f, h = Items.Length * 46f + 96f;
        var panel = new Rect2(new Vector2(size.X * 0.5f - w * 0.5f, size.Y * 0.5f - h * 0.5f), new Vector2(w, h));
        Palette.Panel3D(this, panel, Palette.Panel);

        Palette.TextCentered(this, panel.Position + new Vector2(w * 0.5f, 40f), "PAUSED", 22, Palette.Ink);

        var sit = Game.Instance.League;
        Palette.TextCentered(this, panel.Position + new Vector2(w * 0.5f, 62f),
            $"Year {sit.Year}  ·  {Season.Calendar.FormatShort(sit.Today)}", 12, Palette.InkDim);

        for (int i = 0; i < Items.Length; i++)
        {
            var rect = new Rect2(panel.Position + new Vector2(24f, 84f + i * 46f), new Vector2(w - 48f, 38f));
            bool on = i == _selected;
            Palette.Panel3D(this, rect, on ? Palette.Highlight.Darkened(0.15f) : Palette.PanelLight);
            Palette.TextCentered(this, rect.Position + rect.Size * 0.5f, Items[i], 15,
                on ? Palette.Night : Palette.Ink);

            int pick = i;
            _clicks.Add(rect, () => { _selected = pick; Activate(pick); },
                () => _selected = pick);
        }
    }
}
