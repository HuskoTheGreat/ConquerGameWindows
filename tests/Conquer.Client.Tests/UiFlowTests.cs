using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Conquer.Core;
using NUnit.Framework;

namespace Conquer.Client.Tests
{
    /// <summary>Drives the real window by clicking its buttons, the way a player would.</summary>
    public class UiFlowTests
    {
        static MainWindow Open()
        {
            var window = new MainWindow { Width = 1360, Height = 860 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        /// <summary>Buttons whose label starts with the text. While a dialog is open, only its buttons count.</summary>
        static Button Find(MainWindow w, string startsWith, int nth = 0)
        {
            Visual scope = w;
            Grid overlay = w.GetVisualDescendants().OfType<Grid>().FirstOrDefault(g => g.Name == "Overlay" && g.IsVisible);
            if (overlay != null) scope = overlay;

            return scope.GetVisualDescendants().OfType<Button>()
                .Where(b => b.IsVisible && b.Content is TextBlock t && t.Text != null && t.Text.StartsWith(startsWith, StringComparison.Ordinal))
                .Skip(nth).FirstOrDefault();
        }

        static void Click(MainWindow w, string text, int nth = 0)
        {
            Button b = Find(w, text, nth);
            Assert.IsNotNull(b, $"no button starting with \"{text}\"");
            Assert.IsTrue(b.IsEnabled, $"button \"{text}\" is disabled");
            b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        internal static void Snap(MainWindow w, string name)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            string dir = Environment.GetEnvironmentVariable("CONQUER_SHOT_DIR") ?? Path.Combine(Path.GetTempPath(), "conquer-shots");
            Directory.CreateDirectory(dir);
            w.CaptureRenderedFrame().Save(Path.Combine(dir, name + ".png"));
        }

        static void ToMain(MainWindow w)
        {
            var c = w.Controller;
            while (c.Game.Phase == Phase.SetupVillage || c.Game.Phase == Phase.SetupRoad) c.ClickSpot(c.Spots[0]);
            if (c.HandoffPending) c.AcknowledgeHandoff();
            c.Game.GrantResources(c.Game.CurrentPlayer, new ResourceSet(4, 4, 4, 4, 4));
            c.Game.ForcePhase(Phase.Main);
            c.SelectTool(Tool.None);
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaTest]
        public void SinglePlayer_StartsAGameAgainstComputerPlayers()
        {
            MainWindow w = Open();
            Assert.IsNull(w.Controller.Game);
            Assert.IsNotNull(Find(w, "Online"), "the start screen offers online play");
            Snap(w, "00-start-screen");
            Click(w, "Single player");
            Click(w, "Start game");
            Assert.IsNotNull(w.Controller.Game);
            Assert.AreEqual(3, w.Controller.Game.Players.Count);
            Assert.IsTrue(w.Controller.IsBot(1) && w.Controller.IsBot(2), "two computer players by default");
            Assert.IsFalse(w.Controller.HandoffPending, "one person at the screen, so no pass-the-device screen");
        }

        [AvaloniaTest]
        public void HotSeat_StillHidesHandsBetweenPeople()
        {
            MainWindow w = Open();
            Click(w, "Single player");
            Click(w, "-", nth: 3); // Computer players: 2 -> 1
            Click(w, "-", nth: 3); // 1 -> 0
            Click(w, "Start game");
            Assert.IsFalse(w.Controller.HasBots);
            Assert.IsTrue(w.Controller.HandoffPending, "hide-hands is on by default, so player 1 gets the handoff screen");
            Click(w, "Ready");
            Assert.IsFalse(w.Controller.HandoffPending);
        }

        [AvaloniaTest]
        public void NewGame_GoesBackToTheStartScreen()
        {
            MainWindow w = Open();
            w.StartNewGame(3, 2, 10, hideHands: false, seed: 1);
            Dispatcher.UIThread.RunJobs();
            Click(w, "New game");
            Assert.IsNotNull(Find(w, "Single player"));
            Click(w, "Back to game");
            Assert.IsNotNull(w.Controller.Game);
        }

        [AvaloniaTest]
        public void OfflineOnly_OpensOnSinglePlayerSetup()
        {
            var w = new MainWindow { Width = 1360, Height = 860, OfflineOnly = true };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.IsNotNull(Find(w, "Start game"));
            Assert.IsNull(Find(w, "Online"));
            Assert.IsNull(Find(w, "Back"));
        }

        [AvaloniaTest]
        public void OnlineForm_AsksForTheServerBeforeConnecting()
        {
            MainWindow w = Open();
            Click(w, "Online");
            Click(w, "Create room");
            Snap(w, "00b-online-form");
            Assert.IsTrue(w.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Enter the server's address."));
            Click(w, "Back");
            Assert.IsNotNull(Find(w, "Single player"));
        }

        [AvaloniaTest]
        public void RollAndEndTurn_WorkThroughTheButtons()
        {
            MainWindow w = Open();
            w.StartNewGame(2, 2, 10, hideHands: false, seed: 4);
            var c = w.Controller;
            while (c.Game.Phase == Phase.SetupVillage || c.Game.Phase == Phase.SetupRoad) c.ClickSpot(c.Spots[0]);
            Dispatcher.UIThread.RunJobs();

            Click(w, "Roll dice");
            Assert.IsTrue(c.Game.LastRoll >= 2 && c.Game.LastRoll <= 12);

            if (c.Game.Phase != Phase.Main) Assert.Inconclusive("A 7 sends the game to the raider flow, covered elsewhere.");
            Click(w, "End turn");
            Assert.AreEqual(1, c.Game.CurrentPlayer);
            Assert.AreEqual(Phase.Roll, c.Game.Phase);
        }

        [AvaloniaTest]
        public void BankTradeDialog_TradesThroughTheButtons()
        {
            MainWindow w = Open();
            w.StartNewGame(3, 2, 10, hideHands: false, seed: 9);
            ToMain(w);
            var c = w.Controller;
            int me = c.Game.CurrentPlayer;
            int ratio = c.Game.GetBankRatio(me, Resource.Timber);
            int timberBefore = c.Game.Players[me].Hand.Timber, ironBefore = c.Game.Players[me].Hand.Iron;

            Click(w, "Bank trade");
            Snap(w, "10-bank-trade-empty");
            Click(w, "Timber");        // give timber (the Give row comes first)
            Click(w, "Iron", nth: 1); // get iron (the second "Iron" button is in the Get row)
            Snap(w, "11-bank-trade-selected");
            Click(w, "Trade");

            Assert.AreEqual(timberBefore - ratio, c.Game.Players[me].Hand.Timber);
            Assert.AreEqual(ironBefore + 1, c.Game.Players[me].Hand.Iron);
        }

        [AvaloniaTest]
        public void PlayerTradeDialog_ProposesAnOffer()
        {
            MainWindow w = Open();
            w.StartNewGame(3, 2, 10, hideHands: false, seed: 9);
            ToMain(w);
            var c = w.Controller;

            Click(w, "Trade with players");
            Snap(w, "12-player-trade");
            Click(w, "Propose"); // empty offer is rejected by the engine, with a toast, not a crash
            Assert.IsNull(c.Game.PendingTrade);
            Click(w, "Close");
        }

        [AvaloniaTest]
        public void HouseRulesDialog_AppliesChanges()
        {
            MainWindow w = Open();
            w.StartNewGame(3, 2, 10, hideHands: false, seed: 9);
            ToMain(w);

            Click(w, "House rules");
            Snap(w, "13-house-rules");
            Click(w, "Apply");
            Assert.AreEqual(10, w.Controller.Game.Rules.VictoryPoints);
        }

        [AvaloniaTest]
        public void PlayCardDialog_PlaysASoldier()
        {
            MainWindow w = Open();
            w.StartNewGame(3, 2, 10, hideHands: false, seed: 9);
            ToMain(w);
            var c = w.Controller;
            c.Game.GrantActionCard(c.Game.CurrentPlayer, ActionCard.Soldier);

            Click(w, "Play card");
            Snap(w, "14-play-card");
            Click(w, "Soldier");
            Assert.AreEqual(1, c.Game.Players[c.Game.CurrentPlayer].SoldiersPlayed);
            Assert.AreEqual(Phase.MoveRaider, c.Game.Phase);
            Assert.IsTrue(c.Spots.All(sp => sp.Kind == SpotKind.Hex), "the raider can now be placed by clicking a hex");
            Snap(w, "15-raider-hexes");

            c.ClickSpot(c.Spots[0]);
            Assert.AreNotEqual(Phase.MoveRaider, c.Game.Phase);
        }

        [AvaloniaTest]
        public void DiscardDialog_AppearsAfterASevenAndAcceptsTheDiscard()
        {
            MainWindow w = Open();
            w.StartNewGame(2, 2, 10, hideHands: false, seed: 6, dice: new FixedSevenDice());
            var c = w.Controller;
            while (c.Game.Phase == Phase.SetupVillage || c.Game.Phase == Phase.SetupRoad) c.ClickSpot(c.Spots[0]);

            c.Game.GrantResources(1, new ResourceSet(4, 3, 2, 1, 1)); // player 2 now holds well over 7 cards
            int before = c.Game.Players[1].Hand.Total;
            Dispatcher.UIThread.RunJobs(); // let the window rebuild after the state changes above
            Click(w, "Roll dice");

            Assert.AreEqual(Phase.Discard, c.Game.Phase);
            int owe = c.Game.PendingDiscards[1];
            Assert.AreEqual(before / 2, owe);
            Snap(w, "16-discard");

            Assert.IsFalse(Find(w, "Discard").IsEnabled, "nothing selected yet");
            for (int i = 0; i < owe; i++)
            {
                Button plus = w.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "Overlay")
                    .GetVisualDescendants().OfType<Button>()
                    .First(b => b.IsEnabled && b.Content is TextBlock t && t.Text == "+");
                plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
            }
            Click(w, "Discard");

            Assert.AreEqual(before - owe, c.Game.Players[1].Hand.Total);
            Assert.AreEqual(Phase.MoveRaider, c.Game.Phase);
        }

        sealed class FixedSevenDice : IDice
        {
            public int Roll() => 7;
        }
    }
}
