using System.Linq;
using Conquer.Core;
using NUnit.Framework;

namespace Conquer.Client.Tests
{
    public class ControllerTests
    {
        static LocalGameController Started(int players = 3, int seed = 7, bool hide = false)
        {
            var c = new LocalGameController();
            c.NewGame(players, radius: 2, victoryPoints: 10, hideHands: hide, seed: seed);
            return c;
        }

        /// <summary>Clicks the first glowing spot until setup is over.</summary>
        static void FinishSetup(LocalGameController c)
        {
            int guard = 0;
            while ((c.Game.Phase == Phase.SetupVillage || c.Game.Phase == Phase.SetupRoad) && guard++ < 60)
            {
                Assert.IsNotEmpty(c.Spots, "setup phases must offer somewhere to click");
                c.ClickSpot(c.Spots[0]);
            }
        }

        [Test]
        public void NewGame_OffersSetupSpotsAndLogsIt()
        {
            LocalGameController c = Started();
            Assert.AreEqual(Phase.SetupVillage, c.Game.Phase);
            Assert.IsTrue(c.Spots.All(s => s.Kind == SpotKind.Vertex));
            Assert.AreEqual(c.Game.LegalSetupVertices().Count(), c.Spots.Count);
            Assert.IsNotEmpty(c.Log);
        }

        [Test]
        public void ClickingSpots_PlaysThroughSetup()
        {
            LocalGameController c = Started();
            FinishSetup(c);
            Assert.AreEqual(Phase.Roll, c.Game.Phase);
            Assert.AreEqual(6, c.Game.Buildings.Count);
            Assert.AreEqual(6, c.Game.RoadOwners.Count);
            Assert.IsNull(c.Toast, "no click during setup should have been rejected");
        }

        [Test]
        public void SetupRoadSpots_AreEdgesNextToTheNewVillage()
        {
            LocalGameController c = Started();
            c.ClickSpot(c.Spots[0]);
            Assert.AreEqual(Phase.SetupRoad, c.Game.Phase);
            Assert.IsTrue(c.Spots.All(s => s.Kind == SpotKind.Edge));
            Assert.AreEqual(c.Game.LegalSetupRoadEdges().Count(), c.Spots.Count);
        }

        [Test]
        public void Tools_RequireAffordabilityAndToggle()
        {
            LocalGameController c = Started(players: 2);
            FinishSetup(c);
            c.Send(new RollDice(c.Game.CurrentPlayer));
            if (c.Game.Phase != Phase.Main) Assert.Inconclusive("The roll triggered a 7; covered by the Core tests.");

            string toast = null;
            c.ToastShown += m => toast = m;
            c.SelectTool(Tool.City); // nobody can afford a city yet
            Assert.AreEqual(Tool.None, c.Tool);
            StringAssert.Contains("afford", toast);

            c.Game.GrantResources(c.Game.CurrentPlayer, Costs.Road);
            c.SelectTool(Tool.Road);
            Assert.AreEqual(Tool.Road, c.Tool);
            Assert.IsTrue(c.Spots.All(s => s.Kind == SpotKind.Edge));
            Assert.IsNotEmpty(c.Spots);

            c.SelectTool(Tool.Road); // toggles off
            Assert.AreEqual(Tool.None, c.Tool);
            Assert.IsEmpty(c.Spots);
        }

        [Test]
        public void HideHands_RequestsAHandoffWheneverTheActorChanges()
        {
            LocalGameController c = Started(hide: true);
            Assert.IsTrue(c.HandoffPending, "first player gets a handoff screen too");
            c.AcknowledgeHandoff();
            Assert.IsFalse(c.HandoffPending);

            c.ClickSpot(c.Spots[0]); // village, same player continues with a road
            Assert.IsFalse(c.HandoffPending);
            c.ClickSpot(c.Spots[0]); // road: next player's turn in the snake
            Assert.AreEqual(1, c.Game.CurrentPlayer);
            Assert.IsTrue(c.HandoffPending);
        }

        [Test]
        public void RejectedCommands_ProduceAToastNotAStateChange()
        {
            LocalGameController c = Started();
            string toast = null;
            c.ToastShown += m => toast = m;
            Assert.IsFalse(c.Send(new RollDice(0)));
            Assert.IsNotNull(toast);
            Assert.AreEqual(Phase.SetupVillage, c.Game.Phase);
        }

        [Test]
        public void RaiderPhase_OffersHexSpots()
        {
            LocalGameController c = Started(players: 2);
            FinishSetup(c);
            c.Game.ForcePhase(Phase.MoveRaider);
            c.SelectTool(Tool.None); // forces a refresh of the spots
            Assert.IsTrue(c.Spots.All(s => s.Kind == SpotKind.Hex));
            Assert.AreEqual(c.Game.Board.Tiles.Count - 1, c.Spots.Count);
        }
    }
}
