using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Conquer.Core;
using Conquer.Core.Bots;
using Conquer.Core.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using NUnit.Framework;

namespace Conquer.Server.Tests
{
    /// <summary>The game client's online code (<see cref="OnlineSession"/> over <see cref="WebSocketLink"/>) against the real server.</summary>
    public class OnlineClientTests
    {
        WebApplicationFactory<Program> _factory;

        [SetUp]
        public void SetUp()
        {
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseSetting("Conquer:BehindProxy", "false");
                b.UseSetting("Conquer:MaxConnectionsPerIp", "50");
                b.UseSetting("Conquer:JoinAttemptBurst", "50");
                b.UseSetting("Conquer:BotMoveDelayMs", "0");
            });
        }

        [TearDown]
        public void TearDown() => _factory.Dispose();

        async Task<OnlineSession> Connect(string name, string password = "")
        {
            WebSocketClient ws = _factory.Server.CreateWebSocketClient();
            var socket = await ws.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
            return new OnlineSession(WebSocketLink.Over(socket), name, password);
        }

        static async Task Until(Func<bool> condition, string what, int timeoutMs = 5000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) Assert.Fail("Timed out waiting for " + what);
                await Task.Delay(10);
            }
        }

        [Test]
        public async Task CreateJoinStart_EveryoneSeesTheGame()
        {
            OnlineSession host = await Connect("Ann");
            host.CreateRoom(4);
            await Until(() => host.Seat == 0 && host.RoomCode != null, "the room");
            Assert.AreEqual(OnlineStatus.Lobby, host.Status);
            Assert.IsTrue(host.IsHost);

            OnlineSession guest = await Connect("Bob");
            guest.JoinRoom(host.RoomCode.ToLowerInvariant());
            await Until(() => guest.Seat == 1 && host.Players.Count == 2, "the guest");
            Assert.IsFalse(guest.IsHost);
            CollectionAssert.AreEqual(host.Players, guest.Players);

            host.Start(2, new HouseRules { VictoryPoints = 6 });
            await Until(() => host.Game != null && guest.Game != null, "snapshots");
            Assert.AreEqual(OnlineStatus.Playing, guest.Status);
            Assert.AreEqual(6, guest.Game.Rules.VictoryPoints);
            Assert.AreEqual(2, guest.Game.Players.Count);

            // The host's first village reaches the guest.
            Vertex spot = host.Game.LegalSetupVertices().First();
            host.Send(new SetupVillage(0, spot));
            await Until(() => guest.Game.Players[0].Villages.Contains(spot), "the guest to see the village");
        }

        [Test]
        public async Task BadRoomCode_ReportsTheErrorWithoutASeat()
        {
            OnlineSession s = await Connect("Ann");
            string error = null;
            s.ErrorReceived += e => error = e;
            s.JoinRoom("NOPE99");
            await Until(() => error != null, "an error");
            Assert.AreEqual(-1, s.Seat);
            StringAssert.Contains("No room", error);
        }

        [Test]
        public async Task ChatGoesToEveryone()
        {
            OnlineSession host = await Connect("Ann");
            host.CreateRoom(2);
            await Until(() => host.Seat == 0, "the room");
            OnlineSession guest = await Connect("Bob");
            guest.JoinRoom(host.RoomCode);
            await Until(() => guest.Seat == 1, "the guest");

            var heard = new ConcurrentQueue<string>();
            host.ChatReceived += heard.Enqueue;
            guest.Chat("hello <b>there</b>");
            await Until(() => heard.Count > 0, "chat");
            heard.TryDequeue(out string line);
            Assert.AreEqual("Bob: hello bthere/b", line);
        }

        [Test]
        public async Task DroppedPlayer_RejoinsTheirSeatWithTheToken()
        {
            OnlineSession host = await Connect("Ann");
            host.CreateRoom(2);
            await Until(() => host.Seat == 0, "the room");
            OnlineSession guest = await Connect("Bob");
            guest.JoinRoom(host.RoomCode);
            await Until(() => guest.Seat == 1, "the guest");
            host.Start(2, new HouseRules());
            await Until(() => guest.Game != null, "a snapshot");

            // Wait until the server has seen the drop too: the client marks itself disconnected at once, but a
            // rejoin that reaches the room before the old socket's close is refused as "already connected".
            var log = new ConcurrentQueue<string>();
            host.LogReceived += lines => { foreach (string l in lines) log.Enqueue(l); };
            guest.Leave();
            await Until(() => guest.Status == OnlineStatus.Disconnected, "the drop");
            await Until(() => log.Contains("Bob left."), "the server to see the drop");

            OnlineSession back = await Connect("Bob");
            back.Rejoin(guest.RoomCode, guest.Token);
            await Until(() => back.Game != null, "the rejoined snapshot");
            Assert.AreEqual(1, back.Seat);
            Assert.AreEqual(OnlineStatus.Playing, back.Status);
        }

        [Test, Timeout(240000)]
        public async Task TwoPeoplePlayAWholeGameOnline()
        {
            OnlineSession ann = await Connect("Ann");
            ann.CreateRoom(2);
            await Until(() => ann.Seat == 0, "the room");
            OnlineSession bob = await Connect("Bob");
            bob.JoinRoom(ann.RoomCode);
            await Until(() => bob.Seat == 1, "the guest");

            // Each player's moves come from the bot logic, decided on their own snapshot only, like a person would.
            Task annPlays = Play(ann, seed: 7);
            Task bobPlays = Play(bob, seed: 8);
            ann.Start(2, new HouseRules { VictoryPoints = 5 });
            await Task.WhenAll(annPlays, bobPlays);

            Assert.AreEqual(Phase.GameOver, bob.Game.Phase);
            Assert.AreEqual(ann.Game.Winner, bob.Game.Winner);
            TestContext.WriteLine($"{ann.Players[ann.Game.Winner]} won on turn {ann.Game.Turn}.");
        }

        /// <summary>Plays one seat to the end of the game, paced under the server's command rate limit.</summary>
        static async Task Play(OnlineSession me, int seed)
        {
            var brain = new BotPlayer(me.Seat, BotDifficulty.Normal, seed);
            var changed = new SemaphoreSlim(0);
            int refused = 0, refusedSeen = 0;
            me.SnapshotReceived += _ => changed.Release();
            me.ErrorReceived += _ =>
            {
                Interlocked.Increment(ref refused);
                changed.Release();
            };

            Game actedOn = null;
            while (me.Game?.Phase != Phase.GameOver)
            {
                Assert.IsTrue(await changed.WaitAsync(10000), $"{me.Name} stalled in {me.Game?.Phase} with player {me.Game?.CurrentPlayer} to move");
                // Let the dust settle, then act on the latest state only.
                await Task.Delay(150);
                while (changed.CurrentCount > 0) changed.Wait(0);
                Game g = me.Game;
                bool wasRefused = refused != refusedSeen;
                refusedSeen = refused;
                if (g == null || (g == actedOn && !wasRefused)) continue;
                actedOn = g;
                Command c = wasRefused ? brain.Fallback(g) : brain.Decide(g);
                if (c != null) me.Send(c);
            }
            Assert.That(refused, Is.LessThan(10), $"{me.Name}'s moves, decided on the latest snapshot, should almost always be legal");
        }
    }
}
