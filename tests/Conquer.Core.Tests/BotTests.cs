using System;
using System.Collections.Generic;
using System.Linq;
using Conquer.Core.Bots;
using NUnit.Framework;

namespace Conquer.Core.Tests
{
    public class BotTests
    {
        sealed class Outcome
        {
            public int Winner = -1;
            public int Steps;
            public int Rejected;
            public string LastError;
        }

        /// <summary>Plays a whole game with a bot in every seat. Each step, the first bot with something to do acts.</summary>
        static Outcome Play(Game g, IList<BotPlayer> bots, int maxSteps = 20000)
        {
            var o = new Outcome();
            while (g.Phase != Phase.GameOver && o.Steps < maxSteps)
            {
                Command cmd = null;
                BotPlayer who = null;
                foreach (BotPlayer b in bots)
                {
                    cmd = b.Decide(g);
                    if (cmd != null)
                    {
                        who = b;
                        break;
                    }
                }
                Assert.IsNotNull(cmd, $"nobody could move in {g.Phase} (turn {g.Turn})");

                ActionResult r = g.Apply(cmd);
                if (!r.Ok)
                {
                    o.Rejected++;
                    o.LastError = $"{cmd.GetType().Name}: {r.Error}";
                    Command fallback = who.Fallback(g);
                    Assert.IsNotNull(fallback);
                    Assert.IsTrue(g.Apply(fallback).Ok, "fallback must be legal");
                }
                o.Steps++;
            }
            o.Winner = g.Winner;
            return o;
        }

        static Game NewGame(int players, int seed, HouseRules rules = null) => new Game(new GameConfig
        {
            PlayerCount = players,
            Seed = seed,
            Board = new BoardConfig { Seed = seed },
            Rules = rules ?? new HouseRules(),
        });

        [TestCase(BotDifficulty.Easy)]
        [TestCase(BotDifficulty.Normal)]
        [TestCase(BotDifficulty.Hard)]
        public void EveryDifficulty_FinishesGames_WithOnlyLegalMoves(BotDifficulty d)
        {
            for (int seed = 1; seed <= 12; seed++)
            {
                int players = 2 + seed % 3;
                Game g = NewGame(players, seed);
                var bots = Enumerable.Range(0, players).Select(i => new BotPlayer(i, d, seed * 10 + i)).ToList();
                Outcome o = Play(g, bots);
                Assert.AreEqual(Phase.GameOver, g.Phase, $"seed {seed}: no winner after {o.Steps} steps");
                Assert.AreEqual(0, o.Rejected, $"seed {seed}: {o.LastError}");
            }
        }

        [Test]
        public void Bots_HandleEveryHouseRuleAndEffectCard()
        {
            var rules = new HouseRules { TradeAnytime = true, StartingResources = 1, FriendlyRaider = true, NoSevenRounds = 2 };
            for (int i = 0; i < EffectCardInfo.Count; i++) rules.EffectCards[i] = 2;

            for (int seed = 1; seed <= 15; seed++)
            {
                Game g = NewGame(4, seed, rules);
                var bots = Enumerable.Range(0, 4)
                    .Select(i => new BotPlayer(i, (BotDifficulty)(i % 3), seed * 7 + i)).ToList();
                Outcome o = Play(g, bots);
                Assert.AreEqual(Phase.GameOver, g.Phase, $"seed {seed}");
                Assert.AreEqual(0, o.Rejected, $"seed {seed}: {o.LastError}");
            }
        }

        [Test]
        public void HarderBots_WinMoreOften()
        {
            int hardWins = 0, easyWins = 0, normalVsEasy = 0;
            const int games = 40;
            for (int seed = 1; seed <= games; seed++)
            {
                // Rotate seats so going first doesn't decide it.
                int hardSeat = seed % 2;
                Game g = NewGame(2, seed);
                var bots = new List<BotPlayer>
                {
                    new BotPlayer(hardSeat, BotDifficulty.Hard, seed),
                    new BotPlayer(1 - hardSeat, BotDifficulty.Easy, seed + 1000),
                };
                Outcome o = Play(g, bots);
                if (o.Winner == hardSeat) hardWins++;
                else easyWins++;

                Game n = NewGame(2, seed + 500);
                var nb = new List<BotPlayer>
                {
                    new BotPlayer(hardSeat, BotDifficulty.Normal, seed),
                    new BotPlayer(1 - hardSeat, BotDifficulty.Easy, seed + 1000),
                };
                if (Play(n, nb).Winner == hardSeat) normalVsEasy++;
            }
            TestContext.WriteLine($"Hard beat Easy {hardWins}/{games}; Normal beat Easy {normalVsEasy}/{games}");
            Assert.Greater(hardWins, games * 3 / 4);
            Assert.Greater(normalVsEasy, games * 3 / 4);
        }

        [Test]
        public void HardBeatsNormal_MoreOftenThanNot()
        {
            int hardWins = 0;
            const int games = 60;
            for (int seed = 1; seed <= games; seed++)
            {
                int hardSeat = seed % 3;
                Game g = NewGame(3, seed + 77);
                var bots = Enumerable.Range(0, 3)
                    .Select(i => new BotPlayer(i, i == hardSeat ? BotDifficulty.Hard : BotDifficulty.Normal, seed * 3 + i))
                    .ToList();
                if (Play(g, bots).Winner == hardSeat) hardWins++;
            }
            TestContext.WriteLine($"Hard won {hardWins}/{games} three-player games against two Normal bots");
            Assert.Greater(hardWins, games / 3, "better than the 1-in-3 baseline");
        }

        [Test]
        public void Bots_OnlyMoveWhenItsTheirTurn()
        {
            Game g = NewGame(3, 4);
            var bot = new BotPlayer(1, BotDifficulty.Hard, 1);
            Assert.AreEqual(0, g.CurrentPlayer);
            Assert.IsNull(bot.Decide(g));
        }
    }
}
