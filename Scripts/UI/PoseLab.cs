using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.UI;

/// <summary>
/// A verification harness, not a game screen: every pose the renderer can draw, side by side, at a
/// size where they can actually be judged.
///
/// The slide went in once as a run cycle rotated sixty-five degrees and shipped looking like a man
/// tipped over, because there was no way to look at a pose except by catching one in a live game.
/// This is that way. Run with:
///     --shot &lt;dir&gt; 1 2 --scene res://Scenes/PoseLab.tscn
/// </summary>
public partial class PoseLab : Control
{
    private static readonly Pose[] Shown =
    {
        Pose.Idle, Pose.Stance, Pose.Run, Pose.Slide,
        Pose.Field, Pose.Swing, Pose.Pitch, Pose.Cheer,
    };

    private float _time;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color("#5b7d4a"));

        var team = Teams.Get(0);
        var player = Legends.Make(3, 7001);

        int cols = 4;
        float cw = size.X / cols;
        float ch = size.Y / 2f;

        for (int i = 0; i < Shown.Length; i++)
        {
            int col = i % cols, row = i / cols;
            var cell = new Vector2(cw * (col + 0.5f), ch * (row + 0.5f));

            // The dirt line each figure stands on, so a low pose can be judged against the ground.
            float groundY = cell.Y + 70f;
            DrawLine(new Vector2(cell.X - cw * 0.42f, groundY),
                new Vector2(cell.X + cw * 0.42f, groundY), new Color(0.72f, 0.56f, 0.36f), 3f);

            // Mid-animation rather than at either end, which is where a pose is judged.
            float phase = Shown[i] switch
            {
                Pose.Swing => 0.16f,
                Pose.Pitch => 0.55f,
                Pose.Run => _time * 9f,
                _ => 0f,
            };

            CartoonPlayer.Draw(this, new Vector2(cell.X, groundY), 1.5f, 1f, Shown[i], team,
                player, _time, withBat: Shown[i] is Pose.Stance or Pose.Swing,
                motionPhase: phase);

            Palette.TextCentered(this, new Vector2(cell.X, groundY + 34f),
                Shown[i].ToString().ToUpperInvariant(), 16, Palette.Ink);
        }

        Palette.Text(this, new Vector2(20f, 26f),
            "POSE LAB — every pose the renderer can draw, on a ground line", 18, Palette.Ink);
    }
}
