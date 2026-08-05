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

    public override void _Ready()
    {
        // Give the game scene a moment to build itself before the first capture.
        _timer = -1.2f;
        DirAccess.MakeDirRecursiveAbsolute(Directory);
    }

    /// <summary>Set by `--bat`: capture from the human hitter's point of view.</summary>
    public bool HumanBats;

    /// <summary>
    /// Seconds to wait before navigating. Some screens cannot be captured the instant the game
    /// starts because what they draw does not exist yet — a shared league needs its second owner
    /// to connect first, and going there early captures the offline season instead.
    /// </summary>
    public float StartAfter;

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
            Game.Instance.Mode = HumanBats ? ControlMode.BatOnlyAway : ControlMode.CpuVsCpu;
            Game.Instance.GoTo(Scene);
            return;
        }

        _timer += (float)delta;
        if (_timer < Interval) return;
        _timer = 0f;

        var image = GetViewport().GetTexture().GetImage();
        string path = $"{Directory}/shot_{_taken:D2}.png";
        image.SavePng(path);
        GD.Print($"saved {path}");

        if (++_taken >= Count) GetTree().Quit();
    }
}
