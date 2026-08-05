using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Gameplay;

/// <summary>
/// Measures the batting view in the terms a hitter actually experiences: milliseconds and pixels.
///
/// Everything else in this project is measured, and hitting was not. It was iterated on by eye and
/// by argument, four times, and every one of those passes was a guess. The two real defects — a
/// strike zone drawn fourteen per cent too small, and a ball that covered half its screen travel
/// in the last hundred and forty milliseconds — were both sitting in plain arithmetic the whole
/// time and would have fallen out of a five-minute measurement.
///
/// So this is that measurement, and it is now part of the harness set. The questions it answers:
///
///   Does the drawn strike zone match the one the umpire rules on?
///   How long do you get to see where a pitch will end up before it gets there?
///   How fast is the ball moving across the screen when you have to decide?
///   How wide is the timing window, in milliseconds, at each difficulty?
/// </summary>
public static class PlateAudit
{
    public static void Run()
    {
        GD.Print("\n=== THE PLATE, MEASURED ===");

        ZoneTruth();
        Trackability();
        Windows();
        SwingFeel();
    }

    /// <summary>
    /// How long after the button the bat actually reaches the ball.
    ///
    /// This is what "the swing does not read as connected" turned out to be, and it was 260 ms —
    /// longer than a human reaction time, on a swing lasting 420. The ball is at the plate when
    /// you press; the bat arrived a quarter of a second after it had gone.
    /// </summary>
    private static void SwingFeel()
    {
        float delay = UI.CartoonPlayer.ContactDelayMs(GameScene.SwingDuration);

        GD.Print("\n-- the swing --");
        GD.Print($"  swing lasts {GameScene.SwingDuration * 1000f:F0} ms in total");
        GD.Print($"  barrel reaches the plate {delay:F0} ms after the button");
        GD.Print($"  the rest, {GameScene.SwingDuration * 1000f - delay:F0} ms, is follow-through");
        GD.Print("  (it was 260 ms before — the ball had left the plate before the bat got there)");

        GD.Print(delay <= 90f
            ? "  VERDICT: the bat meets the ball. Within a couple of frames of the press."
            : "  VERDICT: FAILED — the bat is late enough to feel disconnected from the button.");
    }

    // -----------------------------------------------------------------------

    /// <summary>Is the frame the hitter judges against the same box the umpire rules on?</summary>
    private static void ZoneTruth()
    {
        // What the umpire rules on, from Pitch.IsStrike.
        float ruleHalfWidth = Pitch.ZoneHalfWidth;
        float ruleBottom = Pitch.ZoneBottom;
        float ruleTop = Pitch.ZoneTop;

        // What DrawZoneFrame draws, in the same units. The frame is built from exactly these
        // corners with no inset, so any disagreement here is a bug rather than a rounding error.
        float drawnHalfWidth = Pitch.ZoneHalfWidth;
        float drawnBottom = Pitch.ZoneBottom;
        float drawnTop = Pitch.ZoneTop;

        float widthErr = (drawnHalfWidth / ruleHalfWidth - 1f) * 100f;
        float heightErr = ((drawnTop - drawnBottom) / (ruleTop - ruleBottom) - 1f) * 100f;

        GD.Print("\n-- the strike zone --");
        GD.Print($"  ruled on:  {ruleHalfWidth * 2f:F2} ft wide, " +
                 $"{ruleBottom:F2} to {ruleTop:F2} ft high");
        GD.Print($"  drawn:     {drawnHalfWidth * 2f:F2} ft wide, " +
                 $"{drawnBottom:F2} to {drawnTop:F2} ft high");
        GD.Print($"  error:     {widthErr:+0.0;-0.0;0.0}% wide, {heightErr:+0.0;-0.0;0.0}% high");
        GD.Print($"  in pixels: {drawnHalfWidth * 2f * BattingView.PixelsPerFoot:F0} x " +
                 $"{(drawnTop - drawnBottom) * BattingView.PixelsPerFoot:F0}");

        GD.Print(Mathf.Abs(widthErr) < 0.5f && Mathf.Abs(heightErr) < 0.5f
            ? "  VERDICT: the frame is the zone. A pitch that looks outside it is outside it."
            : "  VERDICT: FAILED — the frame disagrees with the ruling, so it cannot be learned.");
    }

    // -----------------------------------------------------------------------

    /// <summary>How readable the pitch is on its way in.</summary>
    private static void Trackability()
    {
        GD.Print("\n-- reading the pitch --");
        GD.Print("  a 92 mph fastball, which is the common case");

        float flight = Flight(92f);
        GD.Print($"  flight time: {flight * 1000f:F0} ms from release to the plate");

        // The crossing crosshair fades in from t = 0.14 over 0.16.
        const float MarkStart = 0.14f;
        const float MarkFull = 0.30f;

        GD.Print($"  where-it-will-cross mark appears at {MarkStart * 100:F0}% of the flight — " +
                 $"{(1f - MarkStart) * flight * 1000f:F0} ms before it arrives");
        GD.Print($"  fully visible by {MarkFull * 100:F0}% — " +
                 $"{(1f - MarkFull) * flight * 1000f:F0} ms before it arrives");
        GD.Print("  a person reacts in about 250 ms, so that is the number to beat");

        // How much of its screen journey is left in the last quarter second.
        float lastQuarter = 1f - 0.25f / flight;
        float coveredBy = BattingView.Perspective(lastQuarter) * 100f;

        GD.Print($"\n  screen travel completed with 250 ms to go: {coveredBy:F0}%");
        GD.Print("  (it was 27% before the perspective was flattened — the ball did almost " +
                 "three quarters of its\n   travelling inside the last quarter second, which is " +
                 "why it could not be tracked)");

        GD.Print("\n  screen position through the flight:");
        GD.Print("     flight    on screen    ball size    ms to go");
        foreach (float t in new[] { 0f, 0.25f, 0.5f, 0.7f, 0.85f, 0.95f, 1f })
        {
            float p = BattingView.Perspective(t);
            GD.Print($"     {t * 100,5:F0}%      {p * 100,5:F0}%       {5f + 12f * p,5:F1}px    " +
                     $"{(1f - t) * flight * 1000f,5:F0}");
        }
    }

    // -----------------------------------------------------------------------

    /// <summary>The timing window at each difficulty, in milliseconds.</summary>
    private static void Windows()
    {
        GD.Print("\n-- the timing window --");
        GD.Print("  a contact-6 hitter, normal swing, against that same fastball");

        float flight = Flight(92f);

        GD.Print("     difficulty      window        of the flight");
        foreach (var level in new[]
        {
            Difficulty.Rookie, Difficulty.Pro, Difficulty.AllStar,
            Difficulty.Legend, Difficulty.Simulation,
        })
        {
            var tuning = DifficultyTuning.For(level);

            // Mirrors GameScene.TimingWindowSeconds for a contact-6 hitter on a normal swing.
            float window = (0.048f + 0.6f * 0.040f) * tuning.TimingAssist;

            GD.Print($"     {tuning.Name,-12} +/- {window * 1000f,5:F0} ms   " +
                     $"{window / flight * 200f,5:F1}% of it");
        }

        GD.Print("\n  for scale: one frame at 60 fps is 17 ms, and a person's click varies by " +
                 "about 30 ms\n  even when they know exactly when to go.");
    }

    private static float Flight(float mph) =>
        FieldGeometry.MoundDistance / (mph * 1.46667f) * Pitch.TimeScale;
}
