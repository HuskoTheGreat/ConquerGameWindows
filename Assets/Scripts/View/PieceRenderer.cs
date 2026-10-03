using System.Collections.Generic;
using Catan.Core;
using UnityEngine;

namespace Catan.View
{
    /// <summary>A glowing marker the UI draws on a legal spot.</summary>
    public struct Highlight
    {
        public Vector3 Position;
        public Color Color;
        public float Size;
        public bool Flat; // a disc (for hexes) instead of a ball (for corners and edges)
    }

    /// <summary>Draws roads, settlements and cities from <see cref="Game"/> state, plus the legal-spot highlights.</summary>
    public sealed class PieceRenderer : MonoBehaviour
    {
        BoardView _view;
        readonly List<GameObject> _pieces = new List<GameObject>();
        readonly List<GameObject> _highlights = new List<GameObject>();
        readonly List<float> _highlightSizes = new List<float>();
        readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();

        public void Init(BoardView view) => _view = view;

        void OnDestroy()
        {
            ClearList(_pieces);
            ClearList(_highlights);
            foreach (Material m in _materials.Values) if (m != null) Destroy(m);
            _materials.Clear();
        }

        public void Sync(Game game)
        {
            ClearList(_pieces);
            float s = _view.hexSize;
            float y = _view.SurfaceY;

            foreach (KeyValuePair<Edge, int> road in game.RoadOwners)
            {
                Vector3[] ends = new Vector3[2];
                int i = 0;
                foreach (Vertex v in road.Key.Endpoints()) ends[i++] = _view.VertexToWorld(v, y + 0.04f);

                Vector3 dir = ends[1] - ends[0];
                GameObject r = Make(PrimitiveType.Cube, "Road", road.Value);
                r.transform.position = (ends[0] + ends[1]) * 0.5f;
                r.transform.rotation = Quaternion.LookRotation(dir);
                r.transform.localScale = new Vector3(s * 0.1f, s * 0.07f, dir.magnitude * 0.72f);
            }

            foreach (KeyValuePair<Vertex, Building> b in game.Buildings)
            {
                Vector3 p = _view.VertexToWorld(b.Key, y);
                if (b.Value.IsCity) MakeCity(p, s, b.Value.Owner);
                else MakeSettlement(p, s, b.Value.Owner);
            }
        }

        void MakeSettlement(Vector3 p, float s, int owner)
        {
            GameObject body = Make(PrimitiveType.Cube, "Settlement", owner);
            body.transform.position = p + Vector3.up * s * 0.1f;
            body.transform.localScale = new Vector3(s * 0.2f, s * 0.2f, s * 0.2f);

            GameObject roof = Make(PrimitiveType.Cube, "Roof", owner);
            roof.transform.position = p + Vector3.up * s * 0.27f;
            roof.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
            roof.transform.localScale = new Vector3(s * 0.15f, s * 0.15f, s * 0.22f);
        }

        void MakeCity(Vector3 p, float s, int owner)
        {
            GameObject body = Make(PrimitiveType.Cube, "City", owner);
            body.transform.position = p + Vector3.up * s * 0.12f;
            body.transform.localScale = new Vector3(s * 0.32f, s * 0.24f, s * 0.26f);

            GameObject tower = Make(PrimitiveType.Cube, "Tower", owner);
            tower.transform.position = p + new Vector3(s * 0.06f, s * 0.3f, 0f);
            tower.transform.localScale = new Vector3(s * 0.16f, s * 0.36f, s * 0.2f);
        }

        // ---- Highlights ----------------------------------------------------------------------------

        public void ShowHighlights(IReadOnlyList<Highlight> items)
        {
            ClearList(_highlights);
            _highlightSizes.Clear();
            foreach (Highlight h in items)
            {
                GameObject go = GameObject.CreatePrimitive(h.Flat ? PrimitiveType.Cylinder : PrimitiveType.Sphere);
                go.name = "Highlight";
                Destroy(go.GetComponent<Collider>());
                go.GetComponent<Renderer>().sharedMaterial = MaterialFor("hl" + ColorUtility.ToHtmlStringRGB(h.Color), h.Color);
                go.transform.position = h.Position;
                SetSize(go.transform, h.Size, h.Flat);
                _highlights.Add(go);
                _highlightSizes.Add(h.Size);
            }
        }

        /// <summary>Enlarges the highlight under the cursor (pass -1 for none).</summary>
        public void SetHover(int index, bool[] flat)
        {
            for (int i = 0; i < _highlights.Count; i++)
            {
                float k = i == index ? 1.5f : 1f;
                SetSize(_highlights[i].transform, _highlightSizes[i] * k, flat != null && i < flat.Length && flat[i]);
            }
        }

        static void SetSize(Transform t, float size, bool flat) =>
            t.localScale = flat ? new Vector3(size, 0.02f, size) : Vector3.one * size;

        // ---- Helpers -------------------------------------------------------------------------------

        GameObject Make(PrimitiveType type, string name, int owner)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = MaterialFor("p" + owner, BoardPalette.Player(owner));
            _pieces.Add(go);
            return go;
        }

        Material MaterialFor(string key, Color color)
        {
            if (!_materials.TryGetValue(key, out Material m))
            {
                m = BoardPalette.CreateMaterial(color);
                _materials[key] = m;
            }
            return m;
        }

        static void ClearList(List<GameObject> list)
        {
            foreach (GameObject go in list) if (go != null) Destroy(go);
            list.Clear();
        }
    }
}
