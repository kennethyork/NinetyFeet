using Godot;

namespace SandlotSlugfest.Gameplay;

/// <summary>
/// Decides when to roll a replay, and works the camera while it runs.
///
/// A broadcast does not replay everything and it does not replay anything immediately — it lets
/// the moment land, then comes back to it. Both of those matter more than the camera work: a
/// replay that interrupts the call is worse than no replay, and one that shows a routine ground
/// ball teaches the viewer to stop looking.
///
/// The camera pushes in and follows the ball with a lag, so it drifts behind a hard-hit ball and
/// catches up — which is what a real camera operator does and what makes a long fly look long.
/// </summary>
public sealed class ReplayDirector
{
    public readonly ReplayTape Tape = new();

    public bool Running { get; private set; }

    /// <summary>Which sample is on screen.</summary>
    public int FrameIndex { get; private set; }

    /// <summary>How far in the camera is pushed, and where it is pointed, in field feet.</summary>
    public float Zoom { get; private set; } = 1f;
    public Vector2 CameraTarget { get; private set; }

    /// <summary>Replays run slower than life. That is most of what makes them readable.</summary>
    private const float PlaybackRate = 0.55f;

    /// <summary>How far in the camera pushes.</summary>
    private const float CloseUp = 1.55f;

    private float _cursor;
    private float _holdIn;
    private float _holdOut;

    /// <summary>Seconds of black-in and black-out either side, so it does not snap.</summary>
    private const float LeadIn = 0.45f;
    private const float LeadOut = 0.85f;

    /// <summary>Set while the replay is fading in or out, for the view to dim on.</summary>
    public float Fade { get; private set; }

    public void Start(string caption)
    {
        if (!Tape.HasFootage) return;

        Tape.Caption = caption;
        Running = true;
        FrameIndex = 0;
        _cursor = 0f;
        _holdIn = LeadIn;
        _holdOut = 0f;
        Zoom = 1f;
        CameraTarget = Tape.At(0).Ball;
        Fade = 1f;
    }

    public void Stop()
    {
        Running = false;
        Fade = 0f;
    }

    /// <summary>Steps the replay. Returns true when it has finished on its own.</summary>
    public bool Update(float dt)
    {
        if (!Running) return false;

        // A beat before it starts, so the live call is not cut off mid-sentence.
        if (_holdIn > 0f)
        {
            _holdIn -= dt;
            Fade = Mathf.Clamp(_holdIn / LeadIn, 0f, 1f);
            return false;
        }

        Fade = 0f;
        _cursor += dt * ReplayTape.SampleHz * PlaybackRate;
        FrameIndex = Mathf.FloorToInt(_cursor);

        // Push in over the first half-second and follow the ball with a lag.
        Zoom = Mathf.Lerp(Zoom, CloseUp, dt * 3.2f);
        var frame = Tape.At(FrameIndex);
        CameraTarget = CameraTarget.Lerp(frame.Ball, dt * 2.6f);

        if (FrameIndex < Tape.Count - 1) return false;

        // Hold on the last frame for a moment rather than cutting the instant it ends.
        _holdOut += dt;
        Fade = Mathf.Clamp(_holdOut / LeadOut, 0f, 1f);
        if (_holdOut < LeadOut) return false;

        Stop();
        return true;
    }
}
