using System.Linq;
using Catan.Core.Net;
using NUnit.Framework;

namespace Catan.Core.Tests
{
    public class ChatTests
    {
        sealed class Clock { public double Now; }

        static GameSession Lobby(Clock clock, int guests = 2)
        {
            var s = new GameSession(100, "Host", 6, "", () => clock.Now);
            for (int i = 0; i < guests; i++)
                Assert.IsTrue(s.Join((ulong)(i + 1), new JoinRequest { Name = "G" + (i + 1) }.Encode()).Ok);
            return s;
        }

        // ---- Codec and cleaning --------------------------------------------------------------------

        [Test]
        public void Codec_RoundTripsSendAndBroadcast()
        {
            Assert.AreEqual("hello there", ChatCodec.DecodeSend(ChatCodec.EncodeSend("hello there")));

            ChatCodec.DecodeBroadcast(ChatCodec.EncodeBroadcast(3, "gg"), out int seat, out string text);
            Assert.AreEqual(3, seat);
            Assert.AreEqual("gg", text);
        }

        [Test]
        public void Codec_RejectsOversizedTruncatedPaddedAndBadSeat()
        {
            Assert.Throws<WireException>(() => ChatCodec.DecodeSend(new byte[ChatCodec.MaxBytes + 1]));
            Assert.Throws<WireException>(() => ChatCodec.DecodeSend(new byte[] { 5, 0, 65 })); // claims 5 bytes, has 1
            Assert.Throws<WireException>(() => ChatCodec.DecodeSend(ChatCodec.EncodeSend("x").Concat(new byte[] { 0 }).ToArray()));
            Assert.Throws<WireException>(() => ChatCodec.DecodeBroadcast(new byte[] { 9, 0, 0 }, out _, out _)); // seat 9
            Assert.Throws<WireException>(() => ChatCodec.DecodeSend(new byte[] { 2, 0, 0xFF, 0xFE })); // invalid UTF-8
        }

        [Test]
        public void Clean_StripsMarkupAndControlsButKeepsPunctuation()
        {
            Assert.AreEqual("hi R&B, ok?", ChatCodec.Clean("hi   R&B,\tok?"));
            string cleaned = ChatCodec.Clean("<color=red>boo</color> \u0007​‮");
            Assert.IsFalse(cleaned.Contains("<"));
            Assert.IsFalse(cleaned.Contains(">"));
            Assert.AreEqual("", ChatCodec.Clean("  \u0001  "));
            Assert.LessOrEqual(ChatCodec.Clean(new string('a', 1000)).Length, ChatCodec.MaxChars);
        }

        [Test]
        public void ChannelNames_AreValidatedStrictly()
        {
            Assert.IsTrue(ChatCodec.IsValidChannelName("catan-0123abcd_EF"));
            Assert.IsFalse(ChatCodec.IsValidChannelName(""));
            Assert.IsFalse(ChatCodec.IsValidChannelName(null));
            Assert.IsFalse(ChatCodec.IsValidChannelName("has space"));
            Assert.IsFalse(ChatCodec.IsValidChannelName("a/b"));
            Assert.IsFalse(ChatCodec.IsValidChannelName("sip:evil@host"));
            Assert.IsFalse(ChatCodec.IsValidChannelName(new string('a', 65)));
        }

        // ---- Host policy ---------------------------------------------------------------------------

        [Test]
        public void Chat_IsAttributedToTheConnectionsSeat()
        {
            var s = Lobby(new Clock());
            ChatResult r = s.HandleChat(2, ChatCodec.EncodeSend("it was me"));
            Assert.IsTrue(r.Ok);
            Assert.AreEqual(2, r.Seat, "seat comes from the connection; the payload cannot name one");
            Assert.AreEqual("it was me", r.Text);
        }

        [Test]
        public void Chat_FromStrangersIsRejected()
        {
            var s = Lobby(new Clock());
            Assert.IsFalse(s.HandleChat(555, ChatCodec.EncodeSend("hi")).Ok);
        }

        [Test]
        public void Chat_TextIsCleanedAndEmptyIsRejected()
        {
            var s = Lobby(new Clock());
            ChatResult r = s.HandleChat(1, ChatCodec.EncodeSend("<b>bold</b>   move"));
            Assert.IsTrue(r.Ok);
            Assert.IsFalse(r.Text.Contains("<"));
            Assert.IsFalse(s.HandleChat(1, ChatCodec.EncodeSend("   \u0001 ")).Ok);
        }

        [Test]
        public void Chat_FloodIsRateLimitedThenDisconnected_OthersUnaffected()
        {
            var clock = new Clock();
            var s = Lobby(clock);
            byte[] msg = ChatCodec.EncodeSend("spam");

            var results = Enumerable.Range(0, 60).Select(_ => s.HandleChat(1, msg)).ToList();
            Assert.AreEqual(4, results.Count(r => r.Ok), "burst only");
            Assert.IsTrue(results.Any(r => r.Disconnect));

            Assert.IsTrue(s.HandleChat(2, msg).Ok);
            clock.Now += 2;
            Assert.IsTrue(s.HandleChat(2, msg).Ok, "tokens refill over time");
        }

        [Test]
        public void Chat_MalformedBytesCountAsViolations()
        {
            var clock = new Clock();
            var s = Lobby(clock);
            bool kicked = false;
            for (int i = 0; i < 30 && !kicked; i++)
            {
                clock.Now += 2;
                ChatResult r = s.HandleChat(1, new byte[] { 200, 200, 1 });
                Assert.IsFalse(r.Ok);
                kicked = r.Disconnect;
            }
            Assert.IsTrue(kicked);
        }

        // ---- Moderation ----------------------------------------------------------------------------

        [Test]
        public void HostMute_DropsMessages_AndIsHostOnly()
        {
            var clock = new Clock();
            var s = Lobby(clock);

            Assert.IsNotNull(s.SetChatMuted(1, 2, true), "guests can't mute");
            Assert.IsNotNull(s.SetChatMuted(100, 0, true), "host can't mute themselves");
            Assert.IsNotNull(s.SetChatMuted(100, 9, true), "no such seat");

            Assert.IsNull(s.SetChatMuted(100, 1, true));
            Assert.IsTrue(s.IsChatMuted(1));
            ChatResult r = s.HandleChat(1, ChatCodec.EncodeSend("hello"));
            Assert.IsFalse(r.Ok);
            Assert.IsFalse(r.Disconnect, "being muted is not misbehaviour");

            Assert.IsTrue(s.HandleChat(2, ChatCodec.EncodeSend("hello")).Ok);

            Assert.IsNull(s.SetChatMuted(100, 1, false));
            clock.Now += 2;
            Assert.IsTrue(s.HandleChat(1, ChatCodec.EncodeSend("back")).Ok);
        }

        [Test]
        public void HostMute_SurvivesReconnect()
        {
            var clock = new Clock();
            var s = Lobby(clock);
            s.Start(100, new StartSettings());
            byte[] token = s.TokenFor(1);
            Assert.IsNull(s.SetChatMuted(100, 1, true));

            s.Disconnected(1);
            Assert.IsTrue(s.Join(9, new JoinRequest { Token = token }.Encode()).Ok);
            Assert.IsFalse(s.HandleChat(9, ChatCodec.EncodeSend("sneaky")).Ok);
        }

        // ---- Voice channel capability --------------------------------------------------------------

        [Test]
        public void VoiceChannel_IsUnguessable_AndOnlyGivenToSeatedPlayers()
        {
            var a = new GameSession(100, "A", 4, "", () => 0);
            var b = new GameSession(100, "B", 4, "", () => 0);
            Assert.IsTrue(ChatCodec.IsValidChannelName(a.VoiceChannel));
            Assert.GreaterOrEqual(a.VoiceChannel.Length, 32 + 6, "128 bits of randomness");
            Assert.AreNotEqual(a.VoiceChannel, b.VoiceChannel);

            a.Join(1, new JoinRequest().Encode());
            Assert.AreEqual(a.VoiceChannel, a.VoiceChannelFor(1));
            Assert.IsNull(a.VoiceChannelFor(555), "strangers never learn the channel");
        }

        [Test]
        public void Welcome_CarriesTheChannel_AndRejectsABadOne()
        {
            var s = Lobby(new Clock(), 1);
            Welcome w = Welcome.Decode(s.WelcomeFor(1));
            Assert.AreEqual(s.VoiceChannel, w.VoiceChannel);

            byte[] bytes = s.WelcomeFor(1);
            int at = System.Text.Encoding.ASCII.GetString(bytes).IndexOf("catan-");
            Assert.Greater(at, 0);
            bytes[at] = (byte)'/'; // corrupt the channel name
            Assert.Throws<WireException>(() => Welcome.Decode(bytes));
        }

        [Test]
        public void Fuzz_ChatDecoders_OnlyThrowWireException()
        {
            var rnd = new System.Random(5);
            for (int i = 0; i < 10000; i++)
            {
                var data = new byte[rnd.Next(0, 30)];
                rnd.NextBytes(data);
                try { ChatCodec.DecodeSend(data); } catch (WireException) { }
                try { ChatCodec.DecodeBroadcast(data, out _, out _); } catch (WireException) { }
            }
        }
    }
}
