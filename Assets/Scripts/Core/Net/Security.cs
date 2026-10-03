using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Catan.Core.Net
{
    /// <summary>Cryptographically secure randomness for seeds and session tokens (never System.Random).</summary>
    public static class SecureRandom
    {
        static readonly RandomNumberGenerator Generator = RandomNumberGenerator.Create();

        public static byte[] Bytes(int count)
        {
            var bytes = new byte[count];
            lock (Generator) Generator.GetBytes(bytes);
            return bytes;
        }

        public static int NextInt() => BitConverter.ToInt32(Bytes(4), 0);
    }

    public static class ConstantTime
    {
        /// <summary>Compares without bailing out at the first difference, so timing doesn't reveal how much matched.</summary>
        public static bool Equals(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            int diff = a.Length ^ b.Length;
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static bool Equals(string a, string b) =>
            Equals(Encoding.UTF8.GetBytes(a ?? ""), Encoding.UTF8.GetBytes(b ?? ""));
    }

    /// <summary>
    /// Player-supplied display names end up in rich-text UI labels and the game log, so they are reduced to
    /// plain, short, printable text: no markup brackets, no control or format characters.
    /// </summary>
    public static class NameSanitizer
    {
        public const int MaxLength = 20;

        public static string Clean(string raw, string fallback, int maxLength = MaxLength, bool stripAmpersand = true)
        {
            var sb = new StringBuilder();
            bool lastSpace = true; // trims leading spaces
            foreach (char c in raw ?? "")
            {
                if (sb.Length >= maxLength) break;
                if (c == '<' || c == '>' || (stripAmpersand && c == '&')) continue;
                if (char.IsWhiteSpace(c))
                {
                    if (!lastSpace) sb.Append(' ');
                    lastSpace = true;
                    continue;
                }
                var category = char.GetUnicodeCategory(c);
                if (char.IsControl(c) ||
                    category == System.Globalization.UnicodeCategory.Format ||
                    category == System.Globalization.UnicodeCategory.PrivateUse ||
                    category == System.Globalization.UnicodeCategory.OtherNotAssigned ||
                    char.IsSurrogate(c)) continue; // surrogates: drop rather than risk split pairs
                sb.Append(c);
                lastSpace = false;
            }

            string name = sb.ToString().TrimEnd();
            return name.Length == 0 ? fallback : name;
        }
    }

    /// <summary>Token bucket per key (a client). Time is injected so tests don't sleep.</summary>
    public sealed class RateLimiter
    {
        sealed class Bucket
        {
            public double Tokens;
            public double LastSeen;
        }

        readonly double _perSecond;
        readonly double _burst;
        readonly Func<double> _clock;
        readonly Dictionary<ulong, Bucket> _buckets = new Dictionary<ulong, Bucket>();

        public RateLimiter(double perSecond, int burst, Func<double> clock)
        {
            _perSecond = perSecond;
            _burst = burst;
            _clock = clock;
        }

        /// <summary>True if the caller may proceed; false means drop the request.</summary>
        public bool Allow(ulong key)
        {
            double now = _clock();
            if (!_buckets.TryGetValue(key, out Bucket b))
            {
                b = new Bucket { Tokens = _burst, LastSeen = now };
                _buckets[key] = b;
            }

            b.Tokens = Math.Min(_burst, b.Tokens + (now - b.LastSeen) * _perSecond);
            b.LastSeen = now;
            if (b.Tokens < 1.0) return false;
            b.Tokens -= 1.0;
            return true;
        }

        public void Forget(ulong key) => _buckets.Remove(key);
    }
}
