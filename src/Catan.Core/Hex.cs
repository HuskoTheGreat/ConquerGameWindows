using System;
using System.Collections.Generic;

namespace Catan.Core
{
    /// <summary>
    /// Axial hex coordinate (pointy-top orientation). Third cube axis is derived: S = -Q - R.
    /// See redblobgames.com/grids/hexagons.
    /// </summary>
    public readonly struct Hex : IEquatable<Hex>, IComparable<Hex>
    {
        public readonly int Q;
        public readonly int R;

        public int S => -Q - R;

        public Hex(int q, int r)
        {
            Q = q;
            R = r;
        }

        /// <summary>Neighbor directions, ordered clockwise starting east (screen coords, y down).</summary>
        public static readonly Hex[] Directions =
        {
            new Hex(1, 0), new Hex(1, -1), new Hex(0, -1),
            new Hex(-1, 0), new Hex(-1, 1), new Hex(0, 1),
        };

        public static readonly Hex Zero = new Hex(0, 0);

        public static Hex operator +(Hex a, Hex b) => new Hex(a.Q + b.Q, a.R + b.R);
        public static Hex operator -(Hex a, Hex b) => new Hex(a.Q - b.Q, a.R - b.R);
        public static Hex operator *(Hex a, int k) => new Hex(a.Q * k, a.R * k);
        public static bool operator ==(Hex a, Hex b) => a.Q == b.Q && a.R == b.R;
        public static bool operator !=(Hex a, Hex b) => !(a == b);

        /// <summary>Neighbor in direction <paramref name="dir"/>; any integer is wrapped into 0..5.</summary>
        public Hex Neighbor(int dir) => this + Directions[((dir % 6) + 6) % 6];

        public IEnumerable<Hex> Neighbors()
        {
            for (int i = 0; i < 6; i++) yield return Neighbor(i);
        }

        public int DistanceTo(Hex other)
        {
            int dq = Q - other.Q;
            int dr = R - other.R;
            return (Math.Abs(dq) + Math.Abs(dq + dr) + Math.Abs(dr)) / 2;
        }

        /// <summary>The hexes at exactly <paramref name="radius"/> steps from <paramref name="center"/>, walked clockwise.</summary>
        public static IEnumerable<Hex> Ring(Hex center, int radius)
        {
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            if (radius == 0)
            {
                yield return center;
                yield break;
            }

            Hex hex = center + Directions[4] * radius;
            for (int side = 0; side < 6; side++)
            {
                for (int step = 0; step < radius; step++)
                {
                    yield return hex;
                    hex = hex.Neighbor(side);
                }
            }
        }

        /// <summary>Center first, then each ring outward, up to and including <paramref name="radius"/>.</summary>
        public static IEnumerable<Hex> Spiral(Hex center, int radius)
        {
            for (int r = 0; r <= radius; r++)
            {
                foreach (Hex h in Ring(center, r)) yield return h;
            }
        }

        /// <summary>Number of hexes in a hexagonal board of the given radius.</summary>
        public static int CountForRadius(int radius) => 3 * radius * (radius + 1) + 1;

        public bool Equals(Hex other) => this == other;
        public override bool Equals(object obj) => obj is Hex h && this == h;
        public override int GetHashCode() => (Q * 397) ^ R;

        public int CompareTo(Hex other)
        {
            int c = Q.CompareTo(other.Q);
            return c != 0 ? c : R.CompareTo(other.R);
        }

        public override string ToString() => $"({Q},{R})";
    }
}
