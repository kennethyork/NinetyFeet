using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Gameplay;

/// <summary>What stage a trip to the mound has reached.</summary>
public enum VisitStage { None, WalkingOut, Talking, WalkingBack }

/// <summary>
/// A trip to the mound: the manager coming out to settle his pitcher, or to take the ball off him.
///
/// This is the one piece of baseball that is entirely presentation and entirely essential. A
/// pitching change that happens between two frames is a number changing; a pitching change where
/// the manager walks out, takes the ball, waits for the bullpen gate and pats the man on the back
/// on his way off is the moment everyone actually pictures. It costs nothing in simulation terms
/// and it is most of what makes the game look like baseball rather than like a spreadsheet with
/// sprites.
///
/// The visit itself is also a real rule and a real decision. A club gets five a game; the sixth
/// forces the change whether the manager wanted one or not, which is exactly the kind of resource
/// worth spending carefully. And a visit genuinely settles a pitcher — it buys a hitter or two of
/// steadier command — so calling one is a choice rather than a cutscene.
/// </summary>
public sealed class MoundVisit
{
    /// <summary>Trips allowed per club per game, as in the real rule.</summary>
    public const int AllowancePerGame = 5;

    public VisitStage Stage { get; private set; } = VisitStage.None;

    /// <summary>Whether this trip ends with the pitcher being taken out.</summary>
    public bool IsChange { get; private set; }

    /// <summary>Who is walking, and to where — the renderer reads these directly.</summary>
    public float Progress { get; private set; }

    /// <summary>The man coming in, once he is known. Null for a settling visit.</summary>
    public PlayerData Incoming { get; private set; }
    public PlayerData Outgoing { get; private set; }

    /// <summary>Trips each club has used.</summary>
    public int AwayUsed { get; private set; }
    public int HomeUsed { get; private set; }

    private bool _awayIsVisiting;
    private float _timer;

    private const float WalkOut = 1.35f;
    private const float TalkFor = 1.15f;
    private const float ChangeTalkFor = 2.10f;   // a change takes longer: the ball changes hands
    private const float WalkBack = 1.20f;

    public bool Busy => Stage != VisitStage.None;

    /// <summary>How many trips this club has left.</summary>
    public int Left(bool away) => Mathf.Max(0, AllowancePerGame - (away ? AwayUsed : HomeUsed));

    /// <summary>
    /// True when the club is out of trips, so the next one has to be a change. The real rule, and
    /// the reason a manager holds one back for the eighth.
    /// </summary>
    public bool MustChange(bool away) => Left(away) <= 0;

    public void Reset()
    {
        Stage = VisitStage.None;
        AwayUsed = HomeUsed = 0;
        Incoming = Outgoing = null;
        _timer = 0f;
        Progress = 0f;
    }

    /// <summary>
    /// Starts a trip. A settling visit spends one of the five; a change does not, because taking
    /// the pitcher out is not a visit under the rule.
    /// </summary>
    public void Begin(bool awayIsFielding, bool change, PlayerData outgoing, PlayerData incoming)
    {
        Stage = VisitStage.WalkingOut;
        _awayIsVisiting = awayIsFielding;
        IsChange = change;
        Outgoing = outgoing;
        Incoming = incoming;
        _timer = 0f;
        Progress = 0f;

        if (change) return;
        if (awayIsFielding) AwayUsed++; else HomeUsed++;
    }

    /// <summary>Steps the trip. Returns true on the frame it finishes.</summary>
    public bool Update(float dt)
    {
        if (Stage == VisitStage.None) return false;

        _timer += dt;
        float talk = IsChange ? ChangeTalkFor : TalkFor;

        switch (Stage)
        {
            case VisitStage.WalkingOut:
                Progress = Mathf.Clamp(_timer / WalkOut, 0f, 1f);
                if (_timer >= WalkOut) { Stage = VisitStage.Talking; _timer = 0f; }
                return false;

            case VisitStage.Talking:
                Progress = 1f;
                if (_timer >= talk) { Stage = VisitStage.WalkingBack; _timer = 0f; }
                return false;

            default:
                Progress = 1f - Mathf.Clamp(_timer / WalkBack, 0f, 1f);
                if (_timer < WalkBack) return false;

                Stage = VisitStage.None;
                Progress = 0f;
                return true;
        }
    }

    /// <summary>Which dugout the manager came out of, for the renderer.</summary>
    public bool FromAwayDugout => _awayIsVisiting;

    /// <summary>What the banner says while he is out there.</summary>
    public string Caption => Stage switch
    {
        VisitStage.None => "",
        VisitStage.WalkingBack when IsChange =>
            Incoming == null ? "" : $"Now pitching: {Incoming.Name}",
        _ => IsChange
            ? $"The manager takes the ball from {Outgoing?.ShortName}."
            : $"Mound visit — a word with {Outgoing?.ShortName}.",
    };
}
