using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Conquer.Core;
using Conquer.Core.Bots;
using NUnit.Framework;

namespace Conquer.Client.Tests
{
    /// <summary>Scratch play-test scenarios: drive real games and capture screenshots at edge cases.</summary>
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

        [AvaloniaTest]
        public void R1_BotTurn_WhatDoesYourHandShow()
        {
            MainWindow w = Open();
            var c = w.Controller;
            w.StartNewGame(3, 2, 10, hideHands: false, seed: 5);
            c.NewGame(3, 2, 10, false, 2, BotDifficulty.Normal, seed: 5, dice: new SeqDice(8, 6, 5, 9));
            FinishSetup(c);
            c.Send(new RollDice(0));
            c.Send(new EndTurn(0));
            Dispatcher.UIThread.RunJobs();
            TestContext.WriteLine($"Current={c.Game.CurrentPlayer} Actor={c.Actor} bot={c.IsBot(c.Game.CurrentPlayer)}");
            TestContext.WriteLine($"Human hand={c.Game.Players[0].Hand} Bot hand={c.Game.Players[1].Hand}");
            UiFlowTests.Snap(w, "p1-bot-turn-hand");
        }

        [AvaloniaTest]
        public void R2_Discard_OverselectAndBadCommands()
        {
            MainWindow w = Open();
            var c = w.Controller;
            w.StartNewGame(3, 2, 10, hideHands: false, seed: 9, dice: new SeqDice(7));
            FinishSetup(c);
            c.Game.GrantResources(0, new ResourceSet(4, 3, 2, 1, 2)); // 12+ cards
            c.Game.GrantResources(1, new ResourceSet(3, 3, 3, 0, 0));
            c.Send(new RollDice(0));
            Dispatcher.UIThread.RunJobs();
            int owe = c.Game.PendingDiscards[0];
            TestContext.WriteLine($"phase={c.Game.Phase} owe0={owe} hand0={c.Game.Players[0].Hand}");

            // Engine edge cases.
            var r1 = c.Game.Apply(new DiscardCards(0, new ResourceSet(10, 0, 0, 0, 0)));
            var r2 = c.Game.Apply(new DiscardCards(0, new ResourceSet(owe + 1, 0, 0, 0, 0)));
            var r3 = c.Game.Apply(new DiscardCards(0, new ResourceSet(-1, owe + 1, 0, 0, 0)));
            var r4 = c.Game.Apply(new DiscardCards(2, new ResourceSet(1, 0, 0, 0, 0)));
            var r5 = c.Game.Apply(new DiscardCards(0, new ResourceSet(0, 0, 0, owe, 0)));
            TestContext.WriteLine($"10 timber: {r1.Error}\nowe+1: {r2.Error}\nnegative: {r3.Error}\nnot owed: {r4.Error}\nmore than held: {r5.Error}");

            // UI: press + on every row as often as possible.
            Grid overlay = w.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "Overlay" && g.IsVisible);
            foreach (Button plus in overlay.GetVisualDescendants().OfType<Button>().Where(b => b.Content is TextBlock t && t.Text == "+").ToList())
                for (int i = 0; i < 6; i++) if (plus.IsEnabled) plus.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            UiFlowTests.Snap(w, "p2-discard-overselect");
        }

        [AvaloniaTest]
        public void R3_PassAndPlay_DiscardHandoffs()
        {
            MainWindow w = Open();
            var c = w.Controller;
            w.StartNewGame(3, 2, 10, hideHands: true, seed: 9, dice: new SeqDice(7));
            FinishSetup(c);
            c.Game.GrantResources(1, new ResourceSet(3, 3, 3, 0, 0));
            c.Game.GrantResources(2, new ResourceSet(3, 3, 3, 0, 0));
            c.Send(new RollDice(0));
            Dispatcher.UIThread.RunJobs();
            TestContext.WriteLine($"after roll: actor={c.Actor} handoff={c.HandoffPending}");
            UiFlowTests.Snap(w, "p3-discard-handoff");
            if (c.HandoffPending) c.AcknowledgeHandoff();
            Dispatcher.UIThread.RunJobs();
            UiFlowTests.Snap(w, "p3b-discard-p2");
        }

        [AvaloniaTest]
        public void R4_SmallWindow()
        {
            MainWindow w = Open(1024, 680);
            var c = w.Controller;
            w.StartNewGame(4, 2, 10, hideHands: false, seed: 3);
            FinishSetup(c);
            UiFlowTests.Snap(w, "p4-small-window");
        }

        [AvaloniaTest]
        public void R5_TradeAndBankEdges()
        {
            MainWindow w = Open();
            var c = w.Controller;
            w.StartNewGame(3, 2, 10, hideHands: false, seed: 4, dice: new SeqDice(8));
            FinishSetup(c);
            c.Send(new RollDice(0));
            var g = c.Game;
            TestContext.WriteLine("offer more than held: " + g.Apply(new ProposeTrade(0, new ResourceSet(9, 0, 0, 0, 0), new ResourceSet(0, 1, 0, 0, 0))).Error);
            TestContext.WriteLine("offer nothing: " + g.Apply(new ProposeTrade(0, ResourceSet.Empty, new ResourceSet(0, 1, 0, 0, 0))).Error);
            TestContext.WriteLine("same both sides: " + g.Apply(new ProposeTrade(0, new ResourceSet(1, 0, 0, 0, 0), new ResourceSet(1, 0, 0, 0, 0))).Error);
            UiFlowTests.Snap(w, "p5-main");
        }
    
        [AvaloniaTest]
        public void R6_LateGame()
        {
            MainWindow w = Open();
            var c = w.Controller;
            w.StartNewGame(4, 2, 12, hideHands: false, seed: 21);
            c.NewGame(4, 2, 12, false, 3, BotDifficulty.Hard, seed: 21);
            var me = new BotPlayer(0, BotDifficulty.Hard, 99);
            for (int i = 0; i < 4000 && c.Game.Phase != Phase.GameOver && c.Game.Turn < 45; i++)
            {
                if (c.HandoffPending) c.AcknowledgeHandoff();
                if (c.StepBot()) continue;
                Command cmd = me.Decide(c.Game);
                if (cmd == null || !c.Send(cmd)) { var f = me.Fallback(c.Game); if (f != null) c.Send(f); }
            }
            c.Game.GrantResources(0, new ResourceSet(9, 12, 8, 11, 10));
            Dispatcher.UIThread.RunJobs();
            TestContext.WriteLine($"turn={c.Game.Turn} phase={c.Game.Phase}");
            UiFlowTests.Snap(w, "p6-late-game");
        }

        [AvaloniaTest]
        public void R7_TradeSameResource()
        {
            MainWindow w = Open();
            var c = w.Controller;
            w.StartNewGame(3, 2, 10, hideHands: false, seed: 4, dice: new SeqDice(8));
            FinishSetup(c);
            c.Game.GrantResources(0, new ResourceSet(3, 0, 0, 0, 0));
            c.Send(new RollDice(0));
            bool ok = c.Send(new ProposeTrade(0, new ResourceSet(2, 0, 0, 0, 0), new ResourceSet(2, 0, 0, 0, 0)));
            Dispatcher.UIThread.RunJobs();
            TestContext.WriteLine($"same-resource offer accepted by engine: {ok}");
            UiFlowTests.Snap(w, "p7-trade-same");
        }
    }
}
