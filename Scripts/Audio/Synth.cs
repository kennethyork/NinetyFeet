using System;
using Godot;

namespace SandlotSlugfest.Audio;

/// <summary>
/// Builds sounds as raw PCM at runtime.
///
/// This project ships no binary assets — every sprite is drawn from code — and the audio follows
/// the same rule. Each effect below is synthesised from oscillators and shaped noise the first
/// time it is asked for, so there are no .wav files to import, licence, or lose.
/// </summary>
public sealed class Voice
{
    public const int Rate = 22050;

    private readonly float[] _samples;

    public Voice(float seconds)
    {
        _samples = new float[Math.Max(1, (int)(seconds * Rate))];
    }

    public int Length => _samples.Length;

    /// <summary>Adds a sine partial with an exponential decay.</summary>
    public Voice Tone(float hz, float amp, float decay, float startSeconds = 0f, float detune = 0f)
    {
        int start = (int)(startSeconds * Rate);
        for (int i = start; i < _samples.Length; i++)
        {
            float t = (i - start) / (float)Rate;
            float env = Mathf.Exp(-t * decay);
            if (env < 0.0005f) break;
            float f = hz + detune * t;
            _samples[i] += Mathf.Sin(Mathf.Tau * f * t) * amp * env;
        }
        return this;
    }

    /// <summary>
    /// Adds a frequency sweep — the body of a whoosh, or the pitch drop in a cartoon thud.
    /// </summary>
    public Voice Sweep(float fromHz, float toHz, float amp, float decay, float startSeconds = 0f)
    {
        int start = (int)(startSeconds * Rate);
        int n = _samples.Length - start;
        if (n <= 0) return this;

        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float p = i / (float)n;
            float f = Mathf.Lerp(fromHz, toHz, p);
            phase += Mathf.Tau * f / Rate;
            _samples[start + i] += Mathf.Sin(phase) * amp * Mathf.Exp(-t * decay);
        }
        return this;
    }

    /// <summary>
    /// Adds filtered noise. <paramref name="colour"/> is a one-pole low-pass coefficient: near 1
    /// is a dull rumble, near 0 is bright hiss.
    /// </summary>
    public Voice Noise(float amp, float decay, float colour, float startSeconds = 0f, int seed = 1,
        float attackSeconds = 0f)
    {
        int start = (int)(startSeconds * Rate);
        var rng = new Random(seed);
        float last = 0f;
        for (int i = start; i < _samples.Length; i++)
        {
            float t = (i - start) / (float)Rate;
            float env = Mathf.Exp(-t * decay);
            if (env < 0.0005f) break;

            // A soft attack stops a swell from starting with a click.
            if (attackSeconds > 0f) env *= Mathf.Min(1f, t / attackSeconds);

            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            last = Mathf.Lerp(white, last, colour);
            _samples[i] += last * amp * env;
        }
        return this;
    }

    /// <summary>Fades the very start and end, so nothing clicks when it is triggered or cut.</summary>
    public Voice Smooth(float edgeSeconds = 0.004f)
    {
        int edge = Math.Max(1, (int)(edgeSeconds * Rate));
        for (int i = 0; i < edge && i < _samples.Length; i++)
        {
            float k = i / (float)edge;
            _samples[i] *= k;
            _samples[_samples.Length - 1 - i] *= k;
        }
        return this;
    }

    /// <summary>Peak-normalises, then applies a soft clip so loud effects stay warm.</summary>
    public Voice Finish(float peak = 0.85f)
    {
        float max = 0f;
        foreach (float v in _samples) max = Math.Max(max, Math.Abs(v));
        if (max < 1e-6f) return this;

        float gain = peak / max;
        for (int i = 0; i < _samples.Length; i++)
        {
            float v = _samples[i] * gain;
            _samples[i] = Mathf.Tanh(v * 1.2f) * 0.92f;
        }
        return this;
    }

    /// <summary>Reads samples back out of PCM, so the dump mode can reuse the built streams.</summary>
    public Voice FromPcm(byte[] pcm)
    {
        int n = Math.Min(_samples.Length, pcm.Length / 2);
        for (int i = 0; i < n; i++)
        {
            short v = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
            _samples[i] = v / 32000f;
        }
        return this;
    }

    /// <summary>Signed 16-bit little-endian PCM, which is what AudioStreamWav wants.</summary>
    public byte[] ToPcm16()
    {
        var bytes = new byte[_samples.Length * 2];
        for (int i = 0; i < _samples.Length; i++)
        {
            short s = (short)Mathf.Clamp(_samples[i] * 32000f, -32768f, 32767f);
            bytes[i * 2] = (byte)(s & 0xFF);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return bytes;
    }

    public AudioStreamWav ToStream(bool loop = false)
    {
        var wav = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = Rate,
            Stereo = false,
            Data = ToPcm16(),
        };
        if (loop)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            wav.LoopBegin = 0;
            wav.LoopEnd = _samples.Length;
        }
        return wav;
    }

    /// <summary>A standalone .wav file, used by the `--sfxdump` verification mode.</summary>
    public byte[] ToWavFile()
    {
        byte[] pcm = ToPcm16();
        var f = new System.IO.MemoryStream();
        var w = new System.IO.BinaryWriter(f);
        w.Write(new[] { 'R', 'I', 'F', 'F' });
        w.Write(36 + pcm.Length);
        w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
        w.Write(16);
        w.Write((short)1);            // PCM
        w.Write((short)1);            // mono
        w.Write(Rate);
        w.Write(Rate * 2);            // byte rate
        w.Write((short)2);            // block align
        w.Write((short)16);           // bits
        w.Write(new[] { 'd', 'a', 't', 'a' });
        w.Write(pcm.Length);
        w.Write(pcm);
        w.Flush();
        return f.ToArray();
    }
}
