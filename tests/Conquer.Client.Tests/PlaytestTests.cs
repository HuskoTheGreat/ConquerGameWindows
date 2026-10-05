using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Conquer.Core;
using Conquer.Core.Bots;
using NUnit.Framework;

namespace Conquer.Client.Tests
{
    /// <summary>Edge cases found by play-testing: hidden information, discard limits and pointless trades.</summary>
    public class PlaytestTests
    {
        sealed class SeqDice : IDice
        {
            readonly int[] _rolls; int _i;
            public SeqDice(params int[] rolls) => _rolls = rolls;
            public int Roll() => _rolls[Math.Min(_i++, _rolls.Length - 1)];
        }

        static MainWindow Open(int w = 1360, int h = 860)
        {
            var window = new MainWindow { Width = w, Height = h };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        static void FinishSetup(LocalGameController c)
        {
            for (int guard = 0; guard < 400 && (c.Game.Phase == Phase.SetupVillage || c.Game.Phase == Phase.SetupRoad); guard++)
            {
                if (c.HandoffPending) c.AcknowledgeHandoff();
                if (c.IsBot(c.Game.CurrentPlayer)) c.StepBot();
                else c.ClickSpot(c.Spots[0]);
            }
            if (c.HandoffPending) c.AcknowledgeHandoff();
            Dispatcher.UIThread.RunJobs();
        }

        static Grid Overlay(MainWindow w) =>
            w.GetVisualDescendants().OfType<Grid>().FirstOrDefault(g => g.Name == "Overlay" && g.IsVisible);

        [AvaloniaTest]
        public void BotTurn_ShowsThePersonsHand_NotTheBots()
        {
            MainWindow w = Open();
            var c = w.Controller;
            w.StartNewGame(3, 2, 10, hideHands: false, seed: 5);
            c.NewGame(3, 2, 10, false, 2, BotDifficulty.Normal, seed: 5, dice: new SeqDice(8, 6, 5, 9));
            FinishSetup(c);
            c.Send(new RollDice(0));
            c.Send(new EndTurn(0));
            Dispatcher.UIThread.RunJobs();

            Assert.IsTrue(c.IsBot(c.Game.CurrentPlayer));
            Assert.AreEqual(0, c.Viewer, "the hand on screen stays the person's while a bot moves");
            Assert.IsNull(w.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsVisible && b.Content is TextBlock t && t.Text == "Roll dice"),
                "no Roll dice button on a bot's turn");
            UiFlowTests.Snap(w, "30-bot-turn");
        }

        [AvaloniaTest]
        public void Bots_HaveNames_PeopleKeepPlayerN()
        {
            MainWindow w = Open();
            var c = w.Controller;
            w.StartNewGame(4, 2, 10, hideHands: false, seed: 5);
            c.NewGame(4, 2, 10, false, 3, BotDifficulty.Easy, seed: 5);
            Assert.AreEqual("Player 1", c.Game.Players[0].Name);
            var bots = c.Game.Players.Skip(1).Select(p => p.Name).ToList();
            Assert.That(bots, Is.Unique);
            Assert.That(bots, Has.None.StartsWith("Player"));
        }

        [AvaloniaTest]
        public void Discard_CannotSelectMoreThanOwed()
        {
            MainWindow w = Open();
            var c = w.Controller;
            w.StartNewGame(3, 2, 10, hideHands: false, seed: 9, dice: new SeqDice(7));
            FinishSetup(c);
            c.Game.GrantResources(0, new ResourceSet(4, 3, 2, 1, 2));
            c.Send(new RollDice(0));
            Dispatcher.UIThread.RunJobs();
            int owe = c.Game.PendingDiscards[0];

            // The engine refuses every wrong discard.
            Assert.IsFalse(c.Game.Apply(new DiscardCards(0, new ResourceSet(owe + 1, 0, 0, 0, 0))).Ok);
            Assert.IsFalse(c.Game.Apply(new DiscardCards(0, new ResourceSet(-1, owe + 1, 0, 0, 0))).Ok);
            Assert.IsFalse(c.Game.Apply(new DiscardCards(0, new ResourceSet(0, 0, 0, owe, 0))).Ok, "more than held");
            Assert.IsFalse(c.Game.Apply(new DiscardCards(2, new ResourceSet(1, 0, 0, 0, 0))).Ok, "a player who owes nothing");

            // Pressing + everywhere stops at the amount owed.
            for (int round = 0; round < 20; round++)
            {
                Button plus = Overlay(w).GetVisualDescendants().OfType<Button>()
                    .FirstOrDefault(b => b.IsEnabled && b.Content is TextBlock t && t.Text == "+");
                if (plus == null) break;
                plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
            }
            Assert.IsTrue(Overlay(w).GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == $"Selected {owe} of {owe}"));
            UiFlowTests.Snap(w, "31-discard-capped");
        }

        [AvaloniaTest]
        public void Trade_SameResourceOnBothSides_IsRefused()
        {
            MainWindow w = Open();
            var c = w.Controller;
            w.StartNewGame(3, 2, 10, hideHands: false, seed: 4, dice: new SeqDice(8));
            FinishSetup(c);
            c.Game.GrantResources(0, new ResourceSet(3, 1, 0, 0, 0));
            c.Send(new RollDice(0));
            Assert.IsFalse(c.Game.Apply(new ProposeTrade(0, new ResourceSet(2, 0, 0, 0, 0), new ResourceSet(2, 0, 0, 0, 0))).Ok);
            Assert.IsTrue(c.Send(new ProposeTrade(0, new ResourceSet(1, 1, 0, 0, 0), new ResourceSet(0, 0, 1, 0, 0))));
            StringAssert.Contains("1 Wood, 1 Brick for 1 Sheep", c.Log.Last());
        }

        [AvaloniaTest]
        public void SmallWindow_Renders()
        {
            MainWindow w = Open(1024, 680);
            w.StartNewGame(4, 2, 10, hideHands: false, seed: 3);
            FinishSetup(w.Controller);
            UiFlowTests.Snap(w, "32-small-window");
        }
    }
}
