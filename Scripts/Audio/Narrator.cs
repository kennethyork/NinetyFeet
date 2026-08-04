using Godot;
using SandlotSlugfest.Core;
using SandlotSlugfest.Data;

namespace SandlotSlugfest.Audio;

/// <summary>Named VoiceLine, not Call — `Call` shadows GodotObject.Call inside any Node.</summary>
public enum VoiceLine
{
    CalledStrike,
    SwingMiss,
    Strikeout,
    Ball,
    Walk,
    Foul,
    Single,
    Double,
    Triple,
    Homer,
    FlyOut,
    GroundOut,
    DoublePlay,
    Safe,
    NiceCatch,
    SideRetired,
    PlayBall,
    Rally,
}

/// <summary>The analyst's lines. He reacts to the situation rather than calling the action.</summary>
public enum ColourLine
{
    CFirstPitch,
    CStrikeout,
    CHomer,
    CWalk,
    CScoringPos,
    CTwoOuts,
    CLateClose,
    CBigLead,
    CPitcherGood,
    CPitcherTired,
    CNiceDefense,
    CRally,
    CInningEnd,
    CGoodAtBat,
    CFoulTrouble,
}

/// <summary>
/// Play-by-play. Each of the 32 home ballparks has its own announcer, so the voice changes with
/// the venue the way a local broadcast crew would.
///
/// The lines were rendered offline with Piper (MIT licence) using the multi-speaker LibriTTS-R
/// model — all 32 voices come from one model, picked for measurably different pitch so
/// neighbouring clubs do not sound alike. They are deliberately name-free: players are generated
/// procedurally, so there is no fixed name list that could ever have been pre-rendered.
/// </summary>
public partial class Narrator : Node
{
    public static Narrator Instance { get; private set; }

    private AudioStreamPlayer _voice;
    private AudioStreamPlayer _colour;
    private AudioStreamPlayer _name;

    /// <summary>A player name waiting to be spoken once the call it belongs to has finished.</summary>
    private readonly System.Collections.Generic.Queue<string> _nameQueue = new();
    private float _nameGap;
    private int _homeTeam;

    /// <summary>A colour line waiting for the play-by-play man to finish.</summary>
    private ColourLine? _pending;
    private float _pendingDelay;

    /// <summary>
    /// Stops the analyst from filling every gap. A booth that never draws breath is worse than
    /// one that says nothing, so most opportunities are simply passed over.
    /// </summary>
    private float _colourCooldown;

    private Rng _rng = new(0xC010);

    /// <summary>
    /// How long a call is suppressed after a more important one. Without this the announcer
    /// talks over himself on a busy play and every line is clipped to a syllable.
    /// </summary>
    private float _busyFor;

    private int _lastPriority;

    public bool Enabled { get; private set; } = true;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;

        _voice = new AudioStreamPlayer { Bus = "Master" };
        AddChild(_voice);

        _colour = new AudioStreamPlayer { Bus = "Master" };
        AddChild(_colour);

        _name = new AudioStreamPlayer { Bus = "Master" };
        AddChild(_name);

        var cfg = new ConfigFile();
        if (cfg.Load("user://settings.cfg") == Error.Ok)
            Enabled = (bool)cfg.GetValue("audio", "narration", true);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        if (_busyFor > 0f) _busyFor -= dt;
        else _lastPriority = 0;

        if (_colourCooldown > 0f) _colourCooldown -= dt;

        // Names are spoken after the call they belong to — "Base hit!" then "Villanueva" — which
        // is how a booth actually does it, and means one clip per name serves every player.
        if (_nameQueue.Count > 0 && !_voice.Playing && !_name.Playing)
        {
            _nameGap -= dt;
            if (_nameGap <= 0f)
            {
                var clip = GD.Load<AudioStream>($"res://Audio/vo/names/{_nameQueue.Dequeue()}.ogg");
                if (clip != null)
                {
                    _name.Stream = clip;
                    _name.VolumeDb = Mathf.LinearToDb(
                        Mathf.Clamp(0.95f * (Sfx.Instance?.Volume ?? 0.7f), 0.0001f, 2f));
                    _name.Play();
                }
                _nameGap = 0.10f;
            }
        }

        // The analyst waits for a gap: the play-by-play call has to finish first, then a beat.
        if (_pending.HasValue)
        {
            if (_busyFor > 0f || _voice.Playing) return;

            _pendingDelay -= dt;
            if (_pendingDelay > 0f) return;

            var stream = GD.Load<AudioStream>($"res://Audio/vo/t{_homeTeam:00}/{_pending.Value}.ogg");
            _pending = null;
            if (stream == null) return;

            _colour.Stream = stream;
            _colour.VolumeDb = Mathf.LinearToDb(
                Mathf.Clamp(0.85f * (Sfx.Instance?.Volume ?? 0.7f), 0.0001f, 2f));
            _colour.Play();
            _colourCooldown = 11f + _rng.NextFloat() * 9f;
        }
    }

    /// <summary>
    /// Queues a player's name to follow the current call.
    ///
    /// Players are generated from a name pool, so a whole-name recording was never possible — but
    /// the pool is finite, so every first and last name is rendered once and the booth says the
    /// player by playing them back to back.
    /// </summary>
    public void SayName(PlayerData player, bool full = false)
    {
        if (!Enabled || player == null) return;
        if (Sfx.Instance is { Muted: true }) return;
        if (_nameQueue.Count > 2) return;          // never let names pile up behind the action

        if (full && !string.IsNullOrEmpty(player.FirstName)) _nameQueue.Enqueue(Safe(player.FirstName));
        if (!string.IsNullOrEmpty(player.LastName)) _nameQueue.Enqueue(Safe(player.LastName));
    }

    /// <summary>Clip filenames strip anything that is not a letter or digit.</summary>
    private static string Safe(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name) if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>
    /// Offers the analyst a line. He takes it only if he has been quiet for a while and nothing
    /// is already queued — most offers are declined on purpose.
    /// </summary>
    public void Colour(ColourLine line, float chance = 0.55f)
    {
        if (!Enabled || _colourCooldown > 0f || _pending.HasValue) return;
        if (Sfx.Instance is { Muted: true }) return;
        if (_colour.Playing) return;
        if (_rng.NextFloat() > chance) return;

        _pending = line;
        _pendingDelay = 0.45f;
    }

    /// <summary>N toggles the booth, the same way M mutes everything.</summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.PhysicalKeycode != Key.N) return;

        SetEnabled(!Enabled);
        Sfx.Instance?.Play(Sound.UiSelect, 0.6f);
        GetViewport().SetInputAsHandled();
    }

    public void SetEnabled(bool on)
    {
        Enabled = on;
        if (!on)
        {
            if (_voice != null && _voice.Playing) _voice.Stop();
            if (_colour != null && _colour.Playing) _colour.Stop();
            if (_name != null && _name.Playing) _name.Stop();
            _pending = null;
            _nameQueue.Clear();
        }

        var cfg = new ConfigFile();
        cfg.Load("user://settings.cfg");
        cfg.SetValue("audio", "narration", on);
        cfg.Save("user://settings.cfg");
    }

    /// <summary>Chooses the announcer for this ballpark. Call when a game starts.</summary>
    public void SetHomeTeam(int teamId)
    {
        _homeTeam = Mathf.Clamp(teamId, 0, 31);
        _busyFor = 0f;
        _lastPriority = 0;
        _pending = null;
        _colourCooldown = 0f;
        _nameQueue.Clear();
    }

    /// <summary>
    /// Makes a call. A higher-priority call interrupts a lower one; a lower one is dropped while
    /// the announcer is still busy, so the big moment is always the line you actually hear.
    /// </summary>
    public void Say(VoiceLine line, int priority = 1)
    {
        if (!Enabled) return;
        if (Sfx.Instance is { Muted: true }) return;

        if (_busyFor > 0f && priority <= _lastPriority) return;

        var stream = GD.Load<AudioStream>($"res://Audio/vo/t{_homeTeam:00}/{line}.ogg");
        if (stream == null) return;

        _voice.Stream = stream;
        _voice.VolumeDb = Mathf.LinearToDb(Mathf.Clamp(0.95f * (Sfx.Instance?.Volume ?? 0.7f), 0.0001f, 2f));
        _voice.Play();

        _busyFor = (float)stream.GetLength();
        _lastPriority = priority;
    }
}
