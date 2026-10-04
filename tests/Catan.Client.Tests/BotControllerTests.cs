using System.Linq;
using Catan.Core;
using Catan.Core.Bots;
using NUnit.Framework;

namespace Catan.Client.Tests
{
    public class BotControllerTests
    {
        [Test]
        public void BotsTakeTheLastSeats_AndAlwaysLeaveOneHuman()
        {
            var c = new LocalGameController();
            c.NewGame(3, 2, 10, hideHands: false, bots: 5, difficulty: BotDifficulty.Hard, seed: 3);
            Assert.IsFalse(c.IsBot(0));
            Assert.IsTrue(c.IsBot(1) && c.IsBot(2));
            Assert.AreEqual(BotDifficulty.Hard, c.BotLevel(2));

            // A plain new game afterwards has no bots.
            c.NewGame(3, 2, 10, hideHands: false, seed: 3);
            Assert.IsFalse(c.HasBots);
        }

        [Test]
        public void PeopleCantMoveForABot_AndBotsPlayUntilItsAHumansTurn()
        {
            var c = new LocalGameController();
            c.NewGame(3, 2, 10, hideHands: true, bots: 2, difficulty: BotDifficulty.Normal, seed: 4);
            c.AcknowledgeHandoff();

            // Human places their first settlement and road.
            c.ClickSpot(c.Spots[0]);
            c.ClickSpot(c.Spots[0]);
            Assert.AreEqual(1, c.Game.CurrentPlayer);
            Assert.IsFalse(c.HandoffPending, "no pass-the-device screen for a bot");
            StringAssert.Contains("thinking", c.Prompt());

            Assert.IsFalse(c.Send(new SetupSettlement(1, c.Game.LegalSetupVertices().First())), "can't click for the bot");

            int guard = 0;
            while (c.Actor != 0 && guard++ < 50) Assert.IsTrue(c.StepBot());
            Assert.AreEqual(0, c.Actor, "back to the human");
            Assert.AreEqual(2, c.Game.Players[1].Settlements.Count, "each bot placed both starting settlements");
            Assert.AreEqual(2, c.Game.Players[2].Settlements.Count);
        }

        [Test]
        public void AGameOfOneHumanAndBots_CanBePlayedToTheEnd()
        {
            var c = new LocalGameController();
            c.NewGame(3, 2, 8, hideHands: false, bots: 2, difficulty: BotDifficulty.Easy, seed: 9);
            var me = new BotPlayer(0, BotDifficulty.Hard, 1); // stands in for the person

            int guard = 0;
            while (c.Game.Phase != Phase.GameOver && guard++ < 20000)
            {
                Command mine = me.Decide(c.Game);
                if (mine != null && c.Send(mine)) continue;
                if (!c.StepBot() && mine == null) Assert.Fail($"stuck in {c.Game.Phase}");
            }
            Assert.AreEqual(Phase.GameOver, c.Game.Phase);
        }
    }
}
