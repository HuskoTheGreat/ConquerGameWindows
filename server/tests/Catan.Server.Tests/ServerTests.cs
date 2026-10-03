using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Catan.Core;
using Catan.Core.Net;
using Catan.Server.Bots;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Catan.Server.Tests
{
    /// <summary>Runs the real server pipeline in memory and talks to it over WebSockets.</summary>
    public class ServerTests
    {
        sealed class FakeModel : ILlmBackend
        {
            public int Calls;
            public Task<string> CompleteAsync(IReadOnlyList<ChatTurn> messages, CancellationToken ct)
            {
                Interlocked.Increment(ref Calls);
                return Task.FromResult("Ahoy, well played!");
            }
        }

        sealed class Client : IDisposable
        {
            public WebSocket Socket;
            readonly byte[] _buffer = new byte[64 * 1024];

            public Task Send(byte[] frame) => Socket.SendAsync(frame, WebSocketMessageType.Binary, true, CancellationToken.None);

            public async Task<byte[]> Receive(int timeoutMs = 5000)
            {
                using var cts = new CancellationTokenSource(timeoutMs);
                int count = 0;
                WebSocketReceiveResult r;
                do
                {
                    r = await Socket.ReceiveAsync(new ArraySegment<byte>(_buffer, count, _buffer.Length - count), cts.Token);
                    if (r.MessageType == WebSocketMessageType.Close) return null;
                    count += r.Count;
                } while (!r.EndOfMessage);
                return _buffer.Take(count).ToArray();
            }

            /// <summary>Reads until a frame of <paramref name="type"/> arrives; returns its payload.</summary>
            public async Task<byte[]> Expect(byte type)
            {
                while (true)
                {
                    byte[] f = await Receive();
                    if (f == null) Assert.Fail($"Closed while waiting for 0x{type:X2}.");
                    if (f[0] == type) return f.Skip(1).ToArray();
                }
            }

            public void Dispose() => Socket.Dispose();
        }

        WebApplicationFactory<Program> _factory;
        FakeModel _model;

        [SetUp]
        public void SetUp()
        {
            _model = new FakeModel();
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseSetting("Catan:BehindProxy", "false");
                b.UseSetting("Catan:Bots:Enabled", "true");
                b.UseSetting("Catan:Bots:ReplyCooldownSeconds", "0");
                b.UseSetting("Catan:MaxConnectionsPerIp", "50");
                b.UseSetting("Catan:JoinAttemptBurst", "50");
                b.ConfigureTestServices(s => s.AddSingleton<ILlmBackend>(_model));
            });
        }

        [TearDown]
        public void TearDown() => _factory.Dispose();

        async Task<Client> Connect()
        {
            WebSocketClient ws = _factory.Server.CreateWebSocketClient();
            return new Client { Socket = await ws.ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None) };
        }

        static JoinRequest Join(string name, string password = "") => new JoinRequest { Name = name, Password = password };

        static string ReadString(byte[] payload)
        {
            var r = new WireReader(payload, 4096);
            return r.String(4096);
        }

        async Task<(Client host, string code)> CreateRoom(int bots = 0, string password = "")
        {
            Client host = await Connect();
            await host.Send(Protocol.EncodeCreate(4, bots, Join("Ann", password)));
            string code = ReadString(await host.Expect(Protocol.RoomCreated));
            await host.Expect(Protocol.Welcome);
            return (host, code);
        }

        [Test]
        public async Task CreateJoinStart_EveryoneGetsTheirOwnSnapshot()
        {
            var (host, code) = await CreateRoom();
            Assert.AreEqual(RoomRegistry.CodeLength, code.Length);

            using Client guest = await Connect();
            await guest.Send(Protocol.EncodeJoin(code.ToLowerInvariant(), Join("Bob")));
            Welcome w = Welcome.Decode(await guest.Expect(Protocol.Welcome));
            Assert.AreEqual(1, w.Seat);
            CollectionAssert.AreEqual(new[] { "Ann", "Bob" }, w.Names);

            await host.Send(Protocol.EncodeStart(2, new HouseRules()));
            Game hostView = SnapshotCodec.Decode(await host.Expect(Protocol.Snapshot));
            Game guestView = SnapshotCodec.Decode(await guest.Expect(Protocol.Snapshot));
            Assert.AreEqual(Phase.SetupSettlement, hostView.Phase);
            Assert.AreEqual(hostView.Board.Tiles.Count, guestView.Board.Tiles.Count);

            // Host places the first settlement; both see it.
            Vertex spot = hostView.LegalSetupVertices().First();
            await host.Send(Protocol.Frame(Protocol.Command, CommandCodec.Encode(new SetupSettlement(0, spot))));
            Game after = SnapshotCodec.Decode(await guest.Expect(Protocol.Snapshot));
            Assert.IsTrue(after.Buildings.ContainsKey(spot));
            host.Dispose();
        }

        [Test]
        public async Task GuestCantActForTheHost_NorStartTheGame()
        {
            var (host, code) = await CreateRoom();
            using Client guest = await Connect();
            await guest.Send(Protocol.EncodeJoin(code, Join("Bob")));
            await guest.Expect(Protocol.Welcome);

            await guest.Send(Protocol.EncodeStart(2, new HouseRules()));
            StringAssert.Contains("host", ReadString(await guest.Expect(Protocol.Error)));
            host.Dispose();
        }

        [Test]
        public async Task WrongCode_AndCommandsBeforeJoining_AreRefused()
        {
            using Client c = await Connect();
            await c.Send(Protocol.EncodeJoin("ZZZZZZZZZ", Join("Eve")));
            StringAssert.Contains("No room", ReadString(await c.Expect(Protocol.Error)));

            await c.Send(Protocol.Frame(Protocol.Command, CommandCodec.Encode(new RollDice(0))));
            StringAssert.Contains("first", ReadString(await c.Expect(Protocol.Error)));
        }

        [Test]
        public async Task OversizedMessage_DropsTheClient()
        {
            using Client c = await Connect();
            await c.Send(new byte[10_000]);
            byte[] f;
            do f = await c.Receive(); while (f != null && f[0] == Protocol.Error);
            Assert.IsNull(f, "connection closed");
        }

        [Test]
        public async Task Chat_IsRelayedWithTheSendersSeat_AndBotsAnswerWhenAddressed()
        {
            var (host, code) = await CreateRoom(bots: 1);
            string[] bots;
            {
                var r = new WireReader(await host.Expect(Protocol.Bots), 1024);
                bots = Enumerable.Range(0, r.Byte()).Select(_ => r.String(160)).ToArray();
            }
            Assert.AreEqual(1, bots.Length);

            using Client guest = await Connect();
            await guest.Send(Protocol.EncodeJoin(code, Join("Bob")));
            await guest.Expect(Protocol.Welcome);

            await guest.Send(Protocol.Frame(Protocol.Chat, ChatCodec.EncodeSend($"hi {bots[0].Split(' ')[0]}!")));
            ChatCodec.DecodeBroadcast(await host.Expect(Protocol.ChatLine), out int seat, out string text);
            Assert.AreEqual(1, seat);

            var bot = new WireReader(await host.Expect(Protocol.BotChat), 1024);
            Assert.AreEqual(bots[0], bot.String(160));
            Assert.AreEqual("Ahoy, well played!", bot.String(800));
            Assert.AreEqual(1, _model.Calls);
            host.Dispose();
        }

        [Test]
        public async Task ConcurrentPlayers_AreProcessedOneAtATime()
        {
            var (host, code) = await CreateRoom();
            var guests = new List<Client>();
            for (int i = 0; i < 3; i++)
            {
                Client g = await Connect();
                await g.Send(Protocol.EncodeJoin(code, Join("G" + i)));
                await g.Expect(Protocol.Welcome);
                guests.Add(g);
            }

            // Everyone fires chat at once; every line must arrive intact at the host.
            await Task.WhenAll(guests.Select((g, i) =>
                Task.WhenAll(Enumerable.Range(0, 4).Select(n => g.Send(Protocol.Frame(Protocol.Chat, ChatCodec.EncodeSend($"m{i}-{n}")))))));

            var seen = new HashSet<string>();
            while (seen.Count < 12)
            {
                ChatCodec.DecodeBroadcast(await host.Expect(Protocol.ChatLine), out _, out string t);
                seen.Add(t);
            }
            Assert.AreEqual(12, seen.Count);
            foreach (Client g in guests) g.Dispose();
            host.Dispose();
        }
    }
}
