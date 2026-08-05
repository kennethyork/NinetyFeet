using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.Gameplay;

namespace SandlotSlugfest.UI;

/// <summary>
/// Pick a situation and see whether you can do it.
///
/// This is the only part of the game that fits in ninety seconds, and the only one that asks a
/// specific question rather than a general one — not "can you play baseball" but "can you drive in
/// the tying run from third with two out". Every one is a real situation in a real game; nothing
/// is simulated differently because it is a moment.
/// </summary>
public partial class MomentsScreen : Control
{
    private int _cursor;
    private readonly ClickMap _clicks = new();

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Cards.Collection.Load();
    }

    private void Leave() => Game.Instance.GoTo("res://Scenes/MainMenu.tscn");

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
            case Key.Escape or Key.Backspace: Leave(); return;
            case Key.Up or Key.W: _cursor = Mathf.Max(0, _cursor - 1); break;
            case Key.Down or Key.S: _cursor = Mathf.Min(Moments.All.Length - 1, _cursor + 1); break;
            case Key.Enter or Key.Space: Play(Moments.All[_cursor]); return;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);
        _clicks.Begin();

        Palette.BackButton(this, size, _clicks, Leave);
        Palette.Text(this, new Vector2(40f, 46f), "MOMENTS", 26, Palette.Ink);
        Palette.Text(this, new Vector2(40f, 68f),
            "One situation. One question. Ninety seconds.", 14, Palette.InkDim);

        float y = 130f;
        for (int i = 0; i < Moments.All.Length; i++)
        {
            var m = Moments.All[i];
            bool on = i == _cursor;
            var rect = new Rect2(new Vector2(40f, y), new Vector2(size.X - 80f, 84f));

            Palette.Panel3D(this, rect, on ? Palette.PanelLight : Palette.Panel);

            Palette.Text(this, rect.Position + new Vector2(20f, 28f), m.Name, 18,
                on ? Palette.Ink : Palette.InkDim);
            Palette.Text(this, rect.Position + new Vector2(20f, 50f), m.Blurb, 13, Palette.InkDim);
            Palette.Text(this, rect.Position + new Vector2(20f, 70f),
                $"{m.SituationText}  ·  {m.GoalText}", 12, Palette.Highlight);

            string pays = m.Pack >= 0
                ? $"{Cards.Market.Coins(m.Coins)} + {Cards.Market.Packs[m.Pack].Name}"
                : Cards.Market.Coins(m.Coins);
            // Right-aligned against the button rather than measured from it — "1,500c + STANDARD
            // PACK" is a great deal wider than "900c" and ran clean under it.
            float paysW = Palette.TextWidth(pays, 14);
            Palette.Text(this, rect.Position + new Vector2(rect.Size.X - 168f - paysW, 48f), pays,
                14, Palette.Highlight);

            var play = new Rect2(rect.Position + new Vector2(rect.Size.X - 150f, 26f),
                new Vector2(126f, 34f));
            Palette.Panel3D(this, play, Palette.Highlight.Darkened(0.2f));
            Palette.TextCentered(this, play.Position + play.Size * 0.5f, "PLAY IT", 14,
                Palette.Night);

            var chosen = m;
            int index = i;
            _clicks.Add(play, () => Play(chosen));
            _clicks.Add(rect, () => { _cursor = index; QueueRedraw(); });

            y += 92f;
        }

        Palette.Text(this, new Vector2(40f, size.Y - 22f),
            "Up/Down to choose  ·  Enter to play  ·  Esc to go back", 14, Palette.InkDim);
    }

    private void Play(Moment m)
    {
        var g = Game.Instance;

        // Two clubs drawn from the league, so the men in it are real players with real ratings.
        var rng = new Rng(m.Name.Length * 7919 + m.Inning * 31 + (int)Time.GetTicksMsec());
        int mine = rng.Range(0, Teams.All.Count);
        int theirs = (mine + 1 + rng.Range(0, Teams.All.Count - 1)) % Teams.All.Count;

        g.PendingSeasonGame = null;
        g.CardClubRoster = null;
        g.ClearFarmGame();
        g.PendingMoment = m;
        g.ReturnTo = "res://Scenes/Moments.tscn";

        // The player's club takes the half of the inning the moment names.
        bool playerBatsTop = m.Batting == m.TopHalf;
        g.AwayTeamId = playerBatsTop ? mine : theirs;
        g.HomeTeamId = playerBatsTop ? theirs : mine;
        g.Mode = playerBatsTop ? ControlMode.PlayerVsCpu : ControlMode.CpuVsPlayer;

        g.GoTo("res://Scenes/Game.tscn");
    }
}
