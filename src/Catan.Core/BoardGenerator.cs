using System;
using System.Collections.Generic;

namespace Catan.Core
{
    /// <summary>
    /// Builds a randomized board of any radius. Resource and number-token pools scale from the classic
    /// 19-tile distribution, 6s and 8s are kept apart, and ports are spaced evenly around the coast.
    /// </summary>
    public static class BoardGenerator
    {
        public const int MinRadius = 1;
        public const int MaxRadius = 10;

        static readonly int[] StandardTokens = { 2, 3, 3, 4, 4, 5, 5, 6, 6, 8, 8, 9, 9, 10, 10, 11, 11, 12 };

        // Classic ratio of land resources: 4 wood, 4 sheep, 4 wheat, 3 brick, 3 ore (18 total).
        static readonly Resource[] ResourceOrder = { Resource.Wood, Resource.Sheep, Resource.Wheat, Resource.Brick, Resource.Ore };
        static readonly int[] ResourceWeights = { 4, 4, 4, 3, 3 };
        const int WeightSum = 18;

        public static Board Generate(BoardConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (config.Radius < MinRadius || config.Radius > MaxRadius)
                throw new ArgumentOutOfRangeException(nameof(config), $"Radius must be {MinRadius}-{MaxRadius}.");

            var rng = new Rng(config.Seed);
            var hexes = new List<Hex>(Hex.Spiral(Hex.Zero, config.Radius));

            List<Resource> resources = BuildResourcePool(hexes.Count, rng);

            var tiles = new Dictionary<Hex, Tile>();
            var producers = new List<Hex>();
            for (int i = 0; i < hexes.Count; i++)
            {
                if (resources[i] != Resource.Desert) producers.Add(hexes[i]);
            }

            Dictionary<Hex, int> numbers = AssignNumbers(producers, rng);

            for (int i = 0; i < hexes.Count; i++)
            {
                Hex h = hexes[i];
                numbers.TryGetValue(h, out int n); // 0 for desert
                tiles[h] = new Tile(h, resources[i], n);
            }

            var ports = config.IncludePorts ? BuildPorts(config.Radius, tiles, rng) : new List<Port>();
            return new Board(config.Radius, config.Seed, tiles, ports);
        }

        /// <summary>Shuffled resource list, one entry per hex, with the desert count scaled to board size.</summary>
        static List<Resource> BuildResourcePool(int hexCount, Rng rng)
        {
            int deserts = Math.Max(1, (int)Math.Round(hexCount / 19.0));
            int land = hexCount - deserts;

            // Largest-remainder apportionment keeps the classic ratio at any size.
            var counts = new int[ResourceOrder.Length];
            var remainders = new int[ResourceOrder.Length];
            int assigned = 0;
            for (int i = 0; i < counts.Length; i++)
            {
                counts[i] = land * ResourceWeights[i] / WeightSum;
                remainders[i] = land * ResourceWeights[i] % WeightSum;
                assigned += counts[i];
            }
            for (int left = land - assigned; left > 0; left--)
            {
                int best = 0;
                for (int i = 1; i < counts.Length; i++)
                {
                    if (remainders[i] > remainders[best]) best = i;
                }
                counts[best]++;
                remainders[best] = -1;
            }

            var pool = new List<Resource>(hexCount);
            for (int i = 0; i < deserts; i++) pool.Add(Resource.Desert);
            for (int i = 0; i < counts.Length; i++)
            {
                for (int k = 0; k < counts[i]; k++) pool.Add(ResourceOrder[i]);
            }
            rng.Shuffle(pool);
            return pool;
        }

        /// <summary>Full copies of the classic 18-token set, plus a random subset for the remainder.</summary>
        static List<int> BuildTokenPool(int count, Rng rng)
        {
            var pool = new List<int>(count);
            while (pool.Count + StandardTokens.Length <= count) pool.AddRange(StandardTokens);

            int rest = count - pool.Count;
            if (rest > 0)
            {
                var extra = new List<int>(StandardTokens);
                rng.Shuffle(extra);
                pool.AddRange(extra.GetRange(0, rest));
            }
            return pool;
        }

        static bool IsHot(int n) => n == 6 || n == 8;

        /// <summary>Counts hexes holding a 6/8 that touch another 6/8; reports one such hex.</summary>
        static int HotViolations(Dictionary<Hex, int> numbers, out Hex first)
        {
            int count = 0;
            first = Hex.Zero;
            foreach (KeyValuePair<Hex, int> kv in numbers)
            {
                if (!IsHot(kv.Value)) continue;
                foreach (Hex nb in kv.Key.Neighbors())
                {
                    if (numbers.TryGetValue(nb, out int other) && IsHot(other))
                    {
                        if (count == 0) first = kv.Key;
                        count++;
                        break;
                    }
                }
            }
            return count;
        }

        static Dictionary<Hex, int> AssignNumbers(List<Hex> producers, Rng rng)
        {
            List<int> tokens = BuildTokenPool(producers.Count, rng);
            rng.Shuffle(tokens);

            var numbers = new Dictionary<Hex, int>();
            for (int i = 0; i < producers.Count; i++) numbers[producers[i]] = tokens[i];

            // Hill-climb: swap an offending token with a random one, keeping the swap unless it makes things worse.
            int violations = HotViolations(numbers, out Hex bad);
            for (int iter = 0; iter < 4000 && violations > 0; iter++)
            {
                Hex other = producers[rng.Next(producers.Count)];
                if (other == bad) continue;

                int a = numbers[bad];
                int b = numbers[other];
                numbers[bad] = b;
                numbers[other] = a;

                int after = HotViolations(numbers, out Hex nextBad);
                if (after <= violations)
                {
                    violations = after;
                    bad = nextBad;
                }
                else
                {
                    numbers[bad] = a;
                    numbers[other] = b;
                }
            }
            return numbers;
        }

        static List<Port> BuildPorts(int radius, Dictionary<Hex, Tile> tiles, Rng rng)
        {
            // Coastal edges: land hex on one side, sea on the other. Sorted by angle around the center so
            // "every Nth edge" is evenly spread regardless of the board's shape.
            var coast = new List<Edge>();
            foreach (Hex h in Hex.Ring(Hex.Zero, radius))
            {
                for (int d = 0; d < 6; d++)
                {
                    if (!tiles.ContainsKey(h.Neighbor(d))) coast.Add(Edge.OfSide(h, d));
                }
            }
            coast.Sort((x, y) => Angle(x).CompareTo(Angle(y)));

            // Classic board: 9 ports over 30 coastal edges, 4 generic + one 2:1 per resource.
            int portCount = Math.Max(3, (int)Math.Round(coast.Count / (30.0 / 9.0)));
            int generic = (int)Math.Round(portCount * 4.0 / 9.0);

            var kinds = new List<Resource>(portCount); // Desert marks a generic port
            for (int i = 0; i < generic; i++) kinds.Add(Resource.Desert);
            for (int i = 0; kinds.Count < portCount; i++) kinds.Add(ResourceOrder[i % ResourceOrder.Length]);
            rng.Shuffle(kinds);

            int offset = rng.Next(coast.Count);
            var ports = new List<Port>(portCount);
            for (int i = 0; i < portCount; i++)
            {
                Edge e = coast[(offset + i * coast.Count / portCount) % coast.Count];
                bool isGeneric = kinds[i] == Resource.Desert;
                ports.Add(new Port(e, isGeneric, kinds[i]));
            }
            return ports;
        }

        static double Angle(Edge e)
        {
            var p = HexLayout.ToPlane(e);
            return Math.Atan2(p.Y, p.X);
        }
    }
}
