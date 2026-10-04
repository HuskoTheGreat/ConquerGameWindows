using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Catan.Core;
using Catan.Core.Net;
using NUnit.Framework;

namespace Catan.Client.Tests
{
    /// <summary>
    /// The window in online mode. A real <see cref="GameSession"/> plays the server's part in memory and its
    /// frames are handed to the client through a fake link, so no network is involved.
    /// </summary>
    public class OnlineWindowTests
    {
        sealed class FakeLink : IOnlineLink
        {
            public readonly List<byte[]> Sent = new List<byte[]>();
            public event Action<byte[]> Received;
            public event Action<string> Closed;
            public void Send(byte[] frame) => Sent.Add(frame);
            public void Close() => Closed?.Invoke("You left the room.");
            public void Deliver(byte[] frame) => Received?.Invoke(frame);
            public void Drop(string reason) => Closed?.Invoke(reason);
            public byte LastType => Sent.Last()[0];
            public byte[] LastPayload => Sent.Last().Skip(1).ToArray();
        }

        const ulong AnnId = 1, BobId = 2;

        static MainWindow Open()
        {
            var window = new MainWindow { Width = 1360, Height = 860 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        static Button Find(MainWindow w, string startsWith)
        {
            var overlay = w.GetVisualDescendants().OfType<Grid>().FirstOrDefault(g => g.Name == "Overlay" && g.IsVisible);
            Avalonia.Visual scope = overlay ?? (Avalonia.Visual)w;
            return scope.GetVisualDescendants().OfType<Button>()
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

        static bool Shows(MainWindow w, string text) =>
            w.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text != null && t.Text.Contains(text));

        static GameSession NewServer() => new GameSession(AnnId, "Ann", 4, "", () => 0);

        static (MainWindow w, OnlineSession bob, FakeLink link, GameSession server) BobInAStartedGame()
        {
            GameSession server = NewServer();
            Assert.IsTrue(server.Join(BobId, new JoinRequest { Name = "Bob" }.Encode()).Ok);
            Assert.IsNull(server.Start(AnnId, new StartSettings { Radius = 2 }));

            var link = new FakeLink();
            var bob = new OnlineSession(link, "Bob");
            bob.JoinRoom("ABC123");
            link.Deliver(Protocol.Frame(Protocol.Welcome, server.WelcomeFor(BobId)));

            MainWindow w = Open();
            w.PlayOnline(bob);
            link.Deliver(Protocol.Frame(Protocol.Snapshot, server.SnapshotFor(BobId)));
            Dispatcher.UIThread.RunJobs();
            return (w, bob, link, server);
        }

        static void ServerApplies(GameSession server, ulong client, Command command, FakeLink link)
        {
            SessionResponse r = server.HandleCommand(client, CommandCodec.Encode(command));
            Assert.IsTrue(r.Ok, r.Error);
            link.Deliver(Protocol.Frame(Protocol.Snapshot, server.SnapshotFor(BobId)));
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaTest]
        public void Lobby_ShowsTheCode_AndTheHostCanAddBotsAndStart()
        {
            GameSession server = NewServer();
            var link = new FakeLink();
            var ann = new OnlineSession(link, "Ann");
            ann.CreateRoom(4);
            Assert.AreEqual(Protocol.CreateRoom, link.LastType);
            link.Deliver(Protocol.RoomCreatedFrame("QX7K2P"));
            link.Deliver(Protocol.Frame(Protocol.Welcome, server.WelcomeFor(AnnId)));
            Assert.AreEqual(OnlineStatus.Lobby, ann.Status);

            MainWindow w = Open();
            w.PlayOnline(ann);
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(Shows(w, "QX7K2P"));
            Assert.IsFalse(Find(w, "Start game").IsEnabled, "one player can't start");

            Click(w, "Hard");
            Assert.AreEqual(Protocol.AddBot, link.LastType);
            Assert.AreEqual(Core.Bots.BotDifficulty.Hard, Protocol.DecodeAddBot(link.LastPayload));

            // The server seats the bot and sends a new welcome; now the host can start.
            Assert.IsNull(server.AddBot(AnnId, Core.Bots.BotDifficulty.Hard));
            link.Deliver(Protocol.Frame(Protocol.Welcome, server.WelcomeFor(AnnId)));
            Dispatcher.UIThread.RunJobs();
            Assert.IsTrue(ann.IsBotSeat(1));
            Assert.IsNotNull(Find(w, "Remove"));
            Click(w, "Start game");
            Assert.AreEqual(Protocol.Start, link.LastType);
        }

        [AvaloniaTest]
        public void Guest_WaitsForTheirTurn_ThenOnlyActsForTheirOwnSeat()
        {
            var (w, bob, link, server) = BobInAStartedGame();
            LocalGameController c = w.Controller;

            Assert.IsTrue(c.IsOnline);
            Assert.AreEqual(1, c.Actor, "the screen is Bob's");
            Assert.IsFalse(c.HandoffPending);
            Assert.IsEmpty(c.Spots, "nothing to click on Ann's turn");
            StringAssert.Contains("Waiting for Ann", c.Prompt());
            Assert.IsNull(Find(w, "House rules"), "only the host changes rules");
            Assert.IsNotNull(Find(w, "Leave game"));

            // Ann places her first settlement and road; then it's Bob's turn.
            Game seen = c.Game;
            ServerApplies(server, AnnId, new SetupSettlement(0, seen.LegalSetupVertices().First()), link);
            ServerApplies(server, AnnId, new SetupRoad(0, c.Game.LegalSetupRoadEdges().First()), link);
            Assert.AreEqual(1, c.Game.CurrentPlayer);
            Assert.IsNotEmpty(c.Spots);
            Assert.IsTrue(c.Game.Players[0].Settlements.Count == 1, "Ann's settlement arrived in the snapshot");

            // Clicking a spot sends Bob's move to the server, which accepts it.
            int sent = link.Sent.Count;
            c.ClickSpot(c.Spots[0]);
            Assert.AreEqual(sent + 1, link.Sent.Count);
            Assert.AreEqual(Protocol.Command, link.LastType);
            Assert.IsInstanceOf<SetupSettlement>(CommandCodec.Decode(link.LastPayload, 1));
            Assert.IsTrue(server.HandleCommand(BobId, link.LastPayload).Ok);

            // Moves for someone else's seat never leave the client.
            Assert.IsFalse(c.Send(new RollDice(0)));
        }

        [AvaloniaTest]
        public void ChatAndLogLines_ShowInTheLog()
        {
            var (w, bob, link, _) = BobInAStartedGame();
            link.Deliver(Protocol.Frame(Protocol.ChatLine, ChatCodec.EncodeBroadcast(0, "good luck")));
            link.Deliver(Protocol.Frame(Protocol.Log, LogCodec.Encode(new[] { "Ann rolled a 6." })));
            Dispatcher.UIThread.RunJobs();
            CollectionAssert.IsSubsetOf(new[] { "Ann: good luck", "Ann rolled a 6." }, w.Controller.Log.ToList());

            bob.Chat("you too");
            Assert.AreEqual(Protocol.Chat, link.LastType);
            Assert.AreEqual("you too", ChatCodec.DecodeSend(link.LastPayload));
        }

        [AvaloniaTest]
        public void LosingTheConnection_OffersToReconnect()
        {
            var (w, bob, link, _) = BobInAStartedGame();
            link.Drop("The connection to the server was lost.");
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(OnlineStatus.Disconnected, bob.Status);
            Assert.IsTrue(Shows(w, "Disconnected"));
            Assert.IsNotNull(Find(w, "Reconnect"));

            Click(w, "Back to menu");
            Assert.IsFalse(w.Controller.IsOnline);
            Assert.IsNotNull(Find(w, "Single player"));
        }

        /// <summary>
        /// The whole path through the real connect form and a real socket. Needs a running server, e.g.
        /// <c>dotnet run --project server/src/Catan.Server</c> and CATAN_LIVE_SERVER=127.0.0.1:5080.
        /// </summary>
        [AvaloniaTest]
        public void LiveServer_CreateRoomAddBotAndStartFromTheWindow()
        {
            string server = Environment.GetEnvironmentVariable("CATAN_LIVE_SERVER");
            if (string.IsNullOrEmpty(server)) Assert.Ignore("Set CATAN_LIVE_SERVER to run against a real server.");

            MainWindow w = Open();
            Click(w, "Online");
            w.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "Server").Text = server;
            w.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "Your name").Text = "Loren";
            Click(w, "Create room");
            Pump(() => !Shows(w, "Connecting..."), "the connection");
            Pump(() => w.Controller.IsOnline && w.Controller.Online.RoomCode != null, "the room");
            UiFlowTests.Snap(w, "online-1-lobby");

            Click(w, "Normal");
            Pump(() => w.Controller.Online.Players.Count == 2, "the bot");
            UiFlowTests.Snap(w, "online-2-lobby-with-bot");
            Click(w, "Start game");
            Pump(() => w.Controller.Game != null, "the first snapshot");
            Assert.AreEqual(0, w.Controller.Actor);
            Assert.IsNotEmpty(w.Controller.Spots);
            UiFlowTests.Snap(w, "online-3-playing");

            Click(w, "Leave game");
            Assert.IsFalse(w.Controller.IsOnline);
        }

        static void Pump(Func<bool> done, string what)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (!done())
            {
                if (DateTime.UtcNow > deadline)
                    Assert.Fail("Timed out waiting for " + what);
                Dispatcher.UIThread.RunJobs();
                System.Threading.Thread.Sleep(20);
            }
            Dispatcher.UIThread.RunJobs();
        }

        [Test]
        public void ServerAddresses_AreFilledInSensibly()
        {
            Assert.AreEqual("wss://catan.example.com/ws", WebSocketLink.ParseAddress("catan.example.com").ToString());
            Assert.AreEqual("ws://127.0.0.1:5080/ws", WebSocketLink.ParseAddress("127.0.0.1:5080").ToString());
            Assert.AreEqual("wss://catan.example.com/ws", WebSocketLink.ParseAddress("https://catan.example.com").ToString());
            Assert.AreEqual("ws://localhost:5080/custom", WebSocketLink.ParseAddress("ws://localhost:5080/custom").ToString());
            Assert.IsNull(WebSocketLink.ParseAddress(""));
            Assert.IsNull(WebSocketLink.ParseAddress("ftp://example.com"));
        }
    }
}
