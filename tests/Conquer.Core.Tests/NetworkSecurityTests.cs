using System;
using System.Collections.Generic;
using System.Linq;
using Conquer.Core.Net;
using NUnit.Framework;
using static Conquer.Core.Tests.GameTestKit;

namespace Conquer.Core.Tests
{
    public class NetworkSecurityTests
    {
        // ---- Codec ---------------------------------------------------------------------------------

        static IEnumerable<Command> SampleCommands(Game g)
        {
            Vertex v = g.Board.Vertices.First();
            Edge e = g.Board.Edges.First();
            Hex h = g.Board.Tiles.First().Hex;
            yield return new SetupVillage(0, v);
            yield return new SetupRoad(0, e);
            yield return new RollDice(0);
            yield return new DiscardCards(0, new ResourceSet(1, 2, 3, 4, 5));
            yield return new MoveRaider(0, h);
            yield return new StealFrom(0, 2);
            yield return new BuildRoad(0, e);
            yield return new BuildVillage(0, v);
            yield return new BuildCity(0, v);
            yield return new BuyActionCard(0);
            yield return new PlaySoldier(0);
            yield return new PlayEngineers(0);
            yield return new PlayHarvest(0, Resource.Timber, Resource.Iron);
            yield return new PlayPlunder(0, Resource.Livestock);
            yield return new BankTrade(0, Resource.Grain, Resource.Clay);
            yield return new ProposeTrade(0, new ResourceSet(timber: 2), new ResourceSet(iron: 1));
            yield return new AcceptTrade(0);
            yield return new CancelTrade(0);
            yield return new EndTurn(0);
            yield return new SetHouseRules(0, new HouseRules { VictoryPoints = 12, FriendlyRaider = true });
        }

        [Test]
        public void Codec_RoundTripsEveryCommand_AndStampsTheHostAssignedSeat()
        {
            Game g = New();
            foreach (Command original in SampleCommands(g))
            {
                byte[] bytes = CommandCodec.Encode(original);
                Command decoded = CommandCodec.Decode(bytes, seat: 2);
                Assert.AreEqual(original.GetType(), decoded.GetType());
                Assert.AreEqual(2, decoded.Player, "player must come from the connection, not the payload");
                CollectionAssert.AreEqual(bytes, CommandCodec.Encode(decoded), original.GetType().Name);
            }
        }

        [Test]
        public void Codec_RejectsTruncatedAndPaddedMessages()
        {
            Game g = New();
            foreach (Command c in SampleCommands(g))
            {
                byte[] bytes = CommandCodec.Encode(c);
                for (int cut = 0; cut < bytes.Length; cut++)
                    Assert.Throws<WireException>(() => CommandCodec.Decode(bytes.Take(cut).ToArray(), 0), $"{c.GetType().Name} cut {cut}");
                Assert.Throws<WireException>(() => CommandCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray(), 0), "trailing byte");
            }
        }

        [Test]
        public void Codec_RejectsOversizedUnknownAndOutOfRangeData()
        {
            Assert.Throws<WireException>(() => CommandCodec.Decode(new byte[CommandCodec.MaxBytes + 1], 0));
            Assert.Throws<WireException>(() => CommandCodec.Decode(new byte[0], 0));
            Assert.Throws<WireException>(() => CommandCodec.Decode(new byte[] { 0 }, 0));
            Assert.Throws<WireException>(() => CommandCodec.Decode(new byte[] { 250 }, 0));

            // Hex far outside any board.
            Assert.Throws<WireException>(() => CommandCodec.Decode(new byte[] { 5, 255, 255 }, 0)); // MoveRaider (-> q=127)
            // Edge between non-adjacent hexes.
            var w = new WireWriter();
            w.Byte(7);
            w.Write(new Hex(0, 0));
            w.Write(new Hex(5, 5));
            Assert.Throws<WireException>(() => CommandCodec.Decode(w.ToArray(), 0));
            // Resource enum out of range.
            Assert.Throws<WireException>(() => CommandCodec.Decode(new byte[] { 14, 9, 1 }, 0));
            // Seat out of range for StealFrom.
            Assert.Throws<WireException>(() => CommandCodec.Decode(new byte[] { 6, 99 }, 0));
        }

        [Test]
        public void Codec_RejectsInvalidHouseRules()
        {
            var w = new WireWriter();
            w.Byte(20);
            w.Write(new HouseRules { VictoryPoints = 10 });
            byte[] good = w.ToArray();
            Assert.DoesNotThrow(() => CommandCodec.Decode(good, 0));

            byte[] bad = (byte[])good.Clone();
            bad[1] = 0; // victory points 0
            Assert.Throws<WireException>(() => CommandCodec.Decode(bad, 0));
        }

        [Test]
        public void Codec_Fuzz_OnlyEverThrowsWireException()
        {
            var rnd = new Random(1234);
            Game g = New();
            var seeds = SampleCommands(g).Select(CommandCodec.Encode).ToList();

            for (int i = 0; i < 20000; i++)
            {
                byte[] data;
                if (i % 2 == 0)
                {
                    data = new byte[rnd.Next(0, 40)];
                    rnd.NextBytes(data);
                }
                else
                {
                    data = (byte[])seeds[rnd.Next(seeds.Count)].Clone();
                    data[rnd.Next(data.Length)] = (byte)rnd.Next(256);
                }

                try
                {
                    CommandCodec.Decode(data, rnd.Next(0, 6));
                }
                catch (WireException)
                {
                }
            }
        }

        // ---- Snapshots and privacy -----------------------------------------------------------------

        static Game PlayedGame()
        {
            Game g = New(players: 3, seed: 11);
            RunSetup(g);
            g.ForcePhase(Phase.Main);
            g.GrantResources(1, new ResourceSet(iron: 7));
            g.GrantActionCard(1, ActionCard.Soldier);
            g.GrantActionCard(2, ActionCard.VictoryPoint);
            return g;
        }

        [Test]
        public void Snapshot_HidesOtherPlayersCardsAndSecrets()
        {
            Game real = PlayedGame();
            Game view = SnapshotCodec.Decode(SnapshotCodec.Encode(real, viewerSeat: 0));

            Assert.IsTrue(view.IsMirror);
            Assert.AreEqual(real.Players[0].Hand, view.Players[0].Hand);
            foreach (int other in new[] { 1, 2 })
            {
                Assert.AreEqual(ResourceSet.Empty, view.Players[other].Hand, "opponent hand must not be sent");
                Assert.AreEqual(0, view.Players[other].ActionCardsTotal(ActionCard.Soldier));
                Assert.AreEqual(0, view.Players[other].ActionCardsTotal(ActionCard.VictoryPoint));
                Assert.AreEqual(real.Players[other].HandCount, view.Players[other].HandCount, "counts are public");
                Assert.AreEqual(real.Players[other].ActionCardCount, view.Players[other].ActionCardCount);
            }
            Assert.AreEqual(real.Bank, view.Bank);
            Assert.AreEqual(real.DevDeckCount, view.DevDeckCount);
        }

        [Test]
        public void Snapshot_NeverContainsGameSeedOrFutureCards()
        {
            Game real = New(players: 2, seed: 0x5EC4E7);
            RunSetup(real);
            byte[] bytes = SnapshotCodec.Encode(real, 0);
            byte[] secret = BitConverter.GetBytes(real.Config.Seed);

            for (int i = 0; i + 4 <= bytes.Length; i++)
                Assert.IsFalse(bytes.Skip(i).Take(4).SequenceEqual(secret), "game seed leaked into snapshot");

            Game view = SnapshotCodec.Decode(bytes);
            Assert.AreEqual(0, view.Config.Seed);
        }

        [Test]
        public void Snapshot_RoundTripsPublicStateAndLegalMoves()
        {
            Game real = PlayedGame();
            Game view = SnapshotCodec.Decode(SnapshotCodec.Encode(real, 0));

            Assert.AreEqual(real.Phase, view.Phase);
            Assert.AreEqual(real.CurrentPlayer, view.CurrentPlayer);
            Assert.AreEqual(real.Turn, view.Turn);
            Assert.AreEqual(real.RaiderHex, view.RaiderHex);
            CollectionAssert.AreEquivalent(real.Buildings, view.Buildings);
            CollectionAssert.AreEquivalent(real.RoadOwners, view.RoadOwners);
            CollectionAssert.AreEquivalent(real.LegalRoadEdges(0), view.LegalRoadEdges(0));
            CollectionAssert.AreEquivalent(real.LegalVillageVertices(0), view.LegalVillageVertices(0));
            Assert.AreEqual(real.GetBankRatio(0, Resource.Iron), view.GetBankRatio(0, Resource.Iron));
            for (int i = 0; i < 3; i++) Assert.AreEqual(real.PublicVictoryPoints(i), view.PublicVictoryPoints(i));
            Assert.AreEqual(real.Board.Tiles.Count, view.Board.Tiles.Count);
            Assert.AreEqual(real.Board.Ports.Count, view.Board.Ports.Count);
        }

        [Test]
        public void Mirror_CannotApplyCommands()
        {
            Game view = SnapshotCodec.Decode(SnapshotCodec.Encode(PlayedGame(), 0));
            Fails(view, new EndTurn(0));
        }

        [Test]
        public void Snapshot_SetupRoadPhase_MirrorKnowsWhichVillageNeedsARoad()
        {
            Game g = New(players: 2);
            Ok(g, new SetupVillage(0, g.LegalSetupVertices().First()));
            Game view = SnapshotCodec.Decode(SnapshotCodec.Encode(g, 0));
            CollectionAssert.AreEquivalent(g.LegalSetupRoadEdges(), view.LegalSetupRoadEdges());
        }

        [Test]
        public void Snapshot_Decode_Fuzz_OnlyEverThrowsWireException()
        {
            byte[] valid = SnapshotCodec.Encode(PlayedGame(), 1);
            var rnd = new Random(77);

            for (int i = 0; i < 6000; i++)
            {
                byte[] data = (byte[])valid.Clone();
                int edits = 1 + rnd.Next(4);
                for (int k = 0; k < edits; k++) data[rnd.Next(data.Length)] = (byte)rnd.Next(256);
                if (i % 5 == 0) data = data.Take(rnd.Next(data.Length)).ToArray();

                try
                {
                    SnapshotCodec.Decode(data);
                }
                catch (WireException)
                {
                }
            }
        }

        // ---- Sanitizing and primitives -------------------------------------------------------------

        [Test]
        public void Names_AreStrippedOfMarkupControlCharsAndLength()
        {
            Assert.AreEqual("bredb", NameSanitizer.Clean("<b>red</b>", "x").Replace("/", ""));
            Assert.AreEqual("x", NameSanitizer.Clean("   \u0007​  ", "x"));
            Assert.AreEqual("Al Bo", NameSanitizer.Clean("  Al \t\n  Bo ", "x"));
            Assert.LessOrEqual(NameSanitizer.Clean(new string('a', 500), "x").Length, NameSanitizer.MaxLength);
            Assert.AreEqual("x", NameSanitizer.Clean(null, "x"));
            Assert.IsFalse(NameSanitizer.Clean("<color=red>Hax</color>", "x").Contains("<"));
        }

        [Test]
        public void ConstantTimeEquals_Works()
        {
            Assert.IsTrue(ConstantTime.Equals(new byte[] { 1, 2 }, new byte[] { 1, 2 }));
            Assert.IsFalse(ConstantTime.Equals(new byte[] { 1, 2 }, new byte[] { 1, 3 }));
            Assert.IsFalse(ConstantTime.Equals(new byte[] { 1, 2 }, new byte[] { 1, 2, 3 }));
            Assert.IsFalse(ConstantTime.Equals((byte[])null, new byte[0]));
            Assert.IsTrue(ConstantTime.Equals("pw", "pw"));
        }

        [Test]
        public void RateLimiter_AllowsBurstThenRefills()
        {
            double now = 0;
            var limiter = new RateLimiter(2, 4, () => now);
            Assert.AreEqual(4, Enumerable.Range(0, 10).Count(_ => limiter.Allow(1)));
            Assert.IsTrue(limiter.Allow(2), "separate bucket per client");
            now += 1.0; // 2 tokens back
            Assert.AreEqual(2, Enumerable.Range(0, 10).Count(_ => limiter.Allow(1)));
        }

        [Test]
        public void SecureRandom_ProducesDistinctValues()
        {
            Assert.AreNotEqual(Convert.ToBase64String(SecureRandom.Bytes(16)), Convert.ToBase64String(SecureRandom.Bytes(16)));
        }

        // ---- Session: joining ----------------------------------------------------------------------

        sealed class Clock { public double Now; }

        static GameSession NewSession(Clock clock, string password = "", int max = 4) =>
            new GameSession(hostClientId: 100, hostName: "Host", maxPlayers: max, password: password, clock: () => clock.Now);

        static byte[] Req(string name = "Guest", string password = "", byte[] token = null, byte version = GameSession.ProtocolVersion) =>
            new JoinRequest { Name = name, Password = password, Token = token ?? new byte[0], Version = version }.Encode();

        [Test]
        public void Join_AssignsSeatsInOrder_SanitizesAndDeduplicatesNames()
        {
            var s = NewSession(new Clock());
            JoinResult a = s.Join(1, Req("<b>Sam</b>"));
            JoinResult b = s.Join(2, Req("bSamb"));
            Assert.IsTrue(a.Ok && b.Ok);
            Assert.AreEqual(1, a.Seat);
            Assert.AreEqual(2, b.Seat);
            Assert.AreEqual(s.Seats.Select(x => x.Name).Distinct().Count(), s.Seats.Count);
            Assert.IsFalse(s.Seats.Any(x => x.Name.Contains("<")));
        }

        [Test]
        public void Join_RejectsBadVersion_Garbage_Duplicates_AndFullLobby()
        {
            var s = NewSession(new Clock(), max: 2);
            Assert.IsFalse(s.Join(1, Req(version: 99)).Ok);
            Assert.IsFalse(s.Join(1, new byte[] { 1, 2, 3 }).Ok);
            Assert.IsFalse(s.Join(1, new byte[500]).Ok);
            Assert.IsTrue(s.Join(1, Req()).Ok);
            Assert.IsFalse(s.Join(1, Req()).Ok, "same connection can't join twice");
            Assert.IsFalse(s.Join(2, Req()).Ok, "lobby of 2 is full");
        }

        [Test]
        public void Join_PasswordIsEnforced_AndBruteForceIsLockedOut()
        {
            var clock = new Clock();
            var s = NewSession(clock, password: "hunter2");
            Assert.IsFalse(s.Join(1, Req(password: "nope")).Ok);
            Assert.IsTrue(s.Join(2, Req(password: "hunter2")).Ok);

            for (ulong id = 10; id < 30; id++) s.Join(id, Req(password: "guess" + id), "6.6.6.6");
            JoinResult locked = s.Join(99, Req(password: "hunter2"), "6.6.6.6");
            Assert.IsFalse(locked.Ok, "even the right password is refused during lockout");
            Assert.IsTrue(s.Join(98, Req(password: "hunter2"), "1.2.3.4").Ok, "other addresses aren't locked out");

            clock.Now += 31;
            Assert.IsTrue(s.Join(99, Req(password: "hunter2"), "6.6.6.6").Ok);
        }

        [Test]
        public void Join_RoomWideBackstop_LocksOutWhenManySourcesFail()
        {
            var s = NewSession(new Clock(), password: "hunter2");
            for (int i = 0; i < 64; i++) s.Join((ulong)(10 + i), Req(password: "guess"), "10.0.0." + i);
            Assert.IsFalse(s.Join(99, Req(password: "hunter2"), "1.2.3.4").Ok);
        }

        [Test]
        public void Reconnect_TokenBypassesLockout()
        {
            var clock = new Clock();
            var s = NewSession(clock, password: "hunter2");
            s.Join(1, Req("Ann", password: "hunter2"), "1.2.3.4");
            s.Join(2, Req("Bob", password: "hunter2"), "5.6.7.8");
            s.Start(100, new StartSettings());
            byte[] annToken = s.TokenFor(1);
            s.Disconnected(1);

            // Ann's own address is locked out (a shared network, say) and so is the whole room.
            for (int i = 0; i < 70; i++) s.Join((ulong)(10 + i), new byte[] { 9, 9 }, i < 10 ? "1.2.3.4" : "10.0.0." + i);
            Assert.IsFalse(s.Join(7, Req(password: "hunter2"), "1.2.3.4").Ok);

            JoinResult back = s.Join(7, Req(token: annToken), "1.2.3.4");
            Assert.IsTrue(back.Ok, back.Reason);
            Assert.AreEqual(1, back.Seat);
        }

        [Test]
        public void Join_FailureTrackingIsBounded()
        {
            var s = NewSession(new Clock(), password: "pw");
            for (int i = 0; i < 2000; i++) s.Join((ulong)(10 + i), Req(password: "x"), "src" + i);
            Assert.Pass("no unbounded growth or exception");
        }

        [Test]
        public void OnlineGames_UseTheSecureRandomSource()
        {
            var s = NewSession(new Clock());
            s.Join(1, Req());
            s.Start(100, new StartSettings());
            Assert.IsInstanceOf<SecureRng>(s.Game.Config.Random);
        }

        [Test]
        public void Reconnect_RequiresTheSeatsSecretToken()
        {
            var clock = new Clock();
            var s = NewSession(clock);
            s.Join(1, Req("Ann"));
            s.Join(2, Req("Bob"));
            s.Start(100, new StartSettings());

            byte[] annToken = s.TokenFor(1);
            s.Disconnected(1);

            Assert.IsFalse(s.Join(7, Req(token: new byte[16])).Ok, "guessed token");
            Assert.IsFalse(s.Join(7, Req()).Ok, "new player can't join a running game");
            Assert.IsFalse(s.Join(7, Req(token: s.TokenFor(2))).Ok, "Bob's token while Bob is connected");

            JoinResult back = s.Join(7, Req(token: annToken));
            Assert.IsTrue(back.Ok);
            Assert.AreEqual(1, back.Seat);
            Assert.AreEqual(1, s.SeatOf(7));
        }

        [Test]
        public void Start_IsHostOnlyAndNeedsTwoPlayers()
        {
            var s = NewSession(new Clock());
            Assert.IsNotNull(s.Start(100, new StartSettings()), "alone");
            s.Join(1, Req());
            Assert.IsNotNull(s.Start(1, new StartSettings()), "non-host");
            Assert.IsNotNull(s.Start(100, new StartSettings { Radius = 99 }));
            Assert.IsNotNull(s.Start(100, new StartSettings { Rules = new HouseRules { VictoryPoints = 0 } }));
            Assert.IsNull(s.Start(100, new StartSettings()));
            Assert.AreEqual(SessionState.Playing, s.State);
            Assert.IsNotNull(s.Start(100, new StartSettings()), "already started");
        }

        // ---- Session: commands ---------------------------------------------------------------------

        static GameSession Started(Clock clock, int guests = 2)
        {
            var s = NewSession(clock);
            for (int i = 0; i < guests; i++) s.Join((ulong)(i + 1), Req("G" + i));
            Assert.IsNull(s.Start(100, new StartSettings()));
            return s;
        }

        [Test]
        public void Commands_ActAsTheConnectionsSeat_NeverAsAnotherPlayer()
        {
            var s = Started(new Clock());
            Assert.AreEqual(0, s.Game.CurrentPlayer);

            // Guest (client 1, seat 1) tries to place a setup village during seat 0's turn.
            Vertex v = s.Game.LegalSetupVertices().First();
            SessionResponse r = s.HandleCommand(1, CommandCodec.Encode(new SetupVillage(0, v))); // claims to be seat 0
            Assert.IsFalse(r.Ok);
            Assert.AreEqual(0, s.Game.Buildings.Count, "nothing may change");

            Assert.IsTrue(s.HandleCommand(100, CommandCodec.Encode(new SetupVillage(1, v))).Ok); // host lies about seat, still seat 0
            Assert.AreEqual(0, s.Game.Buildings[v].Owner);
        }

        [Test]
        public void Commands_FromStrangersAndBeforeStart_AreRejected()
        {
            var clock = new Clock();
            var lobby = NewSession(clock);
            lobby.Join(1, Req());
            Assert.IsFalse(lobby.HandleCommand(1, CommandCodec.Encode(new RollDice(0))).Ok, "not started");

            var s = Started(clock);
            Assert.IsFalse(s.HandleCommand(555, CommandCodec.Encode(new RollDice(0))).Ok, "unknown connection");
        }

        [Test]
        public void Commands_FloodingIsRateLimited_ThenDisconnected()
        {
            var clock = new Clock();
            var s = Started(clock);
            byte[] spam = CommandCodec.Encode(new RollDice(0));

            var results = Enumerable.Range(0, 200).Select(_ => s.HandleCommand(1, spam)).ToList();
            Assert.IsTrue(results.Any(r => !r.Ok && r.Error.Contains("too fast")));
            Assert.IsTrue(results.Any(r => r.Disconnect), "sustained flooding gets the client dropped");

            clock.Now += 10;
            Assert.IsFalse(s.HandleCommand(2, spam).Disconnect, "other clients are unaffected");
        }

        [Test]
        public void Commands_MalformedBytesAccumulateViolations()
        {
            var clock = new Clock();
            var s = Started(clock);
            bool disconnected = false;
            for (int i = 0; i < 40 && !disconnected; i++)
            {
                clock.Now += 1; // stay under the rate limit so only the malformed counter trips
                SessionResponse r = s.HandleCommand(1, new byte[] { 255, 1, 2 });
                Assert.IsFalse(r.Ok);
                disconnected = r.Disconnect;
            }
            Assert.IsTrue(disconnected);
            Assert.AreEqual(0, s.Game.Buildings.Count);
        }

        [Test]
        public void Snapshots_AreTailoredPerClient()
        {
            var s = Started(new Clock());
            s.Game.GrantResources(1, new ResourceSet(timber: 5));

            Game forHost = SnapshotCodec.Decode(s.SnapshotFor(100));
            Game forGuest = SnapshotCodec.Decode(s.SnapshotFor(1));
            Assert.AreEqual(ResourceSet.Empty, forHost.Players[1].Hand);
            Assert.AreEqual(5, forGuest.Players[1].Hand.Timber);
            Assert.AreEqual(5, forHost.Players[1].HandCount);
            Assert.IsNull(s.SnapshotFor(555), "strangers get nothing");
        }

        [Test]
        public void Welcome_ContainsTheSeatAndTokenOnlyForThatClient()
        {
            var s = NewSession(new Clock());
            s.Join(1, Req("Ann"));
            Welcome w = Welcome.Decode(s.WelcomeFor(1));
            Assert.AreEqual(1, w.Seat);
            Assert.AreEqual(16, w.Token.Length);
            CollectionAssert.AreEqual(new[] { "Host", "Ann" }, w.Names);
            Assert.IsNull(s.WelcomeFor(555));

            Assert.IsFalse(s.RosterBytes().SequenceEqual(s.WelcomeFor(1)));
            Assert.IsFalse(ByteSeq(s.RosterBytes(), w.Token), "roster must not include tokens");
        }

        static bool ByteSeq(byte[] hay, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= hay.Length; i++)
                if (hay.Skip(i).Take(needle.Length).SequenceEqual(needle)) return true;
            return false;
        }

        // ---- Log lines -----------------------------------------------------------------------------

        [Test]
        public void LogCodec_RoundTrips_AndStripsMarkupOnArrival()
        {
            var lines = new List<string> { "Ann built a road.", "<color=red>hax</color>" };
            List<string> back = LogCodec.Decode(LogCodec.Encode(lines));
            Assert.AreEqual("Ann built a road.", back[0]);
            Assert.IsFalse(back[1].Contains("<"));

            // A hostile host can't bypass the encoder: hand-built bytes are cleaned too.
            var w = new WireWriter();
            w.Byte(1);
            w.String("<b>evil</b>", 100);
            Assert.IsFalse(LogCodec.Decode(w.ToArray())[0].Contains("<"));
            Assert.Throws<WireException>(() => LogCodec.Decode(new byte[] { 200 }));
        }

        [Test]
        public void Commands_ReturnPublicEventsOnly()
        {
            var s = Started(new Clock());
            Vertex v = s.Game.LegalSetupVertices().First();
            SessionResponse r = s.HandleCommand(100, CommandCodec.Encode(new SetupVillage(0, v)));
            Assert.IsTrue(r.Ok);
            Assert.IsNotEmpty(r.Events);
        }

        // ---- End to end ----------------------------------------------------------------------------

        [Test]
        public void EndToEnd_ClientsPlaySetupUsingOnlyTheirOwnSnapshots()
        {
            var clock = new Clock();
            var s = Started(clock, guests: 2);
            ulong[] clients = { 100, 1, 2 };

            int guard = 0;
            while ((s.Game.Phase == Phase.SetupVillage || s.Game.Phase == Phase.SetupRoad) && guard++ < 40)
            {
                int actor = s.Game.CurrentPlayer;
                ulong client = clients[actor];
                clock.Now += 1;

                // The client decides purely from the snapshot it was sent.
                Game view = SnapshotCodec.Decode(s.SnapshotFor(client));
                Assert.AreEqual(actor, view.CurrentPlayer);
                Command cmd = view.Phase == Phase.SetupVillage
                    ? (Command)new SetupVillage(actor, view.LegalSetupVertices().First())
                    : new SetupRoad(actor, view.LegalSetupRoadEdges().First());

                SessionResponse r = s.HandleCommand(client, CommandCodec.Encode(cmd));
                Assert.IsTrue(r.Ok, r.Error);
                Assert.IsTrue(r.BroadcastState);
            }

            Assert.AreEqual(Phase.Roll, s.Game.Phase);
            Assert.AreEqual(6, s.Game.Buildings.Count);
        }
    }
}
