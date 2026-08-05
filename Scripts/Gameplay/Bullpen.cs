using System.Collections.Generic;
using Godot;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Gameplay;

/// <summary>
/// The pen, and the fact that a reliever cannot simply appear.
///
/// A pitching change was instant: the manager walked out, took the ball, and whoever you named
/// was on the mound at his best. That removes the one decision the bullpen is actually about —
/// you have to guess an inning early who you are going to need, and getting a man up is a
/// commitment. A closer who warmed in the seventh and did not come in has thrown thirty pitches
/// for nothing and is worse in the ninth.
///
/// Nothing here is punitive. A man who has had his time is exactly as good as he was before;
/// only the one rushed in cold is diminished, and only for as long as it takes him to find it.
/// </summary>
public sealed class Bullpen
{
    /// <summary>Seconds of game time before he is loose. About the length of a half inning.</summary>
    public const float ReadyAfter = 70f;

    /// <summary>Past this he is standing around throwing for nothing, and it starts to cost him.</summary>
    public const float OverworkedAfter = 190f;

    /// <summary>Who is up, and how long he has been throwing.</summary>
    public PlayerData Warming { get; private set; }

    private float _warmed;

    /// <summary>How much he has thrown in the pen today, whether or not he ever came in.</summary>
    private readonly Dictionary<PlayerData, float> _spent = new();

    /// <summary>
    /// How cold each man was when he took the ball, as a share of a full warm-up still missing.
    /// Decays as he throws — it is a man finding his release point, not a permanent penalty.
    /// </summary>
    private readonly Dictionary<PlayerData, float> _cold = new();

    public float Readiness => Warming == null ? 0f : Mathf.Clamp(_warmed / ReadyAfter, 0f, 1f);
    public bool IsReady => Warming != null && _warmed >= ReadyAfter;

    /// <summary>True once he has been up so long that bringing him in is no longer free.</summary>
    public bool IsOverworked => Warming != null && _warmed > OverworkedAfter;

    public void Reset()
    {
        Warming = null;
        _warmed = 0f;
        _spent.Clear();
        _cold.Clear();
    }

    /// <summary>Gets a man up. Naming somebody else sits the first one back down.</summary>
    public void StartWarming(PlayerData arm)
    {
        if (arm == null || arm == Warming) return;

        // Whoever was up keeps what he has already thrown against him.
        Bank();

        Warming = arm;

        // A man who was up earlier in the game is loose faster the second time.
        _spent.TryGetValue(arm, out float already);
        _warmed = Mathf.Min(already * 0.5f, ReadyAfter * 0.6f);
    }

    /// <summary>Sits everybody down without bringing anyone in.</summary>
    public void SitDown()
    {
        Bank();
        Warming = null;
        _warmed = 0f;
    }

    private void Bank()
    {
        if (Warming == null) return;
        _spent.TryGetValue(Warming, out float had);
        _spent[Warming] = had + _warmed;
    }

    public void Update(float dt)
    {
        if (Warming != null) _warmed += dt;
    }

    /// <summary>
    /// Takes the ball off him. Whatever warm-up he was short of is carried in as coldness, and a
    /// man who has been up far too long carries a little of that instead.
    /// </summary>
    public void BringIn(PlayerData arm)
    {
        if (arm == null) return;

        float missing = arm == Warming
            ? Mathf.Clamp(1f - _warmed / ReadyAfter, 0f, 1f)
            : 1f;                                   // never got up at all

        // Standing out there throwing for two innings is its own cost.
        if (arm == Warming && _warmed > OverworkedAfter)
            missing = Mathf.Max(missing,
                Mathf.Min(0.5f, (_warmed - OverworkedAfter) / (OverworkedAfter * 2f)));

        _cold[arm] = missing;
        Bank();
        Warming = null;
        _warmed = 0f;
    }

    /// <summary>
    /// What being rushed does to this pitch, on the same scale the fatigue model already uses.
    /// He finds it over roughly ten pitches, which is about how long it takes.
    /// </summary>
    public float Coldness(PlayerData arm, int pitchesThrownSinceEntering)
    {
        if (arm == null || !_cold.TryGetValue(arm, out float cold) || cold <= 0f) return 0f;

        float settling = Mathf.Clamp(1f - pitchesThrownSinceEntering / 10f, 0f, 1f);
        return cold * settling * 0.35f;
    }

    /// <summary>What the bench would say about the man currently up.</summary>
    public string Status()
    {
        if (Warming == null) return "Nobody up";
        if (IsOverworked) return $"{Warming.ShortName} is up too long";
        if (IsReady) return $"{Warming.ShortName} is ready";
        return $"{Warming.ShortName} getting loose — {Readiness * 100f:F0}%";
    }
}
