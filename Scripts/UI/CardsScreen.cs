using System.Collections.Generic;
using System.Linq;
using Godot;
using SandlotSlugfest.Cards;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.UI;

/// <summary>
/// The collection: open packs, work the market, build a side out of what you own, and take it out.
/// </summary>
public partial class CardsScreen : Control
{
    private enum Tab { Packs, Program, Collection, Market, Club, Franchise }

    private static readonly string[] TabNames =
        { "PACKS", "PROGRAM", "MY CARDS", "MARKET", "MY CLUB", "SIGN TO SEASON" };

    private Tab _tab = Tab.Packs;
    private string _notice = "";
    private float _noticeTimer;
    private PackResult _lastPack;
    private float _revealTimer;
    private int _marketDay;
    private readonly ClickMap _clicks = new();

    /// <summary>How many rows of a long list fit on screen at once.</summary>
    private const int Rows = 16;

    /// <summary>How far down the current list we have scrolled, in rows.</summary>
    private int _scroll;

    /// <summary>
    /// What a second click would actually do.
    ///
    /// Everything on this screen used to happen on one click. Opening a pack cost up to twenty-two
    /// thousand coins and the whole pack panel was live, so a click anywhere in the top third of
    /// the screen spent the money. Selling was worse: one click turned a Diamond into 62% of its
    /// value and there was no way back. Neither of those is a decision you should be able to make
    /// by accident, so the first click now only arms the action and says what it is.
    /// </summary>
    private string _armed;
    private float _armedTimer;

    /// <summary>
    /// Arms an action, or performs it if this is the second click on the same thing. The key has
    /// to be stable across redraws, because the click handlers are rebuilt every frame.
    /// </summary>
    private bool Confirm(string key, string prompt)
    {
        if (_armed == key) { _armed = null; _armedTimer = 0f; return true; }

        _armed = key;
        _armedTimer = 4f;
        Say($"{prompt}  ·  click again to confirm.");
        return false;
    }

    private void Disarm()
    {
        _armed = null;
        _armedTimer = 0f;
    }

    /// <summary>Marks the row a second click would act on, so what is armed is never a guess.</summary>
    private bool IsArmed(string key) => _armed == key;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Collection.Load();

        // The listings turn over with the calendar, so the market is worth looking at again.
        _marketDay = (int)(Time.GetUnixTimeFromSystem() / 3600);
        SetProcess(true);

        // Kinetic touch scroll for the row-based list. Accumulates pixel deltas into whole-row
        // steps so a fling scrolls smoothly at 1:1 with the finger. Row height is roughly 20 px
        // for every tab in this screen.
        TouchScroll.Handler = (px, _) =>
        {
            _touchAccum += px;
            const float row = 20f;
            int step = (int)(_touchAccum / row);
            if (step == 0) return;
            _touchAccum -= step * row;
            Scroll(step);
        };
    }

    public override void _ExitTree() => TouchScroll.Handler = null;

    /// <summary>Fractional pixel bank for kinetic scroll: rolls a full row's worth into a step.</summary>
    private float _touchAccum;

    public override void _Process(double delta)
    {
        if (_noticeTimer > 0f)
        {
            _noticeTimer -= (float)delta;
            if (_noticeTimer <= 0f) { _notice = ""; QueueRedraw(); }
        }

        if (_revealTimer > 0f)
        {
            _revealTimer -= (float)delta;
            QueueRedraw();
        }

        // An armed action does not stay armed. Coming back to the screen ten minutes later and
        // clicking once should never be the click that sells somebody.
        if (_armedTimer > 0f)
        {
            _armedTimer -= (float)delta;
            if (_armedTimer <= 0f) { _armed = null; QueueRedraw(); }
        }
    }

    private void Say(string message)
    {
        _notice = message;
        _noticeTimer = 4.5f;
        QueueRedraw();
    }

    private void Leave()
    {
        Collection.Save();
        Game.Instance.GoTo("res://Scenes/MainMenu.tscn");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion m) { if (_clicks.Hover(m.Position)) QueueRedraw(); return; }
        if (@event is InputEventJoypadButton && _clicks.Controller(@event, Leave))
        { QueueRedraw(); return; }

        if (@event is InputEventMouseButton { Pressed: true } wheel &&
            wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            Scroll(wheel.ButtonIndex == MouseButton.WheelDown ? 3 : -3);
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
            case Key.Escape or Key.Backspace: Leave(); return;
            case Key.Left or Key.A: ShowTab((Tab)Mathf.PosMod((int)_tab - 1, TabNames.Length)); break;
            case Key.Right or Key.D: ShowTab((Tab)Mathf.PosMod((int)_tab + 1, TabNames.Length)); break;
            case Key.Up or Key.W: Scroll(-1); break;
            case Key.Down or Key.S: Scroll(1); break;
            case Key.Pageup: Scroll(-Rows); break;
            case Key.Pagedown: Scroll(Rows); break;
            case Key.Home: Scroll(-9999); break;
        }
        QueueRedraw();
    }

    /// <summary>Changing view puts you at the top of the new list and disarms anything pending.</summary>
    private void ShowTab(Tab tab)
    {
        _tab = tab;
        _scroll = 0;
        Disarm();
    }

    private void Scroll(int rows)
    {
        _scroll = Mathf.Max(0, _scroll + rows);
        Disarm();          // the row under the cursor has moved; whatever was armed is not it now
        QueueRedraw();
    }

    /// <summary>
    /// Clamps the scroll against a list that has just been measured, and says where you are in it.
    /// Called at the top of each list so the position can never point past the end.
    /// </summary>
    private void ScrollNote(float y, int total, string noun)
    {
        _scroll = Mathf.Clamp(_scroll, 0, Mathf.Max(0, total - Rows));
        if (total <= Rows) return;

        Palette.Text(this, new Vector2(700f, y),
            $"{_scroll + 1}–{Mathf.Min(_scroll + Rows, total)} of {total} {noun}  ·  " +
            "scroll wheel or Up/Down", 12, Palette.InkDim);
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Night);
        _clicks.Begin();

        Palette.BackButton(this, size, _clicks, Leave);
        Palette.Text(this, new Vector2(Palette.SafeLeft(size), Palette.SafeTop(size)), "THE COLLECTION", 26, Palette.Ink);
        Palette.Text(this, new Vector2(Palette.SafeLeft(size), Palette.SafeTop(size) + 22f),
            $"{Market.Coins(Collection.Coins)}  ·  {Collection.Size} cards  ·  " +
            $"worth {Market.Coins(Collection.Worth)}", 14, Palette.InkDim);

        DrawTabs();

        switch (_tab)
        {
            case Tab.Packs: DrawPacks(size); break;
            case Tab.Program: DrawProgram(size); break;
            case Tab.Collection: DrawCollection(size); break;
            case Tab.Market: DrawMarket(size); break;
            case Tab.Club: DrawClub(size); break;
            case Tab.Franchise: DrawFranchise(size); break;
        }

        if (_notice != "")
            Palette.Text(this, new Vector2(Palette.SafeLeft(size), Palette.SafeBottom(size, 44f)), _notice, 14, Palette.Highlight);

        Palette.Text(this, new Vector2(Palette.SafeLeft(size), Palette.SafeBottom(size, 22f)),
            "Left/Right to switch views  ·  click to act  ·  Esc to go back", 14, Palette.InkDim);
        _clicks.DrawFocus(this, Palette.Highlight);
    }

    private void DrawTabs()
    {
        float x = 40f;
        for (int i = 0; i < TabNames.Length; i++)
        {
            bool on = (int)_tab == i;
            float w = Palette.TextWidth(TabNames[i], 14) + 28f;
            var rect = new Rect2(new Vector2(x, 88f), new Vector2(w, 30f));
            Palette.Panel3D(this, rect, on ? Palette.Highlight.Darkened(0.2f) : Palette.Panel);
            Palette.TextCentered(this, rect.Position + rect.Size * 0.5f, TabNames[i], 14,
                on ? Palette.Night : Palette.InkDim);

            var picked = (Tab)i;
            _clicks.Add(rect, () => ShowTab(picked));
            x += w + 8f;
        }
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// The reward program: the ladder, the missions and the daily.
    ///
    /// This is where a collection stops being a shop. Every game you play moves the bar, and the
    /// packs it pays out are the ones you did not have to afford.
    /// </summary>
    private void DrawProgram(Vector2 size)
    {
        float y = 158f;

        // The ladder, as a bar with the rungs marked on it.
        var next = Program.Next;
        Palette.Text(this, new Vector2(40f, y),
            next == null
                ? $"THE PROGRAM — finished, {Collection.Xp:N0} XP"
                : $"THE PROGRAM — {Collection.Xp:N0} XP  ·  " +
                  $"{next.Xp - Collection.Xp:N0} to {next.Name}",
            15, Palette.Ink);

        y += 18f;
        var bar = new Rect2(new Vector2(40f, y), new Vector2(size.X - 80f, 16f));
        DrawRect(bar, Palette.Panel);
        float done = Mathf.Clamp(Collection.Xp / (float)Program.Summit, 0f, 1f);
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * done, bar.Size.Y)),
            Palette.Highlight);

        // A tick for every rung, so the shape of what is left is visible at a glance.
        foreach (var rung in Program.Ladder)
        {
            float at = bar.Position.X + bar.Size.X * (rung.Xp / (float)Program.Summit);
            bool got = Collection.Xp >= rung.Xp;
            DrawRect(new Rect2(new Vector2(at - 1f, bar.Position.Y - 3f), new Vector2(2f, 22f)),
                got ? Palette.Night : Palette.InkDim);
        }

        y += 30f;
        Palette.Text(this, new Vector2(40f, y),
            "Every game you play moves this — a collection game, a season game, either one.",
            13, Palette.InkDim);

        // The next few rungs and what they pay.
        y += 24f;
        foreach (var rung in Program.Ladder.Where(r => r.Xp > Collection.Xp).Take(3))
        {
            Palette.Text(this, new Vector2(40f, y), rung.Name, 12, Palette.Highlight);
            Palette.Text(this, new Vector2(220f, y), $"{rung.Xp:N0} XP", 12, Palette.InkDim);
            Palette.Text(this, new Vector2(320f, y),
                rung.Pack >= 0 ? Market.Packs[rung.Pack].Name : Market.Coins(rung.Coins),
                12, Palette.Ink);
            y += 19f;
        }

        // Today's pack.
        y += 16f;
        var daily = new Rect2(new Vector2(40f, y), new Vector2(240f, 34f));
        bool ready = Program.DailyReady;
        Palette.Panel3D(this, daily, ready ? Palette.Highlight.Darkened(0.2f) : Palette.Panel);
        Palette.TextCentered(this, daily.Position + daily.Size * 0.5f,
            ready ? "CLAIM TODAY'S PACK" : "CLAIMED — BACK TOMORROW", 12,
            ready ? Palette.Night : Palette.InkDim);

        if (ready)
            _clicks.Add(daily, () =>
            {
                string got = Program.ClaimDaily();
                if (got == null) return;
                Collection.Save();
                Say(got);
            });

        // The missions.
        y += 52f;
        Palette.Text(this, new Vector2(40f, y), "MISSIONS", 13, Palette.Highlight);
        y += 24f;

        foreach (var m in Program.Missions)
        {
            bool done2 = Program.Complete(m);
            bool claimed = Program.Claimed(m);
            int at = Mathf.Min(m.Progress(), m.Target);

            Palette.Text(this, new Vector2(44f, y), m.Name, 12,
                claimed ? Palette.InkDim : done2 ? Palette.Highlight : Palette.Ink);
            Palette.Text(this, new Vector2(260f, y), m.Detail, 11, Palette.InkDim);
            Palette.Text(this, new Vector2(700f, y), $"{at}/{m.Target}", 11,
                done2 ? Palette.Highlight : Palette.InkDim);
            Palette.Text(this, new Vector2(770f, y),
                m.Pack >= 0 ? Market.Packs[m.Pack].Name : Market.Coins(m.Coins), 11, Palette.InkDim);

            if (claimed)
            {
                Palette.Text(this, new Vector2(950f, y), "COLLECTED", 11, Palette.InkDim);
            }
            else if (done2)
            {
                var claim = new Rect2(new Vector2(946f, y - 13f), new Vector2(96f, 19f));
                Palette.Panel3D(this, claim, Palette.Highlight.Darkened(0.2f));
                Palette.TextCentered(this, claim.Position + claim.Size * 0.5f, "COLLECT", 11,
                    Palette.Night);

                var target = m;
                _clicks.Add(claim, () =>
                {
                    string got = Program.Claim(target);
                    if (got == null) return;
                    Collection.Save();
                    Say(got);
                });
            }

            y += 20f;
        }
    }

    /// <summary>
    /// Packs you have earned and not yet opened.
    ///
    /// They sit here rather than opening themselves, because the opening is the good part and it
    /// should happen when you are looking at it.
    /// </summary>
    private void DrawVault(Vector2 size)
    {
        float y = 150f;

        if (Collection.Vault.Count == 0)
        {
            Palette.Text(this, new Vector2(40f, y),
                "No earned packs waiting. Play games and work the PROGRAM to win them.",
                13, Palette.InkDim);
            return;
        }

        Palette.Text(this, new Vector2(40f, y),
            $"EARNED — {Collection.Vault.Count} unopened", 13, Palette.Highlight);

        float x = 250f;
        foreach (var group in Collection.Vault.GroupBy(p => p).OrderBy(g => g.Key))
        {
            var pack = Market.Packs[group.Key];
            string label = $"{pack.Name} ×{group.Count()}";
            float bw = Palette.TextWidth(label, 11) + 24f;
            var rect = new Rect2(new Vector2(x, y - 15f), new Vector2(bw, 26f));

            Palette.Panel3D(this, rect, Palette.Highlight.Darkened(0.2f));
            Palette.TextCentered(this, rect.Position + rect.Size * 0.5f, label, 11, Palette.Night);

            int which = group.Key;
            _clicks.Add(rect, () =>
            {
                // No confirmation here: an earned pack cost nothing, so opening one by accident
                // costs nothing either. The confirmation exists to protect coins.
                var opened = Market.OpenEarned(which);
                if (opened == null) return;

                _lastPack = opened;
                _revealTimer = 1.2f;
                Collection.Save();
                var best = opened.Best;
                Say(best == null ? "Empty pack."
                    : $"Best card: {best.Player.Name} — {best.TierText}, " +
                      $"worth {Market.Coins(best.Value)}.");
            });

            x += bw + 8f;
        }
    }

    private void DrawPacks(Vector2 size)
    {
        DrawVault(size);

        float y = 186f;
        float w = 236f;

        for (int i = 0; i < Market.Packs.Length; i++)
        {
            var pack = Market.Packs[i];
            var rect = new Rect2(new Vector2(40f + i * (w + 10f), y), new Vector2(w, 150f));
            bool afford = Collection.Coins >= pack.Price;
            string key = $"pack:{pack.Name}";
            bool armed = IsArmed(key);

            Palette.Panel3D(this, rect,
                armed ? Palette.Highlight.Darkened(0.55f) : afford ? Palette.PanelLight : Palette.Panel);
            Palette.Text(this, rect.Position + new Vector2(16f, 30f), pack.Name, 17,
                afford ? Palette.Ink : Palette.InkDim);
            Palette.Text(this, rect.Position + new Vector2(16f, 54f), pack.Blurb, 12, Palette.InkDim);

            // The odds are on the packet. A collection mode that hides them is a slot machine.
            Palette.Text(this, rect.Position + new Vector2(16f, 84f),
                $"diamond {pack.DiamondChance * 100f:0.#}%   " +
                $"gold {Mathf.Min(pack.GoldChance, 1f) * 100f:0}%   " +
                $"{pack.Cards} cards", 11, Palette.InkDim);

            Palette.Text(this, rect.Position + new Vector2(16f, 122f), Market.Coins(pack.Price), 20,
                afford ? Palette.Highlight : Palette.Warning);

            // Only this button buys. The whole panel used to be live, which made a click anywhere
            // in the top third of the screen worth up to twenty-two thousand coins.
            var buy = new Rect2(rect.Position + new Vector2(rect.Size.X - 118f, 100f),
                new Vector2(102f, 32f));
            Palette.Panel3D(this, buy, armed ? Palette.Warning.Darkened(0.25f)
                                     : afford ? Palette.Highlight.Darkened(0.2f) : Palette.Panel);
            Palette.TextCentered(this, buy.Position + buy.Size * 0.5f,
                armed ? "CONFIRM" : "OPEN", 13, afford ? Palette.Night : Palette.InkDim);

            var chosen = pack;
            _clicks.Add(buy, () =>
            {
                if (Collection.Coins < chosen.Price)
                {
                    Say($"That's {Market.Coins(chosen.Price)} and you have " +
                        $"{Market.Coins(Collection.Coins)}. Sell some duplicates.");
                    return;
                }

                if (!Confirm(key, $"Open {chosen.Name} for {Market.Coins(chosen.Price)}?")) return;
                if (!Collection.Spend(chosen.Price)) return;

                _lastPack = Market.Open(chosen);
                _revealTimer = 1.2f;
                Collection.Save();
                var best = _lastPack.Best;
                Say(best == null ? "Empty pack." :
                    $"Best card: {best.Player.Name} — {best.TierText}, worth {Market.Coins(best.Value)}.");
            });
        }

        if (_lastPack == null)
        {
            Palette.Text(this, new Vector2(40f, y + 190f),
                "Open a pack to start a collection. You can sell anything you don't want.",
                15, Palette.InkDim);
            return;
        }

        Palette.Text(this, new Vector2(40f, y + 190f), "LAST PACK", 13, Palette.Highlight);
        float cy = y + 216f;

        foreach (var card in _lastPack.Cards.OrderByDescending(c => c.Value))
        {
            DrawCardRow(cy, card, Market.Coins(card.Value), "");
            cy += 22f;
        }
    }

    // -----------------------------------------------------------------------

    private void DrawCollection(Vector2 size)
    {
        float y = 150f;

        if (Collection.Size == 0)
        {
            Palette.Text(this, new Vector2(40f, y),
                "No cards yet. Open a pack.", 15, Palette.InkDim);
            return;
        }

        Palette.Text(this, new Vector2(40f, y),
            "Click a card to sell it, then click again to confirm. Duplicates pay for the next pack.",
            14, Palette.InkDim);
        y += 28f;

        var all = Collection.Mine.ToList();
        ScrollNote(y, all.Count, "cards");
        Header(y);
        y += 22f;

        foreach (var card in all.Skip(_scroll).Take(Rows))
        {
            int owned = Collection.CountOf(card.Player.Id);
            var rect = new Rect2(new Vector2(34f, y - 13f), new Vector2(size.X - 68f, 20f));
            string key = $"sell:{card.Player.Id}";

            if (IsArmed(key))
                DrawRect(rect, Palette.Warning.Darkened(0.62f));

            DrawCardRow(y, card, owned > 1 ? $"x{owned}" : "",
                IsArmed(key) ? "CONFIRM SELL" : $"SELL {Market.Coins(Market.SellPrice(card))}");

            // The action word is the button, not the row. A whole row of live pixels a thousand
            // pixels wide is how a card gets sold by somebody reaching for the scroll wheel.
            var target = card;
            _clicks.Add(Action(y), () =>
            {
                if (Collection.Lineup.ContainsValue(target.Player.Id) ||
                    Collection.Staff.Contains(target.Player.Id))
                {
                    if (Collection.CountOf(target.Player.Id) <= 1)
                    {
                        Say($"{target.Player.Name} is in your club. Take him out first.");
                        return;
                    }
                }

                int paid = Market.SellPrice(target);
                if (!Confirm(key,
                    $"Sell {target.Player.Name} ({target.TierText}, worth " +
                    $"{Market.Coins(target.Value)}) for {Market.Coins(paid)}?")) return;

                if (Market.Sell(target))
                {
                    Collection.Save();
                    Say($"Sold {target.Player.Name} for {Market.Coins(paid)}.");
                }
            });
            y += 22f;
        }
    }

    // -----------------------------------------------------------------------

    private void DrawMarket(Vector2 size)
    {
        float y = 150f;
        Palette.Text(this, new Vector2(40f, y),
            "On the block today. Listings turn over every hour.", 14, Palette.InkDim);
        y += 28f;

        var listings = Market.Listings(_marketDay).ToList();
        ScrollNote(y, listings.Count, "listed");
        Header(y);
        y += 22f;

        foreach (var card in listings.Skip(_scroll).Take(Rows))
        {
            var rect = new Rect2(new Vector2(34f, y - 13f), new Vector2(size.X - 68f, 20f));
            bool afford = Collection.Coins >= Market.BuyPrice(card);
            string key = $"buy:{card.Player.Id}";

            if (IsArmed(key)) DrawRect(rect, Palette.Highlight.Darkened(0.62f));

            DrawCardRow(y, card, Collection.Has(card.Player.Id) ? "owned" : "",
                !afford ? $"{Market.Coins(Market.BuyPrice(card))}"
                : IsArmed(key) ? "CONFIRM BUY"
                : $"BUY {Market.Coins(Market.BuyPrice(card))}");

            var target = card;
            if (!afford) { y += 22f; continue; }
            _clicks.Add(Action(y), () =>
            {
                // A mis-click costs the buy-sell spread, which is most of a Diamond. Ask.
                if (!Confirm(key,
                    $"Buy {target.Player.Name} ({target.TierText}) for " +
                    $"{Market.Coins(Market.BuyPrice(target))}?")) return;

                string refused = Market.Buy(target);
                if (refused != null) { Say(refused); return; }
                Collection.Save();
                Say($"Signed {target.Player.Name} for {Market.Coins(Market.BuyPrice(target))}.");
            });
            y += 22f;
        }
    }

    // -----------------------------------------------------------------------

    private void DrawClub(Vector2 size)
    {
        float y = 150f;
        Palette.Text(this, new Vector2(40f, y),
            $"{Collection.ClubStatus}  ·  rating {Collection.Rating}", 15,
            Collection.ClubIsReady ? Palette.Highlight : Palette.InkDim);

        // Take the field.
        var play = new Rect2(new Vector2(size.X - 260f, y - 16f), new Vector2(220f, 34f));
        Palette.Panel3D(this, play, Collection.ClubIsReady ? Palette.Highlight.Darkened(0.2f) : Palette.Panel);
        Palette.TextCentered(this, play.Position + play.Size * 0.5f, "PLAY A GAME", 14,
            Collection.ClubIsReady ? Palette.Night : Palette.InkDim);
        _clicks.Add(play, StartGame);

        y += 34f;

        // The nine slots, and who is in them.
        foreach (var slot in Collection.Slots)
        {
            var rect = new Rect2(new Vector2(34f, y - 13f), new Vector2(560f, 20f));
            string label = PlayerData.PositionLabel(slot);

            Collection.Lineup.TryGetValue(slot, out int id);
            var card = id == 0 ? null : Collection.Find(id);

            Palette.Text(this, new Vector2(44f, y), label, 12, Palette.Highlight);
            if (card == null)
            {
                Palette.Text(this, new Vector2(90f, y), "— empty —", 13, Palette.InkDim);
            }
            else
            {
                DrawRect(new Rect2(new Vector2(84f, y - 10f), new Vector2(4f, 13f)), card.Colour);
                Palette.Text(this, new Vector2(96f, y), card.Player.Name, 13, Palette.Ink);
                Palette.Text(this, new Vector2(300f, y), $"OVR {card.Player.Overall}", 12, Palette.InkDim);
                Palette.Text(this, new Vector2(370f, y), card.TierText, 11, card.Colour);
                Palette.Text(this, new Vector2(470f, y), "REMOVE", 11, Palette.Warning);
            }

            var chosenSlot = slot;
            var occupant = card;
            _clicks.Add(rect, () =>
            {
                if (occupant != null)
                {
                    Collection.Lineup.Remove(chosenSlot);
                    Collection.Save();
                    return;
                }

                // Fill it with the best card you own who is not already playing.
                var pick = Collection.Mine
                    .Where(c => c.Player.Position != Data.Position.P)
                    .Where(c => !Collection.Lineup.ContainsValue(c.Player.Id))
                    .OrderByDescending(c => (c.Player.Position == chosenSlot ? 12 : 0) + c.Player.Overall)
                    .FirstOrDefault();

                if (pick == null) { Say("No spare position players in the collection."); return; }
                Collection.Assign(chosenSlot, pick.Player.Id);
                Collection.Save();
                Say($"{pick.Player.Name} takes {PlayerData.PositionLabel(chosenSlot)}.");
            });
            y += 22f;
        }

        // The staff.
        y += 12f;
        Palette.Text(this, new Vector2(40f, y), "PITCHING STAFF", 13, Palette.Highlight);
        y += 24f;

        for (int i = 0; i < Collection.StaffSize; i++)
        {
            var rect = new Rect2(new Vector2(34f, y - 13f), new Vector2(560f, 20f));
            var card = i < Collection.Staff.Count ? Collection.Find(Collection.Staff[i]) : null;

            Palette.Text(this, new Vector2(44f, y), $"SP{i + 1}", 12, Palette.Highlight);
            if (card == null)
            {
                Palette.Text(this, new Vector2(90f, y), "— empty —", 13, Palette.InkDim);
            }
            else
            {
                DrawRect(new Rect2(new Vector2(84f, y - 10f), new Vector2(4f, 13f)), card.Colour);
                Palette.Text(this, new Vector2(96f, y), card.Player.Name, 13, Palette.Ink);
                Palette.Text(this, new Vector2(300f, y),
                    $"VEL {card.Player.PitchPower} CMD {card.Player.PitchControl}", 12, Palette.InkDim);
                Palette.Text(this, new Vector2(470f, y), "REMOVE", 11, Palette.Warning);
            }

            var occupant = card;
            _clicks.Add(rect, () =>
            {
                if (occupant != null)
                {
                    Collection.Staff.Remove(occupant.Player.Id);
                    Collection.Save();
                    return;
                }

                var pick = Collection.Mine
                    .Where(c => c.Player.Position == Data.Position.P)
                    .Where(c => !Collection.Staff.Contains(c.Player.Id))
                    .OrderByDescending(c => c.Player.Overall)
                    .FirstOrDefault();

                if (pick == null) { Say("No spare arms in the collection."); return; }
                Collection.AddToStaff(pick.Player.Id);
                Collection.Save();
                Say($"{pick.Player.Name} joins the rotation.");
            });
            y += 22f;
        }

        // The minors used to run on below the staff, which put them off the bottom of the screen:
        // nine lineup rows plus five arms already reach y=530, and eight more took it past 720.
        // They get their own column instead, where the whole farm fits.
        DrawMinors(size, 184f);
    }

    /// <summary>
    /// The club's own minors: cards you are not playing but are not finished with.
    ///
    /// Without somewhere to put them, every card outside the nine faced the same button — sell —
    /// and a young player you believe in went the same way as a duplicate you will never use.
    /// </summary>
    private void DrawMinors(Vector2 size, float y)
    {
        const float X = 650f;

        Palette.Text(this, new Vector2(X, y),
            $"THE MINORS  ({Collection.Minors.Count}/{Collection.MinorsSize})", 13, Palette.Highlight);

        // Send the best spare card down.
        var send = new Rect2(new Vector2(X + 250f, y - 16f), new Vector2(190f, 24f));
        Palette.Panel3D(this, send, Palette.PanelLight);
        Palette.TextCentered(this, send.Position + send.Size * 0.5f, "SEND ONE DOWN", 11, Palette.Ink);
        _clicks.Add(send, () =>
        {
            var spare = Collection.Spare.FirstOrDefault();
            if (spare == null) { Say("Nothing spare to send down."); return; }
            if (!Collection.SendDown(spare.Player.Id))
            {
                Say($"The minors are full at {Collection.MinorsSize}.");
                return;
            }
            Collection.Save();
            Say($"{spare.Player.Name} is sent to the minors.");
        });

        y += 26f;

        if (Collection.Minors.Count == 0)
        {
            Palette.Text(this, new Vector2(X, y),
                "Empty. Send cards down to keep", 13, Palette.InkDim);
            Palette.Text(this, new Vector2(X, y + 18f),
                "them without playing them.", 13, Palette.InkDim);
            return;
        }

        foreach (int id in Collection.Minors.ToList())
        {
            var card = Collection.Find(id);
            if (card == null) continue;

            var rect = new Rect2(new Vector2(X - 6f, y - 13f), new Vector2(560f, 20f));
            DrawRect(new Rect2(new Vector2(X - 6f, y - 10f), new Vector2(5f, 13f)), card.Colour);
            Palette.Text(this, new Vector2(X + 10f, y), card.Player.Name, 13, Palette.Ink);
            Palette.Text(this, new Vector2(X + 210f, y),
                $"{PlayerData.PositionLabel(card.Player.Position)}  OVR {card.Player.Overall}",
                12, Palette.InkDim);
            Palette.Text(this, new Vector2(X + 310f, y), card.TierText, 11, card.Colour);
            Palette.Text(this, new Vector2(X + 400f, y), "CALL UP", 11, Palette.Highlight);

            int who = id;
            var promoted = card;
            _clicks.Add(rect, () =>
            {
                Collection.CallUp(who);
                Collection.Save();
                Say($"{promoted.Player.Name} is back in the collection — put him in a slot.");
            });
            y += 22f;
        }
    }

    /// <summary>Takes the built club out against a real league side.</summary>
    private void StartGame()
    {
        if (!Collection.ClubIsReady)
        {
            Say("You need nine in the lineup and at least one arm before you can play.");
            return;
        }

        Collection.Save();

        var g = Game.Instance;
        g.PendingSeasonGame = null;
        g.CardClubRoster = Collection.BuildRoster();

        // Your side visits; the opposition is drawn from the league so there is a real yardstick.
        var rng = new Rng(Collection.Rating * 31 + Collection.Size);
        g.HomeTeamId = Teams.All[rng.Range(0, Teams.All.Count)].Id;
        g.Mode = ControlMode.PlayerVsCpu;
        g.GoTo("res://Scenes/Game.tscn");
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Playing cards into the season or dynasty you are actually running.
    ///
    /// A card is spent to do it and the man is transferred rather than copied — there is one of
    /// everybody in this league and that stays true — so this is a real decision about a real
    /// roster, not a cheat menu.
    /// </summary>
    private void DrawFranchise(Vector2 size)
    {
        var season = Game.Instance.League;
        float y = 150f;

        if (season == null)
        {
            Palette.Text(this, new Vector2(40f, y),
                "No season in progress. Start a Season or a Dynasty first.", 15, Palette.InkDim);
            return;
        }

        var club = Teams.Get(season.UserTeamId);
        var roster = season.RosterFor(season.UserTeamId);
        int space = Season.Finances.SpaceFor(season, season.UserTeamId);

        Palette.Text(this, new Vector2(40f, y),
            $"{club.FullName} · year {season.Year} · roster {roster.Players.Count}/" +
            $"{Season.Development.RosterLimit} · {Season.Contracts.Text(space)} of room",
            14, Palette.InkDim);

        y += 24f;
        Palette.Text(this, new Vector2(40f, y),
            "Playing a card signs him to your club. He leaves the club he was on, they call " +
            "somebody up, and the card is spent.", 13, Palette.InkDim);

        y += 30f;

        if (Collection.Size == 0)
        {
            Palette.Text(this, new Vector2(40f, y), "No cards to play.", 15, Palette.InkDim);
            return;
        }

        var all = Collection.Mine.ToList();
        ScrollNote(y, all.Count, "cards");
        Header(y);
        y += 22f;

        foreach (var card in all.Skip(_scroll).Take(Rows))
        {
            string refusal = Cards.CardSigning.Refusal(season, card);
            var rect = new Rect2(new Vector2(34f, y - 13f), new Vector2(size.X - 68f, 20f));
            string key = $"sign:{card.Player.Id}";

            if (IsArmed(key)) DrawRect(rect, Palette.Highlight.Darkened(0.62f));

            DrawCardRow(y, card, Season.Contracts.Text(card.Player.Salary),
                refusal != null ? "—" : IsArmed(key) ? "CONFIRM" : "SIGN");

            var target = card;
            _clicks.Add(Action(y), () =>
            {
                string no = Cards.CardSigning.Refusal(season, target);
                if (no != null) { Say(no); return; }

                // This one is properly irreversible: the card is spent and the man changes clubs.
                if (!Confirm(key,
                    $"Sign {target.Player.Name} to {club.Abbrev} at " +
                    $"{Season.Contracts.Text(target.Player.Salary)}? The card is spent.")) return;

                string headline = Cards.CardSigning.Play(season, target);
                if (headline == null) { Say("That signing could not be made."); return; }

                Season.SaveGame.Save(season);
                Say(headline);
            });
            y += 22f;
        }
    }

    /// <summary>
    /// The clickable box for a list row's action, sitting exactly where DrawCardRow prints the
    /// action word. Rows themselves are not clickable: a live strip the full width of the screen
    /// turns a misplaced click into a sale, and the whole point of this screen is that nothing
    /// expensive happens by accident.
    /// </summary>
    private static Rect2 Action(float y) => new(new Vector2(614f, y - 13f), new Vector2(150f, 20f));

    private void Header(float y)
    {
        Palette.Text(this, new Vector2(40f, y), "PLAYER", 12, Palette.InkDim);
        Palette.Text(this, new Vector2(260f, y), "POS", 12, Palette.InkDim);
        Palette.Text(this, new Vector2(310f, y), "OVR", 12, Palette.InkDim);
        Palette.Text(this, new Vector2(360f, y), "TIER", 12, Palette.InkDim);
        Palette.Text(this, new Vector2(460f, y), "CLUB", 12, Palette.InkDim);
        Palette.Text(this, new Vector2(540f, y), "", 12, Palette.InkDim);
    }

    private void DrawCardRow(float y, Card card, string note, string action)
    {
        DrawRect(new Rect2(new Vector2(34f, y - 10f), new Vector2(5f, 13f)), card.Colour);
        Palette.Text(this, new Vector2(46f, y), card.Player.Name, 13, Palette.Ink);
        Palette.Text(this, new Vector2(260f, y), PlayerData.PositionLabel(card.Player.Position), 12,
            Palette.InkDim);
        Palette.Text(this, new Vector2(310f, y), $"{card.Player.Overall}", 12, Palette.InkDim);
        Palette.Text(this, new Vector2(360f, y), card.TierText, 11, card.Colour);
        Palette.Text(this, new Vector2(460f, y), Teams.Get(card.TeamId).Abbrev, 12, Palette.InkDim);
        if (note != "") Palette.Text(this, new Vector2(540f, y), note, 12, Palette.Highlight);
        if (action != "") Palette.Text(this, new Vector2(620f, y), action, 12, Palette.Highlight);
    }
}
