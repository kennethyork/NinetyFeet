using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;
using SandlotSlugfest.Season;

namespace SandlotSlugfest.UI;

/// <summary>
/// The trade desk. Pick a partner club, tag players on each side, then send the offer and see
/// what they say. Accepted deals move the players and are written straight to the save.
/// </summary>
public partial class TradeScreen : Control
{
    private enum Focus { PickPartner, MyRoster, TheirRoster }

    private Focus _focus = Focus.PickPartner;
    private int _partnerId;
    private int _myCursor;
    private int _theirCursor;

    /// <summary>
    /// How far each pane is scrolled.
    ///
    /// The lists drew every man from the first, and a club carries twenty-six to twenty-nine while
    /// about twenty-one rows fit. The rest were drawn outside the panel, over the offer summary —
    /// and their click targets went with them, so there were invisible rows you could tag by
    /// clicking the wrong part of the screen.
    /// </summary>
    private int _myScroll, _theirScroll;

    private readonly HashSet<PlayerData> _iGive = new();
    private readonly HashSet<PlayerData> _iGet = new();

    private string _response = "";
    private bool _lastAccepted;
    private SeasonState _season;
    private readonly ClickMap _clicks = new();

    private Roster MyRoster => _season.RosterFor(_season.UserTeamId);
    private Roster TheirRoster => _season.RosterFor(_partnerId);

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        _season = Game.Instance.League;
        _partnerId = _season.UserTeamId == 0 ? 1 : 0;

        // The Control swallows mouse events at its default filter, so nothing would be clickable.
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventJoypadButton { Pressed: true } pad)
        {
            switch (pad.ButtonIndex)
            {
                case JoyButton.A: ToggleSelection(); QueueRedraw(); return;
                case JoyButton.X: ClearOffer(); QueueRedraw(); return;
                case JoyButton.Y: SendOffer(); QueueRedraw(); return;
            }
        }

        // The wheel scrolls whichever pane the pointer is over, which is the one you mean.
        if (@event is InputEventMouseButton { Pressed: true } wheel &&
            wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            int by = wheel.ButtonIndex == MouseButton.WheelDown ? 3 : -3;
            if (wheel.Position.X < GetViewportRect().Size.X * 0.5f)
                _myScroll = Mathf.Max(0, _myScroll + by);
            else
                _theirScroll = Mathf.Max(0, _theirScroll + by);

            QueueRedraw();
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            if (_clicks.Click(mb.Position)) QueueRedraw();
            return;
        }

        if (!ControllerNav.TryPressedKey(@event, out Key pressed)) return;

        switch (pressed)
        {
            case Key.Escape:
                Leave();
                return;

            case Key.Tab:
                _focus = (Focus)(((int)_focus + 1) % 3);
                break;

            case Key.Left or Key.A:
                if (_focus == Focus.PickPartner) StepPartner(-1);
                else _focus = Focus.MyRoster;
                break;

            case Key.Right or Key.D:
                if (_focus == Focus.PickPartner) StepPartner(1);
                else _focus = Focus.TheirRoster;
                break;

            case Key.Up or Key.W: MoveCursor(-1); break;
            case Key.Down or Key.S: MoveCursor(1); break;

            case Key.Space: ToggleSelection(); break;

            case Key.Enter or Key.KpEnter: SendOffer(); break;

            case Key.C: ClearOffer(); break;
        }
        QueueRedraw();
    }

    private void StepPartner(int delta)
    {
        // Steps through the league. A smaller league does not hold ids 0..n-1, so adding one
        // to a club id can land on a club that is not in it — and the trade desk would then offer
        // you a deal with a team that plays no games.
        do { _partnerId = Teams.Step(_partnerId, delta >= 0 ? 1 : -1).Id; }
        while (_partnerId == _season.UserTeamId);

        ClearOffer();
        _theirCursor = 0;
    }

    private void MoveCursor(int delta)
    {
        switch (_focus)
        {
            case Focus.MyRoster:
                _myCursor = Mathf.PosMod(_myCursor + delta, Mathf.Max(MyRoster.Players.Count, 1));
                break;
            case Focus.TheirRoster:
                _theirCursor = Mathf.PosMod(_theirCursor + delta, Mathf.Max(TheirRoster.Players.Count, 1));
                break;
            default:
                StepPartner(delta);
                break;
        }
    }

    private void ToggleSelection()
    {
        if (_focus == Focus.MyRoster && _myCursor < MyRoster.Players.Count)
        {
            var p = MyRoster.Players[_myCursor];
            if (!_iGive.Remove(p)) _iGive.Add(p);
        }
        else if (_focus == Focus.TheirRoster && _theirCursor < TheirRoster.Players.Count)
        {
            var p = TheirRoster.Players[_theirCursor];
            if (!_iGet.Remove(p)) _iGet.Add(p);
        }
        _response = "";
    }

    private void ClearOffer()
    {
        _iGive.Clear();
        _iGet.Clear();
        _response = "";
    }

    private void SendOffer()
    {
        var offered = _iGive.ToList();
        var requested = _iGet.ToList();

        // The other club judges it: they receive what I give, and give up what I want.
        var verdict = TradeEngine.Evaluate(_season, _partnerId, offered, requested);
        _response = verdict.Reason;
        _lastAccepted = verdict.Accepted;

        if (!verdict.Accepted) return;

        TradeEngine.Execute(_season, _season.UserTeamId, _partnerId, offered, requested);
        Game.Instance.SaveLeague();

        _response = $"Trade complete. {verdict.Reason}";
        ClearOffer();
        _response = "Trade complete — rosters updated and saved.";
        _myCursor = 0;
        _theirCursor = 0;
    }

    // -----------------------------------------------------------------------

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);
        _clicks.Begin();

        var mine = Teams.Get(_season.UserTeamId);
        var theirs = Teams.Get(_partnerId);

        Palette.Text(this, new Vector2(40f, 46f), "TRADE DESK", 26, Palette.Ink);

        // Say plainly whether the window is open before anyone builds an offer.
        bool open = _season.TradesOpen;
        Palette.Text(this, new Vector2(size.X - 400f, 46f),
            open
                ? $"Deadline {Calendar.FormatShort(Calendar.DateOf(_season.TradeDeadlineDay))} " +
                  $"— {_season.DaysToDeadline} game days left"
                : "CLOSED — the deadline has passed",
            14, open && _season.DaysToDeadline <= 4 ? Palette.Warning
                : open ? Palette.InkDim : Palette.Warning);
        Palette.Text(this, new Vector2(40f, 68f),
            $"You run the {mine.FullName}.", 14, Palette.InkDim);

        // Partner selector, with clickable arrows either side.
        var pick = new Rect2(new Vector2(size.X - 440f, 34f), new Vector2(400f, 44f));
        Palette.Panel3D(this, pick, Palette.PanelLight);
        Palette.TextCentered(this, pick.Position + pick.Size * 0.5f, theirs.FullName, 16, Palette.Ink);

        var prev = new Rect2(pick.Position + new Vector2(4f, 5f), new Vector2(34f, 34f));
        var next = new Rect2(pick.Position + new Vector2(362f, 5f), new Vector2(34f, 34f));
        Palette.Panel3D(this, prev, Palette.Panel);
        Palette.Panel3D(this, next, Palette.Panel);
        Palette.TextCentered(this, prev.Position + prev.Size * 0.5f, "‹", 20, Palette.Highlight);
        Palette.TextCentered(this, next.Position + next.Size * 0.5f, "›", 20, Palette.Highlight);
        _clicks.Add(prev, () => StepPartner(-1));
        _clicks.Add(next, () => StepPartner(1));

        Palette.BackButton(this, size, _clicks, Leave);

        float listTop = 108f;
        float listW = (size.X - 120f) / 2f;

        DrawRosterList(new Vector2(40f, listTop), listW, size.Y - 250f, mine, MyRoster,
            _myCursor, _iGive, _focus == Focus.MyRoster, "YOU GIVE", ref _myScroll);

        DrawRosterList(new Vector2(80f + listW, listTop), listW, size.Y - 250f, theirs, TheirRoster,
            _theirCursor, _iGet, _focus == Focus.TheirRoster, "YOU GET", ref _theirScroll);

        DrawOfferSummary(size);

        Palette.Text(this, new Vector2(40f, size.Y - 22f),
            "Tab / Left / Right to switch panes  ·  Up/Down to move  ·  Space to tag a player  ·  " +
            "Enter to offer  ·  C to clear  ·  Esc to leave",
            13, Palette.InkDim);
    }

    /// <summary>Back to the season hub this was opened from, not the title screen.</summary>
    private void Leave() => Game.Instance.GoTo(Game.Instance.League != null
        ? "res://Scenes/Season.tscn"
        : "res://Scenes/MainMenu.tscn");

    private void DrawRosterList(Vector2 at, float w, float h, TeamData team, Roster roster,
        int cursor, HashSet<PlayerData> tagged, bool focused, string caption, ref int scroll)
    {
        var panel = new Rect2(at, new Vector2(w, h));
        Palette.Panel3D(this, panel, Palette.Panel);
        if (focused) DrawRect(panel, Palette.Highlight, false, 2f);

        DrawRect(new Rect2(at, new Vector2(w, 40f)), team.Primary);
        DrawRect(new Rect2(at + new Vector2(0f, 36f), new Vector2(w, 4f)), team.Secondary);
        Palette.Text(this, at + new Vector2(12f, 26f), $"{caption}  —  {team.FullName}", 15,
            team.TextOnPrimary);

        float y = at.Y + 62f;
        Palette.Text(this, new Vector2(at.X + 12f, y), "POS", 11, Palette.Highlight);
        Palette.Text(this, new Vector2(at.X + 48f, y), "NAME", 11, Palette.Highlight);
        Palette.Text(this, new Vector2(at.X + w - 150f, y), "OVR", 11, Palette.Highlight);
        Palette.Text(this, new Vector2(at.X + w - 100f, y), "VALUE", 11, Palette.Highlight);
        y += 18f;

        // Only what fits inside the panel, and the pane the keys are driving follows its cursor so
        // Up and Down cannot walk a man off the bottom.
        int fits = Mathf.Max(4, (int)((at.Y + h - 14f - y) / 19f));
        if (focused)
        {
            if (cursor < scroll) scroll = cursor;
            else if (cursor >= scroll + fits) scroll = cursor - fits + 1;
        }
        scroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, roster.Players.Count - fits));

        // In the club's colour bar, where there is room. At y+54 it printed on top of the OVR and
        // VALUE headers eight pixels below it.
        if (roster.Players.Count > fits)
            Palette.Text(this, new Vector2(at.X + w - 96f, at.Y + 26f),
                $"{scroll + 1}–{Mathf.Min(scroll + fits, roster.Players.Count)} " +
                $"of {roster.Players.Count}", 11, team.TextOnPrimary);

        for (int i = scroll; i < roster.Players.Count && i < scroll + fits; i++)
        {
            var p = roster.Players[i];
            bool on = focused && i == cursor;
            bool isTagged = tagged.Contains(p);

            // Clicking a player tags or untags him for the deal.
            var rowRect = new Rect2(new Vector2(at.X + 6f, y - 13f), new Vector2(w - 12f, 19f));
            var who = p;
            var set = tagged;
            _clicks.Add(rowRect, () =>
            {
                if (!set.Remove(who)) set.Add(who);
                _response = "";
            });

            if (on) DrawRect(rowRect, Palette.PanelLight);
            if (isTagged)
                DrawRect(new Rect2(new Vector2(at.X + 6f, y - 13f), new Vector2(4f, 19f)),
                    Palette.Highlight);

            var ink = isTagged ? Palette.Highlight : on ? Palette.Ink : Palette.InkDim;
            Palette.Text(this, new Vector2(at.X + 16f, y), p.PositionText, 12, Palette.InkDim);
            Palette.Text(this, new Vector2(at.X + 48f, y), p.Name, 13, ink);

            if (p.Special != Special.None)
                Palette.Text(this, new Vector2(at.X + 190f, y), p.SpecialText, 11, team.Secondary);

            Palette.Text(this, new Vector2(at.X + w - 150f, y), p.Overall.ToString(), 13, ink);
            Palette.Text(this, new Vector2(at.X + w - 100f, y),
                TradeEngine.Value(p).ToString("F0"), 13, Palette.InkDim);
            y += 19f;
        }
    }

    private void DrawOfferSummary(Vector2 size)
    {
        var bar = new Rect2(new Vector2(40f, size.Y - 128f), new Vector2(size.X - 80f, 88f));
        Palette.Panel3D(this, bar, Palette.PanelLight);

        float giveValue = _iGive.Sum(TradeEngine.Value);
        float getValue = _iGet.Sum(TradeEngine.Value);

        Palette.Text(this, bar.Position + new Vector2(16f, 24f),
            $"You give {_iGive.Count} player{(_iGive.Count == 1 ? "" : "s")} ({giveValue:F0} value)   →   " +
            $"You get {_iGet.Count} ({getValue:F0} value)", 15, Palette.Ink);

        string names = _iGive.Count == 0 ? "nobody" : string.Join(", ", _iGive.Select(p => p.ShortName));
        string wants = _iGet.Count == 0 ? "nobody" : string.Join(", ", _iGet.Select(p => p.ShortName));
        Palette.Text(this, bar.Position + new Vector2(16f, 46f), $"Out: {names}", 12, Palette.InkDim);
        Palette.Text(this, bar.Position + new Vector2(16f, 64f), $"In:  {wants}", 12, Palette.InkDim);

        // Offer and clear, so a deal can be made without touching the keyboard.
        var offer = new Rect2(bar.Position + new Vector2(bar.Size.X - 200f, 12f), new Vector2(180f, 30f));
        var clear = new Rect2(bar.Position + new Vector2(bar.Size.X - 200f, 48f), new Vector2(180f, 26f));
        Palette.Panel3D(this, offer, Palette.Highlight.Darkened(0.15f));
        Palette.Panel3D(this, clear, Palette.Panel);
        Palette.TextCentered(this, offer.Position + offer.Size * 0.5f, "SEND OFFER", 16, Palette.Night);
        Palette.TextCentered(this, clear.Position + clear.Size * 0.5f, "Clear", 14, Palette.InkDim);
        _clicks.Add(offer, SendOffer);
        _clicks.Add(clear, ClearOffer);

        if (!string.IsNullOrEmpty(_response))
        {
            var color = _lastAccepted ? new Color("#7ddb8a") : Palette.Warning;
            Palette.Text(this, bar.Position + new Vector2(16f, 82f), _response, 15, color);
        }
    }
}
