using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Catan.Core;
using NUnit.Framework;

namespace Catan.Client.Tests
{
    /// <summary>
    /// Renders the real window off-screen and saves PNGs, so the interface can be inspected without a display.
    /// Output goes to %TEMP%\catan-shots (or CATAN_SHOT_DIR).
    /// </summary>
    public class ScreenshotTests
    {
        static string OutDir()
        {
            string dir = Environment.GetEnvironmentVariable("CATAN_SHOT_DIR") ?? Path.Combine(Path.GetTempPath(), "catan-shots");
            Directory.CreateDirectory(dir);
            return dir;
        }

        static MainWindow Open()
        {
            var window = new MainWindow { Width = 1360, Height = 860 };
            window.Show();
            return window;
        }

        static void Snap(MainWindow window, string name)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var frame = window.CaptureRenderedFrame();
            Assert.IsNotNull(frame, "headless renderer produced no frame");
            string path = Path.Combine(OutDir(), name + ".png");
            frame.Save(path);
            Assert.Greater(new FileInfo(path).Length, 5_000, "screenshot looks empty");
        }

        static void Play(LocalGameController c, int clicks)
        {
            for (int i = 0; i < clicks && c.Spots.Count > 0; i++) c.ClickSpot(c.Spots[Math.Min(c.Spots.Count - 1, 3 + i * 7 % Math.Max(1, c.Spots.Count))]);
        }

        [AvaloniaTest]
        public void Setup_Dialog()
        {
            MainWindow w = Open();
            Snap(w, "01-setup");
        }

        [AvaloniaTest]
        public void HouseRules_Dialog_WithEffectCards()
        {
            var w = new MainWindow { Width = 1360, Height = 1300 };
            w.Show();
            w.StartNewGame(3, 2, 10, hideHands: false, seed: 11);
            // Same path as the House rules button: copy the rules into the draft and open the dialog.
            var type = typeof(MainWindow);
            type.GetField("_draft", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(w, w.Controller.Game.Rules.Clone());
            var modal = type.GetNestedType("Modal", System.Reflection.BindingFlags.NonPublic);
            type.GetMethod("OpenModal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(w, new[] { Enum.Parse(modal, "Rules") });
            Snap(w, "01b-house-rules");
        }

        [AvaloniaTest]
        public void Board_AtTheStartOfSetup()
        {
            MainWindow w = Open();
            w.StartNewGame(3, 2, 10, hideHands: false, seed: 11);
            Snap(w, "02-setup-spots");
        }

        [AvaloniaTest]
        public void Board_AfterSetup_WithPiecesAndHand()
        {
            MainWindow w = Open();
            w.StartNewGame(4, 2, 10, hideHands: false, seed: 11);
            var c = w.Controller;
            while (c.Game.Phase == Phase.SetupSettlement || c.Game.Phase == Phase.SetupRoad) c.ClickSpot(c.Spots[c.Spots.Count / 2]);
            c.Game.GrantResources(c.Game.CurrentPlayer, new ResourceSet(2, 2, 2, 2, 2));
            c.Game.ForcePhase(Phase.Main);
            c.SelectTool(Tool.Road);
            Snap(w, "03-main-road-tool");
        }

        [AvaloniaTest]
        public void Board_Large()
        {
            MainWindow w = Open();
            w.StartNewGame(3, 4, 10, hideHands: false, seed: 5);
            Snap(w, "04-large-board");
        }

        [AvaloniaTest]
        public void Dialogs_BankTrade_Rules_Handoff()
        {
            MainWindow w = Open();
            w.StartNewGame(3, 2, 10, hideHands: true, seed: 3);
            Snap(w, "05-handoff");
            w.Controller.AcknowledgeHandoff();
            var c = w.Controller;
            // Skip ahead to the main phase so the action bar is fully populated.
            while (c.Game.Phase == Phase.SetupSettlement || c.Game.Phase == Phase.SetupRoad) c.ClickSpot(c.Spots[0]);
            c.AcknowledgeHandoff();
            c.Game.ForcePhase(Phase.Main);
            c.SelectTool(Tool.None);
            Snap(w, "06-main-actions");
        }
    }
}
