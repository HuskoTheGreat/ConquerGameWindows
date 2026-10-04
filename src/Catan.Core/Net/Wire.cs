using System;
using System.Collections.Generic;
using System.Text;

namespace Catan.Core.Net
{
    /// <summary>Thrown for any malformed, oversized or out-of-range network data. Never trust the sender.</summary>
    public sealed class WireException : Exception
    {
        public WireException(string message) : base(message) { }
    }

    /// <summary>Little-endian binary writer. The protocol is hand-written: no reflection, no BinaryFormatter.</summary>
    public sealed class WireWriter
    {
        readonly List<byte> _bytes = new List<byte>(256);

        public int Length => _bytes.Count;

        public void Byte(int value) => _bytes.Add(unchecked((byte)value));
        public void Bool(bool value) => _bytes.Add(value ? (byte)1 : (byte)0);

        public void Short(int value)
        {
            _bytes.Add(unchecked((byte)value));
            _bytes.Add(unchecked((byte)(value >> 8)));
        }

        public void Int(int value)
        {
            for (int i = 0; i < 4; i++) _bytes.Add(unchecked((byte)(value >> (8 * i))));
        }

        public void String(string value, int maxBytes)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(value ?? "");
            if (utf8.Length > maxBytes) throw new ArgumentException("String too long for the wire.");
            Short(utf8.Length);
            _bytes.AddRange(utf8);
        }

        public void Raw(byte[] data, int maxBytes)
        {
            if (data.Length > maxBytes) throw new ArgumentException("Blob too long for the wire.");
            Short(data.Length);
            _bytes.AddRange(data);
        }

        public byte[] ToArray() => _bytes.ToArray();
    }

    /// <summary>Bounds-checked reader: every read can only succeed or throw <see cref="WireException"/>.</summary>
    public sealed class WireReader
    {
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        readonly byte[] _data;
        int _pos;

        public WireReader(byte[] data, int maxLength)
        {
            if (data == null) throw new WireException("No data.");
            if (data.Length > maxLength) throw new WireException("Message too large.");
            _data = data;
        }

        public int Remaining => _data.Length - _pos;

        void Need(int n)
        {
            if (n < 0 || Remaining < n) throw new WireException("Message truncated.");
        }

        public int Byte()
        {
            Need(1);
            return _data[_pos++];
        }

        /// <summary>A byte restricted to 0..<paramref name="max"/> (use for enums).</summary>
        public int Byte(int max)
        {
            int v = Byte();
            if (v > max) throw new WireException("Value out of range.");
            return v;
        }

        public bool Bool()
        {
            int v = Byte();
            if (v > 1) throw new WireException("Bad boolean.");
            return v == 1;
        }

        public int Short()
        {
            Need(2);
            int v = (short)(_data[_pos] | (_data[_pos + 1] << 8));
            _pos += 2;
            return v;
        }

        public int Short(int min, int max)
        {
            int v = Short();
            if (v < min || v > max) throw new WireException("Value out of range.");
            return v;
        }

        public int Int()
        {
            Need(4);
            int v = _data[_pos] | (_data[_pos + 1] << 8) | (_data[_pos + 2] << 16) | (_data[_pos + 3] << 24);
            _pos += 4;
            return v;
        }

        public int Int(int min, int max)
        {
            int v = Int();
            if (v < min || v > max) throw new WireException("Value out of range.");
            return v;
        }

        public string String(int maxBytes)
        {
            int len = Short(0, maxBytes);
            Need(len);
            try
            {
                string s = StrictUtf8.GetString(_data, _pos, len);
                _pos += len;
                return s;
            }
            catch (ArgumentException)
            {
                throw new WireException("Invalid text encoding.");
            }
        }

        public byte[] Raw(int maxBytes)
        {
            int len = Short(0, maxBytes);
            Need(len);
            var copy = new byte[len];
            Buffer.BlockCopy(_data, _pos, copy, 0, len);
            _pos += len;
            return copy;
        }

        /// <summary>Requires that the whole message was consumed; trailing bytes are rejected.</summary>
        public void End()
        {
            if (Remaining != 0) throw new WireException("Unexpected trailing data.");
        }
    }

    /// <summary>Shared encoders for the board coordinate types, with strict range checks on read.</summary>
    public static class WireTypes
    {
        /// <summary>No coordinate on a legal board (radius &lt;= 10, plus sea ring) exceeds this.</summary>
        public const int MaxCoord = 16;

        public static void Write(this WireWriter w, Hex h)
        {
            w.Byte(h.Q + 128);
            w.Byte(h.R + 128);
        }

        public static Hex ReadHex(this WireReader r)
        {
            int q = r.Byte() - 128;
            int rr = r.Byte() - 128;
            if (q < -MaxCoord || q > MaxCoord || rr < -MaxCoord || rr > MaxCoord) throw new WireException("Hex out of range.");
            return new Hex(q, rr);
        }

        public static void Write(this WireWriter w, Vertex v)
        {
            w.Write(v.A);
            w.Write(v.B);
            w.Write(v.C);
        }

        public static Vertex ReadVertex(this WireReader r)
        {
            Hex a = r.ReadHex(), b = r.ReadHex(), c = r.ReadHex();
            // A corner is three mutually adjacent hexes. Anything else can't exist; reject it here.
            if (a.DistanceTo(b) != 1 || a.DistanceTo(c) != 1 || b.DistanceTo(c) != 1) throw new WireException("Invalid corner.");
            return new Vertex(a, b, c);
        }

        public static void Write(this WireWriter w, Edge e)
        {
            w.Write(e.A);
            w.Write(e.B);
        }

        public static Edge ReadEdge(this WireReader r)
        {
            Hex a = r.ReadHex(), b = r.ReadHex();
            if (a.DistanceTo(b) != 1) throw new WireException("Invalid edge.");
            return new Edge(a, b);
        }

        public static void Write(this WireWriter w, ResourceSet s)
        {
            foreach (Resource res in ResourceSet.Types) w.Byte(s[res]);
        }

        /// <summary>Five counts, each 0..255 (a byte). Negative or oversized values can't be expressed.</summary>
        public static ResourceSet ReadResourceSet(this WireReader r) =>
            new ResourceSet(r.Byte(), r.Byte(), r.Byte(), r.Byte(), r.Byte());

        public static Resource ReadResource(this WireReader r) => (Resource)r.Byte((int)Resource.Ore);

        public static void Write(this WireWriter w, Resource res) => w.Byte((int)res);

        public static void Write(this WireWriter w, HouseRules h)
        {
            w.Byte(h.VictoryPoints);
            w.Byte(h.DiscardThreshold);
            w.Byte(h.BankRatio);
            w.Byte(h.GenericPortRatio);
            w.Byte(h.ResourcePortRatio);
            w.Byte(h.LongestRoadMinimum);
            w.Byte(h.LargestArmyMinimum);
            w.Byte(h.NoSevenRounds);
            w.Bool(h.FriendlyRobber);
            w.Bool(h.OneDevCardPerTurn);
            w.Bool(h.PlayDevCardOnPurchaseTurn);
            w.Bool(h.TradeAnytime);
            w.Byte(h.StartingResources);
            w.Byte(EffectCardInfo.Count);
            for (int i = 0; i < EffectCardInfo.Count; i++) w.Byte(h.EffectCardCount((EffectCard)i));
        }

        /// <summary>Reads rules and runs the same validation the engine uses.</summary>
        public static HouseRules ReadHouseRules(this WireReader r)
        {
            var h = new HouseRules
            {
                VictoryPoints = r.Byte(),
                DiscardThreshold = r.Byte(),
                BankRatio = r.Byte(),
                GenericPortRatio = r.Byte(),
                ResourcePortRatio = r.Byte(),
                LongestRoadMinimum = r.Byte(),
                LargestArmyMinimum = r.Byte(),
                NoSevenRounds = r.Byte(),
                FriendlyRobber = r.Bool(),
                OneDevCardPerTurn = r.Bool(),
                PlayDevCardOnPurchaseTurn = r.Bool(),
                TradeAnytime = r.Bool(),
                StartingResources = r.Byte(),
            };
            if (r.Byte() != EffectCardInfo.Count) throw new WireException("Bad effect card list.");
            for (int i = 0; i < EffectCardInfo.Count; i++) h.EffectCards[i] = r.Byte(EffectCardInfo.MaxEach);
            string error = h.Validate();
            if (error != null) throw new WireException(error);
            return h;
        }
    }
}
