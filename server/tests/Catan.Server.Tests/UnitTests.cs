using System;
using System.Linq;
using System.Net;
using System.Text;
using Catan.Core;
using Catan.Core.Net;
using Catan.Server.Bots;
using NUnit.Framework;

namespace Catan.Server.Tests
{
    public class UnitTests
    {
        sealed class Clock { public double Now; }

        static BotOptions Bots(int cooldown = 45) => new BotOptions
        {
            Enabled = true,
            CommentaryCooldownSeconds = cooldown,
            ReplyCooldownSeconds = 8,
        };

        [Test]
        public void RoomCodes_UseTheUnambiguousAlphabet_AndDontRepeat()
        {
            var codes = Enumerable.Range(0, 500).Select(_ => RoomRegistry.NewCode()).ToList();
            Assert.IsTrue(codes.All(c => c.Length == RoomRegistry.CodeLength && c.All(RoomRegistry.CodeAlphabet.Contains)));
            Assert.AreEqual(codes.Count, codes.Distinct().Count());
            Assert.IsFalse(RoomRegistry.CodeAlphabet.Any("01ILO".Contains));
        }

        [Test]
        public void KeyedRateLimiter_LimitsPerKey_AndRefills()
        {
            var clock = new Clock();
            var limiter = new KeyedRateLimiter(1.0 / 6, 3, () => clock.Now);
            Assert.IsTrue(limiter.Allow("a") && limiter.Allow("a") && limiter.Allow("a"));
            Assert.IsFalse(limiter.Allow("a"));
            Assert.IsTrue(limiter.Allow("b"), "other addresses unaffected");
            clock.Now += 6;
            Assert.IsTrue(limiter.Allow("a"));
        }

        [Test]
        public void KeyedRateLimiter_StaysBounded()
        {
            var limiter = new KeyedRateLimiter(1, 1, () => 0, maxKeys: 100);
            for (int i = 0; i < 10_000; i++) limiter.Allow("ip" + i);
            Assert.Pass();
        }

        [Test]
        public void CountLimiter_CapsPerKeyAndTotal()
        {
            var c = new CountLimiter(perKey: 2, total: 3);
            Assert.IsTrue(c.TryAcquire("a") && c.TryAcquire("a"));
            Assert.IsFalse(c.TryAcquire("a"));
            Assert.IsTrue(c.TryAcquire("b"));
            Assert.IsFalse(c.TryAcquire("c"), "total cap");
            c.Release("a");
            Assert.IsTrue(c.TryAcquire("c"));
            Assert.AreEqual(3, c.Total);
        }

        [Test]
        public void ClientAddress_GroupsIpv6ByPrefix()
        {
            Assert.AreEqual("1.2.3.4", ClientAddress.Key(IPAddress.Parse("::ffff:1.2.3.4")));
            Assert.AreEqual(
                ClientAddress.Key(IPAddress.Parse("2001:db8:1:2::1")),
                ClientAddress.Key(IPAddress.Parse("2001:db8:1:2:ffff::9")));
            Assert.AreNotEqual(
                ClientAddress.Key(IPAddress.Parse("2001:db8:1:2::1")),
                ClientAddress.Key(IPAddress.Parse("2001:db8:1:3::1")));
        }

        [Test]
        public void Protocol_RejectsMalformedPayloads()
        {
            Assert.Throws<WireException>(() => Protocol.DecodeCreate(new byte[] { 1, 0 }), "one player");
            Assert.Throws<WireException>(() => Protocol.DecodeCreate(new byte[] { 9, 0, 0, 0 }), "nine players");
            Assert.Throws<WireException>(() => Protocol.DecodeStart(new byte[] { 2 }), "truncated rules");
            Assert.Throws<WireException>(() => Protocol.DecodeMute(new byte[] { 1, 2 }, out _, out _), "bad bool");
            Assert.Throws<WireException>(() => Protocol.DecodeSetBots(new byte[] { 9 }));
            Assert.Throws<WireException>(() => Protocol.DecodeJoin(new byte[300], out _, out _), "oversized");

            byte[] start = Protocol.EncodeStart(2, new HouseRules());
            StartSettings s = Protocol.DecodeStart(start.Skip(1).ToArray());
            Assert.AreEqual(2, s.Radius);
        }

        [Test]
        public void Backend_ParsesOpenAiResponses_AndStripsThinking()
        {
            string json = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"<think>hmm</think> \\\"Ahoy there!\\\"\"}}]}";
            Assert.AreEqual("Ahoy there!", OpenAiCompatibleBackend.ParseContent(Encoding.UTF8.GetBytes(json)));
            Assert.IsNull(OpenAiCompatibleBackend.ParseContent(Encoding.UTF8.GetBytes("{\"error\":\"x\"}")));
            Assert.IsNull(OpenAiCompatibleBackend.ParseContent(Encoding.UTF8.GetBytes("not json")));
        }

        [Test]
        public void Commentator_OnlyCommentsOnHighlights_WithACooldown()
        {
            var clock = new Clock();
            var c = new BotCommentator(Bots(), () => clock.Now);
            c.SetCount(1);

            Assert.IsNull(c.ObserveEvents(new[] { "Ann built a road." }), "routine events are quiet");
            BotPrompt p = c.ObserveEvents(new[] { "Ann rolled 7." });
            Assert.IsNotNull(p);
            Assert.IsNull(c.ObserveEvents(new[] { "Bob stole a card from Ann." }), "one request in flight per room");

            c.Completed(p.Persona, "Uh oh, the robber!");
            Assert.IsNull(c.ObserveEvents(new[] { "Bob stole a card from Ann." }), "cooldown");
            clock.Now += 46;
            Assert.IsNotNull(c.ObserveEvents(new[] { "Bob upgraded to a city." }));
        }

        [Test]
        public void Commentator_AlwaysCongratulatesTheWinner()
        {
            var clock = new Clock();
            var c = new BotCommentator(Bots(), () => clock.Now);
            c.SetCount(1);
            c.Completed(c.ObserveEvents(new[] { "Ann rolled 7." }).Persona, "ok");
            BotPrompt p = c.ObserveEvents(new[] { "Ann wins!" });
            Assert.IsNotNull(p, "win skips the cooldown");
            StringAssert.Contains("Ann wins!", p.Messages[1].Content);
        }

        [Test]
        public void Commentator_RepliesWhenAddressed_ByFirstName()
        {
            var clock = new Clock();
            var options = Bots();
            options.MaxBotsPerRoom = 3;
            var c = new BotCommentator(options, () => clock.Now);
            c.SetCount(3);

            Assert.IsNull(c.ObserveChat("Ann", "nice roll"));
            BotPrompt p = c.ObserveChat("Ann", "hey professor, what are the odds?");
            Assert.IsNotNull(p);
            Assert.AreEqual("Professor Hex", p.Persona.Name);
            StringAssert.Contains("Ann: hey professor", p.Messages[1].Content);
        }

        [Test]
        public void Commentator_PromptOnlyHasPublicInfo_AndStripsItsOwnName()
        {
            var clock = new Clock();
            var c = new BotCommentator(Bots(), () => clock.Now);
            c.SetCount(1);
            BotPrompt p = c.ObserveEvents(new[] { "Ann rolled 7." });
            StringAssert.Contains("cannot see anyone's cards", p.Messages[0].Content);
            Assert.AreEqual("Arr!", c.Completed(p.Persona, p.Persona.Name + ": Arr!"));
        }

        [Test]
        public void Commentator_DisabledMeansNoBots()
        {
            var c = new BotCommentator(new BotOptions { Enabled = false }, () => 0);
            c.SetCount(2);
            Assert.AreEqual(0, c.Active.Count);
            Assert.IsNull(c.ObserveEvents(new[] { "Ann wins!" }));
        }
    }
}
