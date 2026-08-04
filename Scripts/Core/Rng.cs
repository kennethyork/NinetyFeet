using System;

namespace SandlotSlugfest.Core;

/// <summary>
/// Small deterministic PRNG (xorshift32). Used instead of <see cref="System.Random"/> so that a
/// given seed always produces the same rosters, on every machine and every runtime version.
/// </summary>
public struct Rng
{
    private uint _state;

    public Rng(int seed)
    {
        // Avoid the zero state, which xorshift cannot escape.
        _state = (uint)seed * 2654435761u + 1013904223u;
        if (_state == 0) _state = 0x9E3779B9;
    }

    public uint NextUInt()
    {
        _state ^= _state << 13;
        _state ^= _state >> 17;
        _state ^= _state << 5;
        return _state;
    }

    /// <summary>
    /// The generator's current position, without advancing it.
    ///
    /// Only for diagnostics: an online game is two machines walking one stream, and when their
    /// results differ the first thing worth knowing is whether they are standing in the same
    /// place in it. Reading this never changes the sequence.
    /// </summary>
    public readonly uint Peek() => _state;

    /// <summary>Uniform float in [0, 1).</summary>
    public float NextFloat() => (NextUInt() >> 8) * (1.0f / 16777216.0f);

    /// <summary>Uniform float in [min, max).</summary>
    public float Range(float min, float max) => min + NextFloat() * (max - min);

    /// <summary>Uniform integer in [min, maxExclusive).</summary>
    public int Range(int min, int maxExclusive)
    {
        if (maxExclusive <= min) return min;
        return min + (int)(NextUInt() % (uint)(maxExclusive - min));
    }

    public bool Chance(float probability) => NextFloat() < probability;

    /// <summary>Roughly bell-shaped value in [0, 1) — the average of three uniforms.</summary>
    public float Bell() => (NextFloat() + NextFloat() + NextFloat()) / 3.0f;

    public T Pick<T>(T[] items) => items[Range(0, items.Length)];

    public void Shuffle<T>(T[] items)
    {
        for (int i = items.Length - 1; i > 0; i--)
        {
            int j = Range(0, i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
