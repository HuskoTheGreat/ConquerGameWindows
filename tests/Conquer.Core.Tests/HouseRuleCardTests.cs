using System.Linq;
using Conquer.Core.Net;
using NUnit.Framework;
using static Conquer.Core.Tests.GameTestKit;

namespace Conquer.Core.Tests
{
    /// <summary>The newer house rules: effect cards, elimination, trading anytime, starting cards.</summary>
    public class HouseRuleCardTests
    {
        static Game InMain(int players = 3, HouseRules rules = null)
        {
            var dice = new FixedDice();
            Game g = New(players, rules: rules, dice: dice);
            RunSetup(g);
            dice.Enqueue(8);
            Ok(g, new RollDice(g.CurrentPlayer));
            return g;
        }

        static void Draw(Game g, EffectCard card)
        {
            int me = g.CurrentPlayer;
            g.PutOnDeck(card);
            g.GrantResources(me, Costs.ActionCard);
            Ok(g, new BuyActionCard(me));
        }

        static ResourceSet Total(Game g) => Sum(g) + g.Bank;

        [Test]
        public void EffectCards_AreShuffledIntoTheDeck()
        {
            var rules = new HouseRules();
            rules.EffectCards[(int)EffectCard.Windfall] = 3;
            rules.EffectCards[(int)EffectCard.InstantLoss] = 1;
            Assert.AreEqual(New().DevDeckCount + 4, New(rules: rules).DevDeckCount);
        }

        [Test]
        public void InstantLoss_EliminatesThePlayer_AndPlayMovesOn()
        {
            Game g = InMain();
            int me = g.CurrentPlayer;
            ResourceSet total = Total(g);

            Draw(g, EffectCard.InstantLoss);

            Assert.IsTrue(g.Players[me].Eliminated);
            Assert.AreEqual(0, g.Players[me].Hand.Total, "cards go back to the bank");
            Assert.AreEqual(total - Costs.ActionCard + Costs.ActionCard, Total(g));
            Assert.AreNotEqual(me, g.CurrentPlayer);
            Assert.AreEqual(Phase.Roll, g.Phase);
            Fails(g, new EndTurn(me));
        }

        [Test]
        public void InstantLoss_InATwoPlayerGame_HandsTheOtherPlayerTheWin()
        {
            Game g = InMain(players: 2);
            int me = g.CurrentPlayer;
            Draw(g, EffectCard.InstantLoss);
            Assert.AreEqual(Phase.GameOver, g.Phase);
            Assert.AreEqual(1 - me, g.Winner);
        }

        [Test]
        public void EliminatedPlayers_AreSkipped_AndDontProduce()
        {
            var dice = new FixedDice();
            Game g = New(3, dice: dice);
            RunSetup(g);
            dice.Enqueue(8);
            Ok(g, new RollDice(0));
            Draw(g, EffectCard.InstantLoss); // player 0 is out
            Assert.AreEqual(1, g.CurrentPlayer);

            // A number player 0's villages sit on.
            int number = g.Players[0].Villages
                .SelectMany(v => g.Board.TilesAround(v))
                .First(t => !t.IsWasteland && t.Hex != g.RaiderHex).Number;

            dice.Enqueue(number);
            Ok(g, new RollDice(1));
            Assert.AreEqual(0, g.Players[0].Hand.Total, "ruins don't produce");
            Ok(g, new EndTurn(1));
            Assert.AreEqual(2, g.CurrentPlayer);
            dice.Enqueue(number == 8 ? 9 : 8);
            Ok(g, new RollDice(2));
            Ok(g, new EndTurn(2));
            Assert.AreEqual(1, g.CurrentPlayer, "player 0 is skipped");
        }

        [Test]
        public void InstantWin_EndsTheGame()
        {
            Game g = InMain();
            int me = g.CurrentPlayer;
            Draw(g, EffectCard.InstantWin);
            Assert.AreEqual(Phase.GameOver, g.Phase);
            Assert.AreEqual(me, g.Winner);
        }

        [Test]
        public void Bankruptcy_Plague_Tribute_Windfall_KeepEveryCardAccountedFor()
        {
            foreach (EffectCard card in new[] { EffectCard.Bankruptcy, EffectCard.Plague, EffectCard.Tribute, EffectCard.Windfall })
            {
                Game g = InMain();
                for (int p = 0; p < 3; p++) g.GrantResources(p, new ResourceSet(2, 2, 2, 2, 2));
                ResourceSet total = Total(g);
                int me = g.CurrentPlayer;
                int before = g.Players[me].Hand.Total;
                int othersBefore = g.Players.Where(p => p.Id != me).Sum(p => p.Hand.Total);
                int othersAfterPlague = g.Players.Where(p => p.Id != me).Sum(p => p.Hand.Total - p.Hand.Total / 2);

                Draw(g, card);
                Assert.AreEqual(total + Costs.ActionCard - Costs.ActionCard, Total(g), card.ToString());

                int after = g.Players[me].Hand.Total;
                int othersAfter = g.Players.Where(p => p.Id != me).Sum(p => p.Hand.Total);
                switch (card)
                {
                    case EffectCard.Bankruptcy: Assert.AreEqual(0, after); break;
                    case EffectCard.Windfall: Assert.AreEqual(before + 3, after); break;
                    case EffectCard.Plague: Assert.AreEqual(othersAfterPlague, othersAfter); break;
                    case EffectCard.Tribute:
                        Assert.AreEqual(before + 2, after);
                        Assert.AreEqual(othersBefore - 2, othersAfter);
                        break;
                }
            }
        }

        [Test]
        public void Earthquake_DestroysARoad_AndBanditsMoveTheRaider()
        {
            Game g = InMain();
            int me = g.CurrentPlayer;
            int roads = g.Players[me].Roads.Count;
            Draw(g, EffectCard.Earthquake);
            Assert.AreEqual(roads - 1, g.Players[me].Roads.Count);

            Draw(g, EffectCard.Bandits);
            Assert.AreEqual(Phase.MoveRaider, g.Phase);
        }

        [Test]
        public void TradeAnytime_LetsOtherPlayersTradeDuringSomeoneElsesTurn()
        {
            Game normal = InMain();
            int other = (normal.CurrentPlayer + 1) % 3;
            normal.GrantResources(other, ResourceSet.Of(Resource.Timber, 4));
            Fails(normal, new BankTrade(other, Resource.Timber, Resource.Iron));
            Fails(normal, new ProposeTrade(other, ResourceSet.Of(Resource.Timber), ResourceSet.Of(Resource.Iron)));

            Game g = InMain(rules: new HouseRules { TradeAnytime = true });
            int current = g.CurrentPlayer;
            int a = (current + 1) % 3, b = (current + 2) % 3;
            g.GrantResources(a, ResourceSet.Of(Resource.Timber, 4));
            g.GrantResources(b, ResourceSet.Of(Resource.Iron, 1));
            Ok(g, new BankTrade(a, Resource.Timber, Resource.Clay));
            Ok(g, new ProposeTrade(b, ResourceSet.Of(Resource.Iron), ResourceSet.Of(Resource.Clay)));

            // The offer survives the current player's turn ending, and is accepted during the next Roll phase.
            Ok(g, new EndTurn(current));
            Assert.IsNotNull(g.PendingTrade);
            Ok(g, new AcceptTrade(a));
            Assert.AreEqual(1, g.Players[a].Hand[Resource.Iron]);
        }

        [Test]
        public void StartingResources_AreDealtWhenSetupEnds()
        {
            Game g = New(3, rules: new HouseRules { StartingResources = 2 });
            ResourceSet before = Sum(g);
            Assert.AreEqual(0, before.Total);
            RunSetup(g);
            foreach (Player p in g.Players)
                foreach (Resource r in ResourceSet.Types) Assert.GreaterOrEqual(p.Hand[r], 2);
        }

        [Test]
        public void EffectCards_CanOnlyChangeBeforeTheFirstRoll()
        {
            Game g = New(3);
            var rules = new HouseRules();
            rules.EffectCards[(int)EffectCard.Windfall] = 2;
            int deck = g.DevDeckCount;
            Ok(g, new SetHouseRules(Game.HostPlayer, rules));
            Assert.AreEqual(deck + 2, g.DevDeckCount);

            RunSetup(g);
            var more = rules.Clone();
            more.EffectCards[(int)EffectCard.InstantWin] = 1;
            Fails(g, new SetHouseRules(Game.HostPlayer, more));
        }

        [Test]
        public void RuleChanges_AreSpelledOutInTheLog()
        {
            Game g = New(3);
            ActionResult r = g.Apply(new SetHouseRules(Game.HostPlayer, new HouseRules { VictoryPoints = 12, TradeAnytime = true }));
            Assert.IsTrue(r.Ok);
            StringAssert.Contains("points to win 10 to 12", r.Events.Single());
            StringAssert.Contains("trading anytime on", r.Events.Single());
        }

        [Test]
        public void NewRules_RoundTripOverTheWire_AndInSnapshots()
        {
            var rules = new HouseRules { TradeAnytime = true, StartingResources = 3 };
            rules.EffectCards[(int)EffectCard.Plague] = 4;
            byte[] bytes = CommandCodec.Encode(new SetHouseRules(0, rules));
            var back = (SetHouseRules)CommandCodec.Decode(bytes, 0);
            Assert.IsTrue(back.Rules.TradeAnytime);
            Assert.AreEqual(3, back.Rules.StartingResources);
            Assert.AreEqual(4, back.Rules.EffectCardCount(EffectCard.Plague));

            Game g = InMain(players: 2, rules: rules);
            Draw(g, EffectCard.InstantLoss);
            Game mirror = SnapshotCodec.Decode(SnapshotCodec.Encode(g, 0));
            Assert.AreEqual(4, mirror.Rules.EffectCardCount(EffectCard.Plague));
            Assert.IsTrue(mirror.Players.Any(p => p.Eliminated));
        }

        [Test]
        public void TooManyEffectCards_AreRejected()
        {
            var rules = new HouseRules();
            rules.EffectCards[0] = EffectCardInfo.MaxEach + 1;
            Assert.IsNotNull(rules.Validate());
        }
    }
}
