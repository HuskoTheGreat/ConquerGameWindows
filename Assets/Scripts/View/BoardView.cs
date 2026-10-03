using System.Collections.Generic;
using Catan.Core;
using UnityEngine;

namespace Catan.View
{
    /// <summary>
    /// Renders a <see cref="Board"/> as 3D objects. Everything is a child of this transform and is rebuilt
    /// from data on <see cref="Rebuild"/>; the view never holds game state of its own.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        [Header("Board")]
        [Range(BoardGenerator.MinRadius, BoardGenerator.MaxRadius)] public int radius = 2;
        public int seed;
        public bool includePorts = true;
        public bool buildOnStart = true;

        [Header("Look")]
        public float hexSize = 1f;
        [Range(0f, 0.2f)] public float gap = 0.04f;
        public float tileHeight = 0.25f;

        [Header("Debug")]
        public bool drawVertexGizmos;

        public Board Board { get; private set; }

        readonly Dictionary<Hex, Transform> _tileViews = new Dictionary<Hex, Transform>();
        readonly Dictionary<Resource, Material> _materials = new Dictionary<Resource, Material>();
        readonly List<Object> _owned = new List<Object>();
        Transform _robber;
        Font _font;

        void Start()
        {
            if (buildOnStart) Rebuild();
        }

        void OnDestroy() => Clear();

        // ---- Data -> world mapping (used by later steps to place pieces and pick) -------------------

        public Vector3 HexToWorld(Hex h, float y = 0f) => ToWorld(HexLayout.ToPlane(h, hexSize), y);
        public Vector3 VertexToWorld(Vertex v, float y = 0f) => ToWorld(HexLayout.ToPlane(v, hexSize), y);
        public Vector3 EdgeToWorld(Edge e, float y = 0f) => ToWorld(HexLayout.ToPlane(e, hexSize), y);

        /// <summary>The hex under a world-space point on the board plane, or null if off the land.</summary>
        public Hex? WorldToHex(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            Hex h = HexLayout.FromPlane(local.x, -local.z, hexSize);
            return Board != null && Board.IsLand(h) ? h : (Hex?)null;
        }

        Vector3 ToLocal((float X, float Y) p, float y) => new Vector3(p.X, y, -p.Y);
        Vector3 ToWorld((float X, float Y) p, float y) => transform.TransformPoint(ToLocal(p, y));

        // ---- Building -------------------------------------------------------------------------------

        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            Clear();
            Board = BoardGenerator.Generate(new BoardConfig { Radius = radius, Seed = seed, IncludePorts = includePorts });

            Mesh prism = Own(HexMeshFactory.CreatePrism(hexSize, gap, tileHeight));
            BuildSea();
            foreach (Tile tile in Board.Tiles) BuildTile(tile, prism);
            foreach (Port port in Board.Ports) BuildPort(port);
            BuildRobber(Board.RobberStart);
        }

        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Kill(transform.GetChild(i).gameObject);
            foreach (Object o in _owned) if (o != null) Kill(o);
            _owned.Clear();
            _tileViews.Clear();
            _materials.Clear();
            _robber = null;
            Board = null;
        }

        void BuildSea()
        {
            GameObject sea = Primitive(PrimitiveType.Cylinder, "Sea", transform, BoardPalette.Sea);
            float diameter = 2f * (radius + 1.6f) * hexSize * 1.75f;
            sea.transform.localPosition = new Vector3(0f, -0.07f, 0f);
            sea.transform.localScale = new Vector3(diameter, 0.05f, diameter);
        }

        void BuildTile(Tile tile, Mesh prism)
        {
            var go = new GameObject($"Tile {tile.Hex}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = ToLocal(HexLayout.ToPlane(tile.Hex, hexSize), 0f);
            go.AddComponent<MeshFilter>().sharedMesh = prism;
            go.AddComponent<MeshRenderer>().sharedMaterial = MaterialFor(tile.Resource);
            _tileViews[tile.Hex] = go.transform;

            if (tile.IsDesert) return;

            float d = hexSize * 0.62f;
            GameObject disc = Primitive(PrimitiveType.Cylinder, "Token", go.transform, BoardPalette.Token);
            disc.transform.localPosition = new Vector3(0f, tileHeight + 0.02f, 0f);
            disc.transform.localScale = new Vector3(d, 0.02f, d);

            Color ink = tile.Number == 6 || tile.Number == 8 ? BoardPalette.TokenHotText : BoardPalette.TokenText;
            float top = tileHeight + 0.05f;
            Label(go.transform, tile.Number.ToString(), new Vector3(0f, top, hexSize * 0.05f), hexSize * 0.17f, ink);
            Label(go.transform, new string('•', tile.Pips), new Vector3(0f, top, -hexSize * 0.2f), hexSize * 0.08f, ink);
        }

        void BuildPort(Port port)
        {
            // Sit the marker in the sea hex just outside the port's edge.
            Hex land = Board.IsLand(port.Edge.A) ? port.Edge.A : port.Edge.B;
            var landPos = HexLayout.ToPlane(land, hexSize);
            var edgePos = HexLayout.ToPlane(port.Edge, hexSize);
            var pos = (X: landPos.X + (edgePos.X - landPos.X) * 1.7f, Y: landPos.Y + (edgePos.Y - landPos.Y) * 1.7f);

            Color color = port.IsGeneric ? BoardPalette.GenericPort : BoardPalette.For(port.Resource);

            GameObject dock = Primitive(PrimitiveType.Cylinder, $"Port {port.Ratio}:1", transform, color);
            dock.transform.localPosition = ToLocal(pos, 0f);
            dock.transform.localScale = new Vector3(hexSize * 0.5f, 0.06f, hexSize * 0.5f);

            string text = port.IsGeneric ? "3:1" : "2:1\n" + BoardPalette.ShortName(port.Resource);
            Label(dock.transform.parent, text, ToLocal(pos, 0.12f), hexSize * 0.12f, BoardPalette.TokenText);

            // Pegs on the two corners the port serves.
            foreach (Vertex v in port.Edge.Endpoints())
            {
                GameObject peg = Primitive(PrimitiveType.Sphere, "Port Corner", transform, color);
                peg.transform.localPosition = ToLocal(HexLayout.ToPlane(v, hexSize), 0.05f);
                peg.transform.localScale = Vector3.one * hexSize * 0.14f;
            }
        }

        void BuildRobber(Hex at)
        {
            GameObject robber = Primitive(PrimitiveType.Capsule, "Robber", transform, BoardPalette.Robber);
            robber.transform.localScale = new Vector3(hexSize * 0.3f, hexSize * 0.35f, hexSize * 0.3f);
            robber.transform.localPosition = ToLocal(HexLayout.ToPlane(at, hexSize), tileHeight + hexSize * 0.35f);
            _robber = robber.transform;
        }

        /// <summary>Slides the robber to a hex (the rules step will call this).</summary>
        public void MoveRobber(Hex to)
        {
            if (_robber == null) return;
            _robber.localPosition = ToLocal(HexLayout.ToPlane(to, hexSize), tileHeight + hexSize * 0.35f);
        }

        /// <summary>Positions the camera to see the whole board from above at a slight tilt.</summary>
        public void FrameCamera(Camera cam)
        {
            if (cam == null) return;
            float extent = (radius + 1.6f) * hexSize * 1.75f;
            float dist = extent / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
            cam.transform.position = transform.position + new Vector3(0f, dist * 0.86f, -dist * 0.5f);
            cam.transform.LookAt(transform.position);
        }

        // ---- Helpers --------------------------------------------------------------------------------

        Material MaterialFor(Resource r)
        {
            if (!_materials.TryGetValue(r, out Material m))
            {
                m = Own(BoardPalette.CreateMaterial(BoardPalette.For(r)));
                _materials[r] = m;
            }
            return m;
        }

        GameObject Primitive(PrimitiveType type, string name, Transform parent, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Kill(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.GetComponent<Renderer>().sharedMaterial = Own(BoardPalette.CreateMaterial(color));
            return go;
        }

        /// <summary>Flat text lying on the board, readable from above with its top toward +Z.</summary>
        void Label(Transform parent, string text, Vector3 localPos, float charSize, Color color)
        {
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var tm = go.AddComponent<TextMesh>();
            tm.font = _font;
            tm.text = text;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            tm.characterSize = charSize / 8f;
            tm.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = _font.material;
        }

        T Own<T>(T obj) where T : Object
        {
            _owned.Add(obj);
            return obj;
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        void OnDrawGizmosSelected()
        {
            if (!drawVertexGizmos || Board == null) return;
            Gizmos.color = Color.yellow;
            foreach (Vertex v in Board.Vertices) Gizmos.DrawSphere(VertexToWorld(v, tileHeight), hexSize * 0.05f);
            Gizmos.color = Color.cyan;
            foreach (Edge e in Board.Edges) Gizmos.DrawWireSphere(EdgeToWorld(e, tileHeight), hexSize * 0.04f);
        }
    }
}
