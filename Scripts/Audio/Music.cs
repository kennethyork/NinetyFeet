using System;
using Godot;

namespace SandlotSlugfest.Audio;

public enum Tune { Menu, Ballpark, Victory }

/// <summary>
/// The music, written as note data and synthesised at runtime like everything else here — no
/// audio files. Each tune is a melody over a bass line and a light off-beat chord, rendered once
/// into a looping stream the first time it is asked for.
/// </summary>
public partial class Music : Node
{
    public static Music Instance { get; private set; }

    private AudioStreamPlayer _player;
    private Tune? _playing;

    public bool Enabled { get; private set; } = true;

    private readonly System.Collections.Generic.Dictionary<Tune, AudioStreamWav> _bank = new();

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;

        _player = new AudioStreamPlayer { Bus = "Master" };
        AddChild(_player);

        var cfg = new ConfigFile();
        if (cfg.Load("user://settings.cfg") == Error.Ok)
            Enabled = (bool)cfg.GetValue("audio", "music", true);
    }

    public void SetEnabled(bool on)
    {
        Enabled = on;
        if (!on) Stop();
        else if (_playing is { } t) { _playing = null; Play(t); }

        var cfg = new ConfigFile();
        cfg.Load("user://settings.cfg");
        cfg.SetValue("audio", "music", on);
        cfg.Save("user://settings.cfg");
    }

    /// <summary>Starts a tune, or leaves it alone if it is already the one playing.</summary>
    public void Play(Tune tune)
    {
        if (!Enabled || Sfx.Instance is { Muted: true }) return;
        if (_playing == tune && _player.Playing) return;

        if (!_bank.TryGetValue(tune, out var stream))
        {
            stream = Build(tune);
            _bank[tune] = stream;
        }

        _playing = tune;
        _player.Stream = stream;

        // Well under the effects — music you notice over the game is music turned up too loud.
        _player.VolumeDb = Mathf.LinearToDb(Mathf.Clamp(
            (tune == Tune.Ballpark ? 0.16f : 0.32f) * (Sfx.Instance?.Volume ?? 0.7f), 0.0001f, 1f));
        _player.Play();
    }

    public void Stop()
    {
        _playing = null;
        if (_player is { Playing: true }) _player.Stop();
    }

    // -----------------------------------------------------------------------
    // Composition
    // -----------------------------------------------------------------------

    /// <summary>Semitone offsets from the root, as scale degrees of a major scale.</summary>
    private static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11, 12, 14, 16 };

    private static float Hz(int root, int degree)
    {
        int semis = root + Major[Mathf.Clamp(degree, 0, Major.Length - 1)];
        return 220f * Mathf.Pow(2f, semis / 12f);
    }

    /// <summary>
    /// A tune, as (degree, beats) pairs. -1 is a rest. Written by hand rather than randomised:
    /// a generated melody is a different melody every time, and a theme has to be the same theme.
    /// </summary>
    private static (int Deg, float Beats)[] Melody(Tune tune) => tune switch
    {
        // Bright, marchy, a bit brassy — the walk-up to a sandlot game.
        Tune.Menu => new[]
        {
            (4, 1f), (4, 0.5f), (5, 0.5f), (6, 1f), (4, 1f),
            (7, 1f), (6, 0.5f), (5, 0.5f), (4, 2f),
            (2, 1f), (4, 0.5f), (5, 0.5f), (6, 1f), (7, 1f),
            (8, 1.5f), (7, 0.5f), (6, 2f),
            (6, 1f), (7, 0.5f), (8, 0.5f), (9, 1f), (8, 1f),
            (7, 1f), (6, 0.5f), (5, 0.5f), (4, 2f),
            (0, 1f), (2, 1f), (4, 1f), (5, 1f),
            (4, 4f),
        },

        // Sparse and low, meant to sit under crowd noise without competing with it.
        Tune.Ballpark => new[]
        {
            (0, 2f), (2, 2f), (4, 2f), (2, 2f),
            (-1, 2f), (4, 1f), (5, 1f), (4, 4f),
            (2, 2f), (0, 2f), (-1, 4f),
        },

        // Short and triumphant.
        _ => new[]
        {
            (0, 0.5f), (2, 0.5f), (4, 0.5f), (7, 1.5f),
            (4, 0.5f), (7, 0.5f), (9, 2f),
        },
    };

    private static (int Deg, float Beats)[] Bass(Tune tune) => tune switch
    {
        Tune.Menu => new[]
        {
            (0, 1f), (0, 1f), (4, 1f), (4, 1f),
            (5, 1f), (5, 1f), (2, 1f), (4, 1f),
        },
        Tune.Ballpark => new[] { (0, 2f), (4, 2f), (5, 2f), (4, 2f) },
        _ => new[] { (0, 1f), (4, 1f), (0, 2f) },
    };

    private static AudioStreamWav Build(Tune tune)
    {
        float bpm = tune switch { Tune.Menu => 132f, Tune.Ballpark => 96f, _ => 150f };
        float beat = 60f / bpm;

        var mel = Melody(tune);
        var bass = Bass(tune);

        float melBeats = 0f;
        foreach (var (_, b) in mel) melBeats += b;
        float bassBeats = 0f;
        foreach (var (_, b) in bass) bassBeats += b;

        // Round the loop up to a whole number of bass phrases so the loop point lands on a bar.
        float totalBeats = Mathf.Ceil(melBeats / bassBeats) * bassBeats;
        var voice = new Voice(totalBeats * beat + 0.4f);

        // --- Melody: a bright plucked tone, two partials plus a soft attack chiff. ---
        float t = 0f;
        foreach (var (deg, beats) in mel)
        {
            float dur = beats * beat;
            if (deg >= 0)
            {
                float f = Hz(0, deg);
                float decay = 3.2f / Mathf.Max(0.2f, dur);
                voice.Tone(f, 0.30f, decay, t);
                voice.Tone(f * 2f, 0.10f, decay * 1.6f, t);
                voice.Tone(f * 3f, 0.04f, decay * 2.2f, t);
            }
            t += dur;
        }

        // --- Bass: low, round, repeating under the whole melody. ---
        float bt = 0f;
        while (bt < totalBeats)
        {
            foreach (var (deg, beats) in bass)
            {
                if (bt >= totalBeats) break;
                float f = Hz(-24, deg);
                voice.Tone(f, 0.34f, 4.5f / Mathf.Max(0.25f, beats * beat), bt * beat);
                voice.Tone(f * 2f, 0.08f, 7f, bt * beat);
                bt += beats;
            }
        }

        // --- Off-beat chord stabs, so it swings rather than plods. ---
        if (tune != Tune.Ballpark)
        {
            for (float b = 0.5f; b < totalBeats; b += 1f)
            {
                foreach (int deg in new[] { 2, 4, 6 })
                    voice.Tone(Hz(-12, deg), 0.055f, 16f, b * beat);
            }
        }

        return voice.Smooth(0.03f).Finish(0.55f).ToStream(loop: true);
    }

    /// <summary>Builds every tune as .wav bytes, for `--sfxdump`.</summary>
    public static System.Collections.Generic.Dictionary<string, byte[]> DumpAll()
    {
        var outp = new System.Collections.Generic.Dictionary<string, byte[]>();
        foreach (Tune t in Enum.GetValues<Tune>())
        {
            var wav = Build(t);
            var v = new Voice(wav.Data.Length / 2f / Voice.Rate).FromPcm(wav.Data);
            outp["music_" + t] = v.ToWavFile();
        }
        return outp;
    }
}
