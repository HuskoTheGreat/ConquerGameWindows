using System;
using System.Linq;
using System.Net;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Conquer.Core;
using Conquer.Core.Net;
using NUnit.Framework;

namespace Conquer.Client.Tests
{
    /// <summary>Hosting a game from the window and joining it over the network, with a real server and sockets.</summary>
    public class LanTests
    {
        static MainWindow Open()
        {
            var window = new MainWindow { Width = 1360, Height = 860 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        static void Click(MainWindow w, string text)
        {
            // While a dialog is open only its buttons count.
            Avalonia.Visual scope = w.GetVisualDescendants().OfType<Grid>().FirstOrDefault(g => g.Name == "Overlay" && g.IsVisible) ?? (Avalonia.Visual)w;
            Button b = scope.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(x => x.IsVisible && x.Content is TextBlock t && t.Text != null && t.Text.StartsWith(text, StringComparison.Ordinal));
            Assert.IsNotNull(b, $"no button starting with \"{text}\"");
            Assert.IsTrue(b.IsEnabled, $"button \"{text}\" is disabled");
            b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        static void SetName(MainWindow w, string name) =>
            w.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "Your name").Text = name;

        static void Pump(Func<bool> done, string what, double seconds = 10)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (!done())
            {
                if (DateTime.UtcNow > deadline) Assert.Fail("Timed out waiting for " + what);
                Dispatcher.UIThread.RunJobs();
                System.Threading.Thread.Sleep(20);
            }
            Dispatcher.UIThread.RunJobs();
        }

        [SetUp]
        public void NoSavedSettings() => OnlineSettings.FilePath = null;

        [Test]
        public void Announcement_RoundTrips_AndTrustsOnlyTheSender()
        {
            var game = new LanGame { Address = IPAddress.Parse("8.8.8.8"), Port = 47620, Code = "ABC234XYZ", Host = "Loren\nX", Players = 1, Seats = 4 };
            LanGame back = LanGame.Decode(game.Encode(), IPAddress.Parse("192.168.1.20"), DateTime.UtcNow);
            Assert.IsNotNull(back);
            Assert.AreEqual("192.168.1.20:47620", back.Server, "the address is the sender's, not anything in the packet");
            Assert.AreEqual(("ABC234XYZ", "LorenX", 1, 4), (back.Code, back.Host, back.Players, back.Seats));

            IPAddress from = IPAddress.Loopback;
            Assert.IsNull(LanGame.Decode(Encoding.UTF8.GetBytes("hello"), from, DateTime.UtcNow));
            Assert.IsNull(LanGame.Decode(Encoding.UTF8.GetBytes("CONQUER-LAN 1\n0\nABC\nX\n1\n4"), from, DateTime.UtcNow), "bad port");
            Assert.IsNull(LanGame.Decode(Encoding.UTF8.GetBytes("CONQUER-LAN 1\n5\nA B\nX\n1\n4"), from, DateTime.UtcNow), "bad code");
            Assert.IsNull(LanGame.Decode(Encoding.UTF8.GetBytes("CONQUER-LAN 1\n5\nABC\nX\n9\n4"), from, DateTime.UtcNow), "more players than seats");
            Assert.IsNull(LanGame.Decode(new byte[600], from, DateTime.UtcNow), "too big");
            Assert.IsNull(LanGame.Decode(new byte[] { 0xff, 0xfe }, from, DateTime.UtcNow), "not UTF-8");
        }

        [Test]
        public void PrivateAddresses_UsePlainWebSockets()
        {
            Assert.AreEqual("ws://172.20.1.5:47620/ws", WebSocketLink.ParseAddress("172.20.1.5:47620").ToString());
            Assert.AreEqual("ws://192.168.0.9:47620/ws", WebSocketLink.ParseAddress("192.168.0.9:47620").ToString());
            Assert.AreEqual("wss://172.32.1.5/ws", WebSocketLink.ParseAddress("172.32.1.5").ToString());
            Assert.AreEqual("wss://10.example.com/ws", WebSocketLink.ParseAddress("10.example.com").ToString());
        }

        [AvaloniaTest]
        public void HostOnLan_FriendJoins_PlaysOnTheArrangedBoard_HostLeaving_StopsTheServer()
        {
            MainWindow host = Open();
            Click(host, "Local network");
            SetName(host, "Loren");
            Click(host, "Host game");
            Pump(() => host.Controller.IsOnline && host.Controller.Online.RoomCode != null, "the hosted room");
            Assert.IsNotNull(host.LanPort);
            UiFlowTests.Snap(host, "lan-1-host-lobby");

            // A friend on another computer picks the game from their list.
            MainWindow friend = Open();
            Click(friend, "Local network");
            SetName(friend, "Sam");
            friend.JoinLan(new LanGame { Address = IPAddress.Loopback, Port = host.LanPort.Value, Code = host.Controller.Online.RoomCode, Host = "Loren", Seats = 4 });
            Pump(() => friend.Controller.IsOnline && host.Controller.Online.Players.Count == 2, "the friend to join");

            // The host arranges a bigger board than the public server allows, and starts.
            Click(host, "+"); // board radius 3
            Click(host, "+"); // 4
            Click(host, "Arrange board");
            Click(host, "Use this board");
            Board arranged = host.CustomBoard;
            Assert.AreEqual(4, arranged.Radius);
            Click(host, "Start game");
            Pump(() => host.Controller.Game != null && friend.Controller.Game != null, "the first snapshots");
            string Layout(Board b) => string.Join(",", b.Tiles.OrderBy(t => t.Hex.Q).ThenBy(t => t.Hex.R).Select(t => t.ToString()));
            Assert.AreEqual(Layout(arranged), Layout(friend.Controller.Game.Board));
            UiFlowTests.Snap(friend, "lan-2-friend-playing");

            Click(host, "Leave game");
            Assert.IsNull(host.LanPort);
            Pump(() => friend.Controller.Online.Status == OnlineStatus.Disconnected, "the friend to notice", 15);
            friend.Close();
            host.Close();
        }

        [AvaloniaTest]
        public void HostedGame_IsAnnouncedOnTheNetwork()
        {
            MainWindow host = Open();
            Click(host, "Local network");
            SetName(host, "Loren");
            Click(host, "Host game");
            Pump(() => host.Controller.IsOnline && host.Controller.Online.RoomCode != null, "the hosted room");

            MainWindow friend = Open();
            Click(friend, "Local network");
            Pump(() => friend.LanGames.Any(g => g.Code == host.Controller.Online.RoomCode), "the announcement");
            UiFlowTests.Snap(friend, "lan-0-games-list");
            Click(friend, "Join");
            Pump(() => host.Controller.Online.Players.Count == 2, "the friend to join");
            host.Close();
            friend.Close();
        }
    }
}
