using System;
using System.Linq;
using Conquer.Core.Net;
using NUnit.Framework;

namespace Conquer.Core.Tests
{
    public class BoardDraftTests
    {
        static Resource Land(BoardDraft d, Hex h) => d.ResourceAt(h);

        [Test]
        public void Random_MatchesTheGeneratedBoard()
        {
            Board generated = BoardGenerator.Generate(new BoardConfig { Radius = 2, Seed = 42 });
            Board built = BoardDraft.Random(2, 42).Build();
            CollectionAssert.AreEquivalent(generated.Tiles.Select(t => t.ToString()), built.Tiles.Select(t => t.ToString()));
            Assert.AreEqual(generated.Ports.Count, built.Ports.Count);
        }

        [Test]
        public void SetResource_ToWastelandAndBack_RestoresTheNumber()
        {
            BoardDraft d = BoardDraft.Random(2, 1);
            Hex h = d.Hexes.First(x => d.ResourceAt(x) != Resource.Wasteland);
            int number = d.NumberAt(h);

            d.SetResource(h, Resource.Wasteland);
            Assert.AreEqual(0, d.NumberAt(h));
            d.SetResource(h, ResourceSet.Types[0]);
            Assert.AreEqual(number, d.NumberAt(h));
            Assert.DoesNotThrow(() => d.Build());
        }

        [Test]
        public void SetResource_OnTheWasteland_GivesItANumber()
        {
            BoardDraft d = BoardDraft.Random(2, 3);
            Hex waste = d.Hexes.First(x => d.ResourceAt(x) == Resource.Wasteland);
            d.SetResource(waste, ResourceSet.Types[2]);
            CollectionAssert.Contains(BoardDraft.Numbers, d.NumberAt(waste));
            Board b = d.Build();
            Assert.AreEqual(Hex.Zero, b.RaiderStart, "no wasteland left, so the raider starts in the middle");
            Assert.IsTrue(d.Notes().Any(n => n.Contains("No wasteland")));
        }

        [Test]
        public void SetNumber_RejectsSevenAndIgnoresTheWasteland()
        {
            BoardDraft d = BoardDraft.Random(2, 5);
            Hex land = d.Hexes.First(x => d.ResourceAt(x) != Resource.Wasteland);
            Hex waste = d.Hexes.First(x => d.ResourceAt(x) == Resource.Wasteland);
            Assert.Throws<ArgumentOutOfRangeException>(() => d.SetNumber(land, 7));
            d.SetNumber(land, 12);
            d.SetNumber(waste, 6);
            Assert.AreEqual(12, d.NumberAt(land));
            Assert.AreEqual(0, d.NumberAt(waste));
        }

        [Test]
        public void Swap_TradesTilesAndNumbers()
        {
            BoardDraft d = BoardDraft.Random(2, 8);
            Hex a = d.Hexes[0], b = d.Hexes[5];
            (Resource ra, int na, Resource rb, int nb) = (d.ResourceAt(a), d.NumberAt(a), d.ResourceAt(b), d.NumberAt(b));
            d.Swap(a, b);
            Assert.AreEqual((rb, nb, ra, na), (d.ResourceAt(a), d.NumberAt(a), d.ResourceAt(b), d.NumberAt(b)));
        }

        [Test]
        public void Shuffles_KeepTheSameTilesAndNumbers_AndStayPlayable()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                BoardDraft d = BoardDraft.Random(3, seed);
                var kinds = BoardDraft.Kinds.Select(d.Count).ToArray();
                var numbers = d.Hexes.Select(d.NumberAt).Where(n => n != 0).OrderBy(n => n).ToArray();

                d.ShuffleTiles(seed + 1);
                d.ShuffleNumbers(seed + 2);
                d.ShuffleHarbors(seed + 3);

                CollectionAssert.AreEqual(kinds, BoardDraft.Kinds.Select(d.Count).ToArray());
                CollectionAssert.AreEqual(numbers, d.Hexes.Select(d.NumberAt).Where(n => n != 0).OrderBy(n => n).ToArray());
                Assert.IsTrue(d.Hexes.All(h => (d.ResourceAt(h) == Resource.Wasteland) == (d.NumberAt(h) == 0)));
                Assert.DoesNotThrow(() => new Game(d.Build(), new GameConfig { PlayerCount = 4 }));
            }
        }

        [Test]
        public void AllOneResource_IsPlayable()
        {
            BoardDraft d = BoardDraft.Random(2, 9);
            foreach (Hex h in d.Hexes) d.SetResource(h, ResourceSet.Types[4]);
            Board b = d.Build();
            Assert.IsTrue(b.Tiles.All(t => t.Resource == ResourceSet.Types[4] && t.Number != 0));
            Assert.IsTrue(d.Notes().Count > 1);
        }

        // ---- Starting an online game on an arranged board ------------------------------------------

        static GameSession Lobby()
        {
            var s = new GameSession(100, "Host", 4, "", () => 0);
            Assert.IsTrue(s.Join(1, new JoinRequest { Name = "Guest" }.Encode()).Ok);
            return s;
        }

        [Test]
        public void Start_WithArrangedBoard_RoundTripsAndIsUsed()
        {
            BoardDraft d = BoardDraft.Random(3, 11);
            Hex center = Hex.Zero;
            d.SetResource(center, ResourceSet.Types[1]);
            d.SetNumber(center, 2);
            Board board = d.Build();

            byte[] frame = Protocol.EncodeStart(2, new HouseRules { VictoryPoints = 8 }, board);
            StartSettings settings = Protocol.DecodeStart(frame.Skip(1).ToArray());
            Assert.AreEqual(3, settings.Radius, "the board decides the size");
            Assert.IsNotNull(settings.Board);

            GameSession s = Lobby();
            Assert.IsNull(s.Start(100, settings));
            Assert.IsTrue(s.Game.Board.TryGetTile(center, out Tile t));
            Assert.AreEqual((ResourceSet.Types[1], 2), (t.Resource, t.Number));
            Assert.AreEqual(8, s.Game.Rules.VictoryPoints);

            // Everyone sees the same board in their snapshots.
            Game m = SnapshotCodec.Decode(s.SnapshotFor(1));
            Assert.IsTrue(m.Board.TryGetTile(center, out Tile seen));
            Assert.AreEqual((t.Resource, t.Number), (seen.Resource, seen.Number));
        }

        [Test]
        public void Start_WithoutBoard_IsUnchanged()
        {
            byte[] frame = Protocol.EncodeStart(2, new HouseRules());
            StartSettings settings = Protocol.DecodeStart(frame.Skip(1).ToArray());
            Assert.IsNull(settings.Board);
            Assert.AreEqual(2, settings.Radius);
        }

        [Test]
        public void Start_RejectsBadBoards()
        {
            BoardDraft d = BoardDraft.Random(2, 1);
            d.SetResource(Hex.Zero, ResourceSet.Types[0]);
            d.SetNumber(Hex.Zero, 5);
            byte[] good = Protocol.EncodeStart(2, new HouseRules(), d.Build()).Skip(1).ToArray();

            // Mismatched radius byte.
            byte[] wrongSize = (byte[])good.Clone();
            wrongSize[0] = 3;
            Assert.Throws<WireException>(() => Protocol.DecodeStart(wrongSize));

            // Truncated board.
            Assert.Throws<WireException>(() => Protocol.DecodeStart(good.Take(good.Length - 3).ToArray()));

            // A 7 on a tile.
            int rulesLength = Protocol.EncodeStart(2, new HouseRules()).Length - 1;
            byte[] seven = (byte[])good.Clone();
            int firstNumber = rulesLength + 1 + 2 + 2 + 1; // radius, tile count, the center's hex and resource
            Assert.AreEqual(5, seven[firstNumber]);
            seven[firstNumber] = 7;
            Assert.Throws<WireException>(() => Protocol.DecodeStart(seven));

            // Session refuses a board whose size doesn't match the settings.
            GameSession s = Lobby();
            Assert.IsNotNull(s.Start(100, new StartSettings { Radius = 2, Board = BoardDraft.Random(3, 1).Build() }));
        }
    }
}
