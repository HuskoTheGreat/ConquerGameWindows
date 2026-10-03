using System.Collections.Generic;

namespace Catan.Core
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

        /// <summary>The robber starts on the first desert (or the center if there is none).</summary>
        public Hex RobberStart { get; }

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

            RobberStart = Hex.Zero;
            foreach (Tile t in tiles.Values)
            {
                if (t.IsDesert)
                {
                    RobberStart = t.Hex;
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

        public bool IsLand(Hex h) => _tiles.ContainsKey(h);
        public bool TryGetTile(Hex h, out Tile tile) => _tiles.TryGetValue(h, out tile);
        public bool HasVertex(Vertex v) => _vertices.Contains(v);
        public bool HasEdge(Edge e) => _edges.Contains(e);

        /// <summary>The land tiles touching a corner (1-3): these are what a settlement there collects from.</summary>
        public IEnumerable<Tile> TilesAround(Vertex v)
        {
            foreach (Hex h in v.Hexes())
            {
                if (_tiles.TryGetValue(h, out Tile t)) yield return t;
            }
        }

        /// <summary>Corners one road away that exist on this board (the settlement distance rule uses these).</summary>
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
