using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Catan.Core.Tests.GameTestKit;

namespace Catan.Core.Tests
{
    public class GameRulesTests
    {
        // ---- Setup ---------------------------------------------------------------------------------

        [Test]
        public void Setup_IsSnakeOrder_ThenPlayerOneRolls()
        {
            Game g = New(players: 3);
            var order = new List<int>();
            while (g.Phase == Phase.SetupSettlement || g.Phase == Phase.SetupRoad)
            {
                if (g.Phase == Phase.SetupSettlement) order.Add(g.CurrentPlayer);
                int p = g.CurrentPlayer;
                if (g.Phase == Phase.SetupSettlement) Ok(g, new SetupSettlement(p, g.LegalSetupVertices().First()));
                else Ok(g, new SetupRoad(p, g.LegalSetupRoadEdges().First()));
            }

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 2, 1, 0 }, order);
            Assert.AreEqual(Phase.Roll, g.Phase);
            Assert.AreEqual(0, g.CurrentPlayer);
            Assert.AreEqual(1, g.Turn);
        }

        [Test]
        public void Setup_EnforcesDistanceRuleAndRoadPlacement()
        {
            Game g = New();
            Vertex v = g.LegalSetupVertices().First();
            Fails(g, new SetupSettlement(1, v)); // wrong player
            Ok(g, new SetupSettlement(0, v));

            Fails(g, new SetupRoad(0, g.Board.Edges.First(e => !e.Endpoints().Contains(v)))); // not touching
            Edge ok = g.LegalSetupRoadEdges().First();
            Ok(g, new SetupRoad(0, ok));

            Vertex adjacent = g.Board.AdjacentVertices(v).First();
            Fails(g, new SetupSettlement(1, adjacent)); // too close
            Fails(g, new SetupSettlement(1, v));        // occupied
        }

        [Test]
        public void Setup_SecondSettlementPaysOutAdjacentTiles()
        {
            Game g = New(players: 2);
            int placed = 0;
            while (g.Phase == Phase.SetupSettlement || g.Phase == Phase.SetupRoad)
            {
                int p = g.CurrentPlayer;
                if (g.Phase == Phase.SetupSettlement)
                {
                    Vertex v = g.LegalSetupVertices().First();
                    int before = g.Players[p].Hand.Total;
                    Ok(g, new SetupSettlement(p, v));
                    int expected = placed >= 2 ? g.Board.TilesAround(v).Count(t => !t.IsDesert) : 0;
                    Assert.AreEqual(expected, g.Players[p].Hand.Total - before, $"settlement #{placed}");
                    placed++;
                }
                else Ok(g, new SetupRoad(p, g.LegalSetupRoadEdges().First()));
            }
        }

        // ---- Rolling and production ----------------------------------------------------------------

        [Test]
        public void Roll_ProducesResourcesForSettlementsAndCities()
        {
            var dice = new FixedDice();
            Game g = New(players: 3, dice: dice);
            RunSetup(g);

            Vertex mine = g.Players[0].Settlements.First();
            int n = g.Board.TilesAround(mine).First(t => !t.IsDesert).Number;

            var expected = g.Players.Select(p => ResourceSet.Empty).ToArray();
            foreach (Tile t in g.Board.Tiles.Where(t => t.Number == n))
            {
                for (int i = 0; i < 6; i++)
                {
                    if (g.Buildings.TryGetValue(Vertex.OfCorner(t.Hex, i), out Building b))
                        expected[b.Owner] = expected[b.Owner].With(t.Resource, b.IsCity ? 2 : 1);
                }
            }
            var before = g.Players.Select(p => p.Hand).ToArray();

            dice.Enqueue(n);
            Ok(g, new RollDice(0));

            Assert.AreEqual(Phase.Main, g.Phase);
            for (int i = 0; i < 3; i++) Assert.AreEqual(before[i] + expected[i], g.Players[i].Hand, $"player {i}");
            Assert.IsFalse(expected[0].IsEmpty);
        }

        [Test]
        public void Roll_OnlyCurrentPlayerInRollPhase()
        {
            var dice = new FixedDice();
            Game g = New(dice: dice);
            RunSetup(g);
            dice.Enqueue(6);
            Fails(g, new RollDice(1));
            Ok(g, new RollDice(0));
            Fails(g, new RollDice(0)); // already rolled
        }

        [Test]
        public void Roll_BankShortage_LoneClaimantGetsRemainder_SharedClaimsGetNothing()
        {
            var dice = new FixedDice();
            Game g = New(players: 2, dice: dice);
            RunSetup(g);

            Vertex mine = g.Players[0].Settlements.First();
            Tile tile = g.Board.TilesAround(mine).First(t => !t.IsDesert);
            Resource r = tile.Resource;

            // Drain the bank of that resource down to 1 by handing it to player 1 (who doesn't touch this tile).
            int drain = g.Bank[r] - 1;
            g.GrantResources(1, ResourceSet.Of(r, drain));
            int before = g.Players[0].Hand[r];
            int claimed = g.Board.Tiles.Where(t => t.Number == tile.Number && t.Resource == r)
                .Sum(t => Enumerable.Range(0, 6).Count(i => g.Buildings.TryGetValue(Vertex.OfCorner(t.Hex, i), out Building b) && b.Owner == 0));

            dice.Enqueue(tile.Number);
            Ok(g, new RollDice(0));

            // Player 1 may also claim; if so, nobody gets it. Otherwise player 0 gets exactly the 1 left.
            bool p1Claims = g.Board.Tiles.Where(t => t.Number == tile.Number && t.Resource == r)
                .Any(t => Enumerable.Range(0, 6).Any(i => g.Buildings.TryGetValue(Vertex.OfCorner(t.Hex, i), out Building b) && b.Owner == 1));
            if (claimed > 1 || p1Claims)
                Assert.AreEqual(before + (p1Claims ? 0 : 1), g.Players[0].Hand[r]);
            else
                Assert.AreEqual(before + 1, g.Players[0].Hand[r]);
        }

        // ---- Seven, robber, discard ----------------------------------------------------------------

        [Test]
        public void Seven_ForcesDiscardThenRobberThenSteal()
        {
            var dice = new FixedDice();
            Game g = New(players: 3, dice: dice);
            RunSetup(g);
            g.GrantResources(1, new ResourceSet(wood: 4, brick: 4, sheep: 2)); // 10 cards plus setup hand
            int owe = g.Players[1].Hand.Total / 2;

            dice.Enqueue(7);
            Ok(g, new RollDice(0));
            Assert.AreEqual(Phase.Discard, g.Phase);
            Assert.AreEqual(owe, g.PendingDiscards[1]);

            Fails(g, new DiscardCards(1, ResourceSet.Of(Resource.Wood, owe + 1)));
            Fails(g, new DiscardCards(0, ResourceSet.Of(Resource.Wood, 1)));       // not required to discard
            Fails(g, new MoveRobber(0, g.Board.Tiles.First(t => t.Hex != g.RobberHex).Hex)); // wrong phase

            // Discard `owe` cards from what they hold.
            ResourceSet toDiscard = ResourceSet.Empty;
            ResourceSet hand = g.Players[1].Hand;
            foreach (Resource r in ResourceSet.Types)
            {
                while (hand[r] > 0 && toDiscard.Total < owe)
                {
                    toDiscard = toDiscard.With(r, 1);
                    hand = hand.With(r, -1);
                }
            }
            ResourceSet bankBefore = g.Bank;
            Ok(g, new DiscardCards(1, toDiscard));
            Assert.AreEqual(bankBefore + toDiscard, g.Bank);
            Assert.AreEqual(Phase.MoveRobber, g.Phase);

            Fails(g, new MoveRobber(0, g.RobberHex));
            Hex target = g.Board.Tiles.First(t => t.Hex != g.RobberHex && g.Board.TilesAround(Vertex.OfCorner(t.Hex, 0)).Any()).Hex;
            Ok(g, new MoveRobber(0, target));
            Assert.AreEqual(target, g.RobberHex);

            if (g.Phase == Phase.Steal) Ok(g, new StealFrom(0, g.StealCandidates[0]));
            Assert.AreEqual(Phase.Main, g.Phase);
        }

        [Test]
        public void Robber_BlocksProductionOnItsHex()
        {
            var dice = new FixedDice();
            Game g = New(players: 2, dice: dice);
            RunSetup(g);

            Vertex mine = g.Players[0].Settlements.First();
            Tile tile = g.Board.TilesAround(mine).First(t => !t.IsDesert);
            g.ForcePhase(Phase.MoveRobber);
            Ok(g, new MoveRobber(0, tile.Hex));
            if (g.Phase == Phase.Steal) Ok(g, new StealFrom(0, g.StealCandidates[0]));
            Assert.AreEqual(tile.Hex, g.RobberHex);

            g.ForcePhase(Phase.Roll);
            var before = Sum(g);
            // Roll that tile's number: only other tiles with the same number can pay out.
            dice.Enqueue(tile.Number);
            Ok(g, new RollDice(0));
            int blockedPayout = Enumerable.Range(0, 6).Count(i => g.Buildings.ContainsKey(Vertex.OfCorner(tile.Hex, i)));
            Assert.Greater(blockedPayout, 0);
            int paid = (Sum(g) - before).Total;
            int unblocked = g.Board.Tiles.Where(t => t.Number == tile.Number && t.Hex != tile.Hex)
                .Sum(t => Enumerable.Range(0, 6).Sum(i => g.Buildings.TryGetValue(Vertex.OfCorner(t.Hex, i), out Building b) ? (b.IsCity ? 2 : 1) : 0));
            Assert.AreEqual(unblocked, paid);
        }

        // ---- Building ------------------------------------------------------------------------------

        static Game ReadyToBuild()
        {
            var dice = new FixedDice();
            Game g = New(players: 2, dice: dice);
            RunSetup(g);
            g.ForcePhase(Phase.Main);
            return g;
        }

        [Test]
        public void BuildRoad_CostsResources_AndMustConnect()
        {
            Game g = ReadyToBuild();
            Fails(g, new BuildRoad(0, g.LegalRoadEdges(0).First())); // broke

            g.GrantResources(0, Costs.Road);
            Edge far = g.Board.Edges.First(e => !g.RoadOwners.ContainsKey(e) && !g.LegalRoadEdges(0).Contains(e));
            Fails(g, new BuildRoad(0, far));

            ResourceSet hand = g.Players[0].Hand;
            ResourceSet bank = g.Bank;
            Ok(g, new BuildRoad(0, g.LegalRoadEdges(0).First()));
            Assert.AreEqual(hand - Costs.Road, g.Players[0].Hand);
            Assert.AreEqual(bank + Costs.Road, g.Bank);
            Assert.AreEqual(3, g.Players[0].Roads.Count);
        }

        [Test]
        public void BuildCity_UpgradesSettlement_AndReturnsPiece()
        {
            Game g = ReadyToBuild();
            Vertex v = g.Players[0].Settlements.First();
            Fails(g, new BuildCity(0, v)); // can't afford
            g.GrantResources(0, Costs.City);
            Fails(g, new BuildCity(0, g.Players[1].Settlements.First())); // not yours

            int settlementsLeft = g.Players[0].SettlementsLeft;
            int vp = g.VictoryPoints(0);
            Ok(g, new BuildCity(0, v));
            Assert.IsTrue(g.Buildings[v].IsCity);
            Assert.AreEqual(settlementsLeft + 1, g.Players[0].SettlementsLeft);
            Assert.AreEqual(vp + 1, g.VictoryPoints(0));
        }

        [Test]
        public void BuildSettlement_NeedsRoadDistanceAndPayment()
        {
            Game g = ReadyToBuild();
            g.GrantResources(0, Costs.Settlement + Costs.Road + Costs.Road);

            Vertex noRoad = g.Board.Vertices.First(v => g.IsSettlementSpotFree(v) && !g.LegalSettlementVertices(0).Contains(v));
            Fails(g, new BuildSettlement(0, noRoad));

            // Extend two roads outward until a legal settlement spot appears (distance rule satisfied).
            for (int i = 0; i < 2; i++) Ok(g, new BuildRoad(0, g.LegalRoadEdges(0).First(e => e.Endpoints().Any(g.IsSettlementSpotFree))));
            var spots = g.LegalSettlementVertices(0).ToList();
            if (spots.Count == 0) Assert.Inconclusive("Layout produced no spot two roads out.");
            Ok(g, new BuildSettlement(0, spots[0]));
            Assert.AreEqual(3, g.Players[0].Settlements.Count);
        }

        // ---- Trading -------------------------------------------------------------------------------

        [Test]
        public void BankTrade_UsesPortRatios()
        {
            Game g = New();
            Port generic = g.Board.Ports.First(p => p.IsGeneric);
            Port special = g.Board.Ports.First(p => !p.IsGeneric);

            Assert.AreEqual(4, g.GetBankRatio(0, Resource.Wood));
            g.ForceSettlement(0, generic.Edge.Endpoints().First());
            Assert.AreEqual(3, g.GetBankRatio(0, Resource.Wood));
            g.ForceSettlement(1, special.Edge.Endpoints().First());
            Assert.AreEqual(2, g.GetBankRatio(1, special.Resource));
            Assert.AreEqual(4, g.GetBankRatio(1, ResourceSet.Types.First(r => r != special.Resource)));
        }

        [Test]
        public void BankTrade_ExchangesAtRatio()
        {
            Game g = ReadyToBuild();
            Resource give = Resource.Wood, get = Resource.Ore;
            int ratio = g.GetBankRatio(0, give);
            g.GrantResources(0, ResourceSet.Of(give, ratio));
            int ore = g.Players[0].Hand[get];
            int wood = g.Players[0].Hand[give];

            Fails(g, new BankTrade(0, give, give));
            Ok(g, new BankTrade(0, give, get));
            Assert.AreEqual(wood - ratio, g.Players[0].Hand[give]);
            Assert.AreEqual(ore + 1, g.Players[0].Hand[get]);
            Fails(g, new BankTrade(0, Resource.Brick, Resource.Sheep)); // not enough
        }

        [Test]
        public void PlayerTrade_ProposeAcceptCancel()
        {
            Game g = ReadyToBuild();
            g.GrantResources(0, new ResourceSet(wood: 2));
            g.GrantResources(1, new ResourceSet(ore: 1));

            Fails(g, new ProposeTrade(0, new ResourceSet(wood: 5), new ResourceSet(ore: 1))); // can't offer what you lack
            Fails(g, new AcceptTrade(1)); // nothing open
            Ok(g, new ProposeTrade(0, new ResourceSet(wood: 2), new ResourceSet(ore: 1)));
            Fails(g, new AcceptTrade(0)); // own offer

            var total = Sum(g);
            Ok(g, new AcceptTrade(1));
            Assert.AreEqual(total, Sum(g));
            Assert.IsNull(g.PendingTrade);

            Ok(g, new ProposeTrade(0, new ResourceSet(ore: 1), new ResourceSet(wood: 1)));
            Fails(g, new CancelTrade(1));
            Ok(g, new CancelTrade(0));
            Assert.IsNull(g.PendingTrade);
        }

        // ---- Development cards ---------------------------------------------------------------------

        [Test]
        public void DevCard_BoughtThisTurnIsNotPlayableUntilNextTurn()
        {
            Game g = ReadyToBuild();
            g.GrantResources(0, Costs.DevCard);
            int deck = g.DevDeckCount;
            Ok(g, new BuyDevCard(0));
            Assert.AreEqual(deck - 1, g.DevDeckCount);
            Assert.AreEqual(1, g.Players[0].DevCardCount);
            foreach (DevCard c in System.Enum.GetValues(typeof(DevCard)))
                Assert.AreEqual(0, g.Players[0].DevCardsUsable(c));

            Fails(g, new PlayKnight(0));
            Fails(g, new BuyDevCard(0)); // broke
        }

        [Test]
        public void Knight_MovesRobber_AndOnlyOneDevCardPerTurn()
        {
            Game g = ReadyToBuild();
            g.GrantDevCard(0, DevCard.Knight);
            g.GrantDevCard(0, DevCard.Knight);

            Ok(g, new PlayKnight(0));
            Assert.AreEqual(Phase.MoveRobber, g.Phase);
            Ok(g, new MoveRobber(0, g.Board.Tiles.First(t => t.Hex != g.RobberHex).Hex));
            if (g.Phase == Phase.Steal) Ok(g, new StealFrom(0, g.StealCandidates[0]));
            Assert.AreEqual(Phase.Main, g.Phase);
            Assert.AreEqual(1, g.Players[0].KnightsPlayed);

            Assert.IsTrue(g.DevCardPlayedThisTurn);
            Fails(g, new PlayKnight(0)); // one dev card per turn
        }

        [Test]
        public void Knight_CanBePlayedBeforeRolling_ThenReturnsToRoll()
        {
            Game g = New(players: 2);
            RunSetup(g);
            Assert.AreEqual(Phase.Roll, g.Phase);
            g.GrantDevCard(0, DevCard.Knight);

            Ok(g, new PlayKnight(0));
            Ok(g, new MoveRobber(0, g.Board.Tiles.First(t => t.Hex != g.RobberHex).Hex));
            if (g.Phase == Phase.Steal) Ok(g, new StealFrom(0, g.StealCandidates[0]));
            Assert.AreEqual(Phase.Roll, g.Phase);
        }

        [Test]
        public void Knights_AwardLargestArmy_WhenRuleAllowsMultiplePerTurn()
        {
            var rules = new HouseRules { OneDevCardPerTurn = false };
            Game g = New(players: 2, rules: rules);
            RunSetup(g);
            g.ForcePhase(Phase.Main);
            for (int i = 0; i < 3; i++) g.GrantDevCard(0, DevCard.Knight);

            int vp = g.VictoryPoints(0);
            for (int i = 0; i < 3; i++)
            {
                Ok(g, new PlayKnight(0));
                Ok(g, new MoveRobber(0, g.LegalRobberHexes().First()));
                if (g.Phase == Phase.Steal) Ok(g, new StealFrom(0, g.StealCandidates[0]));
            }
            Assert.AreEqual(0, g.LargestArmyHolder);
            Assert.AreEqual(vp + 2, g.VictoryPoints(0));
        }

        [Test]
        public void Monopoly_TakesEverySingleCopyFromOthers()
        {
            Game g = ReadyToBuild();
            g.GrantDevCard(0, DevCard.Monopoly);
            g.GrantResources(1, ResourceSet.Of(Resource.Ore, 3));
            int mine = g.Players[0].Hand[Resource.Ore];
            Ok(g, new PlayMonopoly(0, Resource.Ore));
            Assert.AreEqual(0, g.Players[1].Hand[Resource.Ore]);
            Assert.AreEqual(mine + 3, g.Players[0].Hand[Resource.Ore]);
        }

        [Test]
        public void YearOfPlenty_TakesFromBank()
        {
            Game g = ReadyToBuild();
            g.GrantDevCard(0, DevCard.YearOfPlenty);
            ResourceSet bank = g.Bank;
            ResourceSet hand = g.Players[0].Hand;
            Ok(g, new PlayYearOfPlenty(0, Resource.Ore, Resource.Ore));
            Assert.AreEqual(hand + ResourceSet.Of(Resource.Ore, 2), g.Players[0].Hand);
            Assert.AreEqual(bank - ResourceSet.Of(Resource.Ore, 2), g.Bank);
        }

        [Test]
        public void RoadBuilding_GivesTwoFreeRoads()
        {
            Game g = ReadyToBuild();
            g.GrantDevCard(0, DevCard.RoadBuilding);
            ResourceSet hand = g.Players[0].Hand;
            Ok(g, new PlayRoadBuilding(0));
            Assert.AreEqual(Phase.RoadBuilding, g.Phase);
            Ok(g, new BuildRoad(0, g.LegalRoadEdges(0).First()));
            Assert.AreEqual(Phase.RoadBuilding, g.Phase);
            Ok(g, new BuildRoad(0, g.LegalRoadEdges(0).First()));
            Assert.AreEqual(Phase.Main, g.Phase);
            Assert.AreEqual(hand, g.Players[0].Hand);
            Assert.AreEqual(4, g.Players[0].Roads.Count);
        }

        // ---- Awards and winning --------------------------------------------------------------------

        static List<Vertex> WalkPath(Game g, int edges, out List<Edge> pathEdges)
        {
            var path = new List<Vertex> { g.Board.Vertices.First() };
            pathEdges = new List<Edge>();
            for (int i = 0; i < edges; i++)
            {
                Vertex cur = path[path.Count - 1];
                Vertex next = g.Board.AdjacentVertices(cur).First(n => !path.Contains(n));
                pathEdges.Add(g.Board.EdgesOf(cur).First(e => e.Endpoints().Contains(next)));
                path.Add(next);
            }
            return path;
        }

        [Test]
        public void LongestRoad_AwardedAtFive_AndBrokenByOpponentSettlement()
        {
            Game g = New(radius: 3);
            var path = WalkPath(g, 5, out var edges);
            foreach (Edge e in edges) g.ForceRoad(0, e);
            g.ForceRefresh();
            Assert.AreEqual(5, g.Players[0].LongestRoad);
            Assert.AreEqual(0, g.LongestRoadHolder);

            g.ForceSettlement(1, path[2]);
            g.ForceRefresh();
            Assert.AreEqual(3, g.Players[0].LongestRoad);
            Assert.AreEqual(-1, g.LongestRoadHolder);
        }

        [Test]
        public void LongestRoad_FourIsNotEnough()
        {
            Game g = New(radius: 3);
            WalkPath(g, 4, out var edges);
            foreach (Edge e in edges) g.ForceRoad(0, e);
            g.ForceRefresh();
            Assert.AreEqual(4, g.Players[0].LongestRoad);
            Assert.AreEqual(-1, g.LongestRoadHolder);
        }

        [Test]
        public void ReachingVictoryPoints_OnYourTurn_EndsTheGame()
        {
            Game g = New(players: 2, radius: 3);
            var verts = g.Board.Vertices.Take(5).ToList();
            for (int i = 0; i < 4; i++) g.ForceCity(0, verts[i]);
            g.ForceSettlement(0, verts[4]);
            g.GrantDevCard(0, DevCard.VictoryPoint);
            g.ForcePhase(Phase.Main);
            g.GrantResources(0, ResourceSet.Of(Resource.Wood, 4));

            Assert.AreEqual(10, g.VictoryPoints(0));
            Ok(g, new BankTrade(0, Resource.Wood, Resource.Ore));
            Assert.AreEqual(Phase.GameOver, g.Phase);
            Assert.AreEqual(0, g.Winner);
            Fails(g, new EndTurn(0));
        }

        // ---- House rules ---------------------------------------------------------------------------

        [Test]
        public void HouseRules_OnlyHostCanChange_AndMustBeValid()
        {
            Game g = New();
            Fails(g, new SetHouseRules(1, new HouseRules { VictoryPoints = 12 }));
            Fails(g, new SetHouseRules(Game.HostPlayer, new HouseRules { VictoryPoints = 1 }));
            Fails(g, new SetHouseRules(Game.HostPlayer, new HouseRules { BankRatio = 3, GenericPortRatio = 4 }));

            Ok(g, new SetHouseRules(Game.HostPlayer, new HouseRules { VictoryPoints = 12 }));
            Assert.AreEqual(12, g.Rules.VictoryPoints);
        }

        [Test]
        public void HouseRules_ChangesAreCopied_NotAliased()
        {
            Game g = New();
            var mine = new HouseRules { VictoryPoints = 8 };
            Ok(g, new SetHouseRules(Game.HostPlayer, mine));
            mine.VictoryPoints = 99;
            Assert.AreEqual(8, g.Rules.VictoryPoints);
        }

        [Test]
        public void HouseRules_CannotChangeMidRobberResolution()
        {
            Game g = ReadyToBuild();
            g.ForcePhase(Phase.MoveRobber);
            Fails(g, new SetHouseRules(Game.HostPlayer, new HouseRules()));
        }

        [Test]
        public void HouseRules_VictoryPointTargetAppliesImmediately()
        {
            Game g = New(players: 2, radius: 3);
            var verts = g.Board.Vertices.Take(3).ToList();
            foreach (Vertex v in verts) g.ForceCity(0, v); // 6 VP
            g.ForcePhase(Phase.Main);

            Ok(g, new SetHouseRules(Game.HostPlayer, new HouseRules { VictoryPoints = 7 }));
            Assert.AreEqual(Phase.Main, g.Phase);

            // Lowering the target to someone's current score would hand them the win, so it's refused
            // (the security review's "host changes the rules to win instantly").
            Assert.IsFalse(g.Apply(new SetHouseRules(Game.HostPlayer, new HouseRules { VictoryPoints = 6 })).Ok);
            Assert.AreEqual(Phase.Main, g.Phase);
            Assert.AreEqual(7, g.Rules.VictoryPoints);
        }

        [Test]
        public void HouseRules_DiscardThresholdAndBankRatio()
        {
            var dice = new FixedDice();
            Game g = New(players: 2, dice: dice, rules: new HouseRules { DiscardThreshold = 3, BankRatio = 3 });
            g.ForcePhase(Phase.Roll);
            g.GrantResources(1, ResourceSet.Of(Resource.Wood, 4));
            Assert.AreEqual(3, g.GetBankRatio(0, Resource.Wood));

            dice.Enqueue(7);
            Ok(g, new RollDice(0));
            Assert.AreEqual(Phase.Discard, g.Phase);
            Assert.AreEqual(2, g.PendingDiscards[1]);
        }

        [Test]
        public void HouseRules_NoSevensInEarlyRounds_Rerolls()
        {
            var dice = new FixedDice();
            Game g = New(players: 2, dice: dice, rules: new HouseRules { NoSevenRounds = 1 });
            g.ForcePhase(Phase.Roll);
            dice.Enqueue(7, 7, 5);
            Ok(g, new RollDice(0));
            Assert.AreEqual(5, g.LastRoll);
            Assert.AreEqual(Phase.Main, g.Phase);
        }

        [Test]
        public void HouseRules_FriendlyRobber_ProtectsLowPointPlayers()
        {
            var rules = new HouseRules { FriendlyRobber = true };
            Game g = New(players: 2, rules: rules);
            Tile protectedTile = g.Board.Tiles.First(t => !t.IsDesert);
            g.ForceSettlement(1, Vertex.OfCorner(protectedTile.Hex, 0)); // 1 VP: protected
            g.ForcePhase(Phase.MoveRobber);

            Fails(g, new MoveRobber(0, protectedTile.Hex));
            Assert.IsFalse(g.LegalRobberHexes().Contains(protectedTile.Hex));
            Ok(g, new MoveRobber(0, g.LegalRobberHexes().First()));
        }

        [Test]
        public void HouseRules_DevCardPlayableOnPurchaseTurn_AndLargestArmyMinimum()
        {
            var rules = new HouseRules { PlayDevCardOnPurchaseTurn = true, LargestArmyMinimum = 1 };
            Game g = New(players: 2, rules: rules);
            RunSetup(g);
            g.ForcePhase(Phase.Main);
            g.GrantResources(0, Costs.DevCard);
            Ok(g, new BuyDevCard(0));
            Assert.AreEqual(1, g.Players[0].Dev.Sum());

            g.GrantDevCard(0, DevCard.Knight);
            Ok(g, new PlayKnight(0));
            Assert.AreEqual(0, g.LargestArmyHolder);
        }

        // ---- Whole-game fuzz ----------------------------------------------------------------------

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(9)]
        [TestCase(10)]
        public void RandomPlay_NeverBreaksInvariants_AndOnlyLegalMovesSucceed(int seed)
        {
            var rnd = new System.Random(seed);
            var rules = new HouseRules { VictoryPoints = 6 + seed % 4 };
            Game g = New(players: 2 + seed % 4, seed: seed, radius: 2 + seed % 2, rules: rules);
            ResourceSet total = g.Bank;

            int steps = 0;
            while (g.Phase != Phase.GameOver && steps++ < 6000)
            {
                Command cmd = RandomBot.Choose(g, rnd);
                ActionResult r = g.Apply(cmd);
                Assert.IsTrue(r.Ok, $"step {steps}, {g.Phase}: {cmd.GetType().Name} by {cmd.Player} failed: {r.Error}");

                Assert.AreEqual(total, g.Bank + Sum(g), $"resources not conserved at step {steps}");
                foreach (Player p in g.Players)
                {
                    Assert.IsFalse(p.Hand.HasNegative);
                    Assert.GreaterOrEqual(p.RoadsLeft, 0);
                    Assert.GreaterOrEqual(p.SettlementsLeft, 0);
                    Assert.GreaterOrEqual(p.CitiesLeft, 0);
                }
                Assert.IsFalse(g.Bank.HasNegative);
            }

            Assert.Greater(g.Turn, 3, "bot should get through setup and play some turns");
            if (g.Winner >= 0) Assert.GreaterOrEqual(g.VictoryPoints(g.Winner), g.Rules.VictoryPoints);
        }

        [Test]
        public void SameSeedAndSameCommands_ReplayIdentically()
        {
            string Play(int seed)
            {
                var rnd = new System.Random(99);
                Game g = New(players: 3, seed: seed);
                for (int i = 0; i < 400 && g.Phase != Phase.GameOver; i++) g.Apply(RandomBot.Choose(g, rnd));
                return string.Join("|", g.Players.Select(p => p.Hand.ToString())) + $"#{g.Turn}#{g.LastRoll}";
            }
            Assert.AreEqual(Play(21), Play(21));
        }
    }
}
