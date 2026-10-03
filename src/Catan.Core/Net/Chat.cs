using System;

namespace Catan.Core.Net
{
    public sealed class ChatResult
    {
        public bool Ok { get; }
        public string Error { get; }
        public int Seat { get; }
        public string Text { get; }

        /// <summary>The sender has misbehaved enough to be dropped.</summary>
        public bool Disconnect { get; }

        ChatResult(bool ok, string error, int seat, string text, bool disconnect)
        {
            Ok = ok;
            Error = error;
            Seat = seat;
            Text = text;
            Disconnect = disconnect;
        }

        internal static ChatResult Accepted(int seat, string text) => new ChatResult(true, null, seat, text, false);
        internal static ChatResult Rejected(string error, bool disconnect = false) => new ChatResult(false, error, -1, null, disconnect);
    }

    /// <summary>
    /// Text chat wire format and policy. Chat is relayed by the authoritative host, which stamps each message
    /// with the sender's real seat; clients never choose who a message appears to be from.
    /// </summary>
    public static class ChatCodec
    {
        public const int MaxChars = 200;
        const int MaxTextBytes = MaxChars * 4;
        public const int MaxBytes = MaxTextBytes + 8;

        public static byte[] EncodeSend(string text)
        {
            var w = new WireWriter();
            w.String(text ?? "", MaxTextBytes);
            return w.ToArray();
        }

        public static string DecodeSend(byte[] data)
        {
            var r = new WireReader(data, MaxBytes);
            string text = r.String(MaxTextBytes);
            r.End();
            return text;
        }

        public static byte[] EncodeBroadcast(int seat, string text)
        {
            var w = new WireWriter();
            w.Byte(seat);
            w.String(text, MaxTextBytes);
            return w.ToArray();
        }

        /// <summary>Re-cleans on arrival, so a hostile host can't smuggle markup into the rich-text UI either.</summary>
        public static void DecodeBroadcast(byte[] data, out int seat, out string text)
        {
            var r = new WireReader(data, MaxBytes);
            seat = r.Byte(5);
            text = Clean(r.String(MaxTextBytes));
            r.End();
        }

        /// <summary>Printable text only: no markup brackets, control or format characters, collapsed whitespace.</summary>
        public static string Clean(string raw) => NameSanitizer.Clean(raw, "", MaxChars, stripAmpersand: false);
    }
}
