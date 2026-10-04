using System;
using System.Collections.Generic;

namespace Conquer.Core
{
    public enum Resource
    {
        Wasteland,
        Timber,
        Clay,
        Livestock,
        Grain,
        Iron,
    }

    public sealed class Tile
    {
        public Hex Hex { get; }
        public Resource Resource { get; }

        /// <summary>Dice number token (2-12, never 7); 0 for the wasteland.</summary>
        public int Number { get; }

        public bool IsWasteland => Resource == Resource.Wasteland;

        /// <summary>Probability dots on the token: 6 - |7 - Number| (out of 36 dice outcomes).</summary>
        public int Pips => Number == 0 ? 0 : 6 - Math.Abs(7 - Number);

        public Tile(Hex hex, Resource resource, int number)
        {
            Hex = hex;
            Resource = resource;
            Number = number;
        }

        public override string ToString() => $"{Hex} {Resource} {(Number == 0 ? "-" : Number.ToString())}";
    }

    /// <summary>A harbor on a coastal edge. Generic ports trade 3:1, resource ports 2:1.</summary>
    public sealed class Port
    {
        public Edge Edge { get; }
        public bool IsGeneric { get; }

        /// <summary>The resource traded at 2:1; <see cref="Resource.Wasteland"/> when generic.</summary>
        public Resource Resource { get; }

        public int Ratio => IsGeneric ? 3 : 2;

        public Port(Edge edge, bool isGeneric, Resource resource)
        {
            Edge = edge;
            IsGeneric = isGeneric;
            Resource = isGeneric ? Resource.Wasteland : resource;
        }
    }

    public sealed class BoardConfig
    {
        /// <summary>Hex rings around the center. 2 gives the standard 19-tile board; 3 gives 37, etc.</summary>
        public int Radius { get; set; } = 2;

        /// <summary>Same seed + same radius always yields the identical board.</summary>
        public int Seed { get; set; }

        public bool IncludePorts { get; set; } = true;
    }

    /// <summary>A source of random integers for dice, the action-card deck and steals.</summary>
    public interface IRandom
    {
        /// <summary>Integer in [0, max).</summary>
        int Next(int max);
    }

    public static class RandomExtensions
    {
        /// <summary>Fisher-Yates shuffle.</summary>
        public static void Shuffle<T>(this IRandom random, IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                T t = list[i];
                list[i] = list[j];
                list[j] = t;
            }
        }
    }

    /// <summary>
    /// Small deterministic PRNG (SplitMix64) so a seed reproduces the same board on every machine. Fine for
    /// boards (public anyway), local games and tests, but a 32-bit seed can be brute-forced from observed dice,
    /// so online games use <see cref="Net.SecureRng"/> for anything secret.
    /// </summary>
    public sealed class Rng : IRandom
    {
        ulong _state;

        public Rng(int seed)
        {
            _state = unchecked((ulong)(uint)seed);
        }

        public ulong NextULong()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Uniform-ish integer in [0, max).</summary>
        public int Next(int max)
        {
            if (max <= 0) throw new ArgumentOutOfRangeException(nameof(max));
            return (int)(NextULong() % (ulong)max);
        }

        public void Shuffle<T>(IList<T> list) => RandomExtensions.Shuffle(this, list);
    }
}
