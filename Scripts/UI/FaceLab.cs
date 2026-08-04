using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.UI;

/// <summary>
/// A verification harness, not a game screen: draws a grid of kids from consecutive look seeds so
/// the amount of real visual variety can be measured rather than assumed.
/// Run with `--shot &lt;dir&gt; 1 2 --scene res://Scenes/FaceLab.tscn`.
/// </summary>
public partial class FaceLab : Control
{
    private const int Cols = 8;
    private const int Rows = 5;

    public override void _Ready() => SetAnchorsPreset(LayoutPreset.FullRect);

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color("#3d4a57"));

        var team = Teams.Get(0);
        float cw = size.X / Cols;
        float ch = size.Y / Rows;

        for (int i = 0; i < Cols * Rows; i++)
        {
            int col = i % Cols, row = i / Cols;
            // The written kids, since they are the ones whose looks were authored rather than
            // rolled — this is where you check they actually read as different people.
            var player = i < Legends.Count
                ? Legends.Make(i, i)
                : new PlayerData { LookSeed = 1000 + i * 7919 };

            var feet = new Vector2(cw * (col + 0.5f), ch * (row + 0.90f));
            CartoonPlayer.Draw(this, feet, ch / 200f, 1f, Pose.Idle, team, player, 0f);

            if (player.IsLegend)
                Palette.TextCentered(this, new Vector2(cw * (col + 0.5f), ch * (row + 0.97f)),
                    player.LastName, 13, new Color("#e9eef5"));
        }
    }
}
