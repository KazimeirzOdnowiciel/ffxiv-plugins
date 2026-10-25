using System;

namespace RouletteBingo.Core;

/// <summary>
/// Small deterministic PRNG. Same seed, same card - on any runtime, any platform, forever.
/// System.Random is not guaranteed to be stable between .NET versions, which would silently
/// change every shared card code, so we roll our own.
/// </summary>
public struct SplitMix64
{
    private ulong state;

    public SplitMix64(ulong seed)
    {
        this.state = seed;
    }

    public ulong Next()
    {
        this.state += 0x9E3779B97F4A7C15UL;
        var z = this.state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform-ish integer in [0, max).</summary>
    public int NextInt(int max)
    {
        if (max <= 0)
            return 0;

        return (int)(this.Next() % (ulong)max);
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (this.Next() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>In-place Fisher-Yates.</summary>
    public void Shuffle<T>(System.Collections.Generic.IList<T> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = this.NextInt(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
