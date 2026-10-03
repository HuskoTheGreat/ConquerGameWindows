using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Catan.Core.Tests
{
    /// <summary>Dice that return a scripted sequence, so tests can force exact rolls.</summary>
    public sealed class FixedDice : IDice
    {
        readonly Queue<int> _rolls = new Queue<int>();
        public void Enqueue(params int[] rolls)
        {
            foreach (int r in rolls) _rolls.Enqueue(r);
        }
        public int Roll() => _rolls.Dequeue();
    }

    public static class GameTestKit
    {
        public static Game New(int players = 3, int seed = 5, int radius = 2, HouseRules rules = null, IDice dice = null)
        {
            var config = new GameConfig
            {
                PlayerCount = players,
                Seed = seed,
                Board = new BoardConfig { Radius = radius, Seed = seed },
                Rules = rules ?? new HouseRules(),
            };
            return new Game(config, dice);
        }

        public static void Ok(Game g, Command c)
        {
            ActionResult r = g.Apply(c);
            Assert.IsTrue(r.Ok, $"{c.GetType().Name} by {c.Player} failed: {r.Error}");
        }

        public static void Fails(Game g, Command c)
        {
            Assert.IsFalse(g.Apply(c).Ok, $"{c.GetType().Name} by {c.Player} should have failed");
        }

        /// <summary>Plays the whole setup phase using the first legal spot each time.</summary>
        public static void RunSetup(Game g)
        {
            while (g.Phase == Phase.SetupSettlement || g.Phase == Phase.SetupRoad)
            {
                int p = g.CurrentPlayer;
                if (g.Phase == Phase.SetupSettlement) Ok(g, new SetupSettlement(p, g.LegalSetupVertices().First()));
                else Ok(g, new SetupRoad(p, g.LegalSetupRoadEdges().First()));
            }
        }

        public static ResourceSet Sum(Game g) => g.Players.Aggregate(ResourceSet.Empty, (acc, p) => acc + p.Hand);
    }
}
