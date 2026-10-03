using System;
using System.Collections.Generic;

namespace Catan.Core
{
    /// <summary>
    /// A hex corner (where settlements/cities go), identified by the three hexes that meet there.
    /// The hexes are stored sorted, so every corner has exactly one canonical value regardless of
    /// which hex it was reached from. Some of the three hexes may be off-board (sea).
    /// </summary>
    public readonly struct Vertex : IEquatable<Vertex>
    {
        public readonly Hex A;
        public readonly Hex B;
        public readonly Hex C;

        public Vertex(Hex x, Hex y, Hex z)
        {
            if (y.CompareTo(x) < 0) Swap(ref x, ref y);
            if (z.CompareTo(y) < 0) Swap(ref y, ref z);
            if (y.CompareTo(x) < 0) Swap(ref x, ref y);
            A = x;
            B = y;
            C = z;
        }

        /// <summary>Corner <paramref name="corner"/> (0..5) of a hex: between neighbor directions corner and corner+1.</summary>
        public static Vertex OfCorner(Hex hex, int corner) =>
            new Vertex(hex, hex.Neighbor(corner), hex.Neighbor(corner + 1));

        public IEnumerable<Hex> Hexes()
        {
            yield return A;
            yield return B;
            yield return C;
        }

        /// <summary>The three edge slots meeting at this corner (they may not all exist on a given board).</summary>
        public IEnumerable<Edge> Edges()
        {
            yield return new Edge(A, B);
            yield return new Edge(A, C);
            yield return new Edge(B, C);
        }

        /// <summary>The three corners one road away (they may not all exist on a given board).</summary>
        public IEnumerable<Vertex> Neighbors()
        {
            foreach (Edge e in Edges())
            {
                foreach (Vertex end in e.Endpoints())
                {
                    if (end != this) yield return end;
                }
            }
        }

        static void Swap(ref Hex a, ref Hex b)
        {
            Hex t = a;
            a = b;
            b = t;
        }

        public static bool operator ==(Vertex a, Vertex b) => a.A == b.A && a.B == b.B && a.C == b.C;
        public static bool operator !=(Vertex a, Vertex b) => !(a == b);
        public bool Equals(Vertex other) => this == other;
        public override bool Equals(object obj) => obj is Vertex v && this == v;
        public override int GetHashCode() => (((A.GetHashCode() * 31) ^ B.GetHashCode()) * 31) ^ C.GetHashCode();
        public override string ToString() => $"V[{A} {B} {C}]";
    }

    /// <summary>A hex side (where roads go), identified by the two hexes that share it, stored sorted.</summary>
    public readonly struct Edge : IEquatable<Edge>
    {
        public readonly Hex A;
        public readonly Hex B;

        public Edge(Hex x, Hex y)
        {
            if (y.CompareTo(x) < 0)
            {
                Hex t = x;
                x = y;
                y = t;
            }
            A = x;
            B = y;
        }

        /// <summary>Side <paramref name="dir"/> (0..5) of a hex, facing that neighbor direction.</summary>
        public static Edge OfSide(Hex hex, int dir) => new Edge(hex, hex.Neighbor(dir));

        /// <summary>The two corners at either end of this edge.</summary>
        public IEnumerable<Vertex> Endpoints()
        {
            // Hexes adjacent to both A and B are exactly the two that complete a corner with this edge.
            for (int i = 0; i < 6; i++)
            {
                Hex c = A.Neighbor(i);
                if (c.DistanceTo(B) == 1) yield return new Vertex(A, B, c);
            }
        }

        public static bool operator ==(Edge a, Edge b) => a.A == b.A && a.B == b.B;
        public static bool operator !=(Edge a, Edge b) => !(a == b);
        public bool Equals(Edge other) => this == other;
        public override bool Equals(object obj) => obj is Edge e && this == e;
        public override int GetHashCode() => (A.GetHashCode() * 31) ^ B.GetHashCode();
        public override string ToString() => $"E[{A} {B}]";
    }
}
