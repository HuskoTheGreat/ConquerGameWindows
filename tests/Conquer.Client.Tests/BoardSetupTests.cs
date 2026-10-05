using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Conquer.Core;
using NUnit.Framework;

namespace Conquer.Client.Tests
{
    /// <summary>The board setup screen, driven through the real window.</summary>
    public class BoardSetupTests
    {
        static MainWindow Open()
        {
            var window = new MainWindow { Width = 1360, Height = 860 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        static Button Find(MainWindow w, string startsWith)
        {
            Grid overlay = w.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "Overlay");
            return overlay.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.IsVisible && b.Content is TextBlock t && t.Text != null && t.Text.StartsWith(startsWith, StringComparison.Ordinal));
        }

        static void Click(MainWindow w, string text)
        {
            Button b = Find(w, text);
            Assert.IsNotNull(b, $"no button starting with \"{text}\"");
            Assert.IsTrue(b.IsEnabled, $"button \"{text}\" is disabled");
            b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        static LocalGameController Preview(MainWindow w)
        {
            BoardControl preview = w.GetVisualDescendants().OfType<BoardControl>().First(b => b.Name == "BoardPreview");
            return preview.Controller;
        }

        [AvaloniaTest]
        public void ArrangedBoard_IsTheOneTheGameStartsOn()
        {
            MainWindow w = Open();
            Click(w, "Single player");
            Click(w, "Arrange board");
            Assert.IsNotNull(w.ArrangingBoard);

            // Pick the center tile, make it the first resource with a 2.
            LocalGameController p = Preview(w);
            Assert.IsTrue(p.IsArranging);
            Assert.AreEqual(19, p.Spots.Count, "every tile can be clicked");
            p.ClickSpot(p.Spots.First(s => s.Hex == Hex.Zero));
            Dispatcher.UIThread.RunJobs();
            Resource first = ResourceSet.Types[0];
            Click(w, first.ToString());
            Click(w, "2");
            UiFlowTests.Snap(w, "board-setup");

            Click(w, "Use this board");
            Assert.IsNotNull(w.CustomBoard);
            Assert.IsNotNull(Find(w, "Edit board"), "the new game screen says the board is arranged");

            Click(w, "Start game");
            Assert.IsTrue(w.Controller.Game.Board.TryGetTile(Hex.Zero, out Tile t));
            Assert.AreEqual((first, 2), (t.Resource, t.Number));
            Assert.IsFalse(w.Controller.IsArranging);
        }

        [AvaloniaTest]
        public void SwapMode_SwapsTwoTiles()
        {
            MainWindow w = Open();
            Click(w, "Single player");
            Click(w, "Arrange board");
            BoardDraft d = w.ArrangingBoard;
            Hex a = d.Hexes[1], b = d.Hexes[2];
            (Resource ra, int na) = (d.ResourceAt(a), d.NumberAt(a));

            w.GetVisualDescendants().OfType<CheckBox>().First(c => c.Content as string == "Swap tiles (click two tiles)").IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            LocalGameController p = Preview(w);
            p.ClickSpot(p.Spots.First(s => s.Hex == a));
            p.ClickSpot(p.Spots.First(s => s.Hex == b));
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual((ra, na), (d.ResourceAt(b), d.NumberAt(b)));
        }

        [AvaloniaTest]
        public void Cancel_KeepsARandomBoard_AndResizingForgetsAnArrangedOne()
        {
            MainWindow w = Open();
            Click(w, "Single player");
            Click(w, "Arrange board");
            Click(w, "Shuffle tiles");
            Click(w, "Cancel");
            Assert.IsNull(w.CustomBoard);

            Click(w, "Arrange board");
            Click(w, "Use this board");
            Assert.IsNotNull(w.CustomBoard);
            Click(w, "Use random");
            Assert.IsNull(w.CustomBoard);
        }
    }
}
