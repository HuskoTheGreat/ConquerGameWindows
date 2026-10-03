using System;
using System.Collections.Generic;
using System.Linq;
using Catan.Core;

namespace Catan.Client
{
    public enum Tool { None, Road, Settlement, City }

    public enum SpotKind { Vertex, Edge, Hex }

    /// <summary>A clickable place on the board, positioned in hex-layout plane coordinates (hex size 1).</summary>
    public sealed class Spot
    {
        public SpotKind Kind { get; init; }
        public Vertex Vertex { get; init; }
        public Edge Edge { get; init; }
        public Hex Hex { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
    }

    /// <summary>
    /// Local hot-seat game session. Owns the <see cref="Game"/> and everything the screen needs to be
    /// clickable, but no UI types: every action is a <see cref="Command"/> sent to the engine.
    /// </summary>
    public sealed class LocalGameController
    {
        readonly List<Spot> _spots = new List<Spot>();
        readonly List<string> _log = new List<string>();
        int _lastActor = -1;

        public Game Game { get; private set; }

        /// <summary>Goes up by one for every new game, so watchers can tell a fresh game from a change.</summary>
        public int GameNumber { get; private set; }
        public Tool Tool { get; private set; }
        public IReadOnlyList<Spot> Spots => _spots;
        public IReadOnlyList<string> Log => _log;
        public bool HideHands { get; private set; }

        /// <summary>True while the screen should hide hands and ask the players to pass the device.</summary>
        public bool HandoffPending { get; private set; }

        public string Toast { get; private set; }
        public event Action Changed;
        public event Action<string> ToastShown;

        // ---- Lifecycle -----------------------------------------------------------------------------

        public void NewGame(int players, int radius, int victoryPoints, bool hideHands, int? seed = null, IDice dice = null)
        {
            var rng = seed.HasValue ? new Random(seed.Value) : new Random();
            Game = new Game(new GameConfig
            {
                PlayerCount = players,
                Seed = rng.Next(),
                Board = new BoardConfig { Radius = radius, Seed = rng.Next() },
                Rules = new HouseRules { VictoryPoints = victoryPoints },
            }, dice);

            GameNumber++;
            _log.Clear();
            _log.Add("New game. Place your starting settlements.");
            Tool = Tool.None;
            HideHands = hideHands;
            HandoffPending = false;
            _lastActor = -1;
            Refresh();
        }

        /// <summary>Whose input the game is waiting for (the discarder during a discard, else the current player).</summary>
        public int Actor =>
            Game.Phase == Phase.Discard && Game.PendingDiscards.Count > 0 ? Game.PendingDiscards.Keys.Min() : Game.CurrentPlayer;

        public Player ActorPlayer => Game.Players[Actor];

        public void AcknowledgeHandoff()
        {
            HandoffPending = false;
            Changed?.Invoke();
        }

        void ShowToast(string message)
        {
            Toast = message;
            ToastShown?.Invoke(message);
            Changed?.Invoke();
        }

        // ---- Sending commands ----------------------------------------------------------------------

        /// <summary>Sends a command to the engine. Returns true if it was accepted.</summary>
        public bool Send(Command command)
        {
            ActionResult result = Game.Apply(command);
            if (!result.Ok)
            {
                ShowToast(result.Error);
                return false;
            }

            _log.AddRange(result.Events);
            if (_log.Count > 60) _log.RemoveRange(0, _log.Count - 60);
            if (command is BuildRoad || command is BuildSettlement || command is BuildCity) Tool = Tool.None;
            Toast = null;
            Refresh();
            return true;
        }

        public void SelectTool(Tool tool)
        {
            if (Tool == tool)
            {
                Tool = Tool.None;
            }
            else
            {
                ResourceSet cost = tool == Tool.Road ? Costs.Road : tool == Tool.Settlement ? Costs.Settlement : Costs.City;
                if (!Game.Players[Game.CurrentPlayer].Hand.Contains(cost))
                {
                    ShowToast("You can't afford that yet.");
                    return;
                }
                Tool = tool;
            }
            RebuildSpots();
            Changed?.Invoke();
        }

        public void ClickSpot(Spot spot)
        {
            int me = Game.CurrentPlayer;
            switch (Game.Phase)
            {
                case Phase.SetupSettlement: Send(new SetupSettlement(me, spot.Vertex)); break;
                case Phase.SetupRoad: Send(new SetupRoad(me, spot.Edge)); break;
                case Phase.RoadBuilding: Send(new BuildRoad(me, spot.Edge)); break;
                case Phase.MoveRobber: Send(new MoveRobber(me, spot.Hex)); break;
                case Phase.Main:
                    if (Tool == Tool.Road) Send(new BuildRoad(me, spot.Edge));
                    else if (Tool == Tool.Settlement) Send(new BuildSettlement(me, spot.Vertex));
                    else if (Tool == Tool.City) Send(new BuildCity(me, spot.Vertex));
                    break;
            }
        }

        // ---- Derived UI state ----------------------------------------------------------------------

        void Refresh()
        {
            RebuildSpots();

            int actor = Game.Phase == Phase.GameOver ? Game.Winner : Actor;
            if (HideHands && actor != _lastActor && Game.Phase != Phase.GameOver) HandoffPending = true;
            _lastActor = actor;
            Changed?.Invoke();
        }

        void RebuildSpots()
        {
            _spots.Clear();
            int me = Game.CurrentPlayer;

            switch (Game.Phase)
            {
                case Phase.SetupSettlement: AddVertices(Game.LegalSetupVertices()); break;
                case Phase.SetupRoad: AddEdges(Game.LegalSetupRoadEdges()); break;
                case Phase.RoadBuilding: AddEdges(Game.LegalRoadEdges(me)); break;
                case Phase.MoveRobber:
                    foreach (Hex h in Game.LegalRobberHexes())
                    {
                        var p = HexLayout.ToPlane(h);
                        _spots.Add(new Spot { Kind = SpotKind.Hex, Hex = h, X = p.X, Y = p.Y });
                    }
                    break;
                case Phase.Main:
                    if (Tool == Tool.Road) AddEdges(Game.LegalRoadEdges(me));
                    else if (Tool == Tool.Settlement) AddVertices(Game.LegalSettlementVertices(me));
                    else if (Tool == Tool.City) AddVertices(Game.LegalCityVertices(me));
                    break;
            }
        }

        void AddVertices(IEnumerable<Vertex> vertices)
        {
            foreach (Vertex v in vertices)
            {
                var p = HexLayout.ToPlane(v);
                _spots.Add(new Spot { Kind = SpotKind.Vertex, Vertex = v, X = p.X, Y = p.Y });
            }
        }

        void AddEdges(IEnumerable<Edge> edges)
        {
            foreach (Edge e in edges)
            {
                var p = HexLayout.ToPlane(e);
                _spots.Add(new Spot { Kind = SpotKind.Edge, Edge = e, X = p.X, Y = p.Y });
            }
        }

        public string Prompt()
        {
            switch (Game.Phase)
            {
                case Phase.SetupSettlement: return "Place a starting settlement on a glowing corner.";
                case Phase.SetupRoad: return "Place a road next to your new settlement.";
                case Phase.Roll: return "Roll the dice.";
                case Phase.Discard: return $"{Game.Players[Actor].Name} must discard {Game.PendingDiscards[Actor]} cards.";
                case Phase.MoveRobber: return "Move the robber: click a hex.";
                case Phase.Steal: return "Choose a player to steal a card from.";
                case Phase.RoadBuilding: return $"Road Building: place {Game.FreeRoadsLeft} free road(s).";
                case Phase.GameOver: return $"{Game.Players[Game.Winner].Name} wins!";
                default:
                    return Tool == Tool.None
                        ? "Build, trade, or end your turn."
                        : $"Click a glowing spot to build a {Tool.ToString().ToLower()}.";
            }
        }
    }
}
