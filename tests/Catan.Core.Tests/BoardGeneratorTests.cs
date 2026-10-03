using System;
using System.Linq;
using NUnit.Framework;

namespace Catan.Core.Tests
{
    public class BoardGeneratorTests
    {
        static Board Make(int radius, int seed) =>
            BoardGenerator.Generate(new BoardConfig { Radius = radius, Seed = seed });

        [Test]
        public void ClassicBoard_MatchesRealCatanCounts()
        {
            Board b = Make(2, 1);
            Assert.AreEqual(19, b.Tiles.Count);
            Assert.AreEqual(54, b.Vertices.Count);
            Assert.AreEqual(72, b.Edges.Count);
            Assert.AreEqual(9, b.Ports.Count);
            Assert.AreEqual(4, b.Ports.Count(p => p.IsGeneric));

            Assert.AreEqual(1, b.Tiles.Count(t => t.IsDesert));
            Assert.AreEqual(4, b.Tiles.Count(t => t.Resource == Resource.Wood));
            Assert.AreEqual(4, b.Tiles.Count(t => t.Resource == Resource.Sheep));
            Assert.AreEqual(4, b.Tiles.Count(t => t.Resource == Resource.Wheat));
            Assert.AreEqual(3, b.Tiles.Count(t => t.Resource == Resource.Brick));
            Assert.AreEqual(3, b.Tiles.Count(t => t.Resource == Resource.Ore));

            var tokens = b.Tiles.Where(t => !t.IsDesert).Select(t => t.Number).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(new[] { 2, 3, 3, 4, 4, 5, 5, 6, 6, 8, 8, 9, 9, 10, 10, 11, 11, 12 }, tokens);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(5)]
        [TestCase(8)]
        public void Topology_ScalesWithRadius(int radius)
        {
            Board b = Make(radius, 7);
            int hexes = Hex.CountForRadius(radius);
            Assert.AreEqual(hexes, b.Tiles.Count);
            Assert.AreEqual(6 * (radius + 1) * (radius + 1), b.Vertices.Count);
            Assert.AreEqual(b.Vertices.Count + hexes - 1, b.Edges.Count); // Euler's formula for a planar graph
            Assert.IsTrue(b.Tiles.All(t => t.IsDesert == (t.Number == 0)));
            Assert.IsTrue(b.Tiles.All(t => t.Number != 7));
        }

        [Test]
        public void Adjacency_IsSymmetric_AndDegreesAreTwoOrThree()
        {
            Board b = Make(3, 42);
            foreach (Vertex v in b.Vertices)
            {
                var adj = b.AdjacentVertices(v).ToList();
                Assert.IsTrue(adj.Count == 2 || adj.Count == 3, $"{v} has {adj.Count} neighbors");
                foreach (Vertex n in adj) Assert.IsTrue(b.AdjacentVertices(n).Contains(v));
                Assert.IsTrue(b.TilesAround(v).Any());
            }
        }

        [Test]
        public void NoAdjacentSixesOrEights()
        {
            for (int seed = 0; seed < 25; seed++)
            {
                foreach (int radius in new[] { 2, 3, 4 })
                {
                    Board b = Make(radius, seed);
                    foreach (Tile t in b.Tiles.Where(t => t.Number == 6 || t.Number == 8))
                    {
                        foreach (Hex n in t.Hex.Neighbors())
                        {
                            if (b.TryGetTile(n, out Tile other))
                                Assert.IsFalse(other.Number == 6 || other.Number == 8,
                                    $"seed {seed} radius {radius}: {t} next to {other}");
                        }
                    }
                }
            }
        }

        [Test]
        public void Ports_SitOnCoastAndNeverShareCorners()
        {
            foreach (int radius in new[] { 1, 2, 3, 6 })
            {
                Board b = Make(radius, 3);
                var corners = b.Ports.SelectMany(p => p.Edge.Endpoints()).ToList();
                Assert.AreEqual(corners.Count, corners.Distinct().Count());
                foreach (Port p in b.Ports)
                    Assert.AreEqual(1, new[] { p.Edge.A, p.Edge.B }.Count(b.IsLand), "port edge must border exactly one land hex");
            }
        }

        [Test]
        public void SameSeedIsIdentical_DifferentSeedDiffers()
        {
            string Dump(Board b) => string.Join("|", b.Tiles.OrderBy(t => t.Hex).Select(t => t.ToString()));

            Assert.AreEqual(Dump(Make(3, 99)), Dump(Make(3, 99)));
            Assert.AreNotEqual(Dump(Make(3, 99)), Dump(Make(3, 100)));
        }

        [Test]
        public void InvalidRadius_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Make(0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Make(BoardGenerator.MaxRadius + 1, 1));
        }
    }
}
