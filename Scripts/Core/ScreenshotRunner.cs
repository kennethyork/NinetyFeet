using System.Linq;
using Godot;

namespace SandlotSlugfest.Core;

/// <summary>
/// Development helper. Run with:
///     godot -- --shot /path/to/dir [seconds-between-shots] [count]
/// It starts a CPU-versus-CPU game and saves periodic screenshots so the rendering can be
/// checked without sitting and watching it.
/// </summary>
public partial class ScreenshotRunner : Node
{
    public string Directory = "/tmp";
    public float Interval = 1.6f;
    public int Count = 8;

    /// <summary>Which scene to capture. Defaults to a live ballgame.</summary>
    public string Scene = "res://Scenes/Game.tscn";

    private float _timer;
    private int _taken;
    private bool _started;
    private bool _clicked;
    private bool _watching;
    private bool _checkedThisShot;
    private bool _scrolled;
    private int _controllerSent;

    /// <summary>Mouse-wheel notches to post before capture; positive moves down.</summary>
    public int ScrollSteps;
    public int ControllerSteps;

    public override void _Ready()
    {
        // Give the game scene a moment to build itself before the first capture.
        _timer = -1.2f;
        DirAccess.MakeDirRecursiveAbsolute(Directory);
    }

    /// <summary>Set by `--bat`: capture from the human hitter's point of view.</summary>
    public bool HumanBats;
    public bool HumanPitches;
    public bool AutoPlay;
    public bool ManualFielding;
    public bool LargeText;
    public bool HighContrast;
    public bool ReducedMotion;

    /// <summary>
    /// Seconds to wait before navigating. Some screens cannot be captured the instant the game
    /// starts because what they draw does not exist yet — a shared league needs its second owner
    /// to connect first, and going there early captures the offline season instead.
    /// </summary>
    public float StartAfter;

    /// <summary>
    /// A click to post once the scene is up, in viewport pixels, or negative for none.
    ///
    /// Several screens in this game only show their most important panel after you select
    /// something — a player card, a trade offer, a box score — and a capture run could never reach
    /// any of them. So the layouts most likely to be wrong were the ones that could not be
    /// checked, which is precisely backwards.
    /// </summary>
    public Vector2 Click = new(-1f, -1f);
    public bool SimulateTouch;

    /// <summary>Report text that runs off the screen or across other text, per capture.</summary>
    public bool CheckText;

    public override void _Process(double delta)
    {
        if (!_started)
        {
            if (StartAfter > 0f)
            {
                StartAfter -= (float)delta;
                return;
            }

            _started = true;
            // `--bat` captures the human hitting view, which is the only way to see the
            // reticle — a CPU-versus-CPU capture never draws it.
            Game.Instance.Mode = HumanPitches ? ControlMode.CpuVsPlayer
                : HumanBats ? ControlMode.BatOnlyAway
                : ControlMode.CpuVsCpu;
            Game.Instance.AutoPlayNextGame = AutoPlay;
            if (ManualFielding) Game.Instance.AutoFielding = false;
            if (LargeText) Game.Instance.LargeText = true;
            if (HighContrast) Game.Instance.HighContrast = true;
            if (ReducedMotion) Game.Instance.ReducedMotion = true;
            Game.Instance.GoTo(Scene);
            return;
        }

        // Read the frame immediately after the watch was opened, so exactly one screen's worth of
        // text is in the list.
        if (_watching)
        {
            _watching = false;
            _checkedThisShot = true;
            var faults = UI.Palette.Report(GetViewport().GetVisibleRect().Size);
            GD.Print(faults.Count == 0
                ? $"  [text] {Scene}: everything fits"
                : $"  [text] {Scene}: {faults.Count} problems");
            foreach (string f in faults.Distinct().Take(12)) GD.Print($"      {f}");
        }

        _timer += (float)delta;

        if (ControllerSteps != 0 && _controllerSent < Mathf.Abs(ControllerSteps)
            && _timer > Interval * 0.25f)
        {
            var button = ControllerSteps > 0 ? JoyButton.DpadDown : JoyButton.DpadUp;
            Input.ParseInputEvent(new InputEventJoypadButton
                { ButtonIndex = button, Pressed = true });
            Input.ParseInputEvent(new InputEventJoypadButton
                { ButtonIndex = button, Pressed = false });
            _controllerSent++;
            if (_controllerSent >= Mathf.Abs(ControllerSteps)) _scrolled = true;
            return;
        }

        if (ScrollSteps != 0 && ControllerSteps == 0 && !_scrolled && _timer > Interval * 0.25f)
        {
            _scrolled = true;
            var button = ScrollSteps > 0 ? MouseButton.WheelDown : MouseButton.WheelUp;
            for (int n = 0; n < Mathf.Abs(ScrollSteps); n++)
                Input.ParseInputEvent(new InputEventMouseButton
                {
                    Position = GetViewport().GetVisibleRect().Size * 0.5f,
                    ButtonIndex = button,
                    Pressed = true,
                });
        }

        // Posted once, a beat after the scene is up so it has drawn and registered its hit boxes.
        if (Click.X >= 0f && _timer > Interval * 0.5f && !_clicked)
        {
            _clicked = true;
            foreach (bool down in new[] { true, false })
                Input.ParseInputEvent(SimulateTouch
                    ? new InputEventScreenTouch { Index = 0, Position = Click, Pressed = down }
                    : new InputEventMouseButton
                    {
                        Position = Click, GlobalPosition = Click,
                        ButtonIndex = MouseButton.Left, Pressed = down,
                    });
        }

        if (_timer < Interval) return;
        _timer = 0f;

        if (CheckText && !_checkedThisShot)
        {
            // Opened here and read on the very next frame, below. A whole capture interval of
            // frames would pile every redraw into one list — the title screen reported five
            // thousand overlaps, which was thirteen strings counted four hundred times.
            UI.Palette.BeginWatch();
            _watching = true;
            return;
        }

        var texture = GetViewport().GetTexture();
        if (texture == null)
        {
            GD.Print("  [capture] renderer has no viewport texture; layout checks completed only");
            GetTree().Quit();
            return;
        }
        var image = texture.GetImage();
        if (image == null)
        {
            GD.Print("  [capture] renderer returned no image; layout checks completed only");
            GetTree().Quit();
            return;
        }
        string path = $"{Directory}/shot_{_taken:D2}.png";
        image.SavePng(path);
        GD.Print($"saved {path}");
        _checkedThisShot = false;

        if (++_taken >= Count) GetTree().Quit();
    }
}
