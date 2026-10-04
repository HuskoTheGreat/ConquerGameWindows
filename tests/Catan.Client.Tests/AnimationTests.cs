using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Catan.Client.Animation;
using Catan.Core;
using NUnit.Framework;

namespace Catan.Client.Tests
{
    /// <summary>
    /// Plays real animations in the real window on a hand-cranked clock and saves frames from the middle of
    /// them (to %TEMP%\catan-shots, or CATAN_SHOT_DIR), so motion can be checked without a display.
    /// </summary>
    public class AnimationTests
    {
        sealed class Dice : IDice
        {
            readonly Queue<int> _rolls = new Queue<int>();
            public void Next(int roll) => _rolls.Enqueue(roll);
            public int Roll() => _rolls.Dequeue();
        }

        double _now;

        [SetUp]
        public void TurnOn()
        {
            _now = 1000;
            AnimationLayer.Clock = () => _now;
            AnimationLayer.Enabled = true;
        }

        [TearDown]
        public void TurnOff()
        {
            AnimationLayer.Clock = AnimationLayer.DefaultClock;
            AnimationLayer.Enabled = false;
        }

        static MainWindow Open()
        {
            var window = new MainWindow { Width = 1360, Height = 860 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        void Advance(MainWindow w, double seconds)
        {
            _now += seconds;
            Dispatcher.UIThread.RunJobs();
            w.Animations.Tick();
        }

        static void Snap(MainWindow w, string name)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            string dir = Environment.GetEnvironmentVariable("CATAN_SHOT_DIR") ?? Path.Combine(Path.GetTempPath(), "catan-shots");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name + ".png");
            w.CaptureRenderedFrame().Save(path);
            Assert.Greater(new FileInfo(path).Length, 5_000, "screenshot looks empty");
        }

        static MainWindow ReadyToRoll(Dice dice)
        {
            MainWindow w = Open();
            w.StartNewGame(3, 2, 10, hideHands: false, seed: 11, dice: dice);
            var c = w.Controller;
            while (c.Game.Phase == Phase.SetupSettlement || c.Game.Phase == Phase.SetupRoad) c.ClickSpot(c.Spots[c.Spots.Count / 2]);
            Dispatcher.UIThread.RunJobs();
            return w;
        }

        [AvaloniaTest]
        public void Roll_TumblesDiceThenThrowsCards()
        {
            var dice = new Dice();
            MainWindow w = ReadyToRoll(dice);
            Game g = w.Controller.Game;
            Tile tile = g.Board.Tiles.First(t => !t.IsDesert && t.Hex != g.RobberHex &&
                Enumerable.Range(0, 6).Any(i => g.Buildings.ContainsKey(Vertex.OfCorner(t.Hex, i))));
            dice.Next(tile.Number);

            Advance(w, 10); // let the setup animations finish
            Assert.IsFalse(w.Animations.Busy);

            Assert.IsTrue(w.Controller.Send(new RollDice(g.CurrentPlayer)));
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(w.Animations.Busy, "rolling should start an animation");

            // The state is already final while the dice are still in the air: animation never holds up the game.
            Assert.AreEqual(Phase.Main, g.Phase);

            Advance(w, 0.35);
            Snap(w, "20-anim-dice-tumbling");
            Advance(w, 0.85);
            Snap(w, "21-anim-cards-flying");

            Advance(w, 10);
            Assert.IsFalse(w.Animations.Busy, "animations should finish on their own");
        }

        [AvaloniaTest]
        public void BuyingADevCard_PullsAndFlipsIt()
        {
            MainWindow w = ReadyToRoll(new Dice());
            var c = w.Controller;
            c.Game.ForcePhase(Phase.Main);
            c.Game.GrantResources(c.Game.CurrentPlayer, Costs.DevCard);
            Advance(w, 10);

            Assert.IsTrue(c.Send(new BuyDevCard(c.Game.CurrentPlayer)));
            Dispatcher.UIThread.RunJobs();
            Advance(w, 0.4);
            Snap(w, "22-anim-card-pull-rising");
            Advance(w, 0.45);
            Snap(w, "23-anim-card-pull-revealed");
            Advance(w, 10);
            Assert.IsFalse(w.Animations.Busy);
        }

        [AvaloniaTest]
        public void Building_PopsThePieceIn()
        {
            MainWindow w = ReadyToRoll(new Dice());
            var c = w.Controller;
            c.Game.ForcePhase(Phase.Main);
            c.Game.GrantResources(c.Game.CurrentPlayer, new ResourceSet(3, 3, 1, 1));
            Advance(w, 10);

            c.SelectTool(Tool.Road);
            c.ClickSpot(c.Spots[0]);
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(w.Animations.Busy);
            Advance(w, 0.18);
            Snap(w, "24-anim-road-growing");

            Advance(w, 10);
            Assert.IsTrue(c.Send(new EndTurn(c.Game.CurrentPlayer)));
            Dispatcher.UIThread.RunJobs();
            Advance(w, 0.5);
            Snap(w, "25-anim-turn-banner");
        }

        [AvaloniaTest]
        public void ChangingHouseRules_ShowsANotice()
        {
            MainWindow w = ReadyToRoll(new Dice());
            var c = w.Controller;
            Advance(w, 10);
            HouseRules rules = c.Game.Rules.Clone();
            rules.VictoryPoints = 12;
            rules.BankRatio = 3;
            rules.GenericPortRatio = 3;
            Assert.IsTrue(c.Send(new SetHouseRules(Game.HostPlayer, rules)));
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(w.Animations.Busy);
            Advance(w, 0.8);
            Snap(w, "26-anim-rules-changed");
        }

        [AvaloniaTest]
        public void TurnOff_StateChangesJustAppear()
        {
            MainWindow w = ReadyToRoll(new Dice());
            AnimationLayer.Enabled = false;
            var c = w.Controller;
            c.Game.ForcePhase(Phase.Main);
            Advance(w, 10);
            Assert.IsTrue(c.Send(new EndTurn(c.Game.CurrentPlayer)));
            Dispatcher.UIThread.RunJobs();
            Assert.IsFalse(w.Animations.Busy);
        }
    }
}
