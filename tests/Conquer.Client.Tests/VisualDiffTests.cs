using System.Collections.Generic;
using System.Linq;
using Conquer.Client.Animation;
using Conquer.Core;
using Conquer.Core.Net;
using NUnit.Framework;

namespace Conquer.Client.Tests
{
    /// <summary>
    /// The animation layer only ever sees two views of the game and works out what changed. These tests check
    /// that the diff finds the right events, and that an online client (a mirror rebuilt from a snapshot)
    /// gets the same animations without seeing anything it shouldn't.
    /// </summary>
    public class VisualDiffTests
    {
        sealed class Dice : IDice
        {
            readonly Queue<int> _rolls = new Queue<int>();
            public void Next(int roll) => _rolls.Enqueue(roll);
            public int Roll() => _rolls.Dequeue();
        }

        static Game Setup(Dice dice, int players = 3)
        {
            var g = new Game(new GameConfig { PlayerCount = players, Seed = 4, Board = new BoardConfig { Radius = 2, Seed = 9 } }, dice);
            while (g.Phase == Phase.SetupVillage || g.Phase == Phase.SetupRoad)
            {
                int p = g.CurrentPlayer;
                ActionResult r = g.Phase == Phase.SetupVillage
                    ? g.Apply(new SetupVillage(p, g.LegalSetupVertices().First()))
                    : g.Apply(new SetupRoad(p, g.LegalSetupRoadEdges().First()));
                Assert.IsTrue(r.Ok, r.Error);
            }
            return g;
        }

        static void Ok(Game g, Command c)
        {
            ActionResult r = g.Apply(c);
            Assert.IsTrue(r.Ok, r.Error);
        }

        /// <summary>How the game looks to <paramref name="viewer"/> over the wire.</summary>
        static GameView Online(Game g, int viewer) => GameView.Capture(SnapshotCodec.Decode(SnapshotCodec.Encode(g, viewer)), viewer);

        /// <summary>A number some building sits next to, on a tile the raider isn't on.</summary>
        static Tile PayingTile(Game g) => g.Board.Tiles.First(t => !t.IsWasteland && t.Hex != g.RaiderHex &&
            Enumerable.Range(0, 6).Any(i => g.Buildings.ContainsKey(Vertex.OfCorner(t.Hex, i))));

        [Test]
        public void Roll_ShowsDiceThenCardsFlyingFromThePayingTiles()
        {
            var dice = new Dice();
            Game g = Setup(dice);
            Tile tile = PayingTile(g);
            dice.Next(tile.Number);

            GameView before = GameView.Capture(g, 0);
            ResourceSet[] hands = g.Players.Select(p => p.Hand).ToArray();
            Ok(g, new RollDice(0));
            List<VisualEvent> events = VisualDiff.Between(before, GameView.Capture(g, 0));

            Assert.AreEqual(new DiceRolled(0, tile.Number), events.OfType<DiceRolled>().Single());
            List<CardsMoved> flights = events.OfType<CardsMoved>().ToList();
            Assert.IsNotEmpty(flights);
            Assert.IsTrue(flights.All(f => f.From.Kind == PlaceKind.Tile), "every produced card should come from a tile");
            foreach (CardsMoved f in flights)
            {
                Tile from = g.Board.Tiles.Single(t => t.Hex == f.From.Hex);
                Assert.AreEqual(tile.Number, from.Number);
                Assert.AreEqual(from.Resource, f.Resource);
            }
            for (int i = 0; i < g.Players.Count; i++)
                Assert.AreEqual(g.Players[i].Hand.Total - hands[i].Total, flights.Where(f => f.To.Player == i).Sum(f => f.Count));
        }

        [Test]
        public void SecondSetupVillage_PaysOutFromTheTilesAroundIt()
        {
            var g = new Game(new GameConfig { PlayerCount = 2, Seed = 4, Board = new BoardConfig { Radius = 2, Seed = 9 } });
            List<VisualEvent> last = null;
            while (g.Phase == Phase.SetupVillage || g.Phase == Phase.SetupRoad)
            {
                GameView before = GameView.Capture(g, g.CurrentPlayer);
                int p = g.CurrentPlayer;
                if (g.Phase == Phase.SetupVillage) Ok(g, new SetupVillage(p, g.LegalSetupVertices().First()));
                else Ok(g, new SetupRoad(p, g.LegalSetupRoadEdges().First()));
                List<VisualEvent> events = VisualDiff.Between(before, GameView.Capture(g, p));
                if (events.OfType<CardsMoved>().Any()) last = events;
            }

            Assert.IsNotNull(last, "the second village should hand out starting cards");
            Assert.IsTrue(last.OfType<VillagePlaced>().Any());
            Assert.IsTrue(last.OfType<CardsMoved>().All(f => f.From.Kind == PlaceKind.Tile && f.Resource.HasValue));
        }

        [Test]
        public void BuildingARoad_PopsTheRoadAndPaysTheBank()
        {
            Game g = Setup(new Dice());
            g.ForcePhase(Phase.Main);
            g.GrantResources(0, Costs.Road);
            Edge edge = g.LegalRoadEdges(0).First();

            GameView before = GameView.Capture(g, 0);
            Ok(g, new BuildRoad(0, edge));
            List<VisualEvent> events = VisualDiff.Between(before, GameView.Capture(g, 0));

            Assert.Contains(new RoadPlaced(0, edge), events);
            Assert.Contains(new CardsMoved(Place.Seat(0), Place.Bank, Resource.Timber, 1), events);
            Assert.Contains(new CardsMoved(Place.Seat(0), Place.Bank, Resource.Clay, 1), events);
        }

        [Test]
        public void BuyingAActionCard_IsRevealedOnlyToTheBuyer()
        {
            Game g = Setup(new Dice());
            g.ForcePhase(Phase.Main);
            g.GrantResources(0, Costs.ActionCard);

            GameView mine = Online(g, 0), theirs = Online(g, 1);
            Ok(g, new BuyActionCard(0));

            ActionCardDrawn forBuyer = VisualDiff.Between(mine, Online(g, 0)).OfType<ActionCardDrawn>().Single();
            ActionCardDrawn forOther = VisualDiff.Between(theirs, Online(g, 1)).OfType<ActionCardDrawn>().Single();
            Assert.IsNotNull(forBuyer.Card);
            Assert.IsNull(forOther.Card);

            // Everyone sees what was paid: the bank is public.
            Assert.Contains(new CardsMoved(Place.Seat(0), Place.Bank, Resource.Iron, 1), VisualDiff.Between(theirs, Online(g, 1)));
        }

        [Test]
        public void Steal_IsFaceUpForThePlayersInvolvedAndFaceDownForEveryoneElse()
        {
            var dice = new Dice();
            Game g = Setup(dice);
            dice.Next(7);
            Ok(g, new RollDice(0));
            Assume.That(g.Phase, Is.EqualTo(Phase.MoveRaider), "nobody should have to discard right after setup");

            // A hex next to player 1 (and not player 0) so the steal is from player 1.
            Hex target = g.LegalRaiderHexes().First(h =>
            {
                var owners = Enumerable.Range(0, 6).Select(i => Vertex.OfCorner(h, i)).Where(g.Buildings.ContainsKey).Select(v => g.Buildings[v].Owner).ToList();
                return owners.Contains(1) && !owners.Contains(0);
            });

            GameView victim = Online(g, 1), bystander = Online(g, 2), thief = Online(g, 0);
            Ok(g, new MoveRaider(0, target));
            if (g.Phase == Phase.Steal) Ok(g, new StealFrom(0, 1));

            CardsMoved seenByVictim = VisualDiff.Between(victim, Online(g, 1)).OfType<CardsMoved>().Single();
            CardsMoved seenByBystander = VisualDiff.Between(bystander, Online(g, 2)).OfType<CardsMoved>().Single();
            CardsMoved seenByThief = VisualDiff.Between(thief, Online(g, 0)).OfType<CardsMoved>().Single();

            Assert.AreEqual(Place.Seat(1), seenByBystander.From);
            Assert.AreEqual(Place.Seat(0), seenByBystander.To);
            Assert.IsNull(seenByBystander.Resource, "a bystander must not learn what was stolen");
            Assert.IsNotNull(seenByVictim.Resource);
            Assert.AreEqual(seenByVictim.Resource, seenByThief.Resource);
            Assert.Contains(new RaiderMoved(victim.Raider, target), VisualDiff.Between(victim, Online(g, 1)));
        }

        [Test]
        public void OpponentBankTrade_OnlineShowsBothWaysFromTheBankCounts()
        {
            Game g = Setup(new Dice());
            g.ForcePhase(Phase.Main);
            g.GrantResources(0, new ResourceSet(timber: 4));
            int ratio = g.GetBankRatio(0, Resource.Timber);

            GameView theirs = Online(g, 1);
            Ok(g, new BankTrade(0, Resource.Timber, Resource.Iron));
            List<CardsMoved> seen = VisualDiff.Between(theirs, Online(g, 1)).OfType<CardsMoved>().ToList();

            CollectionAssert.AreEquivalent(new[]
            {
                new CardsMoved(Place.Seat(0), Place.Bank, Resource.Timber, ratio),
                new CardsMoved(Place.Bank, Place.Seat(0), Resource.Iron, 1),
            }, seen);
        }

        [Test]
        public void ChangingHouseRules_ListsWhatChanged()
        {
            Game g = Setup(new Dice());
            GameView before = Online(g, 1);
            HouseRules rules = g.Rules.Clone();
            rules.VictoryPoints = 8;
            rules.FriendlyRaider = true;
            Ok(g, new SetHouseRules(Game.HostPlayer, rules));

            RulesChanged changed = VisualDiff.Between(before, Online(g, 1)).OfType<RulesChanged>().Single();
            CollectionAssert.AreEquivalent(new[] { "Victory points: 10 → 8", "Friendly raider: off → on" }, changed.Changes);
        }

        [Test]
        public void EndingATurn_AnnouncesTheNextPlayer()
        {
            Game g = Setup(new Dice());
            g.ForcePhase(Phase.Main);
            GameView before = GameView.Capture(g, 0);
            Ok(g, new EndTurn(0));
            Assert.Contains(new TurnStarted(1, g.Turn), VisualDiff.Between(before, GameView.Capture(g, 1)));
        }

        [Test]
        public void NothingChanged_NothingAnimates()
        {
            Game g = Setup(new Dice());
            Assert.IsEmpty(VisualDiff.Between(GameView.Capture(g, 0), GameView.Capture(g, 0)));
        }
    }
}
