using UnityEngine;

namespace Catan.View
{
    /// <summary>
    /// Offline preview harness: drop on an empty GameObject in an empty scene and press Play.
    /// Adds a camera and light if the scene has none, and an on-screen panel to resize and reroll the board.
    /// </summary>
    [RequireComponent(typeof(BoardView))]
    public sealed class BoardDemo : MonoBehaviour
    {
        BoardView _view;

        void Start()
        {
            _view = GetComponent<BoardView>();
            _view.buildOnStart = false;
            EnsureCamera();
            EnsureLight();
            NewBoard(true);
        }

        void NewBoard(bool reroll)
        {
            if (reroll) _view.seed = Random.Range(int.MinValue, int.MaxValue);
            _view.Rebuild();
            _view.FrameCamera(Camera.main);
        }

        static void EnsureCamera()
        {
            if (Camera.main != null) return;
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.backgroundColor = new Color(0.08f, 0.1f, 0.14f);
            cam.clearFlags = CameraClearFlags.SolidColor;
        }

        static void EnsureLight()
        {
            if (FindFirstObjectByType<Light>() != null) return;
            var go = new GameObject("Sun");
            go.AddComponent<Light>().type = LightType.Directional;
            go.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
        }

        void OnGUI()
        {
            if (_view == null) return;
            GUILayout.BeginArea(new Rect(12, 12, 230, 220), GUI.skin.box);
            GUILayout.Label($"Seed {_view.seed}");
            GUILayout.Label($"Radius {_view.radius}  ({_view.Board?.Tiles.Count ?? 0} tiles)");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Smaller")) Resize(-1);
            if (GUILayout.Button("Larger")) Resize(+1);
            GUILayout.EndHorizontal();

            bool ports = GUILayout.Toggle(_view.includePorts, "Ports");
            if (ports != _view.includePorts)
            {
                _view.includePorts = ports;
                NewBoard(false);
            }

            if (GUILayout.Button("New Board")) NewBoard(true);
            GUILayout.EndArea();
        }

        void Resize(int delta)
        {
            int r = Mathf.Clamp(_view.radius + delta, Catan.Core.BoardGenerator.MinRadius, Catan.Core.BoardGenerator.MaxRadius);
            if (r == _view.radius) return;
            _view.radius = r;
            NewBoard(false);
        }
    }
}
