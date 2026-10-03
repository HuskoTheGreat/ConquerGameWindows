using System.Collections.Generic;
using System.Linq;
using Catan.Core;
using UnityEngine;

namespace Catan.View
{
    /// <summary>
    /// Offline hot-seat game: players share one screen and take turns. Every action is a <see cref="Command"/>
    /// sent to the <see cref="Game"/>, which is exactly what a networked client will do later. Drop this on an
    /// empty GameObject in an empty scene and press Play.
    /// </summary>
    [RequireComponent(typeof(BoardView))]
    public sealed partial class HotSeatController : MonoBehaviour
    {
        public enum Tool { None, Road, Settlement, City }

        enum Mode { Setup, None, BankTrade, PlayerTrade, PlayCard, PickYearOfPlenty, PickMonopoly, Rules }

        struct Candidate
        {
            public Vertex Vertex;
            public Edge Edge;
            public Hex Hex;
            public Vector3 Position;
            public bool IsHex;
        }

        [Header("New game defaults")]
        [Range(2, 6)] public int playerCount = 3;
        [Range(1, 6)] public int radius = 2;
        [Range(3, 20)] public int victoryPoints = 10;
        public bool hideHandsBetweenTurns = true;

        Game _game;
        IGameLink _link;
        bool _boardShown;
        BoardView _view;
        PieceRenderer _pieces;

        Mode _mode = Mode.Setup;
        Tool _tool;

        readonly List<Candidate> _candidates = new List<Candidate>();
        readonly List<string> _log = new List<string>();
        int _hover = -1;
        bool[] _flat = new bool[0];

        string _toast;
        float _toastUntil;
        bool _handoff;
        int _lastActor = -1;

        void Awake()
        {
            _view = GetComponent<BoardView>();
            _view.buildOnStart = false;
            _pieces = gameObject.AddComponent<PieceRenderer>();
            _pieces.Init(_view);
        }

        void Start() => SceneBootstrap.EnsureCameraAndLight();

        // ---- State helpers -------------------------------------------------------------------------

        /// <summary>Whose input the game is waiting for (the discarder during a discard, else the current player).</summary>
        int Actor => Remote
            ? _link.Seat
            : _game.Phase == Phase.Discard && _game.PendingDiscards.Count > 0
                ? _game.PendingDiscards.Keys.Min()
                : _game.CurrentPlayer;

        /// <summary>True when playing over the network: this UI only submits commands and shows the host's view.</summary>
        bool Remote => _link != null;

        /// <summary>Whether this player can act right now (always true in hot-seat).</summary>
        bool HasTurn =>
            !Remote ||
            (_game != null &&
             (_game.Phase == Phase.Discard ? _game.PendingDiscards.ContainsKey(_link.Seat) : _game.CurrentPlayer == _link.Seat));

        Player ActorPlayer => _game.Players[Actor];

        static string HtmlColor(Color c) => ColorUtility.ToHtmlStringRGB(c);

        string Dot(int id) => $"<color=#{HtmlColor(BoardPalette.Player(id))}>●</color>";

        void Toast(string message)
        {
            _toast = message;
            _toastUntil = Time.realtimeSinceStartup + 3.5f;
        }

        void AddLog(string line)
        {
            _log.Add(line);
            if (_log.Count > 40) _log.RemoveAt(0);
        }

        // ---- Game lifecycle ------------------------------------------------------------------------

        void NewGame()
        {
            var rules = new HouseRules { VictoryPoints = victoryPoints };
            _game = new Game(new GameConfig
            {
                PlayerCount = playerCount,
                Seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue),
                Board = new BoardConfig { Radius = radius, Seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue) },
                Rules = rules,
            });

            _view.ShowBoard(_game.Board);
            _view.FrameCamera(Camera.main);
            _log.Clear();
            _tool = Tool.None;
            _mode = Mode.None;
            _handoff = false;
            _lastActor = -1;
            AddLog("New game. Place your starting settlements.");
            Refresh();
        }

        /// <summary>Switches this controller to network play. The game itself lives on the host.</summary>
        public void AttachLink(IGameLink link)
        {
            _link = link;
            _mode = Mode.None;
            hideHandsBetweenTurns = false;
            link.Updated += OnLinkUpdated;
            link.LogReceived += AddLog;
            link.ErrorReceived += Toast;
        }

        void OnLinkUpdated()
        {
            _game = _link.Mirror;
            if (_game == null) return;

            if (!_boardShown)
            {
                _view.ShowBoard(_game.Board);
                _view.FrameCamera(Camera.main);
                _boardShown = true;
                AddLog("The game has started.");
            }
            Refresh();
        }

        void OnDestroy()
        {
            if (_link == null) return;
            _link.Updated -= OnLinkUpdated;
            _link.LogReceived -= AddLog;
            _link.ErrorReceived -= Toast;
        }

        /// <summary>Sends a command to the engine (or, online, to the host). Returns true if it was accepted or sent.</summary>
        bool Send(Command command)
        {
            if (Remote)
            {
                _link.Submit(command);
                if (command is BuildRoad || command is BuildSettlement || command is BuildCity) _tool = Tool.None;
                return true;
            }

            ActionResult result = _game.Apply(command);
            if (!result.Ok)
            {
                Toast(result.Error);
                return false;
            }

            foreach (string e in result.Events) AddLog(e);
            if (command is BuildRoad || command is BuildSettlement || command is BuildCity) _tool = Tool.None;
            Refresh();
            return true;
        }

        /// <summary>Re-syncs everything the player sees from the game state.</summary>
        void Refresh()
        {
            _pieces.Sync(_game);
            _view.MoveRobber(_game.RobberHex);
            RebuildCandidates();

            int actor = _game.Phase == Phase.GameOver ? _game.Winner : Actor;
            if (!Remote && hideHandsBetweenTurns && actor != _lastActor && _game.Phase != Phase.GameOver) _handoff = true;
            _lastActor = actor;
        }

        // ---- Clickable spots -----------------------------------------------------------------------

        void RebuildCandidates()
        {
            _candidates.Clear();
            int me = _game.CurrentPlayer;
            float y = _view.SurfaceY;

            switch (HasTurn ? _game.Phase : Phase.GameOver)
            {
                case Phase.SetupSettlement:
                    AddVertices(_game.LegalSetupVertices(), y);
                    break;
                case Phase.SetupRoad:
                    AddEdges(_game.LegalSetupRoadEdges(), y);
                    break;
                case Phase.RoadBuilding:
                    AddEdges(_game.LegalRoadEdges(me), y);
                    break;
                case Phase.MoveRobber:
                    foreach (Hex h in _game.LegalRobberHexes())
                        _candidates.Add(new Candidate { Hex = h, IsHex = true, Position = _view.HexToWorld(h, y + 0.02f) });
                    break;
                case Phase.Main:
                    if (_tool == Tool.Road) AddEdges(_game.LegalRoadEdges(me), y);
                    else if (_tool == Tool.Settlement) AddVertices(_game.LegalSettlementVertices(me), y);
                    else if (_tool == Tool.City) AddVertices(_game.LegalCityVertices(me), y);
                    break;
            }

            var lights = new List<Highlight>(_candidates.Count);
            _flat = new bool[_candidates.Count];
            for (int i = 0; i < _candidates.Count; i++)
            {
                Candidate c = _candidates[i];
                _flat[i] = c.IsHex;
                lights.Add(new Highlight
                {
                    Position = c.Position,
                    Color = c.IsHex ? new Color(1f, 1f, 1f) : new Color(1f, 0.92f, 0.2f),
                    Size = c.IsHex ? _view.hexSize * 1.2f : _view.hexSize * 0.2f,
                    Flat = c.IsHex,
                });
            }
            _pieces.ShowHighlights(lights);
            _hover = -1;
        }

        void AddVertices(IEnumerable<Vertex> vertices, float y)
        {
            foreach (Vertex v in vertices)
                _candidates.Add(new Candidate { Vertex = v, Position = _view.VertexToWorld(v, y + 0.04f) });
        }

        void AddEdges(IEnumerable<Edge> edges, float y)
        {
            foreach (Edge e in edges)
                _candidates.Add(new Candidate { Edge = e, Position = _view.EdgeToWorld(e, y + 0.04f) });
        }

        /// <summary>Finds the candidate nearest the cursor on the board plane, if any is close enough.</summary>
        int PickCandidate(Vector2 guiMouse)
        {
            Camera cam = Camera.main;
            if (cam == null || _candidates.Count == 0) return -1;

            Ray ray = cam.ScreenPointToRay(new Vector3(guiMouse.x, Screen.height - guiMouse.y, 0f));
            var plane = new Plane(Vector3.up, new Vector3(0f, _view.transform.position.y + _view.SurfaceY, 0f));
            if (!plane.Raycast(ray, out float t)) return -1;
            Vector3 hit = ray.GetPoint(t);

            int best = -1;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < _candidates.Count; i++)
            {
                Vector3 d = _candidates[i].Position - hit;
                d.y = 0f;
                float limit = _candidates[i].IsHex ? _view.hexSize * 0.9f : _view.hexSize * 0.4f;
                float sqr = d.sqrMagnitude;
                if (sqr <= limit * limit && sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }
            return best;
        }

        void ClickCandidate(Candidate c)
        {
            int me = Actor;
            switch (_game.Phase)
            {
                case Phase.SetupSettlement: Send(new SetupSettlement(me, c.Vertex)); break;
                case Phase.SetupRoad: Send(new SetupRoad(me, c.Edge)); break;
                case Phase.RoadBuilding: Send(new BuildRoad(me, c.Edge)); break;
                case Phase.MoveRobber: Send(new MoveRobber(me, c.Hex)); break;
                case Phase.Main:
                    if (_tool == Tool.Road) Send(new BuildRoad(me, c.Edge));
                    else if (_tool == Tool.Settlement) Send(new BuildSettlement(me, c.Vertex));
                    else if (_tool == Tool.City) Send(new BuildCity(me, c.Vertex));
                    break;
            }
        }

        void SelectTool(Tool tool, ResourceSet cost)
        {
            if (_tool == tool)
            {
                _tool = Tool.None;
            }
            else if (!_game.Players[_game.CurrentPlayer].Hand.Contains(cost))
            {
                Toast("You can't afford that yet.");
                return;
            }
            else
            {
                _tool = tool;
            }
            RebuildCandidates();
        }

        string Prompt()
        {
            switch (_game.Phase)
            {
                case Phase.SetupSettlement: return "Place a starting settlement on a glowing corner.";
                case Phase.SetupRoad: return "Place a road next to your new settlement.";
                case Phase.Roll: return "Roll the dice.";
                case Phase.Discard: return $"{_game.Players[Actor].Name} must discard {_game.PendingDiscards[Actor]} cards.";
                case Phase.MoveRobber: return "Move the robber: click a hex.";
                case Phase.Steal: return "Choose a player to steal a card from.";
                case Phase.RoadBuilding: return $"Road Building: place {_game.FreeRoadsLeft} free road(s).";
                case Phase.GameOver: return $"{_game.Players[_game.Winner].Name} wins!";
                default:
                    return _tool == Tool.None
                        ? "Build, trade, or end your turn."
                        : $"Click a glowing spot to build a {_tool.ToString().ToLower()}.";
            }
        }
    }
}
