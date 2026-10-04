using System;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Catan.Client.Animation;
using Catan.Core;
using Catan.Local;
using Catan.Local.Tests;
using NUnit.Framework;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Catan.Local.Tests
{
    public static class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<LocalApp>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    public class LocalOptionsTests
    {
        [Test]
        public void NoArgumentsOpensTheNewGameScreen()
        {
            var o = LocalOptions.Parse(Array.Empty<string>());
            Assert.That(o.QuickStart, Is.False);
            Assert.That(o.Players, Is.EqualTo(3));
            Assert.That(o.HideHands, Is.True);
            Assert.That(o.Animations, Is.True);
        }

        [Test]
        public void GameOptionsStartStraightAway()
        {
            var o = LocalOptions.Parse(new[] { "--players", "4", "--radius", "3", "--vp", "12", "--seed", "42", "--show-hands", "--no-animations" });
            Assert.That(o.QuickStart, Is.True);
            Assert.That(o.Players, Is.EqualTo(4));
            Assert.That(o.Radius, Is.EqualTo(3));
            Assert.That(o.VictoryPoints, Is.EqualTo(12));
            Assert.That(o.Seed, Is.EqualTo(42));
            Assert.That(o.HideHands, Is.False);
            Assert.That(o.Animations, Is.False);
        }

        [Test]
        public void AnimationsAloneDoNotSkipTheNewGameScreen() =>
            Assert.That(LocalOptions.Parse(new[] { "--no-animations" }).QuickStart, Is.False);

        [TestCase("--players", "7")]
        [TestCase("--players", "1")]
        [TestCase("--radius", "9")]
        [TestCase("--vp", "two")]
        [TestCase("--seed")]
        [TestCase("--server", "example.com")]
        public void BadOptionsAreRejected(params string[] args) =>
            Assert.Throws<ArgumentException>(() => LocalOptions.Parse(args));
    }

    public class LocalWindowTests
    {
        [TearDown]
        public void RestoreAnimations() => AnimationLayer.Enabled = true;

        [AvaloniaTest]
        public void QuickStartOpensAGameWithTheRequestedSettings()
        {
            var window = LocalApp.OpenWindow(LocalOptions.Parse(new[] { "--players", "4", "--radius", "3", "--seed", "7", "--no-animations" }));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Game g = window.Controller.Game;
            Assert.That(g, Is.Not.Null);
            Assert.That(g.Players.Count, Is.EqualTo(4));
            Assert.That(g.Phase, Is.EqualTo(Phase.SetupSettlement));
            Assert.That(AnimationLayer.Enabled, Is.False);
            window.Close();
        }

        [AvaloniaTest]
        public void TheSameSeedGivesTheSameBoard()
        {
            string Board()
            {
                var w = LocalApp.OpenWindow(LocalOptions.Parse(new[] { "--seed", "123", "--no-animations" }));
                var hexes = string.Join(";", w.Controller.Game.Board.Tiles.Select(t => t.ToString()).OrderBy(t => t));
                w.Close();
                return hexes;
            }
            Assert.That(Board(), Is.EqualTo(Board()));
        }

        [AvaloniaTest]
        public void NoOptionsShowsTheNewGameScreen()
        {
            var window = LocalApp.OpenWindow(new LocalOptions());
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.That(window.Controller.Game, Is.Null);
            window.Close();
        }
    }
}
