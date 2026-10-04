using System.Collections.Generic;

namespace Conquer.Core
{
    /// <summary>
    /// Immutable board topology: land tiles, the corners and edges that touch land, and ports.
    /// Pure data; no engine types. Build one with <see cref="BoardGenerator.Generate"/>.
    /// </summary>
    public sealed class Board
    {
        readonly Dictionary<Hex, Tile> _tiles;
        readonly HashSet<Vertex> _vertices = new HashSet<Vertex>();
        readonly HashSet<Edge> _edges = new HashSet<Edge>();
        readonly List<Port> _ports;
        readonly Dictionary<Vertex, Port> _portByVertex = new Dictionary<Vertex, Port>();

        public int Radius { get; }
        public int Seed { get; }

        /// <summary>The raider starts on the first wasteland (or the center if there is none).</summary>
        public Hex RaiderStart { get; }

        public IReadOnlyCollection<Tile> Tiles => _tiles.Values;
        public IReadOnlyCollection<Vertex> Vertices => _vertices;
        public IReadOnlyCollection<Edge> Edges => _edges;
        public IReadOnlyList<Port> Ports => _ports;

        internal Board(int radius, int seed, Dictionary<Hex, Tile> tiles, List<Port> ports)
        {
            Radius = radius;
            Seed = seed;
            _tiles = tiles;
            _ports = ports;

            RaiderStart = Hex.Zero;
            foreach (Tile t in tiles.Values)
            {
                if (t.IsWasteland)
                {
                    RaiderStart = t.Hex;
                    break;
                }
            }

            foreach (Hex h in tiles.Keys)
            {
                for (int i = 0; i < 6; i++)
                {
                    _vertices.Add(Vertex.OfCorner(h, i));
                    _edges.Add(Edge.OfSide(h, i));
                }
            }

            foreach (Port p in ports)
            {
                foreach (Vertex v in p.Edge.Endpoints()) _portByVertex[v] = p;
            }
        }

        /// <summary>
        /// Rebuilds a board from data received over the network. Everything is validated, because the
        /// shape of a board decides which other messages are legal.
        /// </summary>
        public static Board FromData(int radius, IEnumerable<Tile> tiles, IEnumerable<Port> ports)
        {
            if (radius < BoardGenerator.MinRadius || radius > BoardGenerator.MaxRadius)
                throw new System.ArgumentOutOfRangeException(nameof(radius));

            var map = new Dictionary<Hex, Tile>();
            foreach (Tile t in tiles)
            {
                if (Hex.Zero.DistanceTo(t.Hex) > radius) throw new System.ArgumentException("Tile outside the board.");
                if (t.IsWasteland != (t.Number == 0)) throw new System.ArgumentException("Bad token on tile.");
                if (t.Number == 7 || t.Number < 0 || t.Number > 12 || t.Number == 1) throw new System.ArgumentException("Bad number token.");
                if (!map.TryAdd(t.Hex, t)) throw new System.ArgumentException("Duplicate tile.");
            }
            if (map.Count != Hex.CountForRadius(radius)) throw new System.ArgumentException("Wrong number of tiles.");

            var portList = new List<Port>();
            foreach (Port p in ports)
            {
                bool aLand = map.ContainsKey(p.Edge.A), bLand = map.ContainsKey(p.Edge.B);
                if (aLand == bLand) throw new System.ArgumentException("Port must sit on the coast.");
                portList.Add(p);
            }
            return new Board(radius, 0, map, portList);
        }

        public bool IsLand(Hex h) => _tiles.ContainsKey(h);
        public bool TryGetTile(Hex h, out Tile tile) => _tiles.TryGetValue(h, out tile);
        public bool HasVertex(Vertex v) => _vertices.Contains(v);
        public bool HasEdge(Edge e) => _edges.Contains(e);

        /// <summary>The land tiles touching a corner (1-3): these are what a village there collects from.</summary>
        public IEnumerable<Tile> TilesAround(Vertex v)
        {
            foreach (Hex h in v.Hexes())
            {
                if (_tiles.TryGetValue(h, out Tile t)) yield return t;
            }
        }

        /// <summary>Corners one road away that exist on this board (the village distance rule uses these).</summary>
        public IEnumerable<Vertex> AdjacentVertices(Vertex v)
        {
            foreach (Vertex n in v.Neighbors())
            {
                if (_vertices.Contains(n)) yield return n;
            }
        }

        /// <summary>Edges touching a corner that exist on this board.</summary>
        public IEnumerable<Edge> EdgesOf(Vertex v)
        {
            foreach (Edge e in v.Edges())
            {
                if (_edges.Contains(e)) yield return e;
            }
        }

        /// <summary>Edges sharing a corner with <paramref name="e"/> (used for road connectivity).</summary>
        public IEnumerable<Edge> AdjacentEdges(Edge e)
        {
            foreach (Vertex v in e.Endpoints())
            {
                foreach (Edge other in EdgesOf(v))
                {
                    if (other != e) yield return other;
                }
            }
        }

        /// <summary>The port reachable from a corner, or null.</summary>
        public Port PortAt(Vertex v) => _portByVertex.TryGetValue(v, out Port p) ? p : null;
    }
}
