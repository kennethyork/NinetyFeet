using System.Linq;
using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.UI;

namespace SandlotSlugfest.Gameplay;

/// <summary>The broadcast scoreboard: score bug, count, outs, bases, line score and the play banner.</summary>
public partial class Hud : Node2D
{
    public GameScene Scene;

    /// <summary>Hit boxes for the pitch buttons, so the game is playable with a mouse alone.</summary>
    public readonly ClickMap Clicks = new();

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        var s = Scene.Situation;

        Clicks.Begin();
        DrawScoreBug(new Vector2(20f, 18f), s);
        DrawLineScore(new Vector2(size.X - 24f, 18f), s);
        DrawPlayLog(new Vector2(20f, size.Y - 110f));

        // Blocking banners are now only for the moments that genuinely stop play. Everything
        // during an at-bat comes through the toast instead.
        if (!string.IsNullOrEmpty(Scene.BannerText) &&
            Scene.Phase is AtBatPhase.Intro or AtBatPhase.HalfBreak or AtBatPhase.Over)
        {
            DrawBanner(size, Scene.BannerText);
        }

        if (Scene.Phase == AtBatPhase.HalfBreak) DrawBetweenInnings(size);

        if (Scene.Phase == AtBatPhase.Over)
        {
            Palette.TextCentered(this, new Vector2(size.X * 0.5f, size.Y * 0.72f),
                "Press SPACE to return to the menu", 20, Palette.Highlight);
        }

        if (Scene.HumanPitching && Scene.Phase == AtBatPhase.PitchSelect)
            DrawPitchPicker(size);

        if (Scene.SwingFeedbackTimer > 0f) DrawSwingFeedback(size);
        if (Scene.ToastTimer > 0f) DrawToast(size);
        DrawChallengePrompt(size);
        DrawThrowOverDiamond(size);
        if (Scene.HumanPitching && Scene.Phase == AtBatPhase.PitchSelect) DrawPitchClock(size);

        // The hitter's own signature move, offered while he is in the box.
        if (Scene.HumanBatting && Scene.Phase is AtBatPhase.PitchSelect or AtBatPhase.PitchFlight)
            DrawPowerUpChip(size, s.Batter);

        DrawParkStrip(size);
    }

    /// <summary>
    /// The park, the crowd and the sky. A broadcast tells you where you are and what it is like
    /// there before it tells you anything else, and until now the game never did — every night
    /// looked the same and the wind that just carried a fly ball out was invisible.
    /// </summary>
    private void DrawParkStrip(Vector2 size)
    {
        if (Scene.Phase == AtBatPhase.Over) return;

        var park = FieldGeometry.Current;
        string line = park.Name;
        if (Scene.Crowd > 0) line += $"   {Season.Attendance.Text(Scene.Crowd)}";
        line += $"   {Scene.Conditions.Text}";
        if (Scene.Conditions.WindText != "Calm") line += $"   wind {Scene.Conditions.WindText}";

        float w = Palette.TextWidth(line, 12) + 24f;
        var rect = new Rect2(new Vector2(size.X * 0.5f - w * 0.5f, size.Y - 30f), new Vector2(w, 22f));
        Palette.Panel3D(this, rect, Palette.Panel);
        Palette.TextCentered(this, rect.Position + rect.Size * 0.5f, line, 12, Palette.InkDim);
    }

    /// <summary>How wide the score bug is. Everything inside it is positioned against this.</summary>
    private const float BugWidth = 348f;

    private void DrawScoreBug(Vector2 at, GameSituation s)
    {
        var away = s.Away.Team;
        var home = s.Home.Team;

        // A little taller than it was: the velocity readout gets its own line rather than
        // printing on top of the out indicator.
        //
        // And wide enough for its own contents. The challenge pips sat 7 pixels past the right
        // edge — three dots per club hanging in the air outside the box they belong to — because
        // they were positioned from a number that was never checked against the panel's width.
        // 348 clears the rightmost pip with a margin rather than by a hair.
        var panel = new Rect2(at, new Vector2(BugWidth, 114f));
        Palette.Panel3D(this, panel, Palette.Panel);

        // Two club rows with the score.
        DrawTeamRow(at + new Vector2(0f, 4f), away.Abbrev, away.Primary, away.Secondary,
            s.AwayScore, s.TopHalf);
        DrawTeamRow(at + new Vector2(0f, 38f), home.Abbrev, home.Primary, home.Secondary,
            s.HomeScore, !s.TopHalf);

        // Inning, outs, count.
        float x = at.X + 152f;
        Palette.Text(this, new Vector2(x, at.Y + 24f), s.InningText, 16, Palette.Ink);
        Palette.Text(this, new Vector2(x, at.Y + 46f), $"Count {s.CountText}", 15, Palette.InkDim);

        int shownOuts = Scene.DisplayOuts;
        for (int i = 0; i < 3; i++)
        {
            var dot = new Vector2(x + 4f + i * 16f, at.Y + 62f);
            DrawCircle(dot, 5.5f, i < shownOuts ? Palette.Warning : Palette.PanelLight);
        }
        Palette.Text(this, new Vector2(x + 56f, at.Y + 67f), "OUT", 13, Palette.InkDim);

        // The gun reading, the way a broadcast shows it. Velocity is a real cue for a hitter and
        // it was nowhere on screen at all.
        if (Scene.LastPitchMph > 0f)
        {
            Palette.Text(this, new Vector2(at.X + 12f, at.Y + 104f),
                $"{Scene.LastPitchMph:F0}", 20, Palette.Highlight);
            Palette.Text(this, new Vector2(at.X + 48f, at.Y + 104f), "MPH", 12, Palette.InkDim);
            Palette.Text(this, new Vector2(at.X + 84f, at.Y + 104f),
                Scene.LastPitchName, 14, Palette.Ink);
        }

        DrawBaseDiamond(at + new Vector2(258f, 46f), s);
        DrawChallenges(at, s);
        DrawMatchupCard(s);
        // Only one of these can apply: you are either at the plate or in the field.
        DrawStealPrompt(at, s);
        DrawDefencePrompt(at, s);
    }

    /// <summary>
    /// Challenges remaining, and the prompt to use one. Under the 2026 automated ball-strike rule
    /// a club carries two, keeps one whenever a challenge succeeds, and gets more in extra innings.
    /// </summary>
    private void DrawChallenges(Vector2 at, GameSituation s)
    {
        var bank = Scene.Challenges;
        for (int club = 0; club < 2; club++)
        {
            bool awayClub = club == 0;
            int left = bank.Remaining(awayClub);
            float y = at.Y + 12f + club * 34f;

            for (int i = 0; i < 3; i++)
                DrawCircle(new Vector2(at.X + BugWidth - 30f + i * 9f, y), 3.2f,
                    i < left ? Palette.Highlight : Palette.PanelLight);
        }
    }

    /// <summary>
    /// The throw-over pad: a base diamond in the top right you can click to hold a runner on.
    /// Only shown when there is somebody to throw at and you are the one on the mound.
    /// </summary>
    private void DrawThrowOverDiamond(Vector2 size)
    {
        if (!Scene.CanThrowOver) return;

        var centre = new Vector2(size.X - 96f, 150f);
        const float r = 34f;

        Palette.Panel3D(this, new Rect2(centre - new Vector2(62f, 58f), new Vector2(124f, 116f)),
            new Color(0.09f, 0.12f, 0.17f, 0.88f));
        Palette.TextCentered(this, centre + new Vector2(0f, -44f), "THROW OVER", 10, Palette.InkDim);

        // First right, second up, third left — the diamond as seen from behind the plate.
        var spots = new (int Base, Vector2 At)[]
        {
            (1, centre + new Vector2(r, 0f)),
            (2, centre + new Vector2(0f, -r)),
            (3, centre + new Vector2(-r, 0f)),
        };

        foreach (var (baseIndex, at) in spots)
        {
            bool occupied = Scene.Situation.RunnerOn(baseIndex);
            DrawSquare(at, 9f, occupied ? Palette.Highlight : Palette.PanelLight);

            if (!occupied) continue;
            int pick = baseIndex;
            Clicks.Add(new Rect2(at - new Vector2(15f, 15f), new Vector2(30f, 30f)),
                () => Scene.ThrowOver(pick));
        }

        // Home, for orientation only.
        DrawSquare(centre + new Vector2(0f, r), 7f, Palette.PanelLight);

        int left = GameScene.DisengagementLimit - Scene.Disengagements + 1;
        Palette.TextCentered(this, centre + new Vector2(0f, 52f),
            left > 0 ? $"{left} left before a balk" : "next one is a balk", 10,
            left > 0 ? Palette.InkDim : Palette.Warning);
    }

    /// <summary>A base, drawn as a diamond.</summary>
    private void DrawSquare(Vector2 at, float half, Color colour) =>
        DrawColoredPolygon(new[]
        {
            at + new Vector2(0f, -half), at + new Vector2(half, 0f),
            at + new Vector2(0f, half), at + new Vector2(-half, 0f),
        }, colour);

    /// <summary>The tap-the-helmet prompt, shown only while the call can still be challenged.</summary>
    private void DrawChallengePrompt(Vector2 size)
    {
        if (!Scene.CanChallenge) return;

        var box = new Rect2(new Vector2(size.X * 0.5f - 190f, size.Y * 0.16f), new Vector2(380f, 44f));
        Palette.Panel3D(this, box, new Color(0.10f, 0.14f, 0.20f, 0.92f));
        DrawRect(box, Palette.Highlight, false, 2f);
        Palette.TextCentered(this, box.Position + box.Size * 0.5f + new Vector2(0f, 5f),
            $"{(Scene.LastCallWasStrike ? "STRIKE" : "BALL")} — press R to challenge", 15, Palette.Ink);
    }

    private void DrawTeamRow(Vector2 at, string abbrev, Color primary, Color secondary, int score, bool batting)
    {
        var rect = new Rect2(at + new Vector2(4f, 0f), new Vector2(140f, 30f));
        DrawRect(rect, primary);
        DrawRect(new Rect2(rect.Position, new Vector2(4f, rect.Size.Y)), secondary);

        var ink = primary.Luminance > 0.45f ? Palette.Night : Palette.Ink;
        Palette.Text(this, rect.Position + new Vector2(14f, 21f), abbrev, 17, ink);
        Palette.Text(this, rect.Position + new Vector2(104f, 21f), score.ToString(), 19, ink);

        if (batting)
            DrawCircle(rect.Position + new Vector2(88f, 15f), 4f, Palette.Highlight);
    }

    /// <summary>
    /// The steal sign, spelled out.
    ///
    /// Stealing has been bound to G the whole time and the game never once said so. You had to
    /// already know the rule — a lead runner with the bag ahead of him empty — and then remember
    /// a key nothing mentioned. A verb the game never tells you about is a verb almost nobody
    /// uses, which is why half of a manager's job sat unused.
    ///
    /// It names the runner and prints his speed, because whether to send him is the actual
    /// decision and his legs are what it turns on.
    /// </summary>
    private void DrawStealPrompt(Vector2 at, GameSituation s)
    {
        if (!Scene.HumanBatting) return;
        if (Scene.Phase is not (AtBatPhase.PitchSelect or AtBatPhase.PitchFlight)) return;

        int lead = Core.Baserunning.LeadRunner(s);
        if (lead <= 0) return;

        var man = s.Runners[lead];
        if (man == null) return;

        float beat = 0.5f + 0.5f * Mathf.Sin(_pulse * 6f);
        var tint = new Color(0.45f, 1f, 0.55f, 0.65f + beat * 0.35f);

        var rect = new Rect2(at + new Vector2(0f, 122f), new Vector2(BugWidth, 26f));
        Palette.Panel3D(this, rect, Palette.Panel);
        Palette.Text(this, rect.Position + new Vector2(10f, 18f),
            $"← / G — STEAL   {man.ShortName}   SPD {man.Speed}", 13, tint);
    }

    // The matchup card: who is up, and the one number that makes it interesting.
    private Data.PlayerData _cardFor;
    private float _cardLeft;

    /// <summary>
    /// The graphic a broadcast puts up when a new man comes to the plate.
    ///
    /// It exists because the splits do. Telling you a hitter is batting .262 is filler; telling
    /// you he is batting .310 against right-handers while a right-hander is warming is the reason
    /// to look up. The card is built out of the season book and shows nothing it does not know —
    /// a hitter with nine at-bats gets his name and no numbers rather than a made-up line.
    /// </summary>
    private void DrawMatchupCard(GameSituation s)
    {
        // A new man at the plate puts the card up. Nothing else does, so it never covers a pitch.
        if (s.Batter != _cardFor)
        {
            _cardFor = s.Batter;
            _cardLeft = Broadcast.CardSeconds;
        }

        if (_cardLeft <= 0f || s.Batter == null) return;
        _cardLeft -= (float)GetProcessDeltaTime();

        var card = Broadcast.Matchup(s.Batter, s.CurrentPitcher, Core.Game.Instance.League);
        if (card.IsEmpty) return;

        // Slides in and fades out at the end, so it reads as a graphic rather than a pop-up.
        float t = Mathf.Clamp(Broadcast.CardSeconds - _cardLeft, 0f, 1f);
        float alpha = Mathf.Min(1f, _cardLeft / 0.45f) * Mathf.SmoothStep(0f, 1f, t);
        if (alpha <= 0.01f) return;

        Vector2 size = GetViewportRect().Size;
        float w = 330f;
        float h = 62f + card.Lines.Length * 20f;
        var at = new Vector2(40f, size.Y - h - 64f);

        var club = s.BattingTeam?.Team;
        var body = Palette.Panel;
        body.A *= alpha;

        Palette.Panel3D(this, new Rect2(at, new Vector2(w, h)), body);
        DrawRect(new Rect2(at, new Vector2(5f, h)),
            new Color(club?.Secondary ?? Palette.Highlight) { A = alpha });

        var ink = Palette.Ink; ink.A *= alpha;
        var dim = Palette.InkDim; dim.A *= alpha;

        Palette.Text(this, at + new Vector2(16f, 26f), card.Title, 18, ink);
        Palette.Text(this, at + new Vector2(16f, 44f), card.Subtitle, 11, dim);

        float y = 64f;
        foreach (string line in card.Lines)
        {
            Palette.Text(this, at + new Vector2(16f, y), line, 12, ink);
            y += 20f;
        }
    }

    /// <summary>
    /// Where the defence is standing, while you are the one standing them there.
    ///
    /// The same reason as the steal sign: an alignment nobody is told about is an alignment
    /// nobody uses. It prints what the shape costs as well as what it buys, because that is the
    /// decision — every one of these trades range for something.
    /// </summary>
    private void DrawDefencePrompt(Vector2 at, GameSituation s)
    {
        if (!Scene.HumanPitching || Scene.Online) return;
        if (Scene.Phase is not (AtBatPhase.PitchSelect or AtBatPhase.PitchFlight)) return;

        bool set = s.Defence != Core.Alignment.Straight;
        var rect = new Rect2(at + new Vector2(0f, 122f), new Vector2(BugWidth, 26f));
        Palette.Panel3D(this, rect, Palette.Panel);
        Palette.Text(this, rect.Position + new Vector2(10f, 18f),
            $"Y — DEFENCE   {Core.Positioning.Short(s.Defence)}", 13,
            set ? Palette.Highlight : Palette.InkDim);

        // The pen underneath it, with the warm-up drawn as a bar. Whether he is ready is the
        // whole decision, and a number nobody can see is not a decision.
        var pen = new Rect2(rect.Position + new Vector2(0f, 30f), new Vector2(BugWidth, 26f));
        Palette.Panel3D(this, pen, Palette.Panel);

        if (Scene.Pen.Warming == null)
        {
            Palette.Text(this, pen.Position + new Vector2(10f, 18f),
                "U — GET SOMEBODY UP", 13, Palette.InkDim);
            return;
        }

        float ready = Scene.Pen.Readiness;
        var tint = Scene.Pen.IsOverworked ? Palette.Warning
                 : Scene.Pen.IsReady ? Palette.Highlight
                 : Palette.Ink;

        DrawRect(new Rect2(pen.Position + new Vector2(6f, pen.Size.Y - 6f),
            new Vector2((pen.Size.X - 12f) * ready, 3f)), tint);
        Palette.Text(this, pen.Position + new Vector2(10f, 17f), Scene.Pen.Status(), 12, tint);
    }

    private void DrawBaseDiamond(Vector2 center, GameSituation s)
    {
        float r = 13f;
        Vector2[] spots =
        {
            center + new Vector2(r, 0f),      // first
            center + new Vector2(0f, -r),     // second
            center + new Vector2(-r, 0f),     // third
        };

        // The man who can go, if there is one. Stealing has always been bound to G and there has
        // never been anything on screen to say so — you had to already know the rule (a lead
        // runner with the bag ahead of him empty) and then remember the key. A verb the game never
        // mentions is a verb most people never use.
        int lead = Core.Baserunning.LeadRunner(s);

        for (int i = 0; i < 3; i++)
        {
            bool occupied = s.RunnerOn(i + 1);
            bool canGo = Scene.HumanBatting && lead == i + 1 &&
                         Scene.Phase is AtBatPhase.PitchSelect or AtBatPhase.PitchFlight;
            float h = 7f;

            // A pulsing ring round the bag he would be leaving.
            if (canGo)
            {
                float beat = 0.5f + 0.5f * Mathf.Sin(Scene.Situation.Outs * 1f + _pulse * 6f);
                DrawArc(spots[i], 13f + beat * 3f, 0f, Mathf.Tau, 20,
                    new Color(0.45f, 1f, 0.55f, 0.45f + beat * 0.45f), 2.4f);
            }

            var poly = new[]
            {
                spots[i] + new Vector2(0f, -h), spots[i] + new Vector2(h, 0f),
                spots[i] + new Vector2(0f, h), spots[i] + new Vector2(-h, 0f),
            };
            DrawColoredPolygon(poly,
                canGo ? new Color(0.45f, 1f, 0.55f)
                      : occupied ? Palette.Highlight : Palette.PanelLight);
        }

    }

    /// <summary>Drives the steal prompt's pulse.</summary>
    private float _pulse;

    public override void _Process(double delta)
    {
        _pulse += (float)delta;
        QueueRedraw();
    }

    private void DrawLineScore(Vector2 topRight, GameSituation s)
    {
        int innings = Mathf.Max(s.ScheduledInnings, s.AwayLine.Count);
        float cell = 24f;
        float width = 66f + innings * cell + 84f;
        var at = new Vector2(topRight.X - width, topRight.Y);

        var panel = new Rect2(at, new Vector2(width, 74f));
        Palette.Panel3D(this, panel, Palette.Panel);

        float x0 = at.X + 10f;
        Palette.Text(this, new Vector2(x0, at.Y + 20f), "", 13, Palette.InkDim);

        for (int i = 0; i < innings; i++)
            Palette.Text(this, new Vector2(x0 + 60f + i * cell, at.Y + 20f), (i + 1).ToString(), 13, Palette.InkDim);

        float rx = x0 + 60f + innings * cell;
        Palette.Text(this, new Vector2(rx + 6f, at.Y + 20f), "R", 13, Palette.Highlight);
        Palette.Text(this, new Vector2(rx + 30f, at.Y + 20f), "H", 13, Palette.InkDim);
        Palette.Text(this, new Vector2(rx + 54f, at.Y + 20f), "E", 13, Palette.InkDim);

        DrawLineRow(new Vector2(x0, at.Y + 42f), s.Away.Team.Abbrev, s.AwayLine, innings, cell,
            s.AwayScore, s.AwayHits, s.AwayErrors, rx);
        DrawLineRow(new Vector2(x0, at.Y + 62f), s.Home.Team.Abbrev, s.HomeLine, innings, cell,
            s.HomeScore, s.HomeHits, s.HomeErrors, rx);
    }

    private void DrawLineRow(Vector2 at, string abbrev, System.Collections.Generic.List<int> line,
        int innings, float cell, int runs, int hits, int errors, float rx)
    {
        Palette.Text(this, at, abbrev, 14, Palette.Ink);
        for (int i = 0; i < innings; i++)
        {
            string v = i < line.Count ? line[i].ToString() : "·";
            Palette.Text(this, new Vector2(at.X + 60f + i * cell, at.Y), v, 13,
                i < line.Count ? Palette.Ink : Palette.InkDim);
        }
        Palette.Text(this, new Vector2(rx + 6f, at.Y), runs.ToString(), 14, Palette.Highlight);
        Palette.Text(this, new Vector2(rx + 30f, at.Y), hits.ToString(), 13, Palette.InkDim);
        Palette.Text(this, new Vector2(rx + 54f, at.Y), errors.ToString(), 13, Palette.InkDim);
    }

    private void DrawPlayLog(Vector2 at)
    {
        float y = at.Y;
        for (int i = 0; i < Scene.Log.Count; i++)
        {
            float alpha = 0.35f + 0.65f * (i + 1) / Scene.Log.Count;
            Palette.Text(this, new Vector2(at.X, y), Scene.Log[i], 14,
                new Color(Palette.Ink.R, Palette.Ink.G, Palette.Ink.B, alpha));
            y += 17f;
        }
    }

    private void DrawBanner(Vector2 size, string text)
    {
        string[] lines = text.Split('\n');
        int fontSize = lines.Length > 1 ? 34 : 40;

        // Shrink long calls until the box fits on screen, rather than letting it run off the edge.
        float limit = size.X - 140f;
        float maxW;
        while (true)
        {
            maxW = 0f;
            foreach (var l in lines) maxW = Mathf.Max(maxW, Palette.TextWidth(l, fontSize));
            if (maxW <= limit || fontSize <= 18) break;
            fontSize -= 2;
        }

        float lineH = fontSize * 1.25f;
        float boxH = lines.Length * lineH + 32f;

        var box = new Rect2(
            new Vector2(size.X * 0.5f - maxW * 0.5f - 32f, size.Y * 0.38f - boxH * 0.5f),
            new Vector2(maxW + 64f, boxH));

        DrawRect(box, new Color(0f, 0f, 0f, 0.62f));
        DrawRect(box, Palette.Highlight, false, 2f);

        float y = box.Position.Y + 20f + fontSize * 0.5f;
        foreach (var line in lines)
        {
            Palette.TextCentered(this, new Vector2(size.X * 0.5f, y), line, fontSize, Palette.Ink);
            y += lineH;
        }
    }

    /// <summary>
    /// A call that appears without stopping play. Everything short of a ball in play uses this
    /// instead of a blocking banner, so the game never sits still between pitches.
    /// </summary>
    private void DrawToast(Vector2 size)
    {
        float fade = Mathf.Clamp(Scene.ToastTimer / 0.5f, 0f, 1f);
        float w = Palette.TextWidth(Scene.ToastText, 22) + 40f;
        var box = new Rect2(new Vector2(size.X * 0.5f - w * 0.5f, size.Y * 0.155f), new Vector2(w, 36f));

        DrawRect(box, new Color(0f, 0f, 0f, 0.5f * fade));
        Palette.TextCentered(this, box.Position + box.Size * 0.5f, Scene.ToastText, 22,
            new Color(Palette.Ink.R, Palette.Ink.G, Palette.Ink.B, fade));
    }

    /// <summary>
    /// The signature-move charge. Backyard Baseball's specials were something you chose to
    /// spend at a moment that mattered, so it needs to be visible and armable, not silent.
    /// </summary>
    /// <summary>
    /// Trips to the mound left, for the club currently in the field.
    ///
    /// A resource you cannot see is not a resource. Five a game only means anything if you know in
    /// the fourth that spending one now is one you will not have in the eighth.
    /// </summary>
    private void DrawVisitsLeft(Vector2 size, float y)
    {
        bool away = Scene.Situation.FieldingTeam == Scene.Situation.Away;
        int left = Scene.Visit.Left(away);

        var at = new Vector2(size.X * 0.5f - 300f, y - 16f);
        Palette.Text(this, at, "VISITS", 10, Palette.InkDim);

        for (int i = 0; i < MoundVisit.AllowancePerGame; i++)
        {
            var pip = new Rect2(at + new Vector2(i * 10f, 6f), new Vector2(7f, 7f));
            DrawRect(pip, i < left ? Palette.Highlight : Palette.Panel);
        }

        if (left == 0)
            Palette.Text(this, at + new Vector2(0f, 26f), "none left", 9, Palette.Warning);
    }

    /// <summary>
    /// The card a broadcast throws up between innings: who is coming, who is on the mound, and
    /// what he has done so far.
    ///
    /// A televised game never cuts from the third out straight to the next pitch, and neither
    /// should this. It also does real work — the half inning is exactly when a manager wants to
    /// know what the man on the mound has thrown and who is due up, and until now that meant
    /// leaving the game to go and look.
    /// </summary>
    private void DrawBetweenInnings(Vector2 size)
    {
        var s = Scene.Situation;
        var arm = s.FieldingTeam.CurrentPitcher;

        var card = new Rect2(new Vector2(size.X * 0.5f - 300f, size.Y * 0.60f),
            new Vector2(600f, 128f));
        DrawRect(card, new Color(0.04f, 0.06f, 0.09f, 0.88f));
        DrawRect(new Rect2(card.Position, new Vector2(card.Size.X, 3f)), Palette.Highlight);

        Palette.Text(this, card.Position + new Vector2(20f, 30f),
            $"{s.BattingTeam.Team.FullName.ToUpperInvariant()} BATTING", 16, Palette.Highlight);

        // The three due up, which is the thing a viewer actually wants at a half inning.
        var order = s.BattingTeam.BattingOrder;
        if (order.Count > 0)
        {
            string due = "";
            for (int i = 0; i < 3 && i < order.Count; i++)
            {
                var man = order[(s.BattingTeam.LineupSpot + i) % order.Count];
                due += (i > 0 ? "   ·   " : "") + man.ShortName;
            }
            Palette.Text(this, card.Position + new Vector2(20f, 54f), $"DUE UP   {due}", 14,
                Palette.Ink);
        }

        if (arm != null)
        {
            var line = s.Stats.Pitching(arm);
            Palette.Text(this, card.Position + new Vector2(20f, 86f),
                $"ON THE MOUND   {arm.Name}", 14, Palette.Ink);
            Palette.Text(this, card.Position + new Vector2(20f, 108f),
                $"{line.InningsText} IP   {line.Strikeouts} K   {line.Walks} BB   " +
                $"{line.EarnedRuns} ER   {Scene.PitchesThrownBy(arm)} pitches",
                13, Palette.InkDim);
        }

        Palette.Text(this, card.Position + new Vector2(card.Size.X - 150f, 30f),
            $"{s.Away.Team.Abbrev} {s.AwayScore}   {s.Home.Team.Abbrev} {s.HomeScore}",
            18, Palette.Ink);
    }

    private void DrawPowerUpChip(Vector2 size, Data.PlayerData who)
    {
        if (who == null || who.Special == Data.Special.None) return;
        int left = Scene.PowerUps.Remaining(who);

        var rect = new Rect2(new Vector2(size.X * 0.5f - 190f, size.Y - 112f), new Vector2(380f, 30f));
        bool armed = Scene.PowerUpArmed;

        var fill = left == 0 ? Palette.Panel.Darkened(0.4f)
            : armed ? new Color("#f2b231") : Palette.PanelLight;
        Palette.Panel3D(this, rect, fill);

        string label = left == 0
            ? $"{who.SpecialText} — used up"
            : armed
                ? $"{who.SpecialText.ToUpperInvariant()} READY  —  swing to use it"
                : $"SHIFT to use {who.SpecialText}   ({left} left)";

        Palette.TextCentered(this, rect.Position + rect.Size * 0.5f, label, 14,
            armed ? Palette.Night : left == 0 ? Palette.InkDim : Palette.Ink);

        if (left > 0) Clicks.Add(rect, Scene.TogglePowerUp);
    }

    /// <summary>The pitch clock. It runs whether or not the player acts.</summary>
    private void DrawPitchClock(Vector2 size)
    {
        float left = Mathf.Max(0f, Scene.PitchClock);
        bool urgent = left <= 3f;

        var at = new Vector2(size.X * 0.5f, size.Y * 0.115f);
        var tint = urgent ? Palette.Warning : Palette.Ink;

        Palette.TextCentered(this, at, Mathf.CeilToInt(left).ToString(), urgent ? 34 : 28, tint);

        // A ring that drains as the clock runs down.
        float frac = Mathf.Clamp(left / Scene.PitchClockSeconds, 0f, 1f);
        DrawArc(at + new Vector2(0f, -8f), 26f, -Mathf.Pi * 0.5f,
            -Mathf.Pi * 0.5f + Mathf.Tau * frac, 32, tint, 3f);
    }

    /// <summary>
    /// The post-swing readout: how the timing was, and what came off the bat. This is what
    /// makes the hitting learnable — without it a miss tells you nothing about why.
    /// </summary>
    private void DrawSwingFeedback(Vector2 size)
    {
        var ball = Scene.LastSwing;
        float fade = Mathf.Clamp(Scene.SwingFeedbackTimer / 0.5f, 0f, 1f);

        // Tucked under the line score on the right, clear of the pitcher's name plate and the
        // strike zone, both of which sit around the centre of the frame.
        // Wider than it was: the detail line now carries the placement as well as the contact,
        // and "Got it off the end of the bat · under it, inside" does not fit in three hundred.
        var box = new Rect2(new Vector2(size.X - 420f, 104f), new Vector2(380f, 62f));
        DrawRect(box, new Color(0f, 0f, 0f, 0.55f * fade));

        // Green when squared up, amber when mistimed, red on a whiff.
        var tint = Scene.LastSwingResult == SwingResult.Miss
            ? new Color(0.88f, 0.35f, 0.32f)
            : Mathf.Abs(ball.TimingNorm) <= 0.12f
                ? new Color(0.48f, 0.86f, 0.52f)
                : new Color(0.96f, 0.78f, 0.36f);
        tint.A = fade;

        DrawRect(new Rect2(box.Position, new Vector2(4f, box.Size.Y)), tint);

        Palette.TextCentered(this, box.Position + new Vector2(box.Size.X * 0.5f, 24f),
            ball.TimingVerdict, 19, tint);

        // Backyard Baseball never showed you exit velocity and launch angle. It told you whether
        // you got hold of one, which is the part a player actually wants to know.
        float mph = ball.ExitVelocity / 1.46667f;
        string detail = Scene.LastSwingResult switch
        {
            SwingResult.Miss => "Swung right through it",
            SwingResult.Foul => "Just got a piece of it",
            _ => mph switch
            {
                > 103f => "CRUSHED IT!",
                > 95f => "Really got hold of that one",
                > 85f => "Solid contact",
                > 74f => "Got it off the end of the bat",
                _ => "Just a nubber",
            },
        };

        // Timing was on screen and placement was not, so half of every swing was invisible: a
        // hitter told he was late still had no idea he was also under it by a foot. Both axes are
        // reported now, which is what makes the swing learnable rather than mysterious.
        string where = ball.PlacementVerdict;
        Palette.TextCentered(this, box.Position + new Vector2(box.Size.X * 0.5f, 46f),
            where == "on it" ? detail : $"{detail}  ·  {where}", 14,
            new Color(Palette.Ink.R, Palette.Ink.G, Palette.Ink.B, fade * 0.9f));

        // A little timing bar: centre is perfect, left is early, right is late.
        var bar = new Rect2(box.Position + new Vector2(60f, 54f), new Vector2(180f, 4f));
        DrawRect(bar, new Color(1f, 1f, 1f, 0.20f * fade));
        float x = bar.Position.X + bar.Size.X * 0.5f * (1f + Mathf.Clamp(ball.TimingNorm, -1f, 1f));
        DrawRect(new Rect2(new Vector2(x - 2f, bar.Position.Y - 3f), new Vector2(4f, 10f)), tint);
    }

    private void DrawPitchPicker(Vector2 size)
    {
        var arm = Scene.Situation.FieldingTeam.CurrentPitcher;

        // Only what this man throws, rather than four fixed slots with the ones he lacks greyed
        // out. Pitchers now carry between two and four pitches out of eight, so a fixed row would
        // be mostly dead buttons — and the number keys should land on his pitches, not on gaps.
        var types = arm.Arsenal.ToArray();
        if (types.Length == 0) types = new[] { PitchType.Fastball };

        float w = types.Length >= 5 ? 108f : 132f;
        float x = size.X * 0.5f - (w * types.Length + 8f * (types.Length - 1)) * 0.5f;
        float y = size.Y - 66f;

        for (int i = 0; i < types.Length; i++)
        {
            bool on = types[i] == Scene.SelectedPitch;
            var rect = new Rect2(new Vector2(x + i * (w + 8f), y), new Vector2(w, 34f));

            Palette.Panel3D(this, rect, on ? Palette.Highlight.Darkened(0.2f) : Palette.Panel);
            Palette.TextCentered(this, rect.Position + rect.Size * 0.5f,
                $"{i + 1} {SwingProfileNames.Of(types[i])}", types.Length >= 5 ? 13 : 15,
                on ? Palette.Night : Palette.Ink);

            if (i == 0) DrawVisitsLeft(size, y);

            var picked = types[i];
            Clicks.Add(rect, () =>
            {
                if (Scene.SelectedPitch == picked) Scene.DealNow();
                else Scene.SelectedPitch = picked;
            });
        }

        DrawPowerUpChip(size, arm);

        Palette.TextCentered(this, new Vector2(size.X * 0.5f, y - 14f),
            "Click a pitch to pick it, click again to deal  ·  or aim with the mouse and click the field",
            14, Palette.InkDim);
    }
}
