using System;
using System.Collections.Generic;
using System.Linq;

namespace Catan.Server
{
    /// <summary>Thread-safe token buckets keyed by a string (an IP address), with a bounded number of keys.</summary>
    public sealed class KeyedRateLimiter
    {
        sealed class Bucket
        {
            public double Tokens;
            public double LastSeen;
        }

        readonly double _perSecond;
        readonly double _burst;
        readonly int _maxKeys;
        readonly Func<double> _clock;
        readonly Dictionary<string, Bucket> _buckets = new Dictionary<string, Bucket>();

        public KeyedRateLimiter(double perSecond, int burst, Func<double> clock, int maxKeys = 10_000)
        {
            _perSecond = perSecond;
            _burst = burst;
            _clock = clock;
            _maxKeys = maxKeys;
        }

        public bool Allow(string key)
        {
            lock (_buckets)
            {
                double now = _clock();
                if (!_buckets.TryGetValue(key, out Bucket b))
                {
                    if (_buckets.Count >= _maxKeys) Prune(now);
                    b = new Bucket { Tokens = _burst, LastSeen = now };
                    _buckets[key] = b;
                }
                b.Tokens = Math.Min(_burst, b.Tokens + (now - b.LastSeen) * _perSecond);
                b.LastSeen = now;
                if (b.Tokens < 1.0) return false;
                b.Tokens -= 1.0;
                return true;
            }
        }

        void Prune(double now)
        {
            // A bucket that would have refilled completely carries no information.
            double full = _burst / _perSecond;
            foreach (string k in _buckets.Where(kv => now - kv.Value.LastSeen > full).Select(kv => kv.Key).ToList())
                _buckets.Remove(k);
            if (_buckets.Count >= _maxKeys) _buckets.Clear(); // under attack from many addresses: start over
        }
    }

    /// <summary>Counts live things (connections, rooms) per IP and in total, and refuses past the caps.</summary>
    public sealed class CountLimiter
    {
        readonly int _perKey;
        readonly int _total;
        readonly Dictionary<string, int> _counts = new Dictionary<string, int>();
        int _sum;

        public CountLimiter(int perKey, int total)
        {
            _perKey = perKey;
            _total = total;
        }

        public int Total
        {
            get { lock (_counts) return _sum; }
        }

        public bool TryAcquire(string key)
        {
            lock (_counts)
            {
                _counts.TryGetValue(key, out int n);
                if (n >= _perKey || _sum >= _total) return false;
                _counts[key] = n + 1;
                _sum++;
                return true;
            }
        }

        public void Release(string key)
        {
            lock (_counts)
            {
                if (!_counts.TryGetValue(key, out int n)) return;
                if (n <= 1) _counts.Remove(key);
                else _counts[key] = n - 1;
                _sum--;
            }
        }
    }
}
