using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.Net;

namespace SandlotSlugfest.UI;

/// <summary>
/// The way in to playing somebody else.
///
/// There was not one. The netcode has been finished and proven for a while — a whole game over the
/// wire, a whole season shared — and the only thing that could ever open a socket was a headless
/// self-test driven from the command line. Every part of online play worked and no player could
/// reach any of it.
///
/// Two things are arranged here. A single ballgame, which is the old netplay: both machines run
/// the same simulation and trade what each player decides. And a shared season, which is the same
/// idea over a year — each owner runs a club, plays his own games, and the results cross.
/// </summary>
public partial class OnlineLobby : Control
{
    private const string SelectionPath = "user://settings.cfg";
    private readonly ClickMap _clicks = new();

    private bool _league = true;                  // a shared season rather than one ballgame
    private string _address = "127.0.0.1";
    private bool _typing;
    private int _club;
    private int _seed = 1994;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(true);

        var cfg = new ConfigFile();
        cfg.Load(SelectionPath);
        _league = (bool)cfg.GetValue("online", "league", true);
        _address = (string)cfg.GetValue("online", "address", "127.0.0.1");
        _club = Mathf.Clamp((int)cfg.GetValue("online", "club", Game.Instance.HomeTeamId), 0, Teams.All.Count - 1);
        _seed = (int)cfg.GetValue("online", "seed", 1994);

        if (NetLink.I != null)
        {
            NetLink.I.Changed += QueueRedraw;
            NetLink.I.LeagueStarted += OpenLeague;
            NetLink.I.MatchStarted += OpenMatch;
        }
    }

    public override void _ExitTree()
    {
        if (NetLink.I == null) return;
        NetLink.I.Changed -= QueueRedraw;
        NetLink.I.LeagueStarted -= OpenLeague;
        NetLink.I.MatchStarted -= OpenMatch;
    }

    public override void _Process(double delta) => QueueRedraw();

    // -----------------------------------------------------------------------
    // Doing something
    // -----------------------------------------------------------------------

    private void Host()
    {
        SaveSelections();
        if (!NetLink.I.Host()) return;

        // The host settles the terms before anybody arrives, so a guest connecting is told them
        // in the same breath as being accepted.
        if (_league)
            NetLink.I.SetLeagueTerms(_seed, _club, GuestDefault(), Season.Schedule.FullSeason,
                Game.Instance.Innings);
        else
            NetLink.I.SetTerms(_seed, GuestDefault(), _club, Game.Instance.Innings);
    }

    /// <summary>A club for the guest to start on that is not the one the host has taken.</summary>
    private int GuestDefault() => (_club + 1) % Teams.All.Count;

    private void Join()
    {
        SaveSelections();
        NetLink.I.Join(_address.Trim());
    }

    private void PickClub(int delta)
    {
        int count = Teams.All.Count;
        int wanted = (_club + delta + count) % count;

        // Two owners cannot run the same club. Step past it rather than refusing the click, so
        // holding the button down does not stick.
        if (NetLink.I.IsOnline && wanted == NetLink.I.RemoteClubId)
            wanted = (wanted + (delta >= 0 ? 1 : -1) + count) % count;

        _club = wanted;
        Game.Instance.HomeTeamId = _club;
        NetLink.I.ChooseClub(_club);
        SaveSelections();
    }

    private void ToggleReady() => NetLink.I.SetReady(!NetLink.I.LocalReady);

    private void Leave()
    {
        SaveSelections();
        NetLink.I.Shutdown();
        Game.Instance.GoTo("res://Scenes/MainMenu.tscn");
    }

    private void SaveSelections()
    {
        var cfg = new ConfigFile();
        cfg.Load(SelectionPath);
        cfg.SetValue("online", "league", _league);
        cfg.SetValue("online", "address", _address);
        cfg.SetValue("online", "club", _club);
        cfg.SetValue("online", "seed", _seed);
        cfg.Save(SelectionPath);
    }

    private void OpenLeague()
    {
        var season = NetLeague.I.Begin(NetLink.I.MatchSeed, NetLink.I.LocalClubId,
            NetLink.I.RemoteClubId, NetLink.I.LeagueGames, NetLink.I.Innings);

        Game.Instance.AdoptLeague(season);
        Game.Instance.ManagerOnly = false;
        Game.Instance.PendingSeasonGame = null;
        Game.Instance.GoTo("res://Scenes/Season.tscn");
    }

    private void OpenMatch()
    {
        var g = Game.Instance;
        g.AwayTeamId = NetLink.I.AwayTeamId;
        g.HomeTeamId = NetLink.I.HomeTeamId;
        g.Innings = NetLink.I.Innings;
        g.Mode = NetLink.I.LocalIsAway ? ControlMode.PlayerVsCpu : ControlMode.CpuVsPlayer;
        g.PendingSeasonGame = null;
        g.GoTo("res://Scenes/Game.tscn");
    }

    // -----------------------------------------------------------------------
    // Typing an address
    // -----------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_typing && _clicks.Controller(@event, Leave)) { QueueRedraw(); return; }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            _typing = false;
            if (_clicks.Click(mb.Position)) QueueRedraw();
            return;
        }

        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        if (key.PhysicalKeycode == Key.Escape)
        {
            if (_typing) { _typing = false; QueueRedraw(); return; }
            Leave();
            return;
        }

        if (!_typing)
        {
            if (key.PhysicalKeycode is Key.Left) PickClub(-1);
            if (key.PhysicalKeycode is Key.Right) PickClub(1);
            QueueRedraw();
            return;
        }

        if (key.PhysicalKeycode == Key.Backspace)
        {
            if (_address.Length > 0) _address = _address[..^1];
        }
        else if (key.PhysicalKeycode is Key.Enter or Key.KpEnter)
        {
            _typing = false;
        }
        else
        {
            // An address is digits, dots and colons, plus letters for a hostname. Anything else a
            // keyboard can produce is not one, and letting it through only produces a field the
            // player has to clear before it will connect.
            char c = (char)key.Unicode;
            if (_address.Length < 40 && (char.IsLetterOrDigit(c) || c is '.' or ':' or '-'))
                _address += c;
        }

        QueueRedraw();
    }

    // -----------------------------------------------------------------------
    // Drawing
    // -----------------------------------------------------------------------

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);
        _clicks.Begin();

        var link = NetLink.I;

        Palette.Text(this, new Vector2(40f, 46f), "ONLINE", 26, Palette.Ink);
        Palette.Text(this, new Vector2(40f, 70f),
            _league
                ? "A season the two of you run together. Each owner plays his own club."
                : "One ballgame, two people, one simulation.",
            14, Palette.InkDim);

        Palette.BackButton(this, size, _clicks, Leave);

        DrawWhat(size);
        DrawConnection(size, link);
        DrawClub(size, link);
        _clicks.DrawFocus(this, Palette.Highlight);

        // Whatever the link is currently saying, which is the only thing that tells a player why
        // nothing is happening.
        if (!string.IsNullOrEmpty(link?.Status))
            Palette.Text(this, new Vector2(40f, size.Y - 30f), link.Status, 14,
                link.State == LinkState.Lost ? Palette.Warning : Palette.Highlight);
    }

    private void DrawWhat(Vector2 size)
    {
        var panel = new Rect2(new Vector2(40f, 108f), new Vector2(size.X - 80f, 68f));
        Palette.Panel3D(this, panel, Palette.Panel);

        bool live = NetLink.I is { State: not LinkState.Offline };

        Button(new Rect2(panel.Position + new Vector2(18f, 14f), new Vector2(220f, 40f)),
            "SHARED SEASON", () => { if (!live) _league = true; },
            _league ? Palette.Highlight : (Color?)null);

        Button(new Rect2(panel.Position + new Vector2(250f, 14f), new Vector2(220f, 40f)),
            "ONE BALLGAME", () => { if (!live) _league = false; },
            _league ? (Color?)null : Palette.Highlight);

        if (live)
            Palette.Text(this, panel.Position + new Vector2(490f, 40f),
                "Set before you connect.", 12, Palette.InkDim);
    }

    private void DrawConnection(Vector2 size, NetLink link)
    {
        var panel = new Rect2(new Vector2(40f, 192f), new Vector2(size.X - 80f, 130f));
        Palette.Panel3D(this, panel, Palette.Panel);
        Palette.Text(this, panel.Position + new Vector2(18f, 26f), "CONNECTION", 13, Palette.Highlight);

        bool offline = link == null || link.State is LinkState.Offline or LinkState.Lost;

        if (offline)
        {
            Button(new Rect2(panel.Position + new Vector2(18f, 44f), new Vector2(170f, 44f)),
                "HOST", Host, Palette.Highlight);
            Button(new Rect2(panel.Position + new Vector2(200f, 44f), new Vector2(170f, 44f)),
                "JOIN", Join);

            // The address field. It is a click target rather than a real text box because every
            // screen in this game does its own hit testing and drawing; adding a LineEdit here
            // would be the only Godot Control in the project and would sit at a different scale.
            var field = new Rect2(panel.Position + new Vector2(390f, 44f), new Vector2(300f, 44f));
            Palette.Panel3D(this, field, _typing ? Palette.PanelLight : Palette.Panel);
            Palette.Text(this, field.Position + new Vector2(12f, 28f),
                _address + (_typing && Engine.GetFramesDrawn() / 20 % 2 == 0 ? "_" : ""),
                16, Palette.Ink);
            _clicks.Add(field, () => _typing = true);

            Palette.Text(this, panel.Position + new Vector2(390f, 106f),
                "The host's address. Same house: leave it. Elsewhere: their IP, " +
                $"and port {NetLink.DefaultPort} forwarded.", 11, Palette.InkDim);

            Palette.Text(this, panel.Position + new Vector2(18f, 106f),
                "One of you hosts; the other joins.", 11, Palette.InkDim);
            return;
        }

        Palette.Text(this, panel.Position + new Vector2(18f, 60f),
            link.IsHost ? "Hosting." : "Connected as the guest.", 18, Palette.Ink);
        Palette.Text(this, panel.Position + new Vector2(18f, 84f),
            $"You are {(link.IsHost ? "settling" : "being told")} the terms.  " +
            $"Seed {link.MatchSeed}  ·  {link.Innings} innings" +
            (_league ? $"  ·  {link.LeagueGames} games" : ""), 12, Palette.InkDim);

        Button(new Rect2(panel.Position + new Vector2(size.X - 300f, 44f), new Vector2(110f, 44f)),
            "DISCONNECT", () => NetLink.I.Shutdown());
    }

    private void DrawClub(Vector2 size, NetLink link)
    {
        var panel = new Rect2(new Vector2(40f, 338f), new Vector2(size.X - 80f, 190f));
        Palette.Panel3D(this, panel, Palette.Panel);
        Palette.Text(this, panel.Position + new Vector2(18f, 26f), "YOUR CLUB", 13, Palette.Highlight);

        var club = Teams.Get(_club);
        var swatch = new Rect2(panel.Position + new Vector2(18f, 44f), new Vector2(90f, 90f));
        Palette.Panel3D(this, swatch, club.Primary);
        Palette.TextCentered(this, swatch.Position + swatch.Size * 0.5f, club.Abbrev, 22,
            club.TextOnPrimary);

        Palette.Text(this, panel.Position + new Vector2(126f, 74f), club.FullName, 24, Palette.Ink);

        Button(new Rect2(panel.Position + new Vector2(126f, 92f), new Vector2(46f, 36f)), "<",
            () => PickClub(-1));
        Button(new Rect2(panel.Position + new Vector2(180f, 92f), new Vector2(46f, 36f)), ">",
            () => PickClub(1));

        if (link is { IsOnline: true })
        {
            var them = Teams.Get(link.RemoteClubId);
            Palette.Text(this, panel.Position + new Vector2(250f, 112f),
                $"They have {them.FullName}.", 14, Palette.InkDim);
            Palette.Text(this, panel.Position + new Vector2(250f, 130f),
                link.RemoteReady ? "They are ready." : "They are still choosing.", 12,
                link.RemoteReady ? new Color("#7ddb8a") : Palette.InkDim);
        }

        bool canReady = link is { IsOnline: true };
        Button(new Rect2(panel.Position + new Vector2(18f, 142f), new Vector2(230f, 40f)),
            link is { LocalReady: true } ? "READY — WAITING" : "READY",
            () => { if (canReady) ToggleReady(); },
            canReady && link.LocalReady ? Palette.Highlight : (Color?)null);

        if (!canReady)
            Palette.Text(this, panel.Position + new Vector2(260f, 168f),
                "Connect first.", 12, Palette.InkDim);
        else if (_league)
            Palette.Text(this, panel.Position + new Vector2(260f, 168f),
                "When you are both ready the league is built on both machines from the same seed.",
                12, Palette.InkDim);
    }

    private void Button(Rect2 rect, string label, System.Action onClick, Color? fill = null)
    {
        Palette.Panel3D(this, rect, fill ?? Palette.PanelLight);
        Palette.TextCentered(this, rect.Position + rect.Size * 0.5f, label,
            rect.Size.X > 150f ? 15 : 14, fill.HasValue ? Palette.Night : Palette.Ink);
        _clicks.Add(rect, onClick);
    }
}
