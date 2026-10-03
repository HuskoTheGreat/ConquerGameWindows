using System.Collections.Generic;
using System.Linq;

namespace Catan.Core.Net
{
    internal sealed class MirrorPlayer
    {
        public string Name;
        public ResourceSet Hand;      // real only for the viewer
        public int HandCount;
        public int DevCount;
        public int[] Dev = new int[5];     // real only for the viewer
        public int[] DevNew = new int[5];
        public int Knights;
        public int LongestRoad;
        public List<Vertex> Settlements = new List<Vertex>();
        public List<Vertex> Cities = new List<Vertex>();
        public List<Edge> Roads = new List<Edge>();
    }

    internal sealed class MirrorData
    {
        public int ViewerSeat;
        public Board Board;
        public HouseRules Rules;
        public List<MirrorPlayer> Players = new List<MirrorPlayer>();
        public ResourceSet Bank;
        public Phase Phase;
        public int Current, Turn, LastRoll;
        public Hex Robber;
        public int Winner, LongestHolder, ArmyHolder, DeckCount;
        public bool DevPlayed;
        public int FreeRoads;
        public TradeOffer Trade;
        public Dictionary<int, int> Discards = new Dictionary<int, int>();
        public List<int> Steal = new List<int>();
    }

    /// <summary>
    /// Per-player view of the game for the wire. This is the privacy boundary: a snapshot contains what
    /// <i>that player</i> is entitled to know. It never includes other players' hands or development cards,
    /// the deck order, or any RNG seed (which would let a client predict dice rolls and steals).
    /// </summary>
    public static class SnapshotCodec
    {
        public const byte Version = 1;
        public const int MaxBytes = 32 * 1024;
        const int MaxName = 80;

        public static byte[] Encode(Game g, int viewerSeat)
        {
            var w = new WireWriter();
            w.Byte(Version);
            w.Byte(viewerSeat);

            // Board (public)
            w.Byte(g.Board.Radius);
            w.Short(g.Board.Tiles.Count);
            foreach (Tile t in g.Board.Tiles)
            {
                w.Write(t.Hex);
                w.Byte((int)t.Resource);
                w.Byte(t.Number);
            }
            w.Byte(g.Board.Ports.Count);
            foreach (Port p in g.Board.Ports)
            {
                w.Write(p.Edge);
                w.Bool(p.IsGeneric);
                w.Byte((int)p.Resource);
            }

            w.Write(g.Rules);

            w.Byte(g.Players.Count);
            foreach (Player p in g.Players)
            {
                bool mine = p.Id == viewerSeat;
                w.String(p.Name, MaxName);
                w.Write(mine ? p.Hand : ResourceSet.Empty);
                w.Short(p.HandCount);
                w.Short(p.DevCardCount);
                for (int i = 0; i < 5; i++) w.Byte(mine ? p.Dev[i] : 0);
                for (int i = 0; i < 5; i++) w.Byte(mine ? p.DevNew[i] : 0);
                w.Byte(p.KnightsPlayed);
                w.Byte(p.LongestRoad);

                w.Byte(p.Settlements.Count);
                foreach (Vertex v in p.Settlements) w.Write(v);
                w.Byte(p.Cities.Count);
                foreach (Vertex v in p.Cities) w.Write(v);
                w.Byte(p.Roads.Count);
                foreach (Edge e in p.Roads) w.Write(e);
            }

            WriteBig(w, g.Bank);
            w.Byte((int)g.Phase);
            w.Byte(g.CurrentPlayer);
            w.Short(g.Turn);
            w.Byte(g.LastRoll);
            w.Write(g.RobberHex);
            w.Byte(g.Winner + 1);
            w.Byte(g.LongestRoadHolder + 1);
            w.Byte(g.LargestArmyHolder + 1);
            w.Short(g.DevDeckCount);
            w.Bool(g.DevCardPlayedThisTurn);
            w.Byte(g.FreeRoadsLeft);

            w.Bool(g.PendingTrade != null);
            if (g.PendingTrade != null)
            {
                w.Byte(g.PendingTrade.From);
                w.Write(g.PendingTrade.Give);
                w.Write(g.PendingTrade.Want);
            }

            w.Byte(g.PendingDiscards.Count);
            foreach (KeyValuePair<int, int> kv in g.PendingDiscards)
            {
                w.Byte(kv.Key);
                w.Short(kv.Value);
            }

            w.Byte(g.StealCandidates.Count);
            foreach (int id in g.StealCandidates) w.Byte(id);

            byte[] bytes = w.ToArray();
            if (bytes.Length > MaxBytes) throw new System.InvalidOperationException("Snapshot too large.");
            return bytes;
        }

        /// <summary>Rebuilds a read-only <see cref="Game"/> from a host snapshot, validating every field.</summary>
        public static Game Decode(byte[] data)
        {
            var r = new WireReader(data, MaxBytes);
            if (r.Byte() != Version) throw new WireException("Unsupported protocol version.");

            var m = new MirrorData { ViewerSeat = r.Byte(5) };

            int radius = r.Byte(BoardGenerator.MaxRadius);
            int tileCount = r.Short(0, Hex.CountForRadius(BoardGenerator.MaxRadius));
            var tiles = new List<Tile>(tileCount);
            for (int i = 0; i < tileCount; i++)
                tiles.Add(new Tile(r.ReadHex(), r.ReadResource(), r.Byte(12)));

            int portCount = r.Byte(64);
            var ports = new List<Port>(portCount);
            for (int i = 0; i < portCount; i++)
                ports.Add(new Port(r.ReadEdge(), r.Bool(), r.ReadResource()));

            try
            {
                m.Board = Board.FromData(radius, tiles, ports);
            }
            catch (System.ArgumentException e)
            {
                throw new WireException(e.Message);
            }

            m.Rules = r.ReadHouseRules();

            int playerCount = r.Byte(6);
            if (playerCount < 2) throw new WireException("Too few players.");
            if (m.ViewerSeat >= playerCount) throw new WireException("Bad seat.");

            var occupied = new HashSet<Vertex>();
            var roadSeen = new HashSet<Edge>();
            for (int i = 0; i < playerCount; i++)
            {
                var p = new MirrorPlayer
                {
                    Name = NameSanitizer.Clean(r.String(MaxName), "Player " + (i + 1)),
                    Hand = r.ReadResourceSet(),
                    HandCount = r.Short(0, 4000),
                    DevCount = r.Short(0, 4000),
                };
                for (int k = 0; k < 5; k++) p.Dev[k] = r.Byte();
                for (int k = 0; k < 5; k++) p.DevNew[k] = r.Byte();
                p.Knights = r.Byte(Player.MaxSettlements * 100);
                p.LongestRoad = r.Byte(Player.MaxRoads);

                int s = r.Byte(Player.MaxSettlements);
                for (int k = 0; k < s; k++) p.Settlements.Add(ReadBuilding(r, m.Board, occupied));
                int c = r.Byte(Player.MaxCities);
                for (int k = 0; k < c; k++) p.Cities.Add(ReadBuilding(r, m.Board, occupied));
                int rd = r.Byte(Player.MaxRoads);
                for (int k = 0; k < rd; k++)
                {
                    Edge e = r.ReadEdge();
                    if (!m.Board.HasEdge(e) || !roadSeen.Add(e)) throw new WireException("Bad road.");
                    p.Roads.Add(e);
                }
                m.Players.Add(p);
            }

            m.Bank = ReadBig(r);
            m.Phase = (Phase)r.Byte((int)Phase.GameOver);
            m.Current = r.Byte(playerCount - 1);
            m.Turn = r.Short(0, 30000);
            m.LastRoll = r.Byte(12);
            m.Robber = r.ReadHex();
            if (!m.Board.IsLand(m.Robber)) throw new WireException("Robber off the board.");
            m.Winner = r.Byte(playerCount) - 1;
            m.LongestHolder = r.Byte(playerCount) - 1;
            m.ArmyHolder = r.Byte(playerCount) - 1;
            m.DeckCount = r.Short(0, 4000);
            m.DevPlayed = r.Bool();
            m.FreeRoads = r.Byte(2);

            if (r.Bool()) m.Trade = new TradeOffer(r.Byte(playerCount - 1), r.ReadResourceSet(), r.ReadResourceSet());

            int owing = r.Byte(playerCount);
            for (int i = 0; i < owing; i++) m.Discards[r.Byte(playerCount - 1)] = r.Short(0, 4000);

            int steals = r.Byte(playerCount);
            for (int i = 0; i < steals; i++) m.Steal.Add(r.Byte(playerCount - 1));

            r.End();
            return Game.FromMirror(m);
        }

        static Vertex ReadBuilding(WireReader r, Board board, HashSet<Vertex> occupied)
        {
            Vertex v = r.ReadVertex();
            if (!board.HasVertex(v) || !occupied.Add(v)) throw new WireException("Bad building.");
            return v;
        }

        // Bank counts can exceed a byte on very large boards, so they use 16 bits.
        static void WriteBig(WireWriter w, ResourceSet s)
        {
            foreach (Resource res in ResourceSet.Types) w.Short(s[res]);
        }

        static ResourceSet ReadBig(WireReader r) =>
            new ResourceSet(r.Short(0, 4000), r.Short(0, 4000), r.Short(0, 4000), r.Short(0, 4000), r.Short(0, 4000));
    }
}
