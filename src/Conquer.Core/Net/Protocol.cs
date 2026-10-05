using System;
using System.Collections.Generic;
using Conquer.Core.Bots;

namespace Conquer.Core.Net
{
    /// <summary>
    /// The online server's framing on top of the other codecs, shared by the server and the client. Every WebSocket message is one binary frame: a type
    /// byte, then that type's payload. Payloads reuse the existing bounded codecs (JoinRequest, CommandCodec,
    /// ChatCodec, SnapshotCodec, LogCodec), so all the decoding rules already under test still apply.
    /// </summary>
    public static class Protocol
    {
        // Client -> server
        public const byte CreateRoom = 0x01;   // [maxPlayers][bots] Raw(JoinRequest)
        public const byte JoinRoom = 0x02;     // String(code) Raw(JoinRequest)
        public const byte Command = 0x03;      // CommandCodec bytes
        public const byte Chat = 0x04;         // ChatCodec send bytes
        public const byte Start = 0x05;        // [radius] HouseRules
        public const byte Mute = 0x06;         // [seat][bool]
        public const byte SetBots = 0x07;      // [count]
        public const byte Heartbeat = 0x08;    // empty
        public const byte AddBot = 0x09;       // [difficulty] (host only, lobby)
        public const byte RemoveBot = 0x0A;    // [seat] (host only, lobby)

        // Server -> client
        public const byte RoomCreated = 0x80;  // String(code)
        public const byte Welcome = 0x81;      // GameSession.WelcomeFor bytes
        public const byte Snapshot = 0x83;     // SnapshotCodec bytes for this viewer
        public const byte Log = 0x84;          // LogCodec bytes
        public const byte ChatLine = 0x85;     // ChatCodec broadcast bytes
        public const byte BotChat = 0x86;      // String(name) String(text)
        public const byte Error = 0x87;        // String(message)
        public const byte Bots = 0x88;         // [count] then String(name) per commentator bot

        public const int CodeMaxBytes = 16;
        const int TextMaxBytes = ChatCodec.MaxChars * 4;

        public static byte[] Frame(byte type, byte[] payload)
        {
            var frame = new byte[payload.Length + 1];
            frame[0] = type;
            Buffer.BlockCopy(payload, 0, frame, 1, payload.Length);
            return frame;
        }

        public static byte[] ErrorFrame(string message)
        {
            var w = new WireWriter();
            w.String(message ?? "", TextMaxBytes);
            return Frame(Error, w.ToArray());
        }

        public static byte[] RoomCreatedFrame(string code)
        {
            var w = new WireWriter();
            w.String(code, CodeMaxBytes);
            return Frame(RoomCreated, w.ToArray());
        }

        public static byte[] BotChatFrame(string name, string text)
        {
            var w = new WireWriter();
            w.String(NameSanitizer.Clean(name, "Bot", 40), 160);
            w.String(ChatCodec.Clean(text), TextMaxBytes);
            return Frame(BotChat, w.ToArray());
        }

        public static byte[] BotsFrame(IReadOnlyList<string> names)
        {
            var w = new WireWriter();
            w.Byte(names.Count);
            foreach (string n in names) w.String(NameSanitizer.Clean(n, "Bot", 40), 160);
            return Frame(Bots, w.ToArray());
        }

        // ---- Decoding client payloads (all throw WireException on anything malformed) ------------------

        public sealed class CreateRequest
        {
            public int MaxPlayers;
            public int Bots;
            public byte[] Join;
        }

        public static CreateRequest DecodeCreate(byte[] payload)
        {
            var r = new WireReader(payload, JoinRequest.MaxBytes + 8);
            var req = new CreateRequest { MaxPlayers = r.Byte(6), Bots = r.Byte(4), Join = r.Raw(JoinRequest.MaxBytes) };
            if (req.MaxPlayers < 2) throw new WireException("Bad player count.");
            r.End();
            return req;
        }

        public static void DecodeJoin(byte[] payload, out string code, out byte[] join)
        {
            var r = new WireReader(payload, JoinRequest.MaxBytes + CodeMaxBytes + 8);
            code = r.String(CodeMaxBytes).ToUpperInvariant();
            join = r.Raw(JoinRequest.MaxBytes);
            r.End();
        }

        public static StartSettings DecodeStart(byte[] payload)
        {
            var r = new WireReader(payload, MaxStartBytes);
            var s = new StartSettings { Radius = r.Byte(BoardGenerator.MaxRadius), Rules = r.ReadHouseRules() };
            // A host who arranged the board sends it after the rules. Older clients stop at the rules.
            if (r.Remaining > 0)
            {
                s.Board = r.ReadBoard();
                if (s.Board.Radius != s.Radius) throw new WireException("Board size doesn't match.");
            }
            r.End();
            return s;
        }

        public static void DecodeMute(byte[] payload, out int seat, out bool muted)
        {
            var r = new WireReader(payload, 2);
            seat = r.Byte(5);
            muted = r.Bool();
            r.End();
        }

        public static int DecodeSetBots(byte[] payload)
        {
            var r = new WireReader(payload, 1);
            int n = r.Byte(4);
            r.End();
            return n;
        }

        public static BotDifficulty DecodeAddBot(byte[] payload)
        {
            var r = new WireReader(payload, 1);
            var d = (BotDifficulty)r.Byte((int)BotDifficulty.Hard);
            r.End();
            return d;
        }

        public static int DecodeRemoveBot(byte[] payload)
        {
            var r = new WireReader(payload, 1);
            int seat = r.Byte(5);
            r.End();
            return seat;
        }

        // ---- Decoding server frames (client side) ------------------------------------------------------

        public static string DecodeRoomCreated(byte[] payload)
        {
            var r = new WireReader(payload, CodeMaxBytes + 2);
            string code = r.String(CodeMaxBytes);
            r.End();
            return code;
        }

        /// <summary>Error text, re-cleaned on arrival like chat.</summary>
        public static string DecodeError(byte[] payload)
        {
            var r = new WireReader(payload, TextMaxBytes + 2);
            string text = ChatCodec.Clean(r.String(TextMaxBytes));
            r.End();
            return text;
        }

        public static void DecodeBotChat(byte[] payload, out string name, out string text)
        {
            var r = new WireReader(payload, TextMaxBytes + 170);
            name = NameSanitizer.Clean(r.String(160), "Bot", 40);
            text = ChatCodec.Clean(r.String(TextMaxBytes));
            r.End();
        }

        public static List<string> DecodeBots(byte[] payload)
        {
            var r = new WireReader(payload, 1024);
            int n = r.Byte(4);
            var names = new List<string>(n);
            for (int i = 0; i < n; i++) names.Add(NameSanitizer.Clean(r.String(160), "Bot", 40));
            r.End();
            return names;
        }

        // ---- Client-side encoders (used by tests and as the reference for the Avalonia client) ---------

        public static byte[] EncodeCreate(int maxPlayers, int bots, JoinRequest join)
        {
            var w = new WireWriter();
            w.Byte(maxPlayers);
            w.Byte(bots);
            w.Raw(join.Encode(), JoinRequest.MaxBytes);
            return Frame(CreateRoom, w.ToArray());
        }

        public static byte[] EncodeJoin(string code, JoinRequest join)
        {
            var w = new WireWriter();
            w.String(code, CodeMaxBytes);
            w.Raw(join.Encode(), JoinRequest.MaxBytes);
            return Frame(JoinRoom, w.ToArray());
        }

        public static byte[] EncodeAddBot(BotDifficulty difficulty) => Frame(AddBot, new[] { (byte)difficulty });

        public static byte[] EncodeRemoveBot(int seat) => Frame(RemoveBot, new[] { (byte)seat });

        /// <summary>Rules, plus room for an arranged board of the largest radius.</summary>
        public const int MaxStartBytes = 32 + 8 + 4 * 331 + 64 * 6;

        public static byte[] EncodeStart(int radius, HouseRules rules, Board board = null)
        {
            var w = new WireWriter();
            w.Byte(board?.Radius ?? radius);
            w.Write(rules);
            if (board != null) w.Write(board);
            return Frame(Start, w.ToArray());
        }
    }
}
