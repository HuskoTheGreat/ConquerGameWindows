using System.Linq;
using NUnit.Framework;

namespace Conquer.Core.Tests
{
    public class HexGridTests
    {
        [Test]
        public void Distance_BetweenNeighborsIsOne()
        {
            foreach (Hex n in Hex.Zero.Neighbors()) Assert.AreEqual(1, Hex.Zero.DistanceTo(n));
        }

        [Test]
        public void Distance_KnownValue()
        {
            Assert.AreEqual(3, new Hex(0, 0).DistanceTo(new Hex(2, 1)));
            Assert.AreEqual(4, new Hex(-2, 0).DistanceTo(new Hex(2, 0)));
        }

        [TestCase(0, 1)]
        [TestCase(1, 7)]
        [TestCase(2, 19)]
        [TestCase(3, 37)]
        [TestCase(5, 91)]
        public void Spiral_HasExpectedCountAndNoDuplicates(int radius, int expected)
        {
            var hexes = Hex.Spiral(Hex.Zero, radius).ToList();
            Assert.AreEqual(expected, hexes.Count);
            Assert.AreEqual(expected, hexes.Distinct().Count());
            Assert.AreEqual(expected, Hex.CountForRadius(radius));
            Assert.IsTrue(hexes.All(h => Hex.Zero.DistanceTo(h) <= radius));
        }

        [Test]
        public void Ring_IsContiguousAndAtExactRadius()
        {
            var ring = Hex.Ring(Hex.Zero, 3).ToList();
            Assert.AreEqual(18, ring.Count);
            Assert.IsTrue(ring.All(h => h.DistanceTo(Hex.Zero) == 3));
            for (int i = 0; i < ring.Count; i++)
                Assert.AreEqual(1, ring[i].DistanceTo(ring[(i + 1) % ring.Count]));
        }

        [Test]
        public void Vertex_IsCanonicalFromEveryHexItTouches()
        {
            var h = new Hex(1, -1);
            for (int c = 0; c < 6; c++)
            {
                Vertex v = Vertex.OfCorner(h, c);
                foreach (Hex other in v.Hexes())
                {
                    bool found = Enumerable.Range(0, 6).Any(k => Vertex.OfCorner(other, k) == v);
                    Assert.IsTrue(found, $"{v} not reachable from {other}");
                }
            }
        }

        [Test]
        public void Edge_HasTwoEndpoints_AndEachVertexHasThreeEdgesAndNeighbors()
        {
            Edge e = Edge.OfSide(Hex.Zero, 2);
            Assert.AreEqual(2, e.Endpoints().Distinct().Count());

            Vertex v = Vertex.OfCorner(Hex.Zero, 0);
            Assert.AreEqual(3, v.Edges().Count());
            Assert.AreEqual(3, v.Neighbors().Distinct().Count());
            Assert.IsTrue(v.Neighbors().All(n => n != v));
        }

        [Test]
        public void Layout_RoundTripsHexCenters()
        {
            foreach (Hex h in Hex.Spiral(Hex.Zero, 4))
            {
                var p = HexLayout.ToPlane(h, 2.5f);
                Assert.AreEqual(h, HexLayout.FromPlane(p.X, p.Y, 2.5f));
            }
        }
    }
}
