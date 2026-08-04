using System.Collections.Generic;
using Godot;
using SandlotSlugfest.Core;

namespace SandlotSlugfest.Audio;

public enum Sound
{
    Crack,        // barrelled it
    Contact,      // ordinary contact
    Foul,         // off the end, dull
    Whiff,        // bat through empty air
    MittPop,      // caught by the catcher
    GloveCatch,   // fielded
    Bunt,
    CrowdCheer,
    CrowdGroan,
    Homer,
    Out,
    Walk,
    UiMove,
    UiSelect,
    UiBack,
}

/// <summary>
/// The sound bank, plus a small pool of players so overlapping effects do not cut each other off.
///
/// Everything is synthesised on first use (see <see cref="Voice"/>) — there are no audio files in
/// this project. Sounds are built lazily so start-up does not pay for effects a screen never uses.
/// </summary>
public partial class Sfx : Node
{
    public static Sfx Instance { get; private set; }

    private const int Voices = 12;

    // Three takes of each effect rather than one. A single sample replayed at slightly different
    // speeds still reads as the same sample; three genuinely different renders of the same recipe
    // stop repeated contact sounding like a machine.
    private const int Takes = 3;

    private readonly Dictionary<Sound, AudioStreamWav[]> _bank = new();
    private Rng _pick = new(0x5EED);
    private readonly List<AudioStreamPlayer> _players = new();
    private AudioStreamPlayer _crowdBed;
    private int _next;

    /// <summary>Master volume, 0 to 1. Persisted alongside the other settings.</summary>
    public float Volume { get; private set; } = 0.7f;

    public bool Muted { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;

        for (int i = 0; i < Voices; i++)
        {
            var p = new AudioStreamPlayer { Bus = "Master" };
            AddChild(p);
            _players.Add(p);
        }

        _crowdBed = new AudioStreamPlayer { Bus = "Master", VolumeDb = -26f };
        AddChild(_crowdBed);

        LoadPrefs();
    }

    /// <summary>M mutes from anywhere. Handled here so every screen gets it for free.</summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        if (key.PhysicalKeycode == Key.M)
        {
            ToggleMute();
            if (!Muted) Play(Sound.UiSelect, 0.6f);
            GetViewport().SetInputAsHandled();
        }
        else if (key.PhysicalKeycode is Key.Minus or Key.Equal)
        {
            SetVolume(Volume + (key.PhysicalKeycode == Key.Equal ? 0.1f : -0.1f));
            Play(Sound.UiMove, 0.7f);
            GetViewport().SetInputAsHandled();
        }
    }

    // -----------------------------------------------------------------------
    // Settings
    // -----------------------------------------------------------------------

    private const string Path = "user://settings.cfg";

    private void LoadPrefs()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;
        Volume = (float)cfg.GetValue("audio", "volume", 0.7f);
        Muted = (bool)cfg.GetValue("audio", "muted", false);
    }

    public void SetVolume(float v)
    {
        Volume = Mathf.Clamp(v, 0f, 1f);
        Save();
    }

    public void ToggleMute()
    {
        Muted = !Muted;
        if (Muted) { StopCrowd(); Music.Instance?.Stop(); }
        else Music.Instance?.SetEnabled(Music.Instance.Enabled);
        Save();
    }

    private void Save()
    {
        var cfg = new ConfigFile();
        cfg.Load(Path);
        cfg.SetValue("audio", "volume", Volume);
        cfg.SetValue("audio", "muted", Muted);
        cfg.Save(Path);
    }

    // -----------------------------------------------------------------------
    // Playback
    // -----------------------------------------------------------------------

    /// <param name="gain">Relative loudness, 1 being the effect's natural level.</param>
    /// <param name="pitch">Playback rate. Small random variation stops repeats sounding robotic.</param>
    public void Play(Sound sound, float gain = 1f, float pitch = 1f)
    {
        if (Muted || Volume <= 0.001f) return;

        var stream = Get(sound);
        if (stream == null) return;

        // Round-robin, but prefer a player that is not busy so a long sound is not cut short.
        AudioStreamPlayer p = null;
        for (int i = 0; i < _players.Count; i++)
        {
            var candidate = _players[(_next + i) % _players.Count];
            if (!candidate.Playing) { p = candidate; _next = (_next + i + 1) % _players.Count; break; }
        }
        p ??= _players[_next = (_next + 1) % _players.Count];

        p.Stream = stream;
        p.PitchScale = Mathf.Clamp(pitch, 0.4f, 2.5f);
        p.VolumeDb = Mathf.LinearToDb(Mathf.Clamp(gain * Volume, 0.0001f, 4f));
        p.Play();
    }

    /// <summary>Starts the looping crowd bed. Safe to call repeatedly.</summary>
    public void StartCrowd()
    {
        if (Muted || _crowdBed.Playing) return;
        _crowdBed.Stream = Get(Sound.CrowdCheer, loopBed: true);
        _crowdBed.VolumeDb = Mathf.LinearToDb(Mathf.Clamp(0.09f * Volume, 0.0001f, 1f));
        _crowdBed.Play();
    }

    public void StopCrowd()
    {
        if (_crowdBed != null && _crowdBed.Playing) _crowdBed.Stop();
    }

    // -----------------------------------------------------------------------
    // The bank
    // -----------------------------------------------------------------------

    private readonly Dictionary<Sound, AudioStreamWav> _loopBank = new();

    private AudioStreamWav Get(Sound s, bool loopBed = false)
    {
        if (loopBed)
        {
            if (_loopBank.TryGetValue(s, out var bed)) return bed;
            var madeBed = BuildCrowdBed();
            _loopBank[s] = madeBed;
            return madeBed;
        }

        if (!_bank.TryGetValue(s, out var takes))
        {
            takes = new AudioStreamWav[Takes];
            for (int i = 0; i < Takes; i++) takes[i] = Build(s, i);
            _bank[s] = takes;
        }
        return takes[_pick.Range(0, Takes)];
    }

    private static AudioStreamWav BuildCrowdBed() =>
        new Voice(3.0f)
            .Noise(0.5f, 0.0f, 0.93f, seed: 91, attackSeconds: 0.5f)
            .Noise(0.16f, 0.0f, 0.74f, seed: 92, attackSeconds: 0.5f)
            .Smooth(0.45f)
            .Finish(0.5f)
            .ToStream(loop: true);

    /// <param name="take">
    /// Which render of the recipe this is. It shifts the noise seed and detunes the partials
    /// slightly, so the three takes are related but not identical.
    /// </param>
    private static AudioStreamWav Build(Sound s, int take = 0)
    {
        float d = 1f + (take - 1) * 0.035f;   // partial detune, take 1 is the reference
        int k = take * 977;                   // noise seed offset

        return s switch
        {
        // A bat is a struck wooden bar: a very short bright transient, then a hollow ring that
        // dies fast. The transient is what makes it read as "crack" rather than "thump".
        Sound.Crack => new Voice(0.42f)
            .Noise(1.0f, 190f, 0.18f, seed: 3 + k)
            .Tone(1420f * d, 0.5f, 46f)
            .Tone(880f * d, 0.42f, 26f)
            .Tone(430f * d, 0.30f, 17f)
            .Tone(215f * d, 0.14f, 12f)
            .Smooth()
            .Finish(0.92f)
            .ToStream(),

        Sound.Contact => new Voice(0.32f)
            .Noise(0.7f, 210f, 0.34f, seed: 5 + k)
            .Tone(940f * d, 0.35f, 44f)
            .Tone(470f * d, 0.30f, 26f)
            .Tone(238f * d, 0.16f, 18f)
            .Smooth()
            .Finish(0.78f)
            .ToStream(),

        // Off the end of the bat: no bright transient, mostly low wood.
        Sound.Foul => new Voice(0.30f)
            .Noise(0.42f, 150f, 0.62f, seed: 7 + k)
            .Tone(300f * d, 0.34f, 30f)
            .Tone(168f * d, 0.26f, 20f)
            .Smooth()
            .Finish(0.62f)
            .ToStream(),

        Sound.Bunt => new Voice(0.22f)
            .Noise(0.5f, 240f, 0.55f, seed: 11 + k)
            .Tone(520f * d, 0.22f, 52f)
            .Smooth()
            .Finish(0.55f)
            .ToStream(),

        // Air, not impact — broadband noise swelling and dying with no tone in it.
        Sound.Whiff => new Voice(0.30f)
            .Noise(0.9f, 13f, 0.55f, seed: 13, attackSeconds: 0.09f)
            .Sweep(700f, 190f, 0.10f, 11f)
            .Smooth(0.02f)
            .Finish(0.5f)
            .ToStream(),

        // Leather: a short slap with a low body and almost no ring.
        Sound.MittPop => new Voice(0.20f)
            .Noise(0.95f, 130f, 0.42f, seed: 17 + k)
            .Tone(210f * d, 0.42f, 42f)
            .Tone(118f * d, 0.24f, 30f)
            .Smooth()
            .Finish(0.72f)
            .ToStream(),

        Sound.GloveCatch => new Voice(0.18f)
            .Noise(0.8f, 155f, 0.5f, seed: 19 + k)
            .Tone(180f * d, 0.32f, 46f)
            .Smooth()
            .Finish(0.62f)
            .ToStream(),

        // A crowd is many voices: layered noise at different colours, swelling then settling.
        Sound.CrowdCheer => new Voice(1.9f)
            .Noise(0.85f, 1.5f, 0.90f, seed: 23, attackSeconds: 0.16f)
            .Noise(0.40f, 1.9f, 0.72f, seed: 29, attackSeconds: 0.22f)
            .Noise(0.16f, 2.4f, 0.45f, seed: 31, attackSeconds: 0.3f)
            .Smooth(0.10f)
            .Finish(0.75f)
            .ToStream(),

        Sound.CrowdGroan => new Voice(1.5f)
            .Noise(0.75f, 2.3f, 0.955f, seed: 37, attackSeconds: 0.18f)
            .Sweep(220f, 128f, 0.16f, 2.6f)
            .Smooth(0.10f)
            .Finish(0.55f)
            .ToStream(),

        // Rising organ-style triad over a big crowd swell.
        Sound.Homer => new Voice(1.7f)
            .Tone(392f * d, 0.30f, 3.4f)
            .Tone(523f * d, 0.30f, 3.0f, startSeconds: 0.13f)
            .Tone(659f * d, 0.32f, 2.6f, startSeconds: 0.26f)
            .Tone(784f * d, 0.36f, 1.9f, startSeconds: 0.39f)
            .Tone(1046f * d, 0.26f, 1.7f, startSeconds: 0.52f)
            .Noise(0.55f, 1.4f, 0.90f, seed: 41, attackSeconds: 0.2f)
            .Smooth(0.06f)
            .Finish(0.85f)
            .ToStream(),

        // Two descending notes — the shape of "aw, too bad".
        Sound.Out => new Voice(0.55f)
            .Tone(370f * d, 0.4f, 8f)
            .Tone(262f * d, 0.4f, 7f, startSeconds: 0.16f)
            .Smooth()
            .Finish(0.55f)
            .ToStream(),

        Sound.Walk => new Voice(0.42f)
            .Tone(330f * d, 0.34f, 9f)
            .Tone(440f * d, 0.34f, 8f, startSeconds: 0.14f)
            .Smooth()
            .Finish(0.5f)
            .ToStream(),

        Sound.UiMove => new Voice(0.09f)
            .Tone(760f * d, 0.5f, 42f)
            .Smooth(0.003f)
            .Finish(0.34f)
            .ToStream(),

        Sound.UiSelect => new Voice(0.26f)
            .Tone(620f * d, 0.42f, 16f)
            .Tone(930f * d, 0.38f, 13f, startSeconds: 0.07f)
            .Smooth(0.004f)
            .Finish(0.5f)
            .ToStream(),

        Sound.UiBack => new Voice(0.20f)
            .Tone(520f * d, 0.4f, 18f)
            .Tone(350f * d, 0.36f, 15f, startSeconds: 0.06f)
            .Smooth(0.004f)
            .Finish(0.45f)
            .ToStream(),

        _ => null,
        };
    }

    /// <summary>Builds every effect and returns them as .wav bytes, for `--sfxdump`.</summary>
    public static Dictionary<string, byte[]> DumpAll()
    {
        var outp = new Dictionary<string, byte[]>();
        foreach (Sound s in System.Enum.GetValues<Sound>())
        {
            var v = BuildVoice(s);
            if (v != null) outp[s.ToString()] = v.ToWavFile();
        }
        return outp;
    }

    /// <summary>
    /// The dump mode needs the raw <see cref="Voice"/>, not a stream, so the recipes live in
    /// <see cref="Build"/> and this mirrors them by re-running the same construction.
    /// </summary>
    private static Voice BuildVoice(Sound s)
    {
        // Rebuilt rather than shared because AudioStreamWav does not expose its float samples.
        var stream = Build(s);
        if (stream == null) return null;

        var v = new Voice(stream.Data.Length / 2f / Voice.Rate);
        return v.FromPcm(stream.Data);
    }
}
